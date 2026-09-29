using System;
using System.Globalization;
using Thry.ThryEditor.Helpers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// A color swatch built from elements: the color as it renders, an alpha bar, an HDR intensity
    /// badge and an eyedropper button. Its owner decides what opening and picking do.
    /// </summary>
    internal sealed class ThryColorField : BaseField<Color>
    {
        internal const string UssClass = "thry-color-field";
        // Below this width the badge would cover most of the color; the tooltip still names it.
        const float BadgeMinWidth = 40f;
        const float BadgeMinIntensity = .05f;

        readonly VisualElement _swatch, _alpha, _alphaFill;
        readonly Label _intensity, _mixed;
        readonly Button _eyeDropper;
        ThryColorFormat _format = ThryColorFormat.Ldr();
        bool _showEyeDropper = true, _narrow;
        Color? _previewOverride;

        internal event Action<ThryColorField> OpenRequested;
        internal event Action<ThryColorField> EyeDropperRequested;

        internal ThryColorField(string label = null) : this(label, new VisualElement()) { }

        ThryColorField(string label, VisualElement input) : base(label, input)
        {
            AddToClassList(UssClass);
            // BaseField hands focus to its first focusable child with a tab index.
            input.AddToClassList(UssClass + "__input");
            input.focusable = true; input.tabIndex = 0;

            _swatch = new VisualElement { name = "thry-color-swatch" };
            _swatch.AddToClassList(UssClass + "__swatch");
            input.Add(_swatch);
            _alpha = new VisualElement { pickingMode = PickingMode.Ignore };
            _alpha.AddToClassList(UssClass + "__alpha");
            _alphaFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _alphaFill.AddToClassList(UssClass + "__alpha-fill");
            _alpha.Add(_alphaFill); _swatch.Add(_alpha);
            _intensity = new Label { pickingMode = PickingMode.Ignore };
            _intensity.AddToClassList(UssClass + "__intensity");
            _swatch.Add(_intensity);
            _mixed = new Label("—") { pickingMode = PickingMode.Ignore };
            _mixed.AddToClassList(UssClass + "__mixed");
            _swatch.Add(_mixed);

            // A mouse tool; keyboard users open the picker, which has its own eyedropper.
            _eyeDropper = new Button(() => { if (CanRequest()) EyeDropperRequested?.Invoke(this); }) { focusable = false };
            _eyeDropper.AddToClassList("thry-icon-button");
            _eyeDropper.AddToClassList(UssClass + "__eyedropper");
            _eyeDropper.tooltip = RetainedText.Get("color_picker_eyedropper", "Pick a color from the screen.");
            _eyeDropper.Add(new Image { image = ThryEyeDropper.Icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
            input.Add(_eyeDropper);

            _swatch.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !enabledInHierarchy) return;
                OpenRequested?.Invoke(this);
                // The swatch cannot take focus; its own pointer-down focus step would drop the focus Open gave the field.
#if UNITY_6000_0_OR_NEWER
                focusController?.IgnoreEvent(e);
#else
                e.PreventDefault();
#endif
                e.StopPropagation();
            });
            // Enter and Space send both KeyDown and NavigationSubmit; handling one opens the picker once.
            RegisterCallback<NavigationSubmitEvent>(e =>
            {
                if (!CanRequest()) return;
                e.PreventDefault(); e.StopPropagation();
                OpenRequested?.Invoke(this);
            });
            _swatch.RegisterCallback<GeometryChangedEvent>(e =>
            {
                bool narrow = e.newRect.width < BadgeMinWidth;
                if (narrow == _narrow) return;
                _narrow = narrow; Refresh();
            });
            _swatch.RegisterCallback<TooltipEvent>(e =>
            {
                e.tooltip = Tooltip(); e.rect = _swatch.worldBound; e.StopImmediatePropagation();
            });
            Refresh();
        }

        internal ThryColorFormat Format
        {
            get => _format;
            set { _format = value; Refresh(); }
        }

        internal bool ShowEyeDropper
        {
            get => _showEyeDropper;
            set { _showEyeDropper = value; Refresh(); }
        }

        /// <summary>A displayed color painted instead of the value, e.g. under the eyedropper. Visual only.</summary>
        internal Color? PreviewOverride
        {
            get => _previewOverride;
            set { _previewOverride = value; Refresh(); }
        }

        internal VisualElement Swatch => _swatch;

        public override void SetValueWithoutNotify(Color newValue)
        {
            base.SetValueWithoutNotify(newValue);
            Refresh();
        }

        // The base implementation throws; this field draws its own mixed state.
        protected override void UpdateMixedValueContent()
        {
            EnableInClassList(UssClass + "--mixed", showMixedValue);
            Refresh();
        }

        bool CanRequest() => enabledInHierarchy && !RetainedColorPicker.IsEditing(this);

        Color PreviewColor => _previewOverride.HasValue ? ThryColorMath.Clamp01(_previewOverride.Value) : ThryColorMath.Preview(value, _format);

        float BadgeIntensity => _format.Hdr ? ThryColorMath.Intensity(value, _format) : 0f;

        void Refresh()
        {
            if (_swatch == null) return;
            bool mixed = showMixedValue;
            var preview = PreviewColor;
            var fill = preview; fill.a = 1;
            _swatch.style.backgroundColor = mixed ? new StyleColor(StyleKeyword.Null) : new StyleColor(fill);
            _mixed.style.display = Shown(mixed);
            bool alpha = _format.Alpha && !mixed;
            _alpha.style.display = Shown(alpha);
            if (alpha) _alphaFill.style.width = Length.Percent(Mathf.Clamp01(ThryColorMath.IsFinite(value.a) ? value.a : 1f) * 100f);
            float intensity = mixed ? 0f : BadgeIntensity;
            bool badge = intensity >= BadgeMinIntensity && !_narrow;
            _intensity.style.display = Shown(badge);
            if (badge)
            {
                _intensity.text = "+" + intensity.ToString("0.0", CultureInfo.InvariantCulture);
                _intensity.style.color = ThryColorMath.Luminance(fill) > .5f ? Color.black : Color.white;
            }
            _eyeDropper.style.display = Shown(_showEyeDropper && ThryEyeDropper.Available);
        }

        // Visible parts leave display to the theme, which hides the eyedropper in pathing cells and disabled rows.
        static StyleEnum<DisplayStyle> Shown(bool visible) => visible ? new StyleEnum<DisplayStyle>(StyleKeyword.Null) : DisplayStyle.None;

        string Tooltip()
        {
            if (showMixedValue) return RetainedText.Get("color_field_mixed", "The selected materials have different colors.");
            var raw = value;
            string text = "#" + ThryColorMath.ToHex(ThryColorMath.Preview(raw, _format));
            int alpha = Mathf.RoundToInt(Mathf.Clamp01(ThryColorMath.IsFinite(raw.a) ? raw.a : 1f) * 100f);
            if (_format.Alpha && alpha < 100) text += " · " + RetainedText.Get("color_field_alpha", "Alpha") + " " + alpha + "%";
            float intensity = BadgeIntensity;
            if (intensity >= BadgeMinIntensity)
                text += " · " + RetainedText.Get("color_field_intensity", "Intensity") + " +" + intensity.ToString("0.0", CultureInfo.InvariantCulture);
            return text;
        }
    }
}
