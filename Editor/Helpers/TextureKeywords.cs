using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    // Unlocked shaders only sample a [TextureKeyword] texture while its keyword is on, so the keyword has to
    // follow the texture wherever it changes, not only in the inspector.
    [InitializeOnLoad]
    internal static class TextureKeywords
    {
        sealed class Bindings
        {
            internal RetainedShaderSchema Schema;
            internal (int Id, string Property, string[] Keywords)[] Textures;
        }

        static readonly ConditionalWeakTable<Shader, Bindings> s_bindings = new ConditionalWeakTable<Shader, Bindings>();

        static TextureKeywords()
        {
            // Undoable edits from scripts, presets and links. Imports, undo and redo reach ShaderEditor.ValidateMaterial.
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var change);
                if (EditorUtility.InstanceIDToObject(change.instanceId) is Material material) Sync(material);
            }
        }

        static Bindings Get(Shader shader)
        {
            var schema = RetainedShaderSchema.Read(shader);
            if (s_bindings.TryGetValue(shader, out var bindings) && bindings.Schema.Equals(schema)) return bindings;
            var textures = new List<(int, string, string[])>();
            if (ShaderHelper.IsShaderUsingThryEditor(shader))
            {
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    var keywords = RetainedShaderDeclarations.Get(shader, i).TextureKeywords;
                    if (keywords.Length > 0) textures.Add((shader.GetPropertyNameId(i), shader.GetPropertyName(i), keywords));
                }
            }
            s_bindings.Remove(shader);
            s_bindings.Add(shader, bindings = new Bindings { Schema = schema, Textures = textures.ToArray() });
            return bindings;
        }

        internal static (int Id, string Property, string[] Keywords)[] Textures(Shader shader) => Get(shader).Textures;

        /// <summary>Sets the material's texture keywords from its textures. Locked materials are left alone.</summary>
        internal static void Sync(Material material, string property = null)
        {
            if (material == null) return;
            Shader source = SectionLock.GetSourceShader(material);
            if (source == null) return;
            var textures = Get(source).Textures;
            if (textures.Length == 0 || material.IsLocked()) return;
            string[] enabled = null;
            foreach (var texture in textures)
            {
                if (property != null && texture.Property != property) continue;
                if (!material.HasProperty(texture.Id)) continue;
                bool assigned = material.GetTexture(texture.Id) != null;
                foreach (string keyword in texture.Keywords)
                {
                    // shaderKeywords also lists keywords a section shader doesn't declare
                    if (enabled == null) enabled = material.shaderKeywords;
                    if (Array.IndexOf(enabled, keyword) >= 0 == assigned) continue;
                    if (assigned) material.EnableKeyword(keyword);
                    else material.DisableKeyword(keyword);
                }
            }
        }

        internal static void Sync(IEnumerable<UnityEngine.Object> targets, string property = null)
        {
            if (targets == null) return;
            foreach (var target in targets)
                if (target is Material material) Sync(material, property);
        }
    }

    // Materials that arrive by git, a copy or an avatar package keep the keywords they were saved with, and Unity
    // only validates materials from .unitypackage imports.
    internal sealed class TextureKeywordImports : AssetPostprocessor
    {
        static readonly Regex ShaderGuid = new Regex(@"^  m_Shader: \{fileID: -?\d+, guid: (\w+)", RegexOptions.Multiline);
        static readonly Regex Parent = new Regex(@"^  m_Parent: \{fileID: (-?\d+)", RegexOptions.Multiline);
        static readonly Regex Slot = new Regex(@"^    - (\w+):\r?\n        m_Texture: \{fileID: (-?\d+)", RegexOptions.Multiline);
        static readonly Regex Word = new Regex(@"\w+");

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            string[] paths = importedAssets.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (paths.Length > 0) EditorApplication.delayCall += () => Repair(paths);
        }

        static void Repair(string[] paths)
        {
            var changed = new List<Material>();
            foreach (string path in paths)
            {
                if (!NeedsSync(path)) continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) continue;
                int dirty = EditorUtility.GetDirtyCount(material);
                TextureKeywords.Sync(material);
                if (EditorUtility.GetDirtyCount(material) != dirty && AssetDatabase.IsOpenForEdit(material)) changed.Add(material);
            }
            if (changed.Count == 0) return;
            AssetDatabase.StartAssetEditing();
            try { foreach (var material in changed) AssetDatabase.SaveAssetIfDirty(material); }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        // Reads the file, so materials whose keywords already match never load their textures.
        static bool NeedsSync(string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            var guid = ShaderGuid.Match(text);
            if (!guid.Success) return false;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid.Groups[1].Value));
            if (shader == null) return false;
            var textures = TextureKeywords.Textures(shader);
            if (textures.Length == 0 || ShaderOptimizer.IsShaderLocked(shader)) return false;
            // A variant's file only holds its overrides
            var parent = Parent.Match(text);
            if (parent.Success && parent.Groups[1].Value != "0") return true;
            // Older files list keywords in m_ShaderKeywords, newer ones in m_ValidKeywords and m_InvalidKeywords
            int start = text.IndexOf("m_ValidKeywords:", StringComparison.Ordinal);
            if (start < 0) start = text.IndexOf("m_ShaderKeywords:", StringComparison.Ordinal);
            int end = start < 0 ? -1 : text.IndexOf("m_LightmapFlags:", start, StringComparison.Ordinal);
            int properties = text.IndexOf("m_SavedProperties:", StringComparison.Ordinal);
            if (end < 0 || properties < 0) return true;
            var keywords = new HashSet<string>(Word.Matches(text.Substring(start, end - start)).Cast<Match>().Select(m => m.Value));
            var assigned = new HashSet<string>(Slot.Matches(text, properties).Cast<Match>().Where(m => m.Groups[2].Value != "0").Select(m => m.Groups[1].Value));
            return textures.Any(texture => texture.Keywords.Any(keyword => keywords.Contains(keyword) != assigned.Contains(texture.Property)));
        }
    }
}
