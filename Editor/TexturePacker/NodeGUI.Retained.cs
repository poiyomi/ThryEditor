using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.TexturePacker
{
    public partial class NodeGUI
    {
        Image _retainedPreview;
        Label _retainedStatus;
        IVisualElementScheduledItem _pendingPack;
        RenderTexture _retainedChannelPreview;
        int _retainedPreviewChannel;
        string _retainedError;
        DropdownField _retainedPreviewSelector;

        public void CreateGUI()
        {
            if (_config == null)
            {
                var config = TexturePackerConfig.GetNewConfig();
                config.FileOutput.AlphaIsTransparency = false;
                InitilizeWithData(config);
                return;
            }
            if (_config.KernelSettings == null) _config.KernelSettings = new KernelSettings();
            _pendingPack?.Pause();
            _graph?.Dispose();
            rootVisualElement.Clear();
            RetainedWindow.Style(rootVisualElement);
            rootVisualElement.AddToClassList("thry-texture-workspace");
            rootVisualElement.AddToClassList("thry-studio-graph-window");
            minSize = new Vector2(600, 400);
            titleContent = new GUIContent(RetainedText.Get("studio_title", "Texture studio"));
            BuildGraphWorkspace();
            UpdateRetainedPreview();
            if (_outputTexture == null) QueuePack();
        }

        static Label StudioHint(VisualElement root, string key, string text)
        {
            var label = new Label(RetainedText.Get(key, text));
            label.AddToClassList("thry-studio-hint");
            root.Add(label);
            return label;
        }

        static Foldout Section(VisualElement root, string title, bool expanded)
        {
            var section = new Foldout { text = title, value = expanded };
            section.AddToClassList("thry-studio-section");
            root.Add(section);
            return section;
        }

        void BuildSource(VisualElement root, int index)
        {
            var source = _config.Sources[index];
            var card = new VisualElement(); card.AddToClassList("thry-studio-source"); root.Add(card);
            var heading = new VisualElement(); heading.AddToClassList("thry-components"); card.Add(heading);
            var title = new Label(RetainedText.Get("studio_source", "Source") + " " + (index + 1)); title.style.flexGrow = 1; heading.Add(title);
            var body = new VisualElement();
            AddEnum(heading, "", source.InputType, value =>
            {
                source.InputType = value;
                source.UpdateColorTexture();
                source.UpdateGradientTexture(_config.FileOutput.Resolution);
                body.Clear();
                BuildSourceValue(body, source);
            });
            card.Add(body);
            BuildSourceValue(body, source);
            var options = Section(card, RetainedText.Get("studio_source_options", "Source options"), false);
            AddEnum(options, RetainedText.Get("studio_filtering", "Filtering"), source.FilterMode, value => source.FilterMode = value);
        }

        void BuildSourceValue(VisualElement root, PackerSource source)
        {
            if (source.InputType == InputType.Texture)
            {
                var field = new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false, value = source.ImageTexture };
                field.RegisterValueChangedCallback(e => { source.SetInputTexture(e.newValue as Texture2D); root.Clear(); BuildSourceValue(root, source); QueuePack(); });
                root.Add(field);
                if (source.MissingImageReference)
                {
                    var hint = new Label(RetainedText.Get("studio_missing_source_hint", "Source missing. Reassign it, or clear it to use the fallback.")); hint.style.whiteSpace = WhiteSpace.Normal; root.Add(hint);
                    root.Add(new Button(() => { source.SetInputTexture(null); root.Clear(); BuildSourceValue(root, source); QueuePack(); }) { text = RetainedText.Get("studio_clear_missing_source", "Clear missing source") });
                }
            }
            else if (source.InputType == InputType.Color)
            {
                var field = new ColorField(RetainedText.Get("studio_color", "Color")) { value = source.Color };
                field.RegisterValueChangedCallback(e => { source.Color = e.newValue; source.UpdateColorTexture(); QueuePack(); });
                root.Add(field);
            }
            else
            {
                var field = new GradientField(RetainedText.Get("studio_gradient", "Gradient")) { value = source.Gradient ?? new Gradient() };
                Action update = () =>
                {
                    if (source.GradientTexture != null) DestroyImmediate(source.GradientTexture);
                    source.GradientTexture = null;
                    source.UpdateGradientTexture(_config.FileOutput.Resolution);
                };
                field.RegisterValueChangedCallback(e => { source.Gradient = e.newValue; update(); QueuePack(); });
                root.Add(field);
                AddEnum(root, RetainedText.Get("studio_direction", "Direction"), source.GradientDirection, v => { source.GradientDirection = v; update(); });
            }
        }

        void BuildAdvancedRouting(VisualElement root)
        {
            for (int i = 0; i < 4; i++)
            {
                int output = i;
                var channel = Section(root, "RGBA"[i] + " " + RetainedText.Get("studio_output", "Output"), true);
                var routes = new VisualElement();
                channel.Add(routes);
                Action rebuild = null;
                rebuild = () =>
                {
                    routes.Clear();
                    for (int j = 0; j < _config.Connections.Count; j++)
                    {
                        int routeIndex = j;
                        if ((int)_config.Connections[j].ToChannel != output) continue;
                        var route = new VisualElement();
                        route.AddToClassList("thry-studio-route");
                        routes.Add(route);
                        Action<Action<ConnectionBox>> change = edit =>
                        {
                            var box = new ConnectionBox { Value = _config.Connections[routeIndex] };
                            edit(box);
                            _config.Connections[routeIndex] = box.Value;
                        };
                        var connection = _config.Connections[j];
                        var source = new DropdownField(RetainedText.Get("studio_source", "Source"), Enumerable.Range(1, _config.Sources.Length).Select(n => RetainedText.Get("studio_source", "Source") + " " + n).ToList(), Mathf.Clamp(connection.FromTextureIndex, 0, _config.Sources.Length - 1));
                        RetainedWindow.Dropdown(source);
                        source.RegisterValueChangedCallback(e => { change(b => b.Value.FromTextureIndex = source.index); QueuePack(); });
                        route.Add(source);
                        AddEnum(route, RetainedText.Get("studio_channel", "Channel"), connection.FromChannel, v => change(b => b.Value.FromChannel = v));
                        AddEnum(route, RetainedText.Get("studio_remap", "Remap"), connection.RemappingMode, v => change(b => b.Value.RemappingMode = v));
                        var range = new Vector4Field(RetainedText.Get("studio_range", "Input / output range")) { value = connection.Remapping };
                        range.RegisterValueChangedCallback(e => { change(b => b.Value.Remapping = e.newValue); QueuePack(); });
                        route.Add(range);
                        route.Add(new Button(() => { _config.Connections.RemoveAt(routeIndex); root.Clear(); BuildAdvancedRouting(root); QueuePack(); }) { text = RetainedText.Get("studio_remove_source", "Remove source") });
                    }
                };
                rebuild();
                channel.Add(new Button(() => { _config.Connections.Add(new Connection(Mathf.Min(output, _config.Sources.Length - 1), (TextureChannelIn)output, (TextureChannelOut)output)); root.Clear(); BuildAdvancedRouting(root); QueuePack(); }) { text = RetainedText.Get("studio_add_source", "Add source") });
                AddEnum(channel, RetainedText.Get("studio_combine", "Combine"), _config.Targets[i].BlendMode, v => _config.Targets[output].BlendMode = v);
                AddEnum(channel, RetainedText.Get("studio_invert", "Invert"), _config.Targets[i].Invert, v => _config.Targets[output].Invert = v);
                AddNumber(channel, RetainedText.Get("studio_when_empty", "When empty"), _config.Targets[i].Fallback, v => _config.Targets[output].Fallback = Mathf.Clamp01(v));
            }
        }

        sealed class ConnectionBox { public Connection Value; }

        void BuildAdjustments(VisualElement root)
        {
            var settings = _config.ImageAdjust;
            AddNumber(root, RetainedText.Get("studio_brightness", "Brightness"), settings.Brightness, v => settings.Brightness = v);
            AddNumber(root, RetainedText.Get("studio_hue", "Hue"), settings.Hue, v => settings.Hue = v);
            AddNumber(root, RetainedText.Get("studio_saturation", "Saturation"), settings.Saturation, v => settings.Saturation = v);
            AddNumber(root, RetainedText.Get("studio_rotation", "Rotation"), settings.Rotation, v => settings.Rotation = v);
            AddVector(root, RetainedText.Get("studio_scale", "Scale"), settings.Scale, v => settings.Scale = v);
            AddVector(root, RetainedText.Get("studio_offset", "Offset"), settings.Offset, v => settings.Offset = v);
            root.Add(new Button(() => { _config.ImageAdjust = new ImageAdjust(); root.Clear(); BuildAdjustments(root); QueuePack(); }) { text = RetainedText.Get("studio_reset_adjustments", "Reset adjustments") });
        }

        void BuildFilter(VisualElement root)
        {
            var details = new VisualElement();
            Action rebuild = () =>
            {
                details.Clear();
                details.style.display = _config.KernelPreset == KernelPreset.None ? DisplayStyle.None : DisplayStyle.Flex;
                var settings = _config.KernelSettings;
                AddNumber(details, RetainedText.Get("studio_strength", "Strength"), settings.Strength, v => settings.Strength = v);
                AddNumber(details, RetainedText.Get("studio_passes", "Passes"), settings.Loops, v => settings.Loops = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100));
                AddToggle(details, RetainedText.Get("studio_two_passes", "Two passes"), settings.TwoPass, v => settings.TwoPass = v);
                AddToggle(details, RetainedText.Get("studio_grayscale", "Grayscale"), settings.GrayScale, v => settings.GrayScale = v);
                AddToggle(details, RetainedText.Get("studio_separate_directions", "Separate directions"), settings.SplitVerticalHorizontal, v => settings.SplitVerticalHorizontal = v);
                for (int i = 0; i < 4; i++)
                {
                    int channel = i;
                    AddToggle(details, "RGBA"[i].ToString(), settings.Channels[i], v => settings.Channels[channel] = v);
                }
                if (_config.KernelPreset != KernelPreset.Custom) return;
                BuildKernel(Section(details, RetainedText.Get("studio_horizontal_kernel", "Horizontal kernel"), true), settings.X);
                BuildKernel(Section(details, RetainedText.Get("studio_vertical_kernel", "Vertical kernel"), false), settings.Y);
            };
            AddEnum(root, RetainedText.Get("studio_filter", "Filter"), _config.KernelPreset, v => { _config.KernelPreset = v; _config.KernelSettings.LoadPreset(v); rebuild(); });
            root.Add(details);
            rebuild();
        }

        void BuildKernel(VisualElement root, float[] values)
        {
            for (int y = 0; y < 5; y++)
            {
                var row = new VisualElement();
                row.AddToClassList("thry-components");
                root.Add(row);
                for (int x = 0; x < 5; x++)
                {
                    int index = y * 5 + x;
                    var field = new FloatField { value = values[index], isDelayed = true };
                    field.style.flexBasis = 0;
                    field.style.flexGrow = 1;
                    field.RegisterValueChangedCallback(e => { values[index] = e.newValue; QueuePack(); });
                    row.Add(field);
                }
            }
        }

        void BuildOutput(VisualElement root)
        {
            var output = _config.FileOutput;
            var sizing = new DropdownField(RetainedText.Get("studio_sizing", "Sizing"), new System.Collections.Generic.List<string> { RetainedText.Get("automatic", "Automatic"), RetainedText.Get("custom", "Custom") }, output.CustomResolution ? 1 : 0);
            RetainedWindow.Dropdown(sizing); root.Add(sizing);
            var size = new Vector2IntField(RetainedText.Get("studio_resolution", "Resolution")) { value = output.Resolution };
            sizing.tooltip = RetainedText.Get("studio_automatic_size_tip", "Automatic uses the largest source width and height, rounds each up to a power of two, and limits each to 4096. Choose Custom for a different size.");
            size.SetEnabled(output.CustomResolution);
            sizing.RegisterValueChangedCallback(e => { output.CustomResolution = sizing.index == 1; size.SetEnabled(output.CustomResolution); QueuePack(); });
            size.Query<IntegerField>().ForEach(field => field.isDelayed = true);
            size.RegisterValueChangedCallback(e => { output.CustomResolution = true; sizing.SetValueWithoutNotify(RetainedText.Get("custom", "Custom")); output.Resolution = new Vector2Int(Mathf.Clamp(e.newValue.x, 1, 8192), Mathf.Clamp(e.newValue.y, 1, 8192)); size.SetValueWithoutNotify(output.Resolution); QueuePack(); });
            root.Add(size);
            var filename = new TextField(RetainedText.Get("name", "Name")) { value = output.FileName, isDelayed = true };
            filename.RegisterValueChangedCallback(e => output.FileName = e.newValue);
            root.Add(filename);

            var advice = new VisualElement(); root.Add(advice);
            Action updateAdvice = () =>
            {
                advice.Clear();
                if (output.SaveType == SaveType.JPG)
                    advice.Add(new HelpBox(RetainedText.Get("studio_jpeg_mask_warning", "JPEG loses the Alpha channel and adds compression artifacts. Use PNG to keep all four masks."), HelpBoxMessageType.Warning));
                if (output.SaveType == SaveType.PNG && output.ColorSpace == ColorSpace.Linear && !output.AlphaIsTransparency)
                    StudioHint(advice, "studio_mask_settings_help", "PNG keeps all four masks. Alpha is saved as mask data.");
                else
                {
                    StudioHint(advice, "studio_mask_settings_recommendation", "For masks, use PNG, Linear color space, and turn off Alpha is transparency.");
                    advice.Add(new Button(() =>
                    {
                        output.SaveType = SaveType.PNG; output.ColorSpace = ColorSpace.Linear; output.AlphaIsTransparency = false;
                        root.Clear(); BuildOutput(root); QueuePack();
                    }) { name = "studio-use-mask-settings", text = RetainedText.Get("studio_use_mask_settings", "Use recommended mask settings") });
                }
            };
            var details = Section(root, RetainedText.Get("studio_file_options", "File options"), false);
            details.name = "studio-file-options";
            AddEnum(details, RetainedText.Get("studio_format", "Format"), output.SaveType, v =>
            {
                bool expanded = details.value;
                output.SaveType = v; root.Clear(); BuildOutput(root);
                root.Q<Foldout>("studio-file-options").value = expanded;
            });
            AddEnum(details, RetainedText.Get("studio_color_space", "Color space"), output.ColorSpace, v => { output.ColorSpace = v; updateAdvice(); });
            AddEnum(details, RetainedText.Get("studio_filtering", "Filtering"), output.FilterMode, v => output.FilterMode = v);
            AddToggle(details, RetainedText.Get("studio_alpha_transparency", "Alpha is transparency"), output.AlphaIsTransparency, v => { output.AlphaIsTransparency = v; updateAdvice(); });
            if (output.SaveType == SaveType.JPG)
                AddNumber(details, RetainedText.Get("studio_jpeg_quality", "JPEG quality"), output.SaveQuality, v => output.SaveQuality = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100));
            updateAdvice();
        }

        void AddEnum<T>(VisualElement root, string label, T value, Action<T> set) where T : struct
        {
            var values = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
            var field = new DropdownField(label, values.Select(item => RetainedText.EnumCaption(typeof(T), item.ToString())).ToList(), Array.IndexOf(values, value));
            RetainedWindow.Dropdown(field);
            field.RegisterValueChangedCallback(e => { if (field.index < 0 || field.index >= values.Length) return; set(values[field.index]); QueuePack(); });
            root.Add(field);
        }

        void AddNumber(VisualElement root, string label, float value, Action<float> set)
        {
            var field = new FloatField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(e => { set(e.newValue); QueuePack(); });
            root.Add(field);
        }

        void AddToggle(VisualElement root, string label, bool value, Action<bool> set, bool pack = true)
        {
            var field = new Toggle(label) { value = value };
            field.RegisterValueChangedCallback(e => { set(e.newValue); if (pack) QueuePack(); });
            root.Add(field);
        }

        void AddVector(VisualElement root, string label, Vector2 value, Action<Vector2> set)
        {
            var field = new Vector2Field(label) { value = value };
            field.RegisterValueChangedCallback(e => { set(e.newValue); QueuePack(); });
            root.Add(field);
        }

        void QueuePack()
        {
            _graph?.ScheduleConnectionsRefresh();
            _pendingPack?.Pause();
            _pendingPack = rootVisualElement.schedule.Execute(() => TryStudioAction(Pack)).StartingIn(120);
        }

        bool TryStudioAction(Action action)
        {
            try { action(); return true; }
            catch (ExitGUIException) { throw; }
            catch (Exception exception)
            {
                _retainedError = exception.Message;
                if (_retainedStatus != null) _retainedStatus.text = _retainedError;
                return false;
            }
        }

        void UpdateRetainedPreview()
        {
            if (_retainedPreview == null) return;
            if (_retainedPreviewChannel > 0 && _outputTexture != null)
            {
                float scale = Mathf.Min(1, 512f / Mathf.Max(_outputTexture.width, _outputTexture.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(_outputTexture.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(_outputTexture.height * scale));
                if (_retainedChannelPreview == null || _retainedChannelPreview.width != width || _retainedChannelPreview.height != height)
                {
                    if (_retainedChannelPreview != null) { _retainedChannelPreview.Release(); DestroyImmediate(_retainedChannelPreview); }
                    _retainedChannelPreview = new RenderTexture(width, height, 0) { hideFlags = HideFlags.HideAndDontSave };
                }
                ChannelPreviewMaterial.SetFloat("_Channel", _retainedPreviewChannel - 1);
                var previous = RenderTexture.active;
                try { Graphics.Blit(_outputTexture, _retainedChannelPreview, ChannelPreviewMaterial); }
                finally { RenderTexture.active = previous; }
                _retainedPreview.image = _retainedChannelPreview;
            }
            else _retainedPreview.image = _outputTexture;
            _retainedPreview.tintColor = Color.white;
            _graph?.RefreshPreviews();
            _retainedStatus.text = _retainedError ?? (_config.FileOutput.Resolution.x + " × " + _config.FileOutput.Resolution.y + " · " + _config.FileOutput.SaveType);
            rootVisualElement.Query<Vector2IntField>().ToList().FirstOrDefault(field => field.label == RetainedText.Get("studio_resolution", "Resolution"))?.SetValueWithoutNotify(_config.FileOutput.Resolution);
        }
    }
}
