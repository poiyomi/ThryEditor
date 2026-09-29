using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Unity's gradient picker for <see cref="ThryGradientField"/>, with key colors edited in the Thry color picker.
    /// Like Unity's, edits reach the field as they happen and it closes when it loses focus. Escape puts back the
    /// gradient the field had when it opened.
    /// </summary>
    internal sealed class ThryGradientPicker : EditorWindow
    {
        static ThryGradientPicker s_current;
        [NonSerialized] GradientField _field;
        [NonSerialized] Gradient _gradient, _original;
        [NonSerialized] object _editor, _library;
        [NonSerialized] bool _hdr, _closing;
        [NonSerialized] ColorSpace _colorSpace;

        /// <summary>False when Unity's gradient editor cannot be hosted; the field then opens Unity's picker.</summary>
        internal static bool Show(GradientField field)
        {
            if (!GradientKeyColors.Supported) return false;
            if (s_current != null) s_current.Close();
            var window = CreateInstance<ThryGradientPicker>();
            try
            {
                window._field = field; window._hdr = field.hdr; window._colorSpace = field.colorSpace;
                window._gradient = field.value ?? new Gradient();
                window._original = Copy(window._gradient);
                window._editor = GradientKeyColors.Create(window._gradient, window._hdr, window._colorSpace);
            }
            catch (Exception e) when (e is TargetInvocationException || e is MemberAccessException || e is ArgumentException)
            {
                DestroyImmediate(window);
                return false;
            }
            // The preset library is optional; the keys can be edited without it.
            try { window._library = GradientEditor2.PresetLibraryOnGUI != null ? GradientEditor2.GetGradientLibary(window.PresetClicked) : null; }
            catch (Exception e) when (e is TargetInvocationException || e is MemberAccessException || e is ArgumentException || e is NullReferenceException) { }
            window.titleContent = new GUIContent(RetainedText.Get("gradient", "Gradient"));
            window.minSize = new Vector2(360, 260);
            window.position = new Rect(window.position.position, new Vector2(400, 340));
            window.wantsMouseMove = true;
            Undo.undoRedoPerformed += window.OnUndoRedo;
            s_current = window;
            // Automation shows it like the color picker, as a utility window that stays open without focus.
            if (ThryColorPickerWindow.UseUtilityWindow) window.ShowUtility(); else window.ShowAuxWindow();
            return true;
        }

        internal static void CloseFor(GradientField field)
        {
            if (s_current != null && s_current._field == field) s_current.Close();
        }

        void OnDestroy()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (s_current == this) s_current = null;
            // Like Unity's gradient picker: remember the chosen library and scroll, and read the libraries from disk next time.
            GradientEditor2.ReleaseGradientLibrary(_library);
            _library = null;
        }

        bool Live => _editor != null && _field != null && _field.panel != null;

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog"); root.AddToClassList("thry-gradient-editor");
            // Nothing survives a script reload.
            if (!Live) { CloseLater(); return; }
            var stops = new IMGUIContainer(DrawStops) { name = "gradient-stop-editor" };
            stops.style.height = 130; stops.style.flexShrink = 0; root.Add(stops);
            if (_library != null)
            {
                IMGUIContainer presets = null;
                presets = new IMGUIContainer(() => DrawPresets(presets.contentRect)) { name = "gradient-preset-library" };
                presets.style.flexGrow = 1; root.Add(presets);
            }
            RetainedWindow.Shortcuts(root, Cancel);
            // With nothing focused, keys go to the panel's tree instead of this root.
            root.focusable = true; root.tabIndex = -1;
            root.schedule.Execute(() => { if (root.focusController?.focusedElement == null) root.Focus(); });
        }

        void DrawStops()
        {
            // A field that left its window has nothing to edit.
            if (!Live) { CloseLater(); return; }
            EditorGUI.BeginChangeCheck();
            GradientKeyColors.OnGUI(_editor, GUILayoutUtility.GetRect(0, 10000, 130, 130), Send);
            if (EditorGUI.EndChangeCheck()) Send();
        }

        void DrawPresets(Rect rect)
        {
            if (!Live) return;
            try { GradientEditor2.PresetLibraryOnGUI.Invoke(_library, new object[] { rect, _gradient }); }
            catch (TargetInvocationException e) when (e.InnerException is ExitGUIException) { GUIUtility.ExitGUI(); }
        }

        void PresetClicked(int clickCount, object preset)
        {
            if (!(preset is Gradient gradient) || !Live) return;
            Load(gradient);
            Send();
        }

        void Cancel()
        {
            if (Live && !SameKeys(_original, _gradient)) { Load(_original); Send(); }
            Close();
        }

        void CloseLater()
        {
            if (_closing) return;
            _closing = true;
            EditorApplication.delayCall += () => { if (this) Close(); };
        }

        void Load(Gradient gradient)
        {
            _gradient.SetKeys(gradient.colorKeys, gradient.alphaKeys); _gradient.mode = gradient.mode;
            GradientKeyColors.Init(_editor, _gradient, _hdr, _colorSpace);
            Repaint();
        }

        // The field gets its own copy, as from Unity's picker, so its owner never holds the gradient being edited.
        void Send()
        {
            if (!Live) return;
            // Unity 6.0 to 6.3 compare only colors when a GradientField's value is set, so a moved key would not be sent.
            // Like Unity's own picker, send the change whatever the comparison says.
            var copy = Copy(_gradient);
            var previous = _field.value;
            _field.SetValueWithoutNotify(copy);
            using (var e = ChangeEvent<Gradient>.GetPooled(previous, copy))
            {
                e.target = _field;
                _field.SendEvent(e);
            }
        }

        static Gradient Copy(Gradient source)
        {
            var copy = new Gradient();
            copy.SetKeys(source.colorKeys, source.alphaKeys); copy.mode = source.mode;
            return copy;
        }

        // Gradient.Equals also compares the color space, which the editor sets and field copies leave out.
        static bool SameKeys(Gradient a, Gradient b)
            => a.mode == b.mode && a.colorKeys.SequenceEqual(b.colorKeys) && a.alphaKeys.SequenceEqual(b.alphaKeys);

        // Like Unity's picker, show what the field holds after an undo. Owners update their fields after this callback.
        // Rebuilding the keys ends an open key color session, so an undo that left this gradient alone keeps them.
        void OnUndoRedo() => EditorApplication.delayCall += () =>
        {
            if (this && Live && _field.value is Gradient current && !SameKeys(current, _gradient)) Load(current);
        };
    }
}
