using System;
using System.Collections;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Draws Unity's internal GradientEditor with its color keys edited in the Thry color picker instead of
    /// Unity's: a click on the key's color field, a double click on the selected key, or anything else that
    /// opens Unity's picker for it. Its eyedropper, copy and paste stay Unity's.
    /// </summary>
    internal static class GradientKeyColors
    {
        const BindingFlags Member = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Shared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type EditorType = typeof(Editor).Assembly.GetType("UnityEditor.GradientEditor");
        static readonly MethodInfo InitMethod = EditorType?.GetMethod("Init", Member, null, new[] { typeof(Gradient), typeof(int), typeof(bool), typeof(ColorSpace) }, null);
        static readonly MethodInfo OnGUIMethod = EditorType?.GetMethod("OnGUI", Member, null, new[] { typeof(Rect) }, null);
        static readonly MethodInfo AssignBackMethod = EditorType?.GetMethod("AssignBack", Member, null, Type.EmptyTypes, null);
        static readonly FieldInfo SelectedField = EditorType?.GetField("m_SelectedSwatch", Member);
        static readonly FieldInfo KeysField = EditorType?.GetField("m_RGBSwatches", Member);
        static readonly FieldInfo HdrField = EditorType?.GetField("m_HDR", Member);
        static readonly FieldInfo ColorSpaceField = EditorType?.GetField("m_ColorSpace", Member);
        static readonly FieldInfo DoubleClickField = EditorType?.GetField("m_DoubleClickDetected", Member);
        static readonly Type SwatchType = EditorType?.GetNestedType("Swatch", BindingFlags.Public | BindingFlags.NonPublic);
        static readonly FieldInfo SwatchValue = SwatchType?.GetField("m_Value", Member);
        static readonly FieldInfo SwatchIsAlpha = SwatchType?.GetField("m_IsAlpha", Member);
        static readonly Type PickerType = typeof(Editor).Assembly.GetType("UnityEditor.ColorPicker");
        static readonly FieldInfo PickerInstance = PickerType?.GetField("s_Instance", Shared);
        static readonly FieldInfo PickerView = PickerType?.GetField("m_DelegateView", Member);
        static readonly Type ViewType = typeof(Editor).Assembly.GetType("UnityEditor.GUIView");
        static readonly PropertyInfo CurrentView = ViewType?.GetProperty("current", Shared);
        static readonly MethodInfo RepaintMethod = ViewType?.GetMethod("Repaint", Member, null, Type.EmptyTypes, null);

        internal static bool Supported => InitMethod != null && OnGUIMethod != null && AssignBackMethod != null && SelectedField != null
            && KeysField != null && SwatchValue != null && SwatchIsAlpha != null && CurrentView != null;

        internal static object Create(Gradient gradient, bool hdr, ColorSpace colorSpace)
        {
            var editor = Activator.CreateInstance(EditorType, true);
            Init(editor, gradient, hdr, colorSpace);
            return editor;
        }

        internal static void Init(object editor, Gradient gradient, bool hdr, ColorSpace colorSpace)
            => InitMethod.Invoke(editor, new object[] { gradient, 0, hdr, colorSpace });

        /// <summary>
        /// Unity's GradientEditor.OnGUI. <paramref name="changed"/> runs after the Thry picker writes a key; Unity's
        /// own edits are reported through GUI.changed as before.
        /// </summary>
        internal static void OnGUI(object editor, Rect position, Action changed = null)
        {
            if (Config.Instance.useUnityColorPicker || !Supported) { Draw(editor, position); return; }
            var evt = Event.current;
            var field = ColorFieldRect(position);
            var screen = GUIUtility.GUIToScreenRect(field);
            if (evt.type == EventType.MouseDown && evt.button == 0 && GUI.enabled && field.Contains(evt.mousePosition) && SelectedKey(editor) != null)
            {
                evt.Use();
                Open(editor, SelectedKey(editor), screen, changed);
                GUIUtility.ExitGUI();
            }
            // Unity opens its picker on the mouse up after a double click on the selected key.
            bool open = evt.type == EventType.MouseUp && DoubleClickField != null && (bool)DoubleClickField.GetValue(editor);
            if (open) DoubleClickField.SetValue(editor, false);
            var picker = PickerInstance?.GetValue(null) as UnityEngine.Object;
            var pickerView = picker != null ? PickerView?.GetValue(picker) : null;
            bool exit = false;
            try { OnGUIMethod.Invoke(editor, new object[] { position }); }
            catch (TargetInvocationException e) when (e.InnerException is ExitGUIException) { exit = true; }
            // Anything else that opened Unity's picker for this editor (Space or Enter on its focused field) is handed over.
            var opened = PickerInstance?.GetValue(null) as EditorWindow;
            var openedView = opened != null ? PickerView?.GetValue(opened) : null;
            bool handOver = opened != null && openedView != null && ReferenceEquals(openedView, CurrentView.GetValue(null))
                && (!ReferenceEquals(opened, picker) || !ReferenceEquals(openedView, pickerView));
            var key = open || handOver ? SelectedKey(editor) : null;
            if (key != null)
            {
                if (handOver) opened.Close();
                Open(editor, key, screen, changed);
                exit = true;
            }
            if (exit) GUIUtility.ExitGUI();
        }

        static void Draw(object editor, Rect position)
        {
            try { OnGUIMethod?.Invoke(editor, new object[] { position }); }
            catch (TargetInvocationException e) when (e.InnerException is ExitGUIException) { GUIUtility.ExitGUI(); }
        }

        // The swatch part of the selected key's color field, as GradientEditor.OnGUI lays it out: mode row 24, key rows 16,
        // then 10 below the preview a row 18 high, inset 17, with a 50 wide label and 2 padding, Location taking the last
        // 185 and the eyedropper the last 20 of the field.
        static Rect ColorFieldRect(Rect position)
            => new Rect(position.x + 17 + 52, position.yMax - 16, position.width - 185 - 52 - 20, 18);

        /// <summary>The selected color key, or null when nothing or an alpha key is selected.</summary>
        static object SelectedKey(object editor)
        {
            var key = SelectedField.GetValue(editor);
            return key != null && !(bool)SwatchIsAlpha.GetValue(key) && KeysField.GetValue(editor) is IList keys && keys.Contains(key) ? key : null;
        }

        static void Open(object editor, object key, Rect screenRect, Action changed)
        {
            var target = new KeyTarget(editor, key, CurrentView.GetValue(null), screenRect, changed);
            try { ThryColorPickerWindow.Show(target); }
            catch (ExitGUIException) { throw; }
            catch { target.End(true); throw; }
        }

        /// <summary>
        /// One color key. Writes go into Unity's gradient editor, which assigns them to its gradient, as its own
        /// picker's do. Hosts that record undo when the gradient changes get one step per session.
        /// </summary>
        sealed class KeyTarget : IColorPickerTarget
        {
            readonly object _editor, _key, _view;
            readonly Action _changed;
            readonly ColorPickerUndoGroup _undo = new ColorPickerUndoGroup();
            bool _active, _applied, _dirty;
            Color _original;

            internal KeyTarget(object editor, object key, object view, Rect screenRect, Action changed)
            {
                _editor = editor; _key = key; _view = view; ScreenRect = screenRect; _changed = changed;
                // Keys have no alpha. The editor shows them as the gradient's color space says: gamma encoded when linear,
                // which is how Thry's material gradients are baked and sampled.
                Format = new ThryColorFormat
                {
                    Hdr = HdrField == null || (bool)HdrField.GetValue(editor),
                    LinearData = ColorSpaceField == null || (ColorSpace)ColorSpaceField.GetValue(editor) == ColorSpace.Linear
                };
            }

            public Color Value => (Color)SwatchValue.GetValue(_key);
            public bool Mixed => false;
            public ThryColorFormat Format { get; }
            public string Title => RetainedText.Get("gradient_color_key", "Gradient color");
            // Re-initializing the editor (a preset, an undo) replaces its keys.
            public bool IsValid => _view is UnityEngine.Object view && view != null && KeysField.GetValue(_editor) is IList keys && keys.Contains(_key);
            public Rect ScreenRect { get; }

            public void Begin()
            {
                if (_active) return;
                _active = true; _applied = _dirty = false;
                _original = Value;
                _undo.Start();
            }

            public void Apply(Color raw)
            {
                if (!IsValid) return;
                Write(raw);
                _applied = _dirty = true;
            }

            public void Cancel()
            {
                if (!_dirty) return;
                _dirty = false;
                if (IsValid) Write(_original);
            }

            public void End(bool cancelled)
            {
                if (!_active) return;
                _active = false;
                if (cancelled) Cancel();
                // Begin started this group and only the session records into it; the key is already back at its original.
                if (!_applied) return;
                if (cancelled || (IsValid && ThryColorMath.Same(Value, _original))) _undo.Revert();
                else _undo.Commit();
            }

            void Write(Color raw)
            {
                raw.a = 1f; // Unity keeps key alpha at 1; opacity lives in the alpha keys.
                SwatchValue.SetValue(_key, raw);
                // AssignBack flags GUI.changed for the GUI pass it expects to run in; this write happens outside one.
                bool guiChanged = GUI.changed;
                AssignBackMethod.Invoke(_editor, null);
                GUI.changed = guiChanged;
                _changed?.Invoke();
                if (RepaintMethod != null && _view is UnityEngine.Object view && view != null) RepaintMethod.Invoke(_view, null);
            }
        }
    }

    /// <summary>
    /// A GradientField whose picker is <see cref="ThryGradientPicker"/>, so its key colors open the Thry color picker.
    /// With Config.useUnityColorPicker, or where the picker cannot be built, it opens Unity's gradient picker as before.
    /// </summary>
    internal sealed class ThryGradientField : GradientField
    {
        internal ThryGradientField() : this(null) { }

        internal ThryGradientField(string label) : base(label)
        {
            RegisterCallback<DetachFromPanelEvent>(e => ThryGradientPicker.CloseFor(this));
        }

#if UNITY_2023_2_OR_NEWER
        [EventInterest(EventInterestOptions.Inherit)]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            if (!OpenPicker(evt)) base.HandleEventBubbleUp(evt);
        }
#else
        [EventInterest(EventInterestOptions.Inherit)]
        protected override void ExecuteDefaultAction(EventBase evt)
        {
            if (!OpenPicker(evt)) base.ExecuteDefaultAction(evt);
        }
#endif

        // The events that open Unity's picker.
        bool OpenPicker(EventBase evt)
        {
            if (Config.Instance.useUnityColorPicker) return false;
            bool open = evt is KeyDownEvent key ? key.keyCode == KeyCode.Space || key.keyCode == KeyCode.Return || key.keyCode == KeyCode.KeypadEnter
                : evt is MouseDownEvent mouse && mouse.button == 0 && this.Q(className: inputUssClassName) is VisualElement input
                    && input.ContainsPoint(input.WorldToLocal(mouse.mousePosition));
            return open && ThryGradientPicker.Show(this);
        }
    }
}
