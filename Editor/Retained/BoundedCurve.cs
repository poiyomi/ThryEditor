#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Authoring controls are not bounded to the output domain. The graph and texture
    // converter clamp evaluated results; fitting only sanitizes invalid curve data.
    internal static class BoundedCurve
    {
        internal const float Separation = .001f;
        internal static AnimationCurve Copy(AnimationCurve curve) =>
            new AnimationCurve(curve.keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        internal static float Weight(Keyframe key, bool incoming) =>
            (key.weightedMode & (incoming ? WeightedMode.In : WeightedMode.Out)) != 0
                ? Mathf.Max(incoming ? key.inWeight : key.outWeight, .001f) : 1f / 3;
        internal static AnimationCurve Fit(AnimationCurve source)
        {
            if (source == null || source.length == 0) return AnimationCurve.Linear(0, 0, 1, 1);
            var keys = new List<Keyframe>();
            foreach (var original in source.keys)
            {
                if (float.IsNaN(original.time) || float.IsInfinity(original.time)) continue;
                var key = original;
                if (float.IsNaN(key.value) || float.IsInfinity(key.value)) key.value = 0;
                key.inWeight = float.IsNaN(key.inWeight) || float.IsInfinity(key.inWeight) ? 1f / 3 : Weight(key, true);
                key.outWeight = float.IsNaN(key.outWeight) || float.IsInfinity(key.outWeight) ? 1f / 3 : Weight(key, false);
                key.weightedMode = WeightedMode.Both;
                if (float.IsNaN(key.inTangent)) key.inTangent = 0;
                if (float.IsNaN(key.outTangent)) key.outTangent = 0;
                if (keys.Count > 0 && key.time - keys[keys.Count - 1].time < Separation * .5f)
                    keys[keys.Count - 1] = key;
                else keys.Add(key);
            }
            if (keys.Count == 0) return AnimationCurve.Linear(0, 0, 1, 1);
            return new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        internal static AnimationCurve Move(AnimationCurve source, int index, float time, float value)
        {
            var keys = source.keys;
            float margin = index > 0 && index < keys.Length - 1
                ? Mathf.Min(Separation, (keys[index + 1].time - keys[index - 1].time) * .25f) : Separation;
            if (float.IsNaN(time) || float.IsInfinity(time)) time = keys[index].time;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = keys[index].value;
            time = Mathf.Clamp(time, index > 0 ? keys[index - 1].time + margin : float.NegativeInfinity,
                index < keys.Length - 1 ? keys[index + 1].time - margin : float.PositiveInfinity);
            var key = keys[index]; key.time = time; key.value = value; keys[index] = key;
            return Fit(new AnimationCurve(keys));
        }
        internal static AnimationCurve Insert(AnimationCurve source, float time, float value)
        {
            if (float.IsNaN(time) || float.IsInfinity(time) || float.IsNaN(value) || float.IsInfinity(value)) return Copy(source);
            if (source.keys.Any(k => Mathf.Abs(k.time - time) < Separation)) return Copy(source);
            var keys = source.keys.ToList(); keys.Add(new Keyframe(time, value));
            return Fit(new AnimationCurve(keys.OrderBy(k => k.time).ToArray()));
        }
        internal static AnimationCurve Remove(AnimationCurve source, int index) =>
            source.length <= 1 ? Copy(source)
                : Fit(new AnimationCurve(source.keys.Where((key, i) => i != index).ToArray()));

        internal static Vector2 Handle(AnimationCurve curve, int index, bool incoming)
        {
            var key = curve[index];
            float duration = incoming ? key.time - curve[index - 1].time : curve[index + 1].time - key.time;
            float dx = duration * Weight(key, incoming) * (incoming ? -1 : 1);
            return new Vector2(key.time + dx, key.value + dx * (incoming ? key.inTangent : key.outTangent));
        }
        internal static AnimationCurve MoveHandle(AnimationCurve source, int index, bool incoming, Vector2 point)
        {
            var keys = source.keys; var key = keys[index];
            float duration = incoming ? key.time - keys[index - 1].time : keys[index + 1].time - key.time;
            if (float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.y)) return Copy(source);
            float distance = Mathf.Max(incoming ? key.time - point.x : point.x - key.time, duration * .001f);
            float slope = (point.y - key.value) / (incoming ? -distance : distance);
            if (incoming) { key.inWeight = distance / duration; key.inTangent = slope; key.weightedMode |= WeightedMode.In; }
            else { key.outWeight = distance / duration; key.outTangent = slope; key.weightedMode |= WeightedMode.Out; }
            keys[index] = key; return Fit(new AnimationCurve(keys));
        }
        internal static AnimationCurve Tangents(AnimationCurve source, int index, string mode)
        {
            var keys = source.keys; var key = keys[index];
            key.inTangent = mode == "Linear" && index > 0 ? (key.value - keys[index - 1].value) / (key.time - keys[index - 1].time) : 0;
            key.outTangent = mode == "Hold" ? float.PositiveInfinity
                : mode == "Linear" && index < keys.Length - 1 ? (keys[index + 1].value - key.value) / (keys[index + 1].time - key.time) : 0;
            key.weightedMode = WeightedMode.None; keys[index] = key;
            return Fit(new AnimationCurve(keys));
        }
        internal static AnimationCurve Flip(AnimationCurve source)
        {
            var keys = source.keys.Reverse().Select(key => new Keyframe(1 - key.time, key.value,
                -key.outTangent, -key.inTangent, key.outWeight, key.inWeight)
            {
                weightedMode = ((key.weightedMode & WeightedMode.In) != 0 ? WeightedMode.Out : WeightedMode.None)
                    | ((key.weightedMode & WeightedMode.Out) != 0 ? WeightedMode.In : WeightedMode.None)
            }).ToArray();
            return new AnimationCurve(keys) { preWrapMode = source.postWrapMode, postWrapMode = source.preWrapMode };
        }

        internal static AnimationCurve Preset(string name)
        {
            switch (name)
            {
                case "Reverse": return Fit(AnimationCurve.Linear(0, 1, 1, 0));
                case "Smooth reverse": return Fit(AnimationCurve.EaseInOut(0, 1, 1, 0));
                case "Valley": return Fit(new AnimationCurve(new Keyframe(0, 1), new Keyframe(.5f, 0), new Keyframe(1, 1)));
                case "Triangle": return Fit(new AnimationCurve(new Keyframe(0, 0, 2, 2), new Keyframe(.5f, 1, 2, -2), new Keyframe(1, 0, -2, -2)));
                case "Zero": return Fit(AnimationCurve.Linear(0, 0, 1, 0));
                case "One": return Fit(AnimationCurve.Linear(0, 1, 1, 1));
                case "Smooth": return Fit(AnimationCurve.EaseInOut(0, 0, 1, 1));
                case "Ease in": return Fit(new AnimationCurve(new Keyframe(0, 0, 0, 0), new Keyframe(1, 1, 2, 2)));
                case "Ease out": return Fit(new AnimationCurve(new Keyframe(0, 0, 2, 2), new Keyframe(1, 1, 0, 0)));
                case "Pulse": return Fit(new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 1), new Keyframe(1, 0)));
                case "Step": return Fit(new AnimationCurve(new Keyframe(0, 0, 0, float.PositiveInfinity), new Keyframe(.5f, 1), new Keyframe(1, 1)));
                default: return Fit(AnimationCurve.Linear(0, 0, 1, 1));
            }
        }
    }
}
#endif
