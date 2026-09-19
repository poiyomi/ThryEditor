/// Material Blueprint Generator
/// 
/// Allows the creation of Materials with a predetermined set of instructions and presets pre-applied
/// upon Material creation. Leverages the Thry Editor Presets system by both applying the preferred
/// Rendering Preset and a list of Presets to the Material upon creation. When generated, no unbound
/// properties and no unbound textures exist.
///
/// Extendable by developers for automated Material creation in workflows so that creators can save
/// time without having to manually apply each Preset they want manually after creating a Material.
///
/// Script designed by BluWizard LABS, licensed for exclusive usage in Thry Editor.
///
/// Copyright (c) 2026 BluWizard LABS. All Rights Reserved.
///
/// MIT License
/// 
/// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
/// and associated documentation files (the "Software"), to deal in the Software without restriction,
/// including without limitation the rights to use, copy, modify, merge, publish, distribute,
/// sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
/// furnished to do so, subject to the following conditions:
///
/// The above copyright notice and this permission notice shall be included in all copies or
/// substantial portions of the Software.
/// 
/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
/// BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
/// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
/// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
/// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    public class Blueprint : ScriptableObject
    {
        const string TAG_POSTFIX_IS_PROPERTY_PRESET = "_isPreset";

        [Tooltip("The shader to assign to the new material.")]
        public Shader TargetShader;

        [Tooltip("The rendering preset value to apply (maps to the _Mode property). Set to -1 to skip.")]
        public float RenderingPresetValue = -1;

        [Tooltip("Ordered list of preset names to apply. Applied from top to bottom.")]
        public List<string> PresetNames = new List<string>();

        [Tooltip("Ordered list of section preset entries to apply. Each entry is stored as 'collectionKey/presetName'. Applied from top to bottom, after full presets.")]
        public List<string> SectionPresetEntries = new List<string>();

        public static bool TryParseSectionEntry(string entry, out string collectionKey, out string presetName)
        {
            collectionKey = null;
            presetName = null;

            if (string.IsNullOrEmpty(entry)) return false;

            int sep = entry.IndexOf('/');
            if (sep < 0 || sep == entry.Length - 1) return false;

            collectionKey = entry.Substring(0, sep);
            presetName = entry.Substring(sep + 1);

            return true;
        }

        public string CreateMaterial(string savePath)
        {
            if (TargetShader == null)
            {
                EditorUtility.DisplayDialog("Blueprint Error", "No Target Shader assigned to the Blueprint.", "OK");
                return null;
            }

            // Resolve Preset Names to Materials
            List<(string name, Material mat)> resolved = new List<(string, Material)>();
            foreach (string presetName in PresetNames)
            {
                if (string.IsNullOrEmpty(presetName)) continue;

                string guid = Presets.GetFullPresetGuid(presetName);
                if (guid == null)
                {
                    EditorUtility.DisplayDialog("Blueprint Error", $"Preset '{presetName}' was not found in the preset cache.\n\n" + "It may have been renamed or deleted. Please update the Blueprint.", "OK");
                    return null;
                }

                Material presetMat = Presets.GetPresetMaterial(guid);
                if (presetMat == null)
                {
                    EditorUtility.DisplayDialog("Blueprint Error", $"Could not load the material for preset '{presetName}'.", "OK");
                    return null;
                }

                resolved.Add((presetName, presetMat));
            }

            // Create the Material
            Material newMaterial = new Material(TargetShader);
            newMaterial.name = Path.GetFileNameWithoutExtension(savePath);

            // Apply Rendering Preset _Mode if specified
            // The on_value_actions are parsed from the shader's property attributes
            // and only execute through ThryEditor's ShaderProperty.FloatValue setter.
            // ShaderEditor.ApplyRenderingPresetToMaterial temporarily spins up a
            // ShaderEditor context so that the full side-effect chain runs.
            if (RenderingPresetValue >= 0) ShaderEditor.ApplyRenderingPresetToMaterial(newMaterial, RenderingPresetValue);

            // Apply each Preset in order
            foreach (var (name, presetMat) in resolved)
            {
                ApplyPresetToMaterial(presetMat, newMaterial);
            }

            // Resolve and apply Section Presets in order
            foreach (string entry in SectionPresetEntries)
            {
                if (!TryParseSectionEntry(entry, out string collectionKey, out string presetName)) continue;

                string guid = Presets.GetSectionPresetGuid(collectionKey, presetName);
                if (guid == null)
                {
                    EditorUtility.DisplayDialog("Blueprint Error", $"Section Preset '{presetName}' was not found.\n\n" + "It may have been renamed or deleted. Please update the Blueprint.", "OK");
                    return null;
                }

                Material sectionPresetMat = Presets.GetPresetMaterial(guid);
                if (sectionPresetMat == null)
                {
                    EditorUtility.DisplayDialog("Blueprint Error", $"Could not load the material for section preset '{presetName}'.", "OK");
                    return null;
                }

                ApplySectionPresetToMaterial(sectionPresetMat, newMaterial, collectionKey);
            }

            // Fix Keywords after all Presets are applied
            ShaderEditor.FixKeywords(new Material[] { newMaterial });

            // Final Pass of ApplyMaterialPropertyDrawers to ensure all drawers are consistent
            MaterialEditor.ApplyMaterialPropertyDrawers(newMaterial);

            // Save the Asset
            AssetDatabase.CreateAsset(newMaterial, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            return savePath;
        }

        static void ApplyPresetToMaterial(Material preset, Material target)
        {
            if (preset == null || target == null) return;

            // Temporarily swap the Preset's shader to the target's shader
            // so that Property Names align (same approach as Presets.ApplyPresetInternal)
            Shader prevPresetShader = preset.shader;
            preset.shader = target.shader;

            Shader shader = target.shader;
            int propCount = shader.GetPropertyCount();

            for (int i = 0; i < propCount; i++)
            {
                string propName = shader.GetPropertyName(i);

                // Check if this property is tagged as a preset property
                bool isPresetProp = preset.GetTag(propName + TAG_POSTFIX_IS_PROPERTY_PRESET, false, "") == "true";
                if (!isPresetProp) continue;

                // Copy the values from the preset to target based on property type
                ShaderPropertyType propType = shader.GetPropertyType(i);
                CopyProperty(preset, target, propName, propType);
                CopyAnimatedTag(preset, target, propName);
            }

            // Also check for header/section properties
            string[] serializedFloatProps = MaterialHelper.GetFloatPropertiesFromSerializedObject(preset);
            foreach (string propName in serializedFloatProps)
            {
                // Skip properties we already handled above
                if (shader.FindPropertyIndex(propName) >= 0) continue;

                bool isPresetProp = preset.GetTag(propName + TAG_POSTFIX_IS_PROPERTY_PRESET, false, "") == "true";
                if (!isPresetProp) continue;

                // Float and Header properties
                if (target.HasProperty(propName)) target.SetFloat(propName, preset.GetFloat(propName));

                // Copy the animation tags for Header/Section properties too, if supported.
                CopyAnimatedTag(preset, target, propName);
            }

            // Restore Preset's original shader
            preset.shader = prevPresetShader;
        }

        static void ApplySectionPresetToMaterial(Material preset, Material target, string collectionKey)
        {
            ApplyPresetToMaterial(preset, target);
        }

        static void CopyProperty(Material source, Material target, string propName, ShaderPropertyType propType)
        {
            if (!source.HasProperty(propName) || !target.HasProperty(propName)) return;

            switch (propType)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    target.SetFloat(propName, source.GetFloat(propName));
                    break;
                case ShaderPropertyType.Int:
                    target.SetInt(propName, source.GetInt(propName));
                    break;
                case ShaderPropertyType.Color:
                    target.SetColor(propName, source.GetColor(propName));
                    break;
                case ShaderPropertyType.Vector:
                    target.SetVector(propName, source.GetVector(propName));
                    break;
                case ShaderPropertyType.Texture:
                    target.SetTexture(propName, source.GetTexture(propName));
                    target.SetTextureOffset(propName, source.GetTextureOffset(propName));
                    target.SetTextureScale(propName, source.GetTextureScale(propName));
                    break;
            }
        }

        static void CopyAnimatedTag(Material source, Material target, string propName)
        {
            string animatedTag = source.GetTag(propName + ShaderOptimizer.AnimatedTagSuffix, false, "");
            if (!string.IsNullOrEmpty(animatedTag)) target.SetOverrideTag(propName + ShaderOptimizer.AnimatedTagSuffix, animatedTag);
        }

        #region Asset Creation Menu

        [MenuItem("Assets/Thry/Shaders/New Material Blueprint", priority = 381)]
        static void CreateNewBlueprint()
        {
            Texture2D icon = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
#if UNITY_6000_5_OR_NEWER
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, CreateInstance<DoCreateNewBlueprint>(), "New Material Blueprint.asset", icon, null);
#else
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<DoCreateNewBlueprint>(), "New Material Blueprint.asset", icon, null);
#endif
        }

