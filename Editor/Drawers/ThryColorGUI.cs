using System;
using System.Globalization;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    /// <summary>
    /// IMGUI color field that opens the Thry color picker. Picker edits come back as a command
    /// event for this control, so the caller's change check, undo and callbacks run as for a
    /// normal IMGUI edit.
    /// </summary>
    internal static class ThryColorGUI
    {
        const string ChangedCommand = "ThryColorPickerChanged";
        const float EyeDropperWidth = 20f;
        const float BadgeMinWidth = 40f;
        const float BadgeMinIntensity = .05f;
        static readonly int s_Hash = "ThryColorField".GetHashCode();
        static readonly Type ViewType = typeof(Editor).Assembly.GetType("UnityEditor.GUIView");
        static readonly PropertyInfo CurrentView = ViewType?.GetProperty("current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly MethodInfo SendEventMethod = ViewType?.GetMethod("SendEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Event) }, null);
        static readonly MethodInfo RepaintMethod = ViewType?.GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        // The previous session can still owe its control a value when a new one starts.
        static ImguiColorTarget s_session, s_previous;
        static GUIStyle s_mixedStyle, s_badgeStyle;

        internal static bool SessionActive => (s_session != null && s_session.Active) || (s_previous != null && s_previous.Active);

        internal static Color Field(Rect position, GUIContent label, Color value, ThryColorFormat format, bool showEyeDropper = true)
        {
            if (Config.Instance.useUnityColorPicker) return EditorGUI.ColorField(position, label, value, true, format.Alpha, format.Hdr);
            int id = GUIUtility.GetControlID(s_Hash, FocusType.Keyboard, position);
            position = EditorGUI.PrefixLabel(position, id, label ?? GUIContent.none);
            var evt = Event.current;
            bool mixed = EditorGUI.showMixedValue;
            bool eyeDropper = showEyeDropper && ThryEyeDropper.Available;
            var session = Session(id);
            // Undo and redo change the value under an open picker; it adopts whatever this reports.
            if (session != null && session.Active && !session.Pending.HasValue) { session.Value = value; session.Mixed = mixed; }
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (!position.Contains(evt.mousePosition)) break;
                    if (evt.button == 1) { ShowContextMenu(id); evt.Use(); break; }
                    if (evt.button != 0 || !GUI.enabled) break;
                    GUIUtility.keyboardControl = id;
                    if (eyeDropper && evt.mousePosition.x >= position.xMax - EyeDropperWidth) PickFromScreen(id, value, format, mixed, label, position);
                    else Open(id, value, format, mixed, label, position);
                    GUIUtility.ExitGUI();
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != id || !GUI.enabled
                        || (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space)) break;
                    evt.Use();
                    Open(id, value, format, mixed, label, position);
                    GUIUtility.ExitGUI();
                    break;
                case EventType.ValidateCommand:
                    if (GUIUtility.keyboardControl == id && (evt.commandName == "Copy" || evt.commandName == "Paste")) evt.Use();
                    break;
                case EventType.ExecuteCommand:
                    if (evt.commandName == ChangedCommand)
                    {
                        if (session == null || !session.Pending.HasValue) break;
                        value = session.TakePending(); GUI.changed = true; evt.Use();
                    }
                    else if (GUIUtility.keyboardControl == id && evt.commandName == "Copy")
                    {
                        EditorGUIUtility.systemCopyBuffer = CopyText(value, format); evt.Use();
                    }
                    else if (GUIUtility.keyboardControl == id && evt.commandName == "Paste" && GUI.enabled && CanPaste())
                    {
                        // Keeps alpha for 6-digit codes and the current intensity, like the picker's hex field.
                        var state = new ThryColorState(value, format);
                        state.SetHex(EditorGUIUtility.systemCopyBuffer);
                        if (state.Changed || mixed) { value = state.Raw; GUI.changed = true; }
                        evt.Use();
                    }
                    break;
                case EventType.Layout:
                    // The command could not be sent (or would have re-entered this pass); deliver it here.
                    if (session != null && session.Deferred && session.Pending.HasValue) { value = session.TakePending(); GUI.changed = true; }
                    break;
                case EventType.Repaint:
                    Draw(position, id, value, format, mixed, eyeDropper, session?.PreviewColor);
                    break;
            }
            return value;
        }

        static ImguiColorTarget Session(int id)
        {
            object view = null;
            for (int i = 0; i < 2; i++)
            {
                var session = i == 0 ? s_session : s_previous;
                if (session == null || session.Id != id || (!session.Active && !session.Pending.HasValue)) continue;
                if (view == null) view = CurrentView?.GetValue(null);
                if (session.Shows(view)) return session;
            }
            return null;
        }

        static ImguiColorTarget NewSession(int id, Color value, ThryColorFormat format, bool mixed, GUIContent label, Rect swatch)
        {
            string title = label?.text ?? "";
            if (s_session != null) s_previous = s_session;
            s_session = new ImguiColorTarget(id, CurrentView?.GetValue(null), value, mixed, format, title, GUIUtility.GUIToScreenRect(swatch));
            return s_session;
        }

        static void Open(int id, Color value, ThryColorFormat format, bool mixed, GUIContent label, Rect swatch)
        {
            // End the open picker while its session is still current, so its last value reaches its control.
            var window = ThryColorPickerWindow.Current;
            if (window != null) window.Close();
            var target = NewSession(id, value, format, mixed, label, swatch);
            try { ThryColorPickerWindow.Show(target); }
            catch (ExitGUIException) { throw; }
            catch { target.End(true); throw; }
        }

        static void PickFromScreen(int id, Color value, ThryColorFormat format, bool mixed, GUIContent label, Rect swatch)
        {
            var window = ThryColorPickerWindow.Current;
            if (window != null) window.Close();
            var target = NewSession(id, value, format, mixed, label, swatch);
            target.Begin();
            bool finished = false;
            Action<Color> picked = color =>
            {
                if (finished) return;
                finished = true; target.PreviewColor = null;
                try
                {
                    if (!target.IsValid) return;
                    // IMGUI returns one value for every owner, so the first owner's alpha and intensity are kept.
                    var state = new ThryColorState(target.Value, format);
                    state.SetPicked(color);
                    if (state.Changed || target.Mixed) target.Apply(state.Raw);
                }
                finally { target.End(false); }
            };
            Action cancelled = () =>
            {
                if (finished) return;
                finished = true; target.PreviewColor = null;
                target.End(true);
            };
            try { ThryEyeDropper.BeginFromIMGUI(color => { if (finished) return; target.PreviewColor = color; target.Repaint(); }, picked, cancelled); }
            catch (ExitGUIException) { throw; }
            catch { cancelled(); throw; }
        }

        static void ShowContextMenu(int id)
        {
            GUIUtility.keyboardControl = id;
            var view = CurrentView?.GetValue(null);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy"), false, () => Send(view, EditorGUIUtility.CommandEvent("Copy")));
            if (GUI.enabled && CanPaste()) menu.AddItem(new GUIContent("Paste"), false, () => Send(view, EditorGUIUtility.CommandEvent("Paste")));
            else menu.AddDisabledItem(new GUIContent("Paste"));
            menu.ShowAsContext();
        }

        static string CopyText(Color value, ThryColorFormat format)
        {
            var display = ThryColorMath.Preview(value, format);
            return "#" + ThryColorMath.ToHex(display, format.Alpha && display.a < 1f);
        }

        static bool CanPaste() => ThryColorMath.IsPastedHex(EditorGUIUtility.systemCopyBuffer ?? "");

        static void Draw(Rect position, int id, Color value, ThryColorFormat format, bool mixed, bool eyeDropper, Color? preview)
        {
            bool hover = position.Contains(Event.current.mousePosition);
            Rect swatch;
            if (eyeDropper)
            {
                // Unity's color field style: frame plus eyedropper icon, with the swatch inside its padding.
                EditorStyles.colorField.Draw(position, GUIContent.none, id, false, hover);
                swatch = EditorStyles.colorField.padding.Remove(position);
            }
            else
            {
                EditorStyles.textField.Draw(position, GUIContent.none, id, false, hover);
                swatch = new Rect(position.x + 1, position.y + 1, position.width - 2, position.height - 2);
            }
            if (mixed)
            {
                if (s_mixedStyle == null) s_mixedStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(swatch, "—", s_mixedStyle);
                return;
            }
            var fill = preview.HasValue ? ThryColorMath.Clamp01(preview.Value) : ThryColorMath.Preview(value, format);
            float dim = GUI.enabled ? 1f : .5f;
            EditorGUI.DrawRect(swatch, new Color(fill.r, fill.g, fill.b, dim));
            if (format.Alpha)
            {
                var bar = new Rect(swatch.x, swatch.yMax - 2, swatch.width, 2);
                EditorGUI.DrawRect(bar, new Color(0, 0, 0, dim));
                bar.width *= Mathf.Clamp01(ThryColorMath.IsFinite(value.a) ? value.a : 1f);
                EditorGUI.DrawRect(bar, new Color(1, 1, 1, dim));
            }
            float intensity = format.Hdr ? ThryColorMath.Intensity(value, format) : 0f;
            if (intensity < BadgeMinIntensity || swatch.width < BadgeMinWidth) return;
            if (s_badgeStyle == null)
                s_badgeStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 9, padding = new RectOffset(0, 3, 0, 0) };
            s_badgeStyle.normal.textColor = ThryColorMath.Luminance(fill) > .5f ? Color.black : Color.white;
            GUI.Label(swatch, "+" + intensity.ToString("0.0", CultureInfo.InvariantCulture), s_badgeStyle);
        }

        static bool Send(object view, Event e)
        {
            if (SendEventMethod == null || !(view is UnityEngine.Object alive) || alive == null) return false;
            try { return SendEventMethod.Invoke(view, new object[] { e }) is bool used && used; }
            catch (TargetInvocationException exception) when (exception.InnerException is ExitGUIException) { return true; }
        }

        static void Repaint(object view)
        {
            if (RepaintMethod != null && view is UnityEngine.Object alive && alive != null) RepaintMethod.Invoke(view, null);
        }

        /// <summary>
        /// An IMGUI color control being edited by the picker or eyedropper. Values are handed to the
        /// control through a command event and written by its caller, like Unity's own picker does.
        /// </summary>
        sealed class ImguiColorTarget : IColorPickerTarget
        {
            internal readonly int Id;
            readonly object _view;
            readonly ColorPickerUndoGroup _undo = new ColorPickerUndoGroup();
            bool _applied, _dirty, _beginMixed;
            Color _beginValue;

            internal ImguiColorTarget(int id, object view, Color value, bool mixed, ThryColorFormat format, string title, Rect screenRect)
            {
                Id = id; _view = view; Value = value; Mixed = mixed; Format = format; Title = title; ScreenRect = screenRect;
            }

            internal bool Active { get; private set; }
            internal Color? Pending { get; private set; }
            internal bool Deferred { get; private set; }
            internal Color? PreviewColor { get; set; }

            public Color Value { get; internal set; }
            public bool Mixed { get; internal set; }
            public ThryColorFormat Format { get; }
            public string Title { get; }
            public bool IsValid => _view is UnityEngine.Object view && view != null;
            public Rect ScreenRect { get; }

            internal bool Shows(object view) => ReferenceEquals(view, _view);
            internal void Repaint() => ThryColorGUI.Repaint(_view);

            internal Color TakePending()
            {
                var value = Pending.Value;
                Pending = null; Deferred = false;
                return value;
            }

            public void Begin()
            {
                if (Active) return;
                Active = true; _applied = _dirty = false;
                _beginValue = Value; _beginMixed = Mixed;
                _undo.Start();
            }

            public void Apply(Color raw)
            {
                if (!IsValid) return;
                Value = raw; Pending = raw;
                _applied = _dirty = true;
                Deliver();
            }

            public void Cancel()
            {
                if (!_dirty) return;
                _dirty = false; Pending = null; Deferred = false;
                // Reverting keeps each owner's own value; writing the start color back would flatten a mixed selection.
                if (_applied) _undo.Revert();
                _applied = false;
                // The owners hold their Begin values again. The window resyncs from these before the next GUI pass.
                Value = _beginValue; Mixed = _beginMixed;
                _undo.Start();
                Repaint();
            }

            public void End(bool cancelled)
            {
                if (!Active) return;
                Active = false;
                if (cancelled) Cancel();
                else if (Pending.HasValue) Deliver();
                // Begin started this group and only the session records into it; a cancelled session took its
                // records back, and one that wrote nothing has none.
                if (_applied) _undo.Commit();
                Repaint();
            }

            void Deliver()
            {
                // Sending into the view whose GUI pass is running would re-enter it; wait for its next pass instead.
                bool nested = Event.current != null && ReferenceEquals(CurrentView?.GetValue(null), _view);
                if (!nested) Send(_view, EditorGUIUtility.CommandEvent(ChangedCommand));
                Deferred = Pending.HasValue;
                if (Deferred) Repaint();
            }
        }
    }
}
