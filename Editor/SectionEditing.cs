using System;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>Optional package integration. Thry does not reference a shader generator.</summary>
    public static class SectionEditing
    {
        public static Action<ShaderEditor> DrawToolbar;
        public static Func<ShaderPart, bool> IsEditingPart;
        public static Action<ShaderGroup, Rect> DrawHeaderToggle;
        public static Func<ShaderPart, bool> HidePart;
        public static Func<ShaderGroup, bool> IsExcluded;

        internal readonly struct HeaderTintScope : IDisposable
        {
            private readonly Color _previous;
            private readonly bool _graphics;

            public HeaderTintScope(ShaderGroup group, bool graphics = false)
            {
                _graphics = graphics;
                _previous = graphics ? GUI.color : GUI.contentColor;
                if (IsExcluded?.Invoke(group) != true) return;
                Color tint = _previous * new Color(0.65f, 0.65f, 0.65f, 1);
                if (graphics) GUI.color = tint; else GUI.contentColor = tint;
            }

            public void Dispose()
            {
                if (_graphics) GUI.color = _previous; else GUI.contentColor = _previous;
            }
        }

        internal static void DrawHeaderBox(ShaderGroup group, Rect rect, GUIContent content, GUIStyle style)
        {
            Color previous = GUI.backgroundColor;
            try
            {
                if (IsExcluded?.Invoke(group) == true)
                    GUI.backgroundColor *= new Color(0.78f, 0.78f, 0.78f, 1);
                GUI.Box(rect, content, style);
            }
            finally { GUI.backgroundColor = previous; }
        }

        // Section borders draw their own backgrounds. Shade only the header, before its controls.
        internal static void DrawExcludedBackground(ShaderGroup group, Rect rect, float radius = 0)
        {
            if (Event.current.type == EventType.Repaint && IsExcluded?.Invoke(group) == true)
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    Color.Lerp(Colors.backgroundDark, Color.black, 0.22f), Vector4.zero, radius);
        }

        internal static bool IsEditing(ShaderPart part) => IsEditingPart?.Invoke(part) == true;
        internal static bool IsHidden(ShaderPart part) => HidePart?.Invoke(part) == true;
    }
}