#if UNITY_6000_5_OR_NEWER
        class DoCreateNewBlueprint : AssetCreationEndAction
        {
            public override void Action(EntityId instanceId, string pathName, string resourceFile)
            {
                CreateBlueprintAsset(pathName);
            }
        }
#else
        class DoCreateNewBlueprint : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                CreateBlueprintAsset(pathName);
            }
        }
#endif

        static void CreateBlueprintAsset(string pathName)
        {
            var blueprint = CreateInstance<Blueprint>();
            blueprint.name = Path.GetFileNameWithoutExtension(pathName);
            AssetDatabase.CreateAsset(blueprint, pathName);
            AssetDatabase.Refresh();
            Selection.activeObject = blueprint;
        }

        [MenuItem("Assets/Thry/Shaders/Create Material from Blueprint", priority = 382)]
        static void CreateMaterialFromBlueprintMenu()
        {
            Blueprint blueprint = Selection.activeObject as Blueprint;
            if (blueprint == null)
            {
                EditorUtility.DisplayDialog("Blueprint Error", "Please select a Material Blueprint asset.", "OK");
                return;
            }
            CreateMaterialFromBlueprintAsset(blueprint);
        }

        [MenuItem("Assets/Thry/Shaders/Create Material from Blueprint", true)]
        static bool CreateMaterialFromBlueprintMenuValidation()
        {
            return Selection.activeObject is Blueprint;
        }

        public static Material CreateMaterialFromBlueprintAsset(Blueprint blueprint, string savePath = null)
        {
            if (blueprint == null) return null;

            if (string.IsNullOrEmpty(savePath))
            {
                string blueprintPath = AssetDatabase.GetAssetPath(blueprint);
                string defaultFolder = string.IsNullOrEmpty(blueprintPath) ? "Assets" : Path.GetDirectoryName(blueprintPath);
                string defaultName = blueprint.name + " Material";

                savePath = EditorUtility.SaveFilePanelInProject("Save new Material", defaultName, "mat", "Choose where to save the new material.", defaultFolder);

                if (string.IsNullOrEmpty(savePath)) return null;
            }

            string createdPath = blueprint.CreateMaterial(savePath);
            if (createdPath != null)
            {
                Material created = AssetDatabase.LoadAssetAtPath<Material>(createdPath);
                if (created != null)
                {
                    Selection.activeObject = created;
                    EditorGUIUtility.PingObject(created);
                }
                return created;
            }
            return null;
        }
        #endregion
    }

    [CustomEditor(typeof(Blueprint))]
    public class BlueprintEditor : Editor
    {
        SerializedProperty _targetShaderProp;
        SerializedProperty _renderingPresetValueProp;
        SerializedProperty _presetNamesProp;
        SerializedProperty _sectionPresetEntriesProp;

        ReorderableList _presetList;
        ReorderableList _sectionPresetList;
        string[] _shaderNames;
        int _selectedShaderIndex = -1;
        string[] _availablePresetNames;
        string[] _sectionPresetDisplayLabels;
        string[] _sectionPresetStoredValues;

        Dictionary<string, string> _collectionKeyToDisplayName = new Dictionary<string, string>();

        // Rendering Preset mode names for display
        // These are the standard Poiyomi rendering modes.
        // If the shader defines different options via ThryWideEnum, the user
        // sees the Float value and can consult the shader for the mapping.
        static readonly string[] s_renderingModeLabels = new string[]
        {
            "Skip (Don't Set)",
            "Opaque",
            "Cutout",
            "Fade",
            "Transparent",
            "Additive",
            "Soft Additive",
            "Multiplicative",
            "2x Multiplicative",
            "TransClipping",
            "Custom (enter value)"
        };

        static readonly float[] s_dropdownIndexToModeValue = new float[]
        {
            -1, 0, 1, 2, 3, 4, 5, 6, 7, 9, -99
        };

        bool _useCustomRenderingValue = false;

        void OnEnable()
        {
            _targetShaderProp = serializedObject.FindProperty(nameof(Blueprint.TargetShader));
            _renderingPresetValueProp = serializedObject.FindProperty(nameof(Blueprint.RenderingPresetValue));
            _presetNamesProp = serializedObject.FindProperty(nameof(Blueprint.PresetNames));
            _sectionPresetEntriesProp = serializedObject.FindProperty(nameof(Blueprint.SectionPresetEntries));

            // Build shader name list
            _shaderNames = ShaderUtil.GetAllShaderInfo().Select(s => s.name).Where(s => !s.StartsWith("Hidden/")).Where(s => { Shader sh = Shader.Find(s); return sh != null && ShaderHelper.IsShaderUsingThryEditor(sh); }).OrderBy(s => s).ToArray();

            // Determine current shader selection index
            Shader currentShader = _targetShaderProp.objectReferenceValue as Shader;
            if (currentShader != null) _selectedShaderIndex = System.Array.IndexOf(_shaderNames, currentShader.name);

            // Determine rendering mode selection state
            float currentVal = _renderingPresetValueProp.floatValue;
            _useCustomRenderingValue = currentVal >= 0 && RenderingValueToDropdownIndex(currentVal) == s_renderingModeLabels.Length - 1;

            RefreshAvailablePresets();

            // Setup the Reorderable list for presets
            _presetList = new ReorderableList(serializedObject, _presetNamesProp, true, true, true, true);
            _presetList.drawHeaderCallback = DrawPresetListHeader;
            _presetList.drawElementCallback = DrawPresetListElement;
            _presetList.elementHeight = EditorGUIUtility.singleLineHeight + 4;
            _presetList.onAddCallback = OnPresetAdded;

            // Setup the Reorderable list for section presets
            _sectionPresetList = new ReorderableList(serializedObject, _sectionPresetEntriesProp, true, true, true, true);
            _sectionPresetList.drawHeaderCallback = DrawSectionPresetListHeader;
            _sectionPresetList.drawElementCallback = DrawSectionPresetListElement;
            _sectionPresetList.elementHeight = EditorGUIUtility.singleLineHeight + 4;
            _sectionPresetList.onAddCallback = OnSectionPresetAdded;
        }

        void RefreshAvailablePresets()
        {
            _availablePresetNames = Presets.GetFullPresetNames().ToArray();
            RefreshSectionPresetData();
        }

        void RefreshSectionPresetData()
        {
            _collectionKeyToDisplayName.Clear();
            Shader targetShader = _targetShaderProp.objectReferenceValue as Shader;
            if (targetShader != null)
            {
                int propCount = targetShader.GetPropertyCount();
                for (int i = 0; i < propCount; i++)
                {
                    string propName = targetShader.GetPropertyName(i);
                    // Section collections use header property names (m_start_xxx, m_xxx, etc.)
                    string displayName = targetShader.GetPropertyDescription(i);
                    // Strip ThryEditor options from display name (everything after "--")
                    int optIdx = displayName.IndexOf("--", System.StringComparison.Ordinal);
                    if (optIdx >= 0) displayName = displayName.Substring(0, optIdx).Trim();
                    if (!string.IsNullOrEmpty(displayName)) _collectionKeyToDisplayName[propName] = displayName;
                }
            }

            List<string> labels = new List<string>();
            List<string> values = new List<string>();

            List<string> collectionKeys = Presets.GetSectionCollectionKeys();
            foreach (string key in collectionKeys.OrderBy(k => GetSectionDisplayName(k)))
            {
                string sectionDisplay = GetSectionDisplayName(key);
                List<string> presetNames = Presets.GetSectionPresetNames(key);
                foreach (string presetName in presetNames)
                {
                    labels.Add($"{sectionDisplay}/{presetName}");
                    values.Add($"{key}/{presetName}");
                }
            }

            _sectionPresetDisplayLabels = labels.ToArray();
            _sectionPresetStoredValues = values.ToArray();
        }

        string GetSectionDisplayName(string collectionKey)
        {
            if (_collectionKeyToDisplayName.TryGetValue(collectionKey, out string displayName)) return displayName;
            return collectionKey;
        }

        string GetSectionEntryDisplayLabel(string storedValue)
        {
            if (Blueprint.TryParseSectionEntry(storedValue, out string key, out string name)) return $"{GetSectionDisplayName(key)}/{name}";
            return storedValue;
        }

        void DrawPresetListHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "List of Presets to Apply");
        }

        void DrawPresetListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = _presetNamesProp.GetArrayElementAtIndex(index);
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            float labelWidth = 30f;
            float buttonWidth = rect.width - labelWidth - 4f;

            // Index label
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            EditorGUI.LabelField(labelRect, $"#{index + 1}");

            // Preset dropdown button
            Rect buttonRect = new Rect(rect.x + labelWidth + 4f, rect.y, buttonWidth, rect.height);
            string currentName = element.stringValue;

            // Determine display text and style
            string displayText;
            bool isValid;
            if (string.IsNullOrEmpty(currentName))
            {
                displayText = "(Select a Preset)";
                isValid = false;
            }
            else if (System.Array.IndexOf(_availablePresetNames, currentName) < 0)
            {
                displayText = currentName + " [MISSING]";
                isValid = false;
            }
            else
            {
                displayText = currentName;
                isValid = true;
            }

            // Draw as a popup-style button that opens EditorUtility.DisplayCustomMenu
            // This matches the style used by the existing Presets system
            GUIStyle popupStyle = new GUIStyle(EditorStyles.popup);
            if (!isValid && !string.IsNullOrEmpty(currentName)) popupStyle.normal.textColor = new Color(1f, 0.4f, 0.2f);

            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(displayText), FocusType.Keyboard, popupStyle))
            {
                GUIContent[] menuItems = _availablePresetNames.Select(n => new GUIContent(n)).ToArray();

                int currentSelected = System.Array.IndexOf(_availablePresetNames, currentName);

                EditorUtility.DisplayCustomMenu(buttonRect, menuItems, currentSelected, (userData, options, selected) =>
                {
                    SerializedProperty prop = userData as SerializedProperty;
                    prop.serializedObject.Update();
                    prop.stringValue = options[selected];
                    prop.serializedObject.ApplyModifiedProperties();
                },
                element);
            }
        }

        void OnPresetAdded(ReorderableList list)
        {
            int index = list.serializedProperty.arraySize;
            list.serializedProperty.InsertArrayElementAtIndex(index);
            SerializedProperty newElement = list.serializedProperty.GetArrayElementAtIndex(index);
            newElement.stringValue = "";
        }

        void DrawSectionPresetListHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "List of Section Presets to Apply");
        }

        void DrawSectionPresetListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = _sectionPresetEntriesProp.GetArrayElementAtIndex(index);
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            float labelWidth = 30f;
            float buttonWidth = rect.width - labelWidth - 4f;

            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            EditorGUI.LabelField(labelRect, $"#{index + 1}");

            Rect buttonRect = new Rect(rect.x + labelWidth + 4f, rect.y, buttonWidth, rect.height);
            string storedValue = element.stringValue;

            string displayText;
            bool isValid;
            if (string.IsNullOrEmpty(storedValue))
            {
                displayText = "(Select a Section Preset)";
                isValid = false;
            }
            else
            {
                int idx = System.Array.IndexOf(_sectionPresetStoredValues, storedValue);
                if (idx < 0)
                {
                    displayText = GetSectionEntryDisplayLabel(storedValue) + " [MISSING]";
                    isValid = false;
                }
                else
                {
                    displayText = _sectionPresetDisplayLabels[idx];
                    isValid = true;
                }
            }

            GUIStyle popupStyle = new GUIStyle(EditorStyles.popup);
            if (!isValid && !string.IsNullOrEmpty(storedValue)) popupStyle.normal.textColor = new Color(1f, 0.4f, 0.2f);

            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(displayText), FocusType.Keyboard, popupStyle))
            {
                GUIContent[] menuItems = _sectionPresetDisplayLabels.Select(n => new GUIContent(n)).ToArray();

                int currentSelected = System.Array.IndexOf(_sectionPresetStoredValues, storedValue);

                EditorUtility.DisplayCustomMenu(buttonRect, menuItems, currentSelected, (userData, options, selected) =>
                {
                    SerializedProperty prop = userData as SerializedProperty;
                    prop.serializedObject.Update();
                    // Store the internal value (collectionKey/presetName), not the display label
                    prop.stringValue = _sectionPresetStoredValues[selected];
                    prop.serializedObject.ApplyModifiedProperties();
                },
                element);
            }
        }

        void OnSectionPresetAdded(ReorderableList list)
        {
            int index = list.serializedProperty.arraySize;
            list.serializedProperty.InsertArrayElementAtIndex(index);
            SerializedProperty newElement = list.serializedProperty.GetArrayElementAtIndex(index);
            newElement.stringValue = "";
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Blueprint blueprint = target as Blueprint;

            // === Header ===
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Material Blueprint", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scriptable Object Blueprint that generates a fresh Material with a predetermined configuration automatically applied in advance.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4);

            // === Target Shader ===
            EditorGUILayout.LabelField("Shader", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _selectedShaderIndex = EditorGUILayout.Popup(new GUIContent("Target Shader", "Due to the feature set used by this script, ONLY shaders that use ThryEditor (such as Poiyomi Shaders) are eligible to be selected."), _selectedShaderIndex, _shaderNames);
            if (EditorGUI.EndChangeCheck() && _selectedShaderIndex >= 0 && _selectedShaderIndex < _shaderNames.Length)
            {
                Shader found = Shader.Find(_shaderNames[_selectedShaderIndex]);
                _targetShaderProp.objectReferenceValue = found;
                RefreshAvailablePresets();
            }

            EditorGUILayout.Space(10);

            // === Rendering Preset ===
            EditorGUILayout.LabelField("Rendering Preset", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox("Select which Rendering Preset you wish to be applied when the Material is generated from this Blueprint. To skip this step, set it to 'Skip'.", MessageType.Info);

            float currentValue = _renderingPresetValueProp.floatValue;
            int dropdownIndex = RenderingValueToDropdownIndex(currentValue);

            EditorGUI.BeginChangeCheck();
            int newDropdownIndex = EditorGUILayout.Popup("Rendering Mode", dropdownIndex, s_renderingModeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                if (newDropdownIndex == s_renderingModeLabels.Length - 1)
                {
                    _useCustomRenderingValue = true;
                }
                else
                {
                    _renderingPresetValueProp.floatValue = s_dropdownIndexToModeValue[newDropdownIndex];
                    _useCustomRenderingValue = false;
                }
            }

            if (_useCustomRenderingValue) EditorGUILayout.PropertyField(_renderingPresetValueProp, new GUIContent("Custom _Mode Value"));

            if (_targetShaderProp.objectReferenceValue != null)
            {
                Shader s = _targetShaderProp.objectReferenceValue as Shader;
                if (s != null && s.FindPropertyIndex(ShaderEditor.PROPERTY_NAME_IN_SHADER_PRESETS) < 0) EditorGUILayout.HelpBox($"The selected shader does not have a '{ShaderEditor.PROPERTY_NAME_IN_SHADER_PRESETS}' property. " + "The Rendering Preset value will be ignored.", MessageType.Warning);
            }

            EditorGUILayout.Space(20);

            // === Full Preset List ===
            EditorGUILayout.LabelField("Full Presets", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox("When a Material is generated from this Blueprint, Presets will be applied in descending order (Top to Bottom).", MessageType.Info);

            // Refresh Button + Rebuild Cache Button + Count
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{_availablePresetNames.Length} full presets available", EditorStyles.miniLabel);
            if (GUILayout.Button("Refresh List", EditorStyles.miniButton, GUILayout.Width(85))) RefreshAvailablePresets();
            if (GUILayout.Button("Rebuild Cache", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                Presets.RebuildCache();
                RefreshAvailablePresets();
            }
            EditorGUILayout.EndHorizontal();

            _presetList.DoLayoutList();

            // Warn about missing entries
            bool hasMissing = false;
            for (int i = 0; i < _presetNamesProp.arraySize; i++)
            {
                string name = _presetNamesProp.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(name) && System.Array.IndexOf(_availablePresetNames, name) < 0)
                {
                    hasMissing = true;
                    break;
                }
            }
            if (hasMissing) EditorGUILayout.HelpBox("One or more presets in this list are marked [MISSING]. " + "They may have been renamed or deleted. Try clicking 'Refresh List' or update the entries.", MessageType.Warning);

            EditorGUILayout.Space(10);

            // === Section Preset List ===
            EditorGUILayout.LabelField("Sectioned Presets", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox("Section Presets apply to specific shader features (e.g. a single module like Emission or Outlines). They are applied after Full Presets, in descending order.", MessageType.Info);

            if (_targetShaderProp.objectReferenceValue == null) EditorGUILayout.HelpBox("Select a Target Shader to see section presets with friendly display names.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{_sectionPresetDisplayLabels.Length} section presets available", EditorStyles.miniLabel);
            if (GUILayout.Button("Refresh List", EditorStyles.miniButton, GUILayout.Width(85))) RefreshAvailablePresets();
            if (GUILayout.Button("Rebuild Cache", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                Presets.RebuildCache();
                RefreshAvailablePresets();
            }
            EditorGUILayout.EndHorizontal();

            _sectionPresetList.DoLayoutList();

            // Warn about missing section entries
            bool hasMissingSections = false;
            for (int i = 0; i < _sectionPresetEntriesProp.arraySize; i++)
            {
                string val = _sectionPresetEntriesProp.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(val) && System.Array.IndexOf(_sectionPresetStoredValues, val) < 0)
                {
                    hasMissingSections = true;
                    break;
                }
            }
            if (hasMissingSections) EditorGUILayout.HelpBox("One or more section presets in this list are marked [MISSING]. " + "They may have been renamed or deleted. Try clicking 'Refresh List' or update the entries.", MessageType.Warning);

            EditorGUILayout.Space(12);

            // === Create Material Button ===
            bool canCreate = _targetShaderProp.objectReferenceValue != null;
            EditorGUI.BeginDisabledGroup(!canCreate);

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.fixedHeight = 32;

            if (GUILayout.Button("Create Material from Blueprint", buttonStyle))
            {
                serializedObject.ApplyModifiedProperties();
                Blueprint.CreateMaterialFromBlueprintAsset(blueprint);
                GUIUtility.ExitGUI();
            }

            EditorGUI.EndDisabledGroup();

            if (!canCreate) EditorGUILayout.HelpBox("Assign a Target Shader to enable material creation.", MessageType.Warning);

            serializedObject.ApplyModifiedProperties();
        }

        int RenderingValueToDropdownIndex(float value)
        {
            // Find which dropdown index corresponds to this _Mode value
            if (value < 0) return 0; // Skip

            int intVal = Mathf.RoundToInt(value);
            for (int i = 1; i < s_dropdownIndexToModeValue.Length - 1; i++)
            {
                if (Mathf.RoundToInt(s_dropdownIndexToModeValue[i]) == intVal) return i;
            }

            // Value doesn't match any known mode -> Custom
            _useCustomRenderingValue = true;
            return s_renderingModeLabels.Length - 1;
        }
    }
}