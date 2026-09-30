using System;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    /// 
    /// THIS IS A NEAR-COPY OF IT'S SISTER SCRIPT AND IS PURELY DECORATIVE!
    /// Only intended to be placed above a ThryMultiFloatHeaderDrawer as a decorative element.
    /// THIS DRAWER HAS NO PHYSICAL PROPERTIES!
    /// 
    public class ThryMultiFloatHeaderDrawer : MaterialPropertyDrawer
    {
        readonly string[] _labels = new string[4];

        public ThryMultiFloatHeaderDrawer(string label0, string label1, string label2, string label3)
        {
            _labels[0] = label0;
            _labels[1] = label1;
            _labels[2] = label2;
            _labels[3] = label3;
        }

        static GUIStyle s_centered;

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            Rect fieldR = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            const float spacing = 4f;
            float colWidth = (fieldR.width - spacing * 3f) / 4f;

            if (s_centered == null)
                s_centered = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip,
                    wordWrap = false
                };
            var centered = s_centered;

            for (int i = 0; i < 4; i++)
            {
                Rect r = new Rect(fieldR.x + i * (colWidth + spacing), fieldR.y, colWidth, fieldR.height);
                GUI.Label(r, _labels[i] ?? string.Empty, centered);
            }
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor) => EditorGUIUtility.singleLineHeight;
    }
}