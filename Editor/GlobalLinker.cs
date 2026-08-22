/// Global Links
///
/// String-based linking system that keeps Material sections hooked to the same JSON string to share
/// the same properties - consistently and reliably. Intended to serve as a spiritual replacement
/// to Material Linking, as Global Linking does not rely on requiring Materials to be linked to one
/// another. Rather, it relies on a singular string to keep material sections linked together.
///
/// Hardened to work against changes from the user regardless if Materials are locked or not. If
/// a Globally-Linked Material is in a Locked Material, it will instantly update with the values
/// stored in the JSON if any changes were made. Additionally, allows APIs to hook to it.
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

using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
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
        // A/RA state, which lives in a material override tag rather than in the property, so it has to be
        // carried explicitly. "" = not animated, "1" = A, "2" = RA. See ShaderOptimizer.SetAnimatedTag.
        public string animatedTag;
        // False for links captured before A/RA was tracked. Those have no recorded tag, so applying them
        // leaves the target's existing tag alone rather than clearing it.
        public bool hasAnimatedTag;
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

    [InitializeOnLoad]
    public class GlobalLinker
    {
        private static List<GlobalLink> s_data;

        static GlobalLinker()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private static void OnUndoRedoPerformed()
        {
            Load();
            bool dirty = false;
            foreach (GlobalLink link in s_data)
            {
                if (link == null || link.properties == null || link.subscribedMaterialGuids == null) continue;

                Material truth = null;
                foreach (string guid in link.subscribedMaterialGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m != null) { truth = m; break; }
                }
                if (truth == null) continue;

                if (RecaptureFromMaterial(link, truth)) dirty = true;
            }
            if (dirty) Save();
        }

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
            Material[] selected = section.MaterialProperty.targets.Cast<Material>().ToArray();
            return CreateLink(name, sectionPropertyName, section, selected);
        }

        public static GlobalLink CreateLink(string name, string sectionPropertyName, ShaderGroup section, IEnumerable<Material> materials)
        {
            Load();

            GlobalLink link = new GlobalLink();
            link.name = name;
            link.sectionPropertyName = sectionPropertyName;
            CapturePropertiesFromSection(link, section);

            List<string> guids = link.subscribedMaterialGuids.ToList();
            foreach (Material m in materials)
            {
                if (m == null) continue;
                string guid = UnityHelper.GetGUID(m);
                if (!guids.Contains(guid)) guids.Add(guid);
            }
            link.subscribedMaterialGuids = guids.ToArray();

            s_data.Add(link);
            Save();
            return link;
        }

        public static void Subscribe(GlobalLink link, Material material, bool applyLinkToMaterial)
        {
            Subscribe(link, new[] { material }, applyLinkToMaterial);
        }

        public static void Subscribe(GlobalLink link, IEnumerable<Material> materials, bool applyLinkToMaterial)
        {
            Load();

            List<string> guids = link.subscribedMaterialGuids.ToList();
            foreach (Material material in materials)
            {
                if (material == null) continue;

                // Force-switch: if this material is already in a different link for the same section, drop it from that link first.
                GlobalLink existing = GetLinkForMaterial(material, link.sectionPropertyName);
                if (existing != null && existing != link)
                {
                    string existingGuid = UnityHelper.GetGUID(material);
                    existing.subscribedMaterialGuids = existing.subscribedMaterialGuids.Where(g => g != existingGuid).ToArray();
                    if (existing.subscribedMaterialGuids.Length == 0) s_data.Remove(existing);
                }

                string guid = UnityHelper.GetGUID(material);
                if (!guids.Contains(guid)) guids.Add(guid);

                if (applyLinkToMaterial) ApplyLinkToMaterial(link, material, recordUndo: true);
            }
            link.subscribedMaterialGuids = guids.ToArray();

            Save();
            RequestRepaint();
        }

        public static void Unsubscribe(Material material, string sectionPropertyName)
        {
            Unsubscribe(new[] { material }, sectionPropertyName);
        }

        public static void Unsubscribe(IEnumerable<Material> materials, string sectionPropertyName)
        {
            Load();
            bool changed = false;
            foreach (Material material in materials)
            {
                if (material == null) continue;
                GlobalLink link = GetLinkForMaterial(material, sectionPropertyName);
                if (link == null) continue;

                string guid = UnityHelper.GetGUID(material);
                link.subscribedMaterialGuids = link.subscribedMaterialGuids.Where(g => g != guid).ToArray();
                if (link.subscribedMaterialGuids.Length == 0) s_data.Remove(link);
                changed = true;
            }
            if (changed) Save();
        }

        public static void DeleteLink(GlobalLink link)
        {
            Load();
            s_data.Remove(link);
            Save();
        }

        /// <param name="reloadUI">
        /// Leave true for one-off actions (paste, reset, presets) that replace the inspected material's values wholesale.
        /// Pass false from per-frame draw hooks: a reload there discards runtime-only UI state such as expanded texture foldouts.
        /// </param>
        public static void OnSectionChanged(ShaderGroup section, bool reloadUI = true)
        {
            if (ShaderEditor.Active == null) return;
            if (ShaderEditor.Active.IsInAnimationMode) return;

            Material self = (Material)section.MaterialProperty.targets[0];
            string sectionPropName = section.MaterialProperty.name;

            GlobalLink link = GetLinkForMaterial(self, sectionPropName);
            if (link == null) return;

            // Change checks fire for UI-only interactions too (foldouts, focus changes). Bail before
            // writing to disk or recording undos when no value in the section actually moved.
            if (!CapturePropertiesFromSection(link, section)) return;
            Save();

            string selfGuid = UnityHelper.GetGUID(self);
            foreach (string subscriberGuid in link.subscribedMaterialGuids)
            {
                if (subscriberGuid == selfGuid) continue;
                string path = AssetDatabase.GUIDToAssetPath(subscriberGuid);
                Material target = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (target != null) ApplyLinkToMaterial(link, target, recordUndo: true);
            }
            RequestRepaint(reloadUI);
        }

        /// <summary>
        /// Propagates an edit made to a single property outside the draw loop's change check - a context menu
        /// reset, for example - to whichever globally linked section contains it. No-op if no ancestor is linked.
        /// </summary>
        public static void OnPropertyChanged(ShaderPart part)
        {
            if (part == null) return;
            for (ShaderPart ancestor = part.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor is ShaderGroup group && group.MaterialProperty != null) OnSectionChanged(group);
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
                if (target != null) ApplyLinkToMaterial(link, target, recordUndo: true);
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
                ApplyLinkToMaterial(link, material, recordUndo: false);
            }
        }

        private static bool RecaptureFromMaterial(GlobalLink link, Material material)
        {
            bool changed = false;
            foreach (GlobalLinkPropertyValue pv in link.properties)
            {
                if (!material.HasProperty(pv.name)) continue;
                switch (pv.type)
                {
                    case "Float":
                        float f = material.GetFloat(pv.name);
                        if (pv.floatValue != f) { pv.floatValue = f; changed = true; }
                        break;
                    case "Int":
                        #if UNITY_2022_1_OR_NEWER
                        int i = material.GetInteger(pv.name);
                        #else
                        int i = (int)material.GetFloat(pv.name);
                        #endif
                        if (pv.intValue != i) { pv.intValue = i; changed = true; }
                        break;
                    case "Color":
                        Color c = material.GetColor(pv.name);
                        if (pv.colorValue == null || pv.colorValue.Length != 4 ||
                            pv.colorValue[0] != c.r || pv.colorValue[1] != c.g ||
                            pv.colorValue[2] != c.b || pv.colorValue[3] != c.a)
                        {
                            pv.colorValue = new float[] { c.r, c.g, c.b, c.a };
                            changed = true;
                        }
                        break;
                    case "Vector":
                        Vector4 v = material.GetVector(pv.name);
                        if (pv.vectorValue == null || pv.vectorValue.Length != 4 ||
                            pv.vectorValue[0] != v.x || pv.vectorValue[1] != v.y ||
                            pv.vectorValue[2] != v.z || pv.vectorValue[3] != v.w)
                        {
                            pv.vectorValue = new float[] { v.x, v.y, v.z, v.w };
                            changed = true;
                        }
                        break;
                    case "Texture":
                        Texture tex = material.GetTexture(pv.name);
                        string newGuid = tex != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex)) : "";
                        if (pv.textureGuid != newGuid) { pv.textureGuid = newGuid; changed = true; }
                        Vector2 scale = material.GetTextureScale(pv.name);
                        Vector2 offset = material.GetTextureOffset(pv.name);
                        if (pv.textureScaleAndOffset == null || pv.textureScaleAndOffset.Length != 4 ||
                            pv.textureScaleAndOffset[0] != scale.x || pv.textureScaleAndOffset[1] != scale.y ||
                            pv.textureScaleAndOffset[2] != offset.x || pv.textureScaleAndOffset[3] != offset.y)
                        {
                            pv.textureScaleAndOffset = new float[] { scale.x, scale.y, offset.x, offset.y };
                            changed = true;
                        }
                        break;
                }
            }
            return changed;
        }

        /// <summary>
        /// Recaptures the section into the link. Returns true if any captured value differs from what the link already held.
        /// </summary>
        private static bool CapturePropertiesFromSection(GlobalLink link, ShaderGroup section)
        {
            List<GlobalLinkPropertyValue> captured = new List<GlobalLinkPropertyValue>();
            CaptureRecursive(captured, section);
            bool changed = !IsSamePropertySet(link.properties, captured);
            link.properties = captured.ToArray();
            return changed;
        }

        private static bool IsSamePropertySet(GlobalLinkPropertyValue[] stored, List<GlobalLinkPropertyValue> captured)
        {
            if (stored == null) return captured.Count == 0;
            if (stored.Length != captured.Count) return false;
            for (int i = 0; i < stored.Length; i++)
            {
                if (!IsSameProperty(stored[i], captured[i])) return false;
            }
            return true;
        }

        private static bool IsSameProperty(GlobalLinkPropertyValue a, GlobalLinkPropertyValue b)
        {
            if (a == null || b == null) return a == b;
            if (a.name != b.name || a.type != b.type) return false;

            // Toggling A/RA changes nothing about the value, so without this the link would never notice
            // and the new state would not reach subscribers.
            if (a.hasAnimatedTag != b.hasAnimatedTag) return false;
            if (a.hasAnimatedTag && (a.animatedTag ?? "") != (b.animatedTag ?? "")) return false;

            switch (a.type)
            {
                case "Float": return a.floatValue == b.floatValue;
                case "Int": return a.intValue == b.intValue;
                case "Color": return IsSameFloatArray(a.colorValue, b.colorValue);
                case "Vector": return IsSameFloatArray(a.vectorValue, b.vectorValue);
                case "Texture":
                    return (a.textureGuid ?? "") == (b.textureGuid ?? "")
                        && IsSameFloatArray(a.textureScaleAndOffset, b.textureScaleAndOffset);
            }
            return true;
        }

        private static bool IsSameFloatArray(float[] a, float[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static void CaptureRecursive(List<GlobalLinkPropertyValue> captured, ShaderGroup group)
        {
            CaptureRecursive(captured, group, new HashSet<string>());
        }

        private static void CaptureRecursive(List<GlobalLinkPropertyValue> captured, ShaderGroup group, HashSet<string> seenNames)
        {
            foreach (ShaderPart child in group.Children)
            {
                if (child.MaterialProperty != null && seenNames.Add(child.MaterialProperty.name))
                {
                    GlobalLinkPropertyValue pv = CaptureProperty(child.MaterialProperty);
                    if (pv != null) captured.Add(pv);
                    else seenNames.Remove(child.MaterialProperty.name);
                }
                if (child is ShaderGroup childGroup) CaptureRecursive(captured, childGroup, seenNames);
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

            pv.animatedTag = ShaderOptimizer.GetAnimatedTag(prop);
            pv.hasAnimatedTag = true;

            return pv;
        }

        private static void ApplyLinkToMaterial(GlobalLink link, Material material, bool recordUndo)
        {
            if (recordUndo) Undo.RecordObject(material, "Update Global Link \"" + link.name + "\"");
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

                // Carried separately from the value above, since A/RA is a material tag and not part of
                // the property itself.
                if (pv.hasAnimatedTag)
                    material.SetOverrideTag(pv.name + ShaderOptimizer.AnimatedTagSuffix, pv.animatedTag ?? "");
            }
            EditorUtility.SetDirty(material);
            MaterialEditor.ApplyMaterialPropertyDrawers(material);
        }

        /// <param name="reloadUI">
        /// Rebuilds the whole ShaderEditor UI. Needed when the inspected material's own values were replaced from
        /// outside the UI, but it resets runtime-only state (expanded texture foldouts, drawer caches), so skip it
        /// when only other subscribers were written to.
        /// </param>
        private static void RequestRepaint(bool reloadUI = true)
        {
            if (reloadUI) ShaderEditor.ReloadActive();
            else ShaderEditor.RepaintActive();
            SceneView.RepaintAll();
        }

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
            private Material[] _materials;
            private string _sectionPropertyName;
            private string _newLinkName = "";
            private Vector2 _scrollPos;
            private List<GlobalLink> _availableLinks;
            private GlobalLink _currentLink;       // non-null only when ALL selected materials share the same link
            private bool _hasMixedState;           // true when selected materials are in inconsistent link states
            private int _linkedCount;              // number of selected materials currently linked (for any link) for this section

            private Material PrimaryMaterial => _materials != null && _materials.Length > 0 ? _materials[0] : null;

            public void Init(ShaderGroup section)
            {
                _section = section;
                _materials = section.MaterialProperty.targets.Cast<Material>().Where(m => m != null).ToArray();
                _sectionPropertyName = section.MaterialProperty.name;
                titleContent = new GUIContent("Global Links");
                RefreshState();
            }

            private void RefreshState()
            {
                _availableLinks = GetLinksForSection(_sectionPropertyName);

                _currentLink = null;
                _hasMixedState = false;
                _linkedCount = 0;

                if (_materials == null || _materials.Length == 0) return;

                GlobalLink firstLink = GetLinkForMaterial(_materials[0], _sectionPropertyName);
                bool uniform = true;
                if (firstLink != null) _linkedCount++;

                for (int i = 1; i < _materials.Length; i++)
                {
                    GlobalLink l = GetLinkForMaterial(_materials[i], _sectionPropertyName);
                    if (l != null) _linkedCount++;
                    if (l != firstLink) uniform = false;
                }

                if (uniform) _currentLink = firstLink;
                else _hasMixedState = true;
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
                if (_hasMixedState)
                {
                    EditorGUILayout.HelpBox($"Mixed — {_linkedCount} of {_materials.Length} selected materials are linked.", MessageType.Warning);
                    if (GUILayout.Button("Disconnect All"))
                    {
                        Unsubscribe(_materials, _sectionPropertyName);
                        RefreshState();
                    }
                    GUILayout.Space(4);
                }
                else if (_currentLink != null)
                {
                    string selectionSuffix = _materials.Length > 1 ? $" — {_materials.Length} selected" : "";
                    EditorGUILayout.HelpBox($"Linked to: \"{_currentLink.name}\" ({_currentLink.subscribedMaterialGuids.Length} material(s)){selectionSuffix}", MessageType.Info);
                    if (GUILayout.Button("Disconnect"))
                    {
                        Unsubscribe(_materials, _sectionPropertyName);
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
                        // Drop any existing links on the selected materials first (force-switch)
                        Unsubscribe(_materials, _sectionPropertyName);

                        GlobalLink newLink = CreateLink(_newLinkName, _sectionPropertyName, _section, _materials);
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
                bool linkHasProperties = link.properties.Length > 0;
                bool isMultiSelect = _materials != null && _materials.Length > 1;

                if (linkHasProperties)
                {
                    if (isMultiSelect)
                    {
                        // Override is ambiguous across multiple materials - only offer Apply / Cancel.
                        bool apply = EditorUtility.DisplayDialog(
                            "Sync Properties",
                            $"This Global Link \"{link.name}\" already has stored properties.\n\n" +
                            $"Applying will overwrite the section properties of all {_materials.Length} selected materials with the link's stored values.",
                            "Use Link's properties",
                            "Cancel"
                        );
                        if (!apply)
                        {
                            RefreshState();
                            return;
                        }
                        Subscribe(link, _materials, applyLinkToMaterial: true);
                    }
                    else
                    {
                        Material single = PrimaryMaterial;
                        // Link already has stored values - prompt
                        int choice = EditorUtility.DisplayDialogComplex(
                            "Sync Properties",
                            $"This Global Link \"{link.name}\" already has stored properties.\n\n" +
                            $"How would you like to sync it?",
                            "Use Link's properties",                                    // 0 = Apply Link -> This Material
                            "Cancel",                                                   // 1 = Cancel
                            $"Override with \"{single.name}\"'s current properties."    // 2 = Overwrite Link from this Material
                        );
                        if (choice == 0)
                        {
                            Subscribe(link, single, applyLinkToMaterial: true);
                        }
                        else if (choice == 2)
                        {
                            Subscribe(link, single, applyLinkToMaterial: false);
                            OverwriteLinkFromSection(link, _section);
                        }
                        else
                        {
                            RefreshState();
                            return;
                        }
                    }
                }
                else
                {
                    // Link is empty (shouldn't normally happen since CreateLink captures, but guard anyway)
                    Subscribe(link, _materials, applyLinkToMaterial: false);
                    OverwriteLinkFromSection(link, _section);
                }

                RefreshState();
            }
        }
    }
}
