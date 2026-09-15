using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    // A button in the property list, driven by the same ButtonData json footers and header buttons
    // use. LocalMessage does the same with a clickable label instead.
    public class ThryButtonDrawer : MaterialPropertyDrawer
    {
        private const int ButtonHeight = 22;

        private ButtonData _buttonData;
        private bool _isInit;

        private void Init(string s)
        {
            if (_isInit) return;
            _buttonData = Parser.Deserialize<ButtonData>(s);
            _isInit = true;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            Init(prop.displayName);
            if (_buttonData == null || !_buttonData.condition_show.Test()) return;

            GUIContent content = new GUIContent(_buttonData.text, _buttonData.texture?.loaded_texture, _buttonData.hover);

            // Indent from the property itself, so the button fits whatever section it sits in.
            int xOffset = ShaderEditor.Active?.CurrentProperty?.XOffset ?? 0;
            Rect r = GUILib.GetPropertyRect(xOffset, ButtonHeight);

            // Centred means sized to content rather than filling the row.
            if (_buttonData.center_position)
            {
                float width = Mathf.Min(GUI.skin.button.CalcSize(content).x, r.width);
                r.x += (r.width - width) / 2;
                r.width = width;
            }

            // Every draw, not once at parse: the dictionary is rebuilt with the inspector and the
            // shader can be swapped under the button.
            string missing = FindMissingProperty();
            if (missing != null)
                content.tooltip = $"Property '{missing}' is not on this shader";

            using (new EditorGUI.DisabledScope(missing != null))
            {
                if (GUI.Button(r, content) && _buttonData.action != null)
                {
                    ShaderEditor.Input.Use();
                    Material[] materials = ShaderEditor.Active?.Materials;
                    if (_buttonData.action.Perform(materials) is IThryButtonTarget target)
                        target.SetButtonContext(_buttonData.args ?? new string[0], materials);
                }
            }
        }

        // First args name missing from the shader, or null if they all resolve.
        private string FindMissingProperty()
        {
            if (_buttonData.args == null || ShaderEditor.Active == null) return null;

            foreach (string name in _buttonData.args)
            {
                if (!ShaderEditor.Active.PropertyDictionary.ContainsKey(name)) return name;
            }
            return null;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            // The host property only carries the json, so animating it would drive nothing.
            ShaderProperty.DisallowAnimation();
            return 0;
        }
    }
}
