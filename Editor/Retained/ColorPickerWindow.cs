using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// The Thry color picker: ring and triangle, hex and sliders. One window edits one target for one
    /// session. Closing commits; Escape restores every owner and leaves no undo step.
    /// </summary>
    internal sealed class ThryColorPickerWindow : EditorWindow
    {
        // The width is fixed; the height is fitted to the content once it has been laid out.
        const float Width = 264;
        static readonly FieldInfo ParentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly PropertyInfo ContainerProperty = ParentField?.FieldType.GetProperty("window", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly MethodInfo FitMethod = ContainerProperty?.PropertyType.GetMethod("FitWindowRectToScreen",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(Rect), typeof(bool), typeof(bool) }, null);
        // Unity 6 replaced it with a static method that fits to the screen containing a given point.
        static readonly MethodInfo FitToScreenMethod = ContainerProperty == null ? null : ContainerProperty.PropertyType.GetMethod("FitRectToScreen",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(Rect), typeof(Vector2), typeof(bool), ContainerProperty.PropertyType }, null);

        /// <summary>Automation and tests: show as a utility window, which stays open without focus.</summary>
        internal static bool UseUtilityWindow;
        internal static ThryColorPickerWindow Current { get; private set; }
        internal IColorPickerTarget Target { get; private set; }
        internal ThryColorState State { get; private set; }

        // Session state never survives a domain reload: a reloaded window has no target and closes.
        [NonSerialized] bool _live, _ended, _cancelled, _invalid, _closeScheduled, _dirty, _forceMixed;
        [NonSerialized] bool _adoptPending, _placed, _placementChecked, _eyeDropping, _callbacks;
        [NonSerialized] Color _lastApplied;
        // _hexVersion: the State.Version the hex text reflects (it was written from it, or typed text applied as it).
        [NonSerialized] int _appliedEdits, _adoptEdits, _shownVersion = -1, _hexVersion = -1;
        [NonSerialized] double _nextValidityCheck, _adoptUntil;
        [NonSerialized] Rect _placement;
        // Right after the window is shown or moved by code it reports a stale position for a few frames, so it is only
        // read once it has settled; until then the position last set here is used.
        [NonSerialized] Vector2 _knownPosition;
        [NonSerialized] int _settleFrames = 10;
        [NonSerialized] bool _positionKnown;
        [NonSerialized] Vector2 _size;
        [NonSerialized] float _intensityRange = ThryColorMath.DefaultIntensityRange, _pendingHeight;

        [NonSerialized] VisualElement _wheelArea, _intensityRow, _controls, _rgbGroup, _hsvGroup, _hslGroup;
        [NonSerialized] ColorWheel _wheel;
        [NonSerialized] ColorPreviewSwatch _newSwatch, _originalSwatch;
        [NonSerialized] Button _eyeDropperButton, _triangleButton, _squareButton;
        [NonSerialized] DropdownField _modeField;
        [NonSerialized] Button _bytesButton, _floatsButton;
        [NonSerialized] Button _libraryButton;
        [NonSerialized] string _libraryPath = "";
        [NonSerialized] ColorSwatchGrid _swatchGrid;
        [NonSerialized] readonly List<UnitySwatchLibrary.Swatch> _swatches = new List<UnitySwatchLibrary.Swatch>();
        [NonSerialized] readonly List<Color> _swatchColors = new List<Color>();
        [NonSerialized] readonly List<bool> _swatchHdr = new List<bool>();
        [NonSerialized] readonly List<UnitySwatchLibrary.LibraryInfo> _libraries = new List<UnitySwatchLibrary.LibraryInfo>();
        [NonSerialized] TextField _hex;
        [NonSerialized] ColorChannelSlider _intensity, _alpha, _red, _green, _blue, _hsvHue, _hsvSaturation, _hsvValue, _hslHue, _hslSaturation, _hslLightness;

        /// <summary>Commits and closes any open picker, then opens one for <paramref name="target"/>.</summary>
        internal static ThryColorPickerWindow Show(IColorPickerTarget target)
        {
            if (target == null) return null;
            if (Current != null) Current.Close();
            var window = CreateInstance<ThryColorPickerWindow>();
            window._live = true;
            window.Target = target;
            window.State = new ThryColorState(target.Value, target.Format);
            string title = target.Title;
            window.titleContent = new GUIContent(!string.IsNullOrEmpty(title) ? title : target.Format.Hdr
                ? RetainedText.Get("color_picker_title_hdr", "HDR Color") : RetainedText.Get("color_picker_title", "Color"));
            window._size = new Vector2(Width, EstimatedHeight(target.Format));
            window.minSize = window.maxSize = window._size;
            window.position = new Rect(window.position.position, window._size);
            window.Resync();
            Current = window;
            try
            {
                target.Begin();
                EditorApplication.update += window.Tick;
                Undo.undoRedoPerformed += window.OnUndoRedo;
                Selection.selectionChanged += window.OnSelectionChanged;
                AssemblyReloadEvents.beforeAssemblyReload += window.OnBeforeAssemblyReload;
                if (UseUtilityWindow) window.ShowUtility(); else window.ShowAuxWindow();
            }
            catch
            {
                window._cancelled = true;
                if (window) DestroyImmediate(window);
                throw;
            }
            window.Place();
            return window;
        }

        void OnDestroy()
        {
            EndSession();
            Unsubscribe();
        }

        void Unsubscribe()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Selection.selectionChanged -= OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        void OnBeforeAssemblyReload() { if (!_ended) Close(); }
        void OnSelectionChanged() { if (_live && !_ended) Close(); }

        void OnUndoRedo()
        {
            // Inspectors refresh after this callback, so adopt the value on the next ticks, not here.
            if (_ended || State == null) return;
            _adoptPending = true; _adoptEdits = State.Edits;
            _adoptUntil = EditorApplication.timeSinceStartup + .3;
        }

        void ScheduleClose()
        {
            if (_closeScheduled) return;
            _closeScheduled = true;
            EditorApplication.delayCall += () => { if (this) Close(); };
        }

        void Tick()
        {
            if (_ended) return;
            if (!_live || Target == null || State == null) { ScheduleClose(); return; }
            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextValidityCheck)
            {
                _nextValidityCheck = now + .2;
                if (!Target.IsValid) { _invalid = true; Close(); return; }
            }
            if (_settleFrames > 0) _settleFrames--;
            else { _knownPosition = position.position; _positionKnown = true; }
            ApplyPendingHeight();
            if (_adoptPending) AdoptExternalChange(now);
            Flush();
            if (State.Version != _shownVersion) RefreshControls();
        }

        void AdoptExternalChange(double now)
        {
            // An edit after the undo wins; an IMGUI target may still report its old value for a moment.
            if (State.Edits != _adoptEdits) { _adoptPending = false; return; }
            if (Target.IsValid && !ThryColorMath.Same(Target.Value, State.Raw))
            {
                _adoptPending = false;
                State.Adopt(Target.Value);
                Resync();
            }
            else if (now > _adoptUntil)
            {
                _adoptPending = false;
                _forceMixed = Target.Mixed;
            }
        }

        void Resync()
        {
            _lastApplied = State.Raw; _appliedEdits = State.Edits; _forceMixed = Target.Mixed;
        }

        /// <summary>Writes pending edits. The only place that calls <see cref="IColorPickerTarget.Apply"/>.</summary>
        void Flush()
        {
            // A field losing focus while a cancelled or invalid picker closes must not write.
            if (_ended || _cancelled || _invalid) return;
            ApplyPending();
        }

        void ApplyPending()
        {
            if (State == null || Target == null || State.Edits == _appliedEdits) return;
            // The first edit of a mixed selection writes the whole color to every owner, even when it matches the first.
            bool apply = !ThryColorMath.Same(State.Raw, _lastApplied) || (_forceMixed && Target.Mixed);
            _lastApplied = State.Raw; _appliedEdits = State.Edits;
            if (!apply) return;
            _forceMixed = false; _dirty = true;
            Target.Apply(State.Raw);
        }

        void Revert()
        {
            if (_ended) return;
            if (_dirty) { Target.Cancel(); _dirty = false; }
            // A cancel through undo reports an undo; adopting it could bring back the value just reverted.
            _adoptPending = false;
            State.Revert();
            Resync();
            RefreshControls();
        }

        void CancelAndClose()
        {
            _cancelled = true;
            if (_dirty)
            {
                _dirty = false;
                try { Target.Cancel(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Close();
        }

        void EndSession()
        {
            if (!_live || _ended) return;
            _ended = true;
            Unsubscribe();
            try { if (!_cancelled && !_invalid) ApplyPending(); }
            catch (Exception e) { Debug.LogException(e); }
            try { Target?.End(_cancelled); }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (Current == this) Current = null;
                if (_eyeDropping) { _eyeDropping = false; ThryEyeDropper.Cancel(); }
                // Like Unity's picker on close: the next open reads the library from disk again.
                try { UnitySwatchLibrary.Unload(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ---------- Placement ----------

        void Place()
        {
            var swatch = Target.ScreenRect;
            if (swatch == Rect.zero) return; // Keep Unity's placement at the mouse.
            // Fit to the swatch's screen, even when the rect beside it crosses onto the next monitor.
            var left = Fit(new Rect(swatch.xMin - 6 - _size.x, swatch.yMin - 40, _size.x, _size.y), swatch.center);
            var rect = left;
            float leftOverlap = left.xMax - (swatch.xMin - 6);
            if (leftOverlap > .5f)
            {
                var right = Fit(new Rect(swatch.xMax + 6, swatch.yMin - 40, _size.x, _size.y), swatch.center);
                if (swatch.xMax + 6 - right.xMin < leftOverlap) rect = right;
            }
            _placement = rect; _placed = true;
            SetPosition(rect);
        }

        void SetPosition(Rect rect)
        {
            position = rect;
            _knownPosition = rect.position; _positionKnown = true; _settleFrames = 10;
        }

        // Runs once after the first build: Unity may apply its own placement after Show returns.
        void EnsurePlaced()
        {
            if (_ended || _placementChecked) return;
            _placementChecked = true;
            if (_placed) SetPosition(_placement);
        }

        Rect Fit(Rect rect, Vector2 screenPoint)
        {
            try
            {
                var parent = ParentField?.GetValue(this);
                var container = parent != null ? ContainerProperty?.GetValue(parent) : null;
                if (container != null && FitMethod != null) return (Rect)FitMethod.Invoke(container, new object[] { rect, true, false });
                if (container != null && FitToScreenMethod != null) return (Rect)FitToScreenMethod.Invoke(null, new object[] { rect, screenPoint, true, container });
            }
            catch (TargetInvocationException) { }
            var main = EditorGUIUtility.GetMainWindowPosition();
            rect.x = Mathf.Clamp(rect.x, main.xMin, Mathf.Max(main.xMin, main.xMax - rect.width));
            rect.y = Mathf.Clamp(rect.y, main.yMin, Mathf.Max(main.yMin, main.yMax - rect.height));
            return rect;
        }

        // ---------- Building ----------

        public void CreateGUI()
        {
            var root = rootVisualElement;
            if (!_live || Target == null || State == null) { ScheduleClose(); return; }
            root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-color-picker");
            // With nothing focused, keys and commands go to the panel's tree instead of this root.
            root.focusable = true; root.tabIndex = -1;
            var format = State.Format;
            var prefs = ColorPickerPreferences.Get();

            // Below the wheel the layout follows Unity's picker: mode, sliders, intensity, hex, swatches.
            BuildWheelArea(root, format);
            BuildModeRow(root, prefs);
            AddSection(root, BuildSliders(format, prefs));
            if (format.Hdr) BuildIntensity(root);
            BuildHex(root, format);
            BuildSwatches(root);
            ApplyMode(prefs.mode);
            ApplyUnits(prefs.floats);
            ApplyShape(prefs.square);
            RegisterRootCallbacks(root);
            _shownVersion = -1;
            RefreshControls();
            root.schedule.Execute(() =>
            {
                EnsurePlaced();
                if (!_ended && !FocusInside(_hex)) rootVisualElement.Focus();
            });
        }

        void BuildWheelArea(VisualElement root, ThryColorFormat format)
        {
            _wheelArea = new VisualElement(); _wheelArea.AddToClassList("thry-color-picker__wheel-area");
            _wheel = new ColorWheel(State, OnEdited, Flush);
            _wheelArea.Add(_wheel);

            var swatches = new VisualElement { pickingMode = PickingMode.Ignore };
            swatches.AddToClassList("thry-color-picker__swatches");
            _newSwatch = new ColorPreviewSwatch { tooltip = RetainedText.Get("color_picker_new", "New color.") };
            _newSwatch.AddToClassList("thry-color-picker__new");
            _originalSwatch = new ColorPreviewSwatch();
            _originalSwatch.AddToClassList("thry-color-picker__original");
            var original = ThryColorMath.Preview(State.Original, format); original.a = Mathf.Clamp01(State.Original.a);
            _originalSwatch.Set(original, format.Alpha);
            _originalSwatch.Mixed = Target.Mixed;
            _originalSwatch.tooltip = (Target.Mixed
                ? RetainedText.Get("color_picker_original_mixed", "The selected materials had different colors. Click to give each one its own color back.")
                : RetainedText.Get("color_picker_original", "Original color. Click to go back to it."))
                + "\n" + HexLine(original, format.Hdr ? ThryColorMath.Intensity(State.Original, format) : (float?)null);
            _originalSwatch.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                Revert(); e.StopPropagation();
            });
            swatches.Add(_newSwatch); swatches.Add(_originalSwatch);
            _wheelArea.Add(swatches);

            _eyeDropperButton = new Button(StartEyeDropper) { tooltip = RetainedText.Get("color_picker_eyedropper", "Pick a color from the screen."), focusable = false };
            _eyeDropperButton.AddToClassList("thry-icon-button"); _eyeDropperButton.AddToClassList("thry-color-picker__eyedropper");
            _eyeDropperButton.Add(new Image { image = ThryEyeDropper.Icon, pickingMode = PickingMode.Ignore });
            _eyeDropperButton.style.display = ThryEyeDropper.Available ? DisplayStyle.Flex : DisplayStyle.None;
            _wheelArea.Add(_eyeDropperButton);

            AddSection(root, _wheelArea);
        }

        void BuildIntensity(VisualElement root)
        {
            _intensityRow = new VisualElement(); _intensityRow.AddToClassList("thry-color-picker__intensity");
            string tooltip = RetainedText.Get("color_picker_intensity_tooltip", "Makes the color brighter. Each step of 1 doubles its values.");
            _intensityRange = Mathf.Clamp(Mathf.Max(ThryColorMath.DefaultIntensityRange, Mathf.Ceil(Mathf.Abs(State.Intensity))),
                ThryColorMath.DefaultIntensityRange, ThryColorMath.MaxIntensity);
            _intensity = Slider(_intensityRow, "", ColorSliderUnit.Stops, IntensitySample, 16, v => State.SetIntensity(v), 0, _intensityRange);
            // A sun in the letter column keeps this bar the same size as the channel bars above it.
            _intensity.SetCaptionIcon(new ColorPickerGlyph(ColorPickerGlyph.Kind.Sun));
            _intensityRow.tooltip = tooltip;
            AddSection(root, _intensityRow);
        }

        // Triangle or square on the left; on the right a 0-255 / 0-1 switch for every slider, and the color space.
        void BuildModeRow(VisualElement root, ColorPickerPreferences.Data prefs)
        {
            _controls = new VisualElement(); _controls.AddToClassList("thry-color-picker__controls");
            var shapes = new VisualElement(); shapes.AddToClassList("thry-color-picker__segments"); shapes.AddToClassList("thry-color-picker__shapes");
            _triangleButton = ShapeButton(shapes, false, RetainedText.Get("color_picker_shape_triangle", "Triangle"));
            _squareButton = ShapeButton(shapes, true, RetainedText.Get("color_picker_shape_square", "Square"));
            _controls.Add(shapes);
            var units = new VisualElement { tooltip = RetainedText.Get("color_picker_units_tooltip", "Shows whole numbers (0–255, degrees, percent) or 0–1.") };
            units.AddToClassList("thry-color-picker__segments"); units.AddToClassList("thry-color-picker__units");
            _bytesButton = UnitButton(units, RetainedText.Get("color_picker_units_bytes", "0–255"), false);
            _floatsButton = UnitButton(units, RetainedText.Get("color_picker_units_floats", "0–1"), true);
            _controls.Add(units);
            _modeField = Dropdown(new List<string> { "RGB", "HSV", "HSL" }, prefs.mode,
                RetainedText.Get("color_picker_mode_tooltip", "Which sliders to show."), mode =>
                {
                    var data = ColorPickerPreferences.Get(); data.mode = mode; ColorPickerPreferences.Save();
                    ApplyMode(mode);
                });
            AddSection(root, _controls);
        }

        Button UnitButton(VisualElement parent, string text, bool floats)
        {
            var button = new Button(() =>
            {
                if (_ended) return;
                var data = ColorPickerPreferences.Get(); data.floats = floats; ColorPickerPreferences.Save();
                ApplyUnits(floats);
            }) { text = text, focusable = false };
            button.AddToClassList("thry-color-picker__segment");
            button.AddToClassList(floats ? "thry-color-picker__segment--last" : "thry-color-picker__segment--first");
            parent.Add(button);
            return button;
        }

        Button ShapeButton(VisualElement row, bool square, string tooltip)
        {
            var button = new Button(() =>
            {
                if (_ended) return;
                var data = ColorPickerPreferences.Get(); data.square = square; ColorPickerPreferences.Save();
                ApplyShape(square);
            }) { tooltip = tooltip, focusable = false };
            button.AddToClassList("thry-color-picker__segment"); button.AddToClassList("thry-color-picker__shape");
            button.AddToClassList(square ? "thry-color-picker__segment--last" : "thry-color-picker__segment--first");
            button.Add(new ColorPickerGlyph(square ? ColorPickerGlyph.Kind.Square : ColorPickerGlyph.Kind.Triangle));
            row.Add(button);
            return button;
        }

        void ApplyShape(bool square)
        {
            _wheel.Square = square;
            _triangleButton.EnableInClassList("thry-selected", !square);
            _squareButton.EnableInClassList("thry-selected", square);
        }

        void BuildHex(VisualElement root, ThryColorFormat format)
        {
            var row = new VisualElement(); row.AddToClassList("thry-color-picker__hex-row");
            _hex = new TextField { maxLength = 11 };
            _hex.AddToClassList("thry-color-picker__hex");
            // A fixed '#' inside the box; the text itself is only the digits (a typed or pasted '#' is fine too).
            var input = _hex.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                var hash = new Label("#") { pickingMode = PickingMode.Ignore };
                hash.AddToClassList("thry-color-picker__hash");
                input.Insert(0, hash);
            }
            _hex.tooltip = format.Alpha
                ? format.Hdr ? RetainedText.Get("color_picker_hex_hdr_tooltip", "Hex code of the color before intensity. An 8-digit code also sets alpha.")
                    : RetainedText.Get("color_picker_hex_tooltip", "Hex code of the color. An 8-digit code also sets alpha.")
                : format.Hdr ? RetainedText.Get("color_picker_hex_hdr_noalpha_tooltip", "Hex code of the color before intensity.")
                    : RetainedText.Get("color_picker_hex_noalpha_tooltip", "Hex code of the color.");
            _hex.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                string text = e.newValue ?? "";
                // The box already shows a '#', so a pasted "#FF8800" drops its own.
                if (text.StartsWith("#")) { text = text.Substring(1); _hex.SetValueWithoutNotify(text); }
                // Full codes apply while typing; short forms wait for Enter or leaving the field.
                int digits = HexDigits(text);
                if ((digits == 6 || digits == 8) && !_ended && ThryColorMath.TryParseHex(text, out _, out _) && SetHexForAll(text))
                {
                    _hexVersion = State.Version; // The typed text is the color now; keep it and the caret.
                    OnEdited();
                }
            });
            _hex.RegisterCallback<FocusOutEvent>(e => CommitHex());
            _hex.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) CommitHex(true); }, TrickleDown.TrickleDown);
            row.Add(_hex);
            AddSection(root, row);
        }

        DropdownField Dropdown(List<string> choices, int index, string tooltip, Action<int> changed)
        {
            var field = new DropdownField(choices, Mathf.Clamp(index, 0, choices.Count - 1)) { tooltip = tooltip };
            field.AddToClassList("thry-color-picker__dropdown");
            field.RegisterValueChangedCallback(e => { e.StopPropagation(); if (!_ended && field.index >= 0) changed(field.index); });
            RetainedWindow.Dropdown(field);
            _controls.Add(field);
            return field;
        }

        VisualElement BuildSliders(ThryColorFormat format, ColorPickerPreferences.Data prefs)
        {
            var panel = new VisualElement(); panel.AddToClassList("thry-color-picker__panel");
            var rgbUnit = prefs.floats ? ColorSliderUnit.Floats : ColorSliderUnit.Bytes;
            _rgbGroup = Group(panel);
            _red = Slider(_rgbGroup, "R", rgbUnit, x => WithChannel(0, x), 2, v => State.SetDisplayChannel(0, v));
            _green = Slider(_rgbGroup, "G", rgbUnit, x => WithChannel(1, x), 2, v => State.SetDisplayChannel(1, v));
            _blue = Slider(_rgbGroup, "B", rgbUnit, x => WithChannel(2, x), 2, v => State.SetDisplayChannel(2, v));
            _hsvGroup = Group(panel);
            _hsvHue = Slider(_hsvGroup, "H", ColorSliderUnit.Degrees, h => ThryColorMath.HsvToRgb(new Vector3(h, 1, 1)), 36, v => SetHsv(0, v));
            _hsvSaturation = Slider(_hsvGroup, "S", ColorSliderUnit.Percent, s => ThryColorMath.HsvToRgb(new Vector3(State.Hsv.x, s, State.Hsv.z)), 16, v => SetHsv(1, v));
            _hsvValue = Slider(_hsvGroup, "V", ColorSliderUnit.Percent, v => ThryColorMath.HsvToRgb(new Vector3(State.Hsv.x, State.Hsv.y, v)), 16, v => SetHsv(2, v));
            _hslGroup = Group(panel);
            _hslHue = Slider(_hslGroup, "H", ColorSliderUnit.Degrees, h => ThryColorMath.HslToRgb(new Vector3(h, 1, .5f)), 36, v => SetHsl(0, v));
            _hslSaturation = Slider(_hslGroup, "S", ColorSliderUnit.Percent, s => ThryColorMath.HslToRgb(new Vector3(State.Hsl.x, s, State.Hsl.z)), 16, v => SetHsl(1, v));
            _hslLightness = Slider(_hslGroup, "L", ColorSliderUnit.Percent, l => ThryColorMath.HslToRgb(new Vector3(State.Hsl.x, State.Hsl.y, l)), 16, v => SetHsl(2, v));
            if (format.Alpha)
            {
                var alphaGroup = Group(panel);
                _alpha = Slider(alphaGroup, "A", rgbUnit, AlphaSample, 2, v => State.SetAlpha(v), checker: true);
            }
            return panel;
        }

        // Unity's own swatch library, so colors saved in either picker show up in both.
        void BuildSwatches(VisualElement root)
        {
            if (!UnitySwatchLibrary.Available) return;
            var section = new VisualElement(); section.AddToClassList("thry-color-picker__swatch-section");
            var header = new VisualElement(); header.AddToClassList("thry-color-picker__swatch-header");
            header.Add(new Label(RetainedText.Get("color_picker_swatches", "Swatches"))
            { tooltip = RetainedText.Get("color_picker_swatches_tooltip", "Saved colors. Unity's color picker shows the same swatches.") });
            if (UnitySwatchLibrary.LibrariesAvailable)
            {
                _libraryButton = new Button(ShowLibraryMenu) { text = "⋮", focusable = false,
                    tooltip = RetainedText.Get("color_picker_libraries_tooltip", "Choose which swatch library to show. Unity's color picker switches too.") };
                _libraryButton.AddToClassList("thry-icon-button"); _libraryButton.AddToClassList("thry-color-picker__library-menu");
                header.Add(_libraryButton);
            }
            section.Add(header);
            _swatchGrid = new ColorSwatchGrid(AddSwatch, RetainedText.Get("color_picker_add_swatch", "Save the current color as a swatch."));
            _swatchGrid.Clicked += ApplySwatch;
            _swatchGrid.ContextClicked += SwatchMenu;
            _swatchGrid.Tooltip = SwatchTooltip;
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("thry-color-picker__swatch-scroll");
            scroll.Add(_swatchGrid);
            section.Add(scroll);
            AddSection(root, section);
            ReadSwatches();
        }

        void ShowLibraryMenu()
        {
            if (_ended || _libraryButton == null || !UnitySwatchLibrary.ListLibraries(_libraries) || _libraries.Count == 0) return;
            var names = LibraryNames();
            var items = new RetainedMenu.Item[_libraries.Count];
            for (int i = 0; i < _libraries.Count; i++)
            {
                string path = _libraries[i].Path;
                items[i] = new RetainedMenu.Item
                {
                    Text = names[i], Checked = path == _libraryPath + ".colors",
                    Action = () => { if (_ended) return; UnitySwatchLibrary.SetCurrentLibrary(path); ReadSwatches(); }
                };
            }
            RetainedMenu.Open(_libraryButton.worldBound, _swatchGrid, items);
        }

        // Names as Unity's menu shows them; a name used twice also gets its folder, so each entry picks its own library.
        List<string> LibraryNames()
        {
            var names = new List<string>(_libraries.Count);
            string project = RetainedText.Get("color_picker_library_project", "Project");
            for (int i = 0; i < _libraries.Count; i++)
            {
                var library = _libraries[i];
                string name = library.InProject ? library.Name + " (" + project + ")" : library.Name;
                bool repeated = false;
                for (int j = 0; j < _libraries.Count && !repeated; j++)
                    repeated = j != i && _libraries[j].Name == library.Name && _libraries[j].InProject == library.InProject;
                if (repeated) name += " – " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(library.Path));
                while (names.Contains(name)) name += " ";
                names.Add(name);
            }
            return names;
        }

        void ReadSwatches()
        {
            if (_swatchGrid == null) return;
            // Unity falls back to its Default library when the chosen file is missing; show the one actually read.
            UnitySwatchLibrary.Read(_swatches, out _libraryPath);
            _swatchColors.Clear(); _swatchHdr.Clear();
            for (int i = 0; i < _swatches.Count; i++)
            {
                var raw = UnitySwatchLibrary.ForPicker(_swatches[i].Color, State.Format.Hdr);
                _swatchColors.Add(SwatchDisplay(raw));
                _swatchHdr.Add(ThryColorMath.Intensity(raw, State.Format) > 0);
            }
            _swatchGrid.SetColors(_swatchColors, _swatchHdr);
            _swatchGrid.CanAdd = UnitySwatchLibrary.CurrentLibraryEditable;
        }

        // What the swatch would look like on this color: Unity's click rules, then this property's display.
        Color SwatchDisplay(Color raw)
        {
            var display = ThryColorMath.Preview(raw, State.Format);
            display.a = State.Format.Alpha ? Mathf.Clamp01(raw.a) : 1;
            return display;
        }

        // Six digits, or eight when alpha shows and is not full, so a copied code keeps the alpha.
        string HexText() => ThryColorMath.ToHex(State.Display, State.Format.Alpha && ThryColorMath.ToByte(State.Alpha) < 255);

        void ApplySwatch(int index)
        {
            if (_ended || index < 0 || index >= _swatches.Count) return;
            State.SetRaw(UnitySwatchLibrary.ForPicker(_swatches[index].Color, State.Format.Hdr), State.Format.Alpha);
            OnEdited(); Flush();
        }

        void AddSwatch()
        {
            if (_ended) return;
            // Unity stores the value its picker is editing, so either picker can apply it again as is.
            UnitySwatchLibrary.Add(State.Raw);
            ReadSwatches();
        }

        void SwatchMenu(int index, Vector2 point)
        {
            if (_ended || index < 0 || index >= _swatches.Count || !UnitySwatchLibrary.CurrentLibraryEditable) return;
            RetainedMenu.OpenContext(point, _swatchGrid, new[]
            {
                new RetainedMenu.Item { Text = RetainedText.Get("color_picker_swatch_replace", "Replace with current color"),
                    Action = () => { if (!_ended) { UnitySwatchLibrary.Replace(index, State.Raw); ReadSwatches(); } } },
                new RetainedMenu.Item { Text = RetainedText.Get("color_picker_swatch_move_first", "Move to first"),
                    Action = () => { if (!_ended) { UnitySwatchLibrary.Move(index, 0, false); ReadSwatches(); } } },
                new RetainedMenu.Item { Text = RetainedText.Get("color_picker_swatch_delete", "Delete"),
                    Action = () => { if (!_ended) { UnitySwatchLibrary.Remove(index); ReadSwatches(); } } },
            });
        }

        string SwatchTooltip(int index)
        {
            if (index < 0 || index >= _swatches.Count) return null;
            var raw = UnitySwatchLibrary.ForPicker(_swatches[index].Color, State.Format.Hdr);
            string line = HexLine(ThryColorMath.Preview(raw, State.Format), State.Format.Hdr ? ThryColorMath.Intensity(raw, State.Format) : (float?)null);
            if (State.Format.Alpha && raw.a < 1) line += " · " + RetainedText.Get("color_field_alpha", "Alpha") + " " + Mathf.RoundToInt(Mathf.Clamp01(raw.a) * 100) + "%";
            string name = _swatches[index].Name;
            return string.IsNullOrEmpty(name) ? line : name + "\n" + line;
        }

        // The window never resizes by itself, so any section changing height (a new swatch row) refits it.
        void AddSection(VisualElement root, VisualElement section)
        {
            section.RegisterCallback<GeometryChangedEvent>(e => FitToContent());
            root.Add(section);
        }

        static VisualElement Group(VisualElement panel)
        {
            var group = new VisualElement(); group.AddToClassList("thry-color-picker__group");
            panel.Add(group);
            return group;
        }

        ColorChannelSlider Slider(VisualElement parent, string caption, ColorSliderUnit unit, Func<float, Color> sample, int segments,
            Action<float> write, float min = 0, float max = 1, bool checker = false)
        {
            var slider = new ColorChannelSlider(caption, unit, min, max, sample, segments, checker);
            slider.Changed += v => { if (_ended) return; write(v); OnEdited(); };
            slider.Committed += Flush;
            parent.Add(slider);
            return slider;
        }

        void RegisterRootCallbacks(VisualElement root)
        {
            // CreateGUI can run again on the same root; register once.
            if (_callbacks) return;
            _callbacks = true;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<ValidateCommandEvent>(e =>
            {
                if (IsClipboardCommand(e.commandName) && !_ended && !InTextInput(root.focusController?.focusedElement as VisualElement)
                    && (e.commandName != "Paste" || IsColorCode(EditorGUIUtility.systemCopyBuffer))) e.StopPropagation();
            });
            root.RegisterCallback<ExecuteCommandEvent>(OnCommand);
            root.RegisterCallback<GeometryChangedEvent>(e => FitToContent());
            // A click on anything that cannot take focus (buttons, the original swatch) blurs the
            // focused element, and with nothing focused keys and commands never reach this root.
            root.RegisterCallback<FocusOutEvent>(e => { if (e.relatedTarget == null) root.schedule.Execute(RestoreFocus); }, TrickleDown.TrickleDown);
        }

        void RestoreFocus()
        {
            var root = rootVisualElement;
            // The eyedropper's callbacks focus the root when its pick ends.
            if (!_ended && !_eyeDropping && hasFocus && root.panel != null && root.focusController?.focusedElement == null && root.Q<RetainedMenu>() == null) root.Focus();
        }

        // ---------- Input ----------

        void OnKeyDown(KeyDownEvent e)
        {
            var root = rootVisualElement;
            var target = e.target as VisualElement;
            // An open dropdown or context menu handles its own Escape and Enter.
            if (root.Q<RetainedMenu>() != null || target?.GetFirstAncestorOfType<RetainedMenu>() != null) return;
            if (e.isDefaultPrevented || _ended) return;
            if (e.keyCode == KeyCode.Escape)
            {
                if (ThryEyeDropper.IsActive) ThryEyeDropper.Cancel();
                else CancelAndClose();
                e.PreventDefault(); e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (Within<Button>(target) || Within<Toggle>(target) || Within<DropdownField>(target)) return;
                if (InTextInput(target))
                {
                    // The field commits on this Enter; give focus back so the next Enter closes the picker.
                    root.schedule.Execute(() => { if (!_ended && InTextInput(root.focusController?.focusedElement as VisualElement)) root.Focus(); });
                    return;
                }
                e.PreventDefault(); e.StopPropagation();
                Close();
            }
        }

        static bool IsClipboardCommand(string name) => name == "Copy" || name == "Paste";

        static bool IsColorCode(string text) => ThryColorMath.IsPastedHex(text);

        void OnCommand(ExecuteCommandEvent e)
        {
            if (!IsClipboardCommand(e.commandName) || _ended || InTextInput(rootVisualElement.focusController?.focusedElement as VisualElement)) return;
            if (e.commandName == "Copy")
                EditorGUIUtility.systemCopyBuffer = "#" + ThryColorMath.ToHex(State.Display, State.Format.Alpha && State.Alpha < 1);
            else
            {
                string text = EditorGUIUtility.systemCopyBuffer;
                if (!IsColorCode(text)) return;
                if (SetHexForAll(text)) { OnEdited(); Flush(); }
            }
            e.StopPropagation();
        }

        /// <param name="enter">Enter was pressed; only focus leaving the field never writes an unchanged code.</param>
        void CommitHex(bool enter = false)
        {
            if (_ended || _cancelled || _hex == null) return;
            // Focus leaves the field after the click that moved it has already changed the color (a cell,
            // the original circle, the wheel), or after an undo. Text from before that change must not win.
            if (State.Version == _hexVersion && ThryColorMath.TryParseHex(_hex.value, out _, out _) && (enter ? SetHexForAll(_hex.value) : State.SetHex(_hex.value))) OnEdited();
            if (!FocusInside(_hex) || _hex.value != HexText()) _hex.SetValueWithoutNotify(HexText());
            _hexVersion = State.Version;
            Flush();
        }

        // A typed or pasted code that matches the first owner still reaches every owner of a mixed selection.
        bool SetHexForAll(string text)
        {
            int edits = State.Edits;
            if (!State.SetHex(text)) return false;
            if (State.Edits == edits && Target != null && Target.Mixed) State.SetRaw(State.Raw, true);
            return true;
        }

        void StartEyeDropper()
        {
            if (_ended || _eyeDropping || !ThryEyeDropper.Available) return;
            _eyeDropping = true;
            ThryEyeDropper.Begin(_eyeDropperButton,
                color => { if (!_ended) _newSwatch.Preview = color; },
                color =>
                {
                    _eyeDropping = false;
                    if (_ended) return;
                    _newSwatch.Preview = null;
                    State.SetPicked(color);
                    OnEdited(); Flush();
                    rootVisualElement.Focus();
                },
                () =>
                {
                    _eyeDropping = false;
                    if (_ended) return;
                    _newSwatch.Preview = null;
                    rootVisualElement.Focus();
                });
        }

        void SetHsv(int component, float value)
        {
            var hsv = State.Hsv; hsv[component] = value; State.SetHsv(hsv);
        }

        void SetHsl(int component, float value)
        {
            var hsl = State.Hsl; hsl[component] = value; State.SetHsl(hsl);
        }

        void ApplyMode(int mode)
        {
            mode = Mathf.Clamp(mode, 0, 2);
            _rgbGroup.style.display = mode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _hsvGroup.style.display = mode == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _hslGroup.style.display = mode == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_modeField.index != mode) _modeField.SetValueWithoutNotify(_modeField.choices[mode]);
        }

        void ApplyUnits(bool floats)
        {
            var unit = floats ? ColorSliderUnit.Floats : ColorSliderUnit.Bytes;
            _red.Unit = unit; _green.Unit = unit; _blue.Unit = unit;
            if (_alpha != null) _alpha.Unit = unit;
            // 0–255 is the whole-number view: degrees for hue, percent for the rest.
            var hue = floats ? ColorSliderUnit.Floats : ColorSliderUnit.Degrees;
            var share = floats ? ColorSliderUnit.Floats : ColorSliderUnit.Percent;
            _hsvHue.Unit = hue; _hsvSaturation.Unit = share; _hsvValue.Unit = share;
            _hslHue.Unit = hue; _hslSaturation.Unit = share; _hslLightness.Unit = share;
            _bytesButton.EnableInClassList("thry-selected", !floats);
            _floatsButton.EnableInClassList("thry-selected", floats);
        }

        // ---------- Refresh ----------

        void OnEdited() => RefreshControls();

        void RefreshControls()
        {
            if (_wheel == null || State == null) return;
            _shownVersion = State.Version;
            _wheel.MarkDirtyRepaint();
            var display = State.Display;
            _newSwatch.Set(ThryColorMath.Clamp01(display), State.Format.Alpha);
            // While typing, the field keeps its text and caret; any other change replaces the text.
            if (!FocusInside(_hex) || State.Version != _hexVersion) { _hex.SetValueWithoutNotify(HexText()); _hexVersion = State.Version; }
            _red.Value = display.r; _green.Value = display.g; _blue.Value = display.b;
            var hsv = State.Hsv; _hsvHue.Value = hsv.x; _hsvSaturation.Value = hsv.y; _hsvValue.Value = hsv.z;
            var hsl = State.Hsl; _hslHue.Value = hsl.x; _hslSaturation.Value = hsl.y; _hslLightness.Value = hsl.z;
            if (_alpha != null) _alpha.Value = Mathf.Clamp01(State.Alpha);
            if (_intensity != null)
            {
                // The range grows to fit typed intensities and never shrinks during a session.
                _intensityRange = Mathf.Clamp(Mathf.Max(_intensityRange, Mathf.Ceil(Mathf.Abs(State.Intensity))), ThryColorMath.DefaultIntensityRange, ThryColorMath.MaxIntensity);
                _intensity.SetRange(0, _intensityRange);
                _intensity.Value = State.Intensity;
            }
        }

        static string HexLine(Color display, float? intensity)
        {
            string line = "#" + ThryColorMath.ToHex(display);
            if (intensity.HasValue && Mathf.Abs(intensity.Value) >= .05f)
                line += " · " + RetainedText.Get("color_picker_intensity", "Intensity") + " " + intensity.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
            return line;
        }

        // A rough first size, so the window rarely changes height when it is fitted after layout.
        static float EstimatedHeight(ThryColorFormat format) => 404 + (format.Hdr ? 22 : 0) + (format.Alpha ? 22 : 0);

        void FitToContent()
        {
            var root = rootVisualElement;
            if (_ended || _wheelArea == null || root.panel == null) return;
            float wheel = Mathf.Floor(root.contentRect.width);
            if (!(wheel > 0)) return;
            if (Mathf.Abs(_wheelArea.resolvedStyle.height - wheel) > .5f)
            {
                _wheelArea.style.height = wheel;
                _wheel.style.width = wheel; _wheel.style.height = wheel;
                return; // Measure again after this layout pass.
            }
            float height = root.resolvedStyle.paddingTop + root.resolvedStyle.paddingBottom
                + root.resolvedStyle.borderTopWidth + root.resolvedStyle.borderBottomWidth;
            foreach (var child in root.Children())
                if (child.resolvedStyle.display != DisplayStyle.None && child.resolvedStyle.position != Position.Absolute) height += Outer(child);
            height = Mathf.Ceil(height);
            if (!ThryColorMath.IsFinite(height) || Mathf.Abs(_size.y - height) < .5f) return;
            // The window reports a wrong position while its panel lays out, so resize on the next editor update.
            _pendingHeight = height;
        }

        void ApplyPendingHeight()
        {
            if (_pendingHeight <= 0 || _ended || !_positionKnown) return;
            _size = new Vector2(Width, _pendingHeight);
            _pendingHeight = 0;
            minSize = maxSize = _size;
            // Until the first build is placed, start from the placement; later resizes (a new row of swatches)
            // keep the window where the user put it.
            bool opening = _placed && !_placementChecked;
            var rect = Fit(new Rect(_knownPosition, _size), opening ? Target.ScreenRect.center : _knownPosition + _size / 2);
            if (opening) _placement = rect;
            SetPosition(rect);
        }

        static float Outer(VisualElement element)
        {
            float height = element.layout.height;
            if (!ThryColorMath.IsFinite(height)) return 0;
            return height + element.resolvedStyle.marginTop + element.resolvedStyle.marginBottom;
        }

        // ---------- Helpers ----------

        Color WithChannel(int channel, float value)
        {
            var color = ThryColorMath.Clamp01(State.Display); color[channel] = value; color.a = 1;
            return color;
        }

        Color AlphaSample(float alpha)
        {
            var color = ThryColorMath.Clamp01(State.Display); color.a = alpha;
            return color;
        }

        Color IntensitySample(float stops)
        {
            var color = ThryColorMath.ScaleRgb(State.BaseRaw, stops, 1);
            return ThryColorMath.Clamp01(State.Format.LinearData ? ThryColorMath.EncodeGamma(color) : color);
        }

        static int HexDigits(string text)
        {
            if (text == null) return 0;
            text = text.Trim();
            if (text.StartsWith("#")) text = text.Substring(1);
            else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            return text.Length;
        }

        static bool Within<T>(VisualElement element) where T : VisualElement
            => element is T || element?.GetFirstAncestorOfType<T>() != null;

        static bool InTextInput(VisualElement element)
            => Within<TextField>(element) || Within<FloatField>(element) || Within<IntegerField>(element);

        static bool FocusInside(VisualElement element)
        {
            var focused = element?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == element || element.Contains(focused));
        }
    }
}
