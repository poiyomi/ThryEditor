using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>A compact switch with distinct off, on, and partially enabled positions.</summary>
    public static class SlidingToggle
    {
        public const float Width = 32;
        public const int ReservedWidth = 38;

        public static bool Draw(Rect rect, bool value, bool mixed, GUIContent tooltip)
        {
            // Keep Unity's toggle input handling; draw a pill and thumb instead of a checkbox.
            EditorGUI.BeginChangeCheck();
            bool next = GUI.Toggle(rect, mixed ? false : value, GUIContent.none, GUIStyle.none);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed) { value = next; mixed = false; }

            if (Event.current.type == EventType.Repaint)
            {
                Color track = new Color(0.16f, 0.16f, 0.16f);
                if (GUI.enabled && rect.Contains(Event.current.mousePosition)) track *= 1.12f;
                track.a = GUI.enabled ? 1 : 0.45f;
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, track, Vector4.zero, rect.height / 2);

                float diameter = rect.height - 4;
                float position = mixed ? 0.5f : value ? 1 : 0;
                Rect thumb = new Rect(rect.x + 2 + position * (rect.width - diameter - 4), rect.y + 2, diameter, diameter);
                Color thumbColor = new Color(0.96f, 0.96f, 0.96f, GUI.enabled ? 1 : 0.5f);
                GUI.DrawTexture(thumb, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, thumbColor, Vector4.zero, diameter / 2);
                if (mixed)
                    EditorGUI.DrawRect(new Rect(thumb.center.x - 3, thumb.center.y - 1, 6, 2), track);
            }

            GUI.Label(rect, tooltip);
            if (GUI.enabled) EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return value;
        }
    }
}
