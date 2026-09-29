using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Thry.ThryEditor.Helpers
{
    /// <summary>
    /// Preserves a material's Rendering Preset when its shader is swapped.
    /// </summary>
    public static class RenderingPresets
    {
        const string TAG_PREFIX_PARKED_VALUE = "thry_rendering_preset_";
        // Identifies the shader the material's current preset value belongs to. See GetPresetIdentity.
        const string TAG_OWNER = "thry_rendering_preset_owner";

        /// <summary>
        /// Property names treated as a shader's rendering preset, most specific first. Other packages can add their
        /// own names here if a shader calls its preset something else.
        /// </summary>
        public static readonly List<string> PresetPropertyNames = new List<string>
        {
            ShaderEditor.PROPERTY_NAME_IN_SHADER_PRESETS, "_GrabMode"
        };

        /// <summary>
        /// The preset property of the shader the editor currently has collected, or null if it has none.
        /// </summary>
        public static ShaderProperty FindPresetProperty(ShaderEditor editor)
        {
            if (editor == null || editor.PropertyDictionary == null) return null;

            foreach (string name in PresetPropertyNames)
            {
                if (editor.PropertyDictionary.TryGetValue(name, out ShaderProperty property) && property.MaterialProperty != null) return property;
            }
            return null;
        }

        /// <summary>
        /// Parks the preset value of the shader that is being swapped away from, so that swapping back to it can
        /// restore the value instead of leaving the user with whatever the detour left behind.
        /// Call this before the new shader is assigned, while the old shader's properties are still the live ones.
        /// </summary>
        public static void Park(ShaderEditor editor, Material material, Shader shaderBeingLeft)
        {
            if (material == null || shaderBeingLeft == null || ShaderOptimizer.IsMaterialLocked(material)) return;

            ShaderProperty preset = FindPresetProperty(editor);
            if (preset == null || !material.HasProperty(preset.MaterialProperty.name)) return;

            float value = material.GetNumber(preset.MaterialProperty);
            material.SetOverrideTag(GetParkedValueTag(shaderBeingLeft), value.ToString(CultureInfo.InvariantCulture));
            material.SetOverrideTag(TAG_OWNER, GetPresetIdentity(preset));
        }

        /// <summary>
        /// Applies the incoming shader's rendering preset after a swap, so the render state matches the shader the
        /// material is on now. Does nothing when the value carried over from a shader that means the same thing by
        /// it, so swapping between shaders that share a preset (Toon to Pro, say) leaves the user's choice alone.
        /// Call this once the editor has collected the new shader's properties.
        /// </summary>
        public static void ApplyAfterSwap(ShaderEditor editor, Material[] materials)
        {
            if (editor == null || materials == null || materials.Length == 0 || materials[0] == null) return;
            if (ShaderOptimizer.IsMaterialLocked(materials[0])) return;

            ShaderProperty preset = FindPresetProperty(editor);
            if (preset == null) return;

            string identity = GetPresetIdentity(preset);
            string owner = materials[0].GetTag(TAG_OWNER, false, string.Empty);

            // No owner means the material came from a shader without a preset at all - there is nothing to correct,
            // and a matching owner means this shader reads the inherited value the same way the old one wrote it.
            // Nothing is written in either case, so merely opening a material never dirties it.
            if (string.IsNullOrEmpty(owner) || owner == identity) return;

            List<float> declared = GetDeclaredValues(preset);
            if (declared.Count == 0) return; // No presets declared, nothing sensible to apply

            // Parked values are read off the first material: the editor writes one value to the whole selection, so a
            // multi-selection that parked different values comes back on the first material's preset.
            string parkedTag = GetParkedValueTag(editor.Shader);
            float current = preset.MaterialProperty.GetNumber();
            float target;

            if (TryParse(materials[0].GetTag(parkedTag, false, string.Empty), out float parked) && IsDeclared(declared, parked))
            {
                target = parked; // Back on a shader we swapped away from earlier: pick the user's setup back up
            }
            else if (IsDeclared(declared, current))
            {
                target = current; // The value means something here, it just was never applied
            }
            else
            {
                target = declared[0]; // Foreign value, fall back to this shader's first preset
            }

            // Assigning through the property runs the shader's on_value_actions, which is the point of all this -
            // even when the value is unchanged, the actions are what put the material into the right render state.
            preset.FloatValue = target;

            foreach (Material m in materials)
            {
                if (m == null) continue;
                m.SetOverrideTag(parkedTag, string.Empty); // Consumed
            }
            StampOwner(materials, identity);
        }

        /// <summary>
        /// What a preset value means is defined by the property it lives on plus the set of values that property
        /// declares. Two shaders that agree on both read an inherited value the same way.
        /// </summary>
        static string GetPresetIdentity(ShaderProperty preset)
        {
            if (preset == null || preset.MaterialProperty == null) return string.Empty;

            IEnumerable<string> values = GetDeclaredValues(preset).OrderBy(v => v).Select(v => v.ToString("0.###", CultureInfo.InvariantCulture));
            return preset.MaterialProperty.name + ":" + string.Join(",", values.ToArray());
        }

        /// <summary>
        /// The values the preset property declares actions for, in declaration order.
        /// </summary>
        static List<float> GetDeclaredValues(ShaderProperty preset)
        {
            List<float> values = new List<float>();
            PropertyValueAction[] actions = preset?.Options?.on_value_actions;
            if (actions == null) return values;

            foreach (PropertyValueAction action in actions)
            {
                // on_value_actions is public and comes out of shader source, which can hold anything.
                // So if there is a malformed preset block, null-check it!
                if (action == null) continue;
                if (TryParse(action.value, out float value)) values.Add(value);
            }

            return values;
        }

        static void StampOwner(Material[] materials, string identity)
        {
            foreach (Material m in materials)
            {
                if (m == null) continue;
                m.SetOverrideTag(TAG_OWNER, identity);
            }
        }

        static bool IsDeclared(List<float> declared, float value)
        {
            return declared.Any(v => Mathf.Approximately(v, value));
        }

        static bool TryParse(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static string GetParkedValueTag(Shader shader)
        {
            if (shader == null) return TAG_PREFIX_PARKED_VALUE;
            shader = SectionLock.GetSourceShader(shader) ?? shader;

            char[] name = shader.name.ToCharArray();
            for (int i = 0; i < name.Length; i++)
            {
                if (!char.IsLetterOrDigit(name[i])) name[i] = '_';
            }

            return TAG_PREFIX_PARKED_VALUE + new string(name);
        }
    }
}
