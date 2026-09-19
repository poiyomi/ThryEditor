using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    // Text values remain usable outside this inspector; texture references are session-local.
    internal static class RetainedPropertyClipboard
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static string textureToken;
        static Texture texture;
        static bool nullTexture;

        internal static void AddMenu(GenericMenu menu, RetainedMaterialModel model, ShaderProperty property)
        {
            var p = property.MaterialProperty;
            menu.AddSeparator("");
            if (!p.hasMixedValue)
                menu.AddItem(new GUIContent("Copy Value"), false, () => Copy(property.MaterialProperty));
            else menu.AddDisabledItem(new GUIContent("Copy Value (mixed values)"));
            AddPaste(menu, "Paste Value", model, property, false);
            if (p.GetPropertyType() == ShaderPropertyType.Color)
            {
                if (!p.hasMixedValue)
                    menu.AddItem(new GUIContent("Copy Color as Hex (RGBA)"), false,
                        () => EditorGUIUtility.systemCopyBuffer = "#" + ColorUtility.ToHtmlStringRGBA(property.MaterialProperty.colorValue));
                else menu.AddDisabledItem(new GUIContent("Copy Color as Hex (RGBA)"));
            }
            if (p.GetPropertyType() == ShaderPropertyType.Texture)
            {
                if ((p.flags & MaterialProperty.PropFlags.NoScaleOffset) == 0)
                {
                    if (!p.hasMixedValue)
                        menu.AddItem(new GUIContent("Copy Tiling and Offset"), false,
                            () => EditorGUIUtility.systemCopyBuffer = Format("Vector4", property.MaterialProperty.textureScaleAndOffset));
                    else menu.AddDisabledItem(new GUIContent("Copy Tiling and Offset"));
                    AddPaste(menu, "Paste Tiling and Offset", model, property, true);
                }
                if (model.CanEdit(property))
                    menu.AddItem(new GUIContent("Clear Texture"), false, () => model.Edit(property, target =>
                    {
                        target.textureValue = null;
                        Drawers.ThryRGBAPackerDrawer.ClearPendingPreview(target);
                    }));
                else menu.AddDisabledItem(new GUIContent("Clear Texture"));
            }
        }

        static void AddPaste(GenericMenu menu, string label, RetainedMaterialModel model, ShaderProperty property, bool transform)
        {
            if (model.CanEdit(property) && TryRead(property.MaterialProperty, transform, out _))
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    // Revalidate both the clipboard and editability when an open menu is used.
                    if (TryRead(property.MaterialProperty, transform, out var apply)) model.Edit(property, apply);
                });
            else menu.AddDisabledItem(new GUIContent(label));
        }

        internal static void Copy(MaterialProperty p)
        {
            if (p == null || p.hasMixedValue) return;
            switch (p.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: EditorGUIUtility.systemCopyBuffer = p.floatValue.ToString("R", Invariant); break;
                case ShaderPropertyType.Int: EditorGUIUtility.systemCopyBuffer = p.intValue.ToString(Invariant); break;
                case ShaderPropertyType.Color: EditorGUIUtility.systemCopyBuffer = Format("RGBA", p.colorValue); break;
                case ShaderPropertyType.Vector: EditorGUIUtility.systemCopyBuffer = Format("Vector4", p.vectorValue); break;
                case ShaderPropertyType.Texture:
                    texture = p.textureValue; nullTexture = texture == null;
                    textureToken = "ThryTexture:" + Guid.NewGuid().ToString("N");
                    EditorGUIUtility.systemCopyBuffer = textureToken;
                    break;
            }
        }

        static string Format(string prefix, Vector4 value) => prefix + "(" + value.x.ToString("R", Invariant) + ", "
            + value.y.ToString("R", Invariant) + ", " + value.z.ToString("R", Invariant) + ", " + value.w.ToString("R", Invariant) + ")";

        static bool Number(string text, out float value) => float.TryParse(text, NumberStyles.Float, Invariant, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value);

        static bool Components(string text, string prefix, out Vector4 value)
        {
            value = default;
            if (!text.StartsWith(prefix + "(", StringComparison.Ordinal) || !text.EndsWith(")", StringComparison.Ordinal)) return false;
            var parts = text.Substring(prefix.Length + 1, text.Length - prefix.Length - 2).Split(',');
            if (parts.Length != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                if (!Number(parts[i], out float component)) return false;
                value[i] = component;
            }
            return true;
        }

        internal static bool TryRead(MaterialProperty p, bool transform, out Action<MaterialProperty> apply)
        {
            apply = null;
            if (p == null) return false;
            string text = (EditorGUIUtility.systemCopyBuffer ?? "").Trim();
            if (transform)
            {
                if (p.GetPropertyType() != ShaderPropertyType.Texture || (p.flags & MaterialProperty.PropFlags.NoScaleOffset) != 0
                    || !Components(text, "Vector4", out var st)) return false;
                apply = target => target.textureScaleAndOffset = st;
                return true;
            }
            switch (p.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    if (!Number(text, out float number)) return false;
                    apply = target => target.floatValue = target.GetPropertyType() == ShaderPropertyType.Range
                        ? Mathf.Clamp(number, target.rangeLimits.x, target.rangeLimits.y) : number;
                    break;
                case ShaderPropertyType.Int:
                    if (!int.TryParse(text, NumberStyles.Integer, Invariant, out int integer)) return false;
                    apply = target => target.intValue = integer;
                    break;
                case ShaderPropertyType.Color:
                    Color color;
                    if (Components(text, "RGBA", out var rgba) || Components(text, "Color", out rgba)) color = rgba;
                    else if (!text.StartsWith("#", StringComparison.Ordinal) || !ColorUtility.TryParseHtmlString(text, out color)) return false;
                    apply = target => target.colorValue = color;
                    break;
                case ShaderPropertyType.Vector:
                    if (!Components(text, "Vector4", out var vector)) return false;
                    apply = target => target.vectorValue = vector;
                    break;
                case ShaderPropertyType.Texture:
                    if (textureToken == null || text != textureToken || (!nullTexture && texture == null)
                        || (texture != null && texture.dimension != p.textureDimension)) return false;
                    var copiedTexture = texture;
                    apply = target =>
                    {
                        target.textureValue = copiedTexture;
                        Drawers.ThryRGBAPackerDrawer.ClearPendingPreview(target);
                    };
                    break;
                default: return false;
            }
            return true;
        }
    }
}
