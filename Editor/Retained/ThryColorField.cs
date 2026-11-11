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

        readonly VisualElement _swatch, _alpha, _alphaFill, _lock;
        readonly Label _intensity, _mixed;
        readonly Button _eyeDropper;
        ThryColorFormat _format = ThryColorFormat.Ldr();
        bool _showEyeDropper = true, _narrow, _locked;
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
            // Drawn rather than an image so its color can follow the swatch's contrast.
            _lock = new VisualElement { pickingMode = PickingMode.Ignore };
            _lock.AddToClassList(UssClass + "__lock");
            _lock.generateVisualContent += DrawLock;
            _swatch.Add(_lock);

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
                e.PreventDefaultHandling(); e.StopPropagation();
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

        internal bool Locked
        {
            get => _locked;
            set { if (_locked == value) return; _locked = value; Refresh(); }
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
            var contrast = ThryColorMath.Luminance(fill) > .5f ? Color.black : Color.white;
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
                _intensity.style.color = contrast;
            }
            _lock.style.display = Shown(_locked);
            if (_locked)
            {
                _lock.style.color = mixed ? new StyleColor(StyleKeyword.Null) : new StyleColor(contrast);
                _lock.MarkDirtyRepaint();
            }
            _eyeDropper.style.display = Shown(_showEyeDropper && ThryEyeDropper.Available);
        }

        void DrawLock(MeshGenerationContext context)
        {
            var rect = _lock.contentRect;
            float scale = Mathf.Min(rect.width, rect.height) / 12f;
            if (scale <= 0) return;
            var origin = rect.center - new Vector2(6, 6) * scale;
            Vector2 At(float x, float y) => origin + new Vector2(x, y) * scale;
            var painter = context.painter2D;
            var color = _lock.resolvedStyle.color;
            painter.lineWidth = 1.5f * scale;
            painter.lineCap = LineCap.Butt;
            painter.strokeColor = color;
            painter.BeginPath();
            painter.MoveTo(At(3.5f, 6f));
            painter.LineTo(At(3.5f, 4.5f));
            painter.Arc(At(6f, 4.5f), 2.5f * scale, 180, 360);
            painter.LineTo(At(8.5f, 6f));
            painter.Stroke();
            float radius = 1.2f * scale;
            Vector2 min = At(2f, 5.5f), max = At(10f, 11f);
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(new Vector2(min.x + radius, min.y + radius), radius, 180, 270);
            painter.Arc(new Vector2(max.x - radius, min.y + radius), radius, 270, 360);
            painter.Arc(new Vector2(max.x - radius, max.y - radius), radius, 0, 90);
            painter.Arc(new Vector2(min.x + radius, max.y - radius), radius, 90, 180);
            painter.ClosePath();
            painter.Fill();
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
            if (_locked) text += "\n" + RetainedText.Get("color_field_locked", "Locked: Not marked as Animated. Tag this prop as either A/RA in order to animate this property when Locked.");
            return text;
        }
    }
}
