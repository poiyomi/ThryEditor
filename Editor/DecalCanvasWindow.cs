using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class DecalCanvasWindow : EditorWindow
    {
        enum Drag { None, Move, Scale, Rotate, Pan }

        struct State
        {
            internal Vector4 Position, Scale, Sides;
            internal float Rotation;
            internal bool Same(State other) => Position == other.Position && Scale == other.Scale && Rotation == other.Rotation;
        }

        struct Box
        {
            internal Vector2 Center, Size, X, Y, Aspect;
            internal Vector2 Point(Vector2 local) => Center + X * local.x + Y * local.y;
            internal Vector2 Local(Vector2 uv) { var d = uv - Center; return new Vector2(Vector2.Dot(d, X), Vector2.Dot(d, Y)); }
        }

        static readonly CustomStyleProperty<Color> BoxColorProperty = new CustomStyleProperty<Color>("--thry-decal-canvas-box");
        static readonly CustomStyleProperty<Color> PivotColorProperty = new CustomStyleProperty<Color>("--thry-decal-canvas-pivot");
        static readonly CustomStyleProperty<Color> GhostColorProperty = new CustomStyleProperty<Color>("--thry-decal-canvas-ghost");
        static readonly Vector2[] Handles8 = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1),
            new Vector2(0, -1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0) };
        const float HandleSize = 9;
        const float MainTextureOpacity = 0.5f;

        internal static event Action StateChanged;

        [SerializeField] Material _material;
        [SerializeField] string[] _properties;
        [SerializeField] string _label;
        [SerializeField] Renderer _renderer;
        [SerializeField] Vector2 _center = new Vector2(.5f, .5f);
        [SerializeField] float _zoom = 1;
        [SerializeField] bool _showMesh = true;

        Func<string, MaterialProperty> _resolve;
        IMGUIContainer _background, _canvas;
        VisualElement _image;
        Label _hint;
        bool _fitWidth;
        FloatField _positionX, _positionY, _rotation, _scaleX, _scaleY;
        Toggle _meshToggle;
        Color _boxColor, _pivotColor, _ghostColor;

        Drag _drag;
        int _handle = -1;
        MouseCursor _hover;
        bool _throughProperties;
        State _start;
        Box _startBox;
        Vector2 _startMouse, _startCenter;
        string _signature;

        Mesh _wireMesh;
        int _wireUV = -1;
        Renderer _wireRenderer;
        Vector2[] _wire = Array.Empty<Vector2>();
        Vector3[] _wirePoints = Array.Empty<Vector3>();

        internal static void Open(Material material, string[] properties, string label, Renderer renderer, Func<string, MaterialProperty> resolve)
        {
            bool created = !HasOpenInstances<DecalCanvasWindow>();
            var window = GetWindow<DecalCanvasWindow>();
            window.titleContent = new GUIContent("Decal Canvas");
            window.minSize = new Vector2(320, 360);
            window.wantsMouseMove = true;
            if (created)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.center.x - 440, main.center.y - 320, 880, 640);
                window._fitWidth = true;
            }
            bool rebind = window._material != material || window._properties == null || !window._properties.SequenceEqual(properties);
            if (window._drag != Drag.None) window.EndDrag(true);
            window._material = material; window._properties = properties.ToArray(); window._label = label;
            window.titleContent = new GUIContent("Decal Canvas", material.name + " · " + label);
            window._renderer = renderer; window._resolve = resolve;
            if (rebind) window.FitUV();
            window.Synchronize();
            window.Show(); window.Focus();
            StateChanged?.Invoke();
        }

        internal static bool IsOpen(Material material, string positionProperty) => HasOpenInstances<DecalCanvasWindow>()
            && Resources.FindObjectsOfTypeAll<DecalCanvasWindow>().Any(w => w._material == material && w._properties != null
                && w._properties.Length == 6 && w._properties[2] == positionProperty);

        internal static void CloseFor(Material material, string positionProperty)
        {
            if (!HasOpenInstances<DecalCanvasWindow>()) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<DecalCanvasWindow>())
                if (window._material == material && window._properties != null && window._properties.Length == 6 && window._properties[2] == positionProperty)
                    window.Close();
        }

        void OnEnable()
        {
            _boxColor = InspectorTheme.Accent; _pivotColor = InspectorTheme.Text; _ghostColor = InspectorTheme.Muted;
            Undo.undoRedoPerformed += Synchronize;
            Selection.selectionChanged += FollowSelection;
        }

        void OnDisable()
        {
            if (_drag != Drag.None) EndDrag(false);
            Undo.undoRedoPerformed -= Synchronize;
            Selection.selectionChanged -= FollowSelection;
            ReleaseWire();
            EditorApplication.delayCall += () => StateChanged?.Invoke();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-decal-canvas-window");
            var toolbar = new VisualElement(); toolbar.AddToClassList("thry-decal-canvas-toolbar"); root.Add(toolbar);
            var position = Group(toolbar, "Position");
            _positionX = Field(position, "X", "decal-canvas-position-x", "0.###", "Move Decal", (state, v) => { state.Position.x = v; return state; });
            _positionY = Field(position, "Y", "decal-canvas-position-y", "0.###", "Move Decal", (state, v) => { state.Position.y = v; return state; });
            _rotation = Field(Group(toolbar, null), "Rotation", "decal-canvas-rotation", "0.##", "Rotate Decal", (state, v) => { state.Rotation = v; return state; });
            var scale = Group(toolbar, "Scale");
            _scaleX = Field(scale, "X", "decal-canvas-scale-x", "0.###", "Scale Decal", (state, v) => { state.Scale.x = v; return state; });
            _scaleY = Field(scale, "Y", "decal-canvas-scale-y", "0.###", "Scale Decal", (state, v) => { state.Scale.y = v; return state; });
            var spacer = new VisualElement(); spacer.AddToClassList("thry-decal-canvas-spacer"); toolbar.Add(spacer);
            _meshToggle = new Toggle { text = "Mesh UVs", name = "decal-canvas-mesh", value = _showMesh, tooltip = "Show the UV layout of the selected mesh" };
            _meshToggle.RegisterValueChangedCallback(e => { _showMesh = e.newValue; Redraw(); });
            toolbar.Add(_meshToggle);
            toolbar.Add(new Button(() => { FitUV(); Redraw(); }) { text = "Fit UV", name = "decal-canvas-fit", tooltip = "Show the whole 0–1 UV square (A)" });
            toolbar.RegisterCallback<GeometryChangedEvent>(e => FitWidth(toolbar));
            var view = new VisualElement();
            view.AddToClassList("thry-decal-canvas");
            view.RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(BoxColorProperty, out var box)) _boxColor = box;
                if (e.customStyle.TryGetValue(PivotColorProperty, out var pivot)) _pivotColor = pivot;
                if (e.customStyle.TryGetValue(GhostColorProperty, out var ghost)) _ghostColor = ghost;
                Redraw();
            });
            _background = new IMGUIContainer(PaintBackground) { pickingMode = PickingMode.Ignore };
            _image = new VisualElement { pickingMode = PickingMode.Ignore };
            _image.generateVisualContent += PaintImage;
            _canvas = new IMGUIContainer(OnCanvasGUI) { name = "decal-canvas", focusable = true };
            foreach (var layer in new VisualElement[] { _background, _image, _canvas })
            {
                layer.AddToClassList("thry-decal-canvas-layer"); view.Add(layer);
            }
            root.Add(view);
            _hint = new Label("Drag to move · Drag handles to scale, Shift for free scale, Alt from the center · Drag outside to rotate, Shift snaps · Scroll to zoom · Middle-drag to pan");
            _hint.AddToClassList("thry-muted"); _hint.AddToClassList("thry-decal-canvas-hint"); root.Add(_hint);
            _canvas.schedule.Execute(Poll).Every(100);
            Synchronize();
        }

        void FitWidth(VisualElement toolbar)
        {
            if (!_fitWidth || toolbar.layout.width <= 0) return;
            _fitWidth = false;
            float row = toolbar.Children().Where(c => !c.ClassListContains("thry-decal-canvas-spacer"))
                .Sum(c => c.layout.width + c.resolvedStyle.marginLeft + c.resolvedStyle.marginRight);
            float hint = _hint.MeasureTextSize(_hint.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x
                + _hint.resolvedStyle.marginLeft + _hint.resolvedStyle.marginRight + _hint.resolvedStyle.paddingLeft + _hint.resolvedStyle.paddingRight;
            var root = rootVisualElement;
            float width = Mathf.Ceil(Mathf.Max(row, hint) + root.resolvedStyle.paddingLeft + root.resolvedStyle.paddingRight + 2);
            var current = position;
            position = new Rect(current.center.x - width / 2, current.y, width, current.height);
        }

        static VisualElement Group(VisualElement parent, string caption)
        {
            var group = new VisualElement(); group.AddToClassList("thry-decal-canvas-group");
            if (caption != null) group.Add(new Label(caption));
            parent.Add(group); return group;
        }

        FloatField Field(VisualElement group, string label, string name, string format, string undo, Func<State, float, State> change)
        {
            var field = new FloatField(label) { name = name, isDelayed = true, formatString = format };
            field.AddToClassList("thry-decal-canvas-field");
            field.RegisterValueChangedCallback(e =>
            {
                if (e.newValue.ToString(format, CultureInfo.InvariantCulture) == e.previousValue.ToString(format, CultureInfo.InvariantCulture)) Synchronize(true);
                else Edit(undo, state => change(state, e.newValue));
            });
            group.Add(field); return field;
        }

        void Edit(string undo, Func<State, State> change)
        {
            if (!Valid || _material.IsLocked() || _drag != Drag.None) { Synchronize(); return; }
            var current = Read();
            var next = change(current);
            next.Rotation = WrapRotation(next.Rotation);
            if (!next.Same(current))
            {
                _throughProperties = AnimationMode.InAnimationMode() || new[] { 2, 3, 4 }.Any(i => Property(_properties[i]).applyPropertyCallback != null);
                if (!_throughProperties) Undo.RegisterCompleteObjectUndo(_material, undo);
                Write(next);
            }
            Synchronize(true);
        }

        void ShowValues(State s, bool force)
        {
            foreach (var (field, value) in new[] { (_positionX, s.Position.x), (_positionY, s.Position.y), (_rotation, s.Rotation), (_scaleX, s.Scale.x), (_scaleY, s.Scale.y) })
            {
                if (field == null) continue;
                var focused = field.panel?.focusController?.focusedElement as VisualElement;
                if (!force && focused != null && (focused == field || field.Contains(focused))) continue;
                field.SetValueWithoutNotify(value);
            }
        }

        void Poll()
        {
            if (_drag != Drag.None) return;
            string signature = Signature();
            if (signature == _signature) return;
            Synchronize();
        }

        string Signature()
        {
            if (!Valid) return null;
            var s = Read();
            var texture = _material.HasProperty(_properties[0]) ? _material.GetTexture(_properties[0]) : null;
            var main = MainTexture(out var mainCoords);
            return string.Join("|", F(s.Position), F(s.Scale), F(s.Sides), s.Rotation.ToString("R"), UVChannel, texture != null ? texture.GetInstanceID() : 0,
                main != null ? main.GetInstanceID() : 0, F(new Vector4(mainCoords.x, mainCoords.y, mainCoords.width, mainCoords.height)),
                Option("AspectRatioMode"), Option("SymmetryMode"), _material.IsLocked(), _material.shader != null ? _material.shader.GetInstanceID() : 0);
        }

        static string F(Vector4 v) => v.x.ToString("R") + "," + v.y.ToString("R") + "," + v.z.ToString("R") + "," + v.w.ToString("R");

        void Synchronize() => Synchronize(false);

        void Synchronize(bool force)
        {
            _signature = Signature();
            if (_positionX == null) return;
            bool editable = Valid && !_material.IsLocked();
            foreach (var field in new[] { _positionX, _positionY, _rotation, _scaleX, _scaleY }) field.SetEnabled(editable);
            if (Valid) ShowValues(Read(), force);
            _meshToggle.SetEnabled(WireSource() != null);
            Redraw();
        }

        void Redraw()
        {
            _background?.MarkDirtyRepaint(); _image?.MarkDirtyRepaint(); _canvas?.MarkDirtyRepaint();
        }

        void FollowSelection()
        {
            if (!Valid) return;
            var renderer = Selection.activeTransform != null ? Selection.activeTransform.GetComponent<Renderer>() : null;
            if (renderer != null && renderer.sharedMaterials.Contains(_material) && renderer != _renderer)
            {
                _renderer = renderer; Synchronize();
            }
        }

        bool Valid => _material != null && _properties != null && _properties.Length == 6
            && _properties.Skip(1).All(p => _material.HasProperty(p));

        int UVChannel => Mathf.RoundToInt(_material.GetFloat(_properties[1]));

        float Option(string name)
        {
            var property = OptionName(name);
            return property != null ? _material.GetFloat(property) : 0;
        }

        string OptionName(string name)
        {
            string position = _properties[2];
            int at = position.LastIndexOf("Position", StringComparison.Ordinal);
            if (at < 0) return null;
            string prefix = position.Substring(0, at), suffix = position.Substring(at + "Position".Length);
            return new[] { prefix + (suffix.Length == 0 ? "0" : suffix) + name, prefix + name + suffix }.FirstOrDefault(_material.HasProperty);
        }

        State Read() => new State
        {
            Position = _material.GetVector(_properties[2]),
            Rotation = _material.GetFloat(_properties[3]),
            Scale = _material.GetVector(_properties[4]),
            Sides = _material.GetVector(_properties[5]),
        };

        Vector2 Aspect()
        {
            var texture = _material.HasProperty(_properties[0]) ? _material.GetTexture(_properties[0]) : null;
            if (texture == null || texture.height == 0) return Vector2.one;
            float mode = Option("AspectRatioMode"), ratio = (float)texture.width / texture.height;
            if (mode == 1) return new Vector2(Mathf.Min(ratio, 1), Mathf.Min(1 / ratio, 1));
            if (mode == 2) return new Vector2(Mathf.Max(ratio, 1), Mathf.Max(1 / ratio, 1));
            return Vector2.one;
        }

        Box Measure(State s)
        {
            var aspect = Aspect();
            float angle = s.Rotation * Mathf.Deg2Rad;
            return new Box
            {
                Aspect = aspect,
                Size = new Vector2(s.Scale.x * aspect.x + s.Sides.x + s.Sides.y, s.Scale.y * aspect.y + s.Sides.z + s.Sides.w),
                Center = (Vector2)s.Position + new Vector2((s.Sides.y - s.Sides.x) / 2, (s.Sides.w - s.Sides.z) / 2),
                X = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)),
                Y = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)),
            };
        }

        float PixelsPerUnit(Rect rect) => Mathf.Max(1, _zoom * Mathf.Min(rect.width, rect.height) * .9f);
        Vector2 ToPixel(Rect rect, Vector2 uv) { float k = PixelsPerUnit(rect); return rect.center + new Vector2((uv.x - _center.x) * k, -(uv.y - _center.y) * k); }
        Vector2 ToUV(Rect rect, Vector2 pixel) { float k = PixelsPerUnit(rect); return _center + new Vector2((pixel.x - rect.center.x) / k, -(pixel.y - rect.center.y) / k); }

        void FitUV() { _center = new Vector2(.5f, .5f); _zoom = 1; }

        void OnCanvasGUI()
        {
            var rect = new Rect(0, 0, _canvas.contentRect.width, _canvas.contentRect.height);
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Keyboard);
            if (!Valid)
            {
                if (e.type == EventType.Repaint) Message(rect, "Open the canvas from a decal's positioning tools.");
                return;
            }
            bool editable = !_material.IsLocked();
            var state = Read();
            var box = Measure(state);
            HandleView(rect, e, id);
            if (editable) HandleEdit(rect, e, id, state, box);
            if (e.type == EventType.MouseMove && editable && _drag == Drag.None && Cursor(rect, box, e.mousePosition) != _hover) _canvas.MarkDirtyRepaint();
            if (e.type == EventType.Repaint)
            {
                PaintOverlay(rect, box);
                if (!editable) Message(rect, "Unlock the material to move the decal.");
                if (_drag == Drag.None && editable) EditorGUIUtility.AddCursorRect(rect, _hover = Cursor(rect, box, e.mousePosition));
            }
        }

        void HandleView(Rect rect, Event e, int id)
        {
            switch (e.type)
            {
                case EventType.ScrollWheel when rect.Contains(e.mousePosition):
                    var pivot = ToUV(rect, e.mousePosition);
                    _zoom = Mathf.Clamp(_zoom * Mathf.Exp(-e.delta.y * .06f), .05f, 500);
                    float k = PixelsPerUnit(rect);
                    _center = pivot - new Vector2((e.mousePosition.x - rect.center.x) / k, -(e.mousePosition.y - rect.center.y) / k);
                    e.Use(); Redraw();
                    break;
                case EventType.MouseDown when (e.button == 2 || e.button == 1) && rect.Contains(e.mousePosition) && _drag == Drag.None:
                    _drag = Drag.Pan; _startMouse = e.mousePosition; _startCenter = _center;
                    GUIUtility.hotControl = id; e.Use();
                    break;
                case EventType.MouseDrag when _drag == Drag.Pan && GUIUtility.hotControl == id:
                    float scale = PixelsPerUnit(rect);
                    var delta = e.mousePosition - _startMouse;
                    _center = _startCenter - new Vector2(delta.x / scale, -delta.y / scale);
                    e.Use(); Redraw();
                    break;
                case EventType.MouseUp when _drag == Drag.Pan:
                    _drag = Drag.None; if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0; e.Use();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.A && !e.control && !e.command && _drag == Drag.None:
                    FitUV(); e.Use(); Redraw();
                    break;
            }
        }

        void HandleEdit(Rect rect, Event e, int id, State state, Box box)
        {
            if (_drag != Drag.None && _drag != Drag.Pan && GUIUtility.hotControl != id && e.type != EventType.Layout && e.type != EventType.Repaint)
            {
                EndDrag(false); return;
            }
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && rect.Contains(e.mousePosition) && _drag == Drag.None:
                    _canvas.Focus();
                    _handle = HitHandle(rect, box, e.mousePosition);
                    var local = box.Local(ToUV(rect, e.mousePosition));
                    _drag = _handle >= 0 ? Drag.Scale
                        : Mathf.Abs(local.x) <= Mathf.Abs(box.Size.x) / 2 && Mathf.Abs(local.y) <= Mathf.Abs(box.Size.y) / 2 ? Drag.Move : Drag.Rotate;
                    BeginDrag(state, box, e.mousePosition);
                    GUIUtility.hotControl = id; e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id && _drag != Drag.None && _drag != Drag.Pan:
                    var next = _drag == Drag.Move ? Moved(rect, e) : _drag == Drag.Rotate ? Rotated(rect, e) : Scaled(rect, e);
                    Write(next);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id && _drag != Drag.None && _drag != Drag.Pan:
                    GUIUtility.hotControl = 0; EndDrag(false); e.Use();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && _drag != Drag.None && _drag != Drag.Pan:
                    GUIUtility.hotControl = 0; EndDrag(true); e.Use();
                    break;
                case EventType.KeyDown when _drag == Drag.None && (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow
                    || e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow):
                    var step = (e.shift ? 10 : 1) / PixelsPerUnit(rect);
                    var nudge = e.keyCode == KeyCode.LeftArrow ? Vector2.left : e.keyCode == KeyCode.RightArrow ? Vector2.right
                        : e.keyCode == KeyCode.UpArrow ? Vector2.up : Vector2.down;
                    var nudged = state; nudged.Position += (Vector4)(nudge * step);
                    _drag = Drag.Move; BeginDrag(state, box, e.mousePosition); Write(nudged); EndDrag(false);
                    e.Use();
                    break;
            }
        }

        void BeginDrag(State state, Box box, Vector2 mouse)
        {
            _start = state; _startBox = box; _startMouse = mouse;
            _throughProperties = AnimationMode.InAnimationMode() || new[] { 2, 3, 4 }.Any(i => Property(_properties[i])?.applyPropertyCallback != null);
        }

        void EndDrag(bool cancel)
        {
            var drag = _drag; _drag = Drag.None; _handle = -1;
            if (!Valid || drag == Drag.None || drag == Drag.Pan) return;
            if (cancel) { Write(_start); Synchronize(); return; }
            var final = Read();
            if (!_throughProperties && !final.Same(_start))
            {
                Undo.FlushUndoRecordObjects();
                Apply(_start);
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(_material, drag == Drag.Move ? "Move Decal" : drag == Drag.Rotate ? "Rotate Decal" : "Scale Decal");
                Apply(final);
                Undo.IncrementCurrentGroup();
            }
            Synchronize();
        }

        MaterialProperty Property(string name)
        {
            var property = _resolve?.Invoke(name);
            if (property != null && property.targets.Length == 1 && (Material)property.targets[0] == _material) return property;
            return MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { _material }, name);
        }

        void Write(State next)
        {
            if (_throughProperties)
            {
                var current = Read();
                if (next.Position != current.Position) Property(_properties[2]).vectorValue = next.Position;
                if (next.Rotation != current.Rotation) Property(_properties[3]).floatValue = next.Rotation;
                if (next.Scale != current.Scale) Property(_properties[4]).vectorValue = next.Scale;
            }
            else Apply(next);
            SceneView.RepaintAll();
            _signature = Signature();
            ShowValues(next, false);
            Redraw();
        }

        void Apply(State s)
        {
            if (_material.GetVector(_properties[2]) != s.Position) _material.SetVector(_properties[2], s.Position);
            if (_material.GetFloat(_properties[3]) != s.Rotation) _material.SetFloat(_properties[3], s.Rotation);
            if (_material.GetVector(_properties[4]) != s.Scale) _material.SetVector(_properties[4], s.Scale);
            EditorUtility.SetDirty(_material);
        }

        State Moved(Rect rect, Event e)
        {
            var delta = ToUV(rect, e.mousePosition) - ToUV(rect, _startMouse);
            if (e.shift) { if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = 0; else delta.x = 0; }
            var next = _start; next.Position = _start.Position + (Vector4)delta;
            return next;
        }

        State Rotated(Rect rect, Event e)
        {
            Vector2 from = ToUV(rect, _startMouse) - _startBox.Center, to = ToUV(rect, e.mousePosition) - _startBox.Center;
            float rotation = _start.Rotation - Vector2.SignedAngle(from, to);
            if (e.shift) rotation = Mathf.Round(rotation / 15) * 15;
            var next = _start; next.Rotation = WrapRotation(rotation);
            return next;
        }

        float WrapRotation(float rotation)
        {
            var property = Property(_properties[3]);
            if (property.GetPropertyType() != UnityEngine.Rendering.ShaderPropertyType.Range) return rotation;
            var limits = property.rangeLimits;
            return limits.y > limits.x ? Helper.Mod(rotation - limits.x, limits.y - limits.x) + limits.x : rotation;
        }

        State Scaled(Rect rect, Event e)
        {
            var box = _startBox;
            var direction = Handles8[_handle];
            var half = box.Size / 2;
            var handle = new Vector2(direction.x * half.x, direction.y * half.y);
            var anchor = e.alt ? Vector2.zero : -handle;
            var mouse = box.Local(ToUV(rect, e.mousePosition));
            Vector2 ratio;
            if (direction.x != 0 && direction.y != 0 && !e.shift)
            {
                var diagonal = handle - anchor;
                float t = Vector2.Dot(mouse - anchor, diagonal) / Mathf.Max(diagonal.sqrMagnitude, 1e-12f);
                ratio = new Vector2(t, t);
            }
            else
            {
                ratio = Vector2.one;
                if (direction.x != 0 && Mathf.Abs(handle.x - anchor.x) > 1e-9f) ratio.x = (mouse.x - anchor.x) / (handle.x - anchor.x);
                if (direction.y != 0 && Mathf.Abs(handle.y - anchor.y) > 1e-9f) ratio.y = (mouse.y - anchor.y) / (handle.y - anchor.y);
            }
            var sides = new Vector2(_start.Sides.x + _start.Sides.y, _start.Sides.z + _start.Sides.w);
            var scale = _start.Scale;
            for (int axis = 0; axis < 2; axis++)
            {
                if (box.Size[axis] == 0 || box.Aspect[axis] == 0) continue;
                float effective = box.Size[axis] * ratio[axis] - sides[axis];
                float original = _start.Scale[axis] * box.Aspect[axis];
                float sign = original < 0 ? -1 : 1;
                if (effective * sign < .001f) effective = .001f * sign;
                ratio[axis] = (effective + sides[axis]) / box.Size[axis];
                scale[axis] = effective / box.Aspect[axis];
            }
            var moved = new Vector2(anchor.x * ratio.x, anchor.y * ratio.y);
            var center = box.Point(anchor - moved);
            var next = _start; next.Scale = scale;
            var position = center - new Vector2((_start.Sides.y - _start.Sides.x) / 2, (_start.Sides.w - _start.Sides.z) / 2);
            next.Position = new Vector4(position.x, position.y, _start.Position.z, _start.Position.w);
            return next;
        }

        int HitHandle(Rect rect, Box box, Vector2 mouse)
        {
            for (int i = 0; i < Handles8.Length; i++)
            {
                var p = ToPixel(rect, box.Point(new Vector2(Handles8[i].x * box.Size.x / 2, Handles8[i].y * box.Size.y / 2)));
                if (Mathf.Abs(p.x - mouse.x) <= HandleSize && Mathf.Abs(p.y - mouse.y) <= HandleSize) return i;
            }
            return -1;
        }

        MouseCursor Cursor(Rect rect, Box box, Vector2 mouse)
        {
            int handle = HitHandle(rect, box, mouse);
            if (handle >= 0)
            {
                var center = ToPixel(rect, box.Center);
                var p = ToPixel(rect, box.Point(new Vector2(Handles8[handle].x * box.Size.x / 2, Handles8[handle].y * box.Size.y / 2)));
                float angle = Mathf.Repeat(Mathf.Atan2(p.y - center.y, p.x - center.x) * Mathf.Rad2Deg, 180);
                return angle < 22.5f || angle >= 157.5f ? MouseCursor.ResizeHorizontal : angle < 67.5f ? MouseCursor.ResizeUpLeft
                    : angle < 112.5f ? MouseCursor.ResizeVertical : MouseCursor.ResizeUpRight;
            }
            var local = box.Local(ToUV(rect, mouse));
            return Mathf.Abs(local.x) <= Mathf.Abs(box.Size.x) / 2 && Mathf.Abs(local.y) <= Mathf.Abs(box.Size.y) / 2 ? MouseCursor.MoveArrow : MouseCursor.RotateArrow;
        }

        void PaintBackground()
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = new Rect(0, 0, _background.contentRect.width, _background.contentRect.height);
            EditorGUI.DrawRect(rect, InspectorTheme.Input);
            if (!Valid) return;
            GUI.BeginClip(rect);
            try
            {
                var unit = Rect.MinMaxRect(ToPixel(rect, new Vector2(0, 1)).x, ToPixel(rect, new Vector2(0, 1)).y, ToPixel(rect, new Vector2(1, 0)).x, ToPixel(rect, new Vector2(1, 0)).y);
                EditorGUI.DrawRect(unit, InspectorTheme.Background);
                var main = MainTexture(out var mainCoords);
                if (main != null)
                {
                    var tint = GUI.color;
                    GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * MainTextureOpacity);
                    GUI.DrawTextureWithTexCoords(unit, main, mainCoords, true);
                    GUI.color = tint;
                }
                Handles.BeginGUI();
                var prior = Handles.color;
                try
                {
                    Grid(rect, unit);
                }
                finally { Handles.color = prior; Handles.EndGUI(); }
            }
            finally { GUI.EndClip(); }
        }

        void PaintOverlay(Rect rect, Box box)
        {
            GUI.BeginClip(rect);
            Handles.BeginGUI();
            var prior = Handles.color;
            try
            {
                if (_showMesh) Wire(rect);
                var ghost = Ghost(Corners(box));
                if (ghost != null)
                {
                    var outline = ghost.Select(c => (Vector3)ToPixel(rect, c)).ToList();
                    outline.Add(outline[0]);
                    Handles.color = _ghostColor;
                    Handles.DrawAAPolyLine(1.5f, outline.ToArray());
                }
                Outline(rect, box);
            }
            finally { Handles.color = prior; Handles.EndGUI(); GUI.EndClip(); }
        }

        Vector2[] Ghost(Vector2[] corners)
        {
            float symmetry = Option("SymmetryMode");
            if (symmetry == 1) return corners.Select(c => new Vector2(1 - c.x, c.y)).ToArray();
            if (symmetry == 2) return corners.Select(c => c + new Vector2(-.5f, 0)).ToArray();
            return null;
        }

        void PaintImage(MeshGenerationContext context)
        {
            if (!Valid) return;
            var rect = new Rect(0, 0, _image.contentRect.width, _image.contentRect.height);
            var corners = Corners(Measure(Read()));
            var ghost = Ghost(corners);
            if (ghost != null) Quad(context, rect, ghost, .35f);
            Quad(context, rect, corners, 1);
        }

        void Quad(MeshGenerationContext context, Rect rect, Vector2[] corners, float alpha)
        {
            var texture = _material.HasProperty(_properties[0]) ? _material.GetTexture(_properties[0]) : null;
            if (!(texture is Texture2D) && !(texture is RenderTexture)) texture = null;
            var mesh = texture != null ? context.Allocate(4, 6, texture) : context.Allocate(4, 6);
            Vector2 scale = texture != null ? _material.GetTextureScale(_properties[0]) : Vector2.one;
            Vector2 offset = texture != null ? _material.GetTextureOffset(_properties[0]) : Vector2.zero;
            var tint = texture != null ? new Color(1, 1, 1, alpha) : new Color(_boxColor.r, _boxColor.g, _boxColor.b, .35f * alpha);
            var region = mesh.uvRegion;
            var points = corners.Select(c => ToPixel(rect, c)).ToArray();
            for (int i = 0; i < 4; i++)
            {
                var uv = offset + Vector2.Scale(new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0), scale);
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(points[i].x, points[i].y, Vertex.nearZ),
                    tint = tint,
                    uv = region.position + Vector2.Scale(uv, region.size),
                });
            }
            bool clockwise = (points[1].x - points[0].x) * (points[2].y - points[0].y) - (points[1].y - points[0].y) * (points[2].x - points[0].x) > 0;
            mesh.SetAllIndices(clockwise ? new ushort[] { 0, 1, 2, 0, 2, 3 } : new ushort[] { 0, 2, 1, 0, 3, 2 });
        }

        Texture MainTexture(out Rect coords)
        {
            coords = new Rect(0, 0, 1, 1);
            int uv = UVChannel;
            if (uv > 3 || !_material.HasProperty("_MainTex")) return null;
            if (_material.HasProperty("_MainTexUV") && Mathf.RoundToInt(_material.GetFloat("_MainTexUV")) != uv) return null;
            var texture = _material.GetTexture("_MainTex");
            if (!(texture is Texture2D) && !(texture is RenderTexture)) return null;
            var st = _material.GetTextureScale("_MainTex"); var offset = _material.GetTextureOffset("_MainTex");
            coords = new Rect(offset.x, offset.y, st.x, st.y);
            return texture;
        }

        void Grid(Rect rect, Rect unit)
        {
            Handles.color = InspectorTheme.Border;
            for (int i = 1; i < 4; i++)
            {
                float x = Mathf.Lerp(unit.xMin, unit.xMax, i / 4f), y = Mathf.Lerp(unit.yMin, unit.yMax, i / 4f);
                Handles.DrawLine(new Vector3(x, unit.yMin), new Vector3(x, unit.yMax));
                Handles.DrawLine(new Vector3(unit.xMin, y), new Vector3(unit.xMax, y));
            }
            Handles.color = InspectorTheme.Muted;
            Handles.DrawAAPolyLine(1.5f, new Vector3(unit.xMin, unit.yMin), new Vector3(unit.xMax, unit.yMin), new Vector3(unit.xMax, unit.yMax),
                new Vector3(unit.xMin, unit.yMax), new Vector3(unit.xMin, unit.yMin));
        }

        static Vector2[] Corners(Box box) => new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) }
            .Select(d => box.Point(new Vector2(d.x * box.Size.x / 2, d.y * box.Size.y / 2))).ToArray();

        void Outline(Rect rect, Box box)
        {
            var corners = Corners(box).Select(c => (Vector3)ToPixel(rect, c)).ToList();
            corners.Add(corners[0]);
            Handles.color = _boxColor;
            Handles.DrawAAPolyLine(2f, corners.ToArray());
            var center = ToPixel(rect, box.Center);
            Handles.color = _pivotColor;
            Handles.DrawAAPolyLine(1.5f, new Vector3(center.x - 5, center.y), new Vector3(center.x + 5, center.y));
            Handles.DrawAAPolyLine(1.5f, new Vector3(center.x, center.y - 5), new Vector3(center.x, center.y + 5));
            float s = HandleSize / 2 + .5f;
            for (int i = 0; i < Handles8.Length; i++)
            {
                var p = ToPixel(rect, box.Point(new Vector2(Handles8[i].x * box.Size.x / 2, Handles8[i].y * box.Size.y / 2)));
                var square = new[] { new Vector3(p.x - s, p.y - s), new Vector3(p.x + s, p.y - s), new Vector3(p.x + s, p.y + s), new Vector3(p.x - s, p.y + s) };
                Handles.color = InspectorTheme.Input;
                Handles.DrawAAConvexPolygon(square);
                Handles.color = i == _handle ? _pivotColor : _boxColor;
                Handles.DrawAAPolyLine(1.5f, square[0], square[1], square[2], square[3], square[0]);
            }
        }

        void Message(Rect rect, string text)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = InspectorTheme.Muted;
            GUI.Label(new Rect(rect.x + 16, rect.y, rect.width - 32, rect.height), text, style);
        }

        Renderer WireSource()
        {
            if (!Valid || UVChannel > 3) return null;
            var renderer = _renderer;
            if (renderer == null || !renderer.sharedMaterials.Contains(_material))
            {
                renderer = Selection.activeTransform != null ? Selection.activeTransform.GetComponent<Renderer>() : null;
                if (renderer == null || !renderer.sharedMaterials.Contains(_material)) return null;
            }
            return renderer is MeshRenderer || renderer is SkinnedMeshRenderer ? renderer : null;
        }

        void Wire(Rect rect)
        {
            var renderer = WireSource();
            if (renderer == null) return;
            int uv = UVChannel;
            var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (renderer != _wireRenderer || uv != _wireUV || mesh != _wireMesh) BuildWire(renderer, mesh, uv);
            if (_wire.Length == 0) return;
            if (_wirePoints.Length != _wire.Length) _wirePoints = new Vector3[_wire.Length];
            for (int i = 0; i < _wire.Length; i++) _wirePoints[i] = ToPixel(rect, _wire[i]);
            var color = InspectorTheme.Muted; color.a = .55f;
            Handles.color = color;
            Handles.DrawLines(_wirePoints);
        }

        void BuildWire(Renderer renderer, Mesh mesh, int uv)
        {
            ReleaseWire();
            _wireRenderer = renderer; _wireUV = uv; _wireMesh = mesh;
            Mesh source = mesh; bool owned = false;
            try
            {
                if (MeshToolsBridge.WritesUV(renderer, uv)) { source = MeshToolsBridge.Bake(renderer) ?? mesh; owned = source != mesh; }
                if (source == null) return;
                var uvs = new List<Vector2>(); source.GetUVs(uv, uvs);
                if (uvs.Count == 0) return;
                var subMeshes = new HashSet<int>();
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length && source.subMeshCount > 0; i++)
                    if (slots[i] == _material) subMeshes.Add(Mathf.Min(i, source.subMeshCount - 1));
                var edges = new HashSet<long>();
                var segments = new List<Vector2>();
                foreach (int subMesh in subMeshes)
                {
                    var topology = source.GetTopology(subMesh);
                    int corners = topology == MeshTopology.Triangles ? 3 : topology == MeshTopology.Quads ? 4 : 0;
                    if (corners == 0) continue;
                    var indices = source.GetIndices(subMesh);
                    for (int i = 0; i + corners <= indices.Length; i += corners)
                        for (int j = 0; j < corners; j++)
                        {
                            int a = indices[i + j], b = indices[i + (j + 1) % corners];
                            if (a >= uvs.Count || b >= uvs.Count) continue;
                            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                            if (!edges.Add(key)) continue;
                            segments.Add(uvs[a]); segments.Add(uvs[b]);
                        }
                }
                _wire = segments.ToArray();
            }
            catch (Exception) { _wire = Array.Empty<Vector2>(); }
            finally { if (owned && source != null) DestroyImmediate(source); }
        }

        void ReleaseWire()
        {
            _wire = Array.Empty<Vector2>(); _wirePoints = Array.Empty<Vector3>();
            _wireRenderer = null; _wireMesh = null; _wireUV = -1;
        }
    }
}
