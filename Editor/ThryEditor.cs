// Material/Shader Inspector for Unity 2017/2018
// Copyright (C) 2019 Thryrallo

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using Thry.ThryEditor;
using Thry.ThryEditor.ShaderTranslations;
using JetBrains.Annotations;
using Thry.ThryEditor.Helpers;
using Thry.ThryEditor.Drawers;
using UnityEngine.Rendering;

namespace Thry
{
    public partial class ShaderEditor : ShaderGUI
    {
        public const string EXTRA_OPTIONS_PREFIX = "--";
        public const string PROPERTY_NAME_MASTER_LABEL = "shader_master_label";
        public const string PROPERTY_NAME_LOCALE = "shader_locale";
        public const string PROPERTY_NAME_ON_SWAP_TO_ACTIONS = "shader_on_swap_to";
        public const string PROPERTY_NAME_IN_SHADER_PRESETS = "_Mode";

        //Static
        public static ShaderEditor Active { get; private set; }

        /// <summary>
        /// Optional external hook invoked right before a shader is assigned to a material through the inspector's
        /// shader dropdown. Receives (material, oldShader, newShader). Return <c>false</c> to cancel the assignment -
        /// the material keeps its current shader, with no flicker or wasted recompile. Return <c>true</c> (the default
        /// behaviour when no handler is registered) to let the swap proceed as normal.
        /// Kept shader-agnostic so downstream packages (e.g. Poiyomi's version-upgrade prompt) can opt in without
        /// ThryEditor taking a dependency on them.
        /// </summary>
        public static Func<Material, Shader, Shader, bool> OnBeforeAssignNewShader;

        /// <summary>
        /// Values a new material is created with where they differ from the shader's defaults, by property name
        /// (numbers in x). The inspector doesn't mark them as changed.
        /// </summary>
        public static readonly Dictionary<string, Vector4> CreationDefaults = new Dictionary<string, Vector4>();

        // Stores the different shader properties
        private ShaderGroup _mainGroup;
        private RenderQueueProperty _renderQueueProperty;
        private VRCFallbackProperty _vRCFallbackProperty;

        // shader specified values
        private ShaderHeaderProperty _shaderHeader = null;
        private List<FooterButton> _footers;

        // sates
        private bool _isFirstOnGUICall = true;
        private bool _doReloadNextDraw = false;
        private bool _didSwapToShader = false;
        private bool _didRegisterCallbacks = false;
        private bool _doApplyRenderingPresetAfterSwap = false;

        //EditorData
        public string RenamedPropertySuffix;
        public bool HasCustomRenameSuffix;
        public bool IsLockedMaterial;
        public bool IsCrossEditor = false;

        public MaterialEditor Editor { get; private set; }
        public MaterialProperty[] Properties { get; private set; }
        public Material[] Materials { get; private set; }
        public Shader Shader { get; private set; }
        public ShaderImporter ImporterShader { get; private set; }
        public Shader LastShader { get; private set; }
        public ShaderPart CurrentProperty;
        public Dictionary<string, ShaderProperty> PropertyDictionary { get; private set; }
        public List<ShaderPart> ShaderParts { get; private set; }
        public List<ShaderProperty> TextureArrayProperties { get; private set; }
        public bool IsInAnimationMode { get; private set; }
        public Renderer ActiveRenderer { get; private set; }
        public Localization Locale { get; private set; }
        public ShaderTranslator SuggestedTranslationDefinition { get; private set; }

        public MaterialPropertyNotesContainer[] NoteContainers { get; private set; }

        //other
        string ShaderOptimizerPropertyName = null;
        ShaderProperty ShaderOptimizerProperty { get; set; }
        ShaderProperty InShaderPresetsProperty { get; set; }

        [PublicAPI]
        public float ShaderRenderingPreset { get => InShaderPresetsProperty.FloatValue; set => InShaderPresetsProperty.FloatValue = value; }

        private DefineableAction[] _onSwapToActions = null;

        public bool IsPresetEditor { get; private set; } = false;
        public bool IsSectionedPresetEditor
        {
            get
            {
                return IsPresetEditor && Presets.IsMaterialSectionedPreset(Materials[0]);
            }
        }

        public bool HasMixedCustomPropertySuffix
        {
            get
            {
                if (Materials.Length == 1) return false;
                string suffix = ShaderOptimizer.GetRenamedPropertySuffix(Materials[0]);
                for (int i = 1; i < Materials.Length; i++)
                {
                    if (suffix != ShaderOptimizer.GetRenamedPropertySuffix(Materials[i])) return true;
                }
                return false;
            }
        }

        public bool DidSwapToNewShader
        {
            get
            {
                return _didSwapToShader;
            }
        }

        public string TargetName
        {
            get
            {
                if(Materials.Length == 1)
                {
                    return Materials[0].name;
                }
                return $"{Materials.Length} Materials";
            }
        }

        public void SetShader(Shader shader, Shader lastShader = null)
        {
            Shader = shader;
            LastShader = lastShader;
            ImporterShader = ShaderImporter.GetAtPath(AssetDatabase.GetAssetPath(Shader)) as ShaderImporter;
        }

        // Needed for the CrossEditor
        static Dictionary<Shader, ShaderImporter> s_shaderImporterCache = new Dictionary<Shader, ShaderImporter>();
        public ShaderImporter GetShaderImporter(Shader shader)
        {
            if(s_shaderImporterCache.ContainsKey(shader))
            {
                return s_shaderImporterCache[shader];
            }
            ShaderImporter importer = ShaderImporter.GetAtPath(AssetDatabase.GetAssetPath(shader)) as ShaderImporter;
            s_shaderImporterCache.Add(shader, importer);
            return importer;
        }

        // Needed for the CrossEditor
        Dictionary<string, MaterialEditor> s_editorCache = new Dictionary<string, MaterialEditor>();
        public MaterialEditor GetMaterialEditor(UnityEngine.Object[] targets)
        {
            string key = string.Join(",", targets.Select(t => t.GetObjectId()));
            if(s_editorCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }
            MaterialEditor editor = MaterialEditor.CreateEditor(targets) as MaterialEditor;
            s_editorCache[key] = editor;
            return editor;
        }

        public void ApplySuggestedTranslationDefinition()
        {
            if(SuggestedTranslationDefinition != null)
            {
                SuggestedTranslationDefinition.Apply(this);
                SuggestedTranslationDefinition = null;
            }
        }

        //-------------Init functions--------------------

        public static string SplitOptionsFromDisplayName(ref string displayName)
        {
            int index = displayName.IndexOf(EXTRA_OPTIONS_PREFIX, StringComparison.Ordinal);
            if(index == -1) return null;
            string options = displayName.Substring(index + EXTRA_OPTIONS_PREFIX.Length);
            displayName = displayName.Substring(0, index);
            return options;
        }

