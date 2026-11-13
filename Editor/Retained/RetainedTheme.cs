using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // Enables the ability to change the stylesheet theme if the user ever so desires, configured in ThryEditor Settings.
    // Any alternative stylesheets for ThryTheme must possess the extension '.thrytheme.uss' in order to be valid.
    internal static class RetainedTheme
    {
        internal const string BaseSheetName = "ThryTheme";
        internal const string ThemeSuffix = ".thrytheme.uss";

        private sealed class Registration { internal StyleSheet Theme; internal int Version = -1; }
        private static readonly ConditionalWeakTable<VisualElement, Registration> Installed = new ConditionalWeakTable<VisualElement, Registration>();
        private static readonly HashSet<VisualElement> Attached = new HashSet<VisualElement>();
        private static Dictionary<string, string> themePaths;
        private static string[] themeNames;
        private static string selectedName;
        private static int version;

        static RetainedTheme() { RetainedAppearance.Changed += OnAppearanceChanged; }

        internal static bool IsThemePath(string path) => path != null && path.EndsWith(ThemeSuffix, StringComparison.OrdinalIgnoreCase);

        // Theme names for the Settings dropdown, sorted. A theme in Assets wins over a package theme with the same name.
        internal static IReadOnlyList<string> Available { get { Discover(); return themeNames; } }

        private static void Discover()
        {
            if (themePaths != null) return;
            themePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var paths = AssetDatabase.FindAssets("t:StyleSheet")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(IsThemePath)
                .OrderBy(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string file = Path.GetFileName(path);
                string name = file.Substring(0, file.Length - ThemeSuffix.Length);
                if (name.Length > 0 && !themePaths.ContainsKey(name)) themePaths.Add(name, path);
            }
            themeNames = themePaths.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static StyleSheet Selected()
        {
            if (selectedName == null) selectedName = InspectorAppearancePreferences.Shared.Get(Config.Instance).inspectorTheme ?? "";
            if (selectedName.Length == 0) return null;
            Discover();
            return themePaths.TryGetValue(selectedName, out var path) ? AssetDatabase.LoadAssetAtPath<StyleSheet>(path) : null;
        }

        internal static void Install(VisualElement root)
        {
            if (!Installed.TryGetValue(root, out var registration))
            {
                registration = new Registration();
                Installed.Add(root, registration);
                root.RegisterCallback<AttachToPanelEvent>(e => {
                    if (e.target != root) return;
                    Attached.Add(root); Apply(root, registration);
                });
                root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) Attached.Remove(root); });
            }
            if (root.panel != null) Attached.Add(root);
            Apply(root, registration);
        }

        internal static void RequestReload()
        {
            EditorApplication.delayCall -= Reload;
            EditorApplication.delayCall += Reload;
        }

        private static void Reload()
        {
            themePaths = null; themeNames = null; version++;
            // Refresh reapplies every root through Changed and updates the Settings dropdown.
            RetainedAppearance.Refresh();
        }

        private static void OnAppearanceChanged()
        {
            selectedName = null;
            foreach (var root in Attached.ToArray())
                if (Installed.TryGetValue(root, out var registration)) Apply(root, registration);
        }

        private static void Apply(VisualElement root, Registration registration)
        {
            var baseSheet = Resources.Load<StyleSheet>(BaseSheetName);
            if (baseSheet == null) return;
            var theme = Selected();
            var set = root.styleSheets;
            if (registration.Version == version && ReferenceEquals(registration.Theme, theme) && set.Contains(baseSheet)) return;
            if (!ReferenceEquals(registration.Theme, null))
            {
                // A deleted theme cannot be passed to Remove, so rebuild the list without the previous theme.
                // Re-adding a reimported theme also makes the panel pick up its new rules.
                var keep = new List<StyleSheet>();
                for (int i = 0; i < set.count; i++)
                {
                    var sheet = set[i];
                    if (sheet != null && !ReferenceEquals(sheet, registration.Theme)) keep.Add(sheet);
                }
                set.Clear();
                foreach (var sheet in keep) set.Add(sheet);
            }
            if (!set.Contains(baseSheet)) set.Add(baseSheet);
            if (theme != null) set.Add(theme);
            registration.Theme = theme;
            registration.Version = version;
        }
    }

    internal sealed class RetainedThemeImports : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(RetainedTheme.IsThemePath)) RetainedTheme.RequestReload();
        }
    }
}
