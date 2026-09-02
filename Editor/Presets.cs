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

namespace Thry.ThryEditor
{
    public class Presets : AssetPostprocessor
    {
        const string TAG_IS_MATERIAL_PRESET = "isPreset";
        const string TAG_IS_MATERIAL_SECTIONED_PRESET = "isSectionedPreset";
        const string TAG_MATERIAL_PRESET_NAME = "presetName";
        const string TAG_POSTFIX_IS_PROPERTY_PRESET = "_isPreset";
        const string TAG_POSTFIX_SECTION_NAME = "isSectionedPreset"; // Weird name, because of a leagcy bug
        const string FILE_NAME_CACHE = "Thry/preset_cache.txt";
        const string FILE_NAME_KNOWN_MATERIALS = "Thry/presets_known_materials.txt";
        const string PRESET_VERSION = "1.1.0";

        struct AppliedPreset
        {
            public string name;
            public Material preset;
            // One snapshot per selected material, in the editor's material order. Reverting a
            // multi-selection from a single snapshot would hand every material the first one's values.
            public Material[] prePresetStates;
            public ShaderPart parent;

            public static AppliedPreset Create(string name, Material preset, Material[] currentStates, ShaderPart parent)
            {
                AppliedPreset appliedPreset = new AppliedPreset();
                appliedPreset.name = name;
                appliedPreset.preset = preset;
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
                if (_guidToName.ContainsKey(guid))
                {
                    _nameToGuid.Remove(_guidToName[guid]);
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
                string[] split = line.Split(';');
                _nameToGuid[split[0]] = split[1];
                _guidToName[split[1]] = split[0];
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
                    Dictionary<string, string> pathsToGuids = PresetCollections.
                        SelectMany(c => c.Value.Guids).Distinct(). // Guids of all preset materials. Because of sectioned can exists multiples
                        Select(g => (AssetDatabase.GUIDToAssetPath(g), g)). // Tuple of path and guid
                        ToDictionary(k => k.Item1, v => v.Item2);
                    // Check if any presets were deleted, iterate over all deleted materials
                    foreach (string asset in deletedAssets.Where(IsMaterialAssetPath))
                    {
                        // Check if asset is preset
                        if (pathsToGuids.ContainsKey(asset))
                        {
                            // Remove preset
                            RemovePreset(pathsToGuids[asset]);
                        }
                    }
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
            if (materials.Length == 1 || snapshots.Length != materials.Length)
            {
                // Single material, or the selection changed since the preset was applied: the shared
                // path writes the first snapshot through the editor's own property objects.
                ApplyPresetInternal(shaderEditor, appliedPreset.preset, snapshots[0], appliedPreset.parent);
            }
            else
            {
                // Multi-selection: the editor's MaterialProperty objects write to every target at once,
                // so each material gets its own snapshot copied through a single-target property instead.
                HashSet<ShaderProperty> affected = new HashSet<ShaderProperty>();
                CollectPresetProperties(shaderEditor, appliedPreset.preset, appliedPreset.parent, affected);
                for (int i = 0; i < materials.Length; i++)
                    RevertMaterial(materials[i], snapshots[i], affected);
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
        static void CollectPresetProperties(ShaderEditor shaderEditor, Material preset, ShaderPart parent, HashSet<ShaderProperty> into)
        {
            if (!IsMaterialSectionedPreset(preset))
            {
                foreach (ShaderPart part in shaderEditor.ShaderParts)
                    if (IsPreset(preset, part))
                        CollectPartProperties(shaderEditor, part, copyReferenceProperties: part is ShaderGroup, into);
            }
            else if (parent is ShaderGroup)
            {
                CollectPresetPropertiesRecursive(shaderEditor, preset, parent as ShaderGroup, into);
            }
        }

        static void CollectPresetPropertiesRecursive(ShaderEditor shaderEditor, Material preset, ShaderGroup parent, HashSet<ShaderProperty> into)
        {
            foreach (ShaderPart part in parent.Children)
            {
                if (part is ShaderGroup)
                    CollectPresetPropertiesRecursive(shaderEditor, preset, part as ShaderGroup, into);
                if (IsPreset(preset, part))
                    CollectPartProperties(shaderEditor, part, copyReferenceProperties: true, into);
            }
        }

        static void CollectPartProperties(ShaderEditor shaderEditor, ShaderPart part, bool copyReferenceProperties, HashSet<ShaderProperty> into)
        {
            if (part is ShaderProperty prop) into.Add(prop);
            if (part is ShaderGroup group)
                foreach (ShaderPart child in group.Children)
                    CollectPartProperties(shaderEditor, child, copyReferenceProperties, into);
            if (!copyReferenceProperties) return;
            if (part.Options.reference_properties != null)
                foreach (string name in part.Options.reference_properties)
                    if (shaderEditor.PropertyDictionary.TryGetValue(name, out ShaderProperty reference)) into.Add(reference);
            if (!string.IsNullOrWhiteSpace(part.Options.reference_property)
                && shaderEditor.PropertyDictionary.TryGetValue(part.Options.reference_property, out ShaderProperty singleReference))
                into.Add(singleReference);
        }

        static void RevertMaterial(Material target, Material snapshot, HashSet<ShaderProperty> properties)
        {
            UnityEngine.Object[] targets = { target };
            foreach (ShaderProperty property in properties)
            {
                if (property.MaterialProperty == null || !target.HasProperty(property.MaterialProperty.name)) continue;
                MaterialProperty single = MaterialEditor.GetMaterialProperty(targets, property.MaterialProperty.name);
                if (single == null) continue;
                MaterialHelper.CopyValue(snapshot, single);
                TileLabelUtility.CopyTileLabelTag(snapshot, single);
                if (property.IsAnimatable) ShaderOptimizer.CopyAnimatedTag(snapshot, single);
            }
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

        public static void ApplyFullList(ShaderEditor shaderEditor, Material[] originals, List<Material> presets)
        {
            for (int i = 0; i < shaderEditor.Materials.Length && i < originals.Length; i++)
                shaderEditor.Materials[i].CopyPropertiesFromMaterial(originals[i]);
            shaderEditor.UpdatePropertyReferences();
            foreach (Material preset in presets)
            {
                ApplyPresetInternal(shaderEditor, preset, preset, null);
                GlobalLinker.PropagateAfterPreset(shaderEditor, preset, null);
                PropagateLinkedMaterials(shaderEditor, preset, null);
            }
            shaderEditor.ApplyDrawers();
            shaderEditor.Reload();
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
            // Assigning a shader resets the render queue to the shader's default and drops the material's own
            // override tags, so a preset storing a Render Queue or VRC Fallback would hand those defaults to the
            // target instead of the values it recorded. Swap through the helper that carries both across.
            MaterialHelper.SwapShaderPreservingSettings(source, shaderEditor.Shader);
            // If values were meant to be copied straight from the preset, read them from the clone instead.
            if (copyFrom == preset) copyFrom = source;

            if (!IsMaterialSectionedPreset(preset))
            {
                ThryLogger.LogDetail($"Apply preset '{preset.name}' to '{shaderEditor.Materials[0].name}'");
                foreach (ShaderPart part in shaderEditor.ShaderParts)
                {
                    if (IsPreset(preset, part))
                    {
                        if(part is ShaderGroup)
                            part.CopyFrom(copyFrom, applyDrawers: false, copyReferenceProperties: true, deepCopy: true);
                        else
                            part.CopyFrom(copyFrom, applyDrawers: false, copyReferenceProperties: false);
                    }
                }
            }
            else if(parent is ShaderGroup)
            {
                ThryLogger.LogDetail($"Apply values from '{copyFrom.name}' to '{parent.Content.text}' group");
                ApplyPresetRecursive(preset, copyFrom, parent as ShaderGroup);
            }

            UnityEngine.Object.DestroyImmediate(source);
        }
        
        static void ApplyPresetRecursive(Material preset, Material copyFrom, ShaderGroup parent)
        {
            foreach (ShaderPart part in parent.Children)
            {
                if(part is ShaderGroup)
                {
                    ApplyPresetRecursive(preset, copyFrom, part as ShaderGroup);
                }
                if (IsPreset(preset, part))
                {
                    // ThryDebug.Detail($"Apply values from '{copyFrom.name}' to '{part.Content.text}' ({copyFrom.name} -> {part.MaterialProperty.targets[0].name}) ({MaterialHelper.GetValue(part.MaterialProperty)} -> {MaterialHelper.GetValue(copyFrom, part.MaterialProperty.name)})");
                    part.CopyFrom(copyFrom, applyDrawers: false);
                }
            }
        }

        static void PropagateLinkedMaterials(ShaderEditor shaderEditor, Material preset, ShaderPart parent)
        {
            if (shaderEditor.IsInAnimationMode) return;

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

        public static void SetProperty(Material m, ShaderPart prop, bool value)
        {
            if (prop.CustomStringTagID  != null) m.SetOverrideTag(prop.CustomStringTagID + TAG_POSTFIX_IS_PROPERTY_PRESET, value ? "true" : "");
            if (prop.MaterialProperty   != null) m.SetOverrideTag(prop.MaterialProperty.name + TAG_POSTFIX_IS_PROPERTY_PRESET, value ? "true" : "");
            if (prop.PropertyIdentifier != null) m.SetOverrideTag(prop.PropertyIdentifier    + TAG_POSTFIX_IS_PROPERTY_PRESET, value ? "true" : "");
        }

        public static bool IsPreset(Material m, ShaderPart prop)
        {
            if (prop.CustomStringTagID  != null) return m.GetTag(prop.CustomStringTagID + TAG_POSTFIX_IS_PROPERTY_PRESET, false, "") == "true";
            if (prop.MaterialProperty   != null) return m.GetTag(prop.MaterialProperty.name + TAG_POSTFIX_IS_PROPERTY_PRESET, false, "") == "true";
            if (prop.PropertyIdentifier != null) return m.GetTag(prop.PropertyIdentifier    + TAG_POSTFIX_IS_PROPERTY_PRESET, false, "") == "true";
            return false;
        }

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

    public class PresetsPopupGUI : EditorWindow
    {
        class PresetStruct
        {
            Dictionary<string,PresetStruct> dict;
            public List<PresetStruct> structure;
            string name;
            string fullName;
            string guid;
            bool hasPreset;
            bool isOpen = false;
            bool isOn;
            public PresetStruct(string name)
            {
                this.name = name;
                dict = new Dictionary<string, PresetStruct>();
                structure = new List<PresetStruct>();
            }

            public PresetStruct GetSubStruct(string name)
            {
                name = name.Trim();
                if (dict.ContainsKey(name) == false)
                {
                    dict.Add(name, new PresetStruct(name));
                    structure.Add(dict[name]);
                }
                return dict[name];
            }
            public void AddPresetStruct(bool b, string name, string fullName, string guid)
            {
                PresetStruct s = new PresetStruct(name);
                s.hasPreset = b;
                s.fullName = fullName;
                s.guid = guid;
                if(!dict.ContainsKey(fullName))
                {
                    dict.Add(fullName, s);
                }else
                {
                    PresetStruct dupl = dict[fullName];
                    if(dupl.fullName.EndsWith(dupl.name))
                        dupl.name = dupl.name + $" ({dupl.guid})";
                    s.name = s.name + $" ({guid})";
                }
                structure.Add(s);
            }
            public void StructGUI(PresetsPopupGUI popupGUI)
            {
                if(hasPreset)
                {
                    EditorGUI.BeginChangeCheck();
                    isOn = EditorGUILayout.ToggleLeft(name, isOn);
                    if (EditorGUI.EndChangeCheck())
                    {
                        popupGUI.TogglePreset(Presets.GetPresetMaterial(guid), isOn);
                    }
                }
                if(structure.Count > 0)
                {
                    Rect r = GUILayoutUtility.GetRect(new GUIContent(), Styles.flatHeader);
                    r.x = GUILib.IndentToPixels(EditorGUI.indentLevel);
                    r.width -= r.x;
                    GUI.Box(r, name, Styles.flatHeader);
                    if (Event.current.type == EventType.Repaint)
                    {
                        var toggleRect = new Rect(r.x + 4f, r.y + 2f, 13f, 13f);
                        EditorStyles.foldout.Draw(toggleRect, false, false, isOpen, false);
                    }
                    if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    {
                        isOpen = !isOpen;
                        ShaderEditor.Input.Use();
                    }
                    if (isOpen)
                    {
                        using (new GUILib.IndentScope(1))
                        {
                            foreach (PresetStruct struc in structure)
                            {
                                struc.StructGUI(popupGUI);
                            }
                        }
                    }
                }
                
            }

            public void Reset()
            {
                isOn = false;
                foreach (PresetStruct struc in structure)
                    struc.Reset();
            }
        }

        Material[] beforePreset;
        List<Material> tickedPresets = new List<Material>();
        PresetStruct mainStruct;
        ShaderEditor shaderEditor;
        string _collection;
        public void Init(string collection, List<string> names, List<string> guids, ShaderEditor shaderEditor)
        {
            this.shaderEditor = shaderEditor;
            this._collection = collection;
            ShaderOptimizer.DetourApplyMaterialPropertyDrawers();
            this.beforePreset = shaderEditor.Materials.Select(m => new Material(m)).ToArray();
            ShaderOptimizer.RestoreApplyMaterialPropertyDrawers();
            mainStruct = new PresetStruct("");
            backgroundTextrure = new Texture2D(1,1);
            if (EditorGUIUtility.isProSkin) backgroundTextrure.SetPixel(0, 0, new Color(0.18f, 0.18f, 0.18f, 1));
            else backgroundTextrure.SetPixel(0, 0, new Color(0.9f, 0.9f, 0.9f, 1));
            backgroundTextrure.Apply();
            for (int i = 0; i < names.Count; i++)
            {
                string[] path = names[i].Split('/');
                PresetStruct addUnder = mainStruct;
                for (int j=0;j<path.Length - 1; j++)
                {
                    addUnder = addUnder.GetSubStruct(path[j]);
                }
                addUnder.AddPresetStruct(Presets.DoesPresetExist(this._collection, names[i]), path[path.Length-1], names[i], guids[i]);
            }
        }

        void TogglePreset(Material m, bool on)
        {
            if (tickedPresets.Contains(m) && !on) tickedPresets.Remove(m);
            if (!tickedPresets.Contains(m) && on) tickedPresets.Add(m);
            Presets.ApplyFullList(shaderEditor, beforePreset, tickedPresets);
            shaderEditor.Repaint();
        }

        static Texture2D backgroundTextrure;

        Vector2 scroll;
        bool _save;
        void OnGUI()
        {
            if (mainStruct == null) { this.Close(); return; }

            GUILayout.BeginHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(position.height - 55));

            GUILayoutUtility.GetRect(10, 5);
            TopStructGUI();

            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();

            if (GUI.Button(new Rect(5, this.position.height - 35, this.position.width / 2 - 5, 30), "Apply"))
            {
                _save = true;
                this.Close();
            }
                
            if (GUI.Button(new Rect(this.position.width / 2, this.position.height - 35, this.position.width / 2 - 5, 30), "Discard"))
            {
                Revert();
            }
        }
        private void OnDestroy()
        {
            if (!_save)
            {
                Revert();
            }
        }

        void TopStructGUI()
        {
            foreach (PresetStruct struc in mainStruct.structure)
            {
                struc.StructGUI(this);
            }
        }

        void Revert()
        {
            EditorUtility.DisplayProgressBar("Reverting", "Reverting", 0);
            for (int i = 0; i < shaderEditor.Materials.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Reverting", "Reverting", (float)i / shaderEditor.Materials.Length);
                shaderEditor.Materials[i].CopyPropertiesFromMaterial(beforePreset[i]);
                MaterialEditor.ApplyMaterialPropertyDrawers(shaderEditor.Materials[i]);
            }
            EditorUtility.ClearProgressBar();
            mainStruct.Reset();
            tickedPresets.Clear();
            shaderEditor.Reload(true);
        }
    }
}
