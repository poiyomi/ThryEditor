using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    #region Data Model

    [Serializable]
    public class GlobalLinkPropertyValue
    {
        public string name;
        public string type; // Float, Int, Color, Vector, Texture
        public float floatValue;
        public int intValue;
        public float[] colorValue; // r, g, b, a
        public float[] vectorValue; // x, y, z, w
        public string textureGuid; // Asset GUID for Textures
        public float[] textureScaleAndOffset; // scaleX, scaleY, offsetX, offsetY
    }

    [Serializable]
    public class GlobalLink
    {
        public string name;
        public string sectionPropertyName; // e.g. "m_start_Shading"
        public GlobalLinkPropertyValue[] properties = new GlobalLinkPropertyValue[0];
        public string[] subscribedMaterialGuids = new string[0];
    }

    [Serializable]
    public class GlobalLinksData
    {
        public GlobalLink[] links = new GlobalLink[0];
    }

    #endregion

    public class GlobalLinker
    {

        #region Storage

        private static List<GlobalLink> s_data;

        private static void Load()
        {
            if (s_data != null) return;
            string raw = FileHelper.ReadFileIntoString(PATH.GLOBAL_LINKS_FILE);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                GlobalLinksData parsed = Parser.Deserialize<GlobalLinksData>(raw);
                if (parsed?.links != null) s_data = new List<GlobalLink>(parsed.links);
            }
            if (s_data == null) s_data = new List<GlobalLink>();
        }

        private static void Save()
        {
            GlobalLinksData data = new GlobalLinksData();
            data.links = s_data.ToArray();
            FileHelper.WriteStringToFile(Parser.Serialize(data, prettyPrint: true), PATH.GLOBAL_LINKS_FILE);
        }

        public static void InvalidateCache()
        {
            s_data = null;
        }

        #endregion

        #region Public API

        public static List<GlobalLink> GetLinksForSection(string sectionPropertyName)
        {
            Load();
            return s_data.Where(l => l != null && l.sectionPropertyName == sectionPropertyName).ToList();
        }

        public static List<GlobalLink> GetAllLinks()
        {
            Load();
            return s_data;
        }

        public static GlobalLink GetLinkForMaterial(Material material, string sectionPropertyName)
        {
            Load();
            string guid = UnityHelper.GetGUID(material);
            return s_data.FirstOrDefault(l => l != null && l.sectionPropertyName == sectionPropertyName && l.subscribedMaterialGuids != null && l.subscribedMaterialGuids.Contains(guid));
        }

        public static bool IsGloballyLinked(Material material, string sectionPropertyName)
        {
            return GetLinkForMaterial(material, sectionPropertyName) != null;
        }

        public static GlobalLink CreateLink(string name, string sectionPropertyName, ShaderGroup section)
        {
            Load();

            GlobalLink link = new GlobalLink();
            link.name = name;
            link.sectionPropertyName = sectionPropertyName;
            CapturePropertiesFromSection(link, section);

            Material self = (Material)section.MaterialProperty.targets[0];
            string guid = UnityHelper.GetGUID(self);
            if (!link.subscribedMaterialGuids.Contains(guid)) link.subscribedMaterialGuids = link.subscribedMaterialGuids.Append(guid).ToArray();

            s_data.Add(link);
            Save();
            return link;
        }

        public static void Subscribe(GlobalLink link, Material material, bool applyLinkToMaterial)
        {
            Load();
            string guid = UnityHelper.GetGUID(material);
            if (!link.subscribedMaterialGuids.Contains(guid)) link.subscribedMaterialGuids = link.subscribedMaterialGuids.Append(guid).ToArray();

            if (applyLinkToMaterial) ApplyLinkToMaterial(link, material);

            Save();
            RequestRepaint();
        }

        public static void Unsubscribe(Material material, string sectionPropertyName)
        {
            Load();
            GlobalLink link = GetLinkForMaterial(material, sectionPropertyName);
            if (link == null) return;

            string guid = UnityHelper.GetGUID(material);
            link.subscribedMaterialGuids = link.subscribedMaterialGuids.Where(g => g != guid).ToArray();
            if (link.subscribedMaterialGuids.Length == 0) s_data.Remove(link);

            Save();
        }

        public static void DeleteLink(GlobalLink link)
        {
            Load();
            s_data.Remove(link);
            Save();
        }

        public static void OnSectionChanged(ShaderGroup section)
        {
            if (ShaderEditor.Active == null) return;
            if (ShaderEditor.Active.IsInAnimationMode) return;

            Material self = (Material)section.MaterialProperty.targets[0];
            string sectionPropName = section.MaterialProperty.name;

            GlobalLink link = GetLinkForMaterial(self, sectionPropName);
            if (link == null) return;

            CapturePropertiesFromSection(link, section);
            Save();

            string selfGuid = UnityHelper.GetGUID(self);
            foreach (string subscriberGuid in link.subscribedMaterialGuids)
            {
                if (subscriberGuid == selfGuid) continue;
                string path = AssetDatabase.GUIDToAssetPath(subscriberGuid);
                Material target = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (target != null) ApplyLinkToMaterial(link, target);
            }
        }

        public static void OverwriteLinkFromSection(GlobalLink link, ShaderGroup section)
        {
            CapturePropertiesFromSection(link, section);
            Save();

            Material self = (Material)section.MaterialProperty.targets[0];
            string selfGuid = UnityHelper.GetGUID(self);
            foreach (string subscriberGuid in link.subscribedMaterialGuids)
            {
                if (subscriberGuid == selfGuid) continue;
                string path = AssetDatabase.GUIDToAssetPath(subscriberGuid);
                Material target = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (target != null) ApplyLinkToMaterial(link, target);
            }
            RequestRepaint();
        }

        public static void PropagateAfterPreset(ShaderEditor shaderEditor, Material preset, ShaderPart parent)
        {
            if (shaderEditor.IsInAnimationMode) return;

            if (!Presets.IsMaterialSectionedPreset(preset))
            {
                foreach (ShaderPart part in shaderEditor.ShaderParts)
                {
                    if (part is ShaderGroup group && Presets.IsPreset(preset, part))
                    {
                        Material self = (Material)group.MaterialProperty.targets[0];
                        GlobalLink link = GetLinkForMaterial(self, group.MaterialProperty.name);
                        if (link != null) OnSectionChanged(group);
                    }
                }
            }
            else if (parent is ShaderGroup group)
            {
                Material self = (Material)group.MaterialProperty.targets[0];
                GlobalLink link = GetLinkForMaterial(self, group.MaterialProperty.name);
                if (link != null) OnSectionChanged(group);
            }
        }

        public static void ApplyAllLinksToMaterial(Material material)
        {
            Load();
            string guid = UnityHelper.GetGUID(material);
            foreach (GlobalLink link in s_data)
            {
                if (link == null || link.subscribedMaterialGuids == null) continue;
                if (!link.subscribedMaterialGuids.Contains(guid)) continue;
                ApplyLinkToMaterial(link, material);
            }
        }

        #endregion

        #region Property Capture & Apply

        private static void CapturePropertiesFromSection(GlobalLink link, ShaderGroup section)
        {
            List<GlobalLinkPropertyValue> captured = new List<GlobalLinkPropertyValue>();
            CaptureRecursive(captured, section);
            link.properties = captured.ToArray();
        }

        private static void CaptureRecursive(List<GlobalLinkPropertyValue> captured, ShaderGroup group)
        {
            foreach (ShaderPart child in group.Children)
            {
                if (child.MaterialProperty != null)
                {
                    GlobalLinkPropertyValue pv = CaptureProperty(child.MaterialProperty);
                    if (pv != null)captured.Add(pv);
                }
                if (child is ShaderGroup childGroup) CaptureRecursive(captured, childGroup);
            }
        }

        private static GlobalLinkPropertyValue CaptureProperty(MaterialProperty prop)
        {
            GlobalLinkPropertyValue pv = new GlobalLinkPropertyValue();
            pv.name = prop.name;

            switch (prop.GetPropertyType())
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    pv.type = "Float";
                    pv.floatValue = prop.floatValue;
                    break;
                #if UNITY_2022_1_OR_NEWER
                case ShaderPropertyType.Int:
                    pv.type = "Int";
                    pv.intValue = prop.intValue;
                    break;
                #endif
                case ShaderPropertyType.Color:
                    pv.type = "Color";
                    pv.colorValue = new float[]
                    {
                        prop.colorValue.r,
                        prop.colorValue.g,
                        prop.colorValue.b,
                        prop.colorValue.a
                    };
                    break;
                case ShaderPropertyType.Vector:
                    pv.type = "Vector";
                    pv.vectorValue = new float[]
                    {
                        prop.vectorValue.x,
                        prop.vectorValue.y,
                        prop.vectorValue.z,
                        prop.vectorValue.w
                    };
                    break;
                case ShaderPropertyType.Texture:
                    pv.type = "Texture";
                    if (prop.textureValue != null) pv.textureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prop.textureValue));
                    else pv.textureGuid = "";
                    Vector4 tso = prop.textureScaleAndOffset;
                    pv.textureScaleAndOffset = new float[]
                    {
                        tso.x,
                        tso.y,
                        tso.z,
                        tso.w
                    };
                    break;
                default:
                    return null;
            }
            return pv;
        }

        private static void ApplyLinkToMaterial(GlobalLink link, Material material)
        {
            foreach (GlobalLinkPropertyValue pv in link.properties)
            {
                if (!material.HasProperty(pv.name)) continue;

                switch (pv.type)
                {
                    case "Float":
                        material.SetFloat(pv.name, pv.floatValue);
                        break;
                    case "Int":
                        #if UNITY_2022_1_OR_NEWER
                        material.SetInteger(pv.name, pv.intValue);
                        #else
                        material.SetFloat(pv.name, pv.intValue);
                        #endif
                        break;
                    case "Color":
                        if (pv.colorValue != null && pv.colorValue.Length == 4) material.SetColor(pv.name, new Color(pv.colorValue[0], pv.colorValue[1], pv.colorValue[2], pv.colorValue[3]));
                        break;
                    case "Vector":
                        if (pv.vectorValue != null && pv.vectorValue.Length == 4) material.SetVector(pv.name, new Vector4(pv.vectorValue[0], pv.vectorValue[1], pv.vectorValue[2], pv.vectorValue[3]));
                        break;
                    case "Texture":
                        Texture tex = null;
                        if (!string.IsNullOrEmpty(pv.textureGuid))
                        {
                            string texPath = AssetDatabase.GUIDToAssetPath(pv.textureGuid);
                            tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                        }
                        material.SetTexture(pv.name, tex);
                        if (pv.textureScaleAndOffset != null && pv.textureScaleAndOffset.Length == 4)
                        {
                            material.SetTextureScale(pv.name, new Vector2(pv.textureScaleAndOffset[0], pv.textureScaleAndOffset[1]));
                            material.SetTextureOffset(pv.name, new Vector2(pv.textureScaleAndOffset[2], pv.textureScaleAndOffset[3]));
                        }
                        break;
                }
            }
            EditorUtility.SetDirty(material);
            MaterialEditor.ApplyMaterialPropertyDrawers(material);
        }

        private static void RequestRepaint()
        {
            ShaderEditor.ReloadActive();
            SceneView.RepaintAll();
        }

        #endregion

        #region Popup UI

        private static GlobalLinkerPopupWindow s_window;

        public static void Popup(ShaderGroup section)
        {
            Vector2 pos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            pos.x = Mathf.Min(EditorWindow.focusedWindow.position.x + EditorWindow.focusedWindow.position.width - 300, pos.x);
            pos.y = Mathf.Min(EditorWindow.focusedWindow.position.y + EditorWindow.focusedWindow.position.height - 250, pos.y);

            if (s_window != null) s_window.Close();
            s_window = ScriptableObject.CreateInstance<GlobalLinkerPopupWindow>();
            s_window.position = new Rect(pos.x, pos.y, 300, 250);
            s_window.Init(section);
            s_window.ShowUtility();
        }

        private class GlobalLinkerPopupWindow : EditorWindow
        {
            private ShaderGroup _section;
            private Material _material;
            private string _sectionPropertyName;
            private string _newLinkName = "";
            private Vector2 _scrollPos;
            private List<GlobalLink> _availableLinks;
            private GlobalLink _currentLink;

            public void Init(ShaderGroup section)
            {
                _section = section;
                _material = (Material)section.MaterialProperty.targets[0];
                _sectionPropertyName = section.MaterialProperty.name;
                titleContent = new GUIContent("Global Links");
                RefreshState();
            }

            private void RefreshState()
            {
                _availableLinks = GetLinksForSection(_sectionPropertyName);
                _currentLink = GetLinkForMaterial(_material, _sectionPropertyName);
            }

            void OnGUI()
            {
                if (_section == null)
                {
                    Close();
                    return;
                }

                // Header
                GUILayout.Label("Global Links", EditorStyles.boldLabel);
                GUILayout.Space(4);

                // Current Status
                if (_currentLink != null)
                {
                    EditorGUILayout.HelpBox($"Linked to: \"{_currentLink.name}\" ({_currentLink.subscribedMaterialGuids.Length} material(s))", MessageType.Info);
                    if (GUILayout.Button("Disconnect"))
                    {
                        Unsubscribe(_material, _sectionPropertyName);
                        RefreshState();
                    }
                    GUILayout.Space(4);
                }

                // Available Links List
                GUILayout.Label("Available Links:", EditorStyles.miniBoldLabel);
                float listMaxHeight = position.height - 180;
                _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(listMaxHeight));

                if (_availableLinks.Count == 0)
                {
                    GUILayout.Label("No global links exist for this section yet.", EditorStyles.miniLabel);
                }
                else
                {
                    for (int i = _availableLinks.Count - 1; i >= 0; i--)
                    {
                        GlobalLink link = _availableLinks[i];
                        GUILayout.BeginHorizontal();

                        bool isCurrent = _currentLink == link;
                        string label = link.name + $" ({link.subscribedMaterialGuids.Length})";

                        EditorGUI.BeginDisabledGroup(isCurrent);
                        if (GUILayout.Button(isCurrent ? "● " + label : label, EditorStyles.miniButtonLeft)) SelectLink(link);
                        EditorGUI.EndDisabledGroup();

                        // Delete Button
                        if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24)))
                        {
                            if (EditorUtility.DisplayDialog("Delete Global Link", $"Delete \"{link.name}\"?\n\nAll materials linked to it will be disconnected. Their current properties will be retained.", "Delete", "Cancel"))
                            {
                                DeleteLink(link);
                                RefreshState();
                            }
                        }

                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.EndScrollView();
                GUILayout.Space(4);

                // Create New Link
                GUILayout.Label("Create New Link:", EditorStyles.miniBoldLabel);
                GUILayout.BeginHorizontal();
                _newLinkName = EditorGUILayout.TextField(_newLinkName);
                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_newLinkName));
                if (GUILayout.Button("Add", GUILayout.Width(50)))
                {
                    // Check for duplicates
                    if (_availableLinks.Any(l => l.name == _newLinkName))
                    {
                        EditorUtility.DisplayDialog("Duplicate Name", $"A global link named \"{_newLinkName}\" already exists for this section.", "OK");
                    }
                    else
                    {
                        // If currently linked to something else, disconnect first
                        if (_currentLink != null) Unsubscribe(_material, _sectionPropertyName);

                        GlobalLink newLink = CreateLink(_newLinkName, _sectionPropertyName, _section);
                        _newLinkName = "";
                        RefreshState();
                    }
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // Done button
                if (GUILayout.Button("Done")) Close();
            }

            private void SelectLink(GlobalLink link)
            {
                // If already linked to something else, disconnect first
                if (_currentLink != null) Unsubscribe(_material, _sectionPropertyName);

                bool linkHasProperties = link.properties.Length > 0;

                if (linkHasProperties)
                {
                    // Link already has stored values - prompt
                    int choice = EditorUtility.DisplayDialogComplex(
                        "Sync Properties",
                        $"This Global Link \"{link.name}\" already has stored properties.\n\n" +
                        $"How would you like to sync it?",
                        "Use Link's properties",                                        // 0 = Apply Link -> This Material
                        "Cancel",                                                       // 1 = Cancel
                        $"Override with \"{_material.name}\"'s current properties."     // 2 = Overwrite Link from this Material
                    );
                    if (choice == 0)
                    {
                        Subscribe(link, _material, applyLinkToMaterial: true);
                    }
                    else if (choice == 2)
                    {
                        Subscribe(link, _material, applyLinkToMaterial: false);
                        OverwriteLinkFromSection(link, _section);
                    }
                    else
                    {
                        RefreshState();
                        return;
                    }
                }
                else
                {
                    // Link is empty (shouldn't normally happen since CreateLink captures, but guard anyway)
                    Subscribe(link, _material, applyLinkToMaterial: false);
                    OverwriteLinkFromSection(link, _section);
                }

                RefreshState();
            }
        }

        #endregion
    }
}