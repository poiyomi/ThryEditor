#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Only immutable declaration data is shared. Material values, options, drawers,
    // animation state and GUI controls remain owned by their individual inspectors.
    internal sealed class RetainedShaderDeclarations
    {
        internal sealed class Declaration
        {
            readonly string[] _attributes;
            internal readonly bool IsAnimatable = true;
            internal readonly string Keyword;
            internal readonly string[] TextureKeywords;

            internal Declaration(Shader shader, int index)
            {
                _attributes = shader.GetPropertyAttributes(index);
                var textureKeywords = new List<string>();
                foreach (var source in _attributes)
                {
                    if (source.StartsWith("DoNotAnimate", StringComparison.Ordinal)
                        || source.StartsWith("ThryStencil", StringComparison.Ordinal)
                        || source.StartsWith("ThryShaderOptimizer", StringComparison.Ordinal)
                        || source.StartsWith("TextureKeyword", StringComparison.Ordinal)) IsAnimatable = false;
                    if (source.StartsWith("ThryToggle(", StringComparison.Ordinal)
                        || source.StartsWith("ThryToggleUI(", StringComparison.Ordinal))
                    {
                        var attribute = new DrawerAttribute(source);
                        if (attribute.Args.Length > 0 && attribute.Args[0] != "true" && attribute.Args[0] != "false")
                        { Keyword = attribute.Args[0]; IsAnimatable = false; }
                    }
                    if (source == "TextureKeyword" || source.StartsWith("TextureKeyword(", StringComparison.Ordinal))
                    {
                        var attribute = new DrawerAttribute(source);
                        string keyword = attribute.Args.Length == 0 || string.IsNullOrEmpty(attribute.Args[0])
                            ? "PROP_" + shader.GetPropertyName(index).TrimStart('_').ToUpperInvariant() : attribute.Args[0];
                        if (!textureKeywords.Contains(keyword)) textureKeywords.Add(keyword);
                    }
                }
                TextureKeywords = textureKeywords.ToArray();
            }

            // ShaderPart exposes its attributes to subclasses. Give each part its
            // own array so a custom drawer cannot modify another inspector's schema.
            internal string[] CopyAttributes() => (string[])_attributes.Clone();
        }

        static readonly ConditionalWeakTable<Shader, RetainedShaderDeclarations> Cache
            = new ConditionalWeakTable<Shader, RetainedShaderDeclarations>();
        RetainedShaderSchema _schema;
        readonly Dictionary<int, Declaration> _properties = new Dictionary<int, Declaration>();

        internal static Declaration Get(Shader shader, int index)
        {
            var entry = Cache.GetValue(shader, _ => new RetainedShaderDeclarations());
            var schema = RetainedShaderSchema.Read(shader);
            if (!schema.Equals(entry._schema)) { entry._properties.Clear(); entry._schema = schema; }
            if (!entry._properties.TryGetValue(index, out var declaration))
                entry._properties.Add(index, declaration = new Declaration(shader, index));
            return declaration;
        }
    }
}
#endif
