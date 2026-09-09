#if UNITY_2021_3_OR_NEWER
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

        public void CreateGUI()
        {
            if (_config == null) return;
            if (_config.KernelSettings == null) _config.KernelSettings = new KernelSettings();
            _pendingPack?.Pause();
            var root = rootVisualElement;
            root.Clear();
            RetainedWindow.Style(root);
            root.AddToClassList("thry-texture-workspace");
            minSize = new Vector2(620, 480);

            var title = new Label(RetainedText.Get("studio_title", "Texture studio"));
            title.AddToClassList("thry-title");
            root.Add(title);
            var subtitle = new Label(RetainedText.Get("studio_intro", "Combine sources into one texture. Preview changes before saving."));
            subtitle.AddToClassList("thry-muted");
            root.Add(subtitle);

            var workspace = new VisualElement();
            workspace.AddToClassList("thry-studio-columns");
            root.Add(workspace);
            var controls = new ScrollView();
            controls.AddToClassList("thry-studio-controls");
            workspace.Add(controls);
            var preview = new VisualElement();
            preview.AddToClassList("thry-studio-preview");
            workspace.Add(preview);
            _retainedPreview = new Image { name = "texture-preview", image = _outputTexture, scaleMode = ScaleMode.ScaleToFit };
            _retainedPreview.style.flexGrow = 1;
            _retainedPreview.style.minHeight = 180;
            preview.Add(_retainedPreview);
            _retainedStatus = new Label { name = "texture-studio-status" };
            _retainedStatus.style.whiteSpace = WhiteSpace.Normal;
            preview.Add(_retainedStatus);
            var previewChannel = new DropdownField(RetainedText.Get("studio_preview", "Preview"), new System.Collections.Generic.List<string> { RetainedText.Get("studio_combined", "Combined"), RetainedText.Get("red", "Red"), RetainedText.Get("green", "Green"), RetainedText.Get("blue", "Blue"), RetainedText.Get("alpha", "Alpha") }, _retainedPreviewChannel);
            RetainedWindow.Dropdown(previewChannel);
            previewChannel.RegisterValueChangedCallback(e => { _retainedPreviewChannel = previewChannel.index; UpdateRetainedPreview(); });
            preview.Add(previewChannel);

            var exports = new Foldout { text = RetainedText.Get("studio_export_channels", "Export individual channels"), value = false };
            preview.Add(exports);
            var channels = new VisualElement();
            channels.AddToClassList("thry-components");
            exports.Add(channels);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var channel = new Toggle("RGBA"[i].ToString()) { value = _channel_export[i] };
                channel.style.flexGrow = 1;
                channel.RegisterValueChangedCallback(e => _channel_export[index] = e.newValue);
                channels.Add(channel);
            }
            exports.Add(new Button(() => TryStudioAction(() => { _config.RequireResolvedSources(); ExportChannels(true); })) { text = RetainedText.Get("studio_export_grayscale", "Export as grayscale") });
            exports.Add(new Button(() => TryStudioAction(() => { _config.RequireResolvedSources(); ExportChannels(false); })) { text = RetainedText.Get("studio_export_color", "Export as color") });

            var load = new ObjectField(RetainedText.Get("studio_open_saved", "Open saved texture")) { objectType = typeof(Texture2D), allowSceneObjects = false };
            controls.Add(load);
            load.RegisterValueChangedCallback(e =>
            {
                var texture = e.newValue as Texture2D;
                if (texture == null) return;
                TexturePackerConfig config;
                TryStudioAction(() => {
                    if (TexturePackerConfig.TryGetFromTexture(texture, out config)) InitilizeWithData(config);
                    else InitilizeWithOneTexture(texture);
                });
            });

            var sources = Section(controls, RetainedText.Get("studio_sources", "Sources"), true);
            for (int i = 0; i < _config.Sources.Length; i++) BuildSource(sources, i);
            BuildRouting(Section(controls, RetainedText.Get("studio_routing", "Channel routing"), true));
            BuildAdjustments(Section(controls, RetainedText.Get("studio_adjustments", "Image adjustments"), false));
            BuildFilter(Section(controls, RetainedText.Get("studio_filter", "Filter"), false));
            BuildOutput(Section(controls, RetainedText.Get("studio_output", "Output"), true));

            var save = new Button(() => TryStudioAction(() =>
            {
                _pendingPack?.Pause();
                _config.RequireResolvedSources();
                Pack();
                var importer = Packer.Save(_outputTexture, _config);
                if (importer == null) return;
                _associatedImporter = importer;
                OnSave?.Invoke(AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath));
                _retainedStatus.text = RetainedText.Get("studio_saved", "Saved") + " " + System.IO.Path.GetFileName(importer.assetPath);
            })) { text = RetainedText.Get("studio_save", "Save texture"), name = "save-texture" };
            save.AddToClassList("thry-primary-action");
            root.Add(save);
            UpdateRetainedPreview();
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

        void BuildRouting(VisualElement root)
        {
            var simple = new VisualElement(); root.Add(simple);
            for (int i = 0; i < 4; i++)
            {
                int output = i;
                var matches = _config.Connections.Where(c => (int)c.ToChannel == output).ToArray();
                var row = new VisualElement(); row.AddToClassList("thry-components"); simple.Add(row);
                var label = new Label("RGBA"[i].ToString()); label.style.width = 24; label.style.alignSelf = Align.Center; row.Add(label);
                var choices = new[] { RetainedText.Get("none", "None") }.Concat(Enumerable.Range(1, _config.Sources.Length).Select(n => RetainedText.Get("studio_source", "Source") + " " + n)).ToList();
                var source = new DropdownField(choices, matches.Length == 0 ? 0 : matches[0].FromTextureIndex + 1);
                source.style.flexGrow = 1; source.style.flexBasis = 0; RetainedWindow.Dropdown(source); row.Add(source);
                if (matches.Length > 1) source.SetValueWithoutNotify(RetainedText.Get("studio_multiple_sources", "Multiple sources"));
                var channels = Enum.GetValues(typeof(TextureChannelIn)).Cast<TextureChannelIn>().ToArray();
                var channel = new DropdownField(channels.Select(item => RetainedText.EnumCaption(typeof(TextureChannelIn), item.ToString())).ToList(), matches.Length == 0 ? i : Array.IndexOf(channels, matches[0].FromChannel));
                channel.style.width = 70; RetainedWindow.Dropdown(channel); row.Add(channel);
                Action update = () =>
                {
                    if (source.index < 0 || channel.index < 0 || channel.index >= channels.Length) return;
                    var existing = _config.Connections.Where(c => (int)c.ToChannel == output).ToArray();
                    var replacement = existing.Length == 1 ? existing[0] : new Connection(-1, TextureChannelIn.None, (TextureChannelOut)output);
                    replacement.FromTextureIndex = source.index - 1; replacement.FromChannel = channels[channel.index];
                    replacement.ToChannel = (TextureChannelOut)output;
                    _config.Connections.RemoveAll(c => (int)c.ToChannel == output);
                    if (source.index > 0) _config.Connections.Add(replacement);
                    root.Clear(); BuildRouting(root);
                    QueuePack();
                };
                source.RegisterValueChangedCallback(e => update());
                channel.RegisterValueChangedCallback(e => update());
            }
            var advanced = Section(root, RetainedText.Get("studio_advanced_routing", "Advanced routing"), false);
            advanced.RegisterValueChangedCallback(e =>
            {
                if (e.target != advanced) return;
                if (e.newValue) { advanced.contentContainer.Clear(); BuildAdvancedRouting(advanced); }
                else { root.Clear(); BuildRouting(root); }
            });
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
            size.SetEnabled(output.CustomResolution);
            sizing.RegisterValueChangedCallback(e => { output.CustomResolution = sizing.index == 1; size.SetEnabled(output.CustomResolution); QueuePack(); });
            size.Query<IntegerField>().ForEach(field => field.isDelayed = true);
            size.RegisterValueChangedCallback(e => { output.CustomResolution = true; sizing.SetValueWithoutNotify(RetainedText.Get("custom", "Custom")); output.Resolution = new Vector2Int(Mathf.Clamp(e.newValue.x, 1, 8192), Mathf.Clamp(e.newValue.y, 1, 8192)); size.SetValueWithoutNotify(output.Resolution); QueuePack(); });
            root.Add(size);
            AddEnum(root, RetainedText.Get("studio_format", "Format"), output.SaveType, v => output.SaveType = v);
            AddEnum(root, RetainedText.Get("studio_color_space", "Color space"), output.ColorSpace, v => output.ColorSpace = v);
            AddEnum(root, RetainedText.Get("studio_filtering", "Filtering"), output.FilterMode, v => output.FilterMode = v);
            AddToggle(root, RetainedText.Get("studio_alpha_transparency", "Alpha is transparency"), output.AlphaIsTransparency, v => output.AlphaIsTransparency = v);
            AddNumber(root, RetainedText.Get("studio_jpeg_quality", "JPEG quality"), output.SaveQuality, v => output.SaveQuality = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100));
            var folder = new TextField(RetainedText.Get("studio_folder", "Folder")) { value = output.SaveFolder, isDelayed = true };
            folder.RegisterValueChangedCallback(e => output.SaveFolder = e.newValue);
            root.Add(folder);
            var filename = new TextField(RetainedText.Get("name", "Name")) { value = output.FileName, isDelayed = true };
            filename.RegisterValueChangedCallback(e => output.FileName = e.newValue);
            root.Add(filename);
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
            _retainedStatus.text = _retainedError ?? (_config.FileOutput.Resolution.x + " × " + _config.FileOutput.Resolution.y + " · " + _config.FileOutput.SaveType);
            rootVisualElement.Query<Vector2IntField>().ToList().FirstOrDefault(field => field.label == RetainedText.Get("studio_resolution", "Resolution"))?.SetValueWithoutNotify(_config.FileOutput.Resolution);
        }
    }
}
#endif
