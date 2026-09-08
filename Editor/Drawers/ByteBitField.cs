using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Thry.ThryEditor.Helpers;

namespace Thry.ThryEditor.Drawers
{
    public class ByteBitFieldDrawer : MaterialPropertyDrawer
    {
        private const float ToggleSize = 20f;
        private const float ToggleSpacing = 2f;
        private const float BottomPadding = 1f;
        private const float ErrorLines = 2.5f;

        // Reject-list rather than allow-list: ShaderPropertyType.Int only exists on newer Unity
        // versions, which is why Unity.cs guards every mention of it behind a version check.
        private static bool IsNumericProperty(MaterialProperty prop)
        {
            ShaderPropertyType propertyType = prop.GetPropertyType();
            return propertyType != ShaderPropertyType.Color
                && propertyType != ShaderPropertyType.Vector
                && propertyType != ShaderPropertyType.Texture;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            if (!IsNumericProperty(prop))
            {
                EditorGUI.HelpBox(position, "[ByteBitField] requires a numeric property, e.g. Range(0, 255)", MessageType.Warning);
                return;
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float currentY = position.y;
            float startX = position.x;
            float width = position.width;

            int currentValue = Mathf.Clamp(
                (int)prop.GetNumber(),
                StencilOperationsHelper.ByteMin,
                StencilOperationsHelper.ByteMax);

            GUIContent prefix = string.IsNullOrEmpty(label?.text)
                ? new GUIContent(EditorLocale.editor.Get("stencil_bit_representation"))
                : label;
            Rect controlRect = EditorGUI.PrefixLabel(new Rect(startX, currentY, width, lineHeight), GUIUtility.GetControlID(FocusType.Passive), prefix);

            float availableStride = Mathf.Max(controlRect.width / StencilOperationsHelper.BitsPerByte, 0f);
            float toggleWidth = Mathf.Max(0f, Mathf.Min(ToggleSize, availableStride - ToggleSpacing));
            float toggleStride = Mathf.Min(toggleWidth + ToggleSpacing, availableStride);
            int newValue = 0;
            // EditorGUI.Toggle, not GUI.Toggle: only the EditorGUI controls honour showMixedValue.
            // Its rects are indent-adjusted a second time, so zero the indent for the row the way
            // ThryRGBAPacker does - PrefixLabel already applied the indent to controlRect.
            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUI.showMixedValue = prop.hasMixedValue;
            for (int i = StencilOperationsHelper.BitsPerByte - 1; i >= 0; i--)
            {
                bool bit = ((currentValue >> i) & 1) == 1;
                float toggleX = controlRect.x
                    + (StencilOperationsHelper.BitsPerByte - 1 - i) * toggleStride;
                bool newBit = EditorGUI.Toggle(new Rect(toggleX, currentY + (lineHeight - 16) / 2, toggleWidth, 16), bit);
                if (newBit) newValue |= 1 << i;
            }
            EditorGUI.showMixedValue = false;
            EditorGUI.indentLevel = previousIndent;

            if (newValue != currentValue)
                prop.SetNumber(newValue);
        }

        public float GetHeight()
        {
            return EditorGUIUtility.singleLineHeight + BottomPadding;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            if (!IsNumericProperty(prop))
                return EditorGUIUtility.singleLineHeight * ErrorLines;
            ShaderProperty.RegisterDrawer(this);
            return GetHeight();
        }
    }
}
