using UnityEditor;
using UnityEngine;
using Thry.ThryEditor.Helpers;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Drawers
{
    // Usage in shader:
    // [Curve4] _MyCurve ("My Curve", Vector) = (0, 0.3333333, 0.6666667, 1)
    // The vector is a cubic Bezier: start height, the handle heights at 1/3 and 2/3, and end height.
    // It is edited as a two-key curve whose tangents are those handles. Runtime code is poiSampleCurve4.
    public class Curve4Drawer : MaterialPropertyDrawer
    {
        private AnimationCurve _curve = new AnimationCurve();

        // Guard to re-sync the curve from the underlying vector when first drawn
        private bool _initializedFromProperty = false;
        private Vector4 _lastPropertyValue;

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            if (prop.GetPropertyType() != ShaderPropertyType.Vector)
            {
                EditorGUI.HelpBox(position, "[Curve4] requires a Vector property", MessageType.Warning);
                return;
            }

            Vector4 propertyValue = prop.vectorValue;
            if (!_initializedFromProperty || propertyValue != _lastPropertyValue)
            {
                _curve = ToCurve(propertyValue);
                _lastPropertyValue = propertyValue;
                _initializedFromProperty = true;
            }

            Rect valueRect = EditorGUI.PrefixLabel(position, label);
            valueRect.width = Mathf.Max(0f, valueRect.width - GUILib.GetSmallTextureVRAMWidth(prop));

            // Let Thry render the label; we only draw the field in the valueRect
            EditorGUI.BeginChangeCheck();
            var newCurve = EditorGUI.CurveField(valueRect, _curve);
            if (EditorGUI.EndChangeCheck())
                prop.vectorValue = ToVector(newCurve, prop.vectorValue);
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return base.GetPropertyHeight(prop, label, editor);
        }

        // Handles may reach past the 0-1 box for ends up to 9x as steep as a straight line; the shader clamps
        // the part of the curve that leaves the box.
        public const float HandleMin = -2, HandleMax = 3;
        public static float ClampHandle(float height) => Mathf.Clamp(height, HandleMin, HandleMax);

        public static AnimationCurve ToCurve(Vector4 value)
        {
            float start = 3 * (value.y - value.x), end = 3 * (value.w - value.z);
            return new AnimationCurve(new Keyframe(0f, value.x, start, start), new Keyframe(1f, value.w, end, end));
        }

        // Keys stay at 0 and 1, so two keys are read from their values and tangents alone. A handle that moved is
        // kept within the handle range; one already outside stays until it is moved.
        public static Vector4 ToVector(AnimationCurve curve, Vector4 previous = default)
        {
            Keyframe[] keys = curve.keys;
            if (keys.Length != 2)
            {
                Vector4 fit = Fit(curve);
                return new Vector4(fit.x, ClampHandle(fit.y), ClampHandle(fit.z), fit.w);
            }
            float start = Mathf.Clamp01(keys[0].value), end = Mathf.Clamp01(keys[1].value);
            // An in-tangent dragged past vertical is stored with the opposite sign to the way its handle points.
            float endSlope = float.IsInfinity(keys[1].inTangent) ? -keys[1].inTangent : keys[1].inTangent;
            float startHandle = start + keys[0].outTangent / 3, endHandle = end - endSlope / 3;
            return new Vector4(start,
                Mathf.Abs(startHandle - previous.y) < 1e-4f ? previous.y : ClampHandle(startHandle),
                Mathf.Abs(endHandle - previous.z) < 1e-4f ? previous.z : ClampHandle(endHandle),
                end);
        }

        // Least squares for the handle heights, with the ends held in place.
        private static Vector4 Fit(AnimationCurve curve)
        {
            float start = Mathf.Clamp01(curve.Evaluate(0)), end = Mathf.Clamp01(curve.Evaluate(1));
            float s11 = 0, s12 = 0, s22 = 0, r1 = 0, r2 = 0;
            for (int i = 1; i < 64; i++)
            {
                float t = i / 64f, u = 1 - t;
                float b1 = 3 * t * u * u, b2 = 3 * t * t * u;
                float r = Mathf.Clamp01(curve.Evaluate(t)) - start * u * u * u - end * t * t * t;
                s11 += b1 * b1; s12 += b1 * b2; s22 += b2 * b2;
                r1 += r * b1; r2 += r * b2;
            }
            float det = s11 * s22 - s12 * s12;
            return new Vector4(start, (r1 * s22 - r2 * s12) / det, (r2 * s11 - r1 * s12) / det, end);
        }
    }
}


