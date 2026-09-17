using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    /// <summary>
    /// An HDR-capable picker without ShaderLab's HDR flag. Ordinary Color properties
    /// keep their existing gamma-to-linear conversion, including when optimized.
    /// Do not replace an existing [HDR] flag with this drawer: that changes material data interpretation.
    /// </summary>
    public class ThryHDRDrawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = prop.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            Color value = EditorGUI.ColorField(position, label, prop.colorValue, true, true, true);
            if (EditorGUI.EndChangeCheck())
                prop.colorValue = value;
            EditorGUI.showMixedValue = previousMixedValue;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return base.GetPropertyHeight(prop, label, editor);
        }
    }
}
