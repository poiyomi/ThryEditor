// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Thry.ThryEditor
{
    /// <summary>
    /// A lighter lock for use while editing. It makes a copy of the material's shader without the //ifex sections
    /// that are switched off, and with the material's switch-like values (toggles, enums) and texture presence
    /// written in as constants, the way the full lock does. Sliders, colors, vectors and texture assignments stay
    /// live, and so does any switch or texture slot the user has changed on that material this session, so a
    /// sub-option only costs a new shader the first time it is touched. The whole Properties block is kept, so the
    /// inspector works as on the original shader.
    ///
    /// Section shaders only exist in memory. Importing a shader file makes Unity re-parse every loaded shader,
    /// which costs close to a second on its own, while ShaderUtil.CreateShaderAsset takes under a tenth of that.
    /// Materials are never saved with a section shader: SectionLockSaveGuard puts the original back while a
    /// material is written, so the .mat on disk keeps pointing at the original shader. Builds, uploads and play mode
    /// revert every section-locked material first.
    /// </summary>
    public static class SectionLock
    {
        /// <summary>Name prefix of every section shader. Followed by the original shader's GUID and the hash.</summary>
        public const string ShaderPrefix = "Hidden/SectionLocked/";

        /// <summary>
        /// Holds the original shader's GUID while a material is on a section shader. The save guard removes it
        /// before saving, so it only reaches disk when something writes the material without going through
        /// AssetDatabase.SaveAssets (AssetDatabase.SaveAssetIfDirty and CreateAsset don't call the guard). Such a
        /// file has lost its shader, and the tag is what the repair uses to put the original back.
        /// </summary>
        public const string TAG_SECTION_SOURCE = "thry_section_source";

        // Bump whenever the generated text changes for the same inputs.
        const int GeneratorVersion = 10;

        static readonly Regex s_includeRegex = new Regex(@"^(\s*#include(?:_with_pragmas)?\s*"")([^""]+)("".*)$", RegexOptions.Compiled);
        static readonly Regex s_shaderNameRegex = new Regex(@"^(\s*Shader\s*"")([^""]*)("".*)$", RegexOptions.Compiled);

        // Declarations of shader variables, e.g. "half _AlphaToCoverage;", "Texture2D<float4> _SquishMask;" and
        // "UNITY_DECLARE_TEX2D_NOSAMPLER(_ToonRamp);". Group 1 is the type where there is one, group 2 the name.
        static readonly Regex s_variableDeclarationRegex = new Regex(
            @"^\s*(?:uniform\s+)?((?:float|half|fixed|int|uint|bool|min16float|min16int)(?:[1-4](?:x[1-4])?)?|Texture2D|Texture2DArray|Texture3D|TextureCube|TextureCubeArray|sampler2D|sampler3D|samplerCUBE|SamplerState)(?:\s*<[^>]*>)?\s+(\w+)\s*;",
            RegexOptions.Compiled);
        static readonly Regex s_macroDeclarationRegex = new Regex(
            @"^\s*(?:UNITY_DECLARE_\w+|TEXTURE2D\w*|TEXTURE3D\w*|TEXTURECUBE\w*|SAMPLER)\s*\(\s*(\w+)\s*[,)]",
            RegexOptions.Compiled);
        // An index expression: "[...]". Group 1 is what's inside.
        static readonly Regex s_indexRegex = new Regex(@"\[([^\[\]]*)\]", RegexOptions.Compiled);
        // "#define NAME" or "#define NAME(a, b)". Group 1 is the name, group 2 the parameter list.
        static readonly Regex s_defineRegex = new Regex(@"^#\s*define\s+(\w+)(?:\(([^)]*)\))?", RegexOptions.Compiled);
        // A reference to a property: an identifier starting with '_' that isn't a member access.
        static readonly Regex s_propertyReferenceRegex = new Regex(@"(?<![\w\.])_[A-Za-z0-9_]+\b", RegexOptions.Compiled);

        // Attributes that make a Float or Int property a switch: a value you pick rather than drag.
        static readonly string[] s_switchAttributes = { "Toggle", "ToggleUI", "ThryToggle", "ThryToggleUI", "Enum", "KeywordEnum", "ThryWideEnum" };

        public enum Result
        {
            /// <summary>The material can't be section-locked (locked, not a Thry optimizer shader...).</summary>
            NotApplicable,
            /// <summary>The material already uses the right section shader.</summary>
            Unchanged,
            /// <summary>A section shader made earlier this session was assigned.</summary>
            Reused,
            /// <summary>A new section shader was generated and assigned.</summary>
            Generated,
            Failed
        }

        /// <summary>Durations of the last Apply, for diagnostics.</summary>
        public struct Timings
        {
            public double KeyMs;
            public double GenerateMs;
            public double CreateMs;
            public double SwapMs;
            public int GeneratedBytes;
            public int SourceBytes;
            public int RemovedBlocks;
            public int BakedValues;
        }

        public static Timings LastTimings { get; private set; }

        #region Source analysis

        /// <summary>
        /// Structure of an original shader file, read once per version of the file. Holds the lines, the position
        /// of every //ifex block outside the Properties block and what can be baked.
        /// </summary>
        class SourceInfo
        {
            public string AssetPath;
            public string Guid;
            public string DependencyHash;
            public DateTime WriteTime;
            // Hash of the text that was actually read, which the generated text depends on.
            public string TextHash;
            public string[] Lines;
            public int SourceBytes;

            // What to do with each line when nothing around it is removed.
            public LineKind[] Kinds;
            // Lines that stay even inside a removed block: declarations of material property variables and
            // includes. The full lock can drop them because it replaces every use with the value. Here code outside
            // the block (e.g. "_AlphaToCoverage < 0.5") may still use the variable. An unused one costs nothing.
            public bool[] KeepWhenRemoved;
            // #if/#ifdef/#ifndef/#elif/#else/#endif lines. Declarations kept from a removed block keep the
            // conditions around them, since the source declares some variables differently per keyword.
            public ConditionalKind[] Conditionals;
            // The whole directive of a conditional that continues over several lines with '\'.
            public Dictionary<int, string> ConditionalText = new Dictionary<int, string>();
            // Which CGPROGRAM/HLSLPROGRAM/CGINCLUDE block each line is in, -1 outside of one.
            public int[] ProgramBlock;
            // CGPROGRAM/HLSLPROGRAM lines, after which the defines for baked texture presence go, like in the full lock.
            public bool[] IsProgramStart;
            // Program lines whose property references can be replaced by values: not declarations, not directives
            // other than #define.
            public bool[] Bakeable;
            // Rewritten text for lines of kind ShaderName and Include.
            public Dictionary<int, string> Rewritten = new Dictionary<int, string>();

            // One entry per removable //ifex block outside Properties, in file order.
            public int[] IfexLine;
            public int[] EndexLine;
            public int[] ConditionIndex;
            // Distinct condition texts, indexed by ConditionIndex.
            public string[] Conditions;

            // Properties that can be baked, sorted, with what reading and writing their value needs: the property ID,
            // whether it is an Int property, and whether the code declares it as an integer (then it's written as an
            // integer literal, otherwise as a float one). These are the switch-like properties the program code uses,
            // plus anything used in code that only exists with OPTIMIZER_ENABLED, which expects constants there
            // (e.g. Fur's [instance(_FurLayerCount + 1)]). Those are always baked (SwitchMustBake). Properties the
            // full lock leaves live ([DoNotLock]) are only baked when they must be (SwitchExempt).
            public string[] SwitchProperties;
            public int[] SwitchIds;
            public bool[] SwitchIsInt;
            public bool[] SwitchDeclaredInt;
            public bool[] SwitchMustBake;
            public bool[] SwitchExempt;
            // Used as an index ("x[_Channel]"). A constant index outside the vector fails to compile even in code that
            // never runs, so those are only baked for values 0-3.
            public bool[] SwitchIndexes;
            public Dictionary<string, int> SwitchIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, string> DeclaredTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            // Textures whose PROP_ define the code checks and that aren't shader keywords.
            public string[] PresenceTextures;

            // "#pragma shader_feature..." lines in program blocks, in file order, with the keywords each declares. A baked
            // section shader drops them and #defines the enabled keywords instead, as the full lock does. It then has
            // no material keywords, so switching a keyword never makes the shader the material is leaving compile a
            // variant for it.
            public bool[] IsFeaturePragma;
            public int[] FeaturePragmaLines;
            public string[][] FeaturePragmaKeywords;
            public HashSet<string> FeatureKeywords = new HashSet<string>(StringComparer.Ordinal);
            // Blocks that are CGINCLUDE/HLSLINCLUDE, whose pragmas apply to every program.
            public HashSet<int> IncludeBlocks = new HashSet<int>();

            // The original's property names and Unity's drawer handlers for them, filled by ShareDrawerHandlers.
            public string[] PropertyNames;
            public object[] PropertyHandlers;

            // Problems found while reading, logged on the main thread by GetSourceInfo.
            public List<(LogType type, string message)> Messages = new List<(LogType, string)>();
        }

        enum LineKind : byte { Keep, Drop, ShaderName, Include }

        enum ConditionalKind : byte { None, If, Branch, EndIf }

        static ConditionalKind GetConditionalKind(string trimmed)
        {
            if (!trimmed.StartsWith("#", StringComparison.Ordinal)) return ConditionalKind.None;
            string directive = trimmed.Substring(1).TrimStart();
            if (directive.StartsWith("if", StringComparison.Ordinal)) return ConditionalKind.If; // #if, #ifdef, #ifndef
            if (directive.StartsWith("elif", StringComparison.Ordinal) || directive.StartsWith("else", StringComparison.Ordinal)) return ConditionalKind.Branch;
            if (directive.StartsWith("endif", StringComparison.Ordinal)) return ConditionalKind.EndIf;
            return ConditionalKind.None;
        }

        static readonly Dictionary<string, SourceInfo> s_sources = new Dictionary<string, SourceInfo>();

        // Originals being read on another thread, by asset path, with the file version they were started for.
        static readonly Dictionary<string, (string dependencyHash, DateTime writeTime, Task<SourceInfo> task)> s_preparing =
            new Dictionary<string, (string, DateTime, Task<SourceInfo>)>();

        static SourceInfo GetSourceInfo(Shader original)
        {
            if (!GetSourceVersion(original, out string assetPath, out string dependencyHash, out DateTime writeTime)) return null;
            if (s_sources.TryGetValue(assetPath, out SourceInfo cached) && cached.DependencyHash == dependencyHash && cached.WriteTime == writeTime)
                return cached;

            SourceInfo info;
            try
            {
                // Waits for Prepare's read if it's still running: it has less left to do than a new one.
                if (s_preparing.TryGetValue(assetPath, out var preparing) && preparing.dependencyHash == dependencyHash && preparing.writeTime == writeTime)
                    info = preparing.task.GetAwaiter().GetResult();
                else
                    info = ReadAndAnalyze(assetPath, GetShaderFacts(original));
            }
            catch (Exception e)
            {
                ThryLogger.LogErr("Could not read shader " + assetPath + " for section locking: " + e.Message);
                return null;
            }
            finally
            {
                s_preparing.Remove(assetPath);
            }

            info.Guid = AssetDatabase.AssetPathToGUID(assetPath);
            info.DependencyHash = dependencyHash;
            info.WriteTime = writeTime;
            foreach ((LogType type, string message) in info.Messages)
            {
                if (type == LogType.Error) ThryLogger.LogErr(message);
                else if (type == LogType.Warning) ThryLogger.LogWarn(message);
                else ThryLogger.LogDetail("SectionLock", message);
            }
            info.Messages.Clear();
            s_sources[assetPath] = info;
            return info;
        }

        /// <summary>
        /// Starts reading the original shader on another thread, if that hasn't happened yet, so the first switch on
        /// a material doesn't wait for it. Reading Poiyomi Pro takes about half a second.
        /// </summary>
        public static void Prepare(Shader original)
        {
            if (original == null || IsSectionShader(original)) return;
            if (!GetSourceVersion(original, out string assetPath, out string dependencyHash, out DateTime writeTime)) return;
            if (s_sources.TryGetValue(assetPath, out SourceInfo cached) && cached.DependencyHash == dependencyHash && cached.WriteTime == writeTime) return;
            if (s_preparing.TryGetValue(assetPath, out var preparing) && preparing.dependencyHash == dependencyHash && preparing.writeTime == writeTime) return;

            ShaderFacts facts = GetShaderFacts(original);
            s_preparing[assetPath] = (dependencyHash, writeTime, Task.Run(() => ReadAndAnalyze(assetPath, facts)));
        }

        /// <summary>False while Prepare is still reading the material's original shader.</summary>
        public static bool IsPrepared(Material material)
        {
            Shader source = GetSourceShader(material);
            if (source == null || !GetSourceVersion(source, out string assetPath, out string dependencyHash, out DateTime writeTime)) return true;
            if (s_sources.TryGetValue(assetPath, out SourceInfo cached) && cached.DependencyHash == dependencyHash && cached.WriteTime == writeTime) return true;
            // The file changed since it was read, e.g. the shader was regenerated: read the new version in the background too.
            if (!s_preparing.TryGetValue(assetPath, out var preparing) || preparing.dependencyHash != dependencyHash || preparing.writeTime != writeTime)
            {
                if (!ShaderOptimizer.IsShaderUsingThryOptimizer(source)) return true;
                Prepare(source);
                if (!s_preparing.TryGetValue(assetPath, out preparing)) return true;
            }
            return preparing.task.IsCompleted;
        }

        static bool GetSourceVersion(Shader original, out string assetPath, out string dependencyHash, out DateTime writeTime)
        {
            assetPath = AssetDatabase.GetAssetPath(original);
            dependencyHash = null;
            writeTime = DateTime.MinValue;
            if (string.IsNullOrEmpty(assetPath)) return false;
            dependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath)) writeTime = File.GetLastWriteTimeUtc(fullPath);
            return true;
        }

        // Runs on any thread.
        static SourceInfo ReadAndAnalyze(string assetPath, ShaderFacts facts)
        {
            string text = File.ReadAllText(Path.GetFullPath(assetPath));
            SourceInfo info = Analyze(assetPath, text, facts);
            using (MD5 md5 = MD5.Create())
                info.TextHash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(assetPath + "\n" + text))).Replace("-", "");
            return info;
        }

        /// <summary>What Analyze needs from the Shader object, read on the main thread so the file can be read on another.</summary>
        class ShaderFacts
        {
            // Names a shader variable can have when Unity binds it from a material property.
            public HashSet<string> PropertyVariables = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Keywords;
            public string[] Names;
            public ShaderPropertyType[] Types;
            public int[] Ids;
            public bool[] IsSwitch;
            public bool[] Exempt;
            // First index of each property name, like Shader.FindPropertyIndex.
            public Dictionary<string, int> Index = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        static ShaderFacts GetShaderFacts(Shader shader)
        {
            int count = shader.GetPropertyCount();
            ShaderFacts facts = new ShaderFacts
            {
                Keywords = new HashSet<string>(shader.keywordSpace.keywordNames, StringComparer.Ordinal),
                Names = new string[count],
                Types = new ShaderPropertyType[count],
                Ids = new int[count],
                IsSwitch = new bool[count],
                Exempt = new bool[count],
            };
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                ShaderPropertyType type = shader.GetPropertyType(i);
                facts.Names[i] = name;
                facts.Types[i] = type;
                facts.Ids[i] = Shader.PropertyToID(name);
                facts.IsSwitch[i] = IsSwitchProperty(shader, i);
                facts.Exempt[i] = IsExemptFromLocking(shader, i);
                if (!facts.Index.ContainsKey(name)) facts.Index[name] = i;
                facts.PropertyVariables.Add(name);
                if (type == ShaderPropertyType.Texture)
                {
                    facts.PropertyVariables.Add(name + "_ST");
                    facts.PropertyVariables.Add(name + "_TexelSize");
                    facts.PropertyVariables.Add(name + "_HDR");
                    facts.PropertyVariables.Add("sampler" + name);
                }
            }
            return facts;
        }

        static bool IsSwitchProperty(Shader shader, int index)
        {
            ShaderPropertyType type = shader.GetPropertyType(index);
            if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Int) return false;
            foreach (string attribute in shader.GetPropertyAttributes(index))
            {
                foreach (string name in s_switchAttributes)
                {
                    if (attribute.StartsWith(name, StringComparison.Ordinal)
                        && (attribute.Length == name.Length || attribute[name.Length] == '(')) return true;
                }
            }
            return false;
        }

        // The same test as ShaderOptimizer.IsPropertyExcemptFromLocking, from the shader instead of a MaterialProperty.
        static bool IsExemptFromLocking(Shader shader, int index)
        {
            if (Array.IndexOf(shader.GetPropertyAttributes(index), "DoNotLock") >= 0) return true;
            if (shader.GetPropertyDescription(index).EndsWith(ShaderOptimizer.ExemptFromLockingSuffix, StringComparison.Ordinal)) return true;
            return shader.GetPropertyType(index) != ShaderPropertyType.Texture
                && (shader.GetPropertyFlags(index) & ShaderPropertyFlags.NonModifiableTextureData) != 0;
        }

        /// <summary>
        /// Keeps track of #if branches that only exist when OPTIMIZER_ENABLED is defined: the first branch of an #if
        /// whose condition requires it (#ifdef, or "... &amp;&amp; defined(OPTIMIZER_ENABLED)"), and the #else of one
        /// whose condition holds without it (#ifndef, or "... || !defined(OPTIMIZER_ENABLED)"). #elif branches are
        /// never counted, which only means fewer values are forced to be baked.
        /// </summary>
        static void TrackOptimizerBranches(List<(bool ifNeeds, bool elseNeeds, bool inElse)> branches, ConditionalKind kind, string directive)
        {
            string trimmed = directive.TrimStart();
            string body = trimmed.Substring(1).TrimStart();
            if (kind == ConditionalKind.EndIf)
            {
                if (branches.Count > 0) branches.RemoveAt(branches.Count - 1);
                return;
            }
            if (kind == ConditionalKind.Branch)
            {
                if (branches.Count == 0) return;
                bool isElse = body.StartsWith("else", StringComparison.Ordinal);
                var top = branches[branches.Count - 1];
                branches[branches.Count - 1] = isElse ? (top.ifNeeds, top.elseNeeds, true) : (false, false, true);
                return;
            }

            string optimizer = ShaderOptimizer.OptimizerEnabledKeyword;
            bool ifNeeds = false, elseNeeds = false;
            if (body.StartsWith("ifdef", StringComparison.Ordinal)) ifNeeds = body.Substring(5).Trim() == optimizer;
            else if (body.StartsWith("ifndef", StringComparison.Ordinal)) elseNeeds = body.Substring(6).Trim() == optimizer;
            else
            {
                string condition = Regex.Replace(body.Substring(2).Replace("\\\n", " "), @"\s+", "");
                int comment = condition.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) condition = condition.Substring(0, comment);
                List<string> terms = SplitTopLevel(condition, out bool hasAnd, out bool hasOr);
                foreach (string term in terms)
                {
                    string unwrapped = Unwrap(term);
                    if (!hasOr && unwrapped == "defined(" + optimizer + ")") ifNeeds = true;
                    if (!hasAnd && unwrapped == "!defined(" + optimizer + ")") elseNeeds = true;
                }
            }
            branches.Add((ifNeeds, elseNeeds, false));
        }

        // Splits a condition at its top-level && and || operators.
        static List<string> SplitTopLevel(string condition, out bool hasAnd, out bool hasOr)
        {
            hasAnd = hasOr = false;
            List<string> terms = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < condition.Length; i++)
            {
                char c = condition[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (depth == 0 && i + 1 < condition.Length && (c == '&' || c == '|') && condition[i + 1] == c)
                {
                    if (c == '&') hasAnd = true; else hasOr = true;
                    terms.Add(condition.Substring(start, i - start));
                    start = i + 2;
                    i++;
                }
            }
            terms.Add(condition.Substring(start));
            return terms;
        }

        static string Unwrap(string term)
        {
            while (term.Length > 1 && term[0] == '(' && term[term.Length - 1] == ')' && SplitTopLevel(term.Substring(1, term.Length - 2), out _, out _).Count >= 1 && Balanced(term.Substring(1, term.Length - 2)))
                term = term.Substring(1, term.Length - 2);
            return term;
        }

        static bool Balanced(string text)
        {
            int depth = 0;
            foreach (char c in text)
            {
                if (c == '(') depth++;
                else if (c == ')' && --depth < 0) return false;
            }
            return depth == 0;
        }

        static bool IsShaderLabKeyword(string trimmed, string keyword)
        {
            if (!trimmed.StartsWith(keyword, StringComparison.Ordinal)) return false;
            if (trimmed.Length == keyword.Length) return true;
            char next = trimmed[keyword.Length];
            return char.IsWhiteSpace(next) || next == '{';
        }

        /// <summary>Reads "#pragma shader_feature[_local][_stage] _ A B // comment". "_" and "__" mean no keyword.</summary>
        static bool TryGetFeatureKeywords(string trimmed, out string[] keywords)
        {
            keywords = null;
            if (!trimmed.StartsWith("#", StringComparison.Ordinal)) return false;
            string directive = trimmed.Substring(1).TrimStart();
            if (!directive.StartsWith("pragma", StringComparison.Ordinal)) return false;
            int comment = directive.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) directive = directive.Substring(0, comment);
            string[] tokens = directive.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || tokens[0] != "pragma" || !tokens[1].StartsWith("shader_feature", StringComparison.Ordinal)) return false;
            List<string> names = new List<string>(tokens.Length - 2);
            for (int t = 2; t < tokens.Length; t++)
                if (tokens[t].Trim('_').Length > 0) names.Add(tokens[t]);
            keywords = names.ToArray();
            return true;
        }

        static readonly string[] s_propertiesEnd ={ "SubShader", "CGINCLUDE", "HLSLINCLUDE", "GLSLINCLUDE", "Category", "Fallback", "FallBack", "CustomEditor" };

        static SourceInfo Analyze(string assetPath, string text, ShaderFacts facts)
        {
            HashSet<string> propertyVariables = facts.PropertyVariables;
            SourceInfo info = new SourceInfo();
            info.AssetPath = assetPath;
            info.SourceBytes = text.Length;
            // Same split as the full lock, so line handling matches it.
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            info.Lines = lines;
            info.Kinds = new LineKind[lines.Length];
            info.KeepWhenRemoved = new bool[lines.Length];
            info.Conditionals = new ConditionalKind[lines.Length];
            info.ProgramBlock = new int[lines.Length];
            info.IsProgramStart = new bool[lines.Length];
            info.Bakeable = new bool[lines.Length];
            info.IsFeaturePragma = new bool[lines.Length];
            List<int> featurePragmaLines = new List<int>();
            List<string[]> featurePragmaKeywords = new List<string[]>();
            int programBlock = -1;
            int programBlockCount = 0;

            string directory = Path.GetDirectoryName(assetPath).Replace('\\', '/');

            // Properties is found by keywords, not braces: display names in Poiyomi contain unbalanced braces.
            int propertiesState = 0; // 0 before, 1 inside, 2 after
            bool shaderNameDone = false;
            bool continuesDirective = false;

            List<int> ifexLines = new List<int>();
            List<int> endexLines = new List<int>();
            List<string> conditionTexts = new List<string>();
            bool[] inProperties = new bool[lines.Length];
            Stack<int> open = new Stack<int>();
            HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
            // Properties referenced in code that only exists when OPTIMIZER_ENABLED is defined.
            HashSet<string> referencedWithOptimizer = new HashSet<string>(StringComparer.Ordinal);
            // Properties that a #define uses as its name or a parameter. Replacing those would break the macro.
            HashSet<string> macroNames = new HashSet<string>(StringComparer.Ordinal);
            // Properties used inside [ ] as an index.
            HashSet<string> indexes = new HashSet<string>(StringComparer.Ordinal);
            // One entry per open #if: whether its first branch, and whether its #else, need OPTIMIZER_ENABLED.
            List<(bool ifNeeds, bool elseNeeds, bool inElse)> optimizerBranches = new List<(bool, bool, bool)>();

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                bool isComment = trimmed.StartsWith("//", StringComparison.Ordinal);

                if (!isComment)
                {
                    if (propertiesState == 0 && IsShaderLabKeyword(trimmed, "Properties")) propertiesState = 1;
                    else if (propertiesState == 1)
                    {
                        foreach (string end in s_propertiesEnd)
                            if (IsShaderLabKeyword(trimmed, end)) { propertiesState = 2; break; }
                    }
                }
                bool lineInProperties = propertiesState == 1;
                inProperties[i] = lineInProperties;

                if (!isComment && propertiesState == 2)
                {
                    if (IsShaderLabKeyword(trimmed, "CGPROGRAM") || IsShaderLabKeyword(trimmed, "HLSLPROGRAM"))
                    {
                        programBlock = ++programBlockCount;
                        info.IsProgramStart[i] = true;
                    }
                    else if (IsShaderLabKeyword(trimmed, "CGINCLUDE") || IsShaderLabKeyword(trimmed, "HLSLINCLUDE"))
                    {
                        programBlock = ++programBlockCount;
                        info.IncludeBlocks.Add(programBlock);
                    }
                    else if (IsShaderLabKeyword(trimmed, "ENDCG") || IsShaderLabKeyword(trimmed, "ENDHLSL"))
                        programBlock = -1;
                }
                info.ProgramBlock[i] = programBlock;

                if (trimmed.StartsWith("//ifex", StringComparison.Ordinal))
                {
                    open.Push(ifexLines.Count);
                    ifexLines.Add(i);
                    endexLines.Add(lines.Length); // an unclosed block runs to the end of the file, as in the full lock
                    conditionTexts.Add(trimmed.Substring(6).Trim());
                }
                else if (trimmed.StartsWith("//endex", StringComparison.Ordinal))
                {
                    if (open.Count == 0)
                        info.Messages.Add((LogType.Error, $"Number of 'endex' statements does not match number of 'ifex' statements in '{assetPath}' (line {i + 1} of its non-empty lines)."));
                    else
                        endexLines[open.Pop()] = i;
                }

                // A directive continued with '\' belongs to the line that started it.
                bool isContinuation = continuesDirective;
                continuesDirective = !isComment && (isContinuation || trimmed.StartsWith("#", StringComparison.Ordinal)) && trimmed.EndsWith("\\", StringComparison.Ordinal);

                // Decide what the line becomes when it is not removed. Properties is kept exactly as written.
                if (lineInProperties)
                {
                    info.Kinds[i] = LineKind.Keep;
                }
                else if (!shaderNameDone && propertiesState == 0 && s_shaderNameRegex.IsMatch(lines[i]))
                {
                    info.Kinds[i] = LineKind.ShaderName;
                    shaderNameDone = true;
                }
                else if (isComment || trimmed.Length == 0)
                {
                    info.Kinds[i] = LineKind.Drop;
                }
                else if (propertiesState == 2 && !isContinuation)
                {
                    if (trimmed.StartsWith("#include", StringComparison.Ordinal))
                    {
                        string rewritten = RewriteRelativeInclude(lines[i], directory);
                        if (rewritten != null)
                        {
                            info.Kinds[i] = LineKind.Include;
                            info.Rewritten[i] = rewritten;
                        }
                        info.KeepWhenRemoved[i] = true;
                        continue;
                    }

                    info.Conditionals[i] = GetConditionalKind(trimmed);
                    if (info.Conditionals[i] != ConditionalKind.None)
                    {
                        if (continuesDirective)
                        {
                            StringBuilder directive = new StringBuilder(lines[i]);
                            for (int j = i + 1; j < lines.Length; j++)
                            {
                                directive.Append('\n').Append(lines[j]);
                                if (!lines[j].TrimEnd().EndsWith("\\", StringComparison.Ordinal)) break;
                            }
                            info.ConditionalText[i] = directive.ToString();
                        }
                        TrackOptimizerBranches(optimizerBranches, info.Conditionals[i], info.ConditionalText.TryGetValue(i, out string whole) ? whole : trimmed);
                        continue;
                    }

                    // A pragma continued on the next line stays as written.
                    if (programBlock != -1 && !continuesDirective && TryGetFeatureKeywords(trimmed, out string[] featureKeywords))
                    {
                        info.IsFeaturePragma[i] = true;
                        featurePragmaLines.Add(i);
                        featurePragmaKeywords.Add(featureKeywords);
                        info.FeatureKeywords.UnionWith(featureKeywords);
                        continue;
                    }

                    Match declaration = s_variableDeclarationRegex.Match(trimmed);
                    if (declaration.Success)
                    {
                        string name = declaration.Groups[2].Value;
                        if (propertyVariables.Contains(name))
                        {
                            info.KeepWhenRemoved[i] = true;
                            if (!info.DeclaredTypes.ContainsKey(name)) info.DeclaredTypes[name] = declaration.Groups[1].Value;
                        }
                        continue;
                    }
                    Match macro = s_macroDeclarationRegex.Match(trimmed);
                    if (macro.Success)
                    {
                        if (propertyVariables.Contains(macro.Groups[1].Value)) info.KeepWhenRemoved[i] = true;
                        continue;
                    }

                    // Code that may reference properties. Other directives (#pragma, #if...) are left alone.
                    if (programBlock != -1 && (!trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("#define", StringComparison.Ordinal)))
                    {
                        info.Bakeable[i] = true;
                        bool needsOptimizer = false;
                        foreach (var branch in optimizerBranches)
                            if (branch.inElse ? branch.elseNeeds : branch.ifNeeds) { needsOptimizer = true; break; }
                        foreach (Match reference in s_propertyReferenceRegex.Matches(lines[i]))
                        {
                            referenced.Add(reference.Value);
                            if (needsOptimizer) referencedWithOptimizer.Add(reference.Value);
                        }
                        // Attributes like [instance(...)] also use brackets, but only at the start of a line.
                        if (!trimmed.StartsWith("[", StringComparison.Ordinal))
                        {
                            foreach (Match index in s_indexRegex.Matches(lines[i]))
                                foreach (Match reference in s_propertyReferenceRegex.Matches(index.Groups[1].Value)) indexes.Add(reference.Value);
                        }
                        Match define = s_defineRegex.Match(trimmed);
                        if (define.Success)
                        {
                            macroNames.Add(define.Groups[1].Value);
                            foreach (string parameter in define.Groups[2].Value.Split(','))
                                macroNames.Add(parameter.Trim());
                        }
                    }
                }
            }

            // Only //ifex outside Properties can remove anything. A block crossing into or out of Properties
            // would remove part of it, so such a block is kept whole and reported.
            List<int> keptIndices = new List<int>();
            for (int b = 0; b < ifexLines.Count; b++)
            {
                bool startsInProperties = inProperties[ifexLines[b]];
                bool endsInProperties = endexLines[b] < lines.Length && inProperties[endexLines[b]];
                if (startsInProperties != endsInProperties)
                    info.Messages.Add((LogType.Warning, $"An //ifex block in '{assetPath}' crosses the edge of Properties (line {ifexLines[b] + 1} of its non-empty lines). Section locking keeps it."));
                if (startsInProperties || endsInProperties) continue;
                if (!HasUsableConditionals(info, ifexLines[b], endexLines[b]))
                {
                    info.Messages.Add((LogType.Log, $"An //ifex block in '{assetPath}' (line {ifexLines[b] + 1} of its non-empty lines) cuts through an #if. Section locking keeps it."));
                    continue;
                }
                keptIndices.Add(b);
            }

            Dictionary<string, int> conditionIds = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> distinct = new List<string>();
            info.IfexLine = new int[keptIndices.Count];
            info.EndexLine = new int[keptIndices.Count];
            info.ConditionIndex = new int[keptIndices.Count];
            for (int k = 0; k < keptIndices.Count; k++)
            {
                int b = keptIndices[k];
                info.IfexLine[k] = ifexLines[b];
                info.EndexLine[k] = endexLines[b];
                string condition = conditionTexts[b];
                if (!conditionIds.TryGetValue(condition, out int id))
                {
                    id = distinct.Count;
                    conditionIds[condition] = id;
                    distinct.Add(condition);
                }
                info.ConditionIndex[k] = id;
            }
            info.Conditions = distinct.ToArray();
            info.FeaturePragmaLines = featurePragmaLines.ToArray();
            info.FeaturePragmaKeywords = featurePragmaKeywords.ToArray();

            // What can be baked: switches the code uses, values the OPTIMIZER_ENABLED code needs as constants, and
            // textures whose presence the code checks through PROP_ defines. Texture PROP_ names that are
            // shader_feature keywords are left to the keyword system.
            HashSet<string> keywords = facts.Keywords;
            List<string> switches = new List<string>();
            List<string> textures = new List<string>();
            int count = facts.Names.Length;
            for (int i = 0; i < count; i++)
            {
                string name = facts.Names[i];
                ShaderPropertyType type = facts.Types[i];
                if (type == ShaderPropertyType.Texture)
                {
                    string define = "PROP" + name.ToUpperInvariant();
                    if (!keywords.Contains(define) && text.Contains(define)) textures.Add(name);
                }
                // Only single values are baked; a color or vector keeps its uniform.
                else if (!macroNames.Contains(name) && (type == ShaderPropertyType.Float || type == ShaderPropertyType.Int || type == ShaderPropertyType.Range)
                    && (referencedWithOptimizer.Contains(name) || (referenced.Contains(name) && facts.IsSwitch[i])))
                {
                    switches.Add(name);
                }
            }
            switches.Sort(StringComparer.Ordinal);
            textures.Sort(StringComparer.Ordinal);
            info.SwitchProperties = switches.ToArray();
            info.SwitchIds = new int[switches.Count];
            info.SwitchIsInt = new bool[switches.Count];
            info.SwitchDeclaredInt = new bool[switches.Count];
            info.SwitchMustBake = new bool[switches.Count];
            info.SwitchExempt = new bool[switches.Count];
            info.SwitchIndexes = new bool[switches.Count];
            for (int s = 0; s < switches.Count; s++)
            {
                string name = switches[s];
                int index = facts.Index[name];
                info.SwitchIndex[name] = s;
                info.SwitchIds[s] = facts.Ids[index];
                info.SwitchIsInt[s] = facts.Types[index] == ShaderPropertyType.Int;
                info.SwitchDeclaredInt[s] = info.DeclaredTypes.TryGetValue(name, out string declared) && (declared == "int" || declared == "uint");
                info.SwitchMustBake[s] = referencedWithOptimizer.Contains(name);
                info.SwitchExempt[s] = facts.Exempt[index];
                info.SwitchIndexes[s] = indexes.Contains(name);
            }
            info.PresenceTextures = textures.ToArray();
            return info;
        }

        /// <summary>
        /// True when the #if structure inside a block is balanced, so declarations kept from it can keep their
        /// conditions. A block that starts or ends between #if and #endif is never removed.
        /// </summary>
        static bool HasUsableConditionals(SourceInfo info, int ifexLine, int endexLine)
        {
            int depth = 0;
            int end = Math.Min(endexLine, info.Lines.Length);
            for (int i = ifexLine + 1; i < end; i++)
            {
                switch (info.Conditionals[i])
                {
                    case ConditionalKind.If: depth++; break;
                    case ConditionalKind.Branch: if (depth == 0) return false; break;
                    case ConditionalKind.EndIf: if (--depth < 0) return false; break;
                }
            }
            return depth == 0;
        }

        /// <summary>
        /// A relative include would resolve against the original's folder, which a shader made from text doesn't
        /// have. Rewrites it to a project path. Returns null when the line needs no change.
        /// </summary>
        static string RewriteRelativeInclude(string line, string directory)
        {
            Match match = s_includeRegex.Match(line);
            if (!match.Success) return null;
            string path = match.Groups[2].Value;
            if (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)) return null;
            if (ShaderOptimizer.DefaultUnityShaderIncludes.Contains(path)) return null;

            string resolved = directory;
            string relative = path;
            while (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative.Substring(2);
            while (relative.StartsWith("../", StringComparison.Ordinal))
            {
                int slash = resolved.LastIndexOf('/');
                if (slash < 0) return null;
                resolved = resolved.Substring(0, slash);
                relative = relative.Substring(3);
            }
            return match.Groups[1].Value + resolved + "/" + relative + match.Groups[3].Value;
        }

        #endregion

        #region Identification

        // Every shader asked about, mapped to its original: a section shader to its source, anything else to itself.
        // Asked every inspector frame, so the answer is cached instead of reading shader.name each time.
        static readonly Dictionary<Shader, Shader> s_sourceOf = new Dictionary<Shader, Shader>();

        /// <summary>The shader a section shader was made from. Any other shader is returned as is.</summary>
        public static Shader GetSourceShader(Shader shader)
        {
            if (shader == null) return null;
            if (s_sourceOf.TryGetValue(shader, out Shader source) && source != null) return source;

            source = shader;
            string name = shader.name;
            if (name.StartsWith(ShaderPrefix, StringComparison.Ordinal))
            {
                // Prefix, original GUID, "/", hash.
                string rest = name.Substring(ShaderPrefix.Length);
                int slash = rest.IndexOf('/');
                Shader found = slash > 0 ? AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(rest.Substring(0, slash))) : null;
                // A section shader whose original is gone has no source.
                source = found;
            }
            if (source != null) s_sourceOf[shader] = source;
            return source;
        }

        /// <summary>
        /// The shader a material's section shader was made from, or the material's shader. Prefers the GUID the
        /// material recorded when it was put on the section shader.
        /// </summary>
        public static Shader GetSourceShader(Material material)
        {
            if (material == null) return null;
            if (!IsSectionShader(material.shader)) return material.shader;
            return GetTaggedSource(material) ?? GetSourceShader(material.shader);
        }

        /// <summary>True for a shader made by the section lock.</summary>
        public static bool IsSectionShader(Shader shader)
        {
            if (shader == null) return false;
            // A section shader whose original is gone maps to null, anything else that isn't one to itself.
            return GetSourceShader(shader) != shader;
        }

        /// <summary>True for a material that is on a section shader.</summary>
        public static bool IsSectionLocked(Material material)
        {
            return material != null && IsSectionShader(material.shader);
        }

        static Shader GetTaggedSource(Material material)
        {
            string guid = material.GetTag(TAG_SECTION_SOURCE, false, string.Empty);
            if (string.IsNullOrEmpty(guid)) return null;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            return shader != null && !shader.IsBroken() ? shader : null;
        }

        /// <summary>True when the section lock can be used on this material.</summary>
        public static bool CanSectionLock(Material material)
        {
            // Only a material that is its own .mat file can be kept off disk while on a section shader.
            if (material == null || material.isVariant || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            string path = AssetDatabase.GetAssetPath(material);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) return false;
            if (!AssetDatabase.IsMainAsset(material)) return false;
            if (material.IsLocked() || IsBlocked(material)) return false;
            Shader source = GetSourceShader(material);
            if (source == null || source.IsBroken() || IsSectionShader(source)) return false;
            if (ShaderOptimizer.IsShaderLocked(source)) return false;
            return ShaderOptimizer.IsShaderUsingThryOptimizer(source);
        }

        #endregion

        #region Baking

        /// <summary>What a section shader has written in as constants.</summary>
        class Bake
        {
            public bool Enabled;
            // Property -> the literal written into the code.
            public SortedDictionary<string, string> Values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            // Textures treated as assigned: PROP_ defined, so their code stays.
            public SortedSet<string> Present = new SortedSet<string>(StringComparer.Ordinal);
            // Enabled shader_feature keywords, written as #defines.
            public SortedSet<string> Keywords = new SortedSet<string>(StringComparer.Ordinal);
        }

        // The bake GetOrCreateShader used last, for Apply to record.
        static Bake s_lastBake;

        const string TouchedKeyPrefix = "Thry.SectionLock.Touched.";
        const string PlainKeyPrefix = "Thry.SectionLock.Plain.";
        const string AppliedKeyPrefix = "Thry.SectionLock.Applied.";

        // What the material's current section shader baked. Kept in SessionState, like the touched set, so a domain
        // reload doesn't make the next change look like nothing had been baked.
        static void StoreAppliedBake(Material material, Bake bake)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> value in bake.Values) sb.Append(value.Key).Append('=').Append(value.Value).Append(';');
            sb.Append('|');
            foreach (string texture in bake.Present) sb.Append(texture).Append(';');
            SessionState.SetString(AppliedKeyPrefix + MaterialKey(material), bake.Enabled ? sb.ToString() : string.Empty);
        }

        static Bake LoadAppliedBake(Material material)
        {
            string stored = SessionState.GetString(AppliedKeyPrefix + MaterialKey(material), string.Empty);
            int split = stored.IndexOf('|');
            if (split < 0) return null;
            Bake bake = new Bake { Enabled = true };
            foreach (string entry in stored.Substring(0, split).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = entry.IndexOf('=');
                if (equals > 0) bake.Values[entry.Substring(0, equals)] = entry.Substring(equals + 1);
            }
            foreach (string texture in stored.Substring(split + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                bake.Present.Add(texture);
            return bake;
        }

        static string MaterialKey(Material material)
        {
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
        }

        /// <summary>Switches and texture slots changed on this material this session. They stay live.</summary>
        static HashSet<string> GetTouched(Material material)
        {
            string stored = SessionState.GetString(TouchedKeyPrefix + MaterialKey(material), string.Empty);
            return new HashSet<string>(stored.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }

        static void SetTouched(Material material, HashSet<string> touched)
        {
            SessionState.SetString(TouchedKeyPrefix + MaterialKey(material), string.Join(",", touched));
        }

        const string BlockedKeyPrefix = "Thry.SectionLock.Blocked.";

        /// <summary>Makes the material use section shaders without baking, after a baked one failed to compile.</summary>
        public static void DisableBaking(Material material)
        {
            SessionState.SetBool(PlainKeyPrefix + MaterialKey(material), true);
        }

        public static bool IsBakingDisabled(Material material)
        {
            return SessionState.GetBool(PlainKeyPrefix + MaterialKey(material), false);
        }

        /// <summary>Keeps the material on its original shader for the rest of the session, after even a section shader
        /// without baked values failed to compile.</summary>
        public static void Block(Material material)
        {
            SessionState.SetBool(BlockedKeyPrefix + MaterialKey(material), true);
        }

        static bool IsBlocked(Material material)
        {
            return SessionState.GetBool(BlockedKeyPrefix + MaterialKey(material), false);
        }

        // A variant always renders with its parent's shader, and a parent's section shader is made from the parent's
        // values, so a material with variants stays on its original.
        static bool HasLoadedVariants(Material material)
        {
            foreach (Material other in Resources.FindObjectsOfTypeAll<Material>())
                if (other != null && other != material && other.isVariant && other.GetRoot() == material) return true;
            return false;
        }

        static string Literal(Material material, int switchIndex, SourceInfo info)
        {
            int id = info.SwitchIds[switchIndex];
            float value = info.SwitchIsInt[switchIndex] ? material.GetInteger(id) : material.GetFloat(id);
            // Integer literals only where the code declares an integer, so float expressions keep float division.
            string literal = info.SwitchDeclaredInt[switchIndex]
                ? ((int)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.0#######", CultureInfo.InvariantCulture);
            return value < 0 ? "(" + literal + ")" : literal;
        }

        static Bake GetBake(Material material, SourceInfo info)
        {
            Bake bake = new Bake();
            if (IsBakingDisabled(material)) return bake;
            bake.Enabled = true;
            HashSet<string> touched = GetTouched(material);
            for (int i = 0; i < info.SwitchProperties.Length; i++)
            {
                string property = info.SwitchProperties[i];
                if (ShaderOptimizer.IsAnimated(material, property)) continue;
                if (!info.SwitchMustBake[i] && (touched.Contains(property) || info.SwitchExempt[i])) continue;
                if (!info.SwitchMustBake[i] && info.SwitchIndexes[i])
                {
                    int id = info.SwitchIds[i];
                    float value = info.SwitchIsInt[i] ? material.GetInteger(id) : material.GetFloat(id);
                    if (value < 0 || value > 3) continue;
                }
                bake.Values[property] = Literal(material, i, info);
            }
            foreach (string texture in info.PresenceTextures)
            {
                if (touched.Contains(texture) || material.GetTexture(texture) != null) bake.Present.Add(texture);
            }
            foreach (string keyword in material.shaderKeywords)
            {
                if (info.FeatureKeywords.Contains(keyword)) bake.Keywords.Add(keyword);
            }
            return bake;
        }

        /// <summary>
        /// Anything the current section shader baked that the material no longer matches was changed by the user.
        /// It becomes live for this material from now on, so changing it again doesn't need another shader.
        /// </summary>
        static void MarkChangesAsTouched(Material material, SourceInfo info)
        {
            Bake current = LoadAppliedBake(material);
            if (current == null) return;
            HashSet<string> touched = GetTouched(material);
            int before = touched.Count;
            foreach (KeyValuePair<string, string> baked in current.Values)
            {
                // Values that must be constants stay baked, so changing them simply makes a new shader.
                if (!info.SwitchIndex.TryGetValue(baked.Key, out int index) || info.SwitchMustBake[index]) continue;
                if (Literal(material, index, info) != baked.Value) touched.Add(baked.Key);
            }
            foreach (string texture in info.PresenceTextures)
            {
                if (current.Present.Contains(texture) != (material.GetTexture(texture) != null)) touched.Add(texture);
            }
            if (touched.Count != before) SetTouched(material, touched);
        }

        #endregion

        #region Hash and generation

        /// <summary>
        /// Works out which //ifex blocks the material switches off, outermost first, as indices into the
        /// source's block list. Blocks inside a removed block are not evaluated, exactly like the full lock.
        /// </summary>
        static List<int> GetRemovedBlocks(Material material, SourceInfo info)
        {
            List<int> removed = new List<int>();
            sbyte[] results = new sbyte[info.Conditions.Length]; // 0 unknown, 1 removes, -1 keeps
            int skipUntil = -1;
            for (int b = 0; b < info.IfexLine.Length; b++)
            {
                if (info.IfexLine[b] <= skipUntil) continue;

                int c = info.ConditionIndex[b];
                if (results[c] == 0)
                {
                    DefineableCondition condition = DefineableCondition.ParseForSectionLock(info.Conditions[c], material);
                    // A condition that reads nothing from the material, like '0==0', only exists to drop
                    // unlocked-only lines (#pragma skip_optimizations). The section shader keeps them, as unlocked.
                    results[c] = (sbyte)(!condition.IsConstantCondition && condition.Test() ? 1 : -1);
                }
                if (results[c] == 1)
                {
                    removed.Add(b);
                    skipUntil = info.EndexLine[b];
                }
            }
            return removed;
        }

        static string ComputeName(SourceInfo info, List<int> removedBlocks, Bake bake)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("section|").Append(GeneratorVersion);
            sb.Append('|').Append(info.Guid);
            sb.Append('|').Append(info.TextHash);
            sb.Append('|').Append((string)Config.Instance.Version);
            sb.Append('|');
            foreach (int b in removedBlocks) sb.Append(b).Append(',');
            sb.Append('|').Append(bake.Enabled ? "baked" : "plain");
            foreach (KeyValuePair<string, string> value in bake.Values) sb.Append('|').Append(value.Key).Append('=').Append(value.Value);
            sb.Append('|');
            foreach (string texture in bake.Present) sb.Append(texture).Append(',');
            sb.Append('|');
            foreach (string keyword in bake.Keywords) sb.Append(keyword).Append(',');

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            string hash;
            using (MD5 md5 = MD5.Create())
                hash = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            return ShaderPrefix + info.Guid + "/" + hash;
        }

        static string GenerateText(SourceInfo info, List<int> removedBlocks, string newShaderName, Bake bake)
        {
            StringBuilder sb = new StringBuilder(info.SourceBytes);
            string[] lines = info.Lines;

            string defines = null;
            Dictionary<int, string> programDefines = null;
            if (bake.Enabled)
            {
                StringBuilder block = new StringBuilder("\n#define ").Append(ShaderOptimizer.OptimizerEnabledKeyword);
                foreach (string texture in bake.Present) block.Append("\n#define PROP").Append(texture.ToUpperInvariant());
                defines = block.ToString();
                programDefines = GetKeywordDefines(info, removedBlocks, bake, defines);
            }
            MatchEvaluator replaceValue = reference => bake.Values.TryGetValue(reference.Value, out string literal) ? literal : reference.Value;

            int next = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (next < removedBlocks.Count && i == info.IfexLine[removedBlocks[next]])
                {
                    int end = info.EndexLine[removedBlocks[next]];
                    int block = info.ProgramBlock[i];
                    if (block != -1 && end < lines.Length && info.ProgramBlock[end] == block)
                        AppendKeptDeclarations(sb, info, i + 1, end);
                    i = end;
                    next++;
                    continue;
                }
                if (bake.Enabled && info.IsFeaturePragma[i]) continue;
                switch (info.Kinds[i])
                {
                    case LineKind.Drop:
                        continue;
                    case LineKind.ShaderName:
                        Match nameLine = s_shaderNameRegex.Match(lines[i]);
                        sb.Append(nameLine.Groups[1].Value).Append(newShaderName).Append(nameLine.Groups[3].Value);
                        break;
                    case LineKind.Include:
                        sb.Append(info.Rewritten[i]);
                        break;
                    default:
                        if (bake.Values.Count > 0 && info.Bakeable[i]) sb.Append(s_propertyReferenceRegex.Replace(lines[i], replaceValue));
                        else sb.Append(lines[i]);
                        break;
                }
                if (defines != null && info.IsProgramStart[i])
                    sb.Append(programDefines.TryGetValue(info.ProgramBlock[i], out string withKeywords) ? withKeywords : defines);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Leaves only the enabled keywords the section shader writes as #defines, those whose shader_feature line is
        /// outside the removed blocks, so the name only changes when the text does.
        /// </summary>
        static void KeepWrittenKeywords(SourceInfo info, List<int> removedBlocks, Bake bake)
        {
            if (bake.Keywords.Count == 0) return;
            HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);
            int next = 0;
            for (int p = 0; p < info.FeaturePragmaLines.Length; p++)
            {
                int line = info.FeaturePragmaLines[p];
                while (next < removedBlocks.Count && info.EndexLine[removedBlocks[next]] < line) next++;
                if (next < removedBlocks.Count && info.IfexLine[removedBlocks[next]] < line) continue;
                kept.UnionWith(info.FeaturePragmaKeywords[p]);
            }
            bake.Keywords.IntersectWith(kept);
        }

        /// <summary>
        /// The defines for each program that declares enabled keywords: <paramref name="defines"/> plus a #define per
        /// enabled keyword whose shader_feature line the section shader keeps, in that program or in a CGINCLUDE.
        /// A keyword whose line went with a removed block stays undefined, as it would be without the defines.
        /// </summary>
        static Dictionary<int, string> GetKeywordDefines(SourceInfo info, List<int> removedBlocks, Bake bake, string defines)
        {
            SortedSet<string> shared = new SortedSet<string>(StringComparer.Ordinal);
            Dictionary<int, SortedSet<string>> perProgram = new Dictionary<int, SortedSet<string>>();
            int next = 0;
            for (int p = 0; p < info.FeaturePragmaLines.Length; p++)
            {
                int line = info.FeaturePragmaLines[p];
                // Removed blocks are outermost and in file order, like the pragma lines.
                while (next < removedBlocks.Count && info.EndexLine[removedBlocks[next]] < line) next++;
                if (next < removedBlocks.Count && info.IfexLine[removedBlocks[next]] < line) continue;

                int block = info.ProgramBlock[line];
                SortedSet<string> target = shared;
                if (!info.IncludeBlocks.Contains(block) && !perProgram.TryGetValue(block, out target))
                    perProgram[block] = target = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string keyword in info.FeaturePragmaKeywords[p])
                    if (bake.Keywords.Contains(keyword)) target.Add(keyword);
            }

            Dictionary<int, string> result = new Dictionary<int, string>();
            for (int i = 0; i < info.Lines.Length; i++)
            {
                if (!info.IsProgramStart[i]) continue;
                int block = info.ProgramBlock[i];
                SortedSet<string> keywords = new SortedSet<string>(shared, StringComparer.Ordinal);
                if (perProgram.TryGetValue(block, out SortedSet<string> own)) keywords.UnionWith(own);
                if (keywords.Count == 0) continue;
                StringBuilder sb = new StringBuilder(defines);
                foreach (string keyword in keywords) sb.Append("\n#define ").Append(keyword);
                result[block] = sb.ToString();
            }
            return result;
        }

        /// <summary>
        /// Appends the declarations and includes a removed block keeps, inside the #if structure they had. An #if
        /// with nothing kept inside is left out. Removable blocks lie within one program block and have balanced
        /// conditionals (HasUsableConditionals).
        /// </summary>
        static void AppendKeptDeclarations(StringBuilder sb, SourceInfo info, int start, int end)
        {
            // One entry per open #if: the header lines (#if, #elif, #else) not written yet, and whether any was.
            List<List<string>> pendingHeaders = new List<List<string>>();
            List<bool> written = new List<bool>();
            for (int j = start; j < end; j++)
            {
                string line = info.ConditionalText.TryGetValue(j, out string whole) ? whole : info.Lines[j];
                switch (info.Conditionals[j])
                {
                    case ConditionalKind.If:
                        pendingHeaders.Add(new List<string> { line });
                        written.Add(false);
                        break;
                    case ConditionalKind.Branch:
                        if (pendingHeaders.Count == 0) break;
                        if (written[written.Count - 1]) sb.Append(line).Append('\n');
                        else pendingHeaders[pendingHeaders.Count - 1].Add(line);
                        break;
                    case ConditionalKind.EndIf:
                        if (pendingHeaders.Count == 0) break;
                        if (written[written.Count - 1]) sb.Append(line).Append('\n');
                        pendingHeaders.RemoveAt(pendingHeaders.Count - 1);
                        written.RemoveAt(written.Count - 1);
                        break;
                    default:
                        if (!info.KeepWhenRemoved[j]) break;
                        for (int f = 0; f < pendingHeaders.Count; f++)
                        {
                            foreach (string header in pendingHeaders[f]) sb.Append(header).Append('\n');
                            pendingHeaders[f].Clear();
                            written[f] = true;
                        }
                        sb.Append(info.Kinds[j] == LineKind.Include ? info.Rewritten[j] : line).Append('\n');
                        break;
                }
            }
        }

        #endregion

        #region Section shaders

        // Section shaders made this session, by name. They are DontSave objects: they survive domain reloads but not
        // an editor restart. After a domain reload they are found again by name.
        static readonly Dictionary<string, Shader> s_shaders = new Dictionary<string, Shader>();
        static bool s_didFindShadersAfterReload;

        // Each section shader of Poiyomi Pro takes about 3.5 MB. Beyond the ones materials use, this many recent
        // ones are kept for switching back quickly.
        const int UnusedShadersKept = 8;
        static readonly Dictionary<Shader, double> s_lastUsed = new Dictionary<Shader, double>();

        static Shader FindSectionShader(string name)
        {
            if (!s_didFindShadersAfterReload)
            {
                s_didFindShadersAfterReload = true;
                foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
                    if (IsSectionShader(shader)) s_shaders[shader.name] = shader;
            }
            if (!s_shaders.TryGetValue(name, out Shader found)) return null;
            if (found == null) s_shaders.Remove(name);
            return found;
        }

        /// <summary>
        /// Returns the section shader for the material's current state, making it when this session has not made it
        /// yet. Does not change the material.
        /// </summary>
        public static Shader GetOrCreateShader(Material material, out bool generated)
        {
            generated = false;
            Timings timings = new Timings();
            Stopwatch sw = Stopwatch.StartNew();

            Shader source = GetSourceShader(material);
            if (source == null || source.IsBroken() || IsSectionShader(source)) return null;
            SourceInfo info = GetSourceInfo(source);
            if (info == null) return null;

            List<int> removed = GetRemovedBlocks(material, info);
            Bake bake = GetBake(material, info);
            KeepWrittenKeywords(info, removed, bake);
            string name = ComputeName(info, removed, bake);
            timings.KeyMs = sw.Elapsed.TotalMilliseconds;
            timings.SourceBytes = info.SourceBytes;
            timings.RemovedBlocks = removed.Count;
            timings.BakedValues = bake.Values.Count;

            s_lastBake = bake;
            Shader existing = FindSectionShader(name);
            if (existing != null)
            {
                // Section shaders outlive a domain reload, Unity's handler cache doesn't.
                ShareDrawerHandlers(source, info, existing);
                s_lastUsed[existing] = EditorApplication.timeSinceStartup;
                LastTimings = timings;
                return existing;
            }

            sw.Restart();
            string text = GenerateText(info, removed, name, bake);
            timings.GenerateMs = sw.Elapsed.TotalMilliseconds;
            timings.GeneratedBytes = text.Length;

            sw.Restart();
            Shader created = ShaderUtil.CreateShaderAsset(text, false);
            timings.CreateMs = sw.Elapsed.TotalMilliseconds;
            LastTimings = timings;

            // A name that didn't take (an unusual Shader line) would leave a copy under the original's name.
            if (created == null || created.name != name || ShaderUtil.ShaderHasError(created))
            {
                ThryLogger.LogErr($"Could not make the section shader for \"{material.name}\". The material keeps its current shader.");
                if (created != null) Object.DestroyImmediate(created);
                return null;
            }
            created.hideFlags = HideFlags.DontSave;
            s_shaders[name] = created;
            s_sourceOf[created] = source;
            s_lastUsed[created] = EditorApplication.timeSinceStartup;
            ShareDrawerHandlers(source, info, created);
            DestroyUnusedShaders(UnusedShadersKept);
            generated = true;
            return created;
        }

        /// <summary>
        /// Destroys section shaders no loaded material uses, keeping the most recent <paramref name="keepRecent"/>.
        /// Returns how many were destroyed.
        /// </summary>
        public static int DestroyUnusedShaders(int keepRecent = 0)
        {
            FindSectionShader(string.Empty);
            if (s_shaders.Count <= keepRecent) return 0;

            HashSet<Shader> used = new HashSet<Shader>();
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                if (IsSectionShader(material.shader)) used.Add(material.shader);

            List<KeyValuePair<string, Shader>> unused = new List<KeyValuePair<string, Shader>>();
            foreach (KeyValuePair<string, Shader> pair in s_shaders)
            {
                if (pair.Value == null || !used.Contains(pair.Value)) unused.Add(pair);
            }
            // Oldest first, so the most recently used ones are kept for switching back.
            unused.Sort((a, b) => LastUsed(a.Value).CompareTo(LastUsed(b.Value)));

            int destroyed = 0;
            for (int i = 0; i < unused.Count - keepRecent; i++)
            {
                Shader shader = unused[i].Value;
                s_shaders.Remove(unused[i].Key);
                if (shader == null) continue;
                s_lastUsed.Remove(shader);
                s_invalidatePropertyCache?.Invoke(null, new object[] { shader });
                Object.DestroyImmediate(shader);
                destroyed++;
            }
            return destroyed;
        }

        static double LastUsed(Shader shader)
        {
            return shader != null && s_lastUsed.TryGetValue(shader, out double time) ? time : 0;
        }

        #endregion

        #region Property drawers

        // Unity builds a MaterialPropertyHandler (the parsed [Attribute] drawers) per shader and property the first
        // time a material uses the shader. For Poiyomi's ~5700 properties that takes about a second, on every new
        // section shader. The section shader's Properties block is the original's, so it can reuse the original's
        // handlers, the same drawer instances that already serve every material on the original shader.
        static readonly Type s_handlerType = typeof(MaterialEditor).Assembly.GetType("UnityEditor.MaterialPropertyHandler");
        static readonly MethodInfo s_getHandler = s_handlerType?.GetMethod("GetHandler", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo s_getPropertyString = s_handlerType?.GetMethod("GetPropertyString", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly FieldInfo s_propertyHandlers = s_handlerType?.GetField("s_PropertyHandlers", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo s_invalidatePropertyCache = s_handlerType?.GetMethod("InvalidatePropertyCache", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// Gives a shader made from <paramref name="source"/>, such as a locked shader, the original's handlers for the
        /// properties it has with the same name and attributes. A locked Poiyomi shader keeps about 1300 of them, and
        /// building their handlers takes about half a second on the first material that uses it, which during an upload
        /// is VRChat's material analysis. Handlers are made per property on demand, so a property several locked shaders
        /// share is only parsed once.
        /// </summary>
        internal static void ShareDrawerHandlersByName(Shader source, Shader target)
        {
            if (source == null || target == null || source == target) return;
            if (s_getHandler == null || s_getPropertyString == null || s_propertyHandlers == null) return;
            int count = target.GetPropertyCount();
            if (count == 0 || !(s_propertyHandlers.GetValue(null) is System.Collections.IDictionary handlers)) return;

            try
            {
                string lastName = target.GetPropertyName(count - 1);
                object[] args = { target, lastName };
                string lastKey = (string)s_getPropertyString.Invoke(null, args);
                // Already shared, or already built by Unity.
                if (handlers.Contains(lastKey)) return;
                string prefix = target.GetObjectId().ToString(CultureInfo.InvariantCulture) + "_";
                bool directKeys = lastKey == prefix + lastName;

                Dictionary<string, int> sourceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                int sourceCount = source.GetPropertyCount();
                for (int i = 0; i < sourceCount; i++)
                {
                    string name = source.GetPropertyName(i);
                    if (!sourceIndex.ContainsKey(name)) sourceIndex[name] = i;
                }

                for (int i = 0; i < count; i++)
                {
                    string name = target.GetPropertyName(i);
                    if (!sourceIndex.TryGetValue(name, out int s)) continue;
                    string[] sourceAttributes = source.GetPropertyAttributes(s);
                    string[] targetAttributes = target.GetPropertyAttributes(i);
                    if (sourceAttributes.Length != targetAttributes.Length) continue;
                    bool same = true;
                    for (int a = 0; a < sourceAttributes.Length && same; a++) same = sourceAttributes[a] == targetAttributes[a];
                    if (!same) continue;

                    args[0] = source;
                    args[1] = name;
                    object handler = s_getHandler.Invoke(null, args);
                    string key = prefix + name;
                    if (!directKeys)
                    {
                        args[0] = target;
                        key = (string)s_getPropertyString.Invoke(null, args);
                    }
                    handlers[key] = handler;
                }
            }
            catch (Exception e)
            {
                // Only costs the time it was meant to save.
                ThryLogger.LogDetail("SectionLock", $"Could not share property drawers with {target.name}: {e.Message}");
            }
        }

        static void ShareDrawerHandlers(Shader source, SourceInfo info, Shader target)
        {
            if (s_getHandler == null || s_getPropertyString == null || s_propertyHandlers == null) return;
            int count = target.GetPropertyCount();
            if (count == 0 || count != source.GetPropertyCount()) return;
            if (!(s_propertyHandlers.GetValue(null) is System.Collections.IDictionary handlers)) return;

            try
            {
                object[] args = { target, target.GetPropertyName(count - 1) };
                string lastKey = (string)s_getPropertyString.Invoke(null, args);
                // Already shared this session.
                if (handlers.Contains(lastKey)) return;

                // The original's handlers are looked up once per version of its file, which is what SourceInfo is.
                if (info.PropertyNames == null || info.PropertyNames.Length != count)
                {
                    string[] names = new string[count];
                    object[] sourceHandlers = new object[count];
                    for (int i = 0; i < count; i++)
                    {
                        names[i] = source.GetPropertyName(i);
                        args[0] = source;
                        args[1] = names[i];
                        sourceHandlers[i] = s_getHandler.Invoke(null, args);
                    }
                    info.PropertyNames = names;
                    info.PropertyHandlers = sourceHandlers;
                }

                // Unity's key is "<instance id>_<property>". Built here directly when that still holds, since calling
                // GetPropertyString by reflection for every property takes most of the time.
                string prefix = target.GetObjectId().ToString(CultureInfo.InvariantCulture) + "_";
                bool directKeys = lastKey == prefix + info.PropertyNames[count - 1];
                for (int i = 0; i < count; i++)
                {
                    string name = info.PropertyNames[i];
                    if (name != target.GetPropertyName(i)) return;
                    string key = prefix + name;
                    if (!directKeys)
                    {
                        args[0] = target;
                        args[1] = name;
                        key = (string)s_getPropertyString.Invoke(null, args);
                    }
                    handlers[key] = info.PropertyHandlers[i];
                }
            }
            catch (Exception e)
            {
                // Only costs the time it was meant to save.
                ThryLogger.LogDetail("SectionLock", "Could not share property drawers with the section shader: " + e.Message);
            }
        }

        #endregion

        #region Apply and revert

        /// <summary>Puts the material on the section shader for its current state.</summary>
        public static Result Apply(Material material)
        {
            if (!CanSectionLock(material)) return Result.NotApplicable;

            Shader source = GetSourceShader(material);
            SourceInfo info = GetSourceInfo(source);
            if (info == null) return Result.Failed;
            if (!IsSectionLocked(material) && HasLoadedVariants(material)) return Result.NotApplicable;
            if (IsSectionLocked(material)) MarkChangesAsTouched(material, info);

            Shader target = GetOrCreateShader(material, out bool generated);
            if (target == null) return Result.Failed;
            StoreAppliedBake(material, s_lastBake);
            if (material.shader == target) return Result.Unchanged;

            Stopwatch sw = Stopwatch.StartNew();
            SwapShader(material, target);
            FillNonModifiableTextures(material, source);
            material.SetOverrideTag(TAG_SECTION_SOURCE, info.Guid);

            Timings timings = LastTimings;
            timings.SwapMs = sw.Elapsed.TotalMilliseconds;
            LastTimings = timings;
            return generated ? Result.Generated : Result.Reused;
        }

        /// <summary>
        /// Puts a section-locked material back on its original shader, or a material that lost its section shader.
        /// Values and keywords are untouched, since the section lock never changes them.
        /// </summary>
        public static bool Revert(Material material)
        {
            if (material == null) return false;
            // A variant's shader is its parent's.
            if (material.isVariant) material = material.GetRoot();
            Shader source;
            if (IsSectionLocked(material)) source = GetSourceShader(material);
            else if (material.shader.IsBroken() && LostItsShader(material)) source = GetTaggedSource(material);
            else return false;

            if (source == null || source.IsBroken())
            {
                if (IsSectionLocked(material) || !string.IsNullOrEmpty(material.GetTag(TAG_SECTION_SOURCE, false, string.Empty)))
                    ThryLogger.LogErr($"Could not find the original shader of \"{material.name}\".");
                return false;
            }
            ClearFilledTextures(material);
            SwapShader(material, source);
            material.SetOverrideTag(TAG_SECTION_SOURCE, string.Empty);
            return true;
        }

        /// <summary>Reverts every section-locked material in the list. Returns how many were reverted.</summary>
        public static int RevertAll(IEnumerable<Material> materials)
        {
            int reverted = 0;
            foreach (Material material in materials)
            {
                if (material == null) continue;
                // A variant's shader is its parent's.
                Material root = material.isVariant ? material.GetRoot() : material;
                // A section-locked material's file already has the original shader, so a clean one stays clean. One that
                // lost its shader has a broken file, so it stays dirty and the fix gets saved.
                bool clean = IsSectionLocked(root) && !EditorUtility.IsDirty(root);
                if (!Revert(material)) continue;
                if (clean) EditorUtility.ClearDirty(root);
                reverted++;
            }
            return reverted;
        }

        /// <summary>Reverts every loaded section-locked material. Returns how many were reverted.</summary>
        public static int RevertAllLoaded()
        {
            return RevertAll(Resources.FindObjectsOfTypeAll<Material>());
        }

        /// <summary>Puts a section shader back after it was taken off for saving.</summary>
        internal static void Restore(Material material, Shader sectionShader, Shader source)
        {
            if (material == null || sectionShader == null || source == null) return;
            // Something else changed the shader in the meantime.
            if (material.shader != source) return;
            SwapShader(material, sectionShader);
            FillNonModifiableTextures(material, source);
            material.SetOverrideTag(TAG_SECTION_SOURCE, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)));
        }

        /// <summary>
        /// Fixes a material whose file was written while it was on a section shader and so lost its shader: puts the
        /// original back and saves the file again. Returns true when it did. A material pointing at a shader asset
        /// that is missing is left alone, even when it carries the tag.
        /// </summary>
        public static bool RepairIfBroken(Material material)
        {
            if (material == null || !material.shader.IsBroken()) return false;
            if (string.IsNullOrEmpty(material.GetTag(TAG_SECTION_SOURCE, false, string.Empty))) return false;
            if (!LostItsShader(material)) return false;
            if (!Revert(material)) return false;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            ThryLogger.LogDetail("SectionLock", $"Put the original shader back on \"{material.name}\" after it was saved without it.");
            return true;
        }

        // True when the material's shader reference is empty or points at an object that isn't an asset, as it does
        // after a section shader was written out or destroyed.
        static bool LostItsShader(Material material)
        {
            SerializedProperty shaderProperty = new SerializedObject(material).FindProperty("m_Shader");
            if (shaderProperty == null) return false;
#if UNITY_6000_5_OR_NEWER
            EntityId id = shaderProperty.objectReferenceEntityIdValue;
            return id == EntityId.None || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(id));
#else
            int id = shaderProperty.objectReferenceInstanceIDValue;
            return id == 0 || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(id));
#endif
        }

        static readonly PropertyInfo s_rawRenderQueue = typeof(Material).GetProperty("rawRenderQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly string[] s_tagsKeptAcrossSwap = BuildTagsKeptAcrossSwap();

        static string[] BuildTagsKeptAcrossSwap()
        {
            List<string> tags = new List<string> { "RenderType" };
            tags.AddRange(MaterialHelper.TagsPreservedAcrossShaderSwap);
            return tags.ToArray();
        }

        // Unity drops the material's RenderType and VRCFallback overrides and resets its render queue when the
        // shader changes. Only what the material itself stored is put back, so swapping to the section shader and
        // back leaves the material as it was. Property drawers are skipped like in the full lock; running all of
        // them on a shader this size is slow.
        static void SwapShader(Material material, Shader shader)
        {
            int rawQueue = s_rawRenderQueue != null ? (int)s_rawRenderQueue.GetValue(material) : material.renderQueue;
            Dictionary<string, string> ownTags = MaterialHelper.GetOwnOverrideTags(material, s_tagsKeptAcrossSwap);

            ShaderOptimizer.DetourApplyMaterialPropertyDrawers();
            try
            {
                material.shader = shader;
            }
            finally
            {
                ShaderOptimizer.RestoreApplyMaterialPropertyDrawers();
            }
            material.renderQueue = rawQueue;
            MaterialHelper.ApplyOverrideTags(material, ownTags);
        }

        // Slots FillNonModifiableTextures filled, per material, so Revert can empty them again before a save.
        static readonly Dictionary<Material, List<string>> s_filledTextures = new Dictionary<Material, List<string>>();

        // The importer gives its own shader the non-modifiable textures (Poiyomi's DFG lookup tables). A section
        // shader has no importer, so a material that leaves those slots empty gets the importer's texture instead.
        static void FillNonModifiableTextures(Material material, Shader source)
        {
            ShaderImporter importer = null;
            int count = source.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (!ShaderUtil.IsShaderPropertyNonModifiableTexureProperty(source, i)) continue;
                string name = source.GetPropertyName(i);
                if (material.GetTexture(name) != null) continue;
                if (importer == null) importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as ShaderImporter;
                if (importer == null) return;
                Texture texture = importer.GetNonModifiableTexture(name);
                if (texture == null) continue;
                material.SetTexture(name, texture);
                if (!s_filledTextures.TryGetValue(material, out List<string> filled)) s_filledTextures[material] = filled = new List<string>();
                filled.Add(name);
            }
        }

        static void ClearFilledTextures(Material material)
        {
            if (!s_filledTextures.TryGetValue(material, out List<string> filled)) return;
            foreach (string name in filled) material.SetTexture(name, null);
            s_filledTextures.Remove(material);
        }

        #endregion
    }
}
