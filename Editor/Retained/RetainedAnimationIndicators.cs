#if UNITY_2021_3_OR_NEWER
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        private void DecorateAnimation(VisualElement root, ShaderProperty property, bool inline)
        {
            Label caption = null, indicator = null;
            VisualElement row = null, labelContainer = null;
            StyleColor originalColor = default(StyleColor);
            StyleLength originalPadding = default(StyleLength);
            bool texture = property is ShaderTextureProperty;
            Track(root, () => {
                if (indicator == null)
                {
                    row = root.Children().FirstOrDefault(e => e.ClassListContains("thry-property-row"));
                    if (row == null) return;
                    labelContainer = row.Q(className: "thry-property-label");
                    if (labelContainer == null) return;
                    caption = texture ? labelContainer.Q<Label>(className: "thry-texture-caption") : labelContainer as Label;
                    if (caption == null) return;
                    originalColor = caption.style.color;
                    originalPadding = labelContainer.style.paddingLeft;
                    indicator = new Label { name = "animation-indicator-" + property.MaterialProperty.name };
                    indicator.AddToClassList("thry-animation-letter");
                    if (texture) labelContainer.Insert(1, indicator);
                    else
                    {
                        indicator.style.position = Position.Absolute;
                        row.Add(indicator);
                    }
                }
                bool animated = property.IsAnimated;
                indicator.style.display = animated ? DisplayStyle.Flex : DisplayStyle.None;
                indicator.text = property.HasMixedAnimatedOwners ? "A/RA" : property.IsRenaming ? "RA" : "A";
                if (property.HasMixedAnimatedOwners && !property.HasPlainAnimatedOwners) indicator.text = "RA";
                if (property.HasMixedAnimatedOwners && !property.HasRenamedAnimatedOwners) indicator.text = "A";
                indicator.tooltip = property.AnimatedOwnersTooltip;
                indicator.style.width = indicator.text == "A/RA" ? 32 : 18;
                var color = property.IsRenaming ? Styles.AnimatedRenamedColor : Styles.AnimatedColor;
                indicator.style.color = color;
                caption.style.color = animated ? new StyleColor(color) : originalColor;
                if (texture) return;
                if (inline)
                {
                    // Inline reference values have no caption; reserve a small marker slot
                    // inside their own row, leaving the parent property's column intact.
                    row.style.paddingLeft = animated ? (indicator.text == "A/RA" ? 34 : 20) : 0;
                    indicator.style.left = 0;
                }
                else
                {
                    labelContainer.style.paddingLeft = animated ? new StyleLength(indicator.text == "A/RA" ? 34 : 20) : originalPadding;
                    indicator.style.left = labelContainer.layout.x;
                }
                indicator.style.top = Mathf.Max(0, (row.layout.height - 16) * .5f);
            });
        }
    }
}
#endif