        internal enum ThryPropertyType
        {
            hidden_property, shown_property, master_label, footer, header, header_end, header_start, group_start, group_end, section_start, section_end, subsection_start, subsection_end, instancing, dsgi, lightmap_flags, locale, on_swap_to, space, optimizer, in_shader_presets
        }

        internal ThryPropertyType GetPropertyType(MaterialProperty p)
        {
            string name = p.name;
#if UNITY_6000_2_OR_NEWER
            UnityEngine.Rendering.ShaderPropertyFlags flags = p.propertyFlags;
#else
            MaterialProperty.PropFlags flags = p.flags;
#endif

            // Check for ThryHideInInspector attribute (visible to Unity API, hidden by ThryEditor)
            if (HasThryHideInInspectorAttribute(p))
                return ThryPropertyType.hidden_property;

#if UNITY_6000_2_OR_NEWER
            if (flags == UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector)
#else
            if (flags == MaterialProperty.PropFlags.HideInInspector)
#endif
            {
                if (name[0] == '_')
                {
                    return ThryPropertyType.hidden_property;
                }

                if (name == PROPERTY_NAME_MASTER_LABEL) return ThryPropertyType.master_label;
                if (name == PROPERTY_NAME_ON_SWAP_TO_ACTIONS) return ThryPropertyType.on_swap_to;
                if (name == PROPERTY_NAME_LOCALE) return ThryPropertyType.locale;

                if (name == "Instancing") return ThryPropertyType.instancing;
                if (name == "DSGI") return ThryPropertyType.dsgi;
                if (name == "LightmapFlags") return ThryPropertyType.lightmap_flags;


                if (name[0] == 'm')
                {
                    if (name.StartsWith("m_start", StringComparison.Ordinal)) return ThryPropertyType.header_start;
                    if (name.StartsWith("m_end", StringComparison.Ordinal)) return ThryPropertyType.header_end;
                    if (name.StartsWith("m_", StringComparison.Ordinal)) return ThryPropertyType.header;
                }
                else if (name[0] == 'g')
                {
                    if (name.StartsWith("g_start", StringComparison.Ordinal)) return ThryPropertyType.group_start;
                    if (name.StartsWith("g_end", StringComparison.Ordinal)) return ThryPropertyType.group_end;
                }
                else if (name[0] == 's')
                {
                    if (name.StartsWith("ss_start", StringComparison.Ordinal)) return ThryPropertyType.subsection_start;
                    if (name.StartsWith("ss_end", StringComparison.Ordinal)) return ThryPropertyType.subsection_end;
                    if (name.StartsWith("s_start", StringComparison.Ordinal)) return ThryPropertyType.section_start;
                    if (name.StartsWith("s_end", StringComparison.Ordinal)) return ThryPropertyType.section_end;
                }


                if (name.StartsWith("footer_", StringComparison.Ordinal)) return ThryPropertyType.footer;
                if (name.StartsWith("space", StringComparison.Ordinal)) return ThryPropertyType.space;
            }
            if (name == ShaderOptimizerPropertyName)  return ThryPropertyType.optimizer;
            // Any recognized preset name, not just _Mode, so shaders that call it something else (a grab pass's
            // _GrabMode, say) get the same labelless dropdown next to the presets bar instead of a property row.
            if (RenderingPresets.PresetPropertyNames.Contains(name)) return ThryPropertyType.in_shader_presets;

#if UNITY_6000_2_OR_NEWER
            if (flags.HasFlag(UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector)) return ThryPropertyType.hidden_property;
#else
            if (flags.HasFlag(MaterialProperty.PropFlags.HideInInspector)) return ThryPropertyType.hidden_property;
#endif
            return ThryPropertyType.shown_property;
        }

        // Which properties carry [ThryHideInInspector], per shader.
        //
        // This used to be answered per property with Shader.FindPropertyIndex(name) followed by
        // GetPropertyAttributes - a name search and a string[] allocation for each of the ~4900 properties
        // on a Poiyomi shader, to find the ~830 that are actually flagged. That cost about 24ms of every
        // material selection. The answer depends only on the shader, so gather it once instead: the first
        // selection of a shader pays ~2.8ms and every later one pays a set lookup.
        //
        // Keyed by instance id rather than the Shader itself so a reimported or destroyed shader cannot be
        // held alive by this cache, and cleared on reimport since a shader's attributes can change there.
        private static readonly Dictionary<int, HashSet<string>> _thryHiddenPropertiesByShader = new Dictionary<int, HashSet<string>>();

        internal static void ClearThryHiddenPropertyCache()
        {
            _thryHiddenPropertiesByShader.Clear();
        }

        private static HashSet<string> GetThryHiddenProperties(Shader shader)
        {
            int key = shader.GetObjectId();
            HashSet<string> hidden;
            if (_thryHiddenPropertiesByShader.TryGetValue(key, out hidden)) return hidden;

            hidden = new HashSet<string>();
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string[] attributes = shader.GetPropertyAttributes(i);
                for (int a = 0; a < attributes.Length; a++)
                {
                    if (attributes[a].StartsWith("ThryHideInInspector", StringComparison.Ordinal))
                    {
                        hidden.Add(shader.GetPropertyName(i));
                        break;
                    }
                }
            }

            _thryHiddenPropertiesByShader[key] = hidden;
            return hidden;
        }

        private bool HasThryHideInInspectorAttribute(MaterialProperty p)
        {
            if (Shader == null) return false;
            return GetThryHiddenProperties(Shader).Contains(p.name);
        }

        private void LoadLocales()
        {
            MaterialProperty locales_property = GetMaterialProperty(PROPERTY_NAME_LOCALE);
            Locale = null;
            if (locales_property != null)
            {
                string guid = locales_property.displayName;
                Locale = Localization.Load(guid);
            }else
            {
                Locale = Localization.Create();
            }
        }

        public void FakePartialInitilizationForLocaleGathering(Shader s)
        {
            Material material = new Material(s);
            Materials = new Material[] { material };
            Editor = MaterialEditor.CreateEditor(new UnityEngine.Object[] { material }) as MaterialEditor;
            Properties = MaterialEditor.GetMaterialProperties(Materials);
            RenamedPropertySuffix = ShaderOptimizer.GetRenamedPropertySuffix(Materials[0]);
            HasCustomRenameSuffix = ShaderOptimizer.HasCustomRenameSuffix(Materials[0]);
            ShaderEditor.Active = this;
            CollectAllProperties();
            UnityEngine.Object.DestroyImmediate(Editor);
            UnityEngine.Object.DestroyImmediate(material);
        }

        /// <summary>
        /// ApplyRenderingPresetToMaterial 
        /// This applies a Rendering Preset (_Mode) value to an existing material by temporarily
        /// initializing a ShaderEditor Context so that the on_value_actions defined in the
        /// shader's property attributes are executed (setting blend modes, render queue,
        /// property values, etc.).
        /// Use this to apply Rendering Presets outside of the normal Inspector GUI draw loop.
        /// </summary>
        public static void ApplyRenderingPresetToMaterial(Material material, float modeValue)
        {
            if (material == null || !RenderingPresets.PresetPropertyNames.Any(n => material.HasProperty(n))) return;

            ShaderEditor tempEditor = CreateTemporary(material);
            try
            {
                if (tempEditor.InShaderPresetsProperty != null) tempEditor.ShaderRenderingPreset = modeValue;
            }
            finally
            {
                tempEditor.ReleaseTemporary();
            }
        }

