using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>Consistent, theme-aware icons for the material inspector toolbar.</summary>
    public static class ToolbarIcons
    {
        private static readonly Dictionary<string, GUIStyle> Styles = new Dictionary<string, GUIStyle>();

        public static GUIStyle Settings => Get("settings");
        public static GUIStyle Tools => Get("wrench");
        public static GUIStyle Edit => Get("pencil");
        public static GUIStyle Modules => Get("puzzle");

        // Resolve during OnGUI so GUIStyles and imported textures are available.
        private static GUIStyle Get(string name)
        {
            string resource = "ThryToolbar/" + name + (EditorGUIUtility.isProSkin ? "-dark" : "-light");
            if (Styles.TryGetValue(resource, out var style) && style.normal.background != null)
                return style;

            var texture = Resources.Load<Texture2D>(resource);
            if (texture == null) return null;

            style = new GUIStyle { normal = new GUIStyleState { background = texture } };
            Styles[resource] = style;
            return style;
        }
    }
}
