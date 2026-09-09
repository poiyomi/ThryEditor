#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // A composite gesture changes only its selected axes. Cancellation must restore
    // those axes on each owner without reverting unrelated values or Undo groups.
    internal sealed class RetainedPropertyGesture
    {
        readonly RetainedMaterialModel _model;
        readonly ShaderProperty _property;
        readonly Dictionary<Material, (Shader Shader, Vector4 Value)> _originals = new Dictionary<Material, (Shader, Vector4)>();
        int _mask;
        int _generation;
        bool _texture, _active;
        internal bool Cancelling { get; private set; }
        internal bool Active => _active;
        internal RetainedPropertyGesture(RetainedMaterialModel model, ShaderProperty property)
        { _model = model; _property = property; }

        internal void Begin(int mask, bool texture = false)
        {
            End();
            if (!_model.CanEdit(_property)) return;
            _mask = mask; _texture = texture;
            foreach (var material in _model.Owners(_property))
            {
                var value = MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { material }, _property.MaterialProperty.name);
                _originals[material] = (material.shader, texture ? value.textureScaleAndOffset : value.vectorValue);
            }
            _active = _originals.Count > 0;
        }

        internal void Cancel()
        {
            if (!_active) return;
            Cancelling = true;
            _active = false;
            _model.Edit(_property, property =>
            {
                var owner = property.targets.OfType<Material>().FirstOrDefault();
                if (owner == null || !_originals.TryGetValue(owner, out var original) || owner.shader != original.Shader) return;
                var value = _texture ? property.textureScaleAndOffset : property.vectorValue;
                for (int axis = 0; axis < 4; axis++) if ((_mask & (1 << axis)) != 0) value[axis] = original.Value[axis];
                if (_texture) property.textureScaleAndOffset = value; else property.vectorValue = value;
            }, true);
        }

        internal void End() { ++_generation; _active = false; Cancelling = false; _originals.Clear(); }

        internal void Attach(FloatField field, Func<int> affectedAxes, bool texture = false, Action restored = null, Action starting = null)
        {
            // The native label dragger consumes PointerDown inside FloatField's
            // composite boundary. Listen on its label before native StartDragging
            // normalizes a mixed field to the first displayed scalar.
            field.labelElement.RegisterCallback<PointerDownEvent>(e =>
            {
                var target = e.target as VisualElement;
                if (e.button != 0 || target == null || (target != field.labelElement && !field.labelElement.Contains(target))) return;
                if (!field.enabledInHierarchy || !_model.CanEdit(_property)) { e.StopImmediatePropagation(); return; }
                Begin(affectedAxes(), texture);
                starting?.Invoke();
            }, TrickleDown.TrickleDown);
            field.labelElement.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape || !_active) return;
                int generation = _generation;
                try { Cancel(); restored?.Invoke(); }
                finally
                {
                    // Native Escape releases capture before emitting its original
                    // displayed value. Keep writes suppressed through BOTH events,
                    // even if restoring an owner detaches or resynchronizes this field.
                    // A new gesture must not be ended by this deferred completion.
                    field.schedule.Execute(() => { if (_generation == generation) End(); });
                }
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<PointerUpEvent>(e => { if (!Cancelling) End(); }, TrickleDown.TrickleDown);
            field.RegisterCallback<PointerCaptureOutEvent>(e => { if (!Cancelling) End(); });
            field.RegisterCallback<DetachFromPanelEvent>(e => { if (!Cancelling) End(); });
        }
    }
}
#endif
