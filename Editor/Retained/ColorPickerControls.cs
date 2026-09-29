using System;
using System.Collections.Generic;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // Mesh helpers for the color picker. Vertex tints are display colors: editor panels
    // interpolate them in gamma space on 2022.3 and Unity 6, so gradients stay exact.
    internal static class ColorPickerDrawing
    {
        static Vertex[] s_Vertices = new Vertex[1024];
        static ushort[] s_Indices = new ushort[4096];
        static int s_VertexCount, s_IndexCount;
        static Texture2D s_Checker;
        static bool s_CheckerPro, s_CheckerHooked;
#if !UNITY_6000_0_OR_NEWER
        static readonly Func<VisualElement, float> ScaledPixelsPerPoint = PixelsPerPointGetter();
        static Func<VisualElement, float> PixelsPerPointGetter()
        {
            var getter = typeof(VisualElement).GetProperty("scaledPixelsPerPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            if (getter == null || getter.ReturnType != typeof(float)) return null;
            try { return (Func<VisualElement, float>)Delegate.CreateDelegate(typeof(Func<VisualElement, float>), getter); }
            catch (ArgumentException) { return null; }
        }
#endif

        /// <summary>One physical pixel in points, for anti-aliasing fringes.</summary>
        internal static float PixelSize(VisualElement element)
        {
            float ppp = EditorGUIUtility.pixelsPerPoint;
#if UNITY_6000_0_OR_NEWER
            if (element?.panel != null) ppp = element.scaledPixelsPerPoint;
#else
            if (element?.panel != null && ScaledPixelsPerPoint != null) ppp = ScaledPixelsPerPoint(element);
#endif
            return 1f / Mathf.Max(.5f, ThryColorMath.IsFinite(ppp) ? ppp : 1f);
        }

        internal static Color CheckerLight => EditorGUIUtility.isProSkin ? new Color(.42f, .42f, .42f) : Color.white;
        internal static Color CheckerDark => EditorGUIUtility.isProSkin ? new Color(.27f, .27f, .27f) : new Color(.8f, .8f, .8f);

        // An 8x8 checker stretched over a shape's bounds, for alpha previews that are not rectangles.
        // It stays readable, so the dynamic atlas never takes it and its UVs need no remapping.
        internal static Texture2D CheckerTexture
        {
            get
            {
                bool pro = EditorGUIUtility.isProSkin;
                if (s_Checker != null && s_CheckerPro == pro) return s_Checker;
                if (s_Checker == null)
                {
                    s_Checker = new Texture2D(8, 8, TextureFormat.RGBA32, false, false)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    // HideAndDontSave objects outlive the domain; without this each script reload leaks one.
                    if (!s_CheckerHooked)
                    {
                        s_CheckerHooked = true;
                        AssemblyReloadEvents.beforeAssemblyReload += DestroyChecker;
                        EditorApplication.quitting += DestroyChecker;
                    }
                }
                Color32 light = CheckerLight, dark = CheckerDark;
                var pixels = new Color32[64];
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) pixels[y * 8 + x] = ((x + y) & 1) == 0 ? light : dark;
                s_Checker.SetPixels32(pixels); s_Checker.Apply(false, false);
                s_CheckerPro = pro;
                return s_Checker;
            }
        }

        static void DestroyChecker()
        {
            if (s_Checker != null) UnityEngine.Object.DestroyImmediate(s_Checker);
            s_Checker = null;
        }

        internal static void Begin() { s_VertexCount = 0; s_IndexCount = 0; }

        internal static ushort AddVertex(Vector2 position, Color32 tint, Vector2 uv = default)
        {
            if (s_VertexCount == s_Vertices.Length) Array.Resize(ref s_Vertices, s_Vertices.Length * 2);
            s_Vertices[s_VertexCount] = new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = tint, uv = uv };
            return (ushort)s_VertexCount++;
        }

        /// <summary>Adds a triangle wound clockwise on screen (y down), whatever order the corners come in.</summary>
        internal static void Triangle(ushort a, ushort b, ushort c)
        {
            if (s_IndexCount + 3 > s_Indices.Length) Array.Resize(ref s_Indices, s_Indices.Length * 2);
            Vector3 pa = s_Vertices[a].position, pb = s_Vertices[b].position, pc = s_Vertices[c].position;
            float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
            s_Indices[s_IndexCount++] = a;
            s_Indices[s_IndexCount++] = cross >= 0 ? b : c;
            s_Indices[s_IndexCount++] = cross >= 0 ? c : b;
        }

        /// <summary>Two triangles for corners given in order around the quad; the diagonal runs a to c.</summary>
        internal static void Quad(ushort a, ushort b, ushort c, ushort d) { Triangle(a, b, c); Triangle(a, c, d); }

        internal static void End(MeshGenerationContext context, Texture texture = null)
        {
            if (s_VertexCount == 0 || s_IndexCount == 0) return;
            var mesh = context.Allocate(s_VertexCount, s_IndexCount, texture);
#if !UNITY_6000_0_OR_NEWER
            var region = mesh.uvRegion; // Unity 6 remaps atlas UVs itself.
#endif
            for (int i = 0; i < s_VertexCount; i++)
            {
                var v = s_Vertices[i];
#if !UNITY_6000_0_OR_NEWER
                if (texture != null) v.uv = new Vector2(region.x + v.uv.x * region.width, region.y + v.uv.y * region.height);
#endif
                mesh.SetNextVertex(v);
            }
            for (int i = 0; i < s_IndexCount; i++) mesh.SetNextIndex(s_Indices[i]);
            Begin();
        }

        internal static void Rect(MeshGenerationContext context, Rect rect, Color color)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            Begin();
            Quad(AddVertex(rect.min, color), AddVertex(new Vector2(rect.xMax, rect.yMin), color), AddVertex(rect.max, color), AddVertex(new Vector2(rect.xMin, rect.yMax), color));
            End(context);
        }

        /// <summary>Left-to-right gradient of <paramref name="sample"/> between two argument values.</summary>
        internal static void Gradient(MeshGenerationContext context, Rect rect, Func<float, Color> sample, float from, float to, int segments)
        {
            if (rect.width <= 0 || rect.height <= 0 || sample == null) return;
            segments = Mathf.Max(1, segments);
            Begin();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments, x = rect.x + t * rect.width;
                Color32 color = ThryColorMath.Clamp01(sample(Mathf.Lerp(from, to, t)));
                AddVertex(new Vector2(x, rect.yMin), color); AddVertex(new Vector2(x, rect.yMax), color);
                if (i > 0) { ushort a = (ushort)(i * 2 - 2); Quad(a, (ushort)(a + 2), (ushort)(a + 3), (ushort)(a + 1)); }
            }
            End(context);
        }

        internal static void Checker(MeshGenerationContext context, Rect rect, float cell, Color a, Color b)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            cell = Mathf.Max(1, cell);
            Rect(context, rect, a);
            Begin();
            int columns = Mathf.CeilToInt(rect.width / cell), rows = Mathf.CeilToInt(rect.height / cell);
            for (int y = 0; y < rows; y++) for (int x = (y + 1) & 1; x < columns; x += 2)
            {
                float left = rect.x + x * cell, top = rect.y + y * cell;
                float right = Mathf.Min(rect.xMax, left + cell), bottom = Mathf.Min(rect.yMax, top + cell);
                Quad(AddVertex(new Vector2(left, top), b), AddVertex(new Vector2(right, top), b), AddVertex(new Vector2(right, bottom), b), AddVertex(new Vector2(left, bottom), b));
            }
            End(context);
        }

        /// <summary>
        /// A filled circle or circular sector with a transparent one-pixel fringe on its arc. Turns count
        /// counterclockwise from the right, like hues on the wheel. A texture is stretched over the circle's bounds.
        /// </summary>
        internal static void Disc(MeshGenerationContext context, Vector2 center, float radius, float fringe, Color color,
            float fromTurn = 0, float toTurn = 1, Texture texture = null)
        {
            if (!(radius > 0)) return;
            float span = Mathf.Abs(toTurn - fromTurn);
            int segments = Mathf.Clamp(Mathf.CeilToInt(radius * span * Mathf.PI), 6, 160);
            Color32 solid = color, clear = solid; clear.a = 0;
            float bounds = radius + fringe;
            Begin();
            ushort middle = AddVertex(center, solid, Uv(center, center, bounds));
            for (int i = 0; i <= segments; i++)
            {
                var direction = ThryColorMath.HueDirection(Mathf.Lerp(fromTurn, toTurn, i / (float)segments));
                Vector2 rim = center + direction * radius, edge = center + direction * (radius + fringe);
                AddVertex(rim, solid, Uv(rim, center, bounds)); AddVertex(edge, clear, Uv(edge, center, bounds));
                if (i == 0) continue;
                ushort a = (ushort)(1 + (i - 1) * 2), b = (ushort)(a + 2);
                Triangle(middle, a, b);
                if (fringe > 0) Quad(a, (ushort)(a + 1), (ushort)(b + 1), b);
            }
            End(context, texture);
        }

        static Vector2 Uv(Vector2 point, Vector2 center, float radius)
            => new Vector2((point.x - center.x) / (2 * radius) + .5f, .5f - (point.y - center.y) / (2 * radius));
    }

    /// <summary>
    /// Coolorus-style hue ring around an HSV triangle whose pure-hue corner points at the hue.
    /// Reads and writes the picker's <see cref="ThryColorState"/> directly.
    /// </summary>
    internal sealed class ColorWheel : VisualElement
    {
        static readonly CustomStyleProperty<float> RingProperty = new CustomStyleProperty<float>("--color-wheel-ring");
        static readonly CustomStyleProperty<float> GapProperty = new CustomStyleProperty<float>("--color-wheel-gap");
        static readonly CustomStyleProperty<float> MarkerProperty = new CustomStyleProperty<float>("--color-wheel-marker");
        static readonly CustomStyleProperty<Color> DiscProperty = new CustomStyleProperty<Color>("--color-wheel-disc");
        static readonly CustomStyleProperty<Color> OutlineProperty = new CustomStyleProperty<Color>("--color-wheel-outline");
        static readonly CustomStyleProperty<Color> FocusProperty = new CustomStyleProperty<Color>("--color-wheel-focus");
        const int RingSegments = 192;
        static readonly Color32[] RingColors = BuildRingColors();
        static readonly Color MarkerShadow = new Color(0, 0, 0, .6f);

        readonly ThryColorState _state;
        readonly Action _changed, _committed;
        float _ringWidth = 18, _gap = 1, _markerRadius = 5;
        Color _disc = new Color(.16f, .16f, .16f), _outline = new Color(.29f, .29f, .29f), _focus = new Color(.48f, .68f, .84f);
        int _drag, _pointerId = -1; // _drag: 1 = hue ring, 2 = triangle or square
        bool _square;
        const int SquareCells = 16;

        internal ColorWheel(ThryColorState state, Action changed, Action committed)
        {
            _state = state; _changed = changed; _committed = committed;
            AddToClassList("thry-color-wheel");
            focusable = true; tabIndex = 0;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.target != this) return;
                _ringWidth = customStyle.TryGetValue(RingProperty, out float ring) ? ring : 18;
                _gap = customStyle.TryGetValue(GapProperty, out float gap) ? gap : 1;
                _markerRadius = customStyle.TryGetValue(MarkerProperty, out float marker) ? Mathf.Max(2, marker) : 5;
                if (customStyle.TryGetValue(DiscProperty, out Color disc)) _disc = disc;
                if (customStyle.TryGetValue(OutlineProperty, out Color outline)) _outline = outline;
                if (customStyle.TryGetValue(FocusProperty, out Color focus)) _focus = focus;
                MarkDirtyRepaint();
            });
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_drag == 0 || e.pointerId != _pointerId || !this.HasPointerCapture(e.pointerId)) return;
                Drag(e.localPosition); e.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (_drag == 0 || e.pointerId != _pointerId) return;
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                EndDrag(); e.StopPropagation();
            });
            RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction == NavigationMoveEvent.Direction.Next || e.direction == NavigationMoveEvent.Direction.Previous) return;
                KeepFocus(this, e);
            });
            RegisterCallback<FocusInEvent>(e => MarkDirtyRepaint());
            RegisterCallback<FocusOutEvent>(e => MarkDirtyRepaint());
        }

        /// <summary>A saturation/value square instead of the triangle.</summary>
        internal bool Square { get => _square; set { if (_square == value) return; _square = value; MarkDirtyRepaint(); } }
        static float SquareHalf(float disc) => disc * .70710678f;

        // The triangle keeps its pure-hue corner pointing right; the square stands upright with it at the top right.
        static Vector2 SquarePoint(float saturation, float value, Vector2 center, float half)
            => center + new Vector2(Mathf.Lerp(-half, half, saturation), Mathf.Lerp(half, -half, value));

        static Color32[] BuildRingColors()
        {
            var colors = new Color32[RingSegments + 1];
            for (int i = 0; i <= RingSegments; i++) colors[i] = ThryColorMath.HsvToRgb(new Vector3(i / (float)RingSegments, 1, 1));
            return colors;
        }

        bool Geometry(out Vector2 center, out float outer, out float inner, out float disc)
        {
            var rect = contentRect;
            center = rect.center;
            outer = Mathf.Min(rect.width, rect.height) / 2 - 1;
            float ring = Mathf.Clamp(_ringWidth, 2, Mathf.Max(2, outer * .45f));
            inner = outer - ring;
            disc = Mathf.Max(1, inner - Mathf.Max(0, _gap));
            return outer > 8;
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            // Clicks outside the ring reach the circles and buttons in the wheel's corners.
            if (!Geometry(out var center, out float outer, out _, out _)) return false;
            return (localPoint - center).sqrMagnitude <= (outer + 2) * (outer + 2);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0 || !enabledInHierarchy || _drag != 0 || !Geometry(out var center, out float outer, out _, out float disc)) return;
            float distance = ((Vector2)e.localPosition - center).magnitude;
            if (distance > outer + 2) return;
            _drag = distance >= disc ? 1 : 2; _pointerId = e.pointerId;
            Focus(); this.CapturePointer(e.pointerId);
            Drag(e.localPosition); e.StopPropagation();
        }

        void Drag(Vector2 point)
        {
            if (!Geometry(out var center, out _, out _, out float disc)) return;
            var hsv = _state.Hsv;
            if (_drag == 1) hsv.x = ThryColorMath.HueFromPoint(point, center);
            else if (_square)
            {
                float half = SquareHalf(disc);
                var local = point - center;
                hsv.y = Mathf.Clamp01((local.x + half) / (2 * half));
                hsv.z = Mathf.Clamp01((half - local.y) / (2 * half));
            }
            else
            {
                ThryColorMath.TriangleCorners(0, center, disc, out var pure, out var white, out var black);
                var sv = ThryColorMath.TriangleSaturationValue(point, pure, white, black, hsv.y);
                hsv.y = sv.x; hsv.z = sv.y;
            }
            _state.SetHsv(hsv);
            MarkDirtyRepaint(); _changed?.Invoke();
        }

        void EndDrag()
        {
            if (_drag == 0) return;
            _drag = 0; _pointerId = -1;
            _committed?.Invoke();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (!enabledInHierarchy || _drag != 0) return;
            var hsv = _state.Hsv;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    hsv.x = ThryColorMath.Repeat01(hsv.x + (e.keyCode == KeyCode.RightArrow ? 1 : -1) * (e.shiftKey ? 10f : 1f) / 360f);
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    float step = (e.keyCode == KeyCode.UpArrow ? 1 : -1) * (e.shiftKey ? .1f : .01f);
                    if (e.altKey) hsv.y = Mathf.Clamp01(hsv.y + step); else hsv.z = Mathf.Clamp01(hsv.z + step);
                    break;
                default: return;
            }
            _state.SetHsv(hsv);
            MarkDirtyRepaint(); _changed?.Invoke();
            e.PreventDefault(); e.StopPropagation();
        }

        void Draw(MeshGenerationContext context)
        {
            if (!Geometry(out var center, out float outer, out float inner, out float disc)) return;
            float px = ColorPickerDrawing.PixelSize(this);
            ColorPickerDrawing.Disc(context, center, disc, px, _disc);

            ColorPickerDrawing.Begin();
            for (int i = 0; i <= RingSegments; i++)
            {
                var direction = ThryColorMath.HueDirection(i / (float)RingSegments);
                Color32 color = RingColors[i], clear = color; clear.a = 0;
                ColorPickerDrawing.AddVertex(center + direction * (outer + px), clear);
                ColorPickerDrawing.AddVertex(center + direction * outer, color);
                ColorPickerDrawing.AddVertex(center + direction * inner, color);
                ColorPickerDrawing.AddVertex(center + direction * (inner - px), clear);
                if (i == 0) continue;
                // Strips from outside in: fringe, ring, fringe. Each quad is outer(h0), outer(h1), inner(h1), inner(h0).
                ushort a = (ushort)((i - 1) * 4), b = (ushort)(a + 4);
                for (ushort s = 0; s < 3; s++) ColorPickerDrawing.Quad((ushort)(a + s), (ushort)(b + s), (ushort)(b + s + 1), (ushort)(a + s + 1));
            }
            ColorPickerDrawing.End(context);

            var hsv = _state.Hsv;
            var painter = context.painter2D;
            Vector2 svMarker;
            if (_square) svMarker = DrawSquare(context, painter, hsv, center, disc);
            else svMarker = DrawTriangle(context, painter, hsv, center, disc, px);

            bool focused = focusController?.focusedElement == this;
            painter.lineWidth = focused ? 1.5f : 1f; painter.strokeColor = focused ? _focus : _outline;
            painter.BeginPath(); painter.Arc(center, outer, 0, 360); painter.ClosePath(); painter.Stroke();

            float ring = outer - inner;
            var hueMarker = center + ThryColorMath.HueDirection(hsv.x) * (outer - ring / 2);
            float hueRadius = Mathf.Max(2, ring / 2 - 2);
            Circle(painter, hueMarker, hueRadius, 4, MarkerShadow);
            Circle(painter, hueMarker, hueRadius, 2, Color.white);
            Circle(painter, svMarker, _markerRadius, 1.5f, ThryColorMath.Luminance(_state.Display) > .5f ? Color.black : Color.white);
        }

        Vector2 DrawTriangle(MeshGenerationContext context, Painter2D painter, Vector3 hsv, Vector2 center, float disc, float px)
        {
            ThryColorMath.TriangleCorners(0, center, disc, out var pure, out var white, out var black);
            Color32 pureColor = ThryColorMath.HsvToRgb(new Vector3(hsv.x, 1, 1)), whiteColor = Color.white, blackColor = Color.black;
            ColorPickerDrawing.Begin();
            ushort p = ColorPickerDrawing.AddVertex(pure, pureColor), w = ColorPickerDrawing.AddVertex(white, whiteColor), k = ColorPickerDrawing.AddVertex(black, blackColor);
            ColorPickerDrawing.Triangle(p, k, w);
            // Moving a corner of an equilateral triangle out along its bisector by 2px moves both edges out by 1px.
            ushort pf = Fringe(pure, center, px, pureColor), wf = Fringe(white, center, px, whiteColor), kf = Fringe(black, center, px, blackColor);
            ColorPickerDrawing.Quad(p, pf, wf, w);
            ColorPickerDrawing.Quad(w, wf, kf, k);
            ColorPickerDrawing.Quad(k, kf, pf, p);
            ColorPickerDrawing.End(context);
            painter.lineWidth = 1f; painter.strokeColor = _outline;
            painter.BeginPath(); painter.MoveTo(pure); painter.LineTo(white); painter.LineTo(black); painter.ClosePath(); painter.Stroke();
            return ThryColorMath.TrianglePoint(hsv.y, hsv.z, pure, white, black);
        }

        // The square's colors are bilinear in saturation and value, so it is drawn as a grid of exact vertex colors.
        Vector2 DrawSquare(MeshGenerationContext context, Painter2D painter, Vector3 hsv, Vector2 center, float disc)
        {
            float half = SquareHalf(disc);
            ColorPickerDrawing.Begin();
            for (int y = 0; y <= SquareCells; y++)
                for (int x = 0; x <= SquareCells; x++)
                {
                    float saturation = x / (float)SquareCells, value = 1 - y / (float)SquareCells;
                    ColorPickerDrawing.AddVertex(SquarePoint(saturation, value, center, half), (Color32)ThryColorMath.HsvToRgb(new Vector3(hsv.x, saturation, value)));
                    if (x == 0 || y == 0) continue;
                    ushort d = (ushort)(y * (SquareCells + 1) + x), c = (ushort)(d - 1), b = (ushort)(d - SquareCells - 1), a = (ushort)(b - 1);
                    ColorPickerDrawing.Quad(a, b, d, c);
                }
            ColorPickerDrawing.End(context);
            // The outline also smooths the square's edges.
            painter.lineWidth = 1f; painter.strokeColor = _outline;
            painter.BeginPath();
            painter.MoveTo(SquarePoint(0, 1, center, half)); painter.LineTo(SquarePoint(1, 1, center, half));
            painter.LineTo(SquarePoint(1, 0, center, half)); painter.LineTo(SquarePoint(0, 0, center, half));
            painter.ClosePath(); painter.Stroke();
            return SquarePoint(hsv.y, hsv.z, center, half);
        }

        // Arrow keys adjust the focused control instead of moving focus to a neighbor.
        internal static void KeepFocus(VisualElement element, NavigationMoveEvent e)
        {
#if UNITY_6000_0_OR_NEWER
            element.focusController?.IgnoreEvent(e);
#endif
            e.PreventDefault(); e.StopPropagation();
        }

        static ushort Fringe(Vector2 corner, Vector2 center, float px, Color32 color)
        {
            color.a = 0;
            return ColorPickerDrawing.AddVertex(corner + (corner - center).normalized * (2 * px), color);
        }

        static void Circle(Painter2D painter, Vector2 center, float radius, float width, Color color)
        {
            painter.lineWidth = width; painter.strokeColor = color;
            painter.BeginPath(); painter.Arc(center, radius, 0, 360); painter.ClosePath(); painter.Stroke();
        }
    }

    internal enum ColorSliderUnit { Bytes, Floats, Degrees, Percent, Stops }

    /// <summary>
    /// Caption, gradient track with a box handle, and a number field. <see cref="Value"/> and
    /// <see cref="Changed"/> use the channel's own range (0-1, or stops for intensity); the number
    /// field shows it in the slider's unit.
    /// </summary>
    internal sealed class ColorChannelSlider : VisualElement
    {
        static readonly CustomStyleProperty<Color> FocusProperty = new CustomStyleProperty<Color>("--color-slider-focus");
        // Unity's picker look: a plain gradient bar and a thin handle that stands a little proud of it.
        const float BarHeight = 14, HandleInset = 2;

        readonly Label _caption;
        readonly VisualElement _track;
        readonly FloatField _number;
        readonly Func<float, Color> _sample;
        readonly int _segments;
        readonly bool _checker, _hue;
        ColorSliderUnit _unit;
        float _min, _max, _value, _argument;
        int _pointerId = -1;
        Color _focus = new Color(.48f, .68f, .84f);

        internal event Action<float> Changed;
        /// <summary>A drag ended or a typed number was committed.</summary>
        internal event Action Committed;

        internal ColorChannelSlider(string caption, ColorSliderUnit unit, float min, float max, Func<float, Color> sample, int segments,
            bool checker = false)
        {
            _unit = unit; _min = min; _max = max; _sample = sample; _segments = segments; _checker = checker;
            // Hue tracks start in degrees and keep stopping short of the wrap when shown as 0–1.
            _hue = unit == ColorSliderUnit.Degrees;
            AddToClassList("thry-color-slider");
            _caption = new Label(caption); _caption.AddToClassList("thry-color-slider__caption");
            _caption.style.display = string.IsNullOrEmpty(caption) ? DisplayStyle.None : DisplayStyle.Flex;
            Add(_caption);

            _track = new VisualElement { focusable = true, tabIndex = 0 };
            _track.AddToClassList("thry-color-slider__track");
            _track.generateVisualContent += DrawTrack;
            _track.RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.target != _track) return;
                if (_track.customStyle.TryGetValue(FocusProperty, out Color focus)) _focus = focus;
                _track.MarkDirtyRepaint();
            });
            _track.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !enabledInHierarchy || _pointerId >= 0) return;
                _pointerId = e.pointerId; _track.Focus(); _track.CapturePointer(e.pointerId);
                DragTo(e.localPosition.x); e.StopPropagation();
            });
            _track.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != _pointerId || !_track.HasPointerCapture(e.pointerId)) return;
                DragTo(e.localPosition.x); e.StopPropagation();
            });
            _track.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != _pointerId) return;
                if (_track.HasPointerCapture(e.pointerId)) _track.ReleasePointer(e.pointerId);
                EndDrag(); e.StopPropagation();
            });
            _track.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
            _track.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _track.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction == NavigationMoveEvent.Direction.Next || e.direction == NavigationMoveEvent.Direction.Previous) return;
                ColorWheel.KeepFocus(_track, e);
            });
            _track.RegisterCallback<FocusInEvent>(e => _track.MarkDirtyRepaint());
            _track.RegisterCallback<FocusOutEvent>(e => _track.MarkDirtyRepaint());
            Add(_track);

            _number = new FloatField { isDelayed = true };
            _number.AddToClassList("thry-color-slider__number");
            _number.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                float typed = e.newValue;
                // Commits that only re-parse the shown text change nothing, so tabbing through fields never writes.
                if (!ThryColorMath.IsFinite(typed) || typed.Equals(_value)) { _number.SetValueWithoutNotify(_value); return; }
                Changed?.Invoke(ClampArgument(typed / Scale, true));
                Committed?.Invoke();
            });
            Add(_number);

            ApplyUnit();
        }

        internal ColorSliderUnit Unit
        {
            get => _unit;
            set { if (_unit == value) return; _unit = value; ApplyUnit(); Value = _argument; }
        }

        /// <summary>The value as displayed. Setting it never raises <see cref="Changed"/>.</summary>
        internal float Value
        {
            get => _value / Scale;
            set
            {
                _argument = ThryColorMath.IsFinite(value) ? value : 0;
                _value = Quantize(_argument);
                _number.SetValueWithoutNotify(_value);
                _track.MarkDirtyRepaint();
            }
        }

        internal VisualElement Track => _track;

        /// <summary>Shows an icon instead of a letter in the caption column.</summary>
        internal void SetCaptionIcon(VisualElement icon)
        {
            _caption.text = "";
            _caption.style.display = DisplayStyle.Flex;
            _caption.Add(icon);
        }

        internal void SetRange(float min, float max)
        {
            if (_min.Equals(min) && _max.Equals(max)) return;
            _min = min; _max = max; _track.MarkDirtyRepaint();
        }

        internal void Refresh() => _track.MarkDirtyRepaint();

        float Scale => _unit == ColorSliderUnit.Bytes ? 255f : _unit == ColorSliderUnit.Degrees ? 360f : _unit == ColorSliderUnit.Percent ? 100f : 1f;

        // Exactly what the number field shows, so the field's own re-parse on blur or Enter finds no change.
        float Quantize(float x)
        {
            switch (_unit)
            {
                case ColorSliderUnit.Bytes: return ThryColorMath.ToByte(x);
                case ColorSliderUnit.Floats: return (float)Math.Round(x, 3);
                case ColorSliderUnit.Degrees: return Mathf.Round(x * 360f);
                case ColorSliderUnit.Percent: return Mathf.Round(x * 100f);
                default: return (float)Math.Round(x, 2);
            }
        }

        void ApplyUnit()
        {
            _number.formatString = _unit == ColorSliderUnit.Floats ? "0.###" : _unit == ColorSliderUnit.Stops ? "0.##" : "0";
        }

        // A hue of 1 wraps to 0; stop just short of it so the handle stays at the right end.
        // Typed intensities may go past the track, which then grows to include them.
        float ClampArgument(float x, bool typed = false)
        {
            if (_hue) return Mathf.Clamp(x, _min, _max - 1e-5f);
            if (typed && _unit == ColorSliderUnit.Stops) return Mathf.Clamp(x, 0, ThryColorMath.MaxIntensity);
            return Mathf.Clamp(x, _min, _max);
        }

        Rect Bar()
        {
            var rect = _track.contentRect;
            float height = Mathf.Min(BarHeight, rect.height - 4);
            return new Rect(rect.x + HandleInset, Mathf.Round(rect.y + (rect.height - height) / 2), Mathf.Max(0, rect.width - 2 * HandleInset), height);
        }

        void DragTo(float x)
        {
            var bar = Bar();
            if (bar.width <= 2) return;
            float t = Mathf.Clamp01((x - bar.x) / bar.width);
            Changed?.Invoke(ClampArgument(Mathf.Lerp(_min, _max, t)));
        }

        void EndDrag()
        {
            if (_pointerId < 0) return;
            _pointerId = -1;
            Committed?.Invoke();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (!enabledInHierarchy || _pointerId >= 0) return;
            int direction = e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.UpArrow ? 1
                : e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.DownArrow ? -1 : 0;
            if (direction == 0) return;
            float step = _unit == ColorSliderUnit.Floats ? .01f : _unit == ColorSliderUnit.Stops ? .1f : 1f;
            Changed?.Invoke(ClampArgument((_value + direction * step * (e.shiftKey ? 10 : 1)) / Scale));
            e.PreventDefault(); e.StopPropagation();
        }

        void DrawTrack(MeshGenerationContext context)
        {
            var bar = Bar();
            if (bar.width < 4 || bar.height < 4) return;
            if (_track.focusController?.focusedElement == _track)
                ColorPickerDrawing.Rect(context, new Rect(bar.x - 1, bar.y - 1, bar.width + 2, bar.height + 2), _focus);
            if (_checker) ColorPickerDrawing.Checker(context, bar, 4, ColorPickerDrawing.CheckerLight, ColorPickerDrawing.CheckerDark);
            ColorPickerDrawing.Gradient(context, bar, _sample, _min, _max, _segments);
            float t = _max > _min ? Mathf.Clamp01((_value / Scale - _min) / (_max - _min)) : 0;
            var handle = new Rect(Mathf.Round(bar.x + t * bar.width - 2.5f), bar.y - 2, 5, bar.height + 4);
            ColorPickerDrawing.Rect(context, handle, new Color(0, 0, 0, .65f));
            ColorPickerDrawing.Rect(context, new Rect(handle.x + 1, handle.y + 1, 3, handle.height - 2), Color.white);
        }
    }

    /// <summary>
    /// A round color swatch. With alpha, the right half shows the color at its alpha over a checker.
    /// </summary>
    internal sealed class ColorPreviewSwatch : VisualElement
    {
        readonly Label _mixed;
        Color _color = Color.black;
        Color? _preview;
        bool _showAlpha, _isMixed;

        internal ColorPreviewSwatch()
        {
            AddToClassList("thry-color-preview");
            generateVisualContent += Draw;
            _mixed = new Label("—") { pickingMode = PickingMode.Ignore };
            _mixed.AddToClassList("thry-color-preview__mixed");
            _mixed.style.display = DisplayStyle.None;
            Add(_mixed);
        }

        /// <summary>A display color; alpha is shown only when <paramref name="showAlpha"/> is set.</summary>
        internal void Set(Color display, bool showAlpha)
        {
            if (ThryColorMath.Same(display, _color) && showAlpha == _showAlpha) return;
            _color = display; _showAlpha = showAlpha;
            MarkDirtyRepaint();
        }

        /// <summary>Temporarily paints this display color instead, opaque (eyedropper hover).</summary>
        internal Color? Preview
        {
            get => _preview;
            set { _preview = value; MarkDirtyRepaint(); }
        }

        internal bool Mixed
        {
            get => _isMixed;
            set
            {
                _isMixed = value;
                EnableInClassList("thry-color-preview--mixed", value);
                _mixed.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext context)
        {
            if (_isMixed) return;
            var rect = contentRect;
            float px = ColorPickerDrawing.PixelSize(this);
            float radius = Mathf.Min(rect.width, rect.height) / 2 - px;
            if (!(radius > 1)) return;
            var center = rect.center;
            var color = ThryColorMath.Clamp01(_preview ?? _color);
            var opaque = color; opaque.a = 1;
            if (_preview.HasValue || !_showAlpha || color.a >= 1)
            {
                ColorPickerDrawing.Disc(context, center, radius, px, opaque);
                return;
            }
            ColorPickerDrawing.Disc(context, center, radius, px, opaque, .25f, .75f);
            ColorPickerDrawing.Disc(context, center, radius, px, Color.white, -.25f, .25f, ColorPickerDrawing.CheckerTexture);
            ColorPickerDrawing.Disc(context, center, radius, px, color, -.25f, .25f);
        }
    }

    /// <summary>
    /// Unity-style swatch grid: small flush cells that wrap, with an add button after the last one.
    /// </summary>
    internal sealed class ColorSwatchGrid : VisualElement
    {
        readonly List<VisualElement> _cells = new List<VisualElement>();
        readonly List<Color> _colors = new List<Color>();
        readonly List<bool> _hdr = new List<bool>();
        readonly Button _add;
        internal event Action<int> Clicked;
        /// <summary>Right click on a cell: its index and the pointer position.</summary>
        internal event Action<int, Vector2> ContextClicked;
        /// <summary>A cell's tooltip, built only when it is about to show.</summary>
        internal Func<int, string> Tooltip;
        internal int Count { get; private set; }

        internal ColorSwatchGrid(Action add, string addTooltip)
        {
            AddToClassList("thry-color-swatches");
            // A context menu opened from a cell hands focus back here when it closes; cells cannot take it.
            focusable = true; tabIndex = -1;
            _add = new Button(add) { text = "+", tooltip = addTooltip, focusable = false };
            _add.AddToClassList("thry-color-swatches__add");
            Add(_add);
        }

        internal bool CanAdd { set => _add.SetEnabled(value); }

        internal void SetColors(List<Color> colors, List<bool> hdr)
        {
            while (_cells.Count < colors.Count)
            {
                int index = _cells.Count;
                var cell = new VisualElement();
                cell.AddToClassList("thry-color-swatches__cell");
                cell.generateVisualContent += context => { if (index < Count) DrawCell(context, cell.contentRect, _colors[index], _hdr[index]); };
                cell.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (!enabledInHierarchy || index >= Count) return;
                    if (e.button == 0) Clicked?.Invoke(index);
                    else if (e.button == 1 && ContextClicked != null)
                    {
                        // The pointer-down focus change would blur the menu this opens, and a blurred menu closes.
#if UNITY_6000_0_OR_NEWER
                        cell.focusController?.IgnoreEvent(e);
#else
                        e.PreventDefault();
#endif
                        ContextClicked(index, e.position);
                    }
                    else return;
                    e.StopPropagation();
                });
                cell.RegisterCallback<TooltipEvent>(e =>
                {
                    string text = index < Count ? Tooltip?.Invoke(index) : null;
                    if (string.IsNullOrEmpty(text)) return;
                    e.tooltip = text; e.rect = cell.worldBound;
                    e.StopImmediatePropagation();
                });
                _cells.Add(cell); _colors.Add(Color.clear); _hdr.Add(false);
                Insert(index, cell);
            }
            for (int i = 0; i < _cells.Count; i++)
            {
                bool shown = i < colors.Count;
                _cells[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown) continue;
                bool isHdr = hdr != null && i < hdr.Count && hdr[i];
                if (ThryColorMath.Same(_colors[i], colors[i]) && _hdr[i] == isHdr) continue;
                _colors[i] = colors[i]; _hdr[i] = isHdr;
                _cells[i].MarkDirtyRepaint();
            }
            Count = colors.Count;
        }

        // Like Unity's swatches: a see-through color is split diagonally, opaque at the top left and over a
        // checker at the bottom right; a color with intensity gets a small mark in its top-right corner.
        static void DrawCell(MeshGenerationContext context, Rect rect, Color color, bool hdr)
        {
            if (rect.width < 2 || rect.height < 2) return;
            var opaque = color; opaque.a = 1;
            if (color.a >= .97f) ColorPickerDrawing.Rect(context, rect, opaque);
            else
            {
                Vector2 topLeft = rect.min, topRight = new Vector2(rect.xMax, rect.yMin), bottomRight = rect.max, bottomLeft = new Vector2(rect.xMin, rect.yMax);
                ColorPickerDrawing.Begin();
                ColorPickerDrawing.Triangle(ColorPickerDrawing.AddVertex(topLeft, opaque), ColorPickerDrawing.AddVertex(topRight, opaque), ColorPickerDrawing.AddVertex(bottomLeft, opaque));
                ColorPickerDrawing.End(context);
                ColorPickerDrawing.Begin();
                ColorPickerDrawing.Triangle(ColorPickerDrawing.AddVertex(topRight, Color.white, new Vector2(1, 1)),
                    ColorPickerDrawing.AddVertex(bottomRight, Color.white, new Vector2(1, 0)), ColorPickerDrawing.AddVertex(bottomLeft, Color.white, new Vector2(0, 0)));
                ColorPickerDrawing.End(context, ColorPickerDrawing.CheckerTexture);
                ColorPickerDrawing.Begin();
                ColorPickerDrawing.Triangle(ColorPickerDrawing.AddVertex(topRight, color), ColorPickerDrawing.AddVertex(bottomRight, color), ColorPickerDrawing.AddVertex(bottomLeft, color));
                ColorPickerDrawing.End(context);
            }
            if (!hdr) return;
            float size = Mathf.Min(6, rect.width / 2);
            ColorPickerDrawing.Begin();
            ColorPickerDrawing.Triangle(ColorPickerDrawing.AddVertex(new Vector2(rect.xMax - size - 1, rect.yMin), new Color(0, 0, 0, .7f)),
                ColorPickerDrawing.AddVertex(new Vector2(rect.xMax, rect.yMin), new Color(0, 0, 0, .7f)), ColorPickerDrawing.AddVertex(new Vector2(rect.xMax, rect.yMin + size + 1), new Color(0, 0, 0, .7f)));
            ColorPickerDrawing.Triangle(ColorPickerDrawing.AddVertex(new Vector2(rect.xMax - size, rect.yMin), Color.white),
                ColorPickerDrawing.AddVertex(new Vector2(rect.xMax, rect.yMin), Color.white), ColorPickerDrawing.AddVertex(new Vector2(rect.xMax, rect.yMin + size), Color.white));
            ColorPickerDrawing.End(context);
        }
    }

    /// <summary>Small line icons for the triangle/square switch, drawn in the element's text color.</summary>
    internal sealed class ColorPickerGlyph : VisualElement
    {
        internal enum Kind { Triangle, Square, Sun }
        readonly Kind _kind;

        internal ColorPickerGlyph(Kind kind)
        {
            _kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("thry-color-picker__glyph");
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (!(size > 4)) return;
            var center = rect.center;
            float r = size / 2 - 1.5f;
            var painter = context.painter2D;
            painter.lineWidth = 1.5f; painter.strokeColor = resolvedStyle.color;
            painter.lineJoin = LineJoin.Round; painter.lineCap = LineCap.Round;
            painter.BeginPath();
            if (_kind == Kind.Triangle)
            {
                ThryColorMath.TriangleCorners(0, center, r, out var pure, out var white, out var black);
                painter.MoveTo(pure); painter.LineTo(white); painter.LineTo(black); painter.ClosePath();
            }
            else if (_kind == Kind.Sun)
            {
                painter.Arc(center, r * .38f, 0, 360); painter.ClosePath();
                for (int i = 0; i < 8; i++)
                {
                    var direction = ThryColorMath.HueDirection(i / 8f);
                    painter.MoveTo(center + direction * r * .64f); painter.LineTo(center + direction * r * .95f);
                }
            }
            else
            {
                float h = r * .8f;
                painter.MoveTo(center + new Vector2(-h, -h)); painter.LineTo(center + new Vector2(h, -h));
                painter.LineTo(center + new Vector2(h, h)); painter.LineTo(center + new Vector2(-h, h)); painter.ClosePath();
            }
            painter.Stroke();
        }
    }
}
