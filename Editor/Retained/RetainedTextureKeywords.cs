#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    // TextureKeyword is a behavior-only decorator whose legacy implementation runs
    // on Repaint. Repair only those derived flags when a material actually changes.
    internal sealed class RetainedTextureKeywords
    {
        private struct Binding
        {
            internal string Property, Keyword;
        }
        private readonly Dictionary<Material,List<Binding>> _bindings = new Dictionary<Material,List<Binding>>();
        private int _revision = -1;
        internal int MetadataBuildCount { get; private set; }
        internal int LastMaterialCheckCount { get; private set; }

        internal void Synchronize(ShaderEditor shader, Material[] changedMaterials)
        {
            LastMaterialCheckCount = 0;
            if (_revision != shader.RetainedRevision)
            {
                _bindings.Clear();
                var metadata = new Dictionary<Shader,Dictionary<string,string[]>>();
                foreach (var property in shader.ShaderParts.OfType<ShaderProperty>())
                {
                    if (property.MaterialProperty == null || property.MaterialProperty.type != UnityEditor.MaterialProperty.PropType.Texture) continue;
                    string name = property.MaterialProperty.name;
                    foreach (var material in property.MaterialProperty.targets.OfType<Material>().Where(m => m != null && shader.Materials.Contains(m)))
                    {
                        Dictionary<string,string[]> properties;
                        if (!metadata.TryGetValue(material.shader,out properties)) metadata[material.shader] = properties = new Dictionary<string,string[]>();
                        string[] keywords;
                        if (!properties.TryGetValue(name,out keywords))
                        {
                            int index = material.shader.FindPropertyIndex(name);
                            keywords = index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Texture ? Array.Empty<string>() :
                                material.shader.GetPropertyAttributes(index).Select(a => new DrawerAttribute(a)).Where(a => a.Name == "TextureKeyword")
                                .Select(a => a.Args.Length == 0 || string.IsNullOrEmpty(a.Args[0]) ? "PROP_" + name.TrimStart('_').ToUpperInvariant() : a.Args[0]).Distinct().ToArray();
                            properties[name] = keywords;
                        }
                        List<Binding> bindings;
                        if (!_bindings.TryGetValue(material,out bindings)) _bindings[material] = bindings = new List<Binding>();
                        foreach (var keyword in keywords)
                            if (!bindings.Any(b => b.Property == name && b.Keyword == keyword)) bindings.Add(new Binding { Property = name, Keyword = keyword });
                    }
                }
                _revision = shader.RetainedRevision; MetadataBuildCount++;
            }
            foreach (var material in changedMaterials)
            {
                List<Binding> bindings;
                if (material == null || !_bindings.TryGetValue(material,out bindings) || bindings.Count == 0) continue;
                LastMaterialCheckCount++;
                foreach (var binding in bindings)
                {
                    bool desired = material.GetTexture(binding.Property) != null;
                    if (material.IsKeywordEnabled(binding.Keyword) == desired) continue;
                    if (desired) material.EnableKeyword(binding.Keyword); else material.DisableKeyword(binding.Keyword);
                }
            }
        }
    }
}
#endif
