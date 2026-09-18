#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.TexturePacker
{
    public partial class NodeGUI
    {
        TextureStudioGraph _graph;
        VisualElement _graphSettings;
        VisualElement _graphRoutingSettings;
        VisualElement _graphSourceSettings;

        void OnEnable()
        {
            Undo.undoRedoPerformed -= RestoreStudioGraph;
            Undo.undoRedoPerformed += RestoreStudioGraph;
        }

        void RestoreStudioGraph()
        {
            if (_config == null) return;
            CreateGUI();
            QueuePack();
        }

        void BuildGraphWorkspace()
        {
            var root = rootVisualElement;
            var toolbar = new VisualElement(); toolbar.AddToClassList("thry-studio-toolbar"); root.Add(toolbar);
            var heading = new Label(titleContent.text); heading.AddToClassList("thry-studio-heading"); toolbar.Add(heading);
            var open = new ObjectField(RetainedText.Get("studio_open_graph", "Open")) {
                name = "studio-open-texture", objectType = typeof(Texture2D), allowSceneObjects = false,
                tooltip = RetainedText.Get("studio_open_saved_tip", "Reopen a saved texture and its original sources.")
            };
            open.RegisterValueChangedCallback(e => {
                if (e.newValue is Texture2D texture) TryStudioAction(() => {
                    if (TexturePackerConfig.TryGetFromTexture(texture, out var config)) InitilizeWithData(config);
                    else InitilizeWithOneTexture(texture);
                });
            });
            toolbar.Add(open);
            var previous = new Button { name = "studio-previous-packs", text = RetainedText.Get("studio_previous_packs", "Previous packs") };
            previous.clicked += () => TryStudioAction(() => ShowLoadPreviousProjectDropdown(previous.worldBound));
            toolbar.Add(previous);
            var resetView = new Button(() => _graph?.UpdateViewTransform(Vector3.zero, Vector3.one)) {
                name = "studio-reset-view", text = RetainedText.Get("studio_reset_view", "Reset view")
            };
            toolbar.Add(resetView);
            var saveAs = new Button(() => TryStudioAction(SaveGraphTextureAs)) {
                name = "save-texture-as", text = RetainedText.Get("studio_save_as", "Save as…"),
                tooltip = RetainedText.Get("studio_save_as_tip", "Choose a folder in Assets and save the texture there.")
            };
            var save = new Button(() => TryStudioAction(() => SaveGraphTexture())) { name = "save-texture", text = RetainedText.Get("save", "Save") };
            save.AddToClassList("thry-primary-action"); toolbar.Add(save);
            toolbar.Add(saveAs);

            var help = new Label(RetainedText.Get("studio_graph_pin_help", "Drag pins to connect.  Alt-click a pin to remove its wires.  Shift-drag to combine sources.  Delete removes a selected wire."));
            help.text += "  " + RetainedText.Get("studio_pan_help", "Middle-drag to pan the canvas.");
            help.AddToClassList("thry-studio-graph-help"); root.Add(help);
            var workspace = new VisualElement(); workspace.AddToClassList("thry-studio-graph-workspace"); root.Add(workspace);
            _graph = new TextureStudioGraph(this);
            workspace.Add(_graph);
            _graph.StretchToParentSize();

            _graphSettings = new VisualElement { name = "studio-graph-settings" };
            _graphSettings.AddToClassList("thry-studio-settings-panel");
            _graph.AddOutputSettings(_graphSettings);
            var settingsHeader = new VisualElement(); settingsHeader.AddToClassList("thry-studio-settings-header"); _graphSettings.Add(settingsHeader);
            settingsHeader.Add(new Label(RetainedText.Get("studio_graph_settings", "Settings")));
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "studio-settings-scroll" }; _graphSettings.Add(scroll);
            BuildOutput(Section(scroll, RetainedText.Get("studio_save_options", "Save options"), true));
            _graphRoutingSettings = Section(scroll, RetainedText.Get("studio_channel_settings", "Channels & blending"), false);
            BuildAdvancedRouting(_graphRoutingSettings);
            BuildAdjustments(Section(scroll, RetainedText.Get("studio_adjustments", "Image adjustments"), false));
            BuildFilter(Section(scroll, RetainedText.Get("studio_filter", "Filter"), false));
            _graphSourceSettings = Section(scroll, RetainedText.Get("studio_source_settings", "Source settings"), false);
            RefreshGraphSourceSettings();
            BuildGraphExports(Section(scroll, RetainedText.Get("studio_export_channels", "Export individual channels"), false));

            _retainedStatus = new Label { name = "texture-studio-status" };
            _retainedStatus.AddToClassList("thry-studio-graph-status"); root.Add(_retainedStatus);
        }

        void SaveGraphTextureAs()
        {
            string initialFolder = string.IsNullOrWhiteSpace(_config.FileOutput.SaveFolder)
                ? Application.dataPath : System.IO.Path.GetFullPath(_config.FileOutput.SaveFolder);
            if (!System.IO.Directory.Exists(initialFolder)) initialFolder = Application.dataPath;
            string folder = EditorUtility.OpenFolderPanel(RetainedText.Get("studio_save_as", "Save as…"),
                initialFolder, "");
            SaveGraphTextureInFolder(folder);
        }

        bool SaveGraphTextureInFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string relative = FileUtil.GetProjectRelativePath(folder).Replace('\\', '/');
            if (relative != "Assets" && !relative.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Choose an asset folder", "Save the packed texture inside this project's Assets folder.", "OK");
                return false;
            }
            string previous = _config.FileOutput.SaveFolder;
            bool saved = false;
            try
            {
                _config.FileOutput.SaveFolder = relative;
                saved = SaveGraphTexture();
            }
            finally { if (!saved) _config.FileOutput.SaveFolder = previous; }
            return saved;
        }

        bool SaveGraphTexture()
        {
            _pendingPack?.Pause();
            _config.RequireResolvedSources();
            Pack();
            var importer = Packer.Save(_outputTexture, _config);
            if (importer == null) return false;
            _associatedImporter = importer;
            OnSave?.Invoke(AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath));
            _retainedStatus.text = RetainedText.Get("studio_saved", "Saved") + " " + importer.assetPath;
            _retainedStatus.tooltip = importer.assetPath;
            return true;
        }

        void BuildGraphExports(VisualElement root)
        {
            var channels = new VisualElement(); channels.AddToClassList("thry-components");
            channels.AddToClassList("thry-studio-export-channels"); root.Add(channels);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var channel = new Toggle("RGBA"[i].ToString()) { value = _channel_export[i] };
                channel.RegisterValueChangedCallback(e => _channel_export[index] = e.newValue);
                channels.Add(channel);
            }
            root.Add(new Button(() => TryStudioAction(() => { _config.RequireResolvedSources(); ExportChannels(true); })) { text = RetainedText.Get("studio_export_grayscale", "Export as grayscale") });
            root.Add(new Button(() => TryStudioAction(() => { _config.RequireResolvedSources(); ExportChannels(false); })) { text = RetainedText.Get("studio_export_color", "Export as color") });
        }

        void GraphSetTexture(int index, Texture2D texture)
        {
            Undo.RecordObject(this, "Choose texture source");
            _config.Sources[index].SetInputTexture(texture);
            _graph?.RefreshSourceFields();
            RefreshGraphSourceSettings();
            QueuePack();
        }

        void RefreshGraphSourceSettings()
        {
            if (_graphSourceSettings == null) return;
            _graphSourceSettings.Clear();
            for (int i = 0; i < _config.Sources.Length; i++) BuildSource(_graphSourceSettings, i);
        }

        void ConnectGraphChannel(int source, TextureChannelIn channel, TextureChannelOut output, bool append)
        {
            if (source < 0 || source >= _config.Sources.Length || (int)output < 0 || (int)output >= 4) return;
            var existing = _config.Connections.Where(c => c.ToChannel == output).ToArray();
            if (append && existing.Any(c => c.FromTextureIndex == source && c.FromChannel == channel)) return;
            Undo.RecordObject(this, "Connect texture channel");
            var connection = !append && existing.Length == 1 ? existing[0] : new Connection(source, channel, output);
            connection.FromTextureIndex = source; connection.FromChannel = channel; connection.ToChannel = output;
            if (!append) _config.Connections.RemoveAll(c => c.ToChannel == output);
            _config.Connections.Add(connection);
            RefreshGraphRoutingSettings();
            QueuePack();
        }

        void DisconnectGraphChannels(IEnumerable<int> indices)
        {
            var valid = indices.Distinct().Where(index => index >= 0 && index < _config.Connections.Count).OrderByDescending(index => index).ToArray();
            if (valid.Length == 0) return;
            Undo.RecordObject(this, "Disconnect texture channel");
            foreach (int index in valid) _config.Connections.RemoveAt(index);
            RefreshGraphRoutingSettings();
            QueuePack();
        }

        void RefreshGraphRoutingSettings()
        {
            if (_graphRoutingSettings == null) return;
            _graphRoutingSettings.Clear(); BuildAdvancedRouting(_graphRoutingSettings);
        }

        sealed class StudioEdge : Edge
        {
            public StudioEdge() { }
            protected override EdgeControl CreateEdgeControl() => new StudioCurve();
        }

        // Keep GraphView's ports, drag handling, selection and colors, with smooth wires.
        sealed class StudioCurve : EdgeControl
        {
            const int Steps = 48;
            readonly Vector2[] _points = new Vector2[Steps + 1];

            public StudioCurve() { generateVisualContent = DrawCurve; }

            protected override void ComputeControlPoints()
            {
                base.ComputeControlPoints();
                if (controlPoints == null) return;
                float tangent = Mathf.Max(40, Mathf.Abs(to.x - from.x) * .45f);
                controlPoints[1] = from + Vector2.right * tangent;
                controlPoints[2] = to - Vector2.right * tangent;
            }

            bool SampleCurve()
            {
                if (parent == null || controlPoints == null) return false;
                var a = parent.ChangeCoordinatesTo(this, controlPoints[0]);
                var b = parent.ChangeCoordinatesTo(this, controlPoints[1]);
                var c = parent.ChangeCoordinatesTo(this, controlPoints[2]);
                var d = parent.ChangeCoordinatesTo(this, controlPoints[3]);
                for (int i = 0; i <= Steps; i++)
                {
                    float t = i / (float)Steps, u = 1 - t;
                    _points[i] = u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
                }
                return true;
            }

            void DrawCurve(MeshGenerationContext context)
            {
                if (!SampleCurve()) return;
                var mesh = context.Allocate((Steps + 1) * 4, Steps * 18);
                float halfWidth = Mathf.Max(1, edgeWidth * .5f);
                for (int i = 0; i <= Steps; i++)
                {
                    var direction = (_points[Mathf.Min(Steps, i + 1)] - _points[Mathf.Max(0, i - 1)]).normalized;
                    var normal = new Vector2(-direction.y, direction.x);
                    var color = Color.Lerp(outputColor, inputColor, i / (float)Steps);
                    for (int strip = 0; strip < 4; strip++)
                    {
                        float distance = strip == 0 ? -halfWidth - 1 : strip == 1 ? -halfWidth : strip == 2 ? halfWidth : halfWidth + 1;
                        var tint = color; if (strip == 0 || strip == 3) tint.a = 0;
                        Vector2 point = _points[i] + normal * distance;
                        mesh.SetNextVertex(new Vertex { position = new Vector3(point.x, point.y, Vertex.nearZ), tint = tint });
                    }
                }
                for (int i = 0; i < Steps; i++) for (int strip = 0; strip < 3; strip++)
                {
                    ushort a = (ushort)(i * 4 + strip), b = (ushort)(a + 4);
                    mesh.SetNextIndex(a); mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(a + 1));
                    mesh.SetNextIndex((ushort)(a + 1)); mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1));
                }
            }

            public override bool ContainsPoint(Vector2 localPoint)
            {
                if (!SampleCurve()) return false;
                for (int i = 1; i <= Steps; i++)
                {
                    var segment = _points[i] - _points[i - 1];
                    float t = Mathf.Clamp01(Vector2.Dot(localPoint - _points[i - 1], segment) / Mathf.Max(.001f, segment.sqrMagnitude));
                    if ((localPoint - _points[i - 1] - segment * t).sqrMagnitude <= 36) return true;
                }
                return false;
            }
        }

        sealed class TextureStudioGraph : GraphView, IDisposable
        {
            readonly NodeGUI _owner;
            readonly List<StudioSourceNode> _sources = new List<StudioSourceNode>();
            readonly Node _output;
            readonly Port[] _inputs = new Port[4];
            Material _previewMaterial;
            IVisualElementScheduledItem _sync;
            bool _syncing;
            bool _append;
            bool _disposed;

            internal TextureStudioGraph(NodeGUI owner)
            {
                _owner = owner; name = "texture-studio-graph";
                AddToClassList("thry-studio-graph");
                var grid = new GridBackground(); Insert(0, grid); grid.StretchToParentSize();
                // Pan the board while keeping its nodes in their assigned slots.
                this.AddManipulator(new ContentDragger());
                RegisterCallback<KeyDownEvent>(e => {
                    if (e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace) return;
                    for (var target = e.target as VisualElement; target != null && target != this; target = target.parent)
                        if (target.ClassListContains("unity-base-field")) return;
                    var selectedWires = selection.OfType<Edge>().ToList();
                    if (selectedWires.Count == 0) return;
                    DeleteElements(selectedWires); e.StopPropagation(); e.PreventDefault();
                });
                for (int i = 0; i < owner._config.Sources.Length; i++)
                {
                    var node = new StudioSourceNode(owner, i); _sources.Add(node); AddElement(node);
                }
                _output = NewNode("studio-output-node", RetainedText.Get("studio_output_texture", "Output texture"));
                _output.AddToClassList("thry-studio-output-node");
                var body = _output.topContainer; body.AddToClassList("thry-studio-output-body");
                StylePortColumn(_output.inputContainer);
                body.style.backgroundColor = Color.clear;
                for (int i = 0; i < 4; i++)
                {
                    _inputs[i] = MakePort(Direction.Input, i, "output-port-" + "RGBA"[i]);
                    _inputs[i].userData = i;
                    _inputs[i].tooltip = RetainedText.Get("studio_output_pin_actions_tip", "Drop a source channel here. Hold Shift to add another source. Alt-click to remove all wires from this pin.");
                    _output.inputContainer.Add(_inputs[i]);
                }
                owner._retainedPreview = new Image { name = "texture-preview", image = owner._outputTexture, scaleMode = ScaleMode.ScaleToFit };
                owner._retainedPreview.AddToClassList("thry-studio-result-image"); body.Add(owner._retainedPreview);
                owner._retainedPreviewSelector = new DropdownField(RetainedText.Get("studio_preview", "Preview"), new List<string> {
                    RetainedText.Get("studio_combined", "Combined"), RetainedText.Get("red", "Red"), RetainedText.Get("green", "Green"), RetainedText.Get("blue", "Blue"), RetainedText.Get("alpha", "Alpha")
                }, owner._retainedPreviewChannel);
                RetainedWindow.Dropdown(owner._retainedPreviewSelector);
                owner._retainedPreviewSelector.RegisterValueChangedCallback(e => { owner._retainedPreviewChannel = owner._retainedPreviewSelector.index; owner.UpdateRetainedPreview(); });
                _output.extensionContainer.Add(owner._retainedPreviewSelector);
                _output.RefreshExpandedState(); _output.RefreshPorts(); AddElement(_output);
                graphViewChanged = ApplyGraphChange;
                RegisterCallback<GeometryChangedEvent>(e => LayoutBoard());
                SyncConnections();
            }

            static Node NewNode(string name, string title)
            {
                var node = new Node { name = name, title = title, capabilities = (Capabilities)0 };
                node.AddToClassList("thry-studio-node");
                return node;
            }

            internal void AddOutputSettings(VisualElement settings)
            {
                _output.extensionContainer.Add(settings);
            }

            static void StylePortColumn(VisualElement column, float width = 48)
            {
                // Override the built-in Node template's equal-width port columns.
                column.style.flexGrow = 0; column.style.flexShrink = 0;
                column.style.flexBasis = width; column.style.width = width;
                column.style.backgroundColor = Color.clear;
            }

            internal static Port MakePort(Direction direction, int channel, string name)
            {
                var port = Port.Create<StudioEdge>(Orientation.Horizontal, direction, Port.Capacity.Multi, typeof(float));
                port.name = name; port.portName = channel < 4 ? "RGBA"[channel].ToString() : channel == 4 ? "MAX" : "None";
                port.AddToClassList("thry-studio-channel-" + (channel >= 0 && channel < 4 ? "rgba"[channel].ToString() : "max"));
                port.RegisterCallback<CustomStyleResolvedEvent>(e => {
                    // GraphView caches wire colors separately from the port's USS color.
                    port.schedule.Execute(() => {
                        foreach (var edge in port.connections) edge.UpdateEdgeControl();
                    });
                });
                port.edgeConnector.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Shift });
                // Captured mouse events go straight to the port in Unity 2022.3,
                // bypassing the graph's ancestor callbacks. Read Shift here before
                // the connector handles the drop so mid-drag changes take effect.
                port.RegisterCallback<MouseDownEvent>(e => UpdateAppend(e.shiftKey), TrickleDown.TrickleDown);
                port.RegisterCallback<MouseMoveEvent>(e => UpdateAppend(e.shiftKey), TrickleDown.TrickleDown);
                port.RegisterCallback<MouseUpEvent>(e => UpdateAppend(e.shiftKey), TrickleDown.TrickleDown);
                void UpdateAppend(bool append)
                {
                    var graph = port.GetFirstAncestorOfType<TextureStudioGraph>();
                    if (graph != null) graph._append = append;
                }
                port.RegisterCallback<MouseDownEvent>(e => {
                    if (e.button != 0 || !e.altKey) return;
                    port.GetFirstAncestorOfType<TextureStudioGraph>()?.DisconnectPin(port);
                    e.StopImmediatePropagation(); e.PreventDefault();
                }, TrickleDown.TrickleDown);
                return port;
            }

            void DisconnectPin(Port port)
            {
                var connections = _owner._config.Connections;
                var indices = Enumerable.Range(0, connections.Count).Where(index => {
                    var connection = connections[index];
                    if (port.userData is SourcePort source)
                        return connection.FromTextureIndex == source.Index && connection.FromChannel == source.Channel;
                    return port.userData is int output && (int)connection.ToChannel == output;
                });
                _owner.DisconnectGraphChannels(indices);
            }

            void LayoutBoard()
            {
                float width = contentRect.width, height = contentRect.height;
                if (width < 1 || height < 1) return;
                // Keep the controls usable below the board's preferred size; panning
                // exposes the remainder on small screens.
                width = Mathf.Max(width, 810);
                height = Mathf.Max(height, 600);
                float rightWidth = Mathf.Clamp(width * .38f, 290, 380);
                float slot = (height - 28 - 12 * (_sources.Count - 1)) / Mathf.Max(1, _sources.Count);
                float leftWidth = Mathf.Clamp(slot + 16, 180, 230);
                for (int i = 0; i < _sources.Count; i++)
                {
                    PlaceNode(_sources[i], new Rect(20, 14 + i * (slot + 12), leftWidth, slot));
                    _sources[i].FitPorts(slot);
                }
                float rightX = width - rightWidth - 20;
                float previewHeight = Mathf.Clamp(height * .48f, 260, 340) - 80;
                PlaceNode(_output, new Rect(rightX, 14, rightWidth, height - 28));
                _output.topContainer.style.height = previewHeight;
                _output.topContainer.style.flexBasis = previewHeight;
                _output.topContainer.style.flexGrow = 0;
                _output.topContainer.style.flexShrink = 0;
            }

            static void PlaceNode(Node node, Rect bounds)
            {
                node.SetPosition(bounds);
                node.style.width = bounds.width; node.style.height = bounds.height;
            }

            public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
            {
                return ports.Where(port => port != startPort && port.direction != startPort.direction && port.node != startPort.node).ToList();
            }

            GraphViewChange ApplyGraphChange(GraphViewChange change)
            {
                if (_syncing || _disposed) return change;
                if (change.elementsToRemove != null)
                    _owner.DisconnectGraphChannels(change.elementsToRemove.OfType<Edge>().Where(edge => edge.userData is int).Select(edge => (int)edge.userData));
                if (change.edgesToCreate != null)
                    foreach (var edge in change.edgesToCreate)
                    {
                        if (!(edge.output.userData is SourcePort source) || !(edge.input.userData is int output)) continue;
                        _owner.ConnectGraphChannel(source.Index, source.Channel, (TextureChannelOut)output, _append);
                    }
                return change;
            }

            public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
            {
                var edge = evt.target as Edge ?? (evt.target as VisualElement)?.GetFirstAncestorOfType<Edge>();
                if (edge != null)
                    evt.menu.AppendAction(RetainedText.Get("studio_disconnect", "Disconnect"), action => DeleteElements(new[] { edge }));
            }

            internal void ScheduleConnectionsRefresh()
            {
                if (_disposed) return;
                _sync?.Pause(); _sync = schedule.Execute(SyncConnections);
            }

            internal void SyncConnections()
            {
                if (_disposed) return;
                _syncing = true;
                try
                {
                    foreach (var edge in edges.ToList()) { edge.input?.Disconnect(edge); edge.output?.Disconnect(edge); RemoveElement(edge); }
                    for (int i = 0; i < _owner._config.Connections.Count; i++)
                    {
                        var connection = _owner._config.Connections[i];
                        if (connection.FromTextureIndex < 0 || connection.FromTextureIndex >= _sources.Count || (int)connection.ToChannel < 0 || (int)connection.ToChannel >= 4) continue;
                        var port = _sources[connection.FromTextureIndex].EnsurePort(connection.FromChannel);
                        var edge = port.ConnectTo<StudioEdge>(_inputs[(int)connection.ToChannel]);
                        edge.userData = i; edge.name = "studio-wire-" + i;
                        AddElement(edge);
                    }
                }
                finally { _syncing = false; }
            }

            internal void RefreshSourceFields()
            {
                foreach (var source in _sources) source.RefreshFields();
            }

            internal void RefreshPreviews()
            {
                if (_disposed) return;
                if (_previewMaterial == null)
                {
                    var shader = Shader.Find("Hidden/Thry/StudioSourcePreview");
                    if (shader == null) return;
                    _previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                using (PackerSource.KeepDecodedSources(_owner._config.Sources))
                    foreach (var source in _sources) source.RefreshPreview(_previewMaterial);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true; _sync?.Pause(); graphViewChanged = null;
                foreach (var source in _sources) source.Dispose();
                if (_previewMaterial != null) DestroyImmediate(_previewMaterial);
            }

            internal sealed class SourcePort
            {
                internal int Index;
                internal TextureChannelIn Channel;
            }

            sealed class StudioSourceNode : Node, IDisposable
            {
                readonly NodeGUI _owner;
                readonly int _index;
                readonly VisualElement _fieldRoot;
                readonly Image _image;
                readonly Label _empty;
                readonly Dictionary<TextureChannelIn, Port> _ports = new Dictionary<TextureChannelIn, Port>();
                InputType _fieldType;
                RenderTexture _thumbnail;
                internal StudioSourceNode(NodeGUI owner, int index)
                {
                    _owner = owner; _index = index;
                    name = "studio-source-node-" + index; title = RetainedText.Get("studio_texture_node", "Texture") + " " + (index + 1);
                    capabilities = (Capabilities)0;
                    AddToClassList("thry-studio-node"); AddToClassList("thry-studio-source-node");
                    _fieldRoot = new VisualElement(); _fieldRoot.AddToClassList("thry-studio-node-field"); mainContainer.Q("contents").Insert(0, _fieldRoot);
                    var body = topContainer; body.AddToClassList("thry-studio-source-body");
                    StylePortColumn(outputContainer, 64);
                    body.style.backgroundColor = Color.clear;
                    var previewSlot = new VisualElement();
                    previewSlot.style.flexGrow = 1; previewSlot.style.flexBasis = 0; previewSlot.style.minWidth = 0;
                    body.Insert(0, previewSlot);
                    var frame = new VisualElement(); frame.AddToClassList("thry-studio-source-frame"); previewSlot.Add(frame);
                    previewSlot.RegisterCallback<GeometryChangedEvent>(e => {
                        float side = Mathf.Max(0, Mathf.Floor(Mathf.Min(previewSlot.contentRect.height, previewSlot.contentRect.width)));
                        frame.style.width = side;
                        frame.style.height = side;
                        frame.style.left = (previewSlot.contentRect.width - side) * .5f;
                        frame.style.top = (previewSlot.contentRect.height - side) * .5f;
                    });
                    _image = new Image { name = "studio-source-preview-" + index, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    _image.StretchToParentSize(); frame.Add(_image);
                    _empty = new Label(RetainedText.Get("studio_drop_texture", "Drop texture")); frame.Add(_empty);
                    for (int channel = 0; channel < 5; channel++) EnsurePort((TextureChannelIn)channel);
                    frame.RegisterCallback<DragUpdatedEvent>(e => {
                        if (!DragAndDrop.objectReferences.OfType<Texture2D>().Any()) return;
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation();
                    });
                    frame.RegisterCallback<DragPerformEvent>(e => {
                        var texture = DragAndDrop.objectReferences.OfType<Texture2D>().FirstOrDefault();
                        if (texture == null) return;
                        DragAndDrop.AcceptDrag(); owner.GraphSetTexture(index, texture); e.StopPropagation();
                    });
                    BuildField(); RefreshExpandedState(); RefreshPorts();
                }

                internal Port EnsurePort(TextureChannelIn channel)
                {
                    if (_ports.TryGetValue(channel, out var port)) return port;
                    port = MakePort(Direction.Output, (int)channel, "source-port-" + _index + "-" + channel);
                    port.userData = new SourcePort { Index = _index, Channel = channel };
                    port.tooltip = channel == TextureChannelIn.Max
                        ? RetainedText.Get("studio_max_pin_actions_tip", "Highest of R, G, and B at each pixel. Ignores alpha. Useful for black-and-white masks. Alt-click to remove this pin's wires.")
                        : RetainedText.Get("studio_source_pin_actions_tip", "Drag this channel to R, G, B, or A on the output texture. Alt-click to remove this pin's wires.");
                    _ports[channel] = port; outputContainer.Add(port); RefreshPorts();
                    FitPorts(GetPosition().height);
                    return port;
                }

                internal void FitPorts(float nodeHeight)
                {
                    if (nodeHeight <= 0 || float.IsNaN(nodeHeight)) return;
                    float rowHeight = Mathf.Clamp((nodeHeight - 60) / _ports.Count, 12, 19);
                    foreach (var port in _ports.Values)
                    {
                        port.style.height = rowHeight; port.style.minHeight = rowHeight;
                        port.style.flexShrink = 1;
                    }
                }

                void BuildField()
                {
                    _fieldRoot.Clear(); var source = _owner._config.Sources[_index]; _fieldType = source.InputType;
                    if (_fieldType == InputType.Texture)
                    {
                        var field = new ObjectField { name = "graph-source-texture-" + _index, objectType = typeof(Texture2D), allowSceneObjects = false, value = source.ImageTexture };
                        field.RegisterValueChangedCallback(e => _owner.GraphSetTexture(_index, e.newValue as Texture2D));
                        _fieldRoot.Add(field);
                        _fieldRoot.Add(new Button(() => _owner.GraphSetTexture(_index, null)) { name = "graph-clear-source-" + _index, text = "×", tooltip = RetainedText.Get("studio_clear_source", "Clear texture") });
                    }
                    else if (_fieldType == InputType.Color)
                    {
                        var field = new ColorField { value = source.Color };
                        field.RegisterValueChangedCallback(e => { Undo.RecordObject(_owner, "Change source color"); source.Color = e.newValue; source.UpdateColorTexture(); _owner.RefreshGraphSourceSettings(); _owner.QueuePack(); });
                        _fieldRoot.Add(field);
                    }
                    else
                    {
                        var field = new GradientField { value = source.Gradient ?? new Gradient() };
                        field.RegisterValueChangedCallback(e => {
                            Undo.RecordObject(_owner, "Change source gradient"); source.Gradient = e.newValue;
                            source.DisposeGeneratedTextures(); source.UpdateGradientTexture(_owner._config.FileOutput.Resolution); _owner.RefreshGraphSourceSettings(); _owner.QueuePack();
                        });
                        _fieldRoot.Add(field);
                    }
                }

                internal void RefreshFields()
                {
                    var source = _owner._config.Sources[_index];
                    if (_fieldType != source.InputType) BuildField();
                    _fieldRoot.Q<ObjectField>()?.SetValueWithoutNotify(source.ImageTexture);
                    _fieldRoot.Q<ColorField>()?.SetValueWithoutNotify(source.Color);
                    _fieldRoot.Q<GradientField>()?.SetValueWithoutNotify(source.Gradient ?? new Gradient());
                    var clear = _fieldRoot.Q<Button>(); if (clear != null) clear.SetEnabled(source.ImageTexture != null || source.MissingImageReference);
                    EnableInClassList("thry-studio-source-missing", source.MissingImageReference);
                    _empty.text = source.MissingImageReference ? RetainedText.Get("studio_missing_texture", "Missing texture") : RetainedText.Get("studio_drop_texture", "Drop texture");
                }

                internal void RefreshPreview(Material material)
                {
                    RefreshFields(); var source = _owner._config.Sources[_index];
                    var texture = source.Texture == null ? null : source.ComputeShaderTexture;
                    _empty.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
                    _image.style.display = texture == null ? DisplayStyle.None : DisplayStyle.Flex;
                    if (texture == null) { ReleaseThumbnail(); return; }
                    float scale = Mathf.Min(1, 128f / Mathf.Max(texture.width, texture.height));
                    int width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
                    if (_thumbnail == null || _thumbnail.width != width || _thumbnail.height != height)
                    {
                        ReleaseThumbnail();
                        _thumbnail = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
                        _thumbnail.Create();
                    }
                    var active = RenderTexture.active;
                    try { Graphics.Blit(texture, _thumbnail, material); }
                    finally { RenderTexture.active = active; }
                    _image.image = _thumbnail;
                }

                void ReleaseThumbnail()
                {
                    _image.image = null;
                    if (_thumbnail != null) { _thumbnail.Release(); DestroyImmediate(_thumbnail); _thumbnail = null; }
                }
                public void Dispose() => ReleaseThumbnail();
            }
        }
    }
}
#endif
