using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#endif

namespace Thry.ThryEditor
{
    public class ListTextureUsesPopup : EditorWindow
    {
        private Texture _texture;
        private List<(Material material, string propertyName)> _textureUses;
        private Material _selectedMaterial;
        private string _selectedPropertyName;

        public static void ShowWindow(Texture texture, List<(Material material, string propertyName)> textureUses)
        {
            ListTextureUsesPopup window = GetWindow<ListTextureUsesPopup>("Texture Uses");
            window._texture = texture;
            window._textureUses = textureUses;
#if UNITY_2021_3_OR_NEWER
            window.CreateGUI();
#endif
        }

        private void OnGUI()
        {
#if UNITY_2021_3_OR_NEWER
            if (rootVisualElement.childCount > 0) return;
#endif
            if (_texture == null)
            {
                GUILayout.Label("No texture selected", EditorStyles.boldLabel);
                return;
            }
            GUILayout.Label(_texture, GUILayout.Width(100), GUILayout.Height(100));
            if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.PingObject(_texture);
            }
            if(Event.current.type == EventType.DragPerform)
            {
                if(DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] is Texture)
                {
                    DragAndDrop.AcceptDrag();
                    _texture = DragAndDrop.objectReferences[0] as Texture;
                    FindReferencesAndOpenEditor(_texture);
                }
            }
            // Make drag accept mouse icon
            if (Event.current.type == EventType.DragPerform || Event.current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
            }
            
            GUILayout.Space(10);
            GUILayout.Label("Texture Uses:", EditorStyles.boldLabel);
            if(_textureUses.Count == 0)
            {
                GUILayout.Label("No uses found");
            }
            else
            {
                float width = EditorGUIUtility.currentViewWidth / 2;
                foreach((Material material, string propertyName) in _textureUses)
                {
                    GUILayout.BeginHorizontal();
                    // Material preview
                    
                    if(GUILayout.Button(material.name, GUILayout.Width(width)))
                    {
                        EditorGUIUtility.PingObject(material);
                    }
                    if(GUILayout.Button(propertyName, GUILayout.Width(width)))
                    {
                        _selectedMaterial = material;
                        _selectedPropertyName = propertyName;
                        Selection.activeObject = material;
                    }
                    GUILayout.EndHorizontal();
                }
            }

            if(ShaderEditor.Active?.Materials[0] == _selectedMaterial && _selectedPropertyName != null)
            {
                ShaderEditor.Active?.SetSearchTerm(_selectedPropertyName);
                _selectedMaterial = null;
                _selectedPropertyName = null;
            }
        }

#if UNITY_2021_3_OR_NEWER
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
            root.schedule.Execute(() =>
            {
                if (ShaderEditor.Active == null || _selectedMaterial == null || ShaderEditor.Active.Materials[0] != _selectedMaterial) return;
                ShaderEditor.Active.SetSearchTerm(_selectedPropertyName); _selectedMaterial = null; _selectedPropertyName = null;
            }).Every(150);
        }
#endif
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
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            List<(Material material, string propertyName)> textureUses = new List<(Material, string)>();
            // Search files for references
            foreach (Material material in materials)
            {
                string path = AssetDatabase.GetAssetPath(material);
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    for(int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].IndexOf(guid, StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            textureUses.Add((material, lines[i-1].Substring(6, lines[i-1].Length - 7)));
                        }
                    }
                }
            }
            ListTextureUsesPopup.ShowWindow(texture, textureUses);
        }
    }
}
