using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

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
        private static string selectedName, selectedPath;
        private static bool selectedGradients;
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
            if (selectedName == null)
            {
                selectedName = InspectorAppearancePreferences.Shared.Get(Config.Instance).inspectorTheme ?? "";
                selectedPath = null; selectedGradients = false;
                if (selectedName.Length > 0)
                {
                    Discover();
                    if (themePaths.TryGetValue(selectedName, out var found)) { selectedPath = found; selectedGradients = UsesGradients(found); }
                }
            }
            return selectedPath != null ? AssetDatabase.LoadAssetAtPath<StyleSheet>(selectedPath) : null;
        }

        // Gradient painters are installed only for a theme that mentions them, so other themes cost nothing extra.
        private static bool UsesGradients(string path)
        {
            try { return File.ReadAllText(Path.GetFullPath(path)).Contains("--thry-gradient"); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { return true; }
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
            themePaths = null; themeNames = null; selectedName = null; version++;
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
            RetainedThemeGradients.SetEnabled(root, theme != null && selectedGradients);
        }
    }

    // Optional component for Vertical Gradients on the UI Theme. Set by --thry-gradient-top and --thry-gradient-bottom arguments in the stylesheet.
    // If used by the theme, it will paint inside its border and rounded corners, over the background color and image, and underneath its text and children.
    // Surfaces with a transparent background color is not painted and is ignored.
    internal static class RetainedThemeGradients
    {
        internal const string TopProperty = "--thry-gradient-top";
        internal const string BottomProperty = "--thry-gradient-bottom";
        private const int TextureHeight = 64;
        private static readonly CustomStyleProperty<Color> Top = new CustomStyleProperty<Color>(TopProperty);
        private static readonly CustomStyleProperty<Color> Bottom = new CustomStyleProperty<Color>(BottomProperty);
        private static readonly string[] SurfaceClasses = {
            "thry-section-header", "unity-base-popup-field__input", "unity-base-text-field__input"
        };

        private sealed class Marker { }
        private sealed class Surface { internal Texture2D Applied; }
        private sealed class RootState { internal bool Enabled, Pending, Refresh; }
        private static readonly ConditionalWeakTable<VisualElement, Surface> Painted = new ConditionalWeakTable<VisualElement, Surface>();
        private static readonly ConditionalWeakTable<VisualElement, Marker> Watched = new ConditionalWeakTable<VisualElement, Marker>();
        private static readonly ConditionalWeakTable<VisualElement, RootState> Roots = new ConditionalWeakTable<VisualElement, RootState>();
        private static readonly Dictionary<ulong, Texture2D> Textures = new Dictionary<ulong, Texture2D>();
        private static bool cleanupHooked;

        // Called by RetainedTheme whenever a root's theme is applied. While enabled, the root is scanned again after
        // its layout or a scroll view's content changes, so surfaces built later are painted too.
        internal static void SetEnabled(VisualElement root, bool enabled)
        {
            if (!Roots.TryGetValue(root, out var state))
            {
                if (!enabled) return;
                state = new RootState();
                Roots.Add(root, state);
                root.RegisterCallback<GeometryChangedEvent>(e => Request(root, state, false));
            }
            bool changed = state.Enabled != enabled;
            state.Enabled = enabled;
            if (enabled)
            {
                Scan(root, state, false);
                // The new sheets resolve later in the frame; read every surface again once they have.
                Request(root, state, true);
            }
            else if (changed) root.Query<VisualElement>().ForEach(element => {
                if (Painted.TryGetValue(element, out var surface)) Apply(element, surface, null);
            });
        }

        private static void Request(VisualElement root, RootState state, bool refresh)
        {
            if (!state.Enabled) return;
            state.Refresh |= refresh;
            if (state.Pending) return;
            state.Pending = true;
            root.schedule.Execute(() => {
                bool all = state.Refresh;
                state.Pending = false; state.Refresh = false;
                if (state.Enabled) Scan(root, state, all);
            });
        }

        private static void Scan(VisualElement root, RootState state, bool refresh)
        {
            root.Query<VisualElement>().ForEach(element => {
                if (element is ScrollView view && !Watched.TryGetValue(view.contentContainer, out _))
                {
                    Watched.Add(view.contentContainer, new Marker());
                    view.contentContainer.RegisterCallback<GeometryChangedEvent>(e => Request(root, state, false));
                }
                if (!IsSurface(element)) return;
                if (!Painted.TryGetValue(element, out var surface)) Install(element);
                else if (refresh) Update(element, surface);
            });
        }

        private static bool IsSurface(VisualElement element)
        {
            if (element is Button) return true;
            foreach (string name in SurfaceClasses) if (element.ClassListContains(name)) return true;
            return false;
        }

        private static void Install(VisualElement element)
        {
            var surface = new Surface();
            Painted.Add(element, surface);
            element.RegisterCallback<CustomStyleResolvedEvent>(e => { if (e.target == element) Update(element, surface); });
            Update(element, surface);
        }

        private static void Update(VisualElement element, Surface surface)
        {
            Texture2D texture = null;
            bool hasTop = element.customStyle.TryGetValue(Top, out Color top);
            bool hasBottom = element.customStyle.TryGetValue(Bottom, out Color bottom);
            // Surfaces without a fill, such as icon, link and search buttons, stay clear.
            if ((hasTop || hasBottom) && element.resolvedStyle.backgroundColor.a > 0)
            {
                if (!hasTop) top = bottom;
                if (!hasBottom) bottom = top;
                if (top.a > 0 || bottom.a > 0) texture = GradientTexture(top, bottom);
            }
            Apply(element, surface, texture);
        }

        // Setting the inline style resolves the element again; an unchanged texture ends there.
        private static void Apply(VisualElement element, Surface surface, Texture2D texture)
        {
            if (ReferenceEquals(surface.Applied, texture)) return;
            surface.Applied = texture;
            if (texture != null)
            {
                element.style.backgroundImage = new StyleBackground(texture);
                element.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
                element.style.unityBackgroundImageTintColor = Color.white;
            }
            else
            {
                element.style.backgroundImage = StyleKeyword.Null;
                element.style.backgroundSize = StyleKeyword.Null;
                element.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            }
        }

        // One 1x64 texture per color pair. It stays readable, so the dynamic atlas never packs it beside other
        // images that bilinear filtering could bleed into.
        private static Texture2D GradientTexture(Color32 top, Color32 bottom)
        {
            ulong key = ((ulong)Pack(top) << 32) | Pack(bottom);
            if (Textures.TryGetValue(key, out var texture) && texture != null) return texture;
            texture = new Texture2D(1, TextureHeight, TextureFormat.RGBA32, false, false)
            { name = "ThryGradient", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[TextureHeight];
            // Row 0 is the bottom of the texture.
            for (int y = 0; y < TextureHeight; y++) pixels[y] = Color32.Lerp(bottom, top, y / (float)(TextureHeight - 1));
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Textures[key] = texture;
            if (!cleanupHooked)
            {
                // HideAndDontSave textures outlive the domain; without this each script reload leaks them.
                cleanupHooked = true;
                AssemblyReloadEvents.beforeAssemblyReload += DestroyTextures;
                EditorApplication.quitting += DestroyTextures;
            }
            return texture;
        }

        private static uint Pack(Color32 color) => (uint)(color.r | color.g << 8 | color.b << 16 | color.a << 24);

        private static void DestroyTextures()
        {
            foreach (var texture in Textures.Values) if (texture != null) Object.DestroyImmediate(texture);
            Textures.Clear();
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
