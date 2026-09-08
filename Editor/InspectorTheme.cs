using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>Shared visual tokens for retained and immediate-mode material controls.</summary>
    public static class InspectorTheme
    {
        public const int HeaderHeight = 36;
        public const int SectionHeight = 30;
        public const int FieldHeight = 22;
        public static Color Background => Skin(0x383838, 0xc8c8c8);
        public static Color Surface => Skin(0x454545, 0xdedede);
        public static Color NestedSurface => Skin(0x404040, 0xd5d5d5);
        public static Color Body => Skin(0x383838, 0xc8c8c8);
        public static Color Hover => Skin(0x4c4c4c, 0xe8e8e8);
        public static Color Border => Skin(0x4b4b4b, 0xb5b5b5);
        public static Color Text => Skin(0xe4e4e4, 0x252525);
        public static Color Muted => Skin(0xa3a3a3, 0x5b5b5b);
        public static Color Input => Skin(0x2c2c2c, 0xf0f0f0);
        public static Color Accent => Skin(0x7aadd6, 0x3b78a8);
        public static Color Excluded => Skin(0x3d3d3d, 0xcfcfcf);
        public static Color Skin(int dark, int light) => Hex(EditorGUIUtility.isProSkin ? dark : light);
        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private static GUIStyle _header, _nestedHeader, _section, _subsection, _button, _muted, _toolbarPopup;
        private static bool _dark;
        private static readonly List<StylePair> Fields = new List<StylePair>();
        private static readonly List<Texture2D> Textures = new List<Texture2D>();
        private static int _depth;

        public static GUIStyle Header { get { Ensure(); return _header; } }
        public static GUIStyle NestedHeader { get { Ensure(); return _nestedHeader; } }
        public static GUIStyle Section { get { Ensure(); return _section; } }
        public static GUIStyle Subsection { get { Ensure(); return _subsection; } }
        public static GUIStyle Button { get { Ensure(); return _button; } }
        public static GUIStyle ToolbarPopup { get { Ensure(); return _toolbarPopup; } }
        public static GUIStyle MutedLabel { get { Ensure(); return _muted; } }

        private static void Ensure()
        {
            if (_header != null && _dark == EditorGUIUtility.isProSkin) return;
            _dark = EditorGUIUtility.isProSkin;
            foreach (var texture in Textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            Textures.Clear();
            Fields.Clear();
            _header = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12, alignment = TextAnchor.MiddleLeft,
                fixedHeight = HeaderHeight, padding = new RectOffset(24, 8, 0, 0),
                margin = new RectOffset(0, 0, 4, 4), contentOffset = Vector2.zero
            };
            _header.normal.textColor = Text;
            _nestedHeader = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12, alignment = TextAnchor.MiddleLeft, fixedHeight = SectionHeight,
                padding = new RectOffset(24, 8, 0, 0), margin = new RectOffset(0, 0, 0, 0), contentOffset = Vector2.zero
            };
            _nestedHeader.normal.textColor = Text;
            _section = new GUIStyle(_header) { fontSize = 12, fixedHeight = 0, padding = new RectOffset() };
            _subsection = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), fontSize = 11 };
            _subsection.normal.textColor = Text;
            _muted = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            _muted.normal.textColor = Muted;

            var field = RoundedTexture(Input, Border);
            var focus = RoundedTexture(Input, Accent);
            var button = RoundedTexture(Surface, Surface);
            var hover = RoundedTexture(Hover, Hover);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleCenter, border = new RectOffset(5, 5, 5, 5), padding = new RectOffset(8, 8, 3, 3) };
            SetStates(_button, button, hover, focus);
            AddField(EditorStyles.textField, field, field, focus);
            AddField(EditorStyles.numberField, field, field, focus);
            AddField(GUI.skin.textField, field, field, focus);
            AddField(GUI.skin.button, button, hover, focus);
            var popup = new GUIStyle(EditorStyles.popup) { fixedHeight = 0, padding = new RectOffset(6, 18, 0, 0), border = new RectOffset(5, 16, 5, 5), alignment = TextAnchor.MiddleLeft };
            SetStates(popup, RoundedTexture(Input, Border, popup: true), RoundedTexture(Surface, Border, popup: true), RoundedTexture(Input, Accent, popup: true));
            Fields.Add(new StylePair(EditorStyles.popup, popup));
            _toolbarPopup = new GUIStyle(popup) { fixedHeight = 24, padding = new RectOffset(8, 18, 0, 0) };
            SetStates(_toolbarPopup, RoundedTexture(Surface, Surface, popup: true), RoundedTexture(Hover, Hover, popup: true), RoundedTexture(Surface, Accent, popup: true));
            var toggle = new GUIStyle(EditorStyles.toggle) {
                fixedWidth = 16, fixedHeight = 16, border = new RectOffset(4, 4, 4, 4),
                overflow = new RectOffset(), padding = new RectOffset(), margin = new RectOffset(),
                contentOffset = Vector2.zero, alignment = TextAnchor.MiddleCenter
            };
            SetStates(toggle, field, hover, focus);
            var checkedTexture = RoundedTexture(Hover, Border, check: true);
            toggle.onNormal.background = toggle.onHover.background = toggle.onActive.background = toggle.onFocused.background = checkedTexture;
            toggle.onNormal.scaledBackgrounds = toggle.onHover.scaledBackgrounds = toggle.onActive.scaledBackgrounds = toggle.onFocused.scaledBackgrounds = null;
            Fields.Add(new StylePair(EditorStyles.toggle, toggle));
        }

        private static void AddField(GUIStyle original, Texture2D normal, Texture2D hover, Texture2D focus)
        {
            if (Fields.Exists(p => ReferenceEquals(p.Target, original))) return;
            var styled = new GUIStyle(original) {
                border = new RectOffset(5, 5, 5, 5),
                padding = new RectOffset(6, 6, 0, 0), contentOffset = Vector2.zero,
                fixedHeight = 0,
                alignment = ReferenceEquals(original, GUI.skin.button) ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft
            };
            SetStates(styled, normal, hover, focus);
            if (ReferenceEquals(original, GUI.skin.button))
            {
                var selected = RoundedTexture(Hover, Accent);
                foreach (var state in new[] { styled.onNormal, styled.onHover, styled.onActive, styled.onFocused })
                { state.background = selected; state.scaledBackgrounds = null; state.textColor = Text; }
            }
            Fields.Add(new StylePair(original, styled));
        }

        private static void SetStates(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D focus)
        {
            style.normal.background = normal; style.hover.background = hover;
            style.active.background = focus; style.focused.background = focus;
            style.normal.scaledBackgrounds = style.hover.scaledBackgrounds = style.active.scaledBackgrounds = style.focused.scaledBackgrounds = null;
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Text;
        }

        private sealed class StylePair
        {
            public readonly GUIStyle Target;
            private readonly GUIStyle _saved, _styled;
            public StylePair(GUIStyle target, GUIStyle styled) { Target = target; _saved = new GUIStyle(target); _styled = styled; }
            public void Set(bool active)
            {
                var source = active ? _styled : _saved;
                Target.normal = source.normal; Target.hover = source.hover;
                Target.active = source.active; Target.focused = source.focused; Target.border = source.border;
                Target.alignment = source.alignment;
                Target.padding = source.padding; Target.margin = source.margin; Target.overflow = source.overflow;
                Target.fixedWidth = source.fixedWidth; Target.fixedHeight = source.fixedHeight;
                Target.contentOffset = source.contentOffset;
                Target.onNormal = source.onNormal; Target.onHover = source.onHover;
                Target.onActive = source.onActive; Target.onFocused = source.onFocused;
            }
        }

        public sealed class Scope : IDisposable
        {
            public Scope()
            {
                Ensure();
                if (_depth++ == 0) foreach (var field in Fields) field.Set(true);
            }
            public void Dispose() { if (--_depth == 0) foreach (var field in Fields) field.Set(false); }
        }

        private static Texture2D RoundedTexture(Color fill, Color line, bool check = false, bool popup = false)
        {
            const int size = 20;
            int width = popup ? 48 : size;
            var texture = new Texture2D(width, size, TextureFormat.RGBA32, false) { name = "Thry field surface", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Abs(x + .5f - width / 2f) - (width / 2f - 4);
                float dy = Mathf.Abs(y + .5f - size / 2f) - (size / 2f - 4);
                float distance = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0) - 4;
                var color = Color.Lerp(fill, line, Mathf.Clamp01(distance + 1.5f));
                color.a *= Mathf.Clamp01(.5f - distance);
                if (check)
                {
                    var p = new Vector2(x + .5f, y + .5f);
                    float d = Mathf.Min(SegmentDistance(p, new Vector2(5, 10), new Vector2(8, 7)), SegmentDistance(p, new Vector2(8, 7), new Vector2(15, 14)));
                    color = Color.Lerp(color, Text, Mathf.Clamp01(1.8f - d));
                }
                if (popup && y >= 7 && y <= 11 && Mathf.Abs(x - (width - 9)) <= y - 7) color = Muted;
                pixels[y * width + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(false, true); Textures.Add(texture); return texture;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            return Vector2.Distance(p, a + (b - a) * Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude));
        }

        public static void Fill(Rect rect, Color color, float radius = 5)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, Vector4.zero, radius);
        }

        public static void Panel(Rect rect, Color color, float radius = 5)
        {
            Fill(rect, Border, radius);
            Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), color, Mathf.Max(0, radius - 1));
        }

        internal static void DrawHeader(ShaderGroup group, Rect rect, GUIContent content, GUIStyle style)
        {
            bool excluded = SectionEditing.IsExcluded?.Invoke(group) == true;
            Color surface = excluded ? Excluded : (rect.Contains(Event.current.mousePosition) ? Hover : Surface);
            if (group.XOffset == 0) Fill(rect, surface, 6);
            else
            {
                Color nested = excluded ? Excluded : rect.Contains(Event.current.mousePosition) ? Hover : NestedSurface;
                Fill(new Rect(rect.x, rect.y + 1, rect.width, rect.height - 2), nested, 4);
            }
            GUI.Label(rect, content, style);
        }

        internal static void DrawSection(ShaderGroup group, Rect rect, bool expanded, bool hasHeader, bool subsection)
        {
            if (expanded) Fill(rect, Body, 5);
            if (!hasHeader) return;
            var header = new Rect(rect.x, rect.y, rect.width, SectionHeight);
            bool excluded = SectionEditing.IsExcluded?.Invoke(group) == true;
            Color color = excluded ? Excluded : header.Contains(Event.current.mousePosition) ? Hover : NestedSurface;
            Fill(new Rect(header.x, header.y + 1, header.width, header.height - 2), color, 4);
            if (expanded) Fill(new Rect(rect.x + 1, rect.y + SectionHeight, 1, Mathf.Max(0, rect.height - SectionHeight)), Border, 0);
        }

        internal static void Chevron(Rect rect, bool expanded)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = Handles.color;
            Handles.color = Muted * GUI.color;
            float x = rect.x + 10, y = rect.center.y;
            var a = expanded ? new Vector3(x - 3, y - 1) : new Vector3(x - 1, y - 3);
            var b = expanded ? new Vector3(x, y + 2) : new Vector3(x + 2, y);
            var c = expanded ? new Vector3(x + 3, y - 1) : new Vector3(x - 1, y + 3);
            Handles.DrawAAPolyLine(1.6f, a, b, c);
            Handles.color = old;
        }
    }
}
