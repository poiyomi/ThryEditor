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

            var title = new Label("Texture studio");
            title.AddToClassList("thry-title");
            root.Add(title);
            var subtitle = new Label("Combine sources into one texture. Preview changes before saving.");
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
            _retainedStatus = new Label();
            preview.Add(_retainedStatus);
            var previewChannel = new DropdownField("Preview", new System.Collections.Generic.List<string> { "Combined", "Red", "Green", "Blue", "Alpha" }, _retainedPreviewChannel);
            RetainedWindow.Dropdown(previewChannel);
            previewChannel.RegisterValueChangedCallback(e => { _retainedPreviewChannel = previewChannel.index; UpdateRetainedPreview(); });
            preview.Add(previewChannel);

            var exports = new Foldout { text = "Export individual channels", value = false };
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
            exports.Add(new Button(() => ExportChannels(true)) { text = "Export as grayscale" });
            exports.Add(new Button(() => ExportChannels(false)) { text = "Export as color" });

            var load = new ObjectField("Open saved texture") { objectType = typeof(Texture2D), allowSceneObjects = false };
            controls.Add(load);
            load.RegisterValueChangedCallback(e =>
            {
                var texture = e.newValue as Texture2D;
                if (texture == null) return;
                TexturePackerConfig config;
                if (TexturePackerConfig.TryGetFromTexture(texture, out config)) InitilizeWithData(config);
                else InitilizeWithOneTexture(texture);
            });

            var sources = Section(controls, "Sources", true);
            for (int i = 0; i < _config.Sources.Length; i++) BuildSource(sources, i);
            BuildRouting(Section(controls, "Channel routing", true));
            BuildAdjustments(Section(controls, "Image adjustments", false));
            BuildFilter(Section(controls, "Filter", false));
            BuildOutput(Section(controls, "Output", true));

            var save = new Button(() =>
            {
                _pendingPack?.Pause();
                Pack();
                var importer = Packer.Save(_outputTexture, _config);
                if (importer == null) return;
                _associatedImporter = importer;
                OnSave?.Invoke(AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath));
                _retainedStatus.text = "Saved " + System.IO.Path.GetFileName(importer.assetPath);
            }) { text = "Save texture", name = "save-texture" };
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
            var title = new Label("Source " + (index + 1)); title.style.flexGrow = 1; heading.Add(title);
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
            var options = Section(card, "Source options", false);
            AddEnum(options, "Filtering", source.FilterMode, value => source.FilterMode = value);
        }

        void BuildSourceValue(VisualElement root, PackerSource source)
        {
            if (source.InputType == InputType.Texture)
            {
                var field = new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false, value = source.ImageTexture };
                field.RegisterValueChangedCallback(e => { source.SetInputTexture(e.newValue as Texture2D); QueuePack(); });
                root.Add(field);
            }
            else if (source.InputType == InputType.Color)
            {
                var field = new ColorField("Color") { value = source.Color };
                field.RegisterValueChangedCallback(e => { source.Color = e.newValue; source.UpdateColorTexture(); QueuePack(); });
                root.Add(field);
            }
            else
            {
                var field = new GradientField("Gradient") { value = source.Gradient ?? new Gradient() };
                Action update = () =>
                {
                    if (source.GradientTexture != null) DestroyImmediate(source.GradientTexture);
                    source.GradientTexture = null;
                    source.UpdateGradientTexture(_config.FileOutput.Resolution);
                };
                field.RegisterValueChangedCallback(e => { source.Gradient = e.newValue; update(); QueuePack(); });
                root.Add(field);
                AddEnum(root, "Direction", source.GradientDirection, v => { source.GradientDirection = v; update(); });
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
                var choices = new[] { "None" }.Concat(Enumerable.Range(1, _config.Sources.Length).Select(n => "Source " + n)).ToList();
                var source = new DropdownField(choices, matches.Length == 0 ? 0 : matches[0].FromTextureIndex + 1);
                source.style.flexGrow = 1; source.style.flexBasis = 0; RetainedWindow.Dropdown(source); row.Add(source);
                if (matches.Length > 1) source.SetValueWithoutNotify("Multiple sources");
                var channel = new DropdownField(Enum.GetNames(typeof(TextureChannelIn)).ToList(), matches.Length == 0 ? i : (int)matches[0].FromChannel);
                channel.style.width = 70; RetainedWindow.Dropdown(channel); row.Add(channel);
                Action update = () =>
                {
                    if (source.index < 0) return;
                    _config.Connections.RemoveAll(c => (int)c.ToChannel == output);
                    if (source.index > 0) _config.Connections.Add(new Connection(source.index - 1, (TextureChannelIn)channel.index, (TextureChannelOut)output));
                    QueuePack();
                };
                source.RegisterValueChangedCallback(e => update());
                channel.RegisterValueChangedCallback(e => update());
            }
            var advanced = Section(root, "Advanced routing", false);
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
                var channel = Section(root, "RGBA"[i] + " output", true);
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
                        var source = new DropdownField("Source", Enumerable.Range(1, _config.Sources.Length).Select(n => "Source " + n).ToList(), Mathf.Clamp(connection.FromTextureIndex, 0, _config.Sources.Length - 1));
                        RetainedWindow.Dropdown(source);
                        source.RegisterValueChangedCallback(e => { change(b => b.Value.FromTextureIndex = source.index); QueuePack(); });
                        route.Add(source);
                        AddEnum(route, "Channel", connection.FromChannel, v => change(b => b.Value.FromChannel = v));
                        AddEnum(route, "Remap", connection.RemappingMode, v => change(b => b.Value.RemappingMode = v));
                        var range = new Vector4Field("Input / output range") { value = connection.Remapping };
                        range.RegisterValueChangedCallback(e => { change(b => b.Value.Remapping = e.newValue); QueuePack(); });
                        route.Add(range);
                        route.Add(new Button(() => { _config.Connections.RemoveAt(routeIndex); rebuild(); QueuePack(); }) { text = "Remove source" });
                    }
                };
                rebuild();
                channel.Add(new Button(() => { _config.Connections.Add(new Connection(Mathf.Min(output, _config.Sources.Length - 1), (TextureChannelIn)output, (TextureChannelOut)output)); rebuild(); QueuePack(); }) { text = "Add source" });
                AddEnum(channel, "Combine", _config.Targets[i].BlendMode, v => _config.Targets[output].BlendMode = v);
                AddEnum(channel, "Invert", _config.Targets[i].Invert, v => _config.Targets[output].Invert = v);
                AddNumber(channel, "When empty", _config.Targets[i].Fallback, v => _config.Targets[output].Fallback = Mathf.Clamp01(v));
            }
        }

        sealed class ConnectionBox { public Connection Value; }

        void BuildAdjustments(VisualElement root)
        {
            var settings = _config.ImageAdjust;
            AddNumber(root, "Brightness", settings.Brightness, v => settings.Brightness = v);
            AddNumber(root, "Hue", settings.Hue, v => settings.Hue = v);
            AddNumber(root, "Saturation", settings.Saturation, v => settings.Saturation = v);
            AddNumber(root, "Rotation", settings.Rotation, v => settings.Rotation = v);
            AddVector(root, "Scale", settings.Scale, v => settings.Scale = v);
            AddVector(root, "Offset", settings.Offset, v => settings.Offset = v);
            root.Add(new Button(() => { _config.ImageAdjust = new ImageAdjust(); root.Clear(); BuildAdjustments(root); QueuePack(); }) { text = "Reset adjustments" });
        }

        void BuildFilter(VisualElement root)
        {
            var details = new VisualElement();
            Action rebuild = () =>
            {
                details.Clear();
                details.style.display = _config.KernelPreset == KernelPreset.None ? DisplayStyle.None : DisplayStyle.Flex;
                var settings = _config.KernelSettings;
                AddNumber(details, "Strength", settings.Strength, v => settings.Strength = v);
                AddNumber(details, "Passes", settings.Loops, v => settings.Loops = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100));
                AddToggle(details, "Two passes", settings.TwoPass, v => settings.TwoPass = v);
                AddToggle(details, "Grayscale", settings.GrayScale, v => settings.GrayScale = v);
                AddToggle(details, "Separate directions", settings.SplitVerticalHorizontal, v => settings.SplitVerticalHorizontal = v);
                for (int i = 0; i < 4; i++)
                {
                    int channel = i;
                    AddToggle(details, "RGBA"[i].ToString(), settings.Channels[i], v => settings.Channels[channel] = v);
                }
                if (_config.KernelPreset != KernelPreset.Custom) return;
                BuildKernel(Section(details, "Horizontal kernel", true), settings.X);
                BuildKernel(Section(details, "Vertical kernel", false), settings.Y);
            };
            AddEnum(root, "Filter", _config.KernelPreset, v => { _config.KernelPreset = v; _config.KernelSettings.LoadPreset(v); rebuild(); });
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
            var size = new Vector2IntField("Resolution") { value = output.Resolution };
            size.Query<IntegerField>().ForEach(field => field.isDelayed = true);
            size.RegisterValueChangedCallback(e => { output.Resolution = new Vector2Int(Mathf.Clamp(e.newValue.x, 1, 8192), Mathf.Clamp(e.newValue.y, 1, 8192)); size.SetValueWithoutNotify(output.Resolution); QueuePack(); });
            root.Add(size);
            AddEnum(root, "Format", output.SaveType, v => output.SaveType = v);
            AddEnum(root, "Color space", output.ColorSpace, v => output.ColorSpace = v);
            AddEnum(root, "Filtering", output.FilterMode, v => output.FilterMode = v);
            AddToggle(root, "Alpha is transparency", output.AlphaIsTransparency, v => output.AlphaIsTransparency = v);
            AddNumber(root, "JPEG quality", output.SaveQuality, v => output.SaveQuality = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100));
            var folder = new TextField("Folder") { value = output.SaveFolder, isDelayed = true };
            folder.RegisterValueChangedCallback(e => output.SaveFolder = e.newValue);
            root.Add(folder);
            var filename = new TextField("Name") { value = output.FileName, isDelayed = true };
            filename.RegisterValueChangedCallback(e => output.FileName = e.newValue);
            root.Add(filename);
        }

        void AddEnum<T>(VisualElement root, string label, T value, Action<T> set) where T : struct
        {
            var field = new DropdownField(label, Enum.GetNames(typeof(T)).ToList(), 0);
            field.SetValueWithoutNotify(value.ToString());
            RetainedWindow.Dropdown(field);
            field.RegisterValueChangedCallback(e => { set((T)Enum.Parse(typeof(T), e.newValue)); QueuePack(); });
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
            _pendingPack = rootVisualElement.schedule.Execute(() => { Pack(); UpdateRetainedPreview(); }).StartingIn(120);
        }

        void UpdateRetainedPreview()
        {
            if (_retainedPreview == null) return;
            if (_retainedChannelPreview != null) { _retainedChannelPreview.Release(); DestroyImmediate(_retainedChannelPreview); _retainedChannelPreview = null; }
            if (_retainedPreviewChannel > 0 && _outputTexture != null)
            {
                _retainedChannelPreview = new RenderTexture(_outputTexture.width, _outputTexture.height, 0);
                ChannelPreviewMaterial.SetFloat("_Channel", _retainedPreviewChannel - 1);
                Graphics.Blit(_outputTexture, _retainedChannelPreview, ChannelPreviewMaterial);
                _retainedPreview.image = _retainedChannelPreview;
            }
            else _retainedPreview.image = _outputTexture;
            _retainedPreview.tintColor = Color.white;
            _retainedStatus.text = _config.FileOutput.Resolution.x + " × " + _config.FileOutput.Resolution.y + " · " + _config.FileOutput.SaveType;
        }
    }
}
#endif
