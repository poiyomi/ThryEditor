#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    internal static class RetainedValueDetails
    {
        internal static string Describe(ShaderProperty property)
        {
            var shader = property.MyShaderUI;
            var targets = shader.Materials.Where(m => m != null && m.HasProperty(property.MaterialProperty.name)).ToArray();
            string current = targets.Length == 1
                ? RetainedMultiMaterial.DisplayValue(RetainedMultiMaterial.Value(targets[0], property.MaterialProperty))
                : string.Join("\n", targets.Select(m => RetainedMultiMaterial.MaterialName(m) + ": " + RetainedMultiMaterial.DisplayValue(RetainedMultiMaterial.Value(m, property.MaterialProperty))));
            string defaults = RetainedMultiMaterial.DisplayValue(property.PropertyDefaultValue);
            if (property.MaterialProperty.type == MaterialProperty.PropType.Texture) defaults += " · tiling (1, 1) · offset (0, 0)";
            return RetainedText.Get(shader, "current", "Current") + ": " + current + "\n"
                + RetainedText.Get(shader, "default", "Default") + ": " + defaults;
        }

        internal static string ResetLabel(ShaderPart part)
        {
            if (part is ShaderGroup) return RetainedText.Get(part.MyShaderUI, "reset_section", "Reset entire section (including children)");
            if (part is ShaderTextureProperty) return RetainedText.Get(part.MyShaderUI, "reset_texture", "Reset texture, tiling and offset");
            if (part?.AdditionalDefaultCheckProperties?.Length > 0) return RetainedText.Get(part.MyShaderUI, "reset_linked_values", "Reset value and related controls");
            return RetainedText.Get(part?.MyShaderUI, "reset_value", "Reset value");
        }
    }
}
#endif
