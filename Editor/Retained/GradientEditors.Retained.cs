#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class GradientEditor2
    {
        public void CreateGUI()
        {
            if (_gradient == null) return;
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            var title = new Label("Gradient"); title.AddToClassList("thry-title"); root.Add(title);
            var gradient = new GradientField { value = _gradient }; gradient.style.height = 80; root.Add(gradient);
            gradient.RegisterValueChangedCallback(e => _gradient = e.newValue);
            if (_allowSizeSelection)
            {
                var size = new Vector2IntField("Texture size") { value = _textureSize }; root.Add(size);
                size.RegisterValueChangedCallback(e => { _textureSize = new Vector2Int(Mathf.Clamp(e.newValue.x, Mathf.Max(1, _textureSizeMin.x), Mathf.Max(1, _textureSizeMax.x)), Mathf.Clamp(e.newValue.y, Mathf.Max(1, _textureSizeMin.y), Mathf.Max(1, _textureSizeMax.y))); size.SetValueWithoutNotify(_textureSize); });
            }
            root.Add(new Button(Apply) { text = "Apply gradient" });
        }
    }

    public partial class GradientEditor
    {
        public void CreateGUI()
        {
            if (_data == null || _prop == null) return;
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            var title = new Label("Gradient"); title.AddToClassList("thry-title"); root.Add(title);
            var field = new GradientField { value = _data.Gradient }; field.style.height = 80; root.Add(field);
            field.RegisterValueChangedCallback(e => { _data.Gradient = e.newValue; UpdateGradientPreviewTexture(); });
            if (_show_texture_options)
            {
                var size = new Vector2IntField("Texture size") { value = new Vector2Int(textureSettings.width, textureSettings.height) }; root.Add(size);
                size.RegisterValueChangedCallback(e => { textureSettings.width = Mathf.Clamp(e.newValue.x, 1, 8192); textureSettings.height = Mathf.Clamp(e.newValue.y, 1, 8192); size.SetValueWithoutNotify(new Vector2Int(textureSettings.width, textureSettings.height)); UpdateGradientPreviewTexture(); });
                var filter = new DropdownField("Filtering", Enum.GetNames(typeof(FilterMode)).ToList(), (int)textureSettings.filterMode); RetainedWindow.Dropdown(filter); root.Add(filter);
                filter.RegisterValueChangedCallback(e => { textureSettings.filterMode = (FilterMode)filter.index; UpdateGradientPreviewTexture(); });
                var wrap = new DropdownField("Wrap", Enum.GetNames(typeof(TextureWrapMode)).ToList(), (int)textureSettings.wrapMode); RetainedWindow.Dropdown(wrap); root.Add(wrap);
                wrap.RegisterValueChangedCallback(e => { textureSettings.wrapMode = (TextureWrapMode)wrap.index; UpdateGradientPreviewTexture(); });
                var aniso = new SliderInt("Anisotropic filtering", 0, 16) { value = textureSettings.ansioLevel, showInputField = true }; root.Add(aniso);
                aniso.RegisterValueChangedCallback(e => { textureSettings.ansioLevel = e.newValue; UpdateGradientPreviewTexture(); });
            }
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); root.Add(actions);
            actions.Add(new Button(Close) { text = "Save and close" });
            actions.Add(new Button(() => { _prop.textureValue = _previous_property_texture; _gradient_has_been_edited = false; Close(); }) { text = "Discard" });
        }
    }
}
#endif
