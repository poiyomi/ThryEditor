using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.Drawers
{
    // Explicit property names survive module instance expansion and localization.
    public sealed class ThryMaskLayoutDecorator : MaterialPropertyDrawer
    {
        public ThryMaskLayoutDecorator(string uv, string channel, string panning, string strength) { }
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor) => 0;
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor) { }
    }
}

namespace Thry.ThryEditor
{
    // Composes existing property drawers. Callers supply the controls explicitly;
    // the layout does not infer shader semantics or own material values.
    internal static class RetainedMaskLayout
    {
        internal static void Arrange(VisualElement details, DrawerAttribute bindings, VisualElement levels)
        {
            if (bindings == null || bindings.Args.Length != 4 || levels == null) return;
            VisualElement Find(int slot) => details.Children().FirstOrDefault(row => row.name == "property-" + bindings.Args[slot]);
            var uv = Find(0);
            var channel = Find(1);
            var strength = Find(3);
            if (uv != null)
            {
                var values = uv.Q(className: "thry-property-value");
                if (channel != null && values != null)
                {
                    values.style.flexDirection = FlexDirection.Row;
                    foreach (var child in values.Children())
                    { child.style.flexGrow = 1; child.style.flexBasis = 0; }
                    channel.RemoveFromHierarchy();
                    channel.style.flexGrow = 1; channel.style.flexBasis = 0;
                    channel.style.minWidth = 0; channel.style.marginLeft = 6;
                    var label = channel.Q(className: "thry-property-label");
                    if (label != null) { label.style.width = 58; label.style.minWidth = 58; }
                    values.Add(channel);
                }
            }
            if (strength != null) { strength.RemoveFromHierarchy(); details.Add(strength); }

            // A reference drawn inline (such as Global Mask's blend selector)
            // should not also appear as a second standalone row.
            var nestedNames = new HashSet<string>();
            foreach (var row in details.Children())
                foreach (var nested in row.Query<VisualElement>(className: "thry-property").ToList())
                    if (nested != row && !string.IsNullOrEmpty(nested.name)) nestedNames.Add(nested.name);
            foreach (var row in details.Children().ToArray())
                if (!string.IsNullOrEmpty(row.name) && nestedNames.Contains(row.name)) row.RemoveFromHierarchy();
        }
    }
}
