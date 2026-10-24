using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public static class SectionExtensions
    {
        public sealed class Context
        {
            private readonly RetainedMaterialModel _model;
            private readonly RetainedFields _fields;

            internal Context(RetainedMaterialModel model, RetainedFields fields) { _model = model; _fields = fields; }

            public ShaderEditor Editor => _model.Shader;
            public IReadOnlyList<Material> Materials => _model.Shader.Materials;
            public IReadOnlyList<Renderer> Renderers => _model.Renderers;
            public bool CanEdit(string property) => property != null && _model.Shader.PropertyDictionary != null
                && _model.Shader.PropertyDictionary.TryGetValue(property, out var part) && _model.CanEdit(part);
            public void Mutate(string undoName, Action<Material> edit) => _model.Mutate(undoName, edit);
            public void Notify() => _model.Notify();

            // Bindings share one refresh pass, so an extension that throws must not stop the fields after it.
            public void Track(VisualElement element, Action update)
            {
                if (element == null || update == null) return;
                bool logged = false;
                _fields.TrackVisible(element, () =>
                {
                    try { update(); }
                    catch (Exception e) { if (!logged) { logged = true; Debug.LogException(e); } }
                });
            }
        }

        private sealed class Extension
        {
            internal Func<Context, VisualElement> Create;
            internal HashSet<string> Hide;
            internal bool Failed;
        }

        private static readonly Dictionary<string, Extension> Extensions = new Dictionary<string, Extension>();
        private static readonly List<Action<string, IReadOnlyList<Material>, GenericMenu>> Menus = new List<Action<string, IReadOnlyList<Material>, GenericMenu>>();
        private static readonly List<Func<string, IReadOnlyList<Material>, HeaderButton>> Buttons = new List<Func<string, IReadOnlyList<Material>, HeaderButton>>();

        public sealed class HeaderButton
        {
            public Texture Icon;
            public string Tooltip;
            public Func<bool> Lit;
            public Func<GenericMenu> Menu;
            public Action Click;
            public string Class;
        }

        public static void RegisterHeaderButton(Func<string, IReadOnlyList<Material>, HeaderButton> create) { if (create != null && !Buttons.Contains(create)) Buttons.Add(create); }

        public static void UnregisterHeaderButton(Func<string, IReadOnlyList<Material>, HeaderButton> create) => Buttons.Remove(create);

        internal static List<HeaderButton> CreateHeaderButtons(string sectionProperty, IReadOnlyList<Material> materials)
        {
            var list = new List<HeaderButton>();
            if (sectionProperty == null || materials == null || materials.Count == 0) return list;
            foreach (var create in Buttons.ToArray())
            {
                try { var button = create(sectionProperty, materials); if (button != null && (button.Menu != null || button.Click != null)) list.Add(button); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return list;
        }

        public static void Register(string sectionProperty, Func<Context, VisualElement> create, params string[] hide)
        {
            if (string.IsNullOrEmpty(sectionProperty) || create == null) return;
            Extensions[sectionProperty] = new Extension { Create = create, Hide = new HashSet<string>(hide ?? Array.Empty<string>()) };
        }

        public static void Unregister(string sectionProperty) { if (sectionProperty != null) Extensions.Remove(sectionProperty); }

        public static void RegisterMenu(Action<string, IReadOnlyList<Material>, GenericMenu> extend) { if (extend != null && !Menus.Contains(extend)) Menus.Add(extend); }

        public static void UnregisterMenu(Action<string, IReadOnlyList<Material>, GenericMenu> extend) => Menus.Remove(extend);

        internal static void ExtendMenu(string sectionProperty, IReadOnlyList<Material> materials, GenericMenu menu)
        {
            if (sectionProperty == null || materials == null || materials.Count == 0 || menu == null) return;
            foreach (var extend in Menus.ToArray())
            {
                try { extend(sectionProperty, materials, menu); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static bool Has(string sectionProperty) => sectionProperty != null && Extensions.ContainsKey(sectionProperty);

        internal static bool IsHidden(string sectionProperty, string property) =>
            sectionProperty != null && property != null && Extensions.TryGetValue(sectionProperty, out var extension) && extension.Hide.Contains(property);

        internal static VisualElement Create(string sectionProperty, Context context)
        {
            if (sectionProperty == null || !Extensions.TryGetValue(sectionProperty, out var extension)) return null;
            try { return extension.Create(context); }
            catch (Exception e)
            {
                if (!extension.Failed) { extension.Failed = true; Debug.LogException(e); }
                return null;
            }
        }
    }
}
