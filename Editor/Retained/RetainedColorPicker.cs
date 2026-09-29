using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Connects color fields to the color picker and the eyedropper, and turns each session into
    /// edits of the field's owners: one undo step, and none at all when it is cancelled.
    /// </summary>
    internal static class RetainedColorPicker
    {
        /// <summary>A picker, eyedropper or picker-driven IMGUI edit is in progress. Computed, so it cannot stay set.</summary>
        internal static bool GestureActive => ThryColorPickerWindow.Current != null || ThryEyeDropper.IsActive
            || Drawers.ThryColorGUI.SessionActive || UnityPicker.Target != null;

        /// <summary>The field whose picker or eyedropper session is running. Its inspector no longer has focus.</summary>
        internal static VisualElement ActiveField { get; private set; }

        internal static void Attach(ThryColorField field, ShaderProperty property, RetainedMaterialModel model)
        {
            field.OpenRequested += f => Open(f, new MaterialColorTarget(f, property, model));
            field.EyeDropperRequested += f => PickFromScreen(f, new MaterialColorTarget(f, property, model));
        }

        /// <summary>For fields that are not material properties. <paramref name="write"/> stores a value.</summary>
        internal static void Attach(ThryColorField field, Action<Color> write)
        {
            field.OpenRequested += f => Open(f, new FieldColorTarget(f, write));
            field.EyeDropperRequested += f => PickFromScreen(f, new FieldColorTarget(f, write));
        }

        internal static bool IsEditing(ThryColorField field)
        {
            var window = ThryColorPickerWindow.Current;
            return (window != null && window.Target is ColorFieldTarget target && target.Field == field) || UnityPicker.Target?.Field == field;
        }

        internal static ThryColorFormat Format(MaterialProperty property)
        {
            var shader = property.targets.OfType<Material>().FirstOrDefault(m => m != null)?.shader;
            int index = shader == null ? -1 : shader.FindPropertyIndex(property.name);
            bool thryHdr = index >= 0 && shader.GetPropertyAttributes(index).Any(a => new DrawerAttribute(a).Name == "ThryHDR");
            return ThryColorFormat.ForProperty(property.GetPropertyFlags(), thryHdr);
        }

        /// <summary>An element's rect in screen points, or Rect.zero when its window is unknown.</summary>
        internal static Rect ScreenRect(VisualElement element)
        {
            if (element?.panel == null) return Rect.zero;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.rootVisualElement.panel != element.panel) continue;
                // EditorWindow.position is the content rect; docked windows have their tab strip above the root.
                var bound = element.worldBound;
                return new Rect(window.position.position + (bound.position - window.rootVisualElement.worldBound.position), bound.size);
            }
            return Rect.zero;
        }

        static void Open(ThryColorField field, ColorFieldTarget target)
        {
            if (!target.IsValid) return;
            field.Focus();
            if (Config.Instance.useUnityColorPicker && UnityPicker.Show(target)) return;
            try { ThryColorPickerWindow.Show(target); }
            catch { target.End(true); throw; }
        }

        static void PickFromScreen(ThryColorField field, ColorFieldTarget target)
        {
            if (!target.IsValid) return;
            var window = ThryColorPickerWindow.Current;
            if (window != null) window.Close();
            target.Begin();
            bool finished = false;
            Action<Color> picked = color =>
            {
                if (finished) return;
                finished = true; field.PreviewOverride = null;
                try
                {
                    if (!target.IsValid) return;
                    target.ApplyPicked(color);
                }
                finally { target.End(false); }
            };
            Action cancelled = () =>
            {
                if (finished) return;
                finished = true; field.PreviewOverride = null;
                target.End(true);
            };
            try { ThryEyeDropper.Begin(field, color => { if (!finished) field.PreviewOverride = color; }, picked, cancelled); }
            catch { cancelled(); throw; }
        }

        /// <summary>
        /// A session over a ThryColorField. Writes happen before Apply returns: a queued ChangeEvent
        /// could land after End and fall outside the session's undo step.
        /// </summary>
        abstract class ColorFieldTarget : IColorPickerTarget
        {
            internal readonly ThryColorField Field;
            readonly ColorPickerUndoGroup _undo = new ColorPickerUndoGroup();
            bool _active, _applied, _dirty;

            protected ColorFieldTarget(ThryColorField field) { Field = field; }

            public abstract Color Value { get; }
            public abstract bool Mixed { get; }
            public ThryColorFormat Format => Field.Format;
            public abstract string Title { get; }
            public abstract bool IsValid { get; }
            /// <summary>The per-write check; defaults to IsValid.</summary>
            protected virtual bool CanWrite => IsValid;
            public Rect ScreenRect => RetainedColorPicker.ScreenRect(Field.Swatch);
            protected abstract void Snapshot();
            protected abstract void Write(Color raw);
            /// <summary>Gives every owner back its value from Begin.</summary>
            protected abstract void Restore();
            /// <summary>Applies a picked display color to every owner, keeping each owner's alpha and intensity.</summary>
            protected abstract void WritePicked(Color display);
            protected abstract void Synchronize();
            /// <summary>Every owner still holds its value from Begin.</summary>
            protected abstract bool Unchanged();
            /// <summary>Cancel reverts the session through Undo instead of writing the originals back.</summary>
            protected virtual bool CancelThroughUndo => false;

            public void Begin()
            {
                if (_active) return;
                _active = true; _applied = _dirty = false;
                _undo.Start();
                Snapshot();
                ActiveField = Field;
            }

            public void Apply(Color raw)
            {
                if (!CanWrite) return;
                Write(raw);
                _applied = _dirty = true;
                Synchronize();
            }

            internal void ApplyPicked(Color display)
            {
                if (!CanWrite) return;
                WritePicked(display);
                _applied = _dirty = true;
                Synchronize();
            }

            public void Cancel()
            {
                if (!_dirty) return;
                _dirty = false;
                if (CancelThroughUndo)
                {
                    _undo.Revert();
                    _applied = false;
                    _undo.Start();
                }
                else if (IsValid) Restore();
                Synchronize();
            }

            public void End(bool cancelled)
            {
                if (!_active) return;
                _active = false;
                try
                {
                    if (cancelled) Cancel();
                    // Begin started this group and only the session records into it. With every owner back at its
                    // original (links included), dropping it changes no values and leaves no no-op step; Ctrl+Z after
                    // a revert can bring the edit back, so the owners are compared. With nothing written there is
                    // nothing to keep or take back.
                    if (_applied)
                    {
                        if (cancelled || Unchanged()) _undo.Revert();
                        else _undo.Commit();
                    }
                }
                finally
                {
                    if (ActiveField == Field) ActiveField = null;
                    Synchronize();
                }
            }
        }

        sealed class MaterialColorTarget : ColorFieldTarget
        {
            readonly ShaderProperty _property;
            readonly RetainedMaterialModel _model;
            readonly Dictionary<Material, (Shader Shader, Color Value)> _originals = new Dictionary<Material, (Shader, Color)>();
            string _name;

            internal MaterialColorTarget(ThryColorField field, ShaderProperty property, RetainedMaterialModel model) : base(field)
            { _property = property; _model = model; }

            bool Live => RetainedMaterialModel.HasLiveTargets(_model.Editor) && _property.MaterialProperty != null;
            public override Color Value => Live ? _property.MaterialProperty.colorValue : Field.value;
            public override bool Mixed => Live ? _property.MaterialProperty.hasMixedValue : Field.showMixedValue;
            public override string Title
            {
                get
                {
                    string caption = RetainedMaterialBody.SectionCaption(_property) ?? "";
                    int separator = caption.IndexOf('|');
                    return (separator < 0 ? caption : caption.Substring(0, separator)).Trim();
                }
            }
            public override bool IsValid => Field.panel != null && RetainedMaterialModel.HasValidTargets(_model.Editor) && _model.CanEdit(_property);
            // EditSingleProperty repeats the target and editability checks, so a write needs only a live row.
            protected override bool CanWrite => Field.panel != null && Live;

            protected override void Snapshot()
            {
                _originals.Clear();
                _name = _property.MaterialProperty.name;
                foreach (var material in _model.Owners(_property))
                    _originals[material] = (SectionLock.GetSourceShader(material.shader), material.GetColor(_name));
            }

            protected override bool Unchanged()
            {
                foreach (var pair in _originals)
                {
                    var material = pair.Key;
                    if (material == null || SectionLock.GetSourceShader(material.shader) != pair.Value.Shader
                        || !ThryColorMath.Same(material.GetColor(_name), pair.Value.Value)) return false;
                }
                return true;
            }

            // Writing the originals back would key them into the clip.
            protected override bool CancelThroughUndo => AnimationMode.InAnimationMode();

            // The single-property path Bind uses: undo, animation keys, drawers, callbacks and links.
            // MaterialProperty.colorValue writes every owner even while they differ.
            protected override void Write(Color raw) => _model.EditSingleProperty(_property, p => p.colorValue = raw);

            protected override void Restore() => _model.EditSingleProperty(_property, p =>
            {
                var material = p.targets.OfType<Material>().FirstOrDefault();
                if (material != null && _originals.TryGetValue(material, out var original) && SectionLock.GetSourceShader(material.shader) == original.Shader)
                    p.colorValue = original.Value;
            }, true);

            protected override void WritePicked(Color display)
            {
                var format = Format;
                _model.EditSingleProperty(_property, p =>
                {
                    var state = new ThryColorState(p.colorValue, format);
                    state.SetPicked(display);
                    if (state.Changed) p.colorValue = state.Raw;
                }, true);
            }

            protected override void Synchronize()
            {
                if (Field.panel == null || !Live) return;
                var p = _property.MaterialProperty;
                RetainedFields.SynchronizeValue(Field, p.colorValue, p.hasMixedValue);
            }
        }

        sealed class FieldColorTarget : ColorFieldTarget
        {
            readonly Action<Color> _write;
            Color _original;

            internal FieldColorTarget(ThryColorField field, Action<Color> write) : base(field) { _write = write; }

            public override Color Value => Field.value;
            public override bool Mixed => Field.showMixedValue;
            public override string Title => Field.label ?? "";
            public override bool IsValid => Field.panel != null && Field.enabledInHierarchy;
            protected override void Snapshot() => _original = Field.value;
            protected override void Write(Color raw) { Field.SetValueWithoutNotify(raw); _write(raw); }
            protected override void Restore() => Write(_original);

            protected override void WritePicked(Color display)
            {
                var state = new ThryColorState(Field.value, Format);
                state.SetPicked(display);
                if (state.Changed) Write(state.Raw);
            }

            protected override void Synchronize() { }
            protected override bool Unchanged() => ThryColorMath.Same(Field.value, _original);
        }

        /// <summary>Unity's own picker, for Config.useUnityColorPicker. Its session ends when the picker closes.</summary>
        static class UnityPicker
        {
            static readonly Type PickerType = typeof(Editor).Assembly.GetType("UnityEditor.ColorPicker");
            static readonly MethodInfo ShowMethod = FindShow();
            static readonly PropertyInfo Visible = PickerType?.GetProperty("visible", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            static ColorFieldTarget s_target;
            static bool s_escaped;

            internal static ColorFieldTarget Target => s_target;

            // 2022.3: Show(Action<Color>, Color, bool showAlpha, bool hdr). Unity 6 adds setAlphaIfTransparentOnNextPick.
            static MethodInfo FindShow()
            {
                if (PickerType == null) return null;
                foreach (var method in PickerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != "Show") continue;
                    var parameters = method.GetParameters();
                    if ((parameters.Length == 4 || parameters.Length == 5) && parameters[0].ParameterType == typeof(Action<Color>)
                        && parameters[1].ParameterType == typeof(Color) && parameters.Skip(2).All(p => p.ParameterType == typeof(bool)))
                        return method;
                }
                return null;
            }

            internal static bool Show(ColorFieldTarget target)
            {
                if (ShowMethod == null || Visible == null) return false;
                Finish();
                target.Begin();
                s_target = target; s_escaped = false;
                Action<Color> changed = color =>
                {
                    if (s_target != target) return;
                    var e = Event.current;
                    if (e != null && e.commandName == "UndoRedoPerformed") return;
                    // Escape reverts Unity's undo group, then reports its single starting color. Writing that would
                    // flatten a mixed selection, and writes without undo (Texture Studio) still need restoring.
                    if (e != null && e.rawType == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                    {
                        s_escaped = true;
                        target.Cancel();
                        return;
                    }
                    target.Apply(color);
                };
                var format = target.Format;
                var arguments = ShowMethod.GetParameters().Length == 5
                    ? new object[] { changed, target.Value, format.Alpha, format.Hdr, false }
                    : new object[] { changed, target.Value, format.Alpha, format.Hdr };
                try { ShowMethod.Invoke(null, arguments); }
                catch { Finish(); throw; }
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
                return true;
            }

            static void Poll()
            {
                if (s_target == null || !(bool)Visible.GetValue(null)) Finish();
            }

            static void Finish()
            {
                EditorApplication.update -= Poll;
                var target = s_target;
                bool escaped = s_escaped;
                s_target = null; s_escaped = false;
                target?.End(escaped);
            }
        }
    }
}
