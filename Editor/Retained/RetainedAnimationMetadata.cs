using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Numeric edits dirty a material without changing its animation tags. Read the
    // small tag map once when dirty, instead of asking every property on every owner.
    internal sealed class RetainedAnimationMetadata : IDisposable
    {
        sealed class Entry : IDisposable
        {
            internal int Dirty;
            internal Shader Shader;
            internal int ShaderDirty;
            internal string Name, Tags;
            internal Material Parent;
            internal SerializedObject Serialized;
            internal SerializedProperty TagMap;

            public void Dispose()
            {
                TagMap?.Dispose(); TagMap = null;
                Serialized?.Dispose(); Serialized = null;
            }
        }
        readonly Dictionary<Material, Entry> _entries = new Dictionary<Material, Entry>();
        internal bool Update(Material[] materials, MaterialEditor editor = null)
        {
            bool changed = false;
            var seen = new HashSet<Material>();
            foreach (var owner in materials)
                for (var material = owner; material != null && seen.Add(material);)
                {
                    Material parent = null;
                    parent = material.parent;
                    int dirty = EditorUtility.GetDirtyCount(material);
                    var shader = material.shader;
                    int shaderDirty = shader == null ? 0 : EditorUtility.GetDirtyCount(shader);
                    string name = material.name;
                    if (!_entries.TryGetValue(material, out var entry) || entry.Dirty != dirty
                        || entry.Shader != shader || entry.ShaderDirty != shaderDirty || entry.Name != name || entry.Parent != parent)
                    {
                        bool created = entry == null;
                        if (created) { entry = new Entry(); _entries.Add(material, entry); }
                        // The single-material header already needs this same serialized
                        // snapshot. Sharing it avoids serializing every Pro property twice.
                        var shared = editor != null && editor.targets.Length == 1 && editor.target == material
                            ? editor.serializedObject : null;
                        // Do not discard an extension's pending serialized edits.
                        if (shared != null && shared.hasModifiedProperties) shared = null;
                        string tags = ReadTags(material, entry, shared);
                        changed |= created || entry.Tags != tags || entry.Shader != shader
                            || entry.ShaderDirty != shaderDirty || entry.Name != name || entry.Parent != parent;
                        entry.Dirty = dirty; entry.Shader = shader; entry.ShaderDirty = shaderDirty;
                        entry.Name = name; entry.Tags = tags; entry.Parent = parent;
                    }
                    material = parent;
                }
            if (_entries.Count != seen.Count)
            {
                var removed = new List<Material>();
                foreach (var material in _entries.Keys) if (!seen.Contains(material)) removed.Add(material);
                foreach (var material in removed) { _entries[material].Dispose(); _entries.Remove(material); }
                changed = true;
            }
            return changed;
        }

        public void Dispose()
        {
            foreach (var entry in _entries.Values) entry.Dispose();
            _entries.Clear();
        }

        static string ReadTags(Material material, Entry entry, SerializedObject shared)
        {
            if (shared != null)
            {
                shared.UpdateIfRequiredOrScript();
                using (var tags = shared.FindProperty("stringTagMap")) return ReadTags(material, tags);
            }
            if (entry.Serialized == null) entry.Serialized = new SerializedObject(material);
            entry.Serialized.UpdateIfRequiredOrScript();
            if (entry.TagMap == null) entry.TagMap = entry.Serialized.FindProperty("stringTagMap");
            return ReadTags(material, entry.TagMap);
        }

        static string ReadTags(Material material, SerializedProperty tags)
        {
            // An unknown serialization schema must never silently suppress updates.
            if (tags == null) return Guid.NewGuid().ToString();
            var entries = new List<string>(tags.arraySize);
            for (int i = 0; i < tags.arraySize; i++)
            {
                string key;
                using (var element = tags.GetArrayElementAtIndex(i)) key = element.displayName;
                string value = material.GetTag(key, false, "");
                entries.Add(key.Length + ":" + key + value.Length + ":" + value);
            }
            entries.Sort(StringComparer.Ordinal);
            var result = new StringBuilder();
            foreach (string value in entries) result.Append(value);
            return result.ToString();
        }
    }
}
