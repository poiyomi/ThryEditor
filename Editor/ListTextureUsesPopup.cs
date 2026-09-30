using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Thry.ThryEditor
{
    public class ListTextureUsesPopup : EditorWindow
    {
        private Texture _texture;
        private List<(Material material, string propertyName)> _textureUses;
        private Material _selectedMaterial;
        private string _selectedPropertyName;
        private IVisualElementScheduledItem _searchHandOff;

        public static void ShowWindow(Texture texture, List<(Material material, string propertyName)> textureUses)
        {
            ListTextureUsesPopup window = GetWindow<ListTextureUsesPopup>("Texture Uses");
            window._texture = texture;
            window._textureUses = textureUses;
            window.CreateGUI();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            var texture = new ObjectField("Texture") { objectType = typeof(Texture), allowSceneObjects = false, value = _texture };
            texture.RegisterValueChangedCallback(e => { if (e.newValue != null) FindReferencesAndOpenEditor(e.newValue as Texture); }); root.Add(texture);
            var search = RetainedWindow.Search("Find material or property…"); root.Add(search);
            var list = new ScrollView(); list.style.flexGrow = 1; root.Add(list);
            if (_textureUses == null || _textureUses.Count == 0) list.Add(new Label("No loaded materials use this texture."));
            else foreach (var use in _textureUses)
            {
                var row = new VisualElement(); row.AddToClassList("thry-components"); list.Add(row);
                var material = new Button(() => EditorGUIUtility.PingObject(use.material)) { text = use.material.name }; material.style.flexGrow = 1; row.Add(material);
                var property = new Button(() => { _selectedMaterial = use.material; _selectedPropertyName = use.propertyName; Selection.activeObject = use.material; }) { text = ObjectNames.NicifyVariableName(use.propertyName.TrimStart('_')) }; property.style.flexGrow = 1; row.Add(property);
                search.RegisterValueChangedCallback(e => row.style.display = (use.material.name + use.propertyName).IndexOf(e.newValue, StringComparison.OrdinalIgnoreCase) >= 0 ? DisplayStyle.Flex : DisplayStyle.None);
            }
            // CreateGUI runs again for every new texture; Clear does not cancel scheduled items, so register the hand-off once.
            if (_searchHandOff == null) _searchHandOff = root.schedule.Execute(() =>
            {
                if (ShaderEditor.Active == null || _selectedMaterial == null || ShaderEditor.Active.Materials[0] != _selectedMaterial) return;
                ShaderEditor.Active.SetSearchTerm(_selectedPropertyName); _selectedMaterial = null; _selectedPropertyName = null;
            }).Every(150);
        }
        [MenuItem("Assets/Thry/Textures/Find Uses", true)]
        private static bool FindReferencesValidate()
        {
            return Selection.activeObject is Texture;
        }

        [MenuItem("Assets/Thry/Textures/Find Uses", false, 304)]
        private static void FindReferences()
        {
            Texture texture = Selection.activeObject as Texture;
            FindReferencesAndOpenEditor(texture);
        }

        private static void FindReferencesAndOpenEditor(Texture texture)
        {
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            List<(Material material, string propertyName)> textureUses = new List<(Material, string)>();
            // Check each material's own saved textures. Several materials can share one asset file, and
            // saved entries the current shader no longer declares still count as uses.
            foreach (Material material in materials)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material))) continue;
                using (SerializedObject serializedMaterial = new SerializedObject(material))
                {
                    SerializedProperty texEnvs = serializedMaterial.FindProperty("m_SavedProperties.m_TexEnvs");
                    if (texEnvs == null || !texEnvs.isArray) continue;
                    for (int i = 0; i < texEnvs.arraySize; i++)
                    {
                        SerializedProperty entry = texEnvs.GetArrayElementAtIndex(i);
                        if (entry.FindPropertyRelative("second.m_Texture").objectReferenceValue == texture)
                            textureUses.Add((material, entry.FindPropertyRelative("first").stringValue));
                    }
                }
            }
            ListTextureUsesPopup.ShowWindow(texture, textureUses);
        }
    }
}
