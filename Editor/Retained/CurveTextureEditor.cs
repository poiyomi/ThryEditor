#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class CurveTextureEditor : EditorWindow
    {
        [SerializeField] AnimationCurve _curve;
        [SerializeField] int _selected;
        [SerializeField] bool _vertical;
        internal bool Vertical
        {
            get => _vertical;
            set
            {
                _vertical = value;
                rootVisualElement.Q<DropdownField>("curve-direction")?.SetValueWithoutNotify(value ? "Vertical (bottom to top)" : "Horizontal (left to right)");
            }
        }
        [SerializeField] string _notice, _applyLabel;
        [SerializeField] Rect _view = new Rect(-.1f, -.2f, 1.2f, 1.4f);
        [SerializeField] TextureData _settings;
        Action<AnimationCurve> _apply;
        Func<bool> _canApply;
        readonly Stack<AnimationCurve> _undo = new Stack<AnimationCurve>();
        readonly Stack<AnimationCurve> _redo = new Stack<AnimationCurve>();
        FloatField _time, _value;
        Button _applyButton;
        Label _selection;
        HelpBox _error, _unavailable;
        IMGUIContainer _graph, _preview;
        int _dragPart; // 1 = key, 2 = incoming handle, 3 = outgoing handle.
        bool _dragged;
        bool _panning;
        Vector2 _panStart;
        Rect _panView;
        readonly Vector3[] _line = new Vector3[257];

        internal static CurveTextureEditor Open(AnimationCurve curve, TextureData settings, string caption,
            bool assigned, string notice, Action<AnimationCurve> apply, Func<bool> canApply)
        {
            var window = CreateInstance<CurveTextureEditor>();
            window._curve = BoundedCurve.Fit(curve);
            window._settings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(settings));
            window._notice = notice;
            window._applyLabel = assigned ? "Apply & Assign" : "Create & Assign";
            window._apply = apply; window._canApply = canApply;
            window.titleContent = new GUIContent("Curve Editor", caption);
            window.minSize = new Vector2(560, 600);
            window.position = new Rect(240, 120, 660, 650);
            window.FitView();
            window.ShowUtility();
            return window;
        }

        public void CreateGUI()
        {
            if (_curve == null) _curve = BoundedCurve.Preset("Linear");
            if (_settings == null) _settings = new TextureData();
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog");
            var toolbar = Row(root);
            toolbar.style.justifyContent = Justify.SpaceBetween;
            var presets = new DropdownField(new List<string> { "Linear", "Reverse", "Smooth", "Smooth reverse",
                "Ease in", "Ease out", "Pulse", "Valley", "Triangle", "Step", "Zero", "One" }, -1)
                { name = "curve-presets", tooltip = "Replace the curve with a preset" };
            presets.style.width = 150;
            presets.SetValueWithoutNotify("Presets");
            presets.RegisterValueChangedCallback(e =>
            {
                Change(BoundedCurve.Preset(e.newValue)); FitView();
                presets.SetValueWithoutNotify("Presets");
            });
            RetainedWindow.Dropdown(presets);
            toolbar.Add(presets);
            var controls = Row(toolbar);
            controls.style.marginTop = 0;
            controls.style.marginLeft = StyleKeyword.Auto;
            controls.style.flexShrink = 0;
            controls.Add(new Button(() =>
            {
                _selected = _curve.length - 1 - _selected;
                Change(BoundedCurve.Flip(_curve)); FitView();
            }) { text = "Flip", name = "curve-flip", tooltip = "Reverse the curve from end to start" });
            foreach (string mode in new[] { "Smooth", "Linear" })
            {
                string tangentMode = mode;
                controls.Add(new Button(() => Change(BoundedCurve.Tangents(_curve, _selected, tangentMode)))
                { text = mode + " handles", name = "curve-tangents-" + mode });
            }
            _graph = new IMGUIContainer(DrawGraph) { name = "bounded-curve-graph", focusable = true };
            _graph.style.flexGrow = 1; _graph.style.minHeight = 240; root.Add(_graph);
            var hint = new Label("Double-click to add · Delete to remove · Scroll to zoom · Middle-drag to pan · F to fit");
            hint.AddToClassList("thry-muted"); hint.style.whiteSpace = WhiteSpace.Normal; root.Add(hint);

            var coordinates = Row(root);
            _selection = new Label(); _selection.style.minWidth = 70; _selection.style.alignSelf = Align.Center; coordinates.Add(_selection);
            _time = Coordinate(coordinates, "Position", "curve-position");
            _value = Coordinate(coordinates, "Value", "curve-value");
            _time.RegisterValueChangedCallback(e => { Change(BoundedCurve.Move(_curve, _selected, e.newValue, _curve[_selected].value)); FitView(); });
            _value.RegisterValueChangedCallback(e => { Change(BoundedCurve.Move(_curve, _selected, _curve[_selected].time, e.newValue)); FitView(); });

            var direction = new DropdownField("Direction", new List<string> { "Horizontal (left to right)", "Vertical (bottom to top)" }, _vertical ? 1 : 0)
                { name = "curve-direction" };
            direction.RegisterValueChangedCallback(e => Vertical = e.newValue == "Vertical (bottom to top)");
            RetainedWindow.Dropdown(direction); root.Add(direction);

            var previewTitle = new Label("Texture preview · " + _settings.width + " × " + _settings.height + " · " + char.ToUpperInvariant(_settings.channel) + " channel");
            previewTitle.AddToClassList("thry-muted"); previewTitle.style.marginTop = 8; root.Add(previewTitle);
            _preview = new IMGUIContainer(DrawPreview) { name = "curve-texture-preview" };
            _preview.style.height = 24; _preview.style.flexShrink = 0; root.Add(_preview);
            if (!string.IsNullOrEmpty(_notice))
            {
                var note = new Label(_notice); note.style.whiteSpace = WhiteSpace.Normal;
                note.AddToClassList("thry-muted"); root.Add(note);
            }
            _unavailable = new HelpBox("", HelpBoxMessageType.Info) { name = "curve-target-unavailable" }; root.Add(_unavailable);
            _error = new HelpBox("", HelpBoxMessageType.Error) { name = "curve-save-error" };
            _error.style.display = DisplayStyle.None; root.Add(_error);
            var actions = Row(root); actions.AddToClassList("thry-dialog-actions");
            _applyButton = new Button(Apply) { text = _applyLabel ?? "Create & Assign", name = "apply-curve" };
            _applyButton.AddToClassList("thry-primary-action"); actions.Add(_applyButton);
            actions.Add(new Button(Close) { text = "Cancel", name = "cancel-curve" });
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey) || (e.keyCode != KeyCode.Z && e.keyCode != KeyCode.Y)) return;
                if (e.keyCode == KeyCode.Y || e.shiftKey) RedoDraft(); else UndoDraft();
                e.PreventDefault(); e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            RetainedWindow.Shortcuts(root, Close);
            var availability = _applyButton.schedule.Execute(UpdateAvailability).Every(200);
            _applyButton.RegisterCallback<DetachFromPanelEvent>(e => availability.Pause());
            _applyButton.RegisterCallback<AttachToPanelEvent>(e => availability.Resume());
            Synchronize(); UpdateAvailability();
        }
        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 6; parent.Add(row); return row;
        }
        static FloatField Coordinate(VisualElement row, string label, string name)
        {
            var field = new FloatField(label) { name = name, isDelayed = true };
            field.style.flexGrow = 1; field.style.flexBasis = 170; field.style.minWidth = 150;
            field.Q<Label>().style.minWidth = 58; field.Q<Label>().style.width = 58;
            row.Add(field); return field;
        }
        void Synchronize()
        {
            _selected = Mathf.Clamp(_selected, 0, _curve.length - 1);
            _selection.text = "Point " + (_selected + 1);
            _time.SetValueWithoutNotify(_curve[_selected].time);
            _value.SetValueWithoutNotify(_curve[_selected].value);
            _graph.MarkDirtyRepaint(); _preview.MarkDirtyRepaint(); Repaint();
        }
        void Remember() { _undo.Push(BoundedCurve.Copy(_curve)); _redo.Clear(); }
        void Change(AnimationCurve curve)
        {
            Remember(); _curve = curve; Synchronize();
        }
        void DeleteSelected() { if (_curve.length > 1) Change(BoundedCurve.Remove(_curve, _selected)); }
        void UndoDraft()
        {
            if (_undo.Count == 0) return;
            _redo.Push(BoundedCurve.Copy(_curve)); _curve = _undo.Pop(); Synchronize();
        }
        void RedoDraft()
        {
            if (_redo.Count == 0) return;
            _undo.Push(BoundedCurve.Copy(_curve)); _curve = _redo.Pop(); Synchronize();
        }
        void UpdateAvailability()
        {
            bool available = _apply != null && _canApply != null && _canApply();
            _applyButton.SetEnabled(available);
            _unavailable.style.display = available || _apply == null ? DisplayStyle.None : DisplayStyle.Flex;
            _unavailable.text = "The source material or texture changed, or is no longer editable. Reopen the curve editor to assign a texture.";
        }
        void Apply()
        {
            if (_apply == null || _canApply == null || !_canApply()) { UpdateAvailability(); return; }
            try { _apply(BoundedCurve.Copy(_curve)); Close(); }
            catch (Exception e)
            {
                _error.text = "Could not save the curve. Your draft is still available.\n" + e.Message;
                _error.style.display = DisplayStyle.Flex;
            }
        }
        Vector2 Pixel(Rect rect, Vector2 point) => new Vector2(rect.x + (point.x - _view.x) / _view.width * rect.width, rect.yMax - (point.y - _view.y) / _view.height * rect.height);
        Vector2 Unit(Rect rect, Vector2 point) => new Vector2(_view.x + (point.x - rect.x) / rect.width * _view.width, _view.y + (rect.yMax - point.y) / rect.height * _view.height);
        internal static Rect PlotRect(Rect canvas) =>
            new Rect(canvas.x + 36, canvas.y + 16, Mathf.Max(1, canvas.width - 54), Mathf.Max(1, canvas.height - 46));
        IEnumerable<Vector2> Controls()
        {
            foreach (var key in _curve.keys) yield return new Vector2(key.time, key.value);
            foreach (bool incoming in new[] { true, false })
                if (HasHandle(incoming)) yield return BoundedCurve.Handle(_curve, _selected, incoming);
        }
        void FitView()
        {
            float left = 0, right = 1, bottom = 0, top = 1;
            foreach (var point in Controls())
            {
                left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
                bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y);
            }
            float xPad = (right - left) * .1f, yPad = (top - bottom) * .15f;
            _view = Rect.MinMaxRect(left - xPad, bottom - yPad, right + xPad, top + yPad);
            _graph?.MarkDirtyRepaint(); Repaint();
        }
        Vector2 Point(Rect rect, int index) => Pixel(rect, new Vector2(_curve[index].time, _curve[index].value));
        bool HasHandle(bool incoming) => incoming ? _selected > 0 && !float.IsInfinity(_curve[_selected].inTangent)
            && !float.IsInfinity(_curve[_selected - 1].outTangent)
            : _selected < _curve.length - 1 && !float.IsInfinity(_curve[_selected].outTangent) && !float.IsInfinity(_curve[_selected + 1].inTangent);

        void DrawGraph()
        {
            var outer = GUILayoutUtility.GetRect(0, 10000, Mathf.Max(240, _graph.contentRect.height), Mathf.Max(240, _graph.contentRect.height));
            var plot = PlotRect(outer);
            int control = GUIUtility.GetControlID(FocusType.Keyboard);
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(plot, InspectorTheme.Input);
                var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                label.normal.textColor = InspectorTheme.Muted;
                for (int i = 0; i <= 4; i++)
                {
                    float t = i / 4f; var p = Pixel(plot, new Vector2(t, t));
                    if (p.x >= plot.x && p.x <= plot.xMax)
                    {
                        EditorGUI.DrawRect(new Rect(p.x, plot.y, 1, plot.height), InspectorTheme.Border);
                        GUI.Label(new Rect(p.x - 18, plot.yMax + 5, 36, 18), t.ToString("0.##"), label);
                    }
                    if (p.y >= plot.y && p.y <= plot.yMax)
                    {
                        EditorGUI.DrawRect(new Rect(plot.x, p.y, plot.width, 1), InspectorTheme.Border);
                        GUI.Label(new Rect(outer.x, p.y - 9, 30, 18), t.ToString("0.##"), label);
                    }
                }
                GUI.BeginClip(plot);
                var chart = new Rect(0, 0, plot.width, plot.height);
                Handles.BeginGUI();
                Color prior = Handles.color;
                try
                {
                    Handles.color = InspectorTheme.Accent;
                    for (int i = 0; i < _line.Length; i++)
                        _line[i] = Pixel(chart, new Vector2(i / 256f, Mathf.Clamp01(_curve.Evaluate(i / 256f))));
                    Handles.DrawAAPolyLine(2.5f, _line);
                    foreach (bool incoming in new[] { true, false })
                    {
                        if (!HasHandle(incoming)) continue;
                        Vector2 p = Pixel(chart, BoundedCurve.Handle(_curve, _selected, incoming));
                        Handles.color = InspectorTheme.Muted;
                        Handles.DrawAAPolyLine(1.5f, Point(chart, _selected), p);
                        EditorGUI.DrawRect(new Rect(p.x - 4, p.y - 4, 8, 8), InspectorTheme.Accent);
                        EditorGUI.DrawRect(new Rect(p.x - 2, p.y - 2, 4, 4), InspectorTheme.Input);
                    }
                    for (int i = 0; i < _curve.length; i++)
                    {
                        Vector2 p = Point(chart, i); float size = i == _selected ? 10 : 8;
                        EditorGUI.DrawRect(new Rect(p.x - size / 2 - 1, p.y - size / 2 - 1, size + 2, size + 2), InspectorTheme.Input);
                        EditorGUI.DrawRect(new Rect(p.x - size / 2, p.y - size / 2, size, size), i == _selected ? InspectorTheme.Text : InspectorTheme.Accent);
                    }
                }
                finally { Handles.color = prior; Handles.EndGUI(); GUI.EndClip(); }
            }
            // IMGUI graph focus can consume native key events before UI Toolkit
            // dispatches KeyDownEvent to the root. Handle the same history shortcuts here.
            if (e.type == EventType.KeyDown && (e.control || e.command) && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
            {
                if (e.keyCode == KeyCode.Y || e.shift) RedoDraft(); else UndoDraft();
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && plot.Contains(e.mousePosition))
            {
                Vector2 pivot = Unit(plot, e.mousePosition);
                float factor = Mathf.Exp(e.delta.y * .06f);
                float width = Mathf.Clamp(_view.width * factor, .01f, 100000);
                float height = Mathf.Clamp(_view.height * factor, .01f, 100000);
                _view = new Rect(pivot.x + (_view.x - pivot.x) * width / _view.width,
                    pivot.y + (_view.y - pivot.y) * height / _view.height, width, height);
                Repaint(); e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 2 && plot.Contains(e.mousePosition))
            {
                _panning = true; _panStart = e.mousePosition; _panView = _view;
                GUIUtility.hotControl = control; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _panning && GUIUtility.hotControl == control)
            {
                Vector2 delta = e.mousePosition - _panStart;
                _view.position = _panView.position + new Vector2(-delta.x / plot.width * _panView.width, delta.y / plot.height * _panView.height);
                Repaint(); e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && plot.Contains(e.mousePosition))
            {
                _graph.Focus(); GUIUtility.keyboardControl = control;
                _dragPart = 0; _dragged = false;
                float nearest = 12;
                for (int i = 0; i < _curve.length; i++)
                {
                    float distance = Vector2.Distance(e.mousePosition, Point(plot, i));
                    if (distance >= nearest) continue;
                    nearest = distance; _selected = i; _dragPart = 1;
                }
                if (_dragPart == 0)
                    foreach (bool incoming in new[] { true, false })
                        if (HasHandle(incoming) && Vector2.Distance(e.mousePosition, Pixel(plot, BoundedCurve.Handle(_curve, _selected, incoming))) < 10)
                        { _dragPart = incoming ? 2 : 3; break; }
                if (_dragPart == 0 && e.clickCount == 2 && plot.Contains(e.mousePosition))
                {
                    Vector2 point = Unit(plot, e.mousePosition);
                    Change(BoundedCurve.Insert(_curve, point.x, point.y));
                    _selected = Enumerable.Range(0, _curve.length).OrderBy(i => Mathf.Abs(_curve[i].time - point.x)).First();
                    _dragPart = 1;
                }
                if (_dragPart != 0) GUIUtility.hotControl = control;
                Synchronize(); e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control && _dragPart != 0)
            {
                if (!_dragged) { Remember(); _dragged = true; }
                Vector2 point = Unit(plot, e.mousePosition);
                _curve = _dragPart == 1 ? BoundedCurve.Move(_curve, _selected, point.x, point.y)
                    : BoundedCurve.MoveHandle(_curve, _selected, _dragPart == 2, point);
                Synchronize(); e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                bool reframe = false;
                if (_dragged && !_panning && _dragPart != 0)
                {
                    Vector2 point = _dragPart == 1 ? new Vector2(_curve[_selected].time, _curve[_selected].value)
                        : BoundedCurve.Handle(_curve, _selected, _dragPart == 2);
                    reframe = !_view.Contains(point);
                }
                GUIUtility.hotControl = 0; _dragPart = 0; _panning = false; e.Use();
                if (reframe) FitView();
            }
            else if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == control)
            {
                if (e.keyCode == KeyCode.F) { FitView(); e.Use(); }
                else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { DeleteSelected(); e.Use(); }
                else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
                {
                    float step = e.shift ? .1f : .01f;
                    var key = _curve[_selected];
                    Change(BoundedCurve.Move(_curve, _selected, key.time + (e.keyCode == KeyCode.LeftArrow ? -step : e.keyCode == KeyCode.RightArrow ? step : 0),
                        key.value + (e.keyCode == KeyCode.DownArrow ? -step : e.keyCode == KeyCode.UpArrow ? step : 0)));
                    e.Use();
                }
            }
        }
        void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(0, 10000, 24, 24);
            if (Event.current.type != EventType.Repaint) return;
            int columns = Mathf.Max(1, Mathf.CeilToInt(rect.width));
            for (int i = 0; i < columns; i++)
            {
                // Match the texture converter's sampling positions, including its last texel.
                int texel = Mathf.Min(_settings.width - 1, Mathf.FloorToInt(i * (float)_settings.width / columns));
                float v = Mathf.Clamp01(_curve.Evaluate(texel / (float)Mathf.Max(1, _settings.width - 1)));
                Color color = new Color(v, v, v, 1);
                EditorGUI.DrawRect(new Rect(rect.x + i, rect.y, 1, rect.height), color);
            }
        }
    }
}
#endif
