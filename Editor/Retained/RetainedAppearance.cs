using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // Use authored USS classes: no generated assets, private Unity APIs, or per-frame tree walks.
    internal static class RetainedAppearance
    {
        private sealed class Registration { }
        private static readonly ConditionalWeakTable<VisualElement, Registration> Installed = new ConditionalWeakTable<VisualElement, Registration>();
        private static readonly HashSet<VisualElement> Attached = new HashSet<VisualElement>();

        internal static void Install(VisualElement root)
        {
            if (!Installed.TryGetValue(root, out _))
            {
                Installed.Add(root, new Registration());
                root.RegisterCallback<AttachToPanelEvent>(e => {
                    if (e.target != root) return;
                    Attached.Add(root); Apply(root);
                });
                root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) Attached.Remove(root); });
            }
            if (root.panel != null) Attached.Add(root);
            Apply(root);
        }

        internal static void Refresh()
        {
            foreach (var root in Attached) Apply(root);
        }

        private static void Apply(VisualElement root)
        {
            root.EnableInClassList("thry-dark", !root.ClassListContains("thry-light"));
            var config = Config.Instance;
            SetShade(root, "thry-gray-dark-", config.inspectorDarkGray);
            SetShade(root, "thry-gray-medium-", config.inspectorMediumGray);
            SetShade(root, "thry-gray-light-", config.inspectorLightGray);
            for (int i = 1; i <= 3; i++) root.EnableInClassList("thry-text-size-" + i, (int)config.inspectorTextSize == i);
        }

        private static void SetShade(VisualElement root, string prefix, int value)
        {
            value = Mathf.Clamp(value, -20, 20);
            for (int i = -20; i <= 20; i++)
            {
                if (i == 0) continue;
                root.EnableInClassList(prefix + (i < 0 ? "n" : "p") + Mathf.Abs(i), value == i);
            }
        }
    }
}
