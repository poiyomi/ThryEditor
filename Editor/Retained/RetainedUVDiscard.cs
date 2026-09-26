using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Thry.ThryEditor.Drawers;

namespace Thry.ThryEditor
{
    // Each tile owns its value and animation metadata; column zero is not a controller.
    internal sealed class RetainedUVDiscard : VisualElement
    {
        readonly RetainedMaterialModel model;
        readonly RetainedFields fields;
        readonly ShaderProperty[] tiles;
        readonly Button[] cells = new Button[16];
        readonly Label[] captions = new Label[16], states = new Label[16];
        internal static string Prefix(ShaderGroup group)
        {
            switch (group.MaterialProperty?.name)
            {
                case "m_start_udimdiscardOptions": return "_UDIMDiscardRow";
                case "m_start_udimfacediscardOptions": return "_UDIMFaceDiscardRow";
                default: return null;
            }
        }
        internal static bool CanBuild(ShaderGroup group, RetainedMaterialModel model)
        {
            var prefix = Prefix(group);
            return prefix != null && Enumerable.Range(0, 16).All(i => model.Shader.PropertyDictionary.ContainsKey(prefix + i / 4 + "_" + i % 4));
        }
        internal RetainedUVDiscard(RetainedMaterialModel model, RetainedFields fields, ShaderGroup group, Action<VisualElement, ShaderPart> addOriginal)
        {
            this.model = model; this.fields = fields;
            string prefix = Prefix(group);
            name = "uv-discard-" + prefix;
            tiles = Enumerable.Range(0, 16).Select(i => model.Shader.PropertyDictionary[prefix + i / 4 + "_" + i % 4]).ToArray();
            bool inserted = false;
            foreach (var child in group.Children)
            {
                string id = child.MaterialProperty?.name;
                bool grid = id == "_UDIMDiscardHeader" || id == "_FaceDiscardHeader" || (id != null && id.StartsWith(prefix, StringComparison.Ordinal));
                if (grid)
                {
                    if (!inserted) { inserted = true; Add(BuildGrid()); }
                }
                else addOriginal(this, child);
            }
            fields.Track(this, Synchronize);
        }
        VisualElement BuildGrid()
        {
            var board = new VisualElement { name = "uv-tile-board" }; board.AddToClassList("thry-uv-board");
            for (int v = 3; v >= 0; v--)
            {
                var row = Row(); row.AddToClassList("thry-uv-row"); board.Add(row);
                for (int u = 0; u < 4; u++)
                {
                    int index = v * 4 + u;
                    var property = tiles[index];
                    var cell = new Button(() => Activate(index)) { name = "tile-" + property.MaterialProperty.name, userData = property };
                    cell.AddToClassList("thry-uv-cell");
                    var coordinate = new Label(u + ", " + v); coordinate.AddToClassList("thry-uv-coordinate"); cell.Add(coordinate);
                    captions[index] = new Label(); captions[index].AddToClassList("thry-uv-caption"); cell.Add(captions[index]);
                    states[index] = new Label(); states[index].AddToClassList("thry-uv-state"); cell.Add(states[index]);
                    foreach (var label in cell.Children()) label.pickingMode = PickingMode.Ignore;
                    fields.BindProperty(cell, property, menu => AddTileActions(menu, cell, property, coordinate.text));
                    cells[index] = cell; row.Add(cell);
                }
            }
            return board;
        }
        static VisualElement Row() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; return row; }
        void Activate(int index)
        {
            var property = tiles[index];
            model.Number(property, property.MaterialProperty.hasMixedValue || property.MaterialProperty.GetNumber() <= .5f ? 1 : 0);
            Synchronize();
        }
        void AddTileActions(GenericMenu menu, Button cell, ShaderProperty property, string coordinate)
        {
            model.Refresh();
            var captured = model.Owners(property).Select(m => (Material: m, Shader: m.shader)).ToArray();
            Func<UnityEngine.Object[]> owners = () => {
                if (cell.panel == null || !RetainedMaterialModel.HasValidTargets(model.Editor)) return Array.Empty<UnityEngine.Object>();
                model.Refresh();
                return !model.CanEdit(property) ? Array.Empty<UnityEngine.Object>() : model.Owners(property)
                    .Where(m => captured.Any(c => c.Material == m && c.Shader == m.shader)).Cast<UnityEngine.Object>().ToArray();
            };
            menu.AddSeparator("");
            if (model.CanEdit(property))
            {
                menu.AddItem(new GUIContent("Rename tile"), false, () => {
                    var targets = owners(); if (targets.Length == 0) return;
                    var window = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.rootVisualElement.panel == cell.panel);
                    TileLabelUtility.TileLabelRenamePopup.Show(targets, TileLabelUtility.CanonicalPropertyName(property.MaterialProperty.name), coordinate,
                        (window == null ? Vector2.zero : window.position.position) + cell.worldBound.position, owners);
                });
                menu.AddItem(new GUIContent("Reset tile name"), false, () => TileLabelUtility.ApplyTagToTargets(owners(), TileLabelUtility.CanonicalPropertyName(property.MaterialProperty.name), ""));
            }
        }
        void Synchronize()
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                var p = tiles[i]; p.RefreshRetainedProjection(model.Renderers);
                bool mixed = p.MaterialProperty.hasMixedValue;
                bool discarded = p.MaterialProperty.GetNumber() > .5f;
                var labels = model.Owners(p).Select(m => TileLabelUtility.GetTileLabel(m, p.MaterialProperty.name) ?? "").Distinct().ToArray();
                captions[i].text = labels.Length > 1 ? "Mixed names" : labels.FirstOrDefault() ?? "";
                bool named = !string.IsNullOrEmpty(captions[i].text);
                captions[i].style.display = named ? DisplayStyle.Flex : DisplayStyle.None;
                cells[i].Q<Label>(className: "thry-uv-coordinate").style.display = named ? DisplayStyle.None : DisplayStyle.Flex;
                cells[i].EnableInClassList("thry-uv-has-animation", p.IsAnimated || p.HasMixedAnimatedOwners);
                states[i].text = p.HasMixedAnimatedOwners ? "Mixed" : p.IsAnimated ? p.IsRenaming ? "RA" : "A" : "";
                states[i].EnableInClassList("thry-uv-ra", p.IsRenaming);
                states[i].style.display = string.IsNullOrEmpty(states[i].text) ? DisplayStyle.None : DisplayStyle.Flex;
                cells[i].EnableInClassList("thry-uv-discarded", discarded && !mixed);
                cells[i].EnableInClassList("thry-uv-mixed", mixed || p.HasMixedAnimatedOwners);
                cells[i].SetEnabled(model.CanEdit(p));
                cells[i].tooltip = "Toggle discard" + " · " + i % 4 + ", " + i / 4 + "\n" + string.Join(" / ", labels.Where(label => !string.IsNullOrEmpty(label))) + "\n" + (mixed ? "Mixed discard values" : discarded ? "Discarded" : "Visible") + "\n" + p.MaterialProperty.name + "\n" + p.AnimatedOwnersTooltip;
            }
        }
    }
}
