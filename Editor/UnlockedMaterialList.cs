// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Lists every material the shader optimizer can act on, grouped by shader, folder or the
    /// prefab/scene object using it, and locks or unlocks them in bulk.
    ///
    /// Every mutation is queued and carried out from <see cref="Update"/>.
    /// </summary>
    public partial class UnlockedMaterialsList : EditorWindow
    {
        #region State

        // Survives domain reloads, so a script compile no longer re-scans the whole project.
        [SerializeField] List<MaterialLockEntry> _entries;
        [SerializeField] List<string> _expandedGroupKeys = new List<string>();
        [SerializeField] MaterialLockGrouping _grouping = MaterialLockGrouping.Shader;
        [SerializeField] MaterialLockFilter _filter = MaterialLockFilter.All;
        [SerializeField] bool _includePackages;
        [SerializeField] string _search = "";
        [SerializeField] bool _hasScanned;
        [SerializeField] bool _scanCancelled;
        [SerializeField] int _scannedVersion;

        [NonSerialized] List<MaterialLockGroup> _groups = new List<MaterialLockGroup>();
        [NonSerialized] HashSet<string> _expanded;
        [NonSerialized] bool _needsViewRebuild = true;

        // Totals over everything currently shown, recounted only when the view is rebuilt.
        [NonSerialized] int _shownLockable;
        [NonSerialized] int _shownUnlockable;
        [NonSerialized] int _shownCount;
        [NonSerialized] string _summaryText = "";

        // Queued work, drained in Update so it never runs inside a UI callback.
        [NonSerialized] PendingKind _pendingKind;
        [NonSerialized] List<Material> _pendingMaterials;
        [NonSerialized] bool _pendingConfirm;
        [NonSerialized] bool _pendingRescan;

        enum PendingKind { None, Lock, Unlock }

        #endregion

        #region Lifecycle

        void OnEnable()
        {
            // ObjectContent always resolves, unlike a named built-in icon that can differ between versions.
            titleContent = new GUIContent("Material Lock Manager", EditorGUIUtility.ObjectContent(null, typeof(Material)).image);
            minSize = new Vector2(560, 260);

            // Before the first scan: the packages preference decides its scope.
            LoadPreferences();
            _needsViewRebuild = true;

            // The version counter is static and so restarts at zero on a domain reload, while the
            // scanned version was serialized. Without this the window would always open claiming
            // its results are stale.
            if (_scannedVersion > s_databaseVersion) _scannedVersion = s_databaseVersion;

            // Scans when genuinely opened for the first time, but not on a domain reload: the results
            // are serialized, so a script compile no longer costs a full project scan.
            if (_entries == null) _pendingRescan = true;
        }

        void OnDisable()
        {
            // Releases the scene half of the ownership index. The prefab half is deliberately kept: rebuilding
            // it means re-walking every prefab's dependencies, which costs minutes on a large project, and it
            // holds only asset paths. MaterialLockScanner invalidates it when an asset change actually needs it.
            MaterialLockScanner.InvalidateOwnerIndex();
        }

        void Update()
        {
            UpdateRetainedNotice();
            if (_pendingKind == PendingKind.None && !_pendingRescan && !_needsViewRebuild) return;

            PendingKind kind = _pendingKind;
            List<Material> materials = _pendingMaterials;
            bool confirm = _pendingConfirm;
            bool rescan = _pendingRescan;

            _pendingKind = PendingKind.None;
            _pendingMaterials = null;
            _pendingConfirm = false;
            _pendingRescan = false;

            if (kind != PendingKind.None && materials != null && materials.Count > 0 && (!confirm || Confirm(kind, materials.Count)))
            {
                try
                {
                    if (kind == PendingKind.Lock)
                        ShaderOptimizer.LockMaterials(materials, ShaderOptimizer.ProgressBar.Cancellable);
                    else
                        ShaderOptimizer.UnlockMaterials(materials, ShaderOptimizer.ProgressBar.Cancellable);
                }
                catch (Exception e)
                {
                    // A failure here must not leave the window unable to refresh itself.
                    ThryLogger.LogErr("Material Lock Manager", $"{(kind == PendingKind.Lock ? "Locking" : "Unlocking")} failed: {e}");
                }
                rescan = true;
            }

            if (rescan) Rescan();
            if (_needsViewRebuild) RebuildView();
            RefreshRetainedList();

            Repaint();
        }

        #endregion

        #region Model

        void Rescan()
        {
            bool cancelled;
            _entries = MaterialLockScanner.Scan(_includePackages, out cancelled);
            _scanCancelled = cancelled;
            _hasScanned = true;
            _scannedVersion = s_databaseVersion;
            _needsViewRebuild = true;

            // Re-importing the materials we just wrote can land a tick after the operation returns.
            // Adopting the version again then stops the window from immediately reporting itself stale
            // because of its own work.
            EditorApplication.delayCall += AdoptDatabaseVersion;
        }

        void AdoptDatabaseVersion()
        {
            EditorApplication.delayCall -= AdoptDatabaseVersion;
            if (this == null) return;

            _scannedVersion = s_databaseVersion;
            Repaint();
        }

        void RebuildView()
        {
            _needsViewRebuild = false;

            if (_entries == null)
            {
                _groups = new List<MaterialLockGroup>();
                _shownLockable = _shownUnlockable = _shownCount = 0;
                _summaryText = "";
                return;
            }

            IEnumerable<MaterialLockEntry> visible = _entries
                // A material can be deleted between the scan and now.
                .Where(e => e != null && e.Material != null)
                .Where(e => MaterialLockScanner.PassesFilter(e, _filter))
                .Where(e => MaterialLockScanner.MatchesSearch(e, _search));

            _groups = MaterialLockScanner.Group(visible, _grouping, _includePackages, _filter == MaterialLockFilter.AllSplit);

            // Counted here, not per frame. In prefab grouping a material appears under every prefab
            // using it, so the totals must be taken over distinct materials.
            List<MaterialLockEntry> shown = _groups.SelectMany(g => g.Entries).Distinct().ToList();
            _shownCount = shown.Count;
            _shownLockable = MaterialLockScanner.CountDistinctTargets(shown, true);
            _shownUnlockable = MaterialLockScanner.CountDistinctTargets(shown, false);

            int total = _entries.Count;
            int locked = _entries.Count(e => e.State != MaterialLockState.Unlocked);
            // Same predicate as the "Needs Attention" filter, so the two never disagree.
            int attention = _entries.Count(e => MaterialLockScanner.PassesFilter(e, MaterialLockFilter.NeedsAttention));

            _summaryText = $"{total} material{(total == 1 ? "" : "s")}  ·  {locked} locked  ·  {total - locked} unlocked";
            if (attention > 0) _summaryText += $"  ·  {attention} need attention";
            if (_shownCount != total) _summaryText += $"     (showing {_shownCount})";
        }

        /// <summary>
        /// Only the "Shown" buttons ask. Everything else names its targets on screen - a row is one
        /// material, a group header is the group you can see - so a dialog would just be a second
        /// click for something the user already pointed at.
        /// </summary>
        bool Confirm(PendingKind kind, int count)
        {
            string plural = count == 1 ? "material" : "materials";

            if (kind == PendingKind.Lock)
                return EditorUtility.DisplayDialog("Lock Materials",
                    $"Lock {count} {plural}?\n\nEach one gets its own generated shader, so this can take a while on a large selection.",
                    "Lock", "Cancel");

            return EditorUtility.DisplayDialog("Unlock Materials",
                $"Unlock {count} {plural}?\n\nUnlocked materials compile every shader feature they expose, so a large selection takes longer to load in the editor.",
                "Unlock", "Cancel");
        }

        /// <summary>
        /// Records what to do without doing it. Materialised immediately, so the queued work is not
        /// a lazy query over collections that the following rebuild is about to replace.
        /// </summary>
        void Enqueue(PendingKind kind, IEnumerable<MaterialLockEntry> entries, bool confirm = false)
        {
            List<Material> targets = MaterialLockScanner.CollectTargets(entries, kind == PendingKind.Lock);
            if (targets.Count == 0) return;

            _pendingKind = kind;
            _pendingMaterials = targets;
            _pendingConfirm = confirm;
        }

        IEnumerable<MaterialLockEntry> AllShown
        {
            get { return _groups.SelectMany(g => g.Entries); }
        }

        #endregion

        #region Expansion

        HashSet<string> Expanded
        {
            get
            {
                if (_expanded == null)
                    _expanded = new HashSet<string>(_expandedGroupKeys ?? new List<string>());
                return _expanded;
            }
        }

        bool IsExpanded(string key)
        {
            return Expanded.Contains(key);
        }

        void SetExpanded(string key, bool value)
        {
            if (value) Expanded.Add(key);
            else Expanded.Remove(key);

            _expandedGroupKeys = Expanded.ToList();
        }

        void SetAllExpanded(bool value)
        {
            Expanded.Clear();
            if (value)
                foreach (MaterialLockGroup g in _groups)
                    Expanded.Add(g.Key);

            _expandedGroupKeys = Expanded.ToList();
        }

        #endregion

        #region Actions

        string ActionTooltip(MaterialLockEntry entry, bool locking)
        {
            if (entry.IsReadOnly) return "Materials in immutable packages cannot be changed.";
            if (entry.IsVariant) return $"Applies to the variant root, \"{entry.VariantRoot.name}\".";
            return locking ? "Lock this material" : "Unlock this material";
        }

        void Ping(MaterialLockGroup group)
        {
            UnityEngine.Object target = group.PingObject;
            if (target == null && !string.IsNullOrEmpty(group.PingAssetPath))
                target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(group.PingAssetPath);

            if (target != null) EditorGUIUtility.PingObject(target);
        }

        [Shortcut("Thry/Material Lock Manager/Re-scan", typeof(UnlockedMaterialsList), KeyCode.F5)]
        static void RescanShortcut(ShortcutArguments args)
        {
            if (args.context is UnlockedMaterialsList window) window._pendingRescan = true;
        }

        #endregion

        #region Preferences

        // Kept in the project's Thry/persistent_data file rather than EditorPrefs, so the choices
        // follow the project instead of the machine - a locking layout that suits an avatar project
        // is rarely the one you want in a world project.
        const string PrefGrouping = "MaterialLockManager.Grouping";
        const string PrefFilter = "MaterialLockManager.Filter";
        const string PrefIncludePackages = "MaterialLockManager.IncludePackages";

        void LoadPreferences()
        {
            _grouping = ReadEnum(PrefGrouping, _grouping);
            _filter = ReadEnum(PrefFilter, _filter);

            string packages = PersistentData.Get(PrefIncludePackages);
            if (!string.IsNullOrEmpty(packages)) _includePackages = packages == "1";
        }

        void SavePreferences()
        {
            PersistentData.Set(PrefGrouping, _grouping.ToString());
            PersistentData.Set(PrefFilter, _filter.ToString());
            PersistentData.Set(PrefIncludePackages, _includePackages ? "1" : "0");
        }

        /// <summary>
        /// Enums are stored by name, not by ordinal: the values are ordered to read well in the
        /// dropdown, and inserting one there must not silently change what somebody had selected.
        /// Anything unrecognised - written by a newer version, or since renamed - falls back.
        /// </summary>
        static T ReadEnum<T>(string key, T fallback) where T : struct
        {
            string raw = PersistentData.Get(key);
            if (string.IsNullOrEmpty(raw)) return fallback;

            T parsed;
            if (!Enum.TryParse(raw, out parsed)) return fallback;

            // TryParse also accepts bare numbers and undefined combinations.
            return Enum.IsDefined(typeof(T), parsed) ? parsed : fallback;
        }

        #endregion

        #region Staleness

        // Bumped whenever a material or shader is imported, deleted or moved, so an open window can
        // say its results are out of date instead of quietly showing the wrong thing.
        static int s_databaseVersion;

        class ChangeWatcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (Touches(imported) || Touches(deleted) || Touches(moved)) s_databaseVersion++;
            }

            static bool Touches(string[] paths)
            {
                foreach (string p in paths)
                    if (p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
        }

        #endregion
    }
}
