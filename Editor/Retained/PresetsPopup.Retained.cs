using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class PresetsPopupGUI
    {
        sealed class BrowserEntry
        {
            internal string Name, Path, Category, Guid;
            internal Material Material;
            internal VisualElement Row;
            internal BrowserFolder Folder;
        }

        sealed class BrowserFolder
        {
            internal string Name, Path;
            internal BrowserFolder Parent;
            internal VisualElement Node, Row, Children;
            internal bool Expanded;
            internal readonly SortedDictionary<string, BrowserFolder> Folders = new SortedDictionary<string, BrowserFolder>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<BrowserEntry> Entries = new List<BrowserEntry>();
        }

        readonly List<BrowserEntry> _browserEntries = new List<BrowserEntry>();
        readonly List<BrowserFolder> _browserFolders = new List<BrowserFolder>();
        bool _browserFiltering;
        Material[] _browserTargets;
        Shader _browserShader;
        // Each target's shader when the browser opened. A selection can mix Poiyomi variants.
        Shader[] _browserTargetShaders;
        bool _browserAssetsDirty, _browserSized;
        ToolbarSearchField _browserSearch;
        ScrollView _browserList;
        Label _browserEmpty, _browserSummary;
        ScrollView _browserChangesList;
        VisualElement _browserChanges;
        Button _browserChangesToggle;
        bool _browserChangesOpen;
        Button _browserApply;
        IVisualElementScheduledItem _browserWatch;
        string _browserStateSignature;
        static string PresetText(string key, string fallback) => RetainedText.Get(key, fallback);

        void InitializeBrowser(List<string> names, List<string> guids)
        {
            _browserEntries.Clear();
            _browserTargets = shaderEditor.Materials.ToArray();
            _browserShader = shaderEditor.Shader;
            _browserTargetShaders = _browserTargets.Select(m => m != null ? SectionLock.GetSourceShader(m.shader) : null).ToArray();
            for (int i = 0; i < names.Count && i < guids.Count; i++)
            {
                string path = names[i] ?? "";
                int slash = path.LastIndexOf('/');
                _browserEntries.Add(new BrowserEntry { Path = path, Name = (slash < 0 ? path : path.Substring(slash + 1)).Trim(),
                    Category = slash < 0 ? "" : path.Substring(0, slash), Guid = guids[i] });
            }
            _browserEntries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }

        static Label BrowserLabel(string text, string className)
        {
            var label = new Label(text); label.AddToClassList(className); return label;
        }

        bool BrowserTargetsAvailable()
        {
            return shaderEditor != null && _browserTargets != null && _browserTargets.Length > 0
                && shaderEditor.Materials != null && shaderEditor.Materials.SequenceEqual(_browserTargets)
                && shaderEditor.Shader == _browserShader
                && _browserTargets.Select((m, i) => m != null && SectionLock.GetSourceShader(m.shader) == _browserTargetShaders[i] && !m.IsLocked()).All(ok => ok)
                && (_parent == null || CurrentBrowserParent() != null);
        }

        bool CanApplyBrowser() => BrowserTargetsAvailable() && tickedPresets.Count > 0 && tickedPresets.All(m => m != null);

        ShaderPart CurrentBrowserParent()
        {
            if (_parent == null || shaderEditor == null) return _parent;
            // Material-backed sections use their property name; PropertyIdentifier is only set
            // for synthetic parts and is null on ordinary headers. Never match two null IDs.
            string propertyName = _parent.MaterialProperty?.name;
            string identifier = _parent.PropertyIdentifier;
            foreach (var group in BrowserGroups(shaderEditor.RetainedRoot))
            {
                if (ReferenceEquals(group, _parent)) return group;
                if (!string.IsNullOrEmpty(propertyName) && group.MaterialProperty?.name == propertyName) return group;
                if (string.IsNullOrEmpty(propertyName) && !string.IsNullOrEmpty(identifier)
                    && group.PropertyIdentifier == identifier) return group;
            }
            return null;
        }

        static IEnumerable<ShaderGroup> BrowserGroups(ShaderGroup root)
        {
            if (root == null) yield break;
            yield return root;
            foreach (var child in root.Children.OfType<ShaderGroup>())
                foreach (var descendant in BrowserGroups(child)) yield return descendant;
        }

        void OnEnable() { EditorApplication.projectChanged += BrowserAssetsChanged; }
        void OnDisable() { EditorApplication.projectChanged -= BrowserAssetsChanged; _browserWatch?.Pause(); }
        void BrowserAssetsChanged() { _browserAssetsDirty = true; _browserStateSignature = null; }

        void BuildPresetBrowser()
        {
            _retainedStaging = true;
            _browserWatch?.Pause();
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog"); root.AddToClassList("thry-preset-browser");
            RetainedWindow.Shortcuts(root, Close, ApplyStaged, true);
            minSize = new Vector2(520, 300);
            if (!_browserSized)
            {
                // Resizing while CreateGUI runs discards the new tree, so size it next frame.
                _browserSized = true;
                root.schedule.Execute(() =>
                {
                    var current = position;
                    var center = current.x != 0 || current.y != 0 ? current.center : EditorGUIUtility.GetMainWindowPosition().center;
                    position = new Rect(center.x - 310, center.y - 220, 620, 440);
                });
            }
            if (mainStruct == null || shaderEditor == null)
            {
                root.Add(BrowserLabel(PresetText("preset_reopen", "Reopen Presets from the material inspector."), "thry-preset-muted"));
                root.Add(new Button(Close) { text = PresetText("close", "Close") }); return;
            }

            var heading = new VisualElement(); heading.AddToClassList("thry-preset-browser-header"); root.Add(heading);
            string title = _parent == null ? PresetText("presets", "Presets") : RetainedMaterialBody.SectionCaption(_parent).TrimEnd('*');
            heading.Add(BrowserLabel(title, "thry-title"));
            heading.Add(BrowserLabel(_browserTargets.Length == 1 ? _browserTargets[0].name
                : _browserTargets.Length + " " + PresetText("materials", "materials"), "thry-preset-muted"));
            _browserChangesOpen = RetainedUiState.Get("presets", "/changes", true);
            _browserChangesToggle = new Button(() =>
            {
                _browserChangesOpen = !_browserChangesOpen;
                RetainedUiState.Set("presets", "/changes", _browserChangesOpen);
                _browserStateSignature = null; UpdatePreview();
            }) { name = "preset-changes-toggle", text = PresetText("preset_changes", "Changes"), tooltip = PresetText("preset_changes_hint", "Show what the picked presets change") };
            _browserChangesToggle.AddToClassList("thry-preset-changes-toggle"); heading.Add(_browserChangesToggle);

            var body = new VisualElement(); body.AddToClassList("thry-preset-browser-body"); root.Add(body);
            var library = new VisualElement(); library.AddToClassList("thry-preset-browser-library"); body.Add(library);
            _browserSearch = RetainedWindow.Search(PresetText("search_presets", "Search presets…")); _browserSearch.name = "preset-search";
            _browserSearch.AddToClassList("thry-preset-browser-search"); library.Add(_browserSearch);

            _browserList = new ScrollView(ScrollViewMode.Vertical) { name = "preset-library-list" };
            _browserList.AddToClassList("thry-preset-browser-list"); library.Add(_browserList);
            _browserFolders.Clear();
            var tree = new BrowserFolder { Children = _browserList.contentContainer, Expanded = true };
            foreach (var entry in _browserEntries)
            {
                var folder = tree;
                foreach (string segment in entry.Category.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = segment.Trim();
                    if (!folder.Folders.TryGetValue(name, out var child))
                        folder.Folders.Add(name, child = new BrowserFolder { Name = name, Parent = folder, Path = folder.Path == null ? name : folder.Path + "/" + name });
                    folder = child;
                }
                entry.Folder = folder; folder.Entries.Add(entry);
            }
            BuildBrowserFolder(tree);
            _browserEmpty = BrowserLabel("", "thry-preset-empty"); _browserEmpty.name = "preset-empty"; _browserList.Add(_browserEmpty);

            // The second column shows what the picked presets change.
            _browserChanges = new VisualElement { name = "preset-changes" }; _browserChanges.AddToClassList("thry-preset-changes"); body.Add(_browserChanges);
            _browserChanges.Add(BrowserLabel(PresetText("preset_changes", "Changes"), "thry-preset-changes-title"));
            _browserChangesList = new ScrollView(ScrollViewMode.Vertical) { name = "preset-changes-list" };
            _browserChangesList.AddToClassList("thry-preset-changes-list"); _browserChanges.Add(_browserChangesList);

            var footer = new VisualElement(); footer.AddToClassList("thry-preset-browser-footer"); root.Add(footer);
            _browserSummary = BrowserLabel("", "thry-preset-summary"); _browserSummary.name = "preset-summary"; footer.Add(_browserSummary);
            footer.Add(new Button(Close) { name = "preset-cancel", text = PresetText("cancel", "Cancel") });
            _browserApply = new Button(ApplyStaged) { name = "preset-apply" }; _browserApply.AddToClassList("thry-primary-action"); footer.Add(_browserApply);
            _browserSearch.RegisterValueChangedCallback(e => FilterBrowser());
            FilterBrowser(); _browserStateSignature = null; UpdatePreview();
            _browserSearch.schedule.Execute(() => _browserSearch.Q<TextField>()?.Focus());
            _browserWatch = root.schedule.Execute(UpdatePreview).Every(400);
        }

        // Folders come before presets at every level, each group in name order.
        void BuildBrowserFolder(BrowserFolder folder)
        {
            foreach (var child in folder.Folders.Values)
            {
                var captured = child;
                string scope = "presets:" + _collection, key = "/" + child.Path;
                child.Expanded = RetainedUiState.Get(scope, key, false);
                child.Node = new VisualElement { name = "preset-folder-" + child.Path }; child.Node.AddToClassList("thry-preset-folder");
                child.Row = new VisualElement(); child.Row.AddToClassList("thry-preset-folder-row");
                var arrow = BrowserLabel("▸", "thry-preset-arrow"); arrow.pickingMode = PickingMode.Ignore; child.Row.Add(arrow);
                child.Row.Add(BrowserLabel(child.Name, "thry-preset-folder-name"));
                child.Row.RegisterCallback<ClickEvent>(e =>
                {
                    captured.Expanded = !captured.Expanded;
                    if (!_browserFiltering) RetainedUiState.Set(scope, key, captured.Expanded);
                    UpdateBrowserFolder(captured, captured.Expanded);
                });
                child.Children = new VisualElement(); child.Children.AddToClassList("thry-preset-folder-children");
                child.Node.Add(child.Row); child.Node.Add(child.Children); folder.Children.Add(child.Node);
                _browserFolders.Add(child);
                BuildBrowserFolder(child);
                UpdateBrowserFolder(child, child.Expanded);
            }
            foreach (var entry in folder.Entries)
            {
                entry.Material = string.IsNullOrEmpty(entry.Guid) ? null : Presets.GetPresetMaterial(entry.Guid);
                var item = entry;
                var row = new VisualElement { name = "preset-row-" + entry.Guid, tooltip = entry.Path };
                row.AddToClassList("thry-preset-row");
                row.Add(BrowserLabel(entry.Name, "thry-preset-name"));
                var check = BrowserLabel("✓", "thry-preset-check"); check.pickingMode = PickingMode.Ignore; row.Add(check);
                // A click picks or unpicks a preset; a double click applies just that one.
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (item.Material == null || !BrowserTargetsAvailable()) return;
                    if (e.clickCount == 2) { tickedPresets.Clear(); tickedPresets.Add(item.Material); ApplyStaged(); return; }
                    TogglePreset(item.Material, !tickedPresets.Contains(item.Material));
                });
                entry.Row = row; folder.Children.Add(row);
            }
        }

        static void UpdateBrowserFolder(BrowserFolder folder, bool open)
        {
            folder.Node.EnableInClassList("thry-preset-folder-open", open);
            folder.Children.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void FilterBrowser()
        {
            if (_browserSearch == null) return;
            var tokens = (_browserSearch.value ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _browserFiltering = tokens.Length > 0;
            var visibleFolders = new HashSet<BrowserFolder>();
            int count = 0;
            foreach (var entry in _browserEntries)
            {
                bool shown = tokens.All(t => entry.Path.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                entry.Row.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown) continue;
                count++;
                for (var folder = entry.Folder; folder?.Node != null; folder = folder.Parent) visibleFolders.Add(folder);
            }
            // Searching opens every folder with a match; clearing the search restores the saved layout.
            foreach (var folder in _browserFolders)
            {
                folder.Node.style.display = visibleFolders.Contains(folder) ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateBrowserFolder(folder, _browserFiltering || folder.Expanded);
            }
            _browserEmpty.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _browserEmpty.text = _browserEntries.Count == 0 ? PresetText("preset_library_empty", "No presets here yet.")
                : PresetText("preset_search_empty", "Nothing matches that search.");
        }

        string BrowserStateSignature()
        {
            Func<Material, string> key = m => m == null ? "missing" : m.GetObjectId() + ":" + EditorUtility.GetDirtyCount(m) + ":" + SectionLock.GetSourceShader(m.shader).GetObjectId();
            return BrowserTargetsAvailable() + "|" + _browserChangesOpen + "|" + string.Join(";", (_browserTargets ?? Array.Empty<Material>()).Select(key))
                + "|" + string.Join(";", tickedPresets.Select(key));
        }

        void RefreshPresetPreview()
        {
            if (_browserList == null) return;
            if (_browserAssetsDirty)
            {
                _browserAssetsDirty = false;
                foreach (var entry in _browserEntries)
                    entry.Material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(entry.Guid));
                tickedPresets.RemoveAll(m => m == null);
            }
            string signature = BrowserStateSignature();
            if (_browserStateSignature == signature) return;
            _browserStateSignature = signature;
            bool available = BrowserTargetsAvailable();
            foreach (var entry in _browserEntries)
            {
                bool selected = entry.Material != null && tickedPresets.Contains(entry.Material);
                entry.Row.EnableInClassList("thry-preset-row-selected", selected);
                entry.Row.SetEnabled(entry.Material != null && available);
            }
            // Presets apply in the order they were picked, so the footer lists them that way.
            _browserSummary.text = string.Join(" + ", tickedPresets.Select(m => _browserEntries.FirstOrDefault(e => e.Material == m)?.Name ?? m.name));
            _browserApply.text = tickedPresets.Count > 1 ? string.Format(PresetText("apply_preset_count", "Apply {0}"), tickedPresets.Count) : PresetText("apply", "Apply");
            bool valid = CanApplyBrowser();
            _browserApply.SetEnabled(valid);

            _browserChangesToggle.EnableInClassList("thry-preset-changes-toggle-on", _browserChangesOpen);
            _browserChanges.style.display = _browserChangesOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _browserChangesList.Clear();
            if (!_browserChangesOpen) return;
            if (!valid) { _browserChangesList.Add(BrowserLabel(PresetText("preset_changes_empty", "Pick a preset to see what it changes."), "thry-preset-change-row")); return; }
            var changes = Presets.PreviewChanges(shaderEditor, _browserTargets, tickedPresets, CurrentBrowserParent());
            if (changes.Count == 0) _browserChangesList.Add(BrowserLabel(PresetText("preset_no_changes", "Nothing changes on this material."), "thry-preset-change-row"));
            foreach (var change in changes) _browserChangesList.Add(BrowserLabel(change, "thry-preset-change-row"));
        }
    }
}
