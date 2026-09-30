using UnityEditor;
using UnityEngine;
using Thry.ThryEditor.Helpers;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Drawers
{
    // Usage in shader:
    // [Curve4] _MyFourFloatCurve ("My Curve (4 samples)", Vector) = (1,1,1,1)
    // This drawer shows a CurveField and bakes 4 evenly spaced samples (0, 1/3, 2/3, 1)
    // into the Vector4 material property. Runtime code (poiSampleCurve4) interpolates linearly between them.
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
                EditorGUI.HelpBox(position, "[Curve4] requires a Vector property (stores 4 samples)", MessageType.Warning);
                return;
            }

            Vector4 propertyValue = prop.vectorValue;
            if (!_initializedFromProperty || propertyValue != _lastPropertyValue)
            {
                _curve = CreateCurveFromVector(propertyValue);
                _lastPropertyValue = propertyValue;
                _initializedFromProperty = true;
            }

            Rect valueRect = EditorGUI.PrefixLabel(position, label);
            valueRect.width = Mathf.Max(0f, valueRect.width - GUILib.GetSmallTextureVRAMWidth(prop));

            // Let Thry render the label; we only draw the field in the valueRect
            EditorGUI.BeginChangeCheck();
            var newCurve = EditorGUI.CurveField(valueRect, _curve);
            if (EditorGUI.EndChangeCheck())
            {
                _curve = newCurve;
                BakeCurveToVector(prop);
            }
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return base.GetPropertyHeight(prop, label, editor);
        }

        private void BakeCurveToVector(MaterialProperty prop)
        {
            // Sample at fixed times for compact storage
            float s0 = Mathf.Clamp01(_curve.Evaluate(0f));
            float s1 = Mathf.Clamp01(_curve.Evaluate(1f / 3f));
            float s2 = Mathf.Clamp01(_curve.Evaluate(2f / 3f));
            float s3 = Mathf.Clamp01(_curve.Evaluate(1f));

            _lastPropertyValue = new Vector4(s0, s1, s2, s3);
            prop.vectorValue = _lastPropertyValue;
        }

        private static AnimationCurve CreateCurveFromVector(Vector4 value)
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, value.x),
                new Keyframe(1f / 3f, value.y),
                new Keyframe(2f / 3f, value.z),
                new Keyframe(1f, value.w)
            );

            // Linear to match the shader
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }

            return curve;
        }
    }
}


