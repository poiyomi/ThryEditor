using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    public class MaterialHelper
    {
        /// <summary>
        /// Override tags that describe the material rather than the shader it happens to use, so they have to survive
        /// a shader swap. Unity itself keeps them, but code that rebuilds a material during an upgrade can lose them,
        /// and the VRChat SDK clears VRCFallback after any shader swap recorded for undo (see
        /// KeepPastUndoRecordedSwap). Either way the VRC Fallback Shader the user picked silently resets.
        public static readonly string[] TagsPreservedAcrossShaderSwap = { "VRCFallback" };

        /// <summary>
        /// Reads the override tags the material itself stores, limited to "tagNames".
        /// Material.GetTag falls through to the shader's SubShader tags when the material has no override of its own,
        /// so the serialized tag map is used to tell "the material stores this" apart from "the shader provides this".
        /// </summary>
        public static Dictionary<string, string> GetOwnOverrideTags(Material material, params string[] tagNames)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>();
            if (material == null || tagNames == null || tagNames.Length == 0) return tags;

            var it = new SerializedObject(material).GetIterator();
            while (it.Next(true))
            {
                if (it.name != "stringTagMap") continue;

                for (int i = 0; i < it.arraySize; i++)
                {
                    string tagName = it.GetArrayElementAtIndex(i).displayName;
                    if (Array.IndexOf(tagNames, tagName) < 0) continue;

                    // The material has an override tag for this tag, so GetTag returns that override and not a shader tag.
                    tags[tagName] = material.GetTag(tagName, false, string.Empty);
                }
                break;
            }
            return tags;
        }

        /// <summary>
        /// Re-applies tags gathered by "GetOwnOverrideTags". Re-applying an unchanged value is a no-op.
        /// </summary>
        public static void ApplyOverrideTags(Material material, Dictionary<string, string> tags)
        {
            if (material == null || tags == null) return;
            foreach (KeyValuePair<string, string> tag in tags)
            {
                material.SetOverrideTag(tag.Key, tag.Value);
            }
#if VRC_UNDO_POSTPROCESSOR
            // In VRChat SDK 3.10.6 and newer, VRCUndoPostProcessor clears VRC Fallback Tags from OUR MATERIALS on Undo.postprocessModifications
            // whenever an undo-recorded change swaps a material's shader! Because of this, it breaks our ability to preserve VRC Fallback Tags
            // whenever a user switches their Poiyomi shader to another Poiyomi shader or duplicates the material.
            //
            // I am left with no other choice but to add a guard to ensure this never happens on exclusively Poiyomi-only materials. I will only
            // allow the SDK to clear the VRC Fallback tag IF ThryEditor isn't used. That way, the SDK can then clear the tag so that a fallback
            // picked here doesn't carry over into a shader that has no way to show or change it.
            KeepPastUndoRecordedSwap(material, tags);
#endif
        }

        /// <summary>
        /// Assigns a new shader to a material without losing the settings Unity resets on a shader swap: the render
        /// queue and the material's own override tags (e.g. the VRC Fallback Shader). Use this instead of setting
        /// material.shader directly when swapping shaders outside of the inspector, e.g. from an upgrade tool.
        /// </summary>
        public static void SwapShaderPreservingSettings(Material material, Shader newShader)
        {
            if (material == null || newShader == null) return;

            int previousQueue = material.renderQueue;
            Dictionary<string, string> previousTags = GetOwnOverrideTags(material, TagsPreservedAcrossShaderSwap);

            material.shader = newShader;
            material.renderQueue = previousQueue;
            ApplyOverrideTags(material, previousTags);
        }

        public static void ToggleKeyword(Material material, string keyword, bool turn_on)
        {
            bool is_on = material.IsKeywordEnabled(keyword);
            if (is_on && !turn_on)
                material.DisableKeyword(keyword);
            else if (!is_on && turn_on)
                material.EnableKeyword(keyword);
        }

        public static void ToggleKeyword(Material[] materials, string keyword, bool on)
        {
            foreach (Material m in materials)
                ToggleKeyword(m, keyword, on);
        }

        public static void ToggleKeyword(MaterialProperty p, string keyword, bool on)
        {
            ToggleKeyword(p.targets as Material[], keyword, on);
        }

        /// <summary>
        /// Set Material Property value or Renderqueue of current Editor.
        /// </summary>
        /// <param name="key">Property Name or "render_queue"</param>
        /// <param name="value"></param>
        public static void SetValueAdvanced(string key, string value)
        {
            Material[] materials = ShaderEditor.Active.Materials;
            if (ShaderEditor.Active.PropertyDictionary.TryGetValue(key, out ShaderProperty p))
            {
                MaterialHelper.SetValue(p.MaterialProperty, value);
                p.UpdateKeywordFromValue();
            }
            else if (key == "render_queue")
            {
                int q = 0;
                if (int.TryParse(value, out q))
                {
                    foreach (Material m in materials) m.renderQueue = q;
                }
            }
            else if (key == "render_type")
            {
                foreach (Material m in materials) m.SetOverrideTag("RenderType", value);
            }
            else if (key == "preview_type")
            {
                foreach (Material m in materials) m.SetOverrideTag("PreviewType", value);
            }
            else if (key == "ignore_projector")
            {
                foreach (Material m in materials) m.SetOverrideTag("IgnoreProjector", value);
            }
        }

        public static void SetValue(MaterialProperty p, string value)
        {
            object prev = null;
            if (p.GetPropertyType() == ShaderPropertyType.Texture)
            {
                prev = p.textureValue;
                p.textureValue = AssetDatabase.LoadAssetAtPath<Texture>(value);
            }
            else if (p.GetPropertyType() == ShaderPropertyType.Float || p.GetPropertyType() == ShaderPropertyType.Range)
            {
                prev = p.floatValue;
                p.floatValue = Parser.ParseFloat(value, p.floatValue);
            }
#if UNITY_2022_1_OR_NEWER
            else if (p.GetPropertyType() == ShaderPropertyType.Int)
            {
                prev = p.intValue;
                p.intValue = (int)Parser.ParseFloat(value, p.intValue);
            }
#endif
            else if (p.GetPropertyType() == ShaderPropertyType.Vector)
            {
                prev = p.vectorValue;
                p.vectorValue = Converter.StringToVector(value);
            }
            else if (p.GetPropertyType() == ShaderPropertyType.Color)
            {
                prev = p.colorValue;
                p.colorValue = Converter.StringToColor(value);
            }
            if (p.applyPropertyCallback != null)
                p.applyPropertyCallback.Invoke(p, 1, prev);
        }

        public static void CopyValue(Material source, MaterialProperty target)
        {
            if (!source.HasProperty(target.name)) return;
            object prev = null;
            switch (target.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    prev = target.floatValue;
                    target.floatValue = source.GetNumber(target);
                    break;
#if UNITY_2022_1_OR_NEWER
                case ShaderPropertyType.Int:
                    prev = target.intValue;
                    target.intValue = source.GetInt(target.name);
                    break;
#endif
                case ShaderPropertyType.Color:
                    prev = target.colorValue;
                    target.colorValue = source.GetColor(target.name);
                    break;
                case ShaderPropertyType.Vector:
                    prev = target.vectorValue;
                    target.vectorValue = source.GetVector(target.name);
                    break;
                case ShaderPropertyType.Texture:
                    prev = target.textureValue;
                    target.textureValue = source.GetTexture(target.name);
                    Vector2 offset = source.GetTextureOffset(target.name);
                    Vector2 scale = source.GetTextureScale(target.name);
                    target.textureScaleAndOffset = new Vector4(scale.x, scale.y, offset.x, offset.y);
                    break;
            }
            if (target.applyPropertyCallback != null)
                target.applyPropertyCallback.Invoke(target, 1, prev);
        }

        public static void CopyValue(MaterialProperty source, MaterialProperty target)
        {
            object prev = null;
            switch (target.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    prev = target.floatValue;
                    target.floatValue = source.floatValue;
                    break;
#if UNITY_2022_1_OR_NEWER
                case ShaderPropertyType.Int:
                    prev = target.intValue;
                    target.intValue = source.intValue;
                    break;
#endif
                case ShaderPropertyType.Color:
                    prev = target.colorValue;
                    target.colorValue = source.colorValue;
                    break;
                case ShaderPropertyType.Vector:
                    prev = target.vectorValue;
                    target.vectorValue = source.vectorValue;
                    break;
                case ShaderPropertyType.Texture:
                    prev = target.textureValue;
                    target.textureValue = source.textureValue;
                    target.textureScaleAndOffset = source.textureScaleAndOffset;
                    break;
            }
            if (target.applyPropertyCallback != null)
                target.applyPropertyCallback.Invoke(target, 1, prev);
        }

        public static void CopyValue(MaterialProperty source, params Material[] targets)
        {
            CopyValue(source, MaterialEditor.GetMaterialProperty(targets, source.name));
        }

        public static object GetValue(Material material, string propertyName)
        {
            if (!material.HasProperty(propertyName)) return null;
            MaterialProperty property = MaterialEditor.GetMaterialProperty(new Material[] { material }, propertyName);
            return GetValue(property);
        }

        public static object GetValue(MaterialProperty property)
        {
            switch (property.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return property.floatValue;
#if UNITY_2022_1_OR_NEWER
                case ShaderPropertyType.Int:
                    return property.intValue;
#endif
                case ShaderPropertyType.Color:
                    return property.colorValue;
                case ShaderPropertyType.Vector:
                    return property.vectorValue;
                case ShaderPropertyType.Texture:
                    return property.textureValue;
            }
            return null;
        }

        public static string[] GetFloatPropertiesFromSerializedObject(Material material)
        {
            SerializedObject serializedObject = new SerializedObject(material);
            SerializedProperty savedProperties = serializedObject.FindProperty("m_SavedProperties").FindPropertyRelative("m_Floats");
            string[] properties = new string[savedProperties.arraySize];
            for (int i = 0; i < properties.Length; i++)
            {
                properties[i] = savedProperties.GetArrayElementAtIndex(i).displayName;
            }
            return properties;
        }