        ShaderEditor _previousActive;

        // The editor stays Active until ReleaseTemporary
        internal static ShaderEditor CreateTemporary(Material material, Shader lastShader = null)
        {
            ShaderEditor tempEditor = new ShaderEditor();
            tempEditor._previousActive = Active;
            try
            {
                tempEditor.Materials = new Material[] { material };
                tempEditor.Editor = MaterialEditor.CreateEditor(new UnityEngine.Object[] { material }) as MaterialEditor;
                tempEditor.Properties = MaterialEditor.GetMaterialProperties(tempEditor.Materials);
                tempEditor.RenamedPropertySuffix = ShaderOptimizer.GetRenamedPropertySuffix(material);
                tempEditor.HasCustomRenameSuffix = ShaderOptimizer.HasCustomRenameSuffix(material);
                Active = tempEditor;
                tempEditor.SetShader(SectionLock.GetSourceShader(material.shader), lastShader);
                tempEditor.CollectAllProperties();
                return tempEditor;
            }
            catch
            {
                tempEditor.ReleaseTemporary();
                throw;
            }
        }

        internal void ReleaseTemporary()
        {
            if (Editor != null) UnityEngine.Object.DestroyImmediate(Editor);
            Editor = null;
            Active = _previousActive;
        }

        // finds all properties and headers and stores them in correct order
        /// <summary>
        /// Footer definitions for Poiyomi shaders that predate the JSON footer format.
        ///
        /// 7.3 and older declare a footer as a plain label - footer_youtube ("youtube footer button",
        /// Float) - which carries no url and no icon, so ButtonData deserializes to null and the button
        /// renders as a dead 42px slot. These are Poiyomi's watermark and are meant to be visible, so
        /// they are rebuilt here from the definitions the current shaders ship, rather than dropped.
        ///
        /// Applied only to Poiyomi shaders on purpose: footer_discord and friends are generic
        /// ThryEditor property names that any shader can declare, and a third party shader's footer
        /// must never be silently pointed at Poiyomi's socials.
        /// </summary>
        private static readonly Dictionary<string, string> LegacyPoiyomiFooters = new Dictionary<string, string>()
        {
            { "footer_youtube", "{texture:{name:icon-youtube,height:24},action:{type:URL,data:https://www.youtube.com/poiyomi},hover:YOUTUBE}" },
            { "footer_twitter", "{texture:{name:icon-twitter,height:24},action:{type:URL,data:https://x.com/poiyomi},hover:X}" },
            { "footer_patreon", "{texture:{name:icon-patreon-new,height:24},action:{type:URL,data:https://www.patreon.com/poiyomi},hover:PATREON}" },
            { "footer_discord", "{texture:{name:icon-discord,height:24},action:{type:URL,data:https://discord.gg/poiyomi},hover:DISCORD}" },
            { "footer_github", "{texture:{name:icon-github,height:24},action:{type:URL,data:https://github.com/poiyomi/PoiyomiToonShader},hover:GITHUB}" },
        };

