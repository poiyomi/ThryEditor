#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
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
            _retainedWasBuilt = true;
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog"); root.AddToClassList("thry-gradient-editor");
            minSize = new Vector2(320, _outputSettings == null ? 180 : _fixedOutput ? 370 : 450);
            var title = new Label(_outputSettings == null ? "Gradient" : "Create gradient texture"); title.AddToClassList("thry-title"); root.Add(title);
            root.Add(CreateGradientSurface());
            if (_outputSettings != null)
            {
                var direction = new DropdownField("Direction", new[] { "Horizontal", "Vertical up", "Vertical down" }.ToList(), _outputDirection) { name = "gradient-direction" };
                direction.tooltip = "Direction from the first color stop to the last. Editable dimensions rotate with the gradient; shader-fixed dimensions stay unchanged.";
                RetainedWindow.Dropdown(direction); root.Add(direction);
                direction.RegisterValueChangedCallback(e =>
                {
                    bool rotated = (_outputDirection == 0) != (direction.index == 0);
                    _outputDirection = direction.index;
                    if (rotated && _allowSizeSelection)
                    {
                        _textureSize = new Vector2Int(_textureSize.y, _textureSize.x);
                        root.Q<Vector2IntField>("gradient-size")?.SetValueWithoutNotify(_textureSize);
                    }
                });
            }
            if (_allowSizeSelection)
            {
                var size = new Vector2IntField("Texture size") { name = "gradient-size", value = _textureSize }; root.Add(size);
                size.RegisterValueChangedCallback(e => { _textureSize = new Vector2Int(Mathf.Clamp(e.newValue.x, Mathf.Max(1, _textureSizeMin.x), Mathf.Max(1, _textureSizeMax.x)), Mathf.Clamp(e.newValue.y, Mathf.Max(1, _textureSizeMin.y), Mathf.Max(1, _textureSizeMax.y))); size.SetValueWithoutNotify(_textureSize); });
            }
            if (_outputSettings != null)
            {
                if (_fixedOutput)
                {
                    var summary = new Label(_textureSize.x + " × " + _textureSize.y + " · " + _outputSettings.filterMode + " · " + _outputSettings.wrapMode);
                    summary.name = "gradient-fixed-settings"; summary.AddToClassList("thry-muted"); root.Add(summary);
                    var hint = new Label("Texture settings are fixed by this shader."); hint.AddToClassList("thry-muted"); root.Add(hint);
                }
                else
                {
                    var filter = new DropdownField("Filtering", Enum.GetNames(typeof(FilterMode)).ToList(), (int)_outputSettings.filterMode) { name = "gradient-filter" };
                    RetainedWindow.Dropdown(filter); root.Add(filter);
                    filter.RegisterValueChangedCallback(e => _outputSettings.filterMode = (FilterMode)Enum.Parse(typeof(FilterMode), e.newValue));
                    var wrap = new DropdownField("Wrap", Enum.GetNames(typeof(TextureWrapMode)).ToList(), (int)_outputSettings.wrapMode) { name = "gradient-wrap" };
                    RetainedWindow.Dropdown(wrap); root.Add(wrap);
                    wrap.RegisterValueChangedCallback(e => _outputSettings.wrapMode = (TextureWrapMode)Enum.Parse(typeof(TextureWrapMode), e.newValue));
                    var aniso = new SliderInt("Anisotropy", 0, 16) { name = "gradient-anisotropy", value = _outputSettings.ansioLevel, showInputField = true };
                    aniso.tooltip = "Texture filtering quality at oblique viewing angles."; root.Add(aniso);
                    aniso.RegisterValueChangedCallback(e => _outputSettings.ansioLevel = e.newValue);
                }
            }
            bool missingTarget = _outputSettings != null && _onTextureApply == null;
            if (missingTarget)
            {
                var hint = new Label("Scripts reloaded. Reopen Gradient from the texture card to create its texture.");
                hint.style.whiteSpace = WhiteSpace.Normal; hint.AddToClassList("thry-muted"); root.Add(hint);
            }
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); actions.AddToClassList("thry-dialog-actions"); root.Add(actions);
            var apply = new Button(() => { if (_canApply != null && !_canApply()) return; Apply(); Close(); }) { text = _outputSettings == null ? "Apply gradient" : "Create texture", name = "apply-gradient" }; apply.AddToClassList("thry-primary-action"); actions.Add(apply);
            apply.SetEnabled(!missingTarget);
            if (_canApply != null)
            {
                apply.SetEnabled(_canApply());
                var availability = apply.schedule.Execute(() => apply.SetEnabled(_canApply())).Every(200);
                apply.RegisterCallback<DetachFromPanelEvent>(e => availability.Pause());
                apply.RegisterCallback<AttachToPanelEvent>(e => availability.Resume());
            }
            actions.Add(new Button(Close) { text = "Cancel", name = "cancel-gradient" });
            RetainedWindow.Shortcuts(root, Close);
        }

        VisualElement CreateGradientSurface()
        {
            if (_outputSettings != null)
            {
                try
                {
                    _gradientEditor = GetGradientEditor(_gradient);
                    _gradientLibary = GetGradientLibary((index, preset) =>
                    {
                        var selected = preset as Gradient;
                        if (selected == null) return;
                        _gradient.SetKeys(selected.colorKeys, selected.alphaKeys); _gradient.mode = selected.mode;
                        SetGradient(_gradientEditor, _gradient); Repaint();
                    });
                    if (GradientEditorGUI != null && PresetLibraryOnGUI != null)
                    {
                        var surface = new VisualElement { name = "gradient-creation-surface" };
                        // Unity owns the stop handles, color/opacity editing and preset
                        // library. Keep these inside the same window as output settings.
                        var stops = new IMGUIContainer(() => DrawNativeGradient(GradientEditorGUI, _gradientEditor,
                            GUILayoutUtility.GetRect(0, 10000, 130, 130))) { name = "gradient-stop-editor" };
                        stops.style.height = 130; stops.style.flexShrink = 0; surface.Add(stops);
                        var presets = new IMGUIContainer(() => DrawNativeGradient(PresetLibraryOnGUI, _gradientLibary,
                            GUILayoutUtility.GetRect(0, 10000, 86, 86), _gradient)) { name = "gradient-preset-library" };
                        presets.style.height = 86; presets.style.flexShrink = 0; surface.Add(presets);
                        return surface;
                    }
                }
                catch (Exception e) when (e is MemberAccessException || e is ArgumentException || e is TargetInvocationException || e is NullReferenceException)
                {
                    // Unity's internal editor can vary by version. The supported field
                    // remains available if that editor cannot be embedded.
                    _gradientEditor = null; _gradientLibary = null;
                }
            }
            var field = new GradientField { value = _gradient };
            field.AddToClassList("thry-gradient-preview");
            field.tooltip = "Click to edit gradient colors and opacity.";
            field.RegisterValueChangedCallback(e => _gradient = e.newValue);
            return field;
        }

        static void DrawNativeGradient(MethodInfo method, object editor, Rect rect, Gradient gradient = null)
        {
            try { method.Invoke(editor, gradient == null ? new object[] { rect } : new object[] { rect, gradient }); }
            catch (TargetInvocationException e) when (e.InnerException is ExitGUIException) { throw e.InnerException; }
        }
    }

    public partial class GradientEditor
    {
        public void CreateGUI()
        {
            if (_data == null || _prop == null) return;
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog"); root.AddToClassList("thry-gradient-editor");
            minSize = new Vector2(320, _show_texture_options ? 310 : 180);
            var title = new Label("Gradient"); title.AddToClassList("thry-title"); root.Add(title);
            var field = new GradientField { value = _data.Gradient }; field.AddToClassList("thry-gradient-preview"); root.Add(field);
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
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); actions.AddToClassList("thry-dialog-actions"); root.Add(actions);
            var save = new Button(Close) { text = "Save and close" }; save.AddToClassList("thry-primary-action"); actions.Add(save);
            Action discard = () => { _prop.textureValue = _previous_property_texture; _gradient_has_been_edited = false; Close(); };
            actions.Add(new Button(discard) { text = "Discard" });
            RetainedWindow.Shortcuts(root, discard);
        }
    }
}
#endif