#if VRC_UNDO_POSTPROCESSOR
        static readonly Dictionary<Material, (Shader shader, Dictionary<string, string> tags)> s_tagsKeptPastUndo = new Dictionary<Material, (Shader shader, Dictionary<string, string> tags)>();
        static void KeepPastUndoRecordedSwap(Material material, Dictionary<string, string> tags)
        {
            // Destroyed materials, e.g. the copies presets swap, never see an undo flush.
            List<Material> gone = new List<Material>();
            foreach (Material m in s_tagsKeptPastUndo.Keys) if (m == null) gone.Add(m);
            foreach (Material m in gone) s_tagsKeptPastUndo.Remove(m);

            Dictionary<string, string> kept = new Dictionary<string, string>();
            foreach (string name in TagsPreservedAcrossShaderSwap)
            {
                if (tags.TryGetValue(name, out string value) && !string.IsNullOrEmpty(value)) kept[name] = value;
            }
            Shader shader = material.shader;
            if (kept.Count == 0 || shader == null || !ShaderHelper.IsShaderUsingThryEditor(shader))
            {
                s_tagsKeptPastUndo.Remove(material);
                return;
            }
            s_tagsKeptPastUndo[material] = (shader, kept);

            // Undo runs these handlers in the order they subscribed. The SDK subscribes when scripts load, so
            // subscribing again now puts this one after it.
            Undo.postprocessModifications -= ReapplyAfterUndoRecordedSwap;
            Undo.postprocessModifications += ReapplyAfterUndoRecordedSwap;
        }
        static UndoPropertyModification[] ReapplyAfterUndoRecordedSwap(UndoPropertyModification[] modifications)
        {
            if (modifications == null) return modifications;
            foreach (UndoPropertyModification modification in modifications)
            {
                PropertyModification change = modification.currentValue;
                if (change == null || change.propertyPath != "m_Shader") continue;
                Material material = change.target as Material;
                if (material == null || !s_tagsKeptPastUndo.TryGetValue(material, out var kept)) continue;

                // The tags were kept for one swap. A later swap starts over from what the material has by then.
                s_tagsKeptPastUndo.Remove(material);
                if (material.shader != kept.shader) continue;
                foreach (KeyValuePair<string, string> tag in kept.tags) material.SetOverrideTag(tag.Key, tag.Value);
            }
            if (s_tagsKeptPastUndo.Count == 0) Undo.postprocessModifications -= ReapplyAfterUndoRecordedSwap;
            return modifications;
        }
#endif
    }

}