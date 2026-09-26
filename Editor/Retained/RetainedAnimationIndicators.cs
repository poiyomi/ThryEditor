using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        private static bool HasMixedAnimation(ShaderProperty property) => property.HasPlainAnimatedOwners && property.HasRenamedAnimatedOwners;

        private static Label AnimationIndicator(ShaderProperty property, string suffix = "")
        {
            var indicator = new Label { name = "animation-indicator-" + property.MaterialProperty.name + suffix, enableRichText = true };
            indicator.AddToClassList("thry-animation-letter");
            return indicator;
        }

        private static bool UpdateAnimationIndicator(Label indicator, ShaderProperty property)
        {
            bool animated = property.IsAnimatable && property.IsAnimated;
            indicator.style.display = animated ? DisplayStyle.Flex : DisplayStyle.None;
            indicator.text = HasMixedAnimation(property)
                ? "<color=#" + ColorUtility.ToHtmlStringRGBA(Styles.AnimatedColor) + ">A</color>/<color=#"
                    + ColorUtility.ToHtmlStringRGBA(Styles.AnimatedRenamedColor) + ">RA</color>"
                : property.IsRenaming ? "RA" : "A";
            indicator.tooltip = property.AnimatedOwnersTooltip;
            indicator.style.width = HasMixedAnimation(property) ? 32 : 18;
            indicator.style.color = property.IsRenaming ? Styles.AnimatedRenamedColor : Styles.AnimatedColor;
            return animated;
        }

        internal void DecorateHeaderAnimation(VisualElement root, ShaderProperty property, Label caption)
        {
            var indicator = AnimationIndicator(property);
            root.Insert(0, indicator);
            var originalColor = caption != null ? caption.style.color : default(StyleColor);
            Track(root, () => {
                property.RefreshRetainedProjection(Model.Renderers);
                bool animated = UpdateAnimationIndicator(indicator, property);
                if (caption != null) caption.style.color = animated ? new StyleColor(indicator.style.color.value) : originalColor;
            });
        }

        internal void DecorateGroupAnimation(VisualElement caption, ShaderProperty[] properties)
        {
            var indicator = AnimationIndicator(properties[0], "-group");
            caption.Add(indicator);
            TrackVisible(caption, () => {
                bool plain = properties.Any(property => property.IsAnimatable && property.HasPlainAnimatedOwners);
                bool renamed = properties.Any(property => property.IsAnimatable && property.HasRenamedAnimatedOwners);
                indicator.style.display = plain || renamed ? DisplayStyle.Flex : DisplayStyle.None;
                indicator.text = plain && renamed ? "<color=#" + ColorUtility.ToHtmlStringRGBA(Styles.AnimatedColor)
                    + ">A</color>/<color=#" + ColorUtility.ToHtmlStringRGBA(Styles.AnimatedRenamedColor) + ">RA</color>"
                    : renamed ? "RA" : "A";
                indicator.style.width = plain && renamed ? 32 : 18;
                indicator.style.color = renamed ? Styles.AnimatedRenamedColor : Styles.AnimatedColor;
                indicator.tooltip = string.Join("\n", properties.Where(property => property.IsAnimated)
                    .Select(property => property.Content.text + ": " + property.AnimatedOwnersTooltip));
            });
        }

        private void DecorateTransformAnimation(VisualElement row, ShaderTextureProperty property, string transform)
        {
            var caption = row.Q<Label>(className: "thry-property-label");
            var indicator = AnimationIndicator(property, "-" + transform);
            indicator.style.position = Position.Absolute;
            row.Add(indicator);
            var originalColor = caption.style.color;
            var originalPadding = caption.style.paddingLeft;
            TrackVisible(row, () => {
                bool animated = UpdateAnimationIndicator(indicator, property);
                caption.style.color = animated ? new StyleColor(indicator.style.color.value) : originalColor;
                caption.style.paddingLeft = animated ? new StyleLength(HasMixedAnimation(property) ? 34 : 20) : originalPadding;
                indicator.style.left = caption.layout.x;
                indicator.style.top = Mathf.Max(0, (row.layout.height - 16) * .5f);
            });
        }

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
                    indicator = AnimationIndicator(property);
                    if (texture) labelContainer.Insert(1, indicator);
                    else
                    {
                        indicator.style.position = Position.Absolute;
                        row.Add(indicator);
                    }
                }
                bool animated = UpdateAnimationIndicator(indicator, property);
                caption.style.color = animated ? new StyleColor(indicator.style.color.value) : originalColor;
                if (texture) return;
                if (inline)
                {
                    // Inline reference values have no caption; reserve a small marker slot
                    // inside their own row, leaving the parent property's column intact.
                    row.style.paddingLeft = animated ? (HasMixedAnimation(property) ? 34 : 20) : 0;
                    indicator.style.left = 0;
                }
                else
                {
                    labelContainer.style.paddingLeft = animated ? new StyleLength(HasMixedAnimation(property) ? 34 : 20) : originalPadding;
                    indicator.style.left = labelContainer.layout.x;
                }
                indicator.style.top = Mathf.Max(0, (row.layout.height - 16) * .5f);
            });
        }
    }
}
