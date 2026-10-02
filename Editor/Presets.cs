using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Thry.ThryEditor.Drawers;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class Presets : AssetPostprocessor
    {
        const string TAG_IS_MATERIAL_PRESET = "isPreset";
        const string TAG_IS_MATERIAL_SECTIONED_PRESET = "isSectionedPreset";
        const string TAG_MATERIAL_PRESET_NAME = "presetName";
        const string TAG_POSTFIX_IS_PROPERTY_PRESET = "_isPreset";
        const string TAG_POSTFIX_SECTION_NAME = "isSectionedPreset"; // Weird name, because of a leagcy bug
        const string FILE_NAME_CACHE = "Thry/preset_cache.txt";
        const string FILE_NAME_KNOWN_MATERIALS = "Thry/presets_known_materials.txt";
        const string PRESET_VERSION = "1.1.2";

        public enum PropertyMode { Excluded, ValueAndAnimation, AnimationOnly }

        struct AppliedPreset
        {
            public string name;
            public Material preset;
            // One snapshot per selected material, in the editor's material order. Reverting a
            // multi-selection from a single snapshot would hand every material the first one's values.
            public Material[] prePresetStates;
            // The selection can change before Revert is clicked
            public Material[] targets;
            public ShaderPart parent;

            public static AppliedPreset Create(string name, Material preset, Material[] currentStates, ShaderPart parent)
            {
                AppliedPreset appliedPreset = new AppliedPreset();
                appliedPreset.name = name;
                appliedPreset.preset = preset;
                appliedPreset.targets = (Material[])currentStates.Clone();
                appliedPreset.prePresetStates = new Material[currentStates.Length];
                for (int i = 0; i < currentStates.Length; i++)
                {
                    appliedPreset.prePresetStates[i] = new Material(currentStates[i]);
                    appliedPreset.prePresetStates[i].name = "Before " + name;
                }
                appliedPreset.parent = parent;
                return appliedPreset;
            }

            public void DestroySnapshots()
            {
                if (prePresetStates == null) return;
                foreach (Material m in prePresetStates)
                    if (m != null) UnityEngine.Object.DestroyImmediate(m);
                prePresetStates = null;
            }
        }
        
        static Comparer<string> s_nameComparer = Comparer<string>.Create((a, b) =>
        {
            // Compare by name, names with more slashes (/) are considered to be more specific
            int aSlashCount = a.Count(c => c == '/');
            int bSlashCount = b.Count(c => c == '/');
            if (aSlashCount > bSlashCount) return -1;
            if (aSlashCount < bSlashCount) return 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        class PresetsCollection
        {
            private SortedDictionary<string, string> _nameToGuid = new SortedDictionary<string, string>(s_nameComparer);
            private Dictionary<string, string> _guidToName = new Dictionary<string, string>();
            public IEnumerable<string> Guids => _nameToGuid.Values;
            public IEnumerable<string> Paths => _nameToGuid.Values.Select(g => AssetDatabase.GUIDToAssetPath(g));
            public IEnumerable<string> Names => _nameToGuid.Keys.OrderBy(s => s, s_nameComparer);
            public int Count => _nameToGuid.Count;

            public void Remove(string guid)
            {
                if (_guidToName.ContainsKey(guid))
                {
                    _nameToGuid.Remove(_guidToName[guid]);
                    _guidToName.Remove(guid);
                }
            }

            public bool Add(string name, string guid)
            {
                if (_nameToGuid.ContainsKey(name))
                {
                    return false;
                }
                if (_guidToName.ContainsKey(guid))
                {
                    return false;
                }
                _nameToGuid[name] = guid;
                _guidToName[guid] = name;
                return true;
            }

            public void AddOrUpdate(string name, string guid)
            {
                // The cache file stores "name;guid"
                name = name.Replace(';', '_');
                if (_guidToName.ContainsKey(guid))
                {
                    _nameToGuid.Remove(_guidToName[guid]);
                }
                if (_nameToGuid.TryGetValue(name, out string previousGuid) && previousGuid != guid)
                {
                    _guidToName.Remove(previousGuid);
                }
                _guidToName[guid] = name;
                _nameToGuid[name] = guid;
            }

            public void RemoveWithoutPath()
            {
                var guids = _guidToName.Keys.Where(k => string.IsNullOrWhiteSpace(AssetDatabase.GUIDToAssetPath(k))).ToList();
                foreach (string guid in guids)
                {
                    _nameToGuid.Remove(_guidToName[guid]);
                    _guidToName.Remove(guid);
                }
            }

            public bool ContainsName(string name)
            {
                return _nameToGuid.ContainsKey(name);
            }

            public string GetGuid(string name)
            {
                return _nameToGuid[name];
            }

            public void Serialize(StringBuilder sb)
            {
                foreach (KeyValuePair<string, string> entry in _nameToGuid)
                {
                    sb.AppendLine($"{entry.Key};{entry.Value}");
                }
            }

            public void AddSerialized(string line)
            {
                // Older caches can have ';' in names; guids never do
                int split = line.LastIndexOf(';');
                if (split < 0) return;
                string name = line.Substring(0, split);
                string guid = line.Substring(split + 1);
                _nameToGuid[name] = guid;
                _guidToName[guid] = name;
            }
        }
        
        public class MaterialsList
        {
            string _filepath;
            HashSet<string> _guids;
            bool _isDirty = false;
            public MaterialsList(string filepath)
            {
                _filepath = filepath;
                _guids = new HashSet<string>();
                if (File.Exists(_filepath))
                {
                    string[] lines = File.ReadAllLines(_filepath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        _guids.Add(line);
                    }
                }
            }

            public int Count => _guids.Count;

            public bool Contains(string guid)
            {
                return _guids.Contains(guid);
            }

            public void SetCollection(IEnumerable<string> guids)
            {
                _guids.Clear();
                _guids.UnionWith(guids.Where(g => !string.IsNullOrEmpty(g)));
                _isDirty = true;
            }

            // Only flags the list dirty when the guid was actually new. Otherwise every material
            // re-import (locking, saving, a preset being edited) rewrites the whole file even
            // though nothing changed.
            public void Add(string guid)
            {
                if (string.IsNullOrEmpty(guid)) return;
                if (_guids.Add(guid)) _isDirty = true;
            }

            public void AddAll(IEnumerable<string> guids)
            {
                foreach (string guid in guids) Add(guid);
            }

            // Legacy name, kept so external callers don't break.
            public void AllAll(IEnumerable<string> guids)
            {
                AddAll(guids);
            }

            public void Save()
            {
                if (_isDirty)
                {
                    FileHelper.CreateFileWithDirectories(_filepath);
                    File.WriteAllLines(_filepath, _guids.ToArray());
                    _isDirty = false;
                }
            }
        }

        static Dictionary<Material, AppliedPreset> s_appliedPresets = new Dictionary<Material, AppliedPreset>();
        static Dictionary<string, Material> s_materalCache;
        static Dictionary<string, PresetsCollection> s_presetCollections;
        static Dictionary<string, PresetsCollection> PresetCollections
        {
            get
            {
                InitializeDataStructures();
                return s_presetCollections;
            }
        }
        static PresetsCollection FullPresets
        {
            get
            {
                InitializeDataStructures();
                return s_presetCollections["_full_"];
            }
        }

        public static MaterialsList KnownMaterials = new MaterialsList(FILE_NAME_KNOWN_MATERIALS);

        static void InitializeDataStructures()
        {
            if (s_presetCollections != null) return;
            s_presetCollections = new Dictionary<string, PresetsCollection>();
            s_presetCollections["_full_"] = new PresetsCollection();
            s_materalCache = new Dictionary<string, Material>();

            if(File.Exists(FILE_NAME_CACHE))
            {
                LoadPresetCache();
            }else
            {
                CreatePresetCache();
            }
        }

        static void ClearCache()
        {
            s_presetCollections.Clear();
            s_presetCollections["_full_"] = new PresetsCollection();
        }

        static void LoadPresetCache()
        {
            string[] lines = File.ReadAllLines(FILE_NAME_CACHE);
            bool isEmpty = lines.Length == 0;
            bool isOutOfDate = !isEmpty && lines[0] != PRESET_VERSION;

            if(isEmpty || isOutOfDate)
            {
                if(isOutOfDate)
                {
                    ThryLogger.LogWarn("Preset cache is out of date, rebuilding...");
                }
                CreatePresetCache();
                return;
            }

            bool nextLineIsPresetsCollectionsName = false;
            string currentCollection = null;
            for(int i = 1; i < lines.Length; i++)
            {
                if(string.IsNullOrWhiteSpace(lines[i]))
                {
                    nextLineIsPresetsCollectionsName = true;
                    continue;
                }
                if(nextLineIsPresetsCollectionsName)
                {
                    nextLineIsPresetsCollectionsName = false;
                    currentCollection = lines[i];
                    s_presetCollections[currentCollection] = new PresetsCollection();
                }else
                {
                    s_presetCollections[currentCollection].AddSerialized(lines[i]);
                }                    
            }
        }

        static void CreatePresetCache()
        {
            // Delete old cache
            ClearCache();
            // Create cache
            // Find all materials
            string[] guids = AssetDatabase.FindAssets("t:material");
            IndexPresets(guids, "Creating Preset Cache", alwaysShowProgress: true);

            KnownMaterials.SetCollection(guids);
            KnownMaterials.Save();
        }

        // Only plain .mat assets can be presets. Materials living inside .fbx/.asset containers
        // share their container's guid, which the cache has no way to address individually, so
        // they are never candidates.
        static bool IsMaterialAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
        }

        // Loads the given material assets and folds any presets among them into the cache.
        // Shared by the full rebuild and the incremental catch-up, so both stay consistent.
        static void IndexPresets(IList<string> guids, string progressTitle, bool alwaysShowProgress = false)
        {
            // No-op once built, but external callers can reach this before anything else touched
            // the caches. (Re-entrant from CreatePresetCache, which runs after the fields are set.)
            InitializeDataStructures();

            List<string> paths = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsMaterialAssetPath(path)) paths.Add(path);
            }

            // A handful of materials shouldn't flash a progress bar across the editor.
            bool showProgress = alwaysShowProgress || paths.Count > 25;
            try
            {
                using (new BatchedCacheSave())
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (showProgress)
                            EditorUtility.DisplayProgressBar(progressTitle, $"Loading material {i + 1}/{paths.Count}", (float)i / paths.Count);
                        Material material = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                        if (material != null && IsPreset(material)) AddPreset(material);
                    }
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
        }

        public static void RebuildCache()
        {
            CreatePresetCache();
        }

        /// <summary>
        /// Registers material assets created by external tooling (build pipelines, avatar
        /// processors, generators) with the preset cache. Without this the cache sees them as
        /// unfamiliar on the next domain reload and has to index them itself. Cheap and safe to
        /// call repeatedly - guids that are already known are ignored.
        /// </summary>
        public static void RegisterMaterials(IEnumerable<string> guids)
        {
            if (guids == null) return;

            List<string> unknown = null;
            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid) || KnownMaterials.Contains(guid)) continue;
                if (unknown == null) unknown = new List<string>();
                unknown.Add(guid);
            }
            if (unknown == null) return;

            IndexPresets(unknown, "Indexing Materials");
            KnownMaterials.AddAll(unknown);
            KnownMaterials.Save();
        }

        public static void RegisterMaterial(string guid)
        {
            RegisterMaterials(new[] { guid });
        }

        static Dictionary<Shader, List<string>> s_headersInShader = new Dictionary<Shader, List<string>>();
        static List<string> GetHeadersInShader(Material m)
        {       
            if(s_headersInShader.ContainsKey(m.shader))
            {
                return s_headersInShader[m.shader];
            }
            string[] props = MaterialHelper.GetFloatPropertiesFromSerializedObject(m);
            return props.Where(p => p.StartsWith("m_", StringComparison.Ordinal)).ToList();
        }

        static int s_saveSuppressionDepth = 0;
        static bool s_saveRequested = false;

        // Add/RemovePreset each write the whole cache file. Wrapping a bulk operation in this
        // collapses those writes into a single one at the end.
        class BatchedCacheSave : IDisposable
        {
            public BatchedCacheSave()
            {
                s_saveSuppressionDepth++;
            }

            public void Dispose()
            {
                if (--s_saveSuppressionDepth > 0) return;
                if (!s_saveRequested) return;
                s_saveRequested = false;
                SaveNow();
            }
        }

        static void Save()
        {
            if (s_saveSuppressionDepth > 0)
            {
                s_saveRequested = true;
                return;
            }
            SaveNow();
        }

        static void SaveNow()
        {
            // Save cache
            FileHelper.CreateFileWithDirectories(FILE_NAME_CACHE);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(PRESET_VERSION);

            foreach(KeyValuePair<string, PresetsCollection> collection in PresetCollections)
            {
                sb.AppendLine();
                sb.AppendLine(collection.Key);
                collection.Value.RemoveWithoutPath();
                collection.Value.Serialize(sb);
            }

            File.WriteAllText(FILE_NAME_CACHE, sb.ToString().TrimEnd('\r', '\n'));
        }
        
        // On Asset Delete remove presets from cache
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Batched so an import touching many materials writes the cache file once instead of
            // twice per preset.
            using (new BatchedCacheSave())
            {
                if(importedAssets.Length > 0)
                {
                    // Check if any presets were imported, iterate over all imported materials
                    foreach (string asset in importedAssets.Where(IsMaterialAssetPath))
                    {
                        Material material = AssetDatabase.LoadAssetAtPath<Material>(asset);
                        if (material == null) continue;
                        string guid = AssetDatabase.AssetPathToGUID(asset);
                        // Skip the log for re-imports triggered by the ShaderOptimizer. Those aren't
                        // user-driven material changes and would otherwise fire for every materials.
                        if (!ShaderOptimizer.ConsumeLockUnlockMaterialChange(guid)) ThryLogger.LogDetail($"Material Changed: {material.name} ({guid})");
                        // Check if asset is preset
                        if (IsPreset(material))
                        {
                            // Add preset
                            RemovePreset(material);
                            AddPreset(material);
                        }
                        KnownMaterials.Add(guid);
                    }
                }

                if(deletedAssets.Length > 0)
                {
                    // go through all preset collections
                    // Guids of all preset materials. Because of sectioned can exists multiples
                    Dictionary<string, string> pathsToGuids = new Dictionary<string, string>();
                    bool missingPresets = false;
                    foreach (string g in PresetCollections.SelectMany(c => c.Value.Guids).Distinct())
                    {
                        // Missing presets all resolve to an empty path
                        string path = AssetDatabase.GUIDToAssetPath(g);
                        if (string.IsNullOrWhiteSpace(path)) missingPresets = true;
                        else if (!pathsToGuids.ContainsKey(path)) pathsToGuids[path] = g;
                    }
                    // Check if any presets were deleted, iterate over all deleted materials
                    bool deletedMaterials = false;
                    foreach (string asset in deletedAssets.Where(IsMaterialAssetPath))
                    {
                        deletedMaterials = true;
                        // Check if asset is preset
                        if (pathsToGuids.ContainsKey(asset))
                        {
                            // Remove preset
                            RemovePreset(pathsToGuids[asset]);
                        }
                    }
                    // Save drops presets that no longer resolve
                    if (deletedMaterials && missingPresets) Save();
                }
            }

            KnownMaterials.Save();
        }

        static void AddPreset(Material material)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
            s_materalCache[guid] = material;
            
            if(IsMaterialSectionedPreset(material))
            {
                // Find sections that are presets
                List<string> headers = GetHeadersInShader(material);
                foreach(string header in headers)
                {
                    if(IsSectionPreset(material, header))
                    {
                        // Add to preset collection
                        string collectionName = header;
                        string name = material.GetTag(header + TAG_POSTFIX_SECTION_NAME, false, "").Replace(';', '_');
                        if(string.IsNullOrEmpty(name))
                        {
                            ThryLogger.LogErr($"Preset {material.name} has no name for section '{header}'");
                            continue;
                        }
                        if(!PresetCollections.ContainsKey(collectionName))
                        {
                            PresetCollections[collectionName] = new PresetsCollection();
                        }
                        
                        if(PresetCollections[collectionName].Add(name, guid))
                        {
                            ThryLogger.LogDetail($"Add preset for section '{header}': {name} ({guid})");
                        }else
                        {
                            ThryLogger.LogWarn($"Preset '{name}' already exists in section '{header}'");
                        }
                    }
                }
            }else
            {
                // Add to full preset collection
                string name = material.GetTag(TAG_MATERIAL_PRESET_NAME, false, material.name).Replace(';', '_');
                if(PresetCollections["_full_"].Add(name, guid))
                {
                    ThryLogger.LogDetail($"Add preset: {name} ({guid})");
                }else
                {
                    ThryLogger.LogWarn($"Preset '{name}' already exists");
                }
            }
            s_materalCache[guid] = material;

            // Save cache
            Save();
        }

        static void RemovePreset(Material material)
        {
            // Get guid
            ThryLogger.LogDetail($"Remove preset: {material.name}");
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
            RemovePreset(guid);
        }

        static void RemovePreset(string guid)
        {
            foreach(PresetsCollection collection in PresetCollections.Values)
            {
                collection.Remove(guid);
            }
            // Save cache
            Save();
        }

        public static Material GetPresetMaterial(string guid)
        {
            // Validation no longer runs during domain reload, so the caches may not be built yet.
            InitializeDataStructures();
            if (s_materalCache.ContainsKey(guid))
            {
                return s_materalCache[guid];
            }
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            s_materalCache[guid] = m;
            return m;
        }

        public static bool DoesPresetExist(string collection, string presetName)
        {
            return PresetCollections.ContainsKey(collection) && PresetCollections[collection].ContainsName(presetName);
        }

        public static List<string> GetFullPresetNames()
        {
            return FullPresets.Names.ToList();
        }

        public static List<string> GetFullPresetGuids()
        {
            return FullPresets.Guids.ToList();
        }

        public static string GetFullPresetGuid(string presetName)
        {
            if (FullPresets.ContainsName(presetName)) return FullPresets.GetGuid(presetName);
            return null;
        }

        public static List<string> GetSectionCollectionKeys()
        {
            return PresetCollections.Keys.Where(k => k != "_full_" && PresetCollections[k].Count > 0).ToList();
        }

        public static List<string> GetSectionPresetNames(string collectionKey)
        {
            if (PresetCollections.ContainsKey(collectionKey)) return PresetCollections[collectionKey].Names.ToList();
            return new List<string>();
        }

        public static string GetSectionPresetGuid(string collectionKey, string presetName)
        {
            if (PresetCollections.ContainsKey(collectionKey) && PresetCollections[collectionKey].ContainsName(presetName)) return PresetCollections[collectionKey].GetGuid(presetName);
            return null;
        }

        private static PresetsPopupGUI window;
        public static void OpenPresetsMenu(Rect r, ShaderEditor shaderEditor, bool forceQuick, string collection = "_full_")
        {
            Event.current.Use();
            
            Vector2 pos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            pos.x = Mathf.Min(EditorWindow.focusedWindow.position.x + EditorWindow.focusedWindow.position.width - 250, pos.x);
            pos.y = Mathf.Min(EditorWindow.focusedWindow.position.y + EditorWindow.focusedWindow.position.height - 200, pos.y);
                
            if (Event.current.button == 0 && !forceQuick)
            {
                if (window != null)
                    window.Close();
                window = ScriptableObject.CreateInstance<PresetsPopupGUI>();
                window.position = new Rect(pos.x, pos.y, 250, 200);
                window.Init(collection, PresetCollections[collection].Names.ToList(), PresetCollections[collection].Guids.ToList(), shaderEditor);
                window.titleContent = new GUIContent("Preset List");
                window.ShowUtility();
            }
            else
            {
                ThryLogger.Log($"Open Quick Presets Menu: {collection} ({PresetCollections[collection].Count} presets)");
                EditorUtility.DisplayCustomMenu(r, 
                    PresetCollections[collection].Names.Select(s => new GUIContent(s)).ToArray(), -1, 
                    ApplyQuickPreset, new object[]{shaderEditor, collection, shaderEditor.CurrentProperty});
            }
        }

        static void ApplyQuickPreset(object userData, string[] options, int selected)
        {
            ThryLogger.Log($"Apply quick preset '{options[selected]}'");
            ShaderEditor shaderEditor = (userData as object[])[0] as ShaderEditor;
            string collection = (userData as object[])[1] as string;
            ShaderPart parent = (userData as object[])[2] as ShaderPart;
            Apply(collection, options[selected], shaderEditor, parent);
        }

        public static void PresetEditorGUI(ShaderEditor shaderEditor)
        {
            if (shaderEditor.IsPresetEditor)
            {
                RectifiedLayout.Seperator();

                EditorGUILayout.LabelField(EditorLocale.editor.Get("preset_material_notify"), Styles.greenStyle);
                EditorGUI.BeginChangeCheck();
                bool isSectionPreset = IsMaterialSectionedPreset(shaderEditor.Materials[0]);
                isSectionPreset = EditorGUILayout.Toggle(EditorLocale.editor.Get("preset_section_preset"), isSectionPreset);
                if(EditorGUI.EndChangeCheck())
                {
                    SetMaterialSectionedPreset(shaderEditor.Materials[0], isSectionPreset);
                }
                if(!isSectionPreset)
                {
                    string name = shaderEditor.Materials[0].GetTag(TAG_MATERIAL_PRESET_NAME, false, "");
                    EditorGUI.BeginChangeCheck();
                    name = EditorGUILayout.DelayedTextField(EditorLocale.editor.Get("preset_name"), name);
                    if (EditorGUI.EndChangeCheck())
                    {
                        InitializeDataStructures();
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shaderEditor.Materials[0]));
                        shaderEditor.Materials[0].SetOverrideTag(TAG_MATERIAL_PRESET_NAME, name);
                        FullPresets.AddOrUpdate(name, guid);
                        Save();
                    }
                }

                RectifiedLayout.Seperator();
                GUILayout.Space(10);
            }
            if (s_appliedPresets.ContainsKey(shaderEditor.Materials[0]))
            {
                const float rowHeight = 22f;
                var rowRect = EditorGUILayout.GetControlRect(false, rowHeight);

                float pad = 2f;
                float gap = 4f;

                var inner = new Rect(rowRect.x + pad, rowRect.y, rowRect.width - pad * 2, rowHeight);
                float half = (inner.width - gap) * 0.5f;

                var left = new Rect(inner.x, inner.y, half, inner.height);
                var right = new Rect(inner.x + half + gap, inner.y, half, inner.height);

                var applied = s_appliedPresets[shaderEditor.Materials[0]];
                string revertLabel = EditorLocale.editor.Get("preset_revert") + applied.name;
                string dismissLabel = EditorLocale.editor.Get("preset_dismiss");

                if (GUI.Button(left, revertLabel))
                {
                    Revert(shaderEditor);
                    shaderEditor.Repaint();
                }

                if (GUI.Button(right, dismissLabel))
                {
                    Dismiss(shaderEditor);
                    shaderEditor.Repaint();
                }
                
                GUILayout.Space(12);
            }
        }

        public static void Apply(string collection, string name, ShaderEditor shaderEditor, ShaderPart parent)
        {
            Material key = shaderEditor.Materials[0];
            string guid = PresetCollections[collection].GetGuid(name);
            Material preset = GetPresetMaterial(guid);

            // Clean up the previous revert snapshot for this material, if any, before replacing it.
            if (s_appliedPresets.TryGetValue(key, out AppliedPreset previous))
            {
                previous.DestroySnapshots();
            }
            s_appliedPresets[key] = AppliedPreset.Create(name, preset, shaderEditor.Materials, parent);
            Undo.RecordObjects(shaderEditor.Materials, "Apply preset");
            ApplyPresetInternal(shaderEditor, preset, preset, parent);
            GlobalLinker.PropagateAfterPreset(shaderEditor, preset, parent);
            PropagateLinkedMaterials(shaderEditor, preset, parent);
            foreach (Material m in shaderEditor.Materials)
            {
                MaterialEditor.ApplyMaterialPropertyDrawers(m);
            }
        }

        static void Revert(ShaderEditor shaderEditor)
        {
            Material key = shaderEditor.Materials[0];
            AppliedPreset appliedPreset = s_appliedPresets[key];
            
            ThryLogger.Log($"Revert '{appliedPreset.preset.name}' from '{key.name}'");
            Material[] materials = shaderEditor.Materials;
            Material[] snapshots = appliedPreset.prePresetStates;
            Undo.RecordObjects(materials, "Revert preset");
            if (materials.Length == 1)
            {
                // Single material: the shared path writes its snapshot through the editor's own property objects.
                int index = Array.IndexOf(appliedPreset.targets, materials[0]);
                if (index >= 0)
                    ApplyPresetInternal(shaderEditor, appliedPreset.preset, snapshots[index], appliedPreset.parent);
            }
            else
            {
                // Multi-selection: the editor's MaterialProperty objects write to every target at once,
                // so each material gets its own snapshot copied through a single-target property instead.
                HashSet<ShaderProperty> affected = new HashSet<ShaderProperty>();
                CollectPresetProperties(shaderEditor, appliedPreset.preset, appliedPreset.parent, affected);
                for (int i = 0; i < materials.Length; i++)
                {
                    int index = Array.IndexOf(appliedPreset.targets, materials[i]);
                    if (index >= 0)
                        CopyPresetPropertiesToMaterial(appliedPreset.preset, materials[i], snapshots[index], affected);
                }
                shaderEditor.Reload();
            }
            GlobalLinker.PropagateAfterPreset(shaderEditor, appliedPreset.preset, appliedPreset.parent);
            PropagateLinkedMaterials(shaderEditor, appliedPreset.preset, appliedPreset.parent);
            foreach (Material m in shaderEditor.Materials)
            {
                MaterialEditor.ApplyMaterialPropertyDrawers(m);
            }
            s_appliedPresets.Remove(key);
            appliedPreset.DestroySnapshots();
        }

        // Mirrors what ApplyPresetInternal would touch, as a flat set of properties.
        internal static void CollectPresetProperties(ShaderEditor shaderEditor, Material preset, ShaderPart parent, HashSet<ShaderProperty> into)
        {
            if (!IsMaterialSectionedPreset(preset))
            {
                foreach (ShaderPart part in shaderEditor.ShaderParts)
                    if (IsPreset(preset, part))
                        CollectPartProperties(shaderEditor, part, copyReferenceProperties: part is ShaderGroup, into, preset);
            }
            else if (parent is ShaderGroup)
            {
                if (IsPreset(preset, parent)) CollectPartProperties(shaderEditor, parent, true, into, preset);
                else CollectPresetPropertiesRecursive(shaderEditor, preset, parent as ShaderGroup, into);
            }
        }

        static void CollectPresetPropertiesRecursive(ShaderEditor shaderEditor, Material preset, ShaderGroup parent, HashSet<ShaderProperty> into)
        {
            foreach (ShaderProperty reference in ReferenceProperties(parent))
                if (IsPreset(preset, reference))
                    CollectPartProperties(shaderEditor, reference, copyReferenceProperties: false, into, preset);
            foreach (ShaderPart part in parent.Children)
            {
                if (part is ShaderGroup)
                    CollectPresetPropertiesRecursive(shaderEditor, preset, part as ShaderGroup, into);
                if (IsPreset(preset, part))
                    CollectPartProperties(shaderEditor, part, copyReferenceProperties: true, into, preset);
            }
        }

        static void CollectPartProperties(ShaderEditor shaderEditor, ShaderPart part, bool copyReferenceProperties,
            HashSet<ShaderProperty> into, Material preset, HashSet<ShaderPart> visited = null)
        {
            if (visited == null) visited = new HashSet<ShaderPart>();
            if (!visited.Add(part)) return;
            if (part is ShaderProperty prop) into.Add(prop);
            // A metadata-only property does not copy its associated values or reference properties.
            if (GetPropertyMode(preset, part) == PropertyMode.AnimationOnly) return;
            if (part is ShaderGroup group)
                foreach (ShaderPart child in group.Children)
                    CollectPartProperties(shaderEditor, child, copyReferenceProperties, into, preset, visited);
            if (!copyReferenceProperties) return;
            if (part.Options.reference_properties != null)
                foreach (string name in part.Options.reference_properties)
                    if (shaderEditor.PropertyDictionary.TryGetValue(name, out ShaderProperty reference))
                        CollectPartProperties(shaderEditor, reference, true, into, preset, visited);
            if (!string.IsNullOrWhiteSpace(part.Options.reference_property)
                && shaderEditor.PropertyDictionary.TryGetValue(part.Options.reference_property, out ShaderProperty singleReference))
                CollectPartProperties(shaderEditor, singleReference, true, into, preset, visited);
        }

        static void CopyPresetPropertiesToMaterial(Material preset, Material target, Material source, HashSet<ShaderProperty> properties)
        {
            UnityEngine.Object[] targets = { target };
            foreach (ShaderProperty property in properties)
            {
                if (property.MaterialProperty == null || !target.HasProperty(property.MaterialProperty.name)) continue;
                MaterialProperty single = MaterialEditor.GetMaterialProperty(targets, property.MaterialProperty.name);
                if (single == null) continue;
                if (GetPropertyMode(preset, property) != PropertyMode.AnimationOnly)
                {
                    MaterialHelper.CopyValue(source, single);
                    TileLabelUtility.CopyTileLabelTag(source, single);
                }
                if (property.IsAnimatable) ShaderOptimizer.CopyAnimatedTag(source, single);
            }
            ShaderProperty.InvalidateRetainedAnimatedOwner(target);
            EditorUtility.SetDirty(target);
        }

        static void Dismiss(ShaderEditor shaderEditor)
        {
            Material key = shaderEditor.Materials[0];
            if (s_appliedPresets.TryGetValue(key, out AppliedPreset appliedPreset))
            {
                s_appliedPresets.Remove(key);
                appliedPreset.DestroySnapshots();
                ThryLogger.Log($"Dismissed revert state for '{key.name}'");
            }
        }

        internal static List<string> PreviewChanges(ShaderEditor editor, Material[] originals, IList<Material> presets, ShaderPart parent = null)
        {
            var changes = new List<string>();
            foreach (var original in originals)
            {
                var preview = new Material(original);
                try
                {
                    var affected = new HashSet<ShaderProperty>();
                    foreach (var preset in presets)
                    {
                        var properties = new HashSet<ShaderProperty>();
                        CollectPresetProperties(editor, preset, parent, properties);
                        var source = new Material(preset);
                        // Like applying, a material on another shader reads what the editor's shader lacks through its own.
                        var ownShader = SectionLock.GetSourceShader(original.shader);
                        var ownSource = ownShader != null && ownShader != editor.Shader ? new Material(preset) : null;
                        try
                        {
                            SwapCloneShader(source, editor.Shader);
                            if (ownSource != null) SwapCloneShader(ownSource, ownShader);
                            foreach (var property in properties)
                            {
                                var name = property.MaterialProperty?.name;
                                if (name == null || !preview.HasProperty(name)) continue;
                                var from = ownSource == null || source.HasProperty(name) ? source : ownSource;
                                var destination = MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { preview }, name);
                                if (GetPropertyMode(preset, property) != PropertyMode.AnimationOnly)
                                    MaterialHelper.CopyValue(from, destination);
                                if (property.IsAnimatable) ShaderOptimizer.CopyAnimatedTag(from, destination);
                                affected.Add(property);
                            }
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(source);
                            if (ownSource != null) UnityEngine.Object.DestroyImmediate(ownSource);
                        }
                    }
                    foreach (var property in affected.OrderBy(p => p.ShaderPropertyIndex))
                    {
                        string name = property.MaterialProperty.name;
                        var before = MaterialHelper.GetValue(original, name); var after = MaterialHelper.GetValue(preview, name);
                        bool transform = property.MaterialProperty.GetPropertyType() == UnityEngine.Rendering.ShaderPropertyType.Texture
                            && (original.GetTextureScale(name) != preview.GetTextureScale(name) || original.GetTextureOffset(name) != preview.GetTextureOffset(name));
                        string caption = RetainedMaterialBody.SectionCaption(property).TrimEnd('*');
                        string prefix = originals.Length > 1 ? original.name + " / " : "";
                        if (!Equals(before, after) || transform)
                            changes.Add(prefix + caption + ": " + PreviewValue(before) + " → " + PreviewValue(after)
                                + (transform ? " (tiling / offset)" : ""));
                        string beforeAnimation = ShaderOptimizer.GetAnimatedTag(original, name);
                        string afterAnimation = ShaderOptimizer.GetAnimatedTag(preview, name);
                        if (beforeAnimation != afterAnimation)
                            changes.Add(prefix + caption + " / Animation: " + AnimationCaption(beforeAnimation) + " → " + AnimationCaption(afterAnimation));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(preview); }
            }
            return changes;
        }

        // Drawers are slow on Poiyomi and clones are only read from
        static void SwapCloneShader(Material clone, Shader shader)
        {
            ShaderOptimizer.DetourApplyMaterialPropertyDrawers();
            try { MaterialHelper.SwapShaderPreservingSettings(clone, shader); }
            finally { ShaderOptimizer.RestoreApplyMaterialPropertyDrawers(); }
        }

        static string AnimationCaption(string tag) => tag == "2" ? "RA" : tag == "1" ? "A" : "Off";
        static string PreviewValue(object value)
        {
            var asset = value as UnityEngine.Object;
            return asset != null ? asset.name : value == null ? RetainedText.Get("none", "None") : value.ToString();
        }

        public static void ApplyFullList(ShaderEditor shaderEditor, Material[] originals, List<Material> presets, ShaderPart parent = null)
        {
            for (int i = 0; i < shaderEditor.Materials.Length && i < originals.Length; i++)
                shaderEditor.Materials[i].CopyPropertiesFromMaterial(originals[i]);
            shaderEditor.UpdatePropertyReferences();
            foreach (Material preset in presets)
            {
                ApplyPresetInternal(shaderEditor, preset, preset, parent);
                GlobalLinker.PropagateAfterPreset(shaderEditor, preset, parent);
                PropagateLinkedMaterials(shaderEditor, preset, parent);
            }
            shaderEditor.ApplyDrawers();
            shaderEditor.Reload();
        }

        internal static bool ApplyPresetToMaterial(Material target, Material preset, string sectionHeader = null)
        {
            ShaderEditor editor = ShaderEditor.CreateTemporary(target);
            try
            {
                editor.ShaderParts.Add(new RenderQueueProperty(editor));
                editor.ShaderParts.Add(new VRCFallbackProperty(editor));
                ShaderPart parent = null;
                if (sectionHeader != null)
                {
                    parent = editor.ShaderParts.OfType<ShaderGroup>().FirstOrDefault(g => g.MaterialProperty?.name == sectionHeader);
                    if (parent == null) return false;
                }
                ApplyPresetInternal(editor, preset, preset, parent);
                return true;
            }
            finally { editor.ReleaseTemporary(); }
        }

        static void ApplyPresetInternal(ShaderEditor shaderEditor, Material preset, Material copyFrom, ShaderPart parent)
        {
            // Work on a temporary in-memory clone so the preset asset on disk is never dirtied.
            // We need the editor's shader assigned to make sure all properties are available
            // (and to prevent stuff like missing shaders making presets unusable), but doing
            // that on the asset itself leaves it modified-but-unsaved (e.g. a preset stored on
            // the Standard shader keeps orphaned properties after the swap), which triggers a
            // "Original shader not saved to material" warning when the scene is saved.
            Material source = new Material(preset);
            try
            {
                // Assigning a shader resets the render queue to the shader's default and drops the material's own
                // override tags, so a preset storing a Render Queue or VRC Fallback would hand those defaults to the
                // target instead of the values it recorded. Swap through the helper that carries both across.
                SwapCloneShader(source, shaderEditor.Shader);
                // If values were meant to be copied straight from the preset, read them from the clone instead.
                bool fromPreset = copyFrom == preset;
                if (fromPreset) copyFrom = source;

                var animationOnly = AnimationOnlyProperties(shaderEditor, preset);

                if (!IsMaterialSectionedPreset(preset))
                {
                    ThryLogger.LogDetail($"Apply preset '{preset.name}' to '{shaderEditor.Materials[0].name}'");
                    foreach (ShaderPart part in shaderEditor.ShaderParts)
                    {
                        if (GetPropertyMode(preset, part) == PropertyMode.ValueAndAnimation)
                        {
                            if(part is ShaderGroup)
                                part.CopyFrom(copyFrom, applyDrawers: false, copyReferenceProperties: true, deepCopy: true, skipPropertyNames: animationOnly);
                            else
                                part.CopyFrom(copyFrom, applyDrawers: false, copyReferenceProperties: false, skipPropertyNames: animationOnly);
                        }
                    }
                }
                else if(parent is ShaderGroup)
                {
                    ThryLogger.LogDetail($"Apply values from '{copyFrom.name}' to '{parent.Content.text}' group");
                    if (GetPropertyMode(preset, parent) == PropertyMode.ValueAndAnimation)
                        parent.CopyFrom(copyFrom, applyDrawers: false, skipPropertyNames: animationOnly);
                    else ApplyPresetRecursive(preset, copyFrom, parent as ShaderGroup, animationOnly);
                }

                if (fromPreset) CopyFromOtherSelectedShaders(shaderEditor, preset, source, parent, animationOnly);

                if (animationOnly.Count > 0)
                {
                    var affected = new HashSet<ShaderProperty>();
                    CollectPresetProperties(shaderEditor, preset, parent, affected);
                    foreach (var property in affected)
                    {
                        if (property.MaterialProperty == null || !property.IsAnimatable
                            || !animationOnly.Contains(property.MaterialProperty.name)) continue;
                        ShaderOptimizer.CopyAnimatedTag(copyFrom, property.MaterialProperty);
                        foreach (Material target in property.MaterialProperty.targets)
                        {
                            EditorUtility.SetDirty(target);
                            ShaderProperty.InvalidateRetainedAnimatedOwner(target);
                        }
                        property.RefreshRetainedAnimatedState();
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        
        // The preset is read through the first material's shader, which leaves out what only the other shaders in a
        // mixed selection declare: Two Pass's second pass, Grab Pass's refraction, or Pro's rendering mode when a
        // variant comes first. Read those through a shader that declares them.
        static void CopyFromOtherSelectedShaders(ShaderEditor shaderEditor, Material preset, Material primary, ShaderPart parent, HashSet<string> animationOnly)
        {
            Shader[] others = shaderEditor.Materials.Where(m => m != null).Select(m => SectionLock.GetSourceShader(m.shader))
                .Where(s => s != null && s != shaderEditor.Shader).Distinct().ToArray();
            if (others.Length == 0) return;
            var affected = new HashSet<ShaderProperty>();
            CollectPresetProperties(shaderEditor, preset, parent, affected);
            var missing = affected.Where(p => p.MaterialProperty != null && !primary.HasProperty(p.MaterialProperty.name)).ToList();
            foreach (Shader shader in others)
            {
                if (missing.Count == 0) return;
                Material source = new Material(preset);
                try
                {
                    SwapCloneShader(source, shader);
                    foreach (ShaderProperty property in missing.Where(p => source.HasProperty(p.MaterialProperty.name)).ToList())
                    {
                        property.CopyFrom(source, applyDrawers: false, deepCopy: false, copyReferenceProperties: false, skipPropertyNames: animationOnly);
                        missing.Remove(property);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(source); }
            }
        }

        static void ApplyPresetRecursive(Material preset, Material copyFrom, ShaderGroup parent, HashSet<string> animationOnly)
        {
            foreach (ShaderProperty reference in ReferenceProperties(parent))
                if (GetPropertyMode(preset, reference) == PropertyMode.ValueAndAnimation)
                    reference.CopyFrom(copyFrom, applyDrawers: false, deepCopy: false, copyReferenceProperties: false, skipPropertyNames: animationOnly);
            foreach (ShaderPart part in parent.Children)
            {
                if(part is ShaderGroup)
                {
                    ApplyPresetRecursive(preset, copyFrom, part as ShaderGroup, animationOnly);
                }
                if (GetPropertyMode(preset, part) == PropertyMode.ValueAndAnimation)
                {
                    // ThryDebug.Detail($"Apply values from '{copyFrom.name}' to '{part.Content.text}' ({copyFrom.name} -> {part.MaterialProperty.targets[0].name}) ({MaterialHelper.GetValue(part.MaterialProperty)} -> {MaterialHelper.GetValue(copyFrom, part.MaterialProperty.name)})");
                    part.CopyFrom(copyFrom, applyDrawers: false, skipPropertyNames: animationOnly);
                }
            }
        }

        // Hidden reference toggles are not among a group's children.
        static IEnumerable<ShaderProperty> ReferenceProperties(ShaderGroup group)
        {
            var dictionary = group.MyShaderUI?.PropertyDictionary;
            if (dictionary == null) yield break;
            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(group.Options.reference_property)) names.Add(group.Options.reference_property);
            if (group.Options.reference_properties != null) names.AddRange(group.Options.reference_properties);
            foreach (string name in names.Distinct())
                if (dictionary.TryGetValue(name, out ShaderProperty reference) && !group.Children.Contains(reference))
                    yield return reference;
        }

        static void PropagateLinkedMaterials(ShaderEditor shaderEditor, Material preset, ShaderPart parent)
        {
            if (shaderEditor.IsInAnimationMode) return;

            if (AnimationOnlyProperties(shaderEditor, preset).Count > 0)
            {
                var affected = new HashSet<ShaderProperty>();
                CollectPresetProperties(shaderEditor, preset, parent, affected);
                foreach (var group in PresetLinkGroups(shaderEditor, preset, parent, affected))
                {
                    var linked = MaterialLinker.GetLinked(group.MaterialProperty);
                    if (linked == null) continue;
                    var groupProperties = PropertiesInGroup(shaderEditor, preset, group, affected);
                    foreach (Material target in linked)
                    {
                        if (shaderEditor.Materials.Contains(target)) continue;
                        Undo.RecordObject(target, "Apply linked preset");
                        CopyPresetPropertiesToMaterial(preset, target, (Material)group.MaterialProperty.targets[0], groupProperties);
                        MaterialEditor.ApplyMaterialPropertyDrawers(target);
                    }
                }
                return;
            }

            if (!IsMaterialSectionedPreset(preset))
            {
                foreach (ShaderPart part in shaderEditor.ShaderParts)
                {
                    if (part is ShaderGroup group && IsPreset(preset, part)) group.UpdateLinkedMaterials();
                }
            }
            else if (parent is ShaderGroup group)
            {
                group.UpdateLinkedMaterials();
            }
        }

        internal static HashSet<string> AnimationOnlyProperties(ShaderEditor editor, Material preset)
            => new HashSet<string>(editor.PropertyDictionary.Where(p => GetPropertyMode(preset, p.Value) == PropertyMode.AnimationOnly).Select(p => p.Key));

        internal static HashSet<ShaderGroup> PresetLinkGroups(ShaderEditor editor, Material preset, ShaderPart parent, HashSet<ShaderProperty> affected)
        {
            var groups = new HashSet<ShaderGroup>();
            if (IsMaterialSectionedPreset(preset))
            {
                if (parent is ShaderGroup group) groups.Add(group);
            }
            else
                foreach (var group in editor.ShaderParts.OfType<ShaderGroup>())
                    if (IsPreset(preset, group)) groups.Add(group);
            foreach (var property in affected)
                if (GetPropertyMode(preset, property) == PropertyMode.AnimationOnly)
                    for (var group = property.Parent; group != null; group = group.Parent)
                        if (group is ShaderGroup shaderGroup && group.MaterialProperty != null) groups.Add(shaderGroup);
            return groups;
        }

        internal static HashSet<ShaderProperty> PropertiesInGroup(ShaderEditor editor, Material preset, ShaderGroup group, HashSet<ShaderProperty> affected)
        {
            var properties = new HashSet<ShaderProperty>();
            CollectPartProperties(editor, group, true, properties, preset);
            properties.IntersectWith(affected);
            return properties;
        }

        public static void SetProperty(Material m, ShaderPart prop, bool value)
            => SetPropertyMode(m, prop, value ? PropertyMode.ValueAndAnimation : PropertyMode.Excluded);

        public static void SetPropertyMode(Material m, ShaderPart prop, PropertyMode mode)
        {
            if (mode == PropertyMode.AnimationOnly && (!(prop is ShaderProperty) || !prop.IsAnimatable || prop.MaterialProperty == null))
                throw new ArgumentException("Animation-only presets require an animatable property.", nameof(prop));
            string value = mode == PropertyMode.AnimationOnly ? "animation" : mode == PropertyMode.ValueAndAnimation ? "true" : "";
            if (prop.CustomStringTagID  != null) m.SetOverrideTag(prop.CustomStringTagID + TAG_POSTFIX_IS_PROPERTY_PRESET, value);
            if (prop.MaterialProperty   != null) m.SetOverrideTag(prop.MaterialProperty.name + TAG_POSTFIX_IS_PROPERTY_PRESET, value);
            if (prop.PropertyIdentifier != null) m.SetOverrideTag(prop.PropertyIdentifier    + TAG_POSTFIX_IS_PROPERTY_PRESET, value);
            EditorUtility.SetDirty(m);
        }

        public static PropertyMode GetPropertyMode(Material m, ShaderPart prop)
            => GetPropertyMode(m, prop.CustomStringTagID ?? prop.MaterialProperty?.name ?? prop.PropertyIdentifier);

        public static PropertyMode GetPropertyMode(Material m, string propertyName)
        {
            string value = m != null && propertyName != null ? m.GetTag(propertyName + TAG_POSTFIX_IS_PROPERTY_PRESET, false, "") : "";
            return value == "true" ? PropertyMode.ValueAndAnimation : value == "animation" ? PropertyMode.AnimationOnly : PropertyMode.Excluded;
        }

        public static bool IsPreset(Material m, ShaderPart prop) => GetPropertyMode(m, prop) != PropertyMode.Excluded;

        public static bool ArePreset(Material[] mats)
        {
            return mats.All(m => IsPreset(m));
        }

        public static bool IsPreset(Material m)
        {
            return m?.GetTag(TAG_IS_MATERIAL_PRESET, false, "false") == "true";
        }
        
        public static void SetPreset(IEnumerable<Material> mats, bool set)
        {
            if (set)
            {
                foreach (Material m in mats)
                {
                    if(m == null) continue;
                    m.SetOverrideTag(TAG_IS_MATERIAL_PRESET, "true");
                    if (m.GetTag("presetName", false, "") == "") m.SetOverrideTag("presetName", m.name);
                    Presets.AddPreset(m);
                }
            }
            else
            {
                foreach (Material m in mats)
                {
                    if(m == null) continue;
                    m.SetOverrideTag(TAG_IS_MATERIAL_PRESET, "");
                    Presets.RemovePreset(m);
                }
            }
        }

        public static bool IsMaterialSectionedPreset(Material m)
        {
            return m?.GetTag(TAG_IS_MATERIAL_SECTIONED_PRESET, false, "false") == "true";
        }

        public static void SetMaterialSectionedPreset(Material m, bool value)
        {
            m.SetOverrideTag(TAG_IS_MATERIAL_SECTIONED_PRESET, value ? "true" : "");
            RemovePreset(m);
            AddPreset(m);   
        }

        public static bool IsSectionPreset(Material m, string headerPropName)
        {
            return !string.IsNullOrWhiteSpace(m.GetTag(headerPropName + TAG_POSTFIX_SECTION_NAME, false, ""));
        }

        public static string GetSectionPresetName(Material m, string headerPropName)
        {
            return m.GetTag(headerPropName + TAG_POSTFIX_SECTION_NAME, false, "");
        }

        public static void SetSectionPreset(Material m, string headerPropName, string name)
        {
            m.SetOverrideTag(headerPropName + TAG_POSTFIX_SECTION_NAME, name);

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));
            if(!string.IsNullOrWhiteSpace(name))
            {
                if(!PresetCollections.ContainsKey(headerPropName))
                {
                    PresetCollections[headerPropName] = new PresetsCollection();
                }
    	        
                PresetCollections[headerPropName].AddOrUpdate(name, guid);
                ThryLogger.LogDetail($"Add preset for section '{headerPropName}': {name} ({guid})");
            }else
            {
                if(PresetCollections.ContainsKey(headerPropName))
                {
                    PresetCollections[headerPropName].Remove(guid);
                    ThryLogger.LogDetail($"Remove preset for section '{headerPropName}' ({guid})");
                }
            }
        }

        public static bool DoesSectionHavePresets(string headerPropName)
        {
            return PresetCollections.ContainsKey(headerPropName) && PresetCollections[headerPropName].Count > 0;
        }

#region Preset Validation

        /* Keeps the cache in step with the project without ever throwing it away.

           Materials routinely appear without ThryEditor seeing an import event for them: they
           arrive through version control while Unity is closed, they sit inside .fbx/.asset
           containers, or a build pipeline writes them mid-session. This check used to react to a
           single unfamiliar material by discarding the entire cache and re-loading every material
           in the project, so any tool that generates materials (VRCFury, avatar build pipelines,
           texture packers) forced a full preset rebuild on every domain reload.

           Instead, look only at what actually changed: index the materials we haven't seen before
           and drop entries for the ones that are gone. Everything already in the cache stays. */
        [InitializeOnLoadMethod]
        static void ScheduleCacheValidation()
        {
            // A headless build has no use for the preset list, and scanning would only cost import
            // time on CI.
            if (Application.isBatchMode) return;
            // Deferred so the scan stays off the domain reload path and runs once the AssetDatabase
            // has settled.
            EditorApplication.delayCall += ValidatePresetCache;
        }

        static void ValidatePresetCache()
        {
            InitializeDataStructures();

            string[] currentMaterials = AssetDatabase.FindAssets("t:material");
            List<string> unknown = currentMaterials.Where(g => !KnownMaterials.Contains(g)).ToList();

            if (unknown.Count > 0)
            {
                ThryLogger.LogDetail($"Preset cache: indexing {unknown.Count} new material asset(s)");
                IndexPresets(unknown, "Indexing New Materials");
            }

            // Rewrite the known list only when it actually differs from the project. With no
            // unknowns left the list is a superset of the project, so a differing count means it
            // still holds materials that were deleted - dropping them stops the file growing
            // without bound when a tool creates and discards materials on every build.
            if (unknown.Count > 0 || KnownMaterials.Count != currentMaterials.Length)
            {
                KnownMaterials.SetCollection(currentMaterials);
                KnownMaterials.Save();
            }

            // Presets whose asset vanished are pruned by the write itself (see RemoveWithoutPath),
            // so a dangling entry costs one file write rather than a full rebuild.
            if (PresetCollections.Values.Any(c => c.Paths.Any(string.IsNullOrWhiteSpace)))
            {
                ThryLogger.LogDetail("Preset cache: dropping entries for deleted preset materials");
                Save();
            }
        }

#endregion
    }

    public partial class PresetsPopupGUI : EditorWindow
    {
        List<Material> tickedPresets = new List<Material>();
        ShaderEditor shaderEditor;
        string _collection;
        ShaderPart _parent;
        public void Init(string collection, List<string> names, List<string> guids, ShaderEditor shaderEditor)
        {
            this.shaderEditor = shaderEditor;
            this._collection = collection;
            _parent = collection == "_full_" ? null : shaderEditor.CurrentProperty;
            InitializeBrowser(names, guids);
        }

        void TogglePreset(Material m, bool on)
        {
            if (m == null) return;
            if (tickedPresets.Contains(m) && !on) tickedPresets.Remove(m);
            if (!tickedPresets.Contains(m) && on) tickedPresets.Add(m);
            UpdatePreview();
        }

        bool _save;
        private void OnDestroy()
        {
            _browserWatch?.Pause();
        }
        void ApplyStaged()
        {
            if (_save || !CanApplyBrowser()) return;
            shaderEditor.ActivateRetained();
            _parent = CurrentBrowserParent();
            var originals = _browserTargets.Select(m => new Material(m)).ToArray();
            try
            {
                Undo.RecordObjects(_browserTargets, RetainedText.Get("apply_preset", "Apply presets"));
                Presets.ApplyFullList(shaderEditor, originals, tickedPresets, _parent); _save = true; Close();
            }
            finally { foreach (var original in originals) DestroyImmediate(original); }
        }
        void UpdatePreview()
        {
            RefreshPresetPreview();
        }
        public void CreateGUI()
        {
            BuildPresetBrowser();
        }
    }
}
