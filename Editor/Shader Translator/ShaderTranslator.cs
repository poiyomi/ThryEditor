using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Thry.ThryEditor.ShaderTranslations
{
    public partial class ShaderTranslator : ScriptableObject
    {
        public string Name;
        public string OriginShader;
        public string TargetShader;
        public bool MatchOriginShaderBasedOnRegex;
        public bool MatchTargetShaderBasedOnRegex;
        public string OriginShaderRegex;
        public string TargetShaderRegex;
        public List<ShaderTranslationsContainer> PropertyTranslationContainers;
        public List<ShaderNameMatchedModifications> PreTranslationPropertyModifications;
        [FormerlySerializedAs("PropertyModifications")] public List<ShaderNameMatchedModifications> PostTranslationPropertyModifications;

        public List<PropertyTranslation> AllPropertyTranslations => PropertyTranslationContainers.SelectMany(x => x.PropertyTranslations).ToList();

        public void Apply(ShaderEditor editor, int? renderQueueOverride = null)
        {
            Material[] materials = editor.Materials;
            if (materials.Length == 1)
            {
                ApplyTo(editor, materials[0], null, renderQueueOverride);
                return;
            }

            // Editor properties would write to every selected material
            foreach (Material material in materials)
                ApplyTo(editor, material, material, renderQueueOverride);
            editor.Reload();
        }

        // only == null: write through the editor's properties
        void ApplyTo(ShaderEditor editor, Material material, Material only, int? renderQueueOverride)
        {
            Shader originShader = editor.LastShader;
            SerializedObject serializedMaterial = new SerializedObject(material);

            List<PropertyTranslation> allTranslations = AllPropertyTranslations;

            _HandlePropertyModifications(editor, originShader, PreTranslationPropertyModifications);

            foreach(PropertyTranslation trans in allTranslations)
            {
                if(editor.PropertyDictionary.TryGetValue(trans.Target, out ShaderProperty targetProp))
                {
                    SerializedProperty p;
                    switch(targetProp.MaterialProperty.GetPropertyType())
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_Floats", trans.Origin);
                            if(p != null)
                            {
                                _HandleFloatProperty(targetProp, trans, p);
                                break;
                            }
                            // Convert a texture property to a 1 if assigned and to 0 if not
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_TexEnvs", trans.Origin);
                            if(p != null)
                            {
                                float textureValue = p.FindPropertyRelative("second").FindPropertyRelative("m_Texture").objectReferenceValue != null ? 1f : 0f;
                                textureValue = SolveExpression(trans, textureValue);
                                SetFloat(targetProp, only, textureValue);
                            }
                            break;
                        case ShaderPropertyType.Int:
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_Ints", trans.Origin);
                            if(p != null)
                            {
                                _HandleIntProperty(targetProp, trans, p);
                                break;
                            }

                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_Floats", trans.Origin);
                            if(p != null)
                            {
                                float f = SolveExpression(trans, p.FindPropertyRelative("second").floatValue);
                                SetFloat(targetProp, only, (int)f);
                                break;
                            }
                            // Convert a texture property to a 1 if assigned and to 0 if not
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_TexEnvs", trans.Origin);
                            if(p != null)
                            {
                                float textureValue = p.FindPropertyRelative("second").FindPropertyRelative("m_Texture").objectReferenceValue != null ? 1f : 0f;
                                textureValue = SolveExpression(trans, textureValue);
                                SetFloat(targetProp, only, (int)textureValue);
                            }
                            break;
                        case ShaderPropertyType.Vector:
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_Colors", trans.Origin);
                            if(p != null) SetVector(targetProp, only, p.FindPropertyRelative("second").vector4Value);
                            break;
                        case ShaderPropertyType.Color:
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_Colors", trans.Origin);
                            if(p != null) SetColor(targetProp, only, p.FindPropertyRelative("second").colorValue);
                            break;
                        case ShaderPropertyType.Texture:
                            p = GetProperty(serializedMaterial, "m_SavedProperties.m_TexEnvs", trans.Origin);
                            if(p != null)
                            {
                                SerializedProperty values = p.FindPropertyRelative("second");
                                Vector2 scale = values.FindPropertyRelative("m_Scale").vector2Value;
                                Vector2 offset = values.FindPropertyRelative("m_Offset").vector2Value;
                                SetTexture(targetProp, only, values.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                                    new Vector4(scale.x, scale.y, offset.x, offset.y));
                            }
                            break;
                    }
                }
            }

            // Post translation modifications
            _HandlePropertyModifications(editor, originShader, PostTranslationPropertyModifications);

            serializedMaterial.ApplyModifiedProperties();

            if(renderQueueOverride != null)
                material.renderQueue = (int)renderQueueOverride;

            ShaderEditor.FixKeywords(new Material[] { material });

            void _HandleFloatProperty(ShaderProperty _targetProp, PropertyTranslation trans, SerializedProperty p)
            {
                float f = SolveExpression(trans, p.FindPropertyRelative("second").floatValue);
                SetFloat(_targetProp, only, f);
            }

            void _HandleIntProperty(ShaderProperty _targetProp, PropertyTranslation trans, SerializedProperty p)
            {
                float f = SolveExpression(trans, p.FindPropertyRelative("second").intValue);
                SetFloat(_targetProp, only, (int)f);
            }

            void _HandlePropertyModifications(ShaderEditor _editor, Shader _originShader, List<ShaderNameMatchedModifications> modifications)
            {
                foreach(var mod in modifications)
                {
                    if(!mod.IsShaderNameMatch(_originShader.name))
                        continue;

                    foreach(var action in mod.propertyModifications)
                    {
                        switch(action.actionType)
                        {
                            case ShaderModificationAction.ActionType.ChangeTargetShader:
                                Shader newShader = Shader.Find(action.targetValue);
                                if(newShader)
                                {
                                    Material m = material;
                                    var preservedTags = MaterialHelper.GetOwnOverrideTags(m, MaterialHelper.TagsPreservedAcrossShaderSwap);
                                    m.shader = newShader;
                                    MaterialHelper.ApplyOverrideTags(m, preservedTags);
                                }
                                break;
                            case ShaderModificationAction.ActionType.SetTargetPropertyValue:
                                if(TryParseNumber(action.targetValue, out float parsedFloat))
                                {
                                    if(action.propertyName == ShaderEditor.PROPERTY_NAME_IN_SHADER_PRESETS)
                                    {
                                        if (only == null) _editor.ShaderRenderingPreset = parsedFloat;
                                        else ShaderEditor.ApplyRenderingPresetToMaterial(only, parsedFloat);
                                    }
                                    else
                                        SetPropertyValue(_editor, action.propertyName, parsedFloat, only);
                                }
                                break;
                        }
                    }
                }
            }
        }

        static float SolveExpression(PropertyTranslation trans, float value)
        {
            string expression = trans.GetAppropriateExpression(value);
            if(string.IsNullOrWhiteSpace(expression))
                return value;
            // If we can parse the expression then our expression is just a number. Replace old value with ours
            if(TryParseNumber(expression, out float result))
                return result;
            return Helper.SolveMath(expression, value);
        }

        // Invariant first; system locale for older definitions
        static bool TryParseNumber(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.TryParse(s, out value);
        }

        void SetPropertyValue(ShaderEditor editor, string propertyName, float value, Material only)
        {
            if (!editor.PropertyDictionary.TryGetValue(propertyName, out var prop))
                return;
            MaterialProperty materialProp = only == null ? prop.MaterialProperty : GetMaterialProperty(prop, only);
            switch(materialProp.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                case ShaderPropertyType.Int:
                    materialProp.SetNumber(value);
                    break;
                    // If our property is 0f, clear texture
                case ShaderPropertyType.Texture:
                    if(Convert.ToInt32(value) == 0)
                        materialProp.textureValue = null;
                    break;
            }
        }

        static MaterialProperty GetMaterialProperty(ShaderProperty prop, Material only)
        {
            return MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { only }, prop.MaterialProperty.name);
        }

        static void SetFloat(ShaderProperty prop, Material only, float value)
        {
            if (only == null)
            {
                prop.FloatValue = value;
                return;
            }
            // Preset actions write through the editor (all selected materials)
            if (RenderingPresets.PresetPropertyNames.Contains(prop.MaterialProperty.name))
            {
                ShaderEditor.ApplyRenderingPresetToMaterial(only, value);
                return;
            }
            MaterialProperty materialProp = GetMaterialProperty(prop, only);
            materialProp.SetNumber(value);
            if (prop.Keyword != null)
            {
                if (materialProp.GetNumber() == 1) only.EnableKeyword(prop.Keyword);
                else only.DisableKeyword(prop.Keyword);
            }
            ExecuteOnValueActions(prop, materialProp, only);
        }

        static void SetVector(ShaderProperty prop, Material only, Vector4 value)
        {
            if (only == null)
            {
                prop.VectorValue = value;
                return;
            }
            MaterialProperty materialProp = GetMaterialProperty(prop, only);
            materialProp.vectorValue = value;
            ExecuteOnValueActions(prop, materialProp, only);
        }

        static void SetColor(ShaderProperty prop, Material only, Color value)
        {
            if (only == null)
            {
                prop.ColorValue = value;
                return;
            }
            MaterialProperty materialProp = GetMaterialProperty(prop, only);
            materialProp.colorValue = value;
            ExecuteOnValueActions(prop, materialProp, only);
        }

        static void SetTexture(ShaderProperty prop, Material only, Texture texture, Vector4 scaleAndOffset)
        {
            if (only == null)
            {
                prop.TextureValue = texture;
                prop.MaterialProperty.textureScaleAndOffset = scaleAndOffset;
                return;
            }
            MaterialProperty materialProp = GetMaterialProperty(prop, only);
            materialProp.textureValue = texture;
            materialProp.textureScaleAndOffset = scaleAndOffset;
            MaterialEditor.ApplyMaterialPropertyDrawers(only);
        }

        static void ExecuteOnValueActions(ShaderProperty prop, MaterialProperty materialProp, Material only)
        {
            Material[] targets = { only };
            if (prop.Options.on_value_actions != null)
                foreach (PropertyValueAction action in prop.Options.on_value_actions)
                    action?.Execute(materialProp, targets);
            MaterialEditor.ApplyMaterialPropertyDrawers(targets);
        }

        SerializedProperty GetProperty(SerializedObject o, string arrayPath, string propertyName)
        {
            SerializedProperty array = o.FindProperty(arrayPath);
            for(int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).displayName == propertyName)
                {
                    return array.GetArrayElementAtIndex(i);
                }
            }
            return null;
        }

        static List<ShaderTranslator> s_translationDefinitions;
        public static List<ShaderTranslator> TranslationDefinitions
        {
            get
            {
                if (s_translationDefinitions == null)
                    s_translationDefinitions = AssetDatabase.FindAssets("t:" + nameof(ShaderTranslator)).Select(
                        g => AssetDatabase.LoadAssetAtPath<ShaderTranslator>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null).ToList();
                return s_translationDefinitions;
            }
        }

        class TranslationDefinitionWatcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (s_translationDefinitions == null) return;
                if (imported.Concat(deleted).Concat(moved)
                    .Any(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
                    s_translationDefinitions = null;
            }
        }

        public static ShaderTranslator CheckForExistingTranslationFile(Shader origin, Shader target)
        {
            // Both shaders have to match. Without the parentheses "&&" binds tighter than "?:" and the
            // expression degrades to "origin regex only" or "target name only".
            return TranslationDefinitions.FirstOrDefault(t =>
            (t.MatchOriginShaderBasedOnRegex ? Regex.IsMatch(origin.name, t.OriginShaderRegex) : t.OriginShader == origin.name) &&
            (t.MatchTargetShaderBasedOnRegex ? Regex.IsMatch(target.name, t.TargetShaderRegex) : t.TargetShader == target.name));
        }

        public static void SuggestedTranslationButtonGUI(ShaderEditor editor)
        {
            if(editor.SuggestedTranslationDefinition != null)
            {
                GUILayoutUtility.GetRect(0, 5);
                Color backup = GUI.backgroundColor;
                GUI.backgroundColor = Color.green;
                if(GUILayout.Button($"Apply {editor.SuggestedTranslationDefinition.Name}"))
                {
                    editor.ApplySuggestedTranslationDefinition();
                }
                GUI.backgroundColor = backup;
                GUILayoutUtility.GetRect(0, 5);
            }
        }

        public static void TranslationSelectionGUI(Rect r, ShaderEditor editor)
        {
            if (GUILib.ButtonWithCursor(r, Icons.shaders, "Shader Translation"))
            {
                EditorUtility.DisplayCustomMenu(r, TranslationDefinitions.Select(t => new GUIContent(t.Name)).ToArray(), -1, ConfirmTranslationSelection, editor);
            }
        }

        static void ConfirmTranslationSelection(object userData, string[] options, int selected)
        {
            TranslationDefinitions[selected].Apply(userData as ShaderEditor);
        }

        [MenuItem("Assets/Thry/Shaders/New Translator Definition", priority = 380)]
        static void CreateNewTranslationDefinition()
        {
            // This allows you to name your asset before creating it
            Texture2D icon = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
#if UNITY_6000_5_OR_NEWER
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, CreateInstance<DoCreateNewTranslationDefinition>(), "New Translation Definition.asset", icon, null);
#else
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<DoCreateNewTranslationDefinition>(), "New Translation Definition.asset", icon, null);
#endif
        }

#if UNITY_6000_5_OR_NEWER
        class DoCreateNewTranslationDefinition : AssetCreationEndAction
        {
            public override void Action(EntityId instanceId, string pathName, string resourceFile)
            {
                CreateTranslationDefinitionAsset(pathName);
            }
        }
#else
        class DoCreateNewTranslationDefinition : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                CreateTranslationDefinitionAsset(pathName);
            }
        }
#endif

        static void CreateTranslationDefinitionAsset(string pathName)
        {
            var translator = CreateInstance<ShaderTranslator>();
            translator.name = Path.GetFileNameWithoutExtension(pathName);
            AssetDatabase.CreateAsset(translator, pathName);
            Selection.activeObject = translator;
            s_translationDefinitions = null;
        }
    }
}
