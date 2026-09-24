using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Reference properties are drawn outside the ordinary group tree. Cache their
    // membership per owner, including collapsed sections and shared references.
    internal static class ShaderAnimationSummary
    {
        private sealed class Index
        {
            internal List<ShaderPart> Parts;
            internal ILookup<string, ShaderProperty> Properties;
            internal readonly Dictionary<ShaderGroup, List<KeyValuePair<ShaderProperty, Material>>> Groups
                = new Dictionary<ShaderGroup, List<KeyValuePair<ShaderProperty, Material>>>();
        }

        private static readonly ConditionalWeakTable<ShaderEditor, Index> Indices = new ConditionalWeakTable<ShaderEditor, Index>();

        internal static void Invalidate(ShaderEditor shader)
        {
            foreach (var group in shader.ShaderParts.OfType<ShaderGroup>()) group.SetAnimatedDescendantStateDirty();
        }

        internal static void Resolve(ShaderGroup group, out bool animated, out bool renamed)
        {
            var shader = group.MyShaderUI;
            var index = Indices.GetValue(shader, _ => new Index());
            if (!ReferenceEquals(index.Parts, shader.ShaderParts))
            {
                index.Parts = shader.ShaderParts;
                index.Properties = shader.ShaderParts.OfType<ShaderProperty>().Where(p => p.MaterialProperty != null)
                    .ToLookup(p => p.MaterialProperty.name);
                index.Groups.Clear();
            }
            if (!index.Groups.TryGetValue(group, out var properties))
            {
                properties = new List<KeyValuePair<ShaderProperty, Material>>();
                foreach (var owner in shader.Materials)
                    Collect(group, owner, index, new HashSet<ShaderPart>(), properties);
                index.Groups.Add(group, properties);
            }
            animated = renamed = false;
            foreach (var entry in properties)
            {
                var property = entry.Key;
                property.EnsureAnimatedStateResolved();
                if (!property.IsAnimatable || entry.Value == null) continue;
                string tag = property.GetOwnerAnimatedTag(entry.Value);
                renamed |= tag == "2";
                animated |= tag != "" && tag != "2";
            }
        }

        private static void Collect(ShaderPart part, Material owner, Index index, HashSet<ShaderPart> visited,
            List<KeyValuePair<ShaderProperty, Material>> properties)
        {
            if (owner == null || part.MaterialProperty != null && !part.MaterialProperty.targets.Contains(owner)
                || !visited.Add(part)) return;
            part.EnsureOptionsInitialized();
            if (part is ShaderProperty property && property.MaterialProperty != null)
                properties.Add(new KeyValuePair<ShaderProperty, Material>(property, owner));
            if (part is ShaderGroup group)
                foreach (var child in group.Children) Collect(child, owner, index, visited, properties);
            if (part.Options.reference_property != null)
                foreach (var reference in index.Properties[part.Options.reference_property]) Collect(reference, owner, index, visited, properties);
            if (part.Options.reference_properties != null)
                foreach (var name in part.Options.reference_properties)
                    if (name != null)
                        foreach (var reference in index.Properties[name]) Collect(reference, owner, index, visited, properties);
        }
    }
}
