using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Thry.ThryEditor
{
    public sealed class EnumLabelContext
    {
        public Material[] Materials;
        public IReadOnlyList<Renderer> Renderers;
        public string[] Stock;
    }

    public sealed class EnumAction
    {
        public string Text;
        public Action Run;
    }

    public static class EnumLabels
    {
        private static readonly Dictionary<string, Func<EnumLabelContext, string[]>> Providers = new Dictionary<string, Func<EnumLabelContext, string[]>>();
        private static readonly Dictionary<string, Func<EnumLabelContext, float, IEnumerable<EnumAction>>> ActionProviders = new Dictionary<string, Func<EnumLabelContext, float, IEnumerable<EnumAction>>>();
        private static readonly Dictionary<string, Action<EnumLabelContext>> PickHandlers = new Dictionary<string, Action<EnumLabelContext>>();
        private static readonly HashSet<string> Failed = new HashSet<string>();

        public static int Revision { get; private set; }

        public static void Register(string property, Func<EnumLabelContext, string[]> labels)
        {
            if (string.IsNullOrEmpty(property)) return;
            if (labels == null) Providers.Remove(property); else Providers[property] = labels;
            Failed.Remove(property);
            Invalidate();
        }

        public static void Unregister(string property) { if (property != null && Providers.Remove(property)) Invalidate(); }

        // Actions are listed under the choices in the property's dropdown, for the value it has when opened.
        public static void RegisterActions(string property, Func<EnumLabelContext, float, IEnumerable<EnumAction>> actions)
        {
            if (string.IsNullOrEmpty(property)) return;
            if (actions == null) ActionProviders.Remove(property); else ActionProviders[property] = actions;
            Invalidate();
        }

        // Runs after the user picks a value in the property's control.
        public static void RegisterPick(string property, Action<EnumLabelContext> picked)
        {
            if (string.IsNullOrEmpty(property)) return;
            if (picked == null) PickHandlers.Remove(property); else PickHandlers[property] = picked;
            Invalidate();
        }

        public static void Invalidate() => Revision++;

        internal static bool Has(string property) => property != null && Providers.ContainsKey(property);

        // Locking renames animated properties to <name>_<suffix>; labels stay registered under the original name.
        internal static string Resolve(string property, Object[] targets)
        {
            if (string.IsNullOrEmpty(property) || Providers.Count + ActionProviders.Count + PickHandlers.Count == 0) return null;
            if (Providers.ContainsKey(property) || ActionProviders.ContainsKey(property) || PickHandlers.ContainsKey(property)) return property;
            foreach (var key in Providers.Keys.Concat(ActionProviders.Keys).Concat(PickHandlers.Keys))
            {
                if (property.Length <= key.Length + 1 || property[key.Length] != '_' || !property.StartsWith(key, StringComparison.Ordinal)) continue;
                string suffix = property.Substring(key.Length + 1);
                foreach (var target in targets ?? Array.Empty<Object>())
                    if (target is Material material && material != null && RenameSuffix(material) == suffix) return key;
            }
            return null;
        }

        private static string RenameSuffix(Material material)
        {
            string suffix = material.GetTag(ShaderOptimizer.TAG_LOCKED_RENAME_SUFFIX, false, "");
            return suffix.Length > 0 ? suffix : ShaderOptimizer.GetRenamedPropertySuffix(material);
        }

        internal static string[] Get(string property, EnumLabelContext context)
        {
            if (context?.Stock == null || property == null || !Providers.TryGetValue(property, out var provider)) return null;
            try
            {
                var labels = provider(context);
                if (labels == null || labels.Length != context.Stock.Length) return null;
                labels = labels.Select((label, i) => string.IsNullOrEmpty(label) ? context.Stock[i] : label).ToArray();
                // Choices are matched by text, so two equal labels would select the wrong value.
                return labels.Distinct().Count() == labels.Length ? labels : null;
            }
            catch (Exception e)
            {
                if (Failed.Add(property)) Debug.LogException(e);
                return null;
            }
        }

        internal sealed class Source
        {
            private readonly string _property;
            private readonly string[] _stock;
            private string _key;
            private int _revision = -1, _dirty;
            private Object[] _targets = Array.Empty<Object>();
            private IReadOnlyList<Renderer> _renderers;

            internal Source(string property, string[] stock) { _property = property; _stock = stock; Labels = stock; }

            internal string[] Labels { get; private set; }

            internal bool Update(Object[] targets, IReadOnlyList<Renderer> renderers)
            {
                targets = targets ?? Array.Empty<Object>();
                bool changed = _revision != Revision || !Same(targets);
                if (changed) { _revision = Revision; _targets = (Object[])targets.Clone(); _key = Resolve(_property, _targets); }
                if (_key == null) return changed && Set(_stock);
                int dirty = DirtyCount();
                if (!changed && dirty == _dirty && ReferenceEquals(renderers, _renderers)) return false;
                _dirty = dirty; _renderers = renderers;
                var context = new EnumLabelContext
                {
                    Materials = _targets.OfType<Material>().Where(m => m != null).ToArray(),
                    Renderers = renderers ?? Array.Empty<Renderer>(),
                    Stock = (string[])_stock.Clone()
                };
                return Set(Get(_key, context) ?? _stock);
            }

            private bool Same(Object[] targets)
            {
                if (targets.Length != _targets.Length) return false;
                for (int i = 0; i < targets.Length; i++) if (!ReferenceEquals(targets[i], _targets[i])) return false;
                return true;
            }

            private int DirtyCount()
            {
                int count = 0;
                foreach (var target in _targets) if (target != null) count += EditorUtility.GetDirtyCount(target);
                return count;
            }

            internal List<EnumAction> Actions(float value)
            {
                if (_key == null || !ActionProviders.TryGetValue(_key, out var provider)) return null;
                try { return provider(Context(), value)?.Where(a => a != null && !string.IsNullOrEmpty(a.Text) && a.Run != null).ToList(); }
                catch (Exception e)
                {
                    if (Failed.Add(_key)) Debug.LogException(e);
                    return null;
                }
            }

            internal void Picked()
            {
                if (_key == null || !PickHandlers.TryGetValue(_key, out var handler)) return;
                try { handler(Context()); }
                catch (Exception e) { if (Failed.Add(_key)) Debug.LogException(e); }
            }

            private EnumLabelContext Context() => new EnumLabelContext
            {
                Materials = _targets.OfType<Material>().Where(m => m != null).ToArray(),
                Renderers = _renderers ?? Array.Empty<Renderer>(),
                Stock = (string[])_stock.Clone()
            };

            private bool Set(string[] labels)
            {
                if (labels.SequenceEqual(Labels)) return false;
                Labels = labels;
                return true;
            }
        }
    }
}
