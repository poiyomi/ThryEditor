using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        // IDs are serialized in material vectors and shared with PoiMask1/PoiMask4.
        // Append new IDs; never reorder existing entries.
        static readonly string[] MaskSourceNames = { "Off", "R", "G", "B", "A" };

        private void MaskSources(VisualElement input, ShaderProperty property, DrawerAttribute attribute)
        {
            var labels = attribute.Args.Length == 0 ? new[] { "Mask" } : attribute.Args;
            input.tooltip = "Multiply this mask by a vertex color channel. Off keeps the texture mask unchanged.";
            var row = new VisualElement(); row.AddToClassList("thry-components"); input.Add(row);
            for (int i = 0; i < Math.Min(labels.Length, 4); i++)
            {
                int component = i;
                var field = new DropdownField(new List<string>(MaskSourceNames), 0)
                { name = "mask-source-" + property.MaterialProperty.name + "-" + i,
                    label = labels.Length == 1 ? "" : "RGBA"[i].ToString(),
                    tooltip = labels[i] + ": multiply by a vertex color channel." };
                field.AddToClassList("thry-input");
                field.AddToClassList("thry-component");
                if (i > 0) field.AddToClassList("thry-component-spaced");
                _view.UseInspectorMenu(field);
                TrackVisible(field, () => {
                    int selected = Mathf.Clamp(Mathf.RoundToInt(property.MaterialProperty.vectorValue[component]),
                        0, MaskSourceNames.Length - 1);
                    var values = Model.Owners(property).Select(m => m.GetVector(property.MaterialProperty.name)[component]);
                    bool mixed = values.Distinct().Skip(1).Any();
                    SynchronizeValue(field, MaskSourceNames[selected], mixed);
                });
                field.RegisterValueChangedCallback(e => {
                    int selected = Array.IndexOf(MaskSourceNames, e.newValue);
                    if (selected >= 0) Model.VectorComponent(property, component, selected);
                });
                row.Add(field);
            }
        }
    }
}
