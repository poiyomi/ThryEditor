#if UNITY_2021_3_OR_NEWER
using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal static class RetainedMultiMaterial
    {
        internal static VisualElement SelectionSummary(RetainedMaterialModel model)
        {
            var root = new VisualElement { name = "thry-material-selection" };
            if (model.Shader.Materials.Length < 2) { root.style.display = DisplayStyle.None; return root; }
            var sheet = Resources.Load<StyleSheet>("ThryMultiMaterial"); if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("thry-multi-selection");
            var materials = model.Shader.Materials;
            var foldout = new Foldout { text = "Editing " + materials.Length + " materials", value = false, name = "thry-selected-materials" }; root.Add(foldout);
            var hint = new Label("Edits apply to all selected materials unless a field shows a smaller count."); hint.AddToClassList("thry-multi-hint"); foldout.Add(hint);
            foreach (var material in materials)
            {
                var row = new VisualElement { name = "selected-material-" + material.GetInstanceID() }; row.AddToClassList("thry-multi-material-row"); foldout.Add(row);
                var name = new Button(() => EditorGUIUtility.PingObject(material)) { text = MaterialName(material), tooltip = AssetDatabase.GetAssetPath(material) };
                name.AddToClassList("thry-multi-material-name"); row.Add(name);
                var shader = new Label(material.shader != null ? material.shader.name : "Missing shader"); shader.AddToClassList("thry-multi-shader-name"); row.Add(shader);
            }
            return root;
        }

        internal static string MaterialName(Material material) => string.IsNullOrEmpty(material.name) ? "Unnamed material" : material.name;

        internal static object Value(Material material, MaterialProperty property)
        {
            if (property.GetPropertyType() == UnityEngine.Rendering.ShaderPropertyType.Int) return material.GetInteger(property.name);
            switch (property.type)
            {
                case MaterialProperty.PropType.Color: return material.GetColor(property.name);
                case MaterialProperty.PropType.Vector: return material.GetVector(property.name);
                case MaterialProperty.PropType.Texture:
                    return new TextureValue { Texture = material.GetTexture(property.name), Scale = material.GetTextureScale(property.name), Offset = material.GetTextureOffset(property.name) };
                default: return material.GetFloat(property.name);
            }
        }
        private struct TextureValue
        {
            public Texture Texture;
            public Vector2 Scale, Offset;
            public override string ToString() => (Texture == null ? "None" : Texture.name) + " · tiling " + Scale.ToString("G3") + " · offset " + Offset.ToString("G3");
        }
        internal static string DisplayValue(object value)
        {
            if (value is float) return ((float)value).ToString("G4", CultureInfo.InvariantCulture);
            if (value is Vector4) return ((Vector4)value).ToString("G3");
            if (value is Color) return ((Color)value).ToString("G3");
            return value.ToString();
        }
    }

    internal sealed partial class RetainedFields
    {
        internal void DecorateMultiMaterialProperty(VisualElement root, ShaderProperty property)
        {
            if (Model.Shader.Materials.Length < 2) return;
            var sheet = Resources.Load<StyleSheet>("ThryMultiMaterial"); if (sheet != null) root.styleSheets.Add(sheet);
            VisualElement badge = null; VisualElement caption = null;
            StyleLength originalPadding = default(StyleLength);
            bool badgeVisible = false;
            Action positionBadge = () => {
                if (badge == null || caption == null) return;
                // TextElement renders its own text only while it has no children. Keep the
                // badge beside the caption, positioned within its existing label column.
                const float width = 16;
                badge.style.width = width;
                badge.style.left = caption.layout.x + caption.layout.width - width - 4;
                badge.style.top = Mathf.Max(0, caption.layout.y + (caption.layout.height - 16) * .5f);
                float changedIndicatorSpace = caption.ClassListContains("thry-caption-has-changed-indicator") ? 10 : 0;
                caption.style.paddingRight = badgeVisible ? new StyleLength(width + 8 + changedIndicatorSpace) : originalPadding;
            };
            Func<Material[]> affected = () => property.MaterialProperty.targets.OfType<Material>()
                .Where(m => Model.Shader.Materials.Contains(m) && m.HasProperty(property.MaterialProperty.name)).Distinct().ToArray();
            Track(root, () => {
                // Fields and texture labels are created after decorators. Attach to their own
                // caption on the next refresh; never insert rows into component/value layouts.
                if (caption == null)
                {
                    var row = root.Children().FirstOrDefault(c => c.ClassListContains("thry-property-row") && !c.ClassListContains("thry-inline"));
                    if (row == null) return;
                    caption = row.Q(className: "thry-property-label"); if (caption == null) return;
                    originalPadding = caption.style.paddingRight;
                    badge = new VisualElement { name = "affected-materials-" + property.MaterialProperty.name, focusable = false };
                    badge.AddToClassList("thry-multi-badge"); row.Add(badge);
                    badge.generateVisualContent += context =>
                    {
                        var painter = context.painter2D;
                        painter.lineWidth = 1.4f; painter.strokeColor = badge.resolvedStyle.color;
                        // Two overlapping swatches, with no text or button background.
                        painter.BeginPath(); painter.MoveTo(new Vector2(5, 3)); painter.LineTo(new Vector2(12, 3));
                        painter.LineTo(new Vector2(12, 10)); painter.Stroke();
                        painter.BeginPath(); painter.MoveTo(new Vector2(2, 6)); painter.LineTo(new Vector2(9, 6));
                        painter.LineTo(new Vector2(9, 13)); painter.LineTo(new Vector2(2, 13)); painter.ClosePath(); painter.Stroke();
                    };
                    row.RegisterCallback<GeometryChangedEvent>(e => positionBadge());
                    caption.RegisterCallback<GeometryChangedEvent>(e => positionBadge());
                    badge.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    badge.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                }
                var targetsNow = affected(); int total = Model.Shader.Materials.Length;
                bool mixed = targetsNow.Select(m => RetainedMultiMaterial.Value(m, property.MaterialProperty)).Distinct().Skip(1).Any();
                bool partial = targetsNow.Length != total;
                badgeVisible = mixed || partial;
                badge.style.display = badgeVisible ? DisplayStyle.Flex : DisplayStyle.None;
                positionBadge();
                bool editableNow = Model.CanEdit(property) && root.enabledInHierarchy;
                var details = (editableNow ? "Edits affect " : "Read only · property exists on ") + targetsNow.Length + " of " + total + " selected materials."
                    + (editableNow ? "" : " Editing is disabled for this selection.")
                    + (mixed ? " Different values are selected." : "") + "\n"
                    + string.Join("\n", Model.Shader.Materials.Select(m => RetainedMultiMaterial.MaterialName(m) + ": "
                        + (targetsNow.Contains(m) ? RetainedMultiMaterial.DisplayValue(RetainedMultiMaterial.Value(m, property.MaterialProperty)) : "property not available")));
                badge.tooltip = details;
                // Labels also refresh their ordinary tooltip later in the property update list;
                // the badge remains the dedicated, stable target for material details.
            });
        }
    }
}
#endif
