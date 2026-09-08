using Thry.ThryEditor;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    public class Vector31Drawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            string[] labels = label.text.Split('|');

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;
            Vector4 vec = GUILib.VectorField(position, new GUIContent(labels[0]), prop.vectorValue, 3);
            Rect scalar = EditorGUILayout.GetControlRect(false, InspectorTheme.FieldHeight);
            scalar.x = position.x; scalar.width = position.width;
            float single = EditorGUI.FloatField(scalar, labels.Length > 1 ? labels[1] : labels[0], prop.vectorValue.w);
            if (EditorGUI.EndChangeCheck())
            {
                prop.vectorValue = new Vector4(vec.x, vec.y, vec.z, single);
            }
            EditorGUI.showMixedValue = false;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return base.GetPropertyHeight(prop, label, editor);
        }
    }

}
