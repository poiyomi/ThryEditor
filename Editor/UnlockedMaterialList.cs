// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Lists every material the shader optimizer can act on, grouped by shader, folder or the
    /// prefab/scene object using it, and locks or unlocks them in bulk.
    ///
    /// The window draws with explicit rects rather than GUILayout, and every mutation is queued and
    /// carried out from <see cref="Update"/>. Locking rewrites assets and shows its own progress bar;
    /// doing that partway through an IMGUI pass changes the control count between Layout and Repaint
    /// and leaves the layout stack unbalanced if it throws, which is what made the old window so
    /// prone to spewing "Getting control N's position in a group with only M controls".
    /// </summary>
    public class UnlockedMaterialsList : EditorWindow
    {
        #region Layout constants

        const float ToolbarHeight = 21f;
        const float SummaryHeight = 22f;
        const float NoticeHeight = 30f;
        const float FooterHeight = 26f;

        const float SectionHeaderHeight = 26f;
        const float GroupHeaderHeight = 24f;
        const float GroupSpacing = 4f;
        const float RowHeight = 20f;
        const float RowIndent = 16f;

        const float EdgePadding = 4f;
        const float ActionWidth = 62f;
        const float BadgeWidth = 78f;
        const float ScrollbarWidth = 16f;

        #endregion

        #region State

        // Survives domain reloads, so a script compile no longer re-scans the whole project.
        [SerializeField] List<MaterialLockEntry> _entries;
        [SerializeField] List<string> _expandedGroupKeys = new List<string>();
        [SerializeField] MaterialLockGrouping _grouping = MaterialLockGrouping.Shader;
        [SerializeField] MaterialLockFilter _filter = MaterialLockFilter.All;
        [SerializeField] bool _includePackages;
        [SerializeField] string _search = "";
        [SerializeField] Vector2 _scroll;
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

        // Queued work, drained in Update so it never runs inside OnGUI.
        [NonSerialized] PendingKind _pendingKind;
        [NonSerialized] List<Material> _pendingMaterials;
        [NonSerialized] bool _pendingConfirm;
        [NonSerialized] bool _pendingRescan;

        // Frozen at Layout so the same controls are drawn for every event of a frame, even if a
        // scroll event moves the view in between.
        [NonSerialized] float _visibleMin;
        [NonSerialized] float _visibleMax;

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

        #region GUI

        void OnGUI()
        {
            float y = DrawToolbar(0);
            y = DrawSummary(y);
            y = DrawNotice(y);

            Rect listRect = new Rect(0, y, position.width, Mathf.Max(0, position.height - y - FooterHeight));
            DrawList(listRect);

            DrawFooter(new Rect(0, position.height - FooterHeight, position.width, FooterHeight));

            HandleShortcuts();
        }

        float DrawToolbar(float y)
        {
            Rect bar = new Rect(0, y, position.width, ToolbarHeight);
            GUI.Label(bar, GUIContent.none, EditorStyles.toolbar);

            float x = EdgePadding;

            Rect refresh = new Rect(x, y, 70, ToolbarHeight);
            if (GUI.Button(refresh, new GUIContent(" Refresh", EditorGUIUtility.IconContent("Refresh").image, "Re-scan the project (F5)"), EditorStyles.toolbarButton))
                _pendingRescan = true;
            x += refresh.width + 2;

            Rect grouping = new Rect(x, y, 132, ToolbarHeight);
            EditorGUI.BeginChangeCheck();
            MaterialLockGrouping newGrouping = (MaterialLockGrouping)EditorGUI.EnumPopup(grouping, _grouping, EditorStyles.toolbarPopup);
            if (EditorGUI.EndChangeCheck())
            {
                _grouping = newGrouping;
                // Rebuilt from Update, because the prefab grouping walks every prefab's dependencies
                // behind a progress bar and that has no business running inside OnGUI.
                _needsViewRebuild = true;
                SavePreferences();
            }
            x += grouping.width + 2;

            Rect filter = new Rect(x, y, 130, ToolbarHeight);
            EditorGUI.BeginChangeCheck();
            MaterialLockFilter newFilter = (MaterialLockFilter)EditorGUI.EnumPopup(filter, _filter, EditorStyles.toolbarPopup);
            if (EditorGUI.EndChangeCheck())
            {
                _filter = newFilter;
                _needsViewRebuild = true;
                SavePreferences();
            }
            x += filter.width + 2;

            // Pinned to the right edge, so the search field can take whatever is left over.
            Rect packages = new Rect(position.width - EdgePadding - 78, y, 78, ToolbarHeight);
            EditorGUI.BeginChangeCheck();
            bool newIncludePackages = GUI.Toggle(packages,
                _includePackages,
                new GUIContent("Packages", "Include materials from packages. Ones in immutable packages are listed but cannot be locked."),
                EditorStyles.toolbarButton);
            if (EditorGUI.EndChangeCheck())
            {
                _includePackages = newIncludePackages;
                _pendingRescan = true;
                SavePreferences();
            }

            Rect search = new Rect(x, y + 2, Mathf.Max(60, packages.x - 4 - x), ToolbarHeight - 4);
            EditorGUI.BeginChangeCheck();
            string newSearch = GUI.TextField(search, _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                // Filters the already scanned results, so typing never triggers a re-scan.
                _search = newSearch;
                _needsViewRebuild = true;
            }

            return y + ToolbarHeight;
        }

        float DrawSummary(float y)
        {
            Rect bar = new Rect(0, y, position.width, SummaryHeight);
            EditorGUI.DrawRect(bar, HeaderBackground);

            Rect label = new Rect(bar.x + EdgePadding + 2, bar.y, bar.width - EdgePadding * 2, bar.height);

            if (!_hasScanned)
            {
                GUI.Label(label, "Not scanned yet.", EditorStyles.miniLabel);
                return y + SummaryHeight;
            }

            GUI.Label(label, _summaryText, EditorStyles.miniLabel);
            return y + SummaryHeight;
        }

        /// <summary>
        /// A strip for the two states worth interrupting over: results known to be out of date, and a
        /// scan the user cancelled partway.
        /// </summary>
        float DrawNotice(float y)
        {
            string message = null;
            if (_hasScanned && _scannedVersion != s_databaseVersion)
                message = "Materials have changed since this scan.";
            else if (_scanCancelled)
                message = "The last scan was cancelled, so this list is incomplete.";

            if (message == null) return y;

            Rect bar = new Rect(0, y, position.width, NoticeHeight);
            EditorGUI.DrawRect(bar, AttentionBackground);

            Rect button = new Rect(bar.xMax - 84 - EdgePadding, bar.y + 5, 84, NoticeHeight - 10);
            if (GUI.Button(button, "Re-scan", EditorStyles.miniButton))
                _pendingRescan = true;

            Rect label = new Rect(bar.x + EdgePadding + 2, bar.y, button.x - bar.x - EdgePadding * 2, bar.height);
            GUI.Label(label, message, EditorStyles.label);

            return y + NoticeHeight;
        }

        void DrawList(Rect area)
        {
            if (!_hasScanned)
            {
                DrawCenteredNotice(area, "Scan the project to list every material the optimizer can lock.", "Scan Project");
                return;
            }

            if (_groups.Count == 0)
            {
                DrawCenteredNotice(area, EmptyMessage(), null);
                return;
            }

            // Computed rather than measured, so scrolling and row skipping need no layout pass.
            // Must stay in step with the draw loop below, including where section bands fall.
            float contentHeight = 0;
            string heightSection = null;
            for (int i = 0; i < _groups.Count; i++)
            {
                if (StartsSection(_groups[i], heightSection)) contentHeight += SectionHeaderHeight;
                heightSection = _groups[i].Section;

                contentHeight += GroupHeaderHeight + GroupSpacing;
                if (IsExpanded(_groups[i].Key)) contentHeight += _groups[i].Entries.Count * RowHeight;
            }

            float contentWidth = area.width - (contentHeight > area.height ? ScrollbarWidth : 0);
            Rect content = new Rect(0, 0, contentWidth, contentHeight);

            _scroll = GUI.BeginScrollView(area, _scroll, content);

            if (Event.current.type == EventType.Layout)
            {
                _visibleMin = _scroll.y - RowHeight;
                _visibleMax = _scroll.y + area.height + RowHeight;
            }

            float y = 0;
            string section = null;
            foreach (MaterialLockGroup group in _groups)
            {
                if (StartsSection(group, section))
                {
                    Rect band = new Rect(0, y, contentWidth, SectionHeaderHeight);
                    if (IsVisible(band.y, band.height)) DrawSectionHeader(band, group);
                    y += SectionHeaderHeight;
                }
                section = group.Section;

                Rect header = new Rect(0, y, contentWidth, GroupHeaderHeight);
                if (IsVisible(header.y, header.height)) DrawGroupHeader(header, group);
                y += GroupHeaderHeight;

                if (IsExpanded(group.Key))
                {
                    for (int i = 0; i < group.Entries.Count; i++)
                    {
                        Rect row = new Rect(0, y, contentWidth, RowHeight);
                        // Rows far off screen are skipped entirely - identically in every event of the
                        // frame, so the control id sequence stays consistent.
                        if (IsVisible(row.y, row.height)) DrawRow(row, group.Entries[i], i);
                        y += RowHeight;
                    }
                }

                y += GroupSpacing;
            }

            GUI.EndScrollView();
        }

        bool IsVisible(float y, float height)
        {
            return y + height >= _visibleMin && y <= _visibleMax;
        }

        /// <summary>
        /// True for the first group of each run of a section. Pure function of the group list, so the
        /// height pass and the draw pass always agree on where the bands go.
        /// </summary>
        static bool StartsSection(MaterialLockGroup group, string previousSection)
        {
            return group.Section != null && group.Section != previousSection;
        }

        void DrawSectionHeader(Rect r, MaterialLockGroup group)
        {
            EditorGUI.DrawRect(r, SectionBackground);

            // A rule along the bottom, so the band reads as a divider and not another group header.
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), SectionRule);

            Rect label = new Rect(r.x + EdgePadding + 2, r.y, r.width - EdgePadding * 2, r.height);
            GUI.Label(label, $"{group.Section}  ({group.SectionCount})", EditorStyles.boldLabel);
        }

        string EmptyMessage()
        {
            if (!string.IsNullOrWhiteSpace(_search)) return $"No materials match \"{_search}\".";

            switch (_filter)
            {
                case MaterialLockFilter.Unlocked: return "No unlocked materials.";
                case MaterialLockFilter.Locked: return "No locked materials.";
                case MaterialLockFilter.NeedsAttention: return "Nothing needs attention.";
                default: return "No materials using an optimizer-capable shader were found.";
            }
        }

        void DrawCenteredNotice(Rect area, string message, string buttonText)
        {
            float height = buttonText == null ? 40 : 74;
            Rect box = new Rect(area.x + 20, area.y + Mathf.Max(10, (area.height - height) / 2), Mathf.Max(120, area.width - 40), height);

            // Not the rich text style: the message can quote the user's search term verbatim.
            Rect label = new Rect(box.x, box.y, box.width, 40);
            GUI.Label(label, message, CenteredNoticeStyle);

            if (buttonText == null) return;

            Rect button = new Rect(box.x + box.width / 2 - 70, box.y + 44, 140, 24);
            if (GUI.Button(button, buttonText)) _pendingRescan = true;
        }

        void DrawGroupHeader(Rect r, MaterialLockGroup group)
        {
            bool expanded = IsExpanded(group.Key);
            EditorGUI.DrawRect(r, group.IsAttentionGroup ? AttentionBackground : HeaderBackground);

            int lockable = group.LockableTargets;
            int unlockable = group.UnlockableTargets;

            // Actions are drawn before the header's own click area so they receive the click first.
            float actionsX = r.xMax - EdgePadding - ActionWidth * 2 - 2;

            Rect lockAll = new Rect(actionsX, r.y + 3, ActionWidth, r.height - 6);
            using (new EditorGUI.DisabledScope(lockable == 0))
                if (GUI.Button(lockAll, new GUIContent("Lock All", lockable == 0 ? "Nothing here can be locked" : $"Lock {lockable} material(s)"), EditorStyles.miniButtonLeft))
                    Enqueue(PendingKind.Lock, group.Entries);

            Rect unlockAll = new Rect(lockAll.xMax + 2, lockAll.y, ActionWidth, lockAll.height);
            using (new EditorGUI.DisabledScope(unlockable == 0))
                if (GUI.Button(unlockAll, new GUIContent("Unlock All", unlockable == 0 ? "Nothing here can be unlocked" : $"Unlock {unlockable} material(s)"), EditorStyles.miniButtonRight))
                    Enqueue(PendingKind.Unlock, group.Entries);

            float titleRight = actionsX - 4;

            if (group.PingObject != null || !string.IsNullOrEmpty(group.PingAssetPath))
            {
                Rect ping = new Rect(titleRight - 18, r.y + 4, 16, 16);
                if (GUILib.ButtonWithCursor(ping, Icons.search, "Show in the project"))
                    Ping(group);
                titleRight = ping.x - 4;
            }

            Rect toggle = new Rect(r.x, r.y, Mathf.Max(0, titleRight - r.x), r.height);
            EditorGUIUtility.AddCursorRect(toggle, MouseCursor.Link);
            if (GUI.Button(toggle, GUIContent.none, GUIStyle.none))
                SetExpanded(group.Key, !expanded);

            Rect foldout = new Rect(r.x + 4, r.y + 4, 14, 16);
            if (Event.current.type == EventType.Repaint)
                EditorStyles.foldout.Draw(foldout, false, false, expanded, false);

            const float CountsWidth = 46f;
            Rect title = new Rect(r.x + 20, r.y, Mathf.Max(0, toggle.xMax - r.x - 20 - CountsWidth), r.height);
            GUI.Label(title, new GUIContent(group.DisplayName, group.Subtitle), EditorStyles.boldLabel);

            Rect countsRect = new Rect(toggle.xMax - CountsWidth, r.y, CountsWidth, r.height);
            GUI.Label(countsRect, group.Entries.Count.ToString(), Styles.label_property_note);
        }

        void DrawRow(Rect r, MaterialLockEntry entry, int index)
        {
            if ((index & 1) == 1) EditorGUI.DrawRect(r, RowAlternate);

            Rect action = new Rect(r.xMax - EdgePadding - ActionWidth, r.y + 1, ActionWidth, r.height - 2);

            if (entry.State == MaterialLockState.Unlocked)
            {
                using (new EditorGUI.DisabledScope(!entry.CanLock))
                    if (GUI.Button(action, new GUIContent("Lock", ActionTooltip(entry, true)), EditorStyles.miniButton))
                        Enqueue(PendingKind.Lock, new[] { entry });
            }
            else
            {
                using (new EditorGUI.DisabledScope(!entry.CanUnlock))
                    if (GUI.Button(action, new GUIContent(entry.State == MaterialLockState.Orphaned ? "Recover" : "Unlock", ActionTooltip(entry, false)), EditorStyles.miniButton))
                        Enqueue(PendingKind.Unlock, new[] { entry });
            }

            Rect badge = new Rect(action.x - BadgeWidth - 4, r.y, BadgeWidth, r.height);
            DrawBadge(badge, entry);

            Rect field = new Rect(r.x + RowIndent, r.y + 1, Mathf.Max(60, badge.x - r.x - RowIndent - 4), r.height - 2);
            EditorGUI.ObjectField(field, entry.Material, typeof(Material), false);
        }

        void DrawBadge(Rect r, MaterialLockEntry entry)
        {
            string text = null;
            string tooltip = null;
            bool warn = true;

            if (entry.IsReadOnly)
            {
                text = "Read-only";
                tooltip = "This material is in an immutable package and cannot be rewritten.";
            }
            else if (entry.State == MaterialLockState.Orphaned)
            {
                text = "No shader";
                tooltip = entry.Shader != null
                    ? $"The generated shader is gone. Recovering unlocks it back to \"{entry.Shader.name}\"."
                    : "The generated shader is gone and the shader it was locked from is not in this project.";
            }
            else if (entry.Shader == null)
            {
                text = "Unknown shader";
                tooltip = string.IsNullOrEmpty(entry.RecordedShaderName)
                    ? "This material does not record which shader it was locked from."
                    : $"Recorded as \"{entry.RecordedShaderName}\", which is not in this project.";
            }
            else if (entry.IsVariant)
            {
                text = "Variant";
                tooltip = $"A material variant. Locking targets its root, \"{entry.VariantRoot.name}\".";
                warn = false;
            }

            if (text == null) return;

            GUI.Label(r, new GUIContent(text, tooltip), warn ? WarnBadgeStyle : NoteBadgeStyle);
        }

        // Built once. Constructing a GUIStyle per row per frame is the kind of thing that makes a
        // list of a few thousand materials feel sluggish for no reason.
        static GUIStyle s_warnBadgeStyle;
        static GUIStyle s_noteBadgeStyle;

        static GUIStyle WarnBadgeStyle
        {
            get
            {
                if (s_warnBadgeStyle == null)
                    s_warnBadgeStyle = new GUIStyle(Styles.orangeStyle) { alignment = TextAnchor.MiddleRight, fontSize = 10 };
                return s_warnBadgeStyle;
            }
        }

        static GUIStyle NoteBadgeStyle
        {
            get
            {
                if (s_noteBadgeStyle == null)
                    s_noteBadgeStyle = new GUIStyle(Styles.label_property_note) { alignment = TextAnchor.MiddleRight, fontSize = 10 };
                return s_noteBadgeStyle;
            }
        }

        static GUIStyle s_centeredNoticeStyle;

        static GUIStyle CenteredNoticeStyle
        {
            get
            {
                if (s_centeredNoticeStyle == null)
                    s_centeredNoticeStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                return s_centeredNoticeStyle;
            }
        }

        string ActionTooltip(MaterialLockEntry entry, bool locking)
        {
            if (entry.IsReadOnly) return "Materials in immutable packages cannot be changed.";
            if (entry.IsVariant) return $"Applies to the variant root, \"{entry.VariantRoot.name}\".";
            return locking ? "Lock this material" : "Unlock this material";
        }

        void DrawFooter(Rect r)
        {
            GUI.Label(r, GUIContent.none, EditorStyles.toolbar);

            float x = EdgePadding;
            Rect expand = new Rect(x, r.y + 3, 76, r.height - 6);
            if (GUI.Button(expand, "Expand All", EditorStyles.miniButtonLeft)) SetAllExpanded(true);
            x += expand.width;

            Rect collapse = new Rect(x, expand.y, 82, expand.height);
            if (GUI.Button(collapse, "Collapse All", EditorStyles.miniButtonRight)) SetAllExpanded(false);

            // Bulk actions deliberately act on what is currently shown rather than on the whole
            // project, so narrowing the list is also how you narrow the blast radius. These are the
            // only actions that confirm: their reach is whatever the filters happen to be set to,
            // which is not something you can read off the screen at a glance.
            Rect unlockShown = new Rect(r.xMax - EdgePadding - 132, expand.y, 132, expand.height);
            using (new EditorGUI.DisabledScope(_shownUnlockable == 0))
                if (GUI.Button(unlockShown, $"Unlock Shown ({_shownUnlockable})", EditorStyles.miniButtonRight))
                    Enqueue(PendingKind.Unlock, AllShown, confirm: true);

            Rect lockShown = new Rect(unlockShown.x - 122, expand.y, 122, expand.height);
            using (new EditorGUI.DisabledScope(_shownLockable == 0))
                if (GUI.Button(lockShown, $"Lock Shown ({_shownLockable})", EditorStyles.miniButtonLeft))
                    Enqueue(PendingKind.Lock, AllShown, confirm: true);
        }

        void Ping(MaterialLockGroup group)
        {
            UnityEngine.Object target = group.PingObject;
            if (target == null && !string.IsNullOrEmpty(group.PingAssetPath))
                target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(group.PingAssetPath);

            if (target != null) EditorGUIUtility.PingObject(target);
        }

        void HandleShortcuts()
        {
            if (Event.current.type != EventType.KeyDown) return;

            if (Event.current.keyCode == KeyCode.F5 ||
                (Event.current.keyCode == KeyCode.R && (Event.current.control || Event.current.command)))
            {
                _pendingRescan = true;
                Event.current.Use();
            }
        }

        #endregion

        #region Colors

        static Color HeaderBackground
        {
            get { return EditorGUIUtility.isProSkin ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.78f, 0.78f, 0.78f); }
        }

        static Color AttentionBackground
        {
            get { return EditorGUIUtility.isProSkin ? new Color(0.36f, 0.26f, 0.13f) : new Color(0.97f, 0.88f, 0.70f); }
        }

        static Color RowAlternate
        {
            get { return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.03f); }
        }

        static Color SectionBackground
        {
            get { return EditorGUIUtility.isProSkin ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.68f, 0.68f, 0.68f); }
        }

        static Color SectionRule
        {
            get { return EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.5f) : new Color(0f, 0f, 0f, 0.25f); }
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