        // Matches both the shader itself (".poiyomi/...") and its locked copy ("Hidden/Locked/.poiyomi/...")
        private bool IsPoiyomiShader => Shader != null && Shader.name.Contains(".poiyomi/");
        private void CollectAllProperties()
        {
            // Only set when the shader declares them, so reset first
            _onSwapToActions = null;
            _shaderHeader = null;
            ShaderOptimizerPropertyName = null;

            if (ShaderOptimizer.IsShaderUsingThryOptimizer(Shader))
            {
                ShaderOptimizerPropertyName = ShaderOptimizer.GetOptimizerPropertyName(Shader);
            }

            //load display names from file if it exists
            MaterialProperty[] props = Properties;
            LoadLocales();

            PropertyDictionary = new Dictionary<string, ShaderProperty>();
            ShaderParts = new List<ShaderPart>();
            InShaderPresetsProperty = null; // Cleared so the preset picked below is always one from this pass
            _mainGroup = new ShaderGroup(this); //init top object that all Shader Objects are childs of
            Stack<ShaderGroup> groupStack = new Stack<ShaderGroup>(); //header stack. used to keep track if editorData header to parent new objects to
            groupStack.Push(_mainGroup); //add top object as top object to stack
            groupStack.Push(_mainGroup); //add top object a second time, because it get's popped with first actual header item
            _footers = new List<FooterButton>(); //init footer list

            for (int i = 0; i < props.Length; i++)
            {
                string displayName = props[i].displayName;

                //extract json data from display name
                string optionsRaw = SplitOptionsFromDisplayName(ref displayName);

                // A second declaration of a property can keep its own label; the locale is keyed by property name
                if (optionsRaw != null && optionsRaw.Contains("own_label:true")) displayName = Locale.Get(displayName, displayName);
                else displayName = Locale.Get(props[i], displayName);

                ThryPropertyType type = GetPropertyType(props[i]);
                ShaderProperty NewProperty = null;
                ShaderPart newPart = null;
                // -- Group logic --
                // Create new group if needed
                switch(type)
                {
                    case ThryPropertyType.group_start:
                        newPart = new ShaderGroup(this, props[i], Editor, displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.section_start:
                        newPart = new ShaderSection(this, props[i], Editor, displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.subsection_start:
                        newPart = new ShaderSubSection(this, props[i], Editor, displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.header:
                    case ThryPropertyType.header_start:
                        newPart = new ShaderHeader(this, props[i], Editor, displayName, optionsRaw, i);
                        break;
                }
                // pop if needed
                if(type == ThryPropertyType.header || type == ThryPropertyType.header_end || type == ThryPropertyType.group_end || type == ThryPropertyType.section_end || type == ThryPropertyType.subsection_end)
                {
                    groupStack.Pop();
                }
                // push if needed
                if(newPart != null)
                {
                    groupStack.Peek().AddPart(newPart);
                    groupStack.Push(newPart as ShaderGroup);
                }
                
                bool doAssignPropertyToGroup = true;
                switch (type)
                {
                    case ThryPropertyType.on_swap_to:
                        _onSwapToActions = PropertyOptions.Deserialize(optionsRaw).actions;
                        break;
                    case ThryPropertyType.master_label:
                        _shaderHeader = new ShaderHeaderProperty(this, props[i], displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.footer:
                    {
                        // Null here means the display name was not a {...} ButtonData, which is how
                        // pre-JSON shaders declare footers. See LegacyPoiyomiFooters for the fallback.
                        ButtonData footerData = Parser.Deserialize<ButtonData>(displayName);
                        if (footerData == null && IsPoiyomiShader
                            && LegacyPoiyomiFooters.TryGetValue(props[i].name, out string legacyFooter))
                            footerData = Parser.Deserialize<ButtonData>(legacyFooter);
                        // A footer with no data is a dead slot that throws the moment it is clicked
                        if (footerData != null)
                            _footers.Add(new FooterButton(footerData));
                        break;
                    }
                    case ThryPropertyType.hidden_property:
                    case ThryPropertyType.shown_property:
#if UNITY_6000_2_OR_NEWER
                        if (props[i].propertyType == ShaderPropertyType.Texture)
                            NewProperty = new ShaderTextureProperty(this, props[i], displayName, optionsRaw, props[i].propertyFlags.HasFlag(UnityEngine.Rendering.ShaderPropertyFlags.NoScaleOffset) == false, i);
#else
                        if (props[i].GetPropertyType() == ShaderPropertyType.Texture)
                            NewProperty = new ShaderTextureProperty(this, props[i], displayName, optionsRaw, props[i].flags.HasFlag(MaterialProperty.PropFlags.NoScaleOffset) == false, i);
#endif
                        else
                            NewProperty = new ShaderProperty(this, props[i], displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.lightmap_flags:
                        NewProperty = new GIProperty(this, props[i], displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.dsgi:
                        NewProperty = new DSGIProperty(this, props[i], displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.instancing:
                        NewProperty = new InstancingProperty(this, props[i], displayName, optionsRaw, i);
                        break;
                    case ThryPropertyType.optimizer:
                        ShaderOptimizerProperty = new ShaderProperty(this, props[i], displayName, optionsRaw, i);
                        ShaderOptimizerProperty.SetIsExemptFromLockedDisabling(true);
                        NewProperty = ShaderOptimizerProperty;
                        doAssignPropertyToGroup = false;
                        break;
                    case ThryPropertyType.in_shader_presets:
                        ShaderProperty presetsProperty = new ShaderProperty(this, props[i], displayName, optionsRaw, i);
                        // A shader is expected to declare one preset property; should it declare several, the most
                        // specific name wins, matching how RenderingPresets resolves it.
                        if (InShaderPresetsProperty == null ||
                            RenderingPresets.PresetPropertyNames.IndexOf(props[i].name) < RenderingPresets.PresetPropertyNames.IndexOf(InShaderPresetsProperty.MaterialProperty.name))
                            InShaderPresetsProperty = presetsProperty;
                        NewProperty = presetsProperty;
                        doAssignPropertyToGroup = false;
                        break;
                }
                if (NewProperty != null)
                {
                    NewProperty.SetHidden(type == ThryPropertyType.hidden_property);
                    newPart = NewProperty;
                    if (type != ThryPropertyType.hidden_property && doAssignPropertyToGroup)
                    {
                        groupStack.Peek().AddPart(NewProperty);
                    }
                    
                    PropertyDictionary.TryAdd(props[i].name, NewProperty);
                }
                if (newPart != null)
                {
                    ShaderParts.Add(newPart);
                }
            }
        }

        public void InitlizeThryUI()
        {
            Config config = Config.Instance;
            Active = this;
            RegisterCallacks();

            //get material targets
            Materials = Editor.targets.OfType<Material>()
                .Where(m => m != null && (!IsCrossEditor || ShaderHelper.IsShaderUsingThryEditor(m))).ToArray();

            // A material on a section shader is edited as its original: same properties, importer and UI state.
            SetShader(SectionLock.GetSourceShader(Materials[0].shader), LastShader);

            RenamedPropertySuffix = ShaderOptimizer.GetRenamedPropertySuffix(Materials[0]);
            HasCustomRenameSuffix = ShaderOptimizer.HasCustomRenameSuffix(Materials[0]);

            IsPresetEditor = Materials.Length == 1 && Presets.ArePreset(Materials);
            
            NoteContainers = MaterialPropertyNotesContainer.GetNoteContainersForMaterials(Materials);

            //collect shader properties
            CollectAllProperties();

            _renderQueueProperty = new RenderQueueProperty(this);
            _vRCFallbackProperty = new VRCFallbackProperty(this);
            ShaderParts.Add(_renderQueueProperty);
            ShaderParts.Add(_vRCFallbackProperty);

            if(Config.Instance.forceAsyncCompilationPreview)
            {
                ShaderUtil.allowAsyncCompilation = true;
            }

            // The new shader's properties exist now, so its rendering preset can be applied - a preset value only
            // means something once the shader that declares it is the one collected.
            if (_doApplyRenderingPresetAfterSwap)
            {
                _doApplyRenderingPresetAfterSwap = false;
                RenderingPresets.ApplyAfterSwap(this, Materials);
            }

            _isFirstOnGUICall = false;
        }

        private Dictionary<string, MaterialProperty> materialPropertyDictionary;
        public MaterialProperty GetMaterialProperty(string name)
        {
            if (materialPropertyDictionary == null)
            {
                materialPropertyDictionary = new Dictionary<string, MaterialProperty>();
                foreach (MaterialProperty p in Properties)
                    if (materialPropertyDictionary.ContainsKey(p.name) == false) materialPropertyDictionary.Add(p.name, p);
            }
            if (materialPropertyDictionary.ContainsKey(name))
                return materialPropertyDictionary[name];
            return null;
        }

        
        // Every editor that currently holds callbacks, so the ones Unity has silently discarded can be found.
        //
        // OnClosed was meant to be the place these get released, and it never runs: Unity 2022.3's
        // MaterialEditor.OnDisable only removes its own undo handler and never calls ShaderGUI.OnClosed
        // (verified by reading its IL in a live editor - the calls are CleanUpPreviewRenderUtility,
        // ReflectionProbePicker.OnDisable, List.Remove and Delegate.Remove, nothing else). So every
        // selection change left the previous ShaderEditor subscribed to Undo.undoRedoEvent, and that
        // subscription kept its entire part tree reachable - ~4300 parts plus the MaterialProperty array of
        // every target - for the rest of the session. A live check found seven such trees for three open
        // inspectors, two of them built for a 13-material selection. The managed heap only grows from
        // there, every collection marks all of it, and the whole editor gets slower no matter what is
        // selected, until the next domain reload. That was the "select many materials, then go back to
        // one, and it stays laggy" report.
        private static readonly List<ShaderEditor> s_editorsWithCallbacks = new List<ShaderEditor>();

        /// <summary>
        /// Releases every registered editor whose MaterialEditor Unity has destroyed. Called when an inspector
        /// refreshes, so a discarded tree lives at most until the next refresh. The cross editor's instances are
        /// skipped here because their MaterialEditor stays alive; CrossEditor releases those itself.
        /// </summary>
        private static void ReleaseOrphanedEditors(ShaderEditor current)
        {
            for (int i = s_editorsWithCallbacks.Count - 1; i >= 0; i--)
            {
                ShaderEditor editor = s_editorsWithCallbacks[i];
                if (editor == current || editor == null) continue;
                if (editor.Editor == null) editor.Release(); // removes it from the list
            }
        }

        private void RegisterCallacks()
        {
            if (_didRegisterCallbacks) return;
            _didRegisterCallbacks = true;
            s_editorsWithCallbacks.Add(this);
            Undo.undoRedoEvent += UndoRedoEvent;
        }

        private void UnregisterCallbacks()
        {
            if (_didRegisterCallbacks == false) return;
            _didRegisterCallbacks = false;
            s_editorsWithCallbacks.Remove(this);
            Undo.undoRedoEvent -= UndoRedoEvent;
        }
        
        private bool HasUndoTargets()
        {
            if (!_didRegisterCallbacks || _doReloadNextDraw || Editor == null || ShaderParts == null || Materials == null || Materials.Length == 0
                || Materials.Any(material => material == null)) return false;
            var targets = Editor.targets;
            return targets != null && targets.Length > 0 && targets.All(target => target != null);
        }

        private void UndoRedoEvent(in UndoRedoInfo undo)
        {
            if (!HasUndoTargets()) return;
            string undoName = undo.undoName ?? "";
            if(undoName.EndsWith(Materials[0].name, StringComparison.Ordinal)
                || undoName.EndsWith("Materials", StringComparison.Ordinal))
            {
                var sourceEditor = Editor;
                var sourceTargets = Editor.targets;
                var sourceShaders = Materials.Select(material => SectionLock.GetSourceShader(material.shader)).ToArray();
                var sourceParts = ShaderParts;
                EditorApplication.delayCall += () =>
                {
                    // The inspector may close, be reused, or lose a secondary
                    // material between Undo and this delayed native-value refresh.
                    if (!HasUndoTargets() || Editor != sourceEditor || !Editor.targets.SequenceEqual(sourceTargets)
                        || !ReferenceEquals(ShaderParts, sourceParts) || !Materials.Select(material => SectionLock.GetSourceShader(material.shader)).SequenceEqual(sourceShaders)) return;
                    bool repaint = false;
                    foreach(ShaderPart part in ShaderParts)
                    {
                        repaint |= part.CheckForValueChange();
                    }
                    if(repaint)
                    {
                        Repaint();
                    }
                };   
            }
        }

        public override void OnClosed(Material material)
        {
            base.OnClosed(material);
            // Closing/replacing the GUI must also destroy its auxiliary editors.
            // Unregistering alone removes it from orphan cleanup while leaving
            // those native objects (and their Undo handlers) alive.
            Release();
            _isFirstOnGUICall = true;
            // A swap that never got drawn is not applied to whatever material this editor is reused for
            _doApplyRenderingPresetAfterSwap = false;
        }

        /// <summary>
        /// Lets go of everything this editor holds beyond its own lifetime: the undo subscription that
        /// otherwise keeps the whole part tree reachable, and the MaterialEditors created per target set
        /// for cross editing, which Unity never destroys on its own. Reached from ReleaseOrphanedEditors
        /// once Unity has destroyed this editor's MaterialEditor (OnClosed is never called for that, see
        /// s_editorsWithCallbacks), from OnClosed when Unity replaces the GUI, and from CrossEditor
        /// when it drops an instance it built itself.
        /// </summary>
        public void Release()
        {
            UnregisterCallbacks();
            _inspectorMount?.Clear();
            foreach (MaterialEditor editor in s_editorCache.Values)
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            s_editorCache.Clear();
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            // Also runs while packages import, when a texture they reference may not exist yet
            TextureKeywords.Sync(material, disable: false);
            WatchForMaterialReset(material, Undo.GetCurrentGroup());
        }

        private const string RESET_MATERIAL_UNDO_NAME = "Reset Material";
        // Unity has named the group by the tick after ValidateMaterial in some resets and later in others,
        // so a single deferred check misses it. A short watch covers both without polling indefinitely.
        private const int RESET_WATCH_FRAMES = 20;
        // Every material a watched group validated, and whether it was locked at the time
        private static readonly Dictionary<int, Dictionary<Material, bool>> _watchedResetGroups = new Dictionary<int, Dictionary<Material, bool>>();
        private static readonly System.Reflection.MethodInfo _getUndoRecords = typeof(Undo).GetMethod("GetRecords",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(List<string>), typeof(List<string>) }, null);
        private static int _lastHandledResetGroup = -1;

        /// <summary>
        /// Runs OnReset and unlocks the reset materials when the undo group this ValidateMaterial belongs to turns
        /// out to be a material reset. Every material in the group is collected, so a multi-material reset is too.
        ///
        /// Unity names that group "Reset Material" only after ValidateMaterial has already run, so reading the
        /// name during the call returns whatever the previous action left behind - typically one of ThryEditor's
        /// own group names, like "Expand Color &amp; Normals of X". The old check compared against that stale name,
        /// so the first reset never cleared the animated tags and a second one had to be performed, which then
        /// matched on the name the first had since set.
        ///
        /// Deliberately still an exact name match rather than anything heuristic: OnReset also unlinks materials
        /// and clears notes, so a false positive destroys user data. Failing to detect a reset is the safe
        /// direction to err in, and is no worse than the previous behaviour.
        /// </summary>
        private void WatchForMaterialReset(Material material, int group)
        {
            if (group == _lastHandledResetGroup) return;
            if (_watchedResetGroups.TryGetValue(group, out Dictionary<Material, bool> watched))
            {
                if (!watched.ContainsKey(material)) watched[material] = material.IsLocked();
                return;
            }
            watched = new Dictionary<Material, bool> { { material, material.IsLocked() } };
            _watchedResetGroups[group] = watched;

            int framesLeft = RESET_WATCH_FRAMES;
            int undoSteps = UndoStepNames().Count;
            EditorApplication.CallbackFunction watcher = null;
            watcher = () =>
            {
                // TextCore's Reset menu, the one materials get, starts a new group before the name is readable, so
                // the steps added since are checked too
                bool isReset = Undo.GetCurrentGroup() == group && Undo.GetCurrentGroupName() == RESET_MATERIAL_UNDO_NAME
                    || UndoStepNames().Skip(undoSteps).Contains(RESET_MATERIAL_UNDO_NAME);

                if (isReset && group != _lastHandledResetGroup)
                {
                    _lastHandledResetGroup = group;
                    if (Active != null && Active.Materials.Any(watched.ContainsKey))
                        Active.OnReset();
                    UnlockResetMaterials(watched.Where(p => p.Value && p.Key != null).Select(p => p.Key).ToArray(), group);
                }

                if (isReset || --framesLeft <= 0 || watched.Keys.All(m => m == null))
                {
                    EditorApplication.update -= watcher;
                    _watchedResetGroups.Remove(group);
                }
            };
            EditorApplication.update += watcher;
        }

        private static List<string> UndoStepNames()
        {
            List<string> undo = new List<string>();
            _getUndoRecords?.Invoke(null, new object[] { undo, new List<string>() });
            return undo;
        }

        // A reset material should be a fresh one on the original shader. Unlocking brings back the stripped
        // textures and keywords, so the reset is repeated afterwards.
        private static void UnlockResetMaterials(Material[] materials, int group)
        {
            if (materials.Length == 0) return;
            Undo.RegisterCompleteObjectUndo(materials, RESET_MATERIAL_UNDO_NAME);
            ShaderOptimizer.UnlockMaterials(materials.Where(m => m.IsLocked()));
            foreach (Material material in materials)
            {
                if (!material.IsLocked()) Unsupported.SmartReset(material);
            }
            Undo.CollapseUndoOperations(group);
        }

        private void OnReset()
        {
            ShaderOptimizer.DeleteTags(Materials);
            for(int i = 0; i < Materials.Length; i++)
            {
                MaterialLinker.UnlinkAll(Materials[i]);
                NoteContainers[i]?.ClearAllNotes();
            }

            _vRCFallbackProperty?.SetPropertyValue((string)_vRCFallbackProperty.PropertyDefaultValue);
            _doReloadNextDraw = true;
        }

        public void UpdatePropertyReferences()
        {
            // A selection mixing shaders is laid out from every shader's declarations, and each part indexes that
            // merged list. Unity's bulk read only knows the first material's shader, so re-read each entry against
            // its own owners instead, or parts would pick up the wrong properties.
            if (IsCrossEditor && Properties != null)
                Properties = Properties.Select(p => MaterialEditor.GetMaterialProperty(p.targets, p.name)).ToArray();
            else
                Properties = MaterialEditor.GetMaterialProperties(Materials);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            // Leaving a section shader is leaving its original.
            oldShader = SectionLock.GetSourceShader(oldShader);

            // Give external tooling a chance to veto the swap before anything is mutated. base.AssignNewShaderToMaterial
            // is what actually assigns the shader, so returning here leaves the material on its current shader entirely.
            if (OnBeforeAssignNewShader != null)
            {
                bool proceed = true;
                try
                {
                    proceed = OnBeforeAssignNewShader(material, oldShader, newShader);
                }
                catch (Exception e)
                {
                    // Never let a misbehaving handler block shader assignment.
                    Debug.LogException(e);
                }

                if (!proceed)
                    return;
            }

            // Park the outgoing shader's rendering preset while its properties are still the live ones. The incoming
            // shader's preset is applied once its properties have been collected, at the end of InitlizeThryUI.
            RenderingPresets.Park(this, material, oldShader);
            _doApplyRenderingPresetAfterSwap = true;
            
            this.ShaderOptimizerProperty = null;
            this.InShaderPresetsProperty = null;
            //Unity sets the render queue to the shader defult when changing shader
            //This seems to be some deeper process that cant be disabled so i just set it again after the swap
            //Even material.shader = newShader resets the queue. (this is actually the only thing the base function does)
            int previousQueue = material.renderQueue;
            //Do the same for the material's own override tags. Otherwise, the VRC Fallback Shader selection is lost.
            Dictionary<string, string> previousTags = MaterialHelper.GetOwnOverrideTags(material, MaterialHelper.TagsPreservedAcrossShaderSwap);
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            material.renderQueue = previousQueue;
            MaterialHelper.ApplyOverrideTags(material, previousTags);
            material.SetOverrideTag(SectionLock.TAG_SECTION_SOURCE, string.Empty);
            SuggestedTranslationDefinition = ShaderTranslator.CheckForExistingTranslationFile(oldShader, newShader);
            FixKeywords(new Material[] { material });
            _doReloadNextDraw = true;
            _didSwapToShader = true;
            SetShader(newShader, oldShader);
        }

        void InitEditorData(MaterialEditor materialEditor)
        {
            Editor = materialEditor;
            TextureArrayProperties = new List<ShaderProperty>();
        }

        private RetainedInspectorMount _inspectorMount;
        private bool _drawsHostNotice;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            if (Event.current.type == EventType.Layout)
            {
                _inspectorMount ??= new RetainedInspectorMount();
                _drawsHostNotice = !_inspectorMount.Show(materialEditor);
                // No view refreshes this editor, so a shader swap is finished here.
                if (_drawsHostNotice && (_didSwapToShader || _doApplyRenderingPresetAfterSwap))
                    PrepareRetained(materialEditor, Array.Empty<Renderer>());
            }
            if (!_drawsHostNotice) return;
            using (new InspectorTheme.Scope())
            {
                GUILayout.Label("The Poiyomi inspector can't be shown in this window.", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Open in Cross Shader Editor", InspectorTheme.Button))
                {
                    Material[] materials = materialEditor.targets.OfType<Material>().ToArray();
                    EditorApplication.delayCall += () => CrossEditor.GetInstance().UpdateTargets(materials.Where(m => m != null));
                }
            }
        }

        internal Func<string, bool> Navigate;
        private string _focusedCategory;
        internal string InspectorTitle => _shaderHeader?.Content.text ?? Shader?.name ?? "Material";
        internal string FocusedCategory => _focusedCategory;
        internal IEnumerable<ShaderGroup> RootCategories => _mainGroup == null
            ? Enumerable.Empty<ShaderGroup>() : _mainGroup.Children.OfType<ShaderGroup>();
        internal bool SupportsRetainedToolbar => _shaderHeader?.Options.texture == null;

        internal void FocusCategory(string propertyName)
        {
            _focusedCategory = propertyName;
            foreach (var group in RootCategories)
                if (group.MaterialProperty?.name == propertyName) group.ExpandForNavigation(true);
        }

        internal void CollapseCategories()
        {
            ActivateRetained();
            var groups = RootCategories.ToList();
            // Walk the model, including children whose UI has not been built yet.
            for (int i = 0; i < groups.Count; i++)
                groups.AddRange(groups[i].Children.OfType<ShaderGroup>());

            var expanded = groups.Where(group => group.RetainedExpanded
                || (group.PersistsExpanded && group.MaterialProperty.hasMixedValue)).ToList();
            bool writesMaterial = !AnimationMode.InAnimationMode()
                && expanded.Any(group => group.PersistsExpanded);
            if (writesMaterial) Editor.RegisterPropertyChangeUndo("Collapse All");
            foreach (var group in expanded) group.SetExpandedFromView(false);
            if (writesMaterial)
            {
                Undo.SetCurrentGroupName("Collapse All");
                Editor.PropertiesChanged();
            }
        }

        internal GenericMenu RetainedToolsMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Fix Keywords"), false, delegate ()
            {
                FixKeywords(Materials);
            });
            menu.AddSeparator("");

            int unboundTextures = MaterialCleaner.CountUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, Materials);
            int unboundProperties = MaterialCleaner.CountAllUnusedProperties(Materials);
            if (unboundTextures > 0 && !IsLockedMaterial)
            {
                menu.AddItem(new GUIContent($"Unbound Textures: {unboundTextures}/List in console"), false, delegate ()
                {
                    MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, Materials);
                });
                menu.AddItem(new GUIContent($"Unbound Textures: {unboundTextures}/Remove"), false, delegate ()
                {
                    MaterialCleaner.RemoveUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, Materials);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent($"Unbound textures: 0"));
            }
            if (unboundProperties > 0 && !IsLockedMaterial)
            {
                menu.AddItem(new GUIContent($"Unbound properties: {unboundProperties}/List in console"), false, delegate ()
                {
                    MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, Materials);
                    MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Float, Materials);
                    MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Color, Materials);
                });
                menu.AddItem(new GUIContent($"Unbound properties: {unboundProperties}/Remove"), false, delegate ()
                {
                    MaterialCleaner.RemoveAllUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, Materials);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent($"Unbound properties: 0"));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Debug/Screenshot Material Inspector"),false, () =>
            {
                InspectorCapture.CaptureActiveInspector(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            });
            menu.AddItem(new GUIContent("Debug/Copy Non-Default Material Settings"), false, () =>
            {
                string materialString = MaterialToDebugString.ConvertMaterialToDebugString(this, true);
                if(string.IsNullOrEmpty(materialString))
                {
                    Debug.LogError($"Failed to copy material settings to clipboard");
                    return;
                }

                EditorGUIUtility.systemCopyBuffer = materialString;
                Debug.Log($"Copied material settings to clipboard:\n{materialString}");
            });
            menu.AddItem(new GUIContent("Debug/Copy All Material Settings"), false, () =>
            {
                string materialString = MaterialToDebugString.ConvertMaterialToDebugString(this, false); 
                if(string.IsNullOrEmpty(materialString))
                {
                    Debug.LogError($"Failed to copy material settings to clipboard");
                    return;
                }

                EditorGUIUtility.systemCopyBuffer = materialString;
                Debug.Log($"Copied material settings to clipboard:\n{materialString}");
            });
            menu.AddItem(new GUIContent("Copy as Text"), false, () =>
            {
                string json = MaterialTextSerializer.Serialize(Materials[0]);
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError("Failed to serialize material to text");
                    return;
                }
                EditorGUIUtility.systemCopyBuffer = json;
                ThryLogger.LogDetail("MaterialTextSerializer", $"Copied material '{Materials[0].name}' as text ({json.Length} chars)");
            });
            menu.AddItem(new GUIContent("Paste from Text"), false, () =>
            {
                string clip = EditorGUIUtility.systemCopyBuffer;
                if (!MaterialTextSerializer.TryDeserialize(clip, out var data))
                {
                    Debug.LogError("Paste from Text: clipboard does not contain a valid Thry material text payload.");
                    return;
                }

                if (!string.IsNullOrEmpty(data.shader) && data.shader != Shader.name) ThryLogger.LogWarn("MaterialTextSerializer", $"Pasting from shader '{data.shader}' onto '{Shader.name}'. Properties that don't exist on the target shader will be skipped.");

                int undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObjects(Materials, "Paste from Text");

                var scratch = new Material(Shader);
                var appliedNames = new HashSet<string>();
                int applied = MaterialTextSerializer.ApplyToMaterial(data, scratch, appliedNames);
                var skipNames = MaterialTextSerializer.GetUnpastedPropertyNames(this, appliedNames);

                // Copying a preset value skips its on_value_actions
                ShaderProperty preset = RenderingPresets.FindPresetProperty(this);
                if (preset != null && appliedNames.Contains(preset.MaterialProperty.name))
                {
                    float pastedPreset = scratch.GetNumber(preset.MaterialProperty);
                    if (preset.MaterialProperty.hasMixedValue || preset.MaterialProperty.GetNumber() != pastedPreset) preset.FloatValue = pastedPreset;
                }

                MaterialTextSerializer.KeepAnimatedTags(this, appliedNames, () =>
                {
                    foreach (var part in ShaderParts)
                    {
                        // Not in the text; copying would reset them to defaults
                        if (part == _renderQueueProperty || part == _vRCFallbackProperty) continue;
                        part.CopyFrom(scratch, skipPropertyTypes: MaterialTextSerializer.SkipTextures, skipPropertyNames: skipNames);
                    }
                });

                UnityEngine.Object.DestroyImmediate(scratch);

                Undo.SetCurrentGroupName($"Paste from Text ({applied} properties)");
                Undo.CollapseUndoOperations(undoGroup);

                ThryLogger.LogDetail("MaterialTextSerializer", $"Pasted {applied} properties from text onto '{Materials[0].name}'");
            });
            menu.AddSeparator("");
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Is Preset"), Presets.IsPreset(Materials[0]), delegate ()
            {
                Presets.SetPreset(Materials, !Presets.IsPreset(Materials[0]));
                this.Reload();
            });
            return menu;
        }

        public static void Out(string s)
        {
            Debug.Log($"<color=#ff80ff>[Thry]</color> {s}");
        }
        public static void Out(string header, params string[] lines)
        {
            Debug.Log($"<color=#ff80ff>[Thry]</color> <b>{header}</b>\n{lines.Aggregate((s1, s2) => s1 + "\n" + s2)}");
        }
        public static void Out(string header, IEnumerable<string> lines)
        {
            if (lines.Count() == 0) Out(header);
            else Debug.Log($"<color=#ff80ff>[Thry]</color> <b>{header}</b>\n{lines.Aggregate((s1, s2) => s1 + "\n" + s2)}");
        }
        public static void Out(string header, Color c, IEnumerable<string> lines)
        {
            if (lines.Count() == 0) Out(header);
            else Debug.Log($"<color=#ff80ff>[Thry]</color> <b><color={ColorUtility.ToHtmlStringRGB(c)}>{header}</b></color> \n{lines.Aggregate((s1, s2) => s1 + "\n" + s2)}");
        }

        public void Repaint()
        {
            if (Materials.Length > 0)
                EditorUtility.SetDirty(Materials[0]);
        }

        public static void RepaintActive()
        {
            if (ShaderEditor.Active != null)
                Active.Repaint();
        }

        public void Reload(bool doRepaint = false)
        {
            this._isFirstOnGUICall = true;
            this._doReloadNextDraw = true;
            if (doRepaint)
            {
                this.Repaint();
            }
            ThryRGBAPackerDrawer.Reload();
        }

        public static void ReloadActive()
        {
            if (ShaderEditor.Active != null)
                Active.Reload();
        }

        public void ApplyDrawers()
        {
            foreach (Material target in Materials)
                MaterialEditor.ApplyMaterialPropertyDrawers(target);
        }

        // Cache property->keyword lookup for performance
        static Dictionary<Shader, List<(string prop, List<string> keywords, bool inverted)>> PropertyKeywordsByShader = new Dictionary<Shader, List<(string prop, List<string> keywords, bool inverted)>>();

        internal static void ClearPropertyKeywordCache() => PropertyKeywordsByShader.Clear();

        /// <summary> Iterate through all materials to ensure keywords list matches properties. </summary>
        public static void FixKeywords(IEnumerable<Material> materialsToFix)
        {
            // Process Shaders
            // Section shaders share their original's properties, so they share its keyword map too.
            IEnumerable<Material> uniqueShadersMaterials = materialsToFix.GroupBy(m => SectionLock.GetSourceShader(m.shader)).Select(g => g.First());
            IEnumerable<Shader> shadersWithThryEditor = uniqueShadersMaterials.Where(m => ShaderHelper.IsShaderUsingThryEditor(m)).Select(m => SectionLock.GetSourceShader(m.shader));

            // Clear cache every time if in developer mode, so that changes aren't missed
            if(Config.Instance.enableDeveloperMode)
                PropertyKeywordsByShader.Clear();

            float f = 0;
            int count = shadersWithThryEditor.Count();

            if(count > 1) EditorUtility.DisplayProgressBar("Validating Keywords", "Processing Shaders", 0);

            foreach (Shader s in shadersWithThryEditor)
            {
                if(count > 1) EditorUtility.DisplayProgressBar("Validating Keywords", $"Processing Shader: {s.name}", f++ / count);
                if(!PropertyKeywordsByShader.ContainsKey(s))
                    PropertyKeywordsByShader[s] = ShaderHelper.GetPropertyKeywordsForShader(s);
            }
            // Find Materials
            IEnumerable<Material> materials = materialsToFix.Where(m => PropertyKeywordsByShader.ContainsKey(SectionLock.GetSourceShader(m.shader)));
            f = 0;
            count = materials.Count();

            // Set Keywords
            foreach(Material m in materials)
            {
                if(count > 1) EditorUtility.DisplayProgressBar("Validating Keywords", $"Validating Material: {m.name}", f++ / count);

                List<string> keywordsInMaterial = m.shaderKeywords.ToList();

                Shader source = SectionLock.GetSourceShader(m.shader);
                foreach((string prop, List<string> keywords, bool inverted) in PropertyKeywordsByShader[source])
                {
                    switch(keywords.Count)
                    {
                        case 0:
                            break;
                        case 1:
                            string keyword = keywords[0];
                            keywordsInMaterial.Remove(keyword);

                            // If the prop is float (toggle), GetFloat works; if it's texture, use has texture
                            int propIndex = source.FindPropertyIndex(prop);
                            if (propIndex >= 0 && source.GetPropertyType(propIndex) == ShaderPropertyType.Texture)
                            {
                                bool hasTexture = m.GetTexture(prop) != null;
                                if (hasTexture) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
                            }
                            else
                            {
                                float value = GetKeywordPropertyValue(m, source, prop);
                                if(inverted ? value == 0 : value == 1) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
                            }
                            break;
                        default: // KeywordEnum
                            for (int i = 0; i < keywords.Count; i++)
                            {
                                keywordsInMaterial.Remove(keywords[i]);
                                if (GetKeywordPropertyValue(m, source, prop) == i)
                                    m.EnableKeyword(keywords[i]);
                                else
                                    m.DisableKeyword(keywords[i]);
                            }
                            break;
                    }
                }

                // Disable any remaining keywords
                foreach(string keyword in keywordsInMaterial)
                    m.DisableKeyword(keyword);
            }
            if(count > 1) EditorUtility.ClearProgressBar();
        }

        static float GetKeywordPropertyValue(Material m, Shader source, string prop)
        {
            int index = source.FindPropertyIndex(prop);
            return index >= 0 && source.GetPropertyType(index) == ShaderPropertyType.Int ? m.GetInteger(prop) : m.GetFloat(prop);
        }

        [MenuItem("Poi/Thry/Twitter", priority = -100)]
        static void MenuThryTwitter()
        {
            Application.OpenURL("https://www.twitter.com/thryrallo");
        }

        [MenuItem("Poi/Thry/Texture Packer", priority = 1)]
        static void MenuTexturePacker()
        {
            ThryEditor.TexturePacker.NodeGUI.Open();
        }

        [MenuItem("Poi/Thry/Cross Shader Editor", priority = 2)]
        public static void ShowWindow()
        {
            CrossEditor.GetInstance();
        }

        [MenuItem("Poi/Thry/Material Lock Manager", priority = 3)]
        static void MenuShaderOptUnlockedMaterials()
        {
            EditorWindow.GetWindow<UnlockedMaterialsList>(false, "Material Lock Manager", true);
        }

        [MenuItem("Poi/Thry/ThryEditor/Settings",priority = 4)]
        static void MenuShaderUISettings()
        {
            EditorWindow.GetWindow<Settings>(false, "Thry Settings", true);
        }

        /// <summary> Iterate through all materials with FixKeywords. </summary>
        [MenuItem("Poi/Thry/ThryEditor/Fix Keywords for All Materials (Slow)", priority = 50)]
        static void FixAllKeywords()
        {
            IEnumerable<Material> materialsToFix = AssetDatabase.FindAssets("t:material")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => string.IsNullOrEmpty(p) == false)
                .Select(p => AssetDatabase.LoadAssetAtPath<Material>(p))
                .Where(m => m != null && !m.shader.IsBroken() && !m.IsLocked());

            FixKeywords(materialsToFix);
        }

        [MenuItem("Poi/Thry/ThryEditor/Upgraded Animated Properties", priority = 50)]
        static void MenuUpgradeAnimatedPropertiesToTagsOnAllMaterials()
        {
            ShaderOptimizer.UpgradeAnimatedPropertiesToTagsOnAllMaterials();
        }
        
        [MenuItem("Poi/Thry/ThryEditor/Rebuild Presets Cache", priority = 50)]
        static void RebuildCache()
        {
            Presets.RebuildCache();
        }

        [MenuItem("Assets/Thry/Materials/Cleaner/List Unbound Properties", priority = 303)]
        static void AssetsCleanMaterials_ListUnboundProperties()
        {
            IEnumerable<Material> materials = Selection.objects.Where(o => o is Material).Select(o => o as Material);
            foreach (Material m in materials)
            {
                Debug.Log("_______Unbound Properties for " + m.name + "_______");
                MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, m);
                MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Color, m);
                MaterialCleaner.ListUnusedProperties(MaterialCleaner.CleanPropertyType.Float, m);
            }
        }

        [MenuItem("Assets/Thry/Materials/Cleaner/Remove Unbound Textures", priority = 303)]
        static void AssetsCleanMaterials_CleanUnboundTextures()
        {
            IEnumerable<Material> materials = Selection.objects.Where(o => o is Material).Select(o => o as Material);
            foreach (Material m in materials)
            {
                Debug.Log("_______Removing Unbound Textures for " + m.name + "_______");
                MaterialCleaner.RemoveUnusedProperties(MaterialCleaner.CleanPropertyType.Texture, m);
            }
        }
    }
}
