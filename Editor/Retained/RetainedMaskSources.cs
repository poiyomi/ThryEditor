using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.Drawers
{
    // IDs are serialized in material vectors and shared with PoiMask1/PoiMask4.
    // Append new IDs; never reorder existing entries.
    public sealed class ThryMaskSourcesDrawer : MaterialPropertyDrawer
    {
        public static readonly string[] SourceNames = { "Off", "R", "G", "B", "A" };
        readonly string[] labels;
        public ThryMaskSourcesDrawer() : this("Mask") { }
        public ThryMaskSourcesDrawer(string a) { labels = new[] { a }; }
        public ThryMaskSourcesDrawer(string a, string b) { labels = new[] { a, b }; }
        public ThryMaskSourcesDrawer(string a, string b, string c) { labels = new[] { a, b, c }; }
        public ThryMaskSourcesDrawer(string a, string b, string c, string d) { labels = new[] { a, b, c, d }; }
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return EditorGUIUtility.singleLineHeight;
        }
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            position.height = EditorGUIUtility.singleLineHeight;
            bool previousMixed = EditorGUI.showMixedValue;
            if (labels.Length > 1) position = EditorGUI.PrefixLabel(position, label);
            float cellWidth = position.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int component = i;
                EditorGUI.showMixedValue = prop.targets.OfType<Material>()
                    .Select(m => m.GetVector(prop.name)[component]).Distinct().Skip(1).Any();
                var cell = new Rect(position.x + i * cellWidth, position.y, cellWidth, position.height);
                if (labels.Length > 1)
                {
                    EditorGUI.LabelField(new Rect(cell.x, cell.y, 12, cell.height), new GUIContent("RGBA"[i].ToString(), labels[i]));
                    cell.x += 12; cell.width -= 16;
                }
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(cell, labels.Length == 1 ? label.text : "",
                    Mathf.Clamp(Mathf.RoundToInt(prop.vectorValue[i]), 0, SourceNames.Length - 1), SourceNames);
                if (EditorGUI.EndChangeCheck())
                {
                    editor.RegisterPropertyChangeUndo("Mask source");
                    foreach (var material in prop.targets.OfType<Material>())
                    {
                        var value = material.GetVector(prop.name); value[i] = selected;
                        material.SetVector(prop.name, value);
                    }
                }
            }
            EditorGUI.showMixedValue = previousMixed;
        }
    }
}

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        private void MaskSources(VisualElement input, ShaderProperty property, DrawerAttribute attribute)
        {
            var labels = attribute.Args.Length == 0 ? new[] { "Mask" } : attribute.Args;
            input.tooltip = "Multiply this mask by a vertex color channel. Off keeps the texture mask unchanged.";
            var row = new VisualElement(); row.AddToClassList("thry-components"); input.Add(row);
            for (int i = 0; i < Math.Min(labels.Length, 4); i++)
            {
                int component = i;
                var field = new DropdownField(new List<string>(Drawers.ThryMaskSourcesDrawer.SourceNames), 0)
                { name = "mask-source-" + property.MaterialProperty.name + "-" + i,
                    label = labels.Length == 1 ? "" : "RGBA"[i].ToString(),
                    tooltip = labels[i] + ": multiply by a vertex color channel." };
                field.AddToClassList("thry-input");
                field.AddToClassList("thry-component");
                if (i > 0) field.AddToClassList("thry-component-spaced");
                _view.UseInspectorMenu(field);
                TrackVisible(field, () => {
                    int selected = Mathf.Clamp(Mathf.RoundToInt(property.MaterialProperty.vectorValue[component]),
                        0, Drawers.ThryMaskSourcesDrawer.SourceNames.Length - 1);
                    var values = Model.Owners(property).Select(m => m.GetVector(property.MaterialProperty.name)[component]);
                    bool mixed = values.Distinct().Skip(1).Any();
                    SynchronizeValue(field, Drawers.ThryMaskSourcesDrawer.SourceNames[selected], mixed);
                });
                field.RegisterValueChangedCallback(e => {
                    int selected = Array.IndexOf(Drawers.ThryMaskSourcesDrawer.SourceNames, e.newValue);
                    if (selected >= 0) Model.VectorComponent(property, component, selected);
                });
                row.Add(field);
            }
        }
    }
}
