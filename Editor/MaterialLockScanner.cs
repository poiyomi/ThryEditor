// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Where a material stands with regards to the shader optimizer.
    /// </summary>
    public enum MaterialLockState
    {
        /// <summary>Uses an optimizer-capable shader and is not locked.</summary>
        Unlocked,
        /// <summary>Locked, and the generated shader it points at is present.</summary>
        Locked,
        /// <summary>Locked, but the generated shader it points at is gone. See <see cref="LockedShaderRecovery"/>.</summary>
        Orphaned
    }

    public enum MaterialLockGrouping
    {
        Shader,
        Folder,
        [InspectorName("Prefab / Scene")] PrefabAndScene
    }

    public enum MaterialLockFilter
    {
        All,
        /// <summary>Everything, but split into a locked and an unlocked section.</summary>
        [InspectorName("All (Split)")] AllSplit,
        Unlocked,
        Locked,
        [InspectorName("Needs Attention")] NeedsAttention
    }

    /// <summary>
    /// One material as the lock manager sees it. Serializable so an open window keeps its results
    /// across a domain reload instead of re-scanning the whole project on every script compile.
    /// </summary>
    [Serializable]
    public class MaterialLockEntry
    {
        public Material Material;
        public string AssetPath;
        public MaterialLockState State;

        /// <summary>
        /// The current shader when unlocked, the shader it was locked from otherwise.
        /// Null when the material does not record one that still exists.
        /// </summary>
        public Shader Shader;

        /// <summary>Shader name recorded on the material, kept so an unresolved one can still be named.</summary>
        public string RecordedShaderName;

        /// <summary>Lives in an immutable package, so its shader cannot be rewritten.</summary>
        public bool IsReadOnly;

        /// <summary>
        /// Root of the variant chain. Locking always targets this, never the variant itself,
        /// because a variant cannot have its shader changed.
        /// </summary>
        public Material VariantRoot;

        public bool IsVariant { get { return VariantRoot != null && VariantRoot != Material; } }
        public bool CanLock { get { return State == MaterialLockState.Unlocked && !IsReadOnly; } }
        public bool CanUnlock { get { return State != MaterialLockState.Unlocked && !IsReadOnly; } }
        public bool NeedsAttention { get { return State == MaterialLockState.Orphaned || Shader == null; } }
        public string Name { get { return Material != null ? Material.name : "<missing material>"; } }

        /// <summary>What lock/unlock will actually be applied to.</summary>
        public Material Target { get { return VariantRoot != null ? VariantRoot : Material; } }
    }

    /// <summary>
    /// A drawn section of the list. Rebuilt from the entries whenever the view changes, never serialized.
    /// </summary>
    public class MaterialLockGroup
    {
        /// <summary>Stable across rebuilds and domain reloads, so foldout state sticks to the right group.</summary>
        public string Key;
        public string DisplayName;
        public string Subtitle;

        /// <summary>Pinged directly when set, otherwise <see cref="PingAssetPath"/> is loaded on demand.</summary>
        public UnityEngine.Object PingObject;
        public string PingAssetPath;

        /// <summary>Collects materials that cannot be acted on normally, and sorts to the top.</summary>
        public bool IsAttentionGroup;

        /// <summary>
        /// Band this group belongs under ("Locked" / "Unlocked"), or null when the view is not split.
        /// A band is drawn above the first group of each run.
        /// </summary>
        public string Section;

        /// <summary>Distinct materials in this group's section, for the band's own count.</summary>
        public int SectionCount;

        public readonly List<MaterialLockEntry> Entries = new List<MaterialLockEntry>();

        // Counted once when the group is built rather than per frame: the window redraws constantly
        // and deduplicating variant roots across thousands of entries is not free.
        public int LockableTargets;
        public int UnlockableTargets;
    }

    /// <summary>
    /// Finds every material the shader optimizer can act on and arranges them for display.
    /// Pure model: it never draws, so the window can call it outside the IMGUI pass.
    /// </summary>
    public static class MaterialLockScanner
    {
        public const string GroupKeyOrphaned = "!orphaned";
        public const string GroupKeyUnresolved = "!unresolved";
        public const string GroupKeyUnreferenced = "!unreferenced";

        static readonly string[] AssetsOnly = { "Assets" };

        #region Scanning

        /// <summary>
        /// Classifies every material in scope. Cancelling returns what was found up to that point
        /// rather than nothing, which is more useful than starting over on a large project.
        /// </summary>
        public static List<MaterialLockEntry> Scan(bool includePackages, out bool cancelled)
        {
            cancelled = false;
            List<MaterialLockEntry> entries = new List<MaterialLockEntry>();

            // Shaders can be installed or removed between scans, so resolution must not be cached across them.
            s_shaderByGuid.Clear();
            s_shaderByName.Clear();
            // Drops the scene half of the ownership index only. The prefab half is the expensive one and now
            // survives until an asset change actually invalidates it - see OwnerIndexWatcher.
            InvalidateOwnerIndex();

            string[] guids = includePackages
                ? AssetDatabase.FindAssets("t:Material")
                : AssetDatabase.FindAssets("t:Material", AssetsOnly);

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    // Throttled: the progress bar costs more than the work between two materials.
                    if ((i & 31) == 0 && guids.Length > 0 &&
                        EditorUtility.DisplayCancelableProgressBar("Scanning materials", $"{i} / {guids.Length}", (float)i / guids.Length))
                    {
                        cancelled = true;
                        break;
                    }

                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    // Null for a corrupt or mid-import material. There is nothing to classify, and it
                    // must not take the scan (and the progress bar) down with it.
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) continue;

                    MaterialLockEntry entry = Classify(m, path);
                    if (entry != null) entries.Add(entry);
                }
            }
            finally
            {
                // Without this a throw would leave a modal progress bar over the editor with no way to dismiss it.
                EditorUtility.ClearProgressBar();
            }

            return entries;
        }

        /// <summary>
        /// Returns null for materials the optimizer has no business touching.
        /// Mirrors <see cref="ShaderOptimizer.IsMaterialLocked"/>, minus its guessing fallback.
        /// </summary>
        static MaterialLockEntry Classify(Material m, string path)
        {
            Material root = m.GetRoot();
            MaterialLockEntry entry = new MaterialLockEntry
            {
                Material = m,
                AssetPath = path,
                VariantRoot = root != null && root != m ? root : null
            };

            // Writability is decided by what actually gets written, which for a variant is its root -
            // a variant in Assets/ whose root sits in an immutable package still cannot be locked.
            entry.IsReadOnly = IsReadOnly(entry.VariantRoot != null ? AssetDatabase.GetAssetPath(entry.VariantRoot) : path);

            if (m.shader.IsBroken())
            {
                Shader original = ResolveOriginalShader(m, out entry.RecordedShaderName);

                // The original shader tag sometimes points at an unrelated Unity shader, so a resolved
                // one only counts as evidence of locking if it is actually optimizer-capable.
                bool wasLocked = original != null
                    ? ShaderOptimizer.IsShaderUsingThryOptimizer(original)
                    : !string.IsNullOrEmpty(m.GetTag(ShaderOptimizer.TAG_ALL_MATERIALS_GUIDS_USING_THIS_LOCKED_SHADER, false, string.Empty));

                // A material with a missing shader we never locked is not ours to list.
                if (!wasLocked) return null;

                entry.State = MaterialLockState.Orphaned;
                entry.Shader = original;
                return entry;
            }

            if (ShaderOptimizer.IsShaderLocked(m.shader))
            {
                entry.State = MaterialLockState.Locked;
                entry.Shader = ResolveOriginalShader(m, out entry.RecordedShaderName);
                return entry;
            }

            if (ShaderOptimizer.IsShaderUsingThryOptimizer(m.shader))
            {
                entry.State = MaterialLockState.Unlocked;
                entry.Shader = m.shader;
                return entry;
            }

            return null;
        }

        static readonly Dictionary<string, Shader> s_shaderByGuid = new Dictionary<string, Shader>();
        static readonly Dictionary<string, Shader> s_shaderByName = new Dictionary<string, Shader>();

        /// <summary>
        /// Resolves the shader a locked material was locked from using only what the material records.
        /// Deliberately stops there rather than falling back to <see cref="ShaderOptimizer.GuessShader"/>:
        /// that runs a Levenshtein distance against every shader in the project, which is far too slow
        /// to do per material, and a guess is not something this window should present as fact.
        /// </summary>
        static Shader ResolveOriginalShader(Material m, out string recordedName)
        {
            recordedName = m.GetTag(ShaderOptimizer.TAG_ORIGINAL_SHADER, false, string.Empty);

            string guid = m.GetTag(ShaderOptimizer.TAG_ORIGINAL_SHADER_GUID, false, string.Empty);
            if (!string.IsNullOrEmpty(guid))
            {
                Shader byGuid;
                if (!s_shaderByGuid.TryGetValue(guid, out byGuid))
                {
                    string shaderPath = AssetDatabase.GUIDToAssetPath(guid);
                    byGuid = string.IsNullOrEmpty(shaderPath) ? null : AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                    s_shaderByGuid[guid] = byGuid;
                }
                if (byGuid != null) return byGuid;
            }

            if (string.IsNullOrEmpty(recordedName)) return null;

            Shader byName;
            if (!s_shaderByName.TryGetValue(recordedName, out byName))
            {
                byName = Shader.Find(recordedName);
                s_shaderByName[recordedName] = byName;
            }
            return byName;
        }

        /// <summary>
        /// True for materials inside an immutable package. Their shader cannot be rewritten, so
        /// offering to lock them would only produce a failure later.
        /// </summary>
        static bool IsReadOnly(string assetPath)
        {
            if (!assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;

            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (info == null) return false;

            return info.source != UnityEditor.PackageManager.PackageSource.Embedded
                && info.source != UnityEditor.PackageManager.PackageSource.Local;
        }

        #endregion

        #region Filtering

        public static bool PassesFilter(MaterialLockEntry entry, MaterialLockFilter filter)
        {
            switch (filter)
            {
                case MaterialLockFilter.Unlocked: return entry.State == MaterialLockState.Unlocked;
                case MaterialLockFilter.Locked: return entry.State != MaterialLockState.Unlocked;
                case MaterialLockFilter.NeedsAttention: return entry.NeedsAttention || entry.IsReadOnly;
                default: return true;
            }
        }

        /// <summary>
        /// Matches material name, asset path and shader name. Applied to the already scanned results
        /// rather than pushed into the AssetDatabase filter string, so searching never re-scans and
        /// a term like "t:" cannot silently rewrite the query.
        /// </summary>
        public static bool MatchesSearch(MaterialLockEntry entry, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;

            foreach (string term in search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool hit = Contains(entry.Name, term)
                    || Contains(entry.AssetPath, term)
                    || (entry.Shader != null && Contains(entry.Shader.name, term))
                    || Contains(entry.RecordedShaderName, term);

                if (!hit) return false;
            }
            return true;
        }

        static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Grouping

        public static List<MaterialLockGroup> Group(IEnumerable<MaterialLockEntry> entries, MaterialLockGrouping grouping, bool includePackages, bool splitByLockState = false)
        {
            List<MaterialLockGroup> groups;

            if (splitByLockState)
            {
                List<MaterialLockEntry> all = entries as List<MaterialLockEntry> ?? entries.ToList();

                // Orphaned materials are locked ones whose shader went missing, so they belong to
                // the locked section rather than a third pile.
                groups = BuildSection(all.Where(e => e.State != MaterialLockState.Unlocked), grouping, includePackages, "Locked");
                groups.AddRange(BuildSection(all.Where(e => e.State == MaterialLockState.Unlocked), grouping, includePackages, "Unlocked"));
            }
            else
            {
                groups = BuildGroups(entries, grouping, includePackages);
            }

            foreach (MaterialLockGroup group in groups)
            {
                group.LockableTargets = CountDistinctTargets(group.Entries, true);
                group.UnlockableTargets = CountDistinctTargets(group.Entries, false);
            }

            return groups;
        }

        static List<MaterialLockGroup> BuildGroups(IEnumerable<MaterialLockEntry> entries, MaterialLockGrouping grouping, bool includePackages)
        {
            switch (grouping)
            {
                case MaterialLockGrouping.Folder: return GroupByFolder(entries);
                case MaterialLockGrouping.PrefabAndScene: return GroupByPrefabAndScene(entries, includePackages);
                default: return GroupByShader(entries);
            }
        }

        static List<MaterialLockGroup> BuildSection(IEnumerable<MaterialLockEntry> entries, MaterialLockGrouping grouping, bool includePackages, string section)
        {
            List<MaterialLockEntry> materialised = entries.ToList();
            List<MaterialLockGroup> groups = BuildGroups(materialised, grouping, includePackages);

            foreach (MaterialLockGroup group in groups)
            {
                group.Section = section;
                group.SectionCount = materialised.Count;

                // The same shader or folder appears in both sections, so the keys have to be
                // distinct or the two would share one foldout.
                group.Key = section + "/" + group.Key;
            }

            return groups;
        }

        /// <summary>
        /// How many materials a lock or unlock would actually write. Variants collapse onto their
        /// root, so this is what the user should be told, not the number of rows.
        /// </summary>
        public static int CountDistinctTargets(IEnumerable<MaterialLockEntry> entries, bool locking)
        {
            HashSet<Material> targets = new HashSet<Material>();
            foreach (MaterialLockEntry e in entries)
            {
                if (locking ? !e.CanLock : !e.CanUnlock) continue;
                if (e.Material != null && e.Target != null) targets.Add(e.Target);
            }
            return targets.Count;
        }

        /// <summary>The distinct materials a lock or unlock would be applied to.</summary>
        public static List<Material> CollectTargets(IEnumerable<MaterialLockEntry> entries, bool locking)
        {
            HashSet<Material> targets = new HashSet<Material>();
            foreach (MaterialLockEntry e in entries)
            {
                if (locking ? !e.CanLock : !e.CanUnlock) continue;
                if (e.Material != null && e.Target != null) targets.Add(e.Target);
            }
            return targets.ToList();
        }

        static List<MaterialLockGroup> GroupByShader(IEnumerable<MaterialLockEntry> entries)
        {
            Dictionary<string, MaterialLockGroup> groups = new Dictionary<string, MaterialLockGroup>();
            Dictionary<Shader, string> pathByShader = new Dictionary<Shader, string>();

            foreach (MaterialLockEntry entry in entries)
            {
                string key;
                if (entry.State == MaterialLockState.Orphaned) key = GroupKeyOrphaned;
                else if (entry.Shader == null) key = GroupKeyUnresolved;
                else
                {
                    string shaderPath;
                    if (!pathByShader.TryGetValue(entry.Shader, out shaderPath))
                    {
                        shaderPath = AssetDatabase.GetAssetPath(entry.Shader);
                        pathByShader[entry.Shader] = shaderPath;
                    }
                    // Keyed by asset, not name: two installed shaders can share a name and must not
                    // share a foldout or a "Lock All".
                    key = "shader:" + shaderPath;
                }

                MaterialLockGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = BuildShaderGroup(key, entry, pathByShader);
                    groups.Add(key, group);
                }
                group.Entries.Add(entry);
            }

            return Order(groups.Values);
        }

        static MaterialLockGroup BuildShaderGroup(string key, MaterialLockEntry first, Dictionary<Shader, string> pathByShader)
        {
            if (key == GroupKeyOrphaned)
                return new MaterialLockGroup
                {
                    Key = key,
                    DisplayName = "Locked shader is missing",
                    Subtitle = "Unlocking puts these back to the shader they were locked from",
                    IsAttentionGroup = true
                };

            if (key == GroupKeyUnresolved)
                return new MaterialLockGroup
                {
                    Key = key,
                    DisplayName = "Original shader unknown",
                    Subtitle = "These are locked but do not record a shader that still exists",
                    IsAttentionGroup = true
                };

            return new MaterialLockGroup
            {
                Key = key,
                DisplayName = first.Shader.name,
                Subtitle = pathByShader[first.Shader],
                PingObject = first.Shader
            };
        }

        static List<MaterialLockGroup> GroupByFolder(IEnumerable<MaterialLockEntry> entries)
        {
            Dictionary<string, MaterialLockGroup> groups = new Dictionary<string, MaterialLockGroup>();

            foreach (MaterialLockEntry entry in entries)
            {
                string dir = Path.GetDirectoryName(entry.AssetPath);
                dir = string.IsNullOrEmpty(dir) ? "/" : dir.Replace('\\', '/');
                string key = "folder:" + dir;

                MaterialLockGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new MaterialLockGroup
                    {
                        Key = key,
                        DisplayName = dir,
                        PingAssetPath = dir
                    };
                    groups.Add(key, group);
                }
                group.Entries.Add(entry);
            }

            return Order(groups.Values);
        }

        static List<MaterialLockGroup> GroupByPrefabAndScene(IEnumerable<MaterialLockEntry> entries, bool includePackages)
        {
            Dictionary<string, List<Owner>> index = GetOwnerIndex(includePackages);

            Dictionary<string, MaterialLockGroup> groups = new Dictionary<string, MaterialLockGroup>();
            MaterialLockGroup unreferenced = null;

            foreach (MaterialLockEntry entry in entries)
            {
                List<Owner> owners;
                if (!index.TryGetValue(entry.AssetPath, out owners) || owners.Count == 0)
                {
                    if (unreferenced == null)
                        unreferenced = new MaterialLockGroup
                        {
                            Key = GroupKeyUnreferenced,
                            DisplayName = "Not used by any prefab or open scene",
                            Subtitle = "Only prefabs under the scanned scope and currently loaded scenes are checked"
                        };
                    unreferenced.Entries.Add(entry);
                    continue;
                }

                // A material used by several prefabs is listed under each of them.
                foreach (Owner owner in owners)
                {
                    MaterialLockGroup group;
                    if (!groups.TryGetValue(owner.Key, out group))
                    {
                        group = new MaterialLockGroup
                        {
                            Key = owner.Key,
                            DisplayName = owner.DisplayName,
                            Subtitle = owner.Subtitle,
                            PingObject = owner.PingObject,
                            PingAssetPath = owner.PingAssetPath
                        };
                        groups.Add(owner.Key, group);
                    }
                    group.Entries.Add(entry);
                }
            }

            List<MaterialLockGroup> ordered = Order(groups.Values);
            if (unreferenced != null) ordered.Add(unreferenced);
            return ordered;
        }

        static List<MaterialLockGroup> Order(IEnumerable<MaterialLockGroup> groups)
        {
            return groups
                .OrderByDescending(g => g.IsAttentionGroup)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        #endregion

        #region Prefab / scene ownership

        struct Owner
        {
            public string Key;
            public string DisplayName;
            public string Subtitle;
            public UnityEngine.Object PingObject;
            public string PingAssetPath;
        }

        static Dictionary<string, List<Owner>> s_ownerIndex;
        static bool s_ownerIndexIncludedPackages;

        // The prefab half is held separately because it is the only expensive part: one recursive
        // AssetDatabase.GetDependencies walk per prefab in the project, which runs into minutes on a large avatar
        // project. It now survives scans and window closes. Prefab owners are identified by asset path and hold no
        // UnityEngine.Object reference, so keeping the map costs a few MB and keeps nothing alive that shouldn't be.
        static Dictionary<string, List<Owner>> s_prefabOwners;
        static bool s_prefabOwnersIncludedPackages;

        /// <summary>
        /// Drops the combined index, so the next lookup re-walks the loaded scenes. The cached prefab walk is
        /// deliberately kept; <see cref="InvalidatePrefabOwners"/> is what discards that.
        /// </summary>
        public static void InvalidateOwnerIndex()
        {
            s_ownerIndex = null;
        }

        /// <summary>Discards the prefab ownership walk too, forcing a full rebuild on the next lookup.</summary>
        public static void InvalidatePrefabOwners()
        {
            s_ownerIndex = null;
            s_prefabOwners = null;
        }

        /// <summary>
        /// Material asset path to the prefabs and scene objects using it. Built on demand, because
        /// walking every prefab's dependencies is only worth paying for in this grouping mode.
        /// </summary>
        static Dictionary<string, List<Owner>> GetOwnerIndex(bool includePackages)
        {
            if (s_ownerIndex != null && s_ownerIndexIncludedPackages == includePackages) return s_ownerIndex;

            Dictionary<string, List<Owner>> prefabOwners = GetPrefabOwners(includePackages);

            // Scene contents change on every edit and cost almost nothing to walk, so that half is rebuilt each
            // time. Copy the prefab half rather than adding to it, or scene owners accumulate inside the cache.
            Dictionary<string, List<Owner>> index = new Dictionary<string, List<Owner>>(prefabOwners.Count);
            foreach (KeyValuePair<string, List<Owner>> pair in prefabOwners)
                index.Add(pair.Key, new List<Owner>(pair.Value));

            AddSceneOwners(index);

            s_ownerIndex = index;
            s_ownerIndexIncludedPackages = includePackages;
            return index;
        }

        static Dictionary<string, List<Owner>> GetPrefabOwners(bool includePackages)
        {
            if (s_prefabOwners != null && s_prefabOwnersIncludedPackages == includePackages) return s_prefabOwners;

            Dictionary<string, List<Owner>> owners = new Dictionary<string, List<Owner>>();
            bool completed;
            try
            {
                completed = AddPrefabOwners(owners, includePackages);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // A cancelled walk leaves a partial map. It still answers this lookup, but caching it would hide
            // prefab owners for the rest of the session - previously the next scan papered over that.
            if (!completed) return owners;

            s_prefabOwners = owners;
            s_prefabOwnersIncludedPackages = includePackages;
            return owners;
        }

        /// <summary>Returns false when the user cancelled, which leaves <paramref name="index"/> partly filled.</summary>
        static bool AddPrefabOwners(Dictionary<string, List<Owner>> index, bool includePackages)
        {
            string[] guids = includePackages
                ? AssetDatabase.FindAssets("t:Prefab")
                : AssetDatabase.FindAssets("t:Prefab", AssetsOnly);

            for (int i = 0; i < guids.Length; i++)
            {
                if ((i & 15) == 0 &&
                    EditorUtility.DisplayCancelableProgressBar("Mapping materials to prefabs", $"{i} / {guids.Length}", (float)i / guids.Length))
                    return false;

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;

                Owner owner = new Owner
                {
                    Key = "prefab:" + path,
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    Subtitle = path,
                    PingAssetPath = path
                };

                // Recursive, so materials reached through an animator controller's clips (avatar
                // material swaps) are included - the same set locking an avatar root would touch.
                // Dependencies are read from the database rather than by loading the prefab.
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                    if (dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        Add(index, dependency, owner);
            }

            return true;
        }

        static void AddSceneOwners(Dictionary<string, List<Owner>> index)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                string scenePath = string.IsNullOrEmpty(scene.path) ? "Untitled scene" : scene.path;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Owner owner = new Owner
                    {
                        Key = "scene:" + scenePath + "/" + root.name,
                        DisplayName = root.name,
                        Subtitle = scenePath,
                        PingObject = root
                    };

                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (Material m in renderer.sharedMaterials)
                        {
                            if (m == null) continue;

                            string path = AssetDatabase.GetAssetPath(m);
                            // Materials embedded in the scene have no asset path and are never scanned.
                            if (!string.IsNullOrEmpty(path)) Add(index, path, owner);
                        }
                    }
                }
            }
        }

        static void Add(Dictionary<string, List<Owner>> index, string materialPath, Owner owner)
        {
            List<Owner> owners;
            if (!index.TryGetValue(materialPath, out owners))
            {
                owners = new List<Owner>();
                index.Add(materialPath, owners);
            }
            // The same prefab reaches a material through several dependency paths often enough to matter.
            for (int i = 0; i < owners.Count; i++)
                if (owners[i].Key == owner.Key) return;

            owners.Add(owner);
        }

        /// <summary>
        /// Keeps the cached prefab walk honest without paying for it on every scan. Only changes that can move a
        /// prefab -> material edge count. Re-saving a material is deliberately NOT one of them: locking, unlocking
        /// and version upgrades rewrite materials constantly, and discarding the walk each time is what made the
        /// Material Upgrade Utility crawl. A material's own edits can't change which prefabs reference it - only
        /// its path can, which shows up as a delete or a move.
        /// </summary>
        class OwnerIndexWatcher : AssetPostprocessor
        {
            // Assets that can start referencing a different material when their CONTENT changes.
            static readonly string[] Referencing = { ".prefab", ".controller", ".overridecontroller", ".anim" };
            // Assets whose PATH is part of the index, so creating, deleting or moving one invalidates it.
            static readonly string[] Indexed = { ".prefab", ".mat" };

            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (AnyOf(imported, Referencing) || AnyOf(deleted, Indexed) ||
                    AnyOf(moved, Indexed) || AnyOf(movedFrom, Indexed))
                    InvalidatePrefabOwners();
            }

            static bool AnyOf(string[] paths, string[] extensions)
            {
                for (int i = 0; i < paths.Length; i++)
                    for (int e = 0; e < extensions.Length; e++)
                        if (paths[i].EndsWith(extensions[e], StringComparison.OrdinalIgnoreCase))
                            return true;
                return false;
            }
        }

        #endregion
    }
}
