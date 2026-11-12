using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class UnlockedMaterialsList
    {
        ScrollView _retainedGroups;
        Label _retainedSummary;
        VisualElement _retainedNotice;
        VisualElement _retainedUnmarked;
        ScrollView _retainedUnmarkedRows;
        Button _retainedRelock;

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-lock-manager");
            var title = new Label("Material lock manager"); title.AddToClassList("thry-title"); root.Add(title);
            var toolbar = new VisualElement(); toolbar.AddToClassList("thry-components"); root.Add(toolbar);
            var search = RetainedWindow.Search("Find materials…"); search.value = _search; search.style.flexGrow = 1; toolbar.Add(search);
            search.RegisterValueChangedCallback(e => { _search = e.newValue; _needsViewRebuild = true; });
            toolbar.Add(new Button(() => _pendingRescan = true) { text = "Refresh", tooltip = "Re-scan the project (F5)" });
            var filters = new VisualElement(); filters.AddToClassList("thry-components"); root.Add(filters);
            var grouping = new DropdownField("Group by", Enum.GetNames(typeof(MaterialLockGrouping)).Select(ObjectNames.NicifyVariableName).ToList(), (int)_grouping); RetainedWindow.Dropdown(grouping); filters.Add(grouping);
            grouping.RegisterValueChangedCallback(e => { _grouping = (MaterialLockGrouping)grouping.index; _needsViewRebuild = true; SavePreferences(); });
            var filter = new DropdownField("Show", Enum.GetNames(typeof(MaterialLockFilter)).Select(ObjectNames.NicifyVariableName).ToList(), (int)_filter); RetainedWindow.Dropdown(filter); filters.Add(filter);
            filter.RegisterValueChangedCallback(e => { _filter = (MaterialLockFilter)filter.index; _needsViewRebuild = true; SavePreferences(); });
            var packages = new Toggle("Include packages") { value = _includePackages }; root.Add(packages);
            packages.RegisterValueChangedCallback(e => { _includePackages = e.newValue; _pendingRescan = true; SavePreferences(); });
            _retainedSummary = new Label(); _retainedSummary.AddToClassList("thry-muted"); root.Add(_retainedSummary);
            _retainedNotice = new VisualElement(); _retainedNotice.AddToClassList("thry-components"); root.Add(_retainedNotice);
            var stale = new HelpBox("Materials have changed since this scan.", HelpBoxMessageType.Info); stale.style.flexGrow = 1; _retainedNotice.Add(stale);
            _retainedNotice.Add(new Button(() => _pendingRescan = true) { text = "Re-scan" });
            _retainedUnmarked = new VisualElement(); _retainedUnmarked.AddToClassList("thry-components"); root.Add(_retainedUnmarked);
            _retainedUnmarkedRows = new ScrollView(); _retainedUnmarkedRows.AddToClassList("thry-lock-unmarked"); root.Add(_retainedUnmarkedRows);
            RefreshRetainedUnmarked();
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); root.Add(actions);
            actions.Add(new Button(() => Enqueue(PendingKind.Lock, AllShown, true)) { text = "Lock shown" });
            actions.Add(new Button(() => Enqueue(PendingKind.Unlock, AllShown, true)) { text = "Unlock shown" });
            _retainedRelock = new Button(() => Enqueue(PendingKind.Relock, AllShown, true)) { text = "Relock outdated", tooltip = "Relock the shown materials that were locked with an older shader version" };
            actions.Add(_retainedRelock);
            actions.Add(new Button(() => { SetAllExpanded(true); RefreshRetainedList(); }) { text = "Expand all" });
            actions.Add(new Button(() => { SetAllExpanded(false); RefreshRetainedList(); }) { text = "Collapse all" });
            _retainedGroups = new ScrollView(); _retainedGroups.style.flexGrow = 1; root.Add(_retainedGroups);
            RefreshRetainedList();
        }

        // Polled from Update; asset changes queue no refresh.
        void UpdateRetainedNotice()
        {
            if (_retainedNotice == null) return;
            DisplayStyle display = _hasScanned && _scannedVersion != s_databaseVersion ? DisplayStyle.Flex : DisplayStyle.None;
            if (_retainedNotice.style.display.value != display) _retainedNotice.style.display = display;
        }

        void RefreshRetainedUnmarked()
        {
            if (_retainedUnmarked == null) return;
            _retainedUnmarked.Clear(); _retainedUnmarkedRows.Clear();
            var items = _unmarked;
            var display = items != null && items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _retainedUnmarked.style.display = display; _retainedUnmarkedRows.style.display = display;
            if (display == DisplayStyle.None) return;
            var notice = new HelpBox(items.Count == 1 ? "1 animated property is not marked" : items.Count + " animated properties are not marked", HelpBoxMessageType.Warning)
                { tooltip = "Animation clips change these properties, but locking bakes them unless they are marked Animated." };
            notice.style.flexGrow = 1; _retainedUnmarked.Add(notice);
            var markable = items.Where(i => !i.Locked).ToList();
            var markAll = new Button(() => _pendingMark = markable) { text = "Mark all" }; markAll.SetEnabled(markable.Count > 0); _retainedUnmarked.Add(markAll);
            _retainedUnmarkedRows.style.height = Mathf.Min(112, items.Count * 28);
            foreach (var item in items)
            {
                if (item.Material == null) continue;
                var row = new VisualElement { tooltip = item.RendererPath + "\n" + item.ClipList(10) }; row.style.height = 28; row.AddToClassList("thry-components"); _retainedUnmarkedRows.Add(row);
                var material = new Button(() => { Selection.activeObject = item.Material; EditorGUIUtility.PingObject(item.Material); }) { text = item.Material.name }; material.style.flexGrow = 1; material.AddToClassList("thry-lock-material-name"); row.Add(material);
                row.Add(new Label(item.Property));
                var clip = new Label(item.ClipList(1)); clip.AddToClassList("thry-muted"); row.Add(clip);
                var action = item.Locked
                    ? new Button(() => { _pendingKind = PendingKind.Unlock; _pendingMaterials = new List<Material> { item.Material }; }) { text = "Unlock", tooltip = "Unlock this material so the property can be marked" }
                    : new Button(() => _pendingMark = new List<AnimatedPropertyCheck.Item> { item }) { text = "Mark", tooltip = item.Renamed ? "Mark as Renamed" : "Mark as Animated" };
                action.style.width = 70; row.Add(action);
            }
        }

        void RefreshRetainedList()
        {
            if (_retainedGroups == null) return;
            UpdateRetainedNotice();
            _retainedSummary.text = _scanCancelled ? _summaryText + " · Scan cancelled; showing partial results" : _summaryText;
            _retainedGroups.Clear();
            _retainedRelock.style.display = _entries != null && _entries.Any(e => e != null && e.CanRelock) ? DisplayStyle.Flex : DisplayStyle.None;
            _retainedRelock.SetEnabled(_groups.Any(g => g.Entries.Any(e => e.CanRelock)));
            if (_entries == null) { _retainedGroups.Add(new Label("Scanning materials…")); return; }
            if (_groups.Count == 0) { _retainedGroups.Add(new Label("No materials match these filters.")); return; }
            string lastSection = null;
            foreach (var group in _groups)
            {
                if (group.Section != lastSection && !string.IsNullOrEmpty(group.Section)) { var band = new Label(group.Section); band.AddToClassList("thry-field-heading"); _retainedGroups.Add(band); }
                lastSection = group.Section;
                var foldout = new Foldout { text = group.DisplayName + " · " + group.Entries.Count, value = IsExpanded(group.Key), tooltip = group.Subtitle };
                _retainedGroups.Add(foldout);
                bool built = false;
                Action build = () =>
                {
                    if (built || !foldout.value) return;
                    built = true;
                    var actions = new VisualElement(); actions.AddToClassList("thry-components"); foldout.Add(actions);
                    var lockGroup = new Button(() => Enqueue(PendingKind.Lock, group.Entries)) { text = "Lock group" }; lockGroup.SetEnabled(group.Entries.Any(e => e.CanLock)); actions.Add(lockGroup);
                    var unlockGroup = new Button(() => Enqueue(PendingKind.Unlock, group.Entries)) { text = "Unlock group" }; unlockGroup.SetEnabled(group.Entries.Any(e => e.CanUnlock)); actions.Add(unlockGroup);
                    actions.Add(new Button(() => Ping(group)) { text = "Show in project" });
                    var list = new ListView(group.Entries, 28, () =>
                    {
                        var row = new VisualElement(); row.AddToClassList("thry-components");
                        var material = new Button(() => { var entry = (MaterialLockEntry)row.userData; Selection.activeObject = entry.Material; EditorGUIUtility.PingObject(entry.Material); }) { name = "material" }; material.style.flexGrow = 1; material.AddToClassList("thry-lock-material-name"); row.Add(material);
                        var state = new Label { name = "state" }; state.style.minWidth = 90; state.style.alignSelf = Align.Center; row.Add(state);
                        var relock = new Button(() => Enqueue(PendingKind.Relock, new[] { (MaterialLockEntry)row.userData })) { name = "relock", text = "Relock", tooltip = "Unlock this material and lock it again with the installed shader version" }; relock.style.width = 70; row.Add(relock);
                        var action = new Button(() => { var entry = (MaterialLockEntry)row.userData; Enqueue(entry.CanLock ? PendingKind.Lock : PendingKind.Unlock, new[] { entry }); }) { name = "action" }; action.style.width = 70; row.Add(action);
                        return row;
                    }, (row, index) =>
                    {
                        var entry = group.Entries[index]; row.userData = entry;
                        row.Q<Button>("material").text = entry.Name; row.tooltip = entry.AssetPath;
                        row.Q<Label>("state").text = entry.IsReadOnly ? "Read-only" : entry.IsVariant ? "Variant" : entry.IsOutdated ? "Locked " + entry.LockedVersion : entry.State.ToString();
                        row.Q<Button>("relock").style.display = entry.CanRelock ? DisplayStyle.Flex : DisplayStyle.None;
                        var action = row.Q<Button>("action"); action.text = entry.CanLock ? "Lock" : entry.State == MaterialLockState.Orphaned ? "Recover" : "Unlock"; action.SetEnabled(entry.CanLock || entry.CanUnlock);
                        action.tooltip = ActionTooltip(entry, entry.CanLock);
                        row.Q<Label>("state").tooltip = entry.IsVariant || entry.IsReadOnly ? action.tooltip
                            : entry.State == MaterialLockState.Orphaned ? "The generated shader is missing. Recover restores the original shader when available."
                            : entry.IsOutdated ? "Locked with shader version " + entry.LockedVersion + ". The installed shader is newer." : entry.State.ToString();
                    });
                    list.selectionType = SelectionType.None;
                    list.style.height = Mathf.Min(420, group.Entries.Count * 28);
                    foldout.Add(list);
                };
                foldout.RegisterValueChangedCallback(e => { if (e.target != foldout) return; SetExpanded(group.Key, e.newValue); build(); });
                build();
            }
        }
    }
}
