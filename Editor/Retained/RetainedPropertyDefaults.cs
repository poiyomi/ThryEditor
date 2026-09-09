#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    // A mixed aggregate can contain several correct defaults. Compare the actual owners
    // to their own declarations, sharing the result's semantics between dots and search.
    internal static class RetainedPropertyDefaults
    {
        private sealed class DefaultValue
        {
            internal ShaderPropertyType Type;
            internal float Number;
            internal int Integer;
            internal Vector4 Vector;
            internal Texture Texture;
        }
        private sealed class Cache
        {
            internal int Revision = -1;
            internal readonly Dictionary<Shader, Dictionary<string, DefaultValue>> Shaders = new Dictionary<Shader, Dictionary<string, DefaultValue>>();
            internal readonly Dictionary<Shader, int> ShaderVersions = new Dictionary<Shader, int>();
            internal readonly Dictionary<Material, OwnerChecks> Owners = new Dictionary<Material, OwnerChecks>();
            internal readonly Dictionary<ShaderProperty, Material[]> PropertyOwners = new Dictionary<ShaderProperty, Material[]>();
            internal readonly Dictionary<ShaderProperty, Comparison> Aggregates = new Dictionary<ShaderProperty, Comparison>();
            internal Material[] Selection = Array.Empty<Material>();
            internal ILookup<string, ShaderProperty> References;
            internal int Depth, ReadCount, AggregateReadCount, SnapshotReadCount, SnapshotRevision = -1;
            internal bool Compare, SnapshotsFresh;
            internal MaterialProperty[] SnapshotProperties;
            internal Material[] SnapshotSelection = Array.Empty<Material>();
            internal readonly Dictionary<Material, SnapshotOwner> SnapshotOwners = new Dictionary<Material, SnapshotOwner>();
            internal readonly Dictionary<Shader, int> SnapshotShaders = new Dictionary<Shader, int>();
        }
        private sealed class SnapshotOwner
        {
            internal int Dirty;
            internal Shader Shader;
            internal Material Parent;
        }
        private sealed class Comparison
        {
            internal bool? Value, Transform;
        }
        private sealed class OwnerChecks
        {
            internal int Dirty;
            internal Shader Shader;
            internal Material[] Parents = Array.Empty<Material>();
            internal int[] ParentVersions = Array.Empty<int>();
            internal readonly Dictionary<string, Comparison> Properties = new Dictionary<string, Comparison>();
            internal readonly HashSet<Texture> Dependencies = new HashSet<Texture>();
        }
        private sealed class Evaluation : IDisposable
        {
            private Cache _cache;
            internal Evaluation(Cache cache) { _cache = cache; cache.Depth++; }
            public void Dispose() { if (_cache == null) return; _cache.Depth--; _cache = null; }
        }
        private static readonly ConditionalWeakTable<ShaderEditor, Cache> Caches = new ConditionalWeakTable<ShaderEditor, Cache>();

        private static Cache GetCache(ShaderEditor shader)
        {
            var cache = Caches.GetValue(shader, _ => new Cache());
            if (cache.Revision == shader.RetainedRevision) return cache;
            cache.Shaders.Clear(); cache.ShaderVersions.Clear(); cache.Owners.Clear(); cache.PropertyOwners.Clear();
            cache.Aggregates.Clear();
            cache.SnapshotRevision = -1; cache.SnapshotsFresh = false;
            cache.References = null; cache.Revision = shader.RetainedRevision;
            return cache;
        }

        // Cached comparisons are available only while rendering a synchronized batch.
        // Direct callers (including editing commands) always read current native values.
        internal static IDisposable BeginEvaluation(ShaderEditor shader)
        {
            var cache = GetCache(shader);
            if (cache.Depth == 0)
            {
                cache.Compare = !AnimationMode.InAnimationMode();
                cache.SnapshotsFresh = cache.Compare && SnapshotsMatch(cache, shader);
                if (!cache.Compare) { cache.Owners.Clear(); cache.PropertyOwners.Clear(); cache.Aggregates.Clear(); }
                var selection = shader.Materials.Where(material => material != null).Distinct().ToArray();
                if (!cache.Selection.SequenceEqual(selection))
                {
                    cache.Selection = selection; cache.Owners.Clear(); cache.PropertyOwners.Clear(); cache.Aggregates.Clear();
                }
                var seenShaders = new HashSet<Shader>();
                var parentVersions = new Dictionary<Material, int>();
                foreach (var material in selection)
                {
                    var ownerShader = material.shader;
                    if (ownerShader == null) { cache.Owners.Remove(material); cache.PropertyOwners.Clear(); cache.Aggregates.Clear(); continue; }
                    if (seenShaders.Add(ownerShader)) ValidateShader(cache, ownerShader);
                    int dirty = EditorUtility.GetDirtyCount(material);
                    if (!cache.Owners.TryGetValue(material, out var checks))
                        cache.Owners.Add(material, checks = new OwnerChecks());
                    if (checks.Shader != ownerShader) cache.PropertyOwners.Clear();
                    bool ancestorsChanged = UpdateParents(material, checks, parentVersions);
                    if (checks.Dirty != dirty || checks.Shader != ownerShader || ancestorsChanged || checks.Dependencies.Any(texture => texture == null))
                    { checks.Properties.Clear(); checks.Dependencies.Clear(); cache.Aggregates.Clear(); }
                    checks.Shader = ownerShader; checks.Dirty = dirty;
                }
            }
            return new Evaluation(cache);
        }

        // Call only after the model has refreshed Unity's MaterialProperty array.
        // The array may be reused in place, so identity alone never proves freshness.
        internal static void MarkSnapshotsFresh(ShaderEditor shader)
        {
            var cache = GetCache(shader);
            if (SnapshotsMatch(cache, shader)) return;
            cache.SnapshotProperties = shader.Properties;
            cache.SnapshotRevision = shader.RetainedRevision;
            cache.SnapshotSelection = shader.Materials.Where(material => material != null).Distinct().ToArray();
            cache.SnapshotOwners.Clear(); cache.SnapshotShaders.Clear();
            foreach (var selected in cache.SnapshotSelection)
                for (var material = selected; material != null && !cache.SnapshotOwners.ContainsKey(material); material = Parent(material))
                {
                    var ownerShader = material.shader;
                    cache.SnapshotOwners.Add(material, new SnapshotOwner { Dirty = EditorUtility.GetDirtyCount(material),
                        Shader = ownerShader, Parent = Parent(material) });
                    if (ownerShader != null && !cache.SnapshotShaders.ContainsKey(ownerShader))
                        cache.SnapshotShaders.Add(ownerShader, EditorUtility.GetDirtyCount(ownerShader));
                }
        }

        private static Material Parent(Material material)
        {
#if UNITY_2022_1_OR_NEWER
            return material.parent;
#else
            return null;
#endif
        }

        private static bool SnapshotsMatch(Cache cache, ShaderEditor shader)
        {
            if (cache.SnapshotProperties == null || cache.SnapshotRevision != shader.RetainedRevision
                || !ReferenceEquals(cache.SnapshotProperties, shader.Properties)
                || !cache.SnapshotSelection.SequenceEqual(shader.Materials.Where(material => material != null).Distinct())) return false;
            foreach (var entry in cache.SnapshotOwners)
                if (entry.Key == null || entry.Value.Dirty != EditorUtility.GetDirtyCount(entry.Key)
                    || entry.Value.Shader != entry.Key.shader || entry.Value.Parent != Parent(entry.Key)) return false;
            foreach (var entry in cache.SnapshotShaders)
                if (entry.Key == null || entry.Value != EditorUtility.GetDirtyCount(entry.Key)) return false;
            return true;
        }

        private static MaterialProperty Snapshot(ShaderProperty property)
        {
            var cache = ActiveCache(property);
            if (cache == null || !cache.SnapshotsFresh) return null;
            int index = property.ThryPropertyIndex;
            if (index < 0 || index >= cache.SnapshotProperties.Length
                || !ReferenceEquals(cache.SnapshotProperties[index], property.MaterialProperty)
                || property.MaterialProperty.hasMixedValue) return null;
            cache.SnapshotReadCount++;
            return property.MaterialProperty;
        }

        private static void ValidateShader(Cache cache, Shader shader)
        {
            int version = EditorUtility.GetDirtyCount(shader);
            if (cache.ShaderVersions.TryGetValue(shader, out int previous) && previous != version)
            {
                cache.Shaders.Remove(shader); cache.PropertyOwners.Clear(); cache.Aggregates.Clear();
                foreach (var owner in cache.Owners.Values.Where(owner => owner.Shader == shader))
                { owner.Properties.Clear(); owner.Dependencies.Clear(); }
            }
            cache.ShaderVersions[shader] = version;
        }

        private static bool UpdateParents(Material material, OwnerChecks checks, Dictionary<Material, int> versions)
        {
#if UNITY_2022_1_OR_NEWER
            if (material.parent != null)
            {
                var parents = new List<Material>(); var dirty = new List<int>();
                for (var parent = material.parent; parent != null; parent = parent.parent)
                {
                    if (!versions.TryGetValue(parent, out int version)) versions.Add(parent, version = EditorUtility.GetDirtyCount(parent));
                    parents.Add(parent); dirty.Add(version);
                }
                if (checks.Parents.SequenceEqual(parents) && checks.ParentVersions.SequenceEqual(dirty)) return false;
                checks.Parents = parents.ToArray(); checks.ParentVersions = dirty.ToArray(); return true;
            }
#endif
            if (checks.Parents.Length == 0) return false;
            checks.Parents = Array.Empty<Material>(); checks.ParentVersions = Array.Empty<int>(); return true;
        }

        internal static int EvaluationReadCount(ShaderEditor shader) => GetCache(shader).ReadCount;
        internal static int EvaluationAggregateReadCount(ShaderEditor shader) => GetCache(shader).AggregateReadCount;
        internal static int EvaluationSnapshotReadCount(ShaderEditor shader) => GetCache(shader).SnapshotReadCount;

        private static Cache ActiveCache(ShaderProperty property)
        {
            if (property?.MyShaderUI == null || !Caches.TryGetValue(property.MyShaderUI, out var cache)
                || cache.Depth == 0 || !cache.Compare) return null;
            return cache;
        }

        private static IEnumerable<Material> EvaluationOwners(ShaderProperty property)
        {
            var cache = ActiveCache(property);
            if (cache == null) return Owners(property);
            if (!cache.PropertyOwners.TryGetValue(property, out var owners))
                cache.PropertyOwners.Add(property, owners = Owners(property).ToArray());
            return owners;
        }

        private static bool CompareOwner(ShaderProperty property, Material material, bool transform)
        {
            var cache = ActiveCache(property);
            if (cache == null || !cache.Owners.TryGetValue(material, out var owner))
                return transform ? ReadTransform(property, material) : ReadValue(property, material);
            string name = property.MaterialProperty.name;
            if (!owner.Properties.TryGetValue(name, out var comparison))
                owner.Properties.Add(name, comparison = new Comparison());
            bool? result = transform ? comparison.Transform : comparison.Value;
            if (result.HasValue) return result.Value;
            cache.ReadCount++;
            bool changed = transform ? ReadTransform(property, material) : ReadValue(property, material, owner.Dependencies);
            if (transform) comparison.Transform = changed; else comparison.Value = changed;
            return changed;
        }

        internal static IEnumerable<Material> Owners(ShaderProperty property)
        {
            if (property?.MaterialProperty == null) yield break;
            foreach (var target in property.MaterialProperty.targets)
                if (target is Material material && material != null && material.shader != null
                    && property.MyShaderUI.Materials.Contains(material) && material.HasProperty(property.MaterialProperty.name))
                    yield return material;
        }

        private static Dictionary<string, DefaultValue> Defaults(ShaderProperty property, Shader shader)
        {
            var cache = GetCache(property.MyShaderUI);
            if (cache.Depth == 0 || !cache.Compare) ValidateShader(cache, shader);
            if (cache.Shaders.TryGetValue(shader, out var values)) return values;
            values = new Dictionary<string, DefaultValue>();
            // One temporary material captures Unity's color/default-texture interpretation.
            // Never retain native temporary materials for the lifetime of an inspector.
            var defaults = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(shader)) as ShaderImporter;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i);
                    if (values.ContainsKey(name)) continue;
                    var value = new DefaultValue { Type = shader.GetPropertyType(i) };
                    switch (value.Type)
                    {
                        case ShaderPropertyType.Texture: value.Texture = importer?.GetDefaultTexture(name) ?? defaults.GetTexture(name); break;
                        case ShaderPropertyType.Vector: value.Vector = defaults.GetVector(name); break;
                        case ShaderPropertyType.Color: value.Vector = defaults.GetColor(name); break;
#if UNITY_2022_1_OR_NEWER
                        case ShaderPropertyType.Int: value.Integer = defaults.GetInteger(name); break;
#endif
                        default: value.Number = defaults.GetFloat(name); break;
                    }
                    values.Add(name, value);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(defaults); }
            cache.Shaders.Add(shader, values);
            return values;
        }

        internal static bool HasChangedValue(ShaderProperty property) => HasChangedValue(property, null);
        private static bool HasChangedValue(ShaderProperty property, HashSet<Material> scope)
            => CompareProperty(property, scope, false);

        private static bool CompareProperty(ShaderProperty property, HashSet<Material> scope, bool transform)
        {
            if (property?.MaterialProperty == null) return false;
            var cache = scope == null ? ActiveCache(property) : null;
            Comparison aggregate = null;
            if (cache != null)
            {
                if (!cache.Aggregates.TryGetValue(property, out aggregate)) cache.Aggregates.Add(property, aggregate = new Comparison());
                bool? result = transform ? aggregate.Transform : aggregate.Value;
                if (result.HasValue) return result.Value;
                cache.AggregateReadCount++;
            }
            bool changed = false;
            foreach (var material in EvaluationOwners(property))
            {
                if (scope != null && !scope.Contains(material)) continue;
                if (CompareOwner(property, material, transform)) { changed = true; break; }
            }
            if (aggregate != null)
            {
                if (transform) aggregate.Transform = changed; else aggregate.Value = changed;
            }
            return changed;
        }

        private static bool ReadValue(ShaderProperty property, Material material, HashSet<Texture> dependencies = null)
        {
            string name = property.MaterialProperty.name;
            if (!Defaults(property, material.shader).TryGetValue(name, out var value)) return false;
            var snapshot = Snapshot(property);
            switch (value.Type)
            {
                // Null means use the shader's default. Compare assigned objects, never names.
                case ShaderPropertyType.Texture:
                    var assigned = snapshot != null ? snapshot.textureValue : material.GetTexture(name);
                    if (assigned != null) dependencies?.Add(assigned);
                    if (assigned != null && assigned != value.Texture) return true;
                    break;
                case ShaderPropertyType.Color: if ((Vector4)(snapshot != null ? snapshot.colorValue : material.GetColor(name)) != value.Vector) return true; break;
                case ShaderPropertyType.Vector: if ((snapshot != null ? snapshot.vectorValue : material.GetVector(name)) != value.Vector) return true; break;
#if UNITY_2022_1_OR_NEWER
                case ShaderPropertyType.Int: if ((snapshot != null ? snapshot.intValue : material.GetInteger(name)) != value.Integer) return true; break;
#endif
                default: if ((snapshot != null ? snapshot.floatValue : material.GetFloat(name)) != value.Number) return true; break;
            }
            return false;
        }

        private static bool ReadTransform(ShaderProperty property, Material material)
        {
            // hasMixedValue describes the texture assignment and can remain false
            // when owners have different scale/offset values. Its first-owner ST
            // snapshot is therefore not an aggregate transform snapshot.
            return material.GetTextureScale(property.MaterialProperty.name) != Vector2.one
                || material.GetTextureOffset(property.MaterialProperty.name) != Vector2.zero;
        }

        internal static bool HasChangedTextureTransform(ShaderProperty property) => HasChangedTextureTransform(property, null);
        private static bool HasChangedTextureTransform(ShaderProperty property, HashSet<Material> scope)
        {
            if (property?.MaterialProperty == null || property.MaterialProperty.GetPropertyType() != ShaderPropertyType.Texture) return false;
            return CompareProperty(property, scope, true);
        }

        internal static bool HasChanged(ShaderProperty property, ShaderEditor shader, HashSet<ShaderProperty> visited = null)
            => HasChangedWithinOwners(property, shader, visited, null);

        private static bool HasChangedWithinOwners(ShaderProperty property, ShaderEditor shader, HashSet<ShaderProperty> visited, HashSet<Material> scope)
        {
            if (property == null || (visited != null && visited.Contains(property))) return false;
            if (HasChangedValue(property, scope) || HasChangedTextureTransform(property, scope)) return true;
            if (property.AdditionalDefaultCheckProperties == null || property.AdditionalDefaultCheckProperties.Length == 0) return false;
            visited = visited ?? new HashSet<ShaderProperty>(); visited.Add(property);
            var owners = new HashSet<Material>(EvaluationOwners(property).Where(material => scope == null || scope.Contains(material)));
            var cache = GetCache(shader);
            if (cache.References == null) cache.References = shader.ShaderParts.OfType<ShaderProperty>()
                .Where(p => p.MaterialProperty != null).ToLookup(p => p.MaterialProperty.name);
            try
            {
                foreach (string name in property.AdditionalDefaultCheckProperties)
                    foreach (var reference in cache.References[name])
                        if (HasChangedWithinOwners(reference, shader, visited, owners)) return true;
                return false;
            }
            finally { visited.Remove(property); }
        }
    }
}
#endif
