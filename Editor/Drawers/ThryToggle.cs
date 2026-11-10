using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Drawers
{
    public class ThryToggleDrawer : MaterialPropertyDrawer
    {
        public string keyword;
        private bool hasKeyword = false;

        public ThryToggleDrawer()
        {
        }

        //the reason for weird string thing here is that you cant have bools as params for drawers
        public ThryToggleDrawer(string keywordLeft)
        {
            if (!keywordLeft.Equals("true", StringComparison.OrdinalIgnoreCase) && !keywordLeft.Equals("false", StringComparison.OrdinalIgnoreCase))
                keyword = keywordLeft;
            hasKeyword = keyword != null;
        }

        public ThryToggleDrawer(string keyword, string left)
        {
            this.keyword = keyword;
            hasKeyword = keyword != null;
        }

        protected void SetKeyword(MaterialProperty prop, bool on)
        {
            if ((prop.targets[0] as Material).IsLocked()) return;
            SetKeywordInternal(prop, on, "_ON");
        }

        static bool IsPropertyTypeSuitable(MaterialProperty prop)
        {
            return prop.GetPropertyType() == ShaderPropertyType.Float
                   || prop.GetPropertyType() == ShaderPropertyType.Range
                   || prop.GetPropertyType() == ShaderPropertyType.Int;
            ;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            if (!IsPropertyTypeSuitable(prop)) return;
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;
            bool value = EditorGUI.Toggle(position, label, Math.Abs(prop.GetNumber()) > 0.001f);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck()) return;
            prop.SetNumber(value ? 1.0f : 0.0f);
            if (hasKeyword) SetKeyword(prop, value);
        }

        public override void Apply(MaterialProperty prop)
        {
            base.Apply(prop);
            if (!IsPropertyTypeSuitable(prop))
                return;

            if (prop.hasMixedValue)
                return;

            if (hasKeyword) SetKeyword(prop, (Math.Abs(prop.GetNumber()) > 0.001f));
        }

        protected void SetKeywordInternal(MaterialProperty prop, bool on, string defaultKeywordSuffix)
        {
            // if no keyword is provided, use <uppercase property name> + defaultKeywordSuffix
            string kw = string.IsNullOrEmpty(keyword) ? prop.name.ToUpperInvariant() + defaultKeywordSuffix : keyword;
            // set or clear the keyword
            foreach (Material material in prop.targets)
            {
                if (on)
                    material.EnableKeyword(kw);
                else
                    material.DisableKeyword(kw);
            }
        }
    }

}
