#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    // Keep the expensive declaration merge separate from ordinary material value refreshes.
    internal sealed class RetainedCrossSelectionProperties
    {
        private sealed class TargetGroup
        {
            internal UnityEngine.Object[] Targets;
            internal readonly List<int> Indices = new List<int>();
            internal readonly Dictionary<string, MaterialProperty> Values = new Dictionary<string, MaterialProperty>();
            internal SourceGroup[] Sources;
            internal SourceGroup HomogeneousSource;
        }

        private sealed class SourceGroup
        {
            internal UnityEngine.Object[] Targets;
            internal int[] TargetIndices;
            internal bool Dirty;
            internal readonly Dictionary<string, MaterialProperty> Values = new Dictionary<string, MaterialProperty>();
            internal readonly Dictionary<string, PropertySnapshot> Snapshots = new Dictionary<string, PropertySnapshot>();
            internal readonly HashSet<string> ChangedNames = new HashSet<string>();
        }

        private struct PropertySnapshot
        {
            internal object Value;
            internal Vector4 TextureTransform;
            internal bool Mixed;

            internal static PropertySnapshot Capture(MaterialProperty property)
            {
                return new PropertySnapshot { Value = MaterialHelper.GetValue(property), Mixed = property.hasMixedValue,
                    TextureTransform = property.GetPropertyType() == ShaderPropertyType.Texture ? property.textureScaleAndOffset : Vector4.zero };
            }

            internal bool SameValue(PropertySnapshot other)
            {
                return Equals(Value, other.Value) && TextureTransform.Equals(other.TextureTransform) && Mixed == other.Mixed;
            }
        }

        private readonly MaterialEditor _editor;
        private readonly ShaderEditor _shader;
        private Material[] _targets = Array.Empty<Material>();
        private Shader[] _shaders = Array.Empty<Shader>();
        private int[] _dirtyCounts = Array.Empty<int>();
        private MaterialProperty[] _properties;
        private readonly List<TargetGroup> _groups = new List<TargetGroup>();
        private readonly List<SourceGroup> _sources = new List<SourceGroup>();
        internal int BuildCount { get; private set; }

        internal RetainedCrossSelectionProperties(MaterialEditor editor, ShaderEditor shader)
        { _editor = editor; _shader = shader; }

        internal MaterialProperty[] Read()
        {
            if (!RetainedMaterialModel.HasValidTargets(_editor)) return Array.Empty<MaterialProperty>();
            var targets = _editor.targets.Cast<Material>().ToArray();
            bool changed = targets.Length != _targets.Length;
            for (int i = 0; !changed && i < targets.Length; i++)
                changed = targets[i] != _targets[i] || targets[i].shader != _shaders[i];
            if (changed)
            {
                _targets = targets;
                _shaders = targets.Select(m => m.shader).ToArray();
                _dirtyCounts = new int[targets.Length];
                _properties = null; _groups.Clear(); _sources.Clear();
                // Unsupported materials stay visible in the selection list, but never enter a
                // native property request for this inspector's shader controls.
                var compatible = targets.Where(m => ShaderHelper.IsShaderUsingThryEditor(m)).ToArray();
                if (compatible.Length != targets.Length || compatible.Select(m => m.shader).Distinct().Skip(1).Any())
                {
                    _properties = compatible.Length == 0 ? Array.Empty<MaterialProperty>() : CrossEditor.CollectProperties(_shader, compatible);
                    BuildCount++;
                    var sources = new Dictionary<Shader, SourceGroup>();
                    foreach (var shaderTargets in compatible.GroupBy(m => m.shader))
                    {
                        var source = new SourceGroup { Targets = shaderTargets.Cast<UnityEngine.Object>().ToArray(),
                            TargetIndices = shaderTargets.Select(m => Array.IndexOf(_targets, m)).ToArray() };
                        sources.Add(shaderTargets.Key, source); _sources.Add(source);
                        ReadSource(source, baseline: true);
                    }
                    var groups = new Dictionary<string, TargetGroup>();
                    for (int i = 0; i < _properties.Length; i++)
                    {
                        var owners = _properties[i].targets;
                        string key = string.Join(",", owners.Select(o => o.GetInstanceID()));
                        if (!groups.TryGetValue(key, out var group))
                        {
                            var ownerSources = owners.Cast<Material>().Select(m => sources[m.shader]).Distinct().ToArray();
                            group = new TargetGroup { Targets = owners, Sources = ownerSources };
                            if (ownerSources.Length == 1 && ownerSources[0].Targets.SequenceEqual(owners)) group.HomogeneousSource = ownerSources[0];
                            groups.Add(key, group); _groups.Add(group);
                        }
                        group.Indices.Add(i);
                    }
                    RecordDirtyCounts();
                }
                _shader.IsCrossEditor = _properties != null;
                // A secondary target can swap shaders without changing the inspector's first target.
                _shader.Reload();
            }
            if (_properties == null) return MaterialEditor.GetMaterialProperties(targets);

            bool valuesChanged = false;
            for (int i = 0; i < _targets.Length; i++)
                valuesChanged |= _dirtyCounts[i] != EditorUtility.GetDirtyCount(_targets[i]);
            if (valuesChanged)
            {
                // Unity's bulk API assumes every target uses the first target's shader. Read
                // homogeneous sources only, then resolve changed shared names across shaders.
                foreach (var source in _sources)
                {
                    source.ChangedNames.Clear();
                    source.Dirty = source.TargetIndices.Any(i => _dirtyCounts[i] != EditorUtility.GetDirtyCount(_targets[i]));
                    if (source.Dirty) ReadSource(source, baseline: false);
                }
                foreach (var group in _groups)
                {
                    if (!group.Sources.Any(source => source.Dirty)) continue;
                    group.Values.Clear();
                    foreach (int index in group.Indices)
                    {
                        string name = _properties[index].name;
                        if (group.HomogeneousSource != null && group.HomogeneousSource.Values.TryGetValue(name, out var homogeneous))
                        { _properties[index] = homogeneous; continue; }
                        if (!group.Sources.Any(source => source.ChangedNames.Contains(name))) continue;
                        if (!group.Values.TryGetValue(name, out var value))
                        {
                            value = MaterialEditor.GetMaterialProperty(group.Targets, name);
                            group.Values.Add(name, value);
                        }
                        _properties[index] = value;
                    }
                }
                RecordDirtyCounts();
            }
            return _properties;
        }

        private static void ReadSource(SourceGroup source, bool baseline)
        {
            source.Values.Clear();
            foreach (var property in MaterialEditor.GetMaterialProperties(source.Targets))
            {
                if (source.Values.ContainsKey(property.name)) continue;
                source.Values.Add(property.name, property);
                var snapshot = PropertySnapshot.Capture(property);
                // A mixed aggregate exposes only its first value. A secondary material can
                // change while that value and the mixed flag stay unchanged, so refresh it.
                if (!baseline && (!source.Snapshots.TryGetValue(property.name, out var previous)
                    || !snapshot.SameValue(previous) || snapshot.Mixed || previous.Mixed))
                    source.ChangedNames.Add(property.name);
                source.Snapshots[property.name] = snapshot;
            }
        }

        private void RecordDirtyCounts()
        {
            for (int i = 0; i < _targets.Length; i++) _dirtyCounts[i] = EditorUtility.GetDirtyCount(_targets[i]);
        }
    }
}
#endif
