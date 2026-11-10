using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>The Inspector elements that host an editor.</summary>
    public static class RetainedInspectorHost
    {
        private static readonly PropertyInfo HostedEditor = typeof(InspectorElement).GetProperty("editor",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo Recreate = typeof(InspectorElement).GetMethod("CreateInspectorElementFromSerializedObject",
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(SerializedObject) }, null);

        public static bool CanRebuild => HostedEditor != null && Recreate != null;

        internal static bool Hosts(InspectorElement inspector, Editor editor)
            => HostedEditor != null && ReferenceEquals(HostedEditor.GetValue(inspector), editor);

        public static List<InspectorElement> Find(Editor editor)
        {
            var hosts = new List<InspectorElement>();
            if (HostedEditor == null) return hosts;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                foreach (var inspector in window.rootVisualElement.Query<InspectorElement>().ToList())
                    if (Hosts(inspector, editor)) hosts.Add(inspector);
            return hosts;
        }

        // Keeps Unity's InspectorElement, editor, header, bindings and foldout, and asks the editor for its
        // inspector again. A tracker refresh alone reuses the body and cannot change its UI framework.
        public static void Rebuild(Editor editor)
        {
            if (!CanRebuild) return;
            foreach (var inspector in Find(editor))
                Recreate.Invoke(inspector, new object[] { editor.serializedObject });
        }
    }

    /// <summary>Puts the retained view into Inspectors that draw a material editor's body through IMGUI.</summary>
    internal sealed class RetainedInspectorMount
    {
        private sealed class Entry
        {
            internal InspectorElement Host;
            internal MaterialInspectorView View;
            internal bool OwnsView;
            internal readonly List<VisualElement> Siblings = new List<VisualElement>();
            internal EventCallback<DetachFromPanelEvent> SiblingDetached;
        }

        private const double SEARCH_INTERVAL = 1;
        private readonly List<Entry> _entries = new List<Entry>();
        private MaterialEditor _editor;
        private double _nextSearch;

        /// <summary>
        /// True when an Inspector shows, or is about to show, the retained view for this editor, so its IMGUI
        /// pass has nothing to draw. Call during Layout only.
        /// </summary>
        internal bool Show(MaterialEditor editor)
        {
            if (!ReferenceEquals(_editor, editor))
            {
                Clear();
                _editor = editor;
                _nextSearch = 0;
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry.Host.panel == null || (entry.View != null && entry.View.parent != entry.Host)) Remove(entry, false);
            }
            if (_entries.Count > 0) return true;
            if (EditorApplication.timeSinceStartup < _nextSearch || !RetainedMaterialModel.HasValidTargets(editor)) return false;

            foreach (InspectorElement host in RetainedInspectorHost.Find(editor))
            {
                if (host.panel == null) continue;
                var entry = new Entry { Host = host };
                _entries.Add(entry);
                // Unity refuses hierarchy changes while it measures an IMGUI container.
                host.schedule.Execute(() => Add(entry));
            }
            if (_entries.Count == 0) _nextSearch = EditorApplication.timeSinceStartup + SEARCH_INTERVAL;
            return _entries.Count > 0;
        }

        internal void Clear()
        {
            for (int i = _entries.Count - 1; i >= 0; i--) Remove(_entries[i], false);
        }

        private bool IsCurrent(InspectorElement host)
            => RetainedInspectorHost.Hosts(host, _editor) && RetainedMaterialModel.HasValidTargets(_editor);

        private void Add(Entry entry)
        {
            if (!_entries.Contains(entry)) return;
            InspectorElement host = entry.Host;
            if (host.panel == null || !IsCurrent(host))
            {
                _entries.Remove(entry);
                _nextSearch = EditorApplication.timeSinceStartup + SEARCH_INTERVAL;
                return;
            }
            // An editor that builds the view itself needs no second one.
            entry.View = host.Children().OfType<MaterialInspectorView>().FirstOrDefault(view => ReferenceEquals(view.Editor, _editor));
            if (entry.View != null) return;

            entry.OwnsView = true;
            entry.View = new MaterialInspectorView(_editor, () => { });
            // Unity swaps the editor's own element out when the Inspector moves to another editor or rebuilds.
            // The view leaves in the same step, so it never shows the previous material.
            entry.SiblingDetached = evt => { if (((VisualElement)evt.target).parent != host) Remove(entry, true); };
            foreach (VisualElement sibling in host.Children())
            {
                entry.Siblings.Add(sibling);
                sibling.RegisterCallback(entry.SiblingDetached);
            }
            host.Add(entry.View);
            entry.View.schedule.Execute(() => { if (!IsCurrent(host)) Remove(entry, true); }).Every(250);
        }

        // Removing right away is only safe outside a GUI pass.
        private void Remove(Entry entry, bool now)
        {
            if (!_entries.Remove(entry)) return;
            foreach (VisualElement sibling in entry.Siblings) sibling.UnregisterCallback(entry.SiblingDetached);
            if (!entry.OwnsView) return;
            if (now) entry.View.RemoveFromHierarchy();
            else entry.View.schedule.Execute(entry.View.RemoveFromHierarchy);
        }
    }
}
