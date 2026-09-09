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
            var title = new Label(_outputSettings == null ? RetainedText.Get("gradient", "Gradient") : RetainedText.Get("create_gradient_texture", "Create gradient texture")); title.AddToClassList("thry-title"); root.Add(title);
            var content = new ScrollView(ScrollViewMode.Vertical) { name = "gradient-content" };
            content.style.flexGrow = 1; content.style.flexShrink = 1; root.Add(content);
            content.Add(CreateGradientSurface());
            if (_outputSettings != null)
            {
                var direction = new DropdownField(RetainedText.Get("direction", "Direction"), new[] { RetainedText.Get("horizontal", "Horizontal"), RetainedText.Get("vertical_up", "Vertical up"), RetainedText.Get("vertical_down", "Vertical down") }.ToList(), _outputDirection) { name = "gradient-direction" };
                direction.tooltip = RetainedText.Get("gradient_direction_hint", "Direction from the first color stop to the last. Editable dimensions rotate with the gradient; shader-fixed dimensions stay unchanged.");
                RetainedWindow.Dropdown(direction); content.Add(direction);
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
                var size = new Vector2IntField(RetainedText.Get("texture_size", "Texture size")) { name = "gradient-size", value = _textureSize }; content.Add(size);
                size.RegisterValueChangedCallback(e => { _textureSize = new Vector2Int(Mathf.Clamp(e.newValue.x, Mathf.Max(1, _textureSizeMin.x), Mathf.Max(1, _textureSizeMax.x)), Mathf.Clamp(e.newValue.y, Mathf.Max(1, _textureSizeMin.y), Mathf.Max(1, _textureSizeMax.y))); size.SetValueWithoutNotify(_textureSize); });
            }
            if (_outputSettings != null)
            {
                if (_fixedOutput)
                {
                    var summary = new Label(_textureSize.x + " × " + _textureSize.y + " · " + _outputSettings.filterMode + " · " + _outputSettings.wrapMode);
                    summary.name = "gradient-fixed-settings"; summary.AddToClassList("thry-muted"); content.Add(summary);
                    var hint = new Label(RetainedText.Get("gradient_fixed_settings", "Texture settings are fixed by this shader.")); hint.AddToClassList("thry-muted"); content.Add(hint);
                }
                else
                {
                    var filter = new DropdownField(RetainedText.Get("filtering", "Filtering"), Enum.GetNames(typeof(FilterMode)).Select(name => RetainedText.EnumCaption(typeof(FilterMode), name)).ToList(), (int)_outputSettings.filterMode) { name = "gradient-filter" };
                    RetainedWindow.Dropdown(filter); content.Add(filter);
                    filter.RegisterValueChangedCallback(e => _outputSettings.filterMode = (FilterMode)filter.index);
                    var wrap = new DropdownField(RetainedText.Get("wrap", "Wrap"), Enum.GetNames(typeof(TextureWrapMode)).Select(name => RetainedText.EnumCaption(typeof(TextureWrapMode), name)).ToList(), (int)_outputSettings.wrapMode) { name = "gradient-wrap" };
                    RetainedWindow.Dropdown(wrap); content.Add(wrap);
                    wrap.RegisterValueChangedCallback(e => _outputSettings.wrapMode = (TextureWrapMode)wrap.index);
                    var aniso = new SliderInt(RetainedText.Get("anisotropy", "Anisotropy"), 0, 16) { name = "gradient-anisotropy", value = _outputSettings.ansioLevel, showInputField = true };
                    aniso.tooltip = RetainedText.Get("anisotropy_help", "Texture filtering quality at oblique viewing angles."); content.Add(aniso);
                    aniso.RegisterValueChangedCallback(e => _outputSettings.ansioLevel = e.newValue);
                }
            }
            bool missingTarget = _outputSettings != null && _onTextureApply == null;
            if (missingTarget)
            {
                var hint = new Label(RetainedText.Get("gradient_reopen", "Scripts reloaded. Reopen Gradient from the texture card to create its texture."));
                hint.style.whiteSpace = WhiteSpace.Normal; hint.AddToClassList("thry-muted"); root.Add(hint);
            }
            var error = new HelpBox("", HelpBoxMessageType.Error) { name = "gradient-error" };
            error.style.display = DisplayStyle.None; root.Add(error);
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); actions.AddToClassList("thry-dialog-actions"); root.Add(actions);
            var apply = new Button { text = _outputSettings == null ? RetainedText.Get("apply_gradient", "Apply gradient") : RetainedText.Get("create_texture", "Create texture"), name = "apply-gradient" }; apply.AddToClassList("thry-primary-action"); actions.Add(apply);
            apply.clicked += () =>
            {
                if (_canApply != null && !_canApply()) return;
                try { Apply(); Close(); }
                catch (Exception exception)
                {
                    error.text = RetainedText.Get("gradient_save_failed", "Could not save the gradient. Your draft is still available.") + "\n" + exception.Message;
                    error.style.display = DisplayStyle.Flex;
                    apply.text = RetainedText.Get("retry", "Retry");
                }
            };
            apply.SetEnabled(!missingTarget);
            if (_canApply != null)
            {
                apply.SetEnabled(_canApply());
                var availability = apply.schedule.Execute(() => apply.SetEnabled(_canApply())).Every(200);
                apply.RegisterCallback<DetachFromPanelEvent>(e => availability.Pause());
                apply.RegisterCallback<AttachToPanelEvent>(e => availability.Resume());
            }
            actions.Add(new Button(Close) { text = RetainedText.Get("cancel", "Cancel"), name = "cancel-gradient" });
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
                        var library = new Foldout { text = RetainedText.Get("presets", "Presets"), name = "gradient-presets", value = false };
                        RetainedUiState.Bind(library, "GradientEditor", "presets"); surface.Add(library);
                        var presets = new IMGUIContainer(() => DrawNativeGradient(PresetLibraryOnGUI, _gradientLibary,
                            GUILayoutUtility.GetRect(0, 10000, 150, 150), _gradient)) { name = "gradient-preset-library" };
                        presets.style.height = 150; presets.style.flexShrink = 0; library.Add(presets);
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
            field.tooltip = RetainedText.Get("gradient_edit_hint", "Click to edit gradient colors and opacity.");
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
            var title = new Label(RetainedText.Get("gradient", "Gradient")); title.AddToClassList("thry-title"); root.Add(title);
            var field = new GradientField { value = _data.Gradient }; field.AddToClassList("thry-gradient-preview"); root.Add(field);
            field.RegisterValueChangedCallback(e => { _data.Gradient = e.newValue; UpdateGradientPreviewTexture(); });
            if (_show_texture_options)
            {
                var size = new Vector2IntField(RetainedText.Get("texture_size", "Texture size")) { value = new Vector2Int(textureSettings.width, textureSettings.height) }; root.Add(size);
                size.RegisterValueChangedCallback(e => { textureSettings.width = Mathf.Clamp(e.newValue.x, 1, 8192); textureSettings.height = Mathf.Clamp(e.newValue.y, 1, 8192); size.SetValueWithoutNotify(new Vector2Int(textureSettings.width, textureSettings.height)); UpdateGradientPreviewTexture(); });
                var filter = new DropdownField(RetainedText.Get("filtering", "Filtering"), Enum.GetNames(typeof(FilterMode)).Select(name => RetainedText.EnumCaption(typeof(FilterMode), name)).ToList(), (int)textureSettings.filterMode); RetainedWindow.Dropdown(filter); root.Add(filter);
                filter.RegisterValueChangedCallback(e => { textureSettings.filterMode = (FilterMode)filter.index; UpdateGradientPreviewTexture(); });
                var wrap = new DropdownField(RetainedText.Get("wrap", "Wrap"), Enum.GetNames(typeof(TextureWrapMode)).Select(name => RetainedText.EnumCaption(typeof(TextureWrapMode), name)).ToList(), (int)textureSettings.wrapMode); RetainedWindow.Dropdown(wrap); root.Add(wrap);
                wrap.RegisterValueChangedCallback(e => { textureSettings.wrapMode = (TextureWrapMode)wrap.index; UpdateGradientPreviewTexture(); });
                var aniso = new SliderInt(RetainedText.Get("anisotropy", "Anisotropic filtering"), 0, 16) { value = textureSettings.ansioLevel, showInputField = true }; root.Add(aniso);
                aniso.RegisterValueChangedCallback(e => { textureSettings.ansioLevel = e.newValue; UpdateGradientPreviewTexture(); });
            }
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); actions.AddToClassList("thry-dialog-actions"); root.Add(actions);
            var save = new Button(Close) { text = RetainedText.Get("save_and_close", "Save and close") }; save.AddToClassList("thry-primary-action"); actions.Add(save);
            Action discard = () => { _prop.textureValue = _previous_property_texture; _gradient_has_been_edited = false; Close(); };
            actions.Add(new Button(discard) { text = RetainedText.Get("discard", "Discard") });
            RetainedWindow.Shortcuts(root, discard);
        }
    }
}
#endif
