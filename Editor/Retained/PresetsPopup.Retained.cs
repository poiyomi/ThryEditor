#if UNITY_2021_3_OR_NEWER
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
            internal Toggle Row;
            internal readonly List<BrowserBranch> Ancestors = new List<BrowserBranch>();
        }

        sealed class BrowserBranch
        {
            internal string Path;
            internal Foldout Foldout;
            internal bool Expanded;
        }

        readonly List<BrowserEntry> _browserEntries = new List<BrowserEntry>();
        readonly Dictionary<string, BrowserBranch> _browserBranches = new Dictionary<string, BrowserBranch>(StringComparer.Ordinal);
        bool _browserFiltering;
        Material[] _browserTargets;
        Shader _browserShader;
        bool _browserAssetsDirty;
        ToolbarSearchField _browserSearch;
        DropdownField _browserCategory;
        ScrollView _browserList;
        VisualElement _browserSelection;
        VisualElement _browserBody, _browserLibrary, _browserDetails;
        Label _browserCount, _browserEmpty, _browserSummary, _browserSelectedTitle, _browserChangesTitle, _browserOrderHint;
        Button _browserApply, _browserClear;
        IVisualElementScheduledItem _browserWatch;
        string _browserPreviewSignature;
        static string PresetText(string key, string fallback) => RetainedText.Get(key, fallback);

        void InitializeBrowser(List<string> names, List<string> guids)
        {
            minSize = new Vector2(420, 340);
            position = new Rect(position.x, position.y, 620, 460);
            _browserEntries.Clear();
            _browserTargets = shaderEditor.Materials.ToArray();
            _browserShader = shaderEditor.Shader;
            for (int i = 0; i < names.Count && i < guids.Count; i++)
            {
                string path = names[i] ?? "";
                int slash = path.LastIndexOf('/');
                _browserEntries.Add(new BrowserEntry { Path = path, Name = slash < 0 ? path : path.Substring(slash + 1),
                    Category = slash < 0 ? "" : path.Substring(0, slash), Guid = guids[i] });
            }
            _browserEntries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
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
                && _browserTargets.All(m => m != null && m.shader == _browserShader && !m.IsLocked())
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
        void BrowserAssetsChanged() { _browserAssetsDirty = true; _browserPreviewSignature = null; }

        void BuildPresetBrowser()
        {
            _retainedStaging = true;
            _browserWatch?.Pause();
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog"); root.AddToClassList("thry-preset-browser");
            RetainedWindow.Shortcuts(root, Close, ApplyStaged, true);
            minSize = new Vector2(420, 340);
            if (mainStruct == null || shaderEditor == null)
            {
                root.Add(BrowserLabel(PresetText("preset_reopen", "Reopen Presets from the material inspector."), "thry-preset-muted"));
                root.Add(new Button(Close) { text = PresetText("close", "Close") }); return;
            }

            var heading = new VisualElement(); heading.AddToClassList("thry-preset-browser-header"); root.Add(heading);
            heading.Add(BrowserLabel(PresetText("presets", "Presets"), "thry-title"));
            string scope = _parent == null ? shaderEditor.Shader.name.Split('/').Last() : RetainedMaterialBody.SectionCaption(_parent);
            heading.Add(BrowserLabel(scope + " · " + (_browserTargets.Length == 1 ? _browserTargets[0].name
                : _browserTargets.Length + " " + PresetText("materials", "materials")), "thry-preset-muted"));

            var searchRow = new VisualElement(); searchRow.AddToClassList("thry-preset-browser-search"); root.Add(searchRow);
            _browserSearch = RetainedWindow.Search(PresetText("search_presets", "Search presets…")); _browserSearch.name = "preset-search";
            _browserSearch.style.flexGrow = 1; _browserSearch.style.minWidth = 0; searchRow.Add(_browserSearch);
            string all = PresetText("all_categories", "All categories");
            _browserCategory = new DropdownField(new[] { all }.Concat(_browserEntries.SelectMany(e => CategoryPaths(e.Category))
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase)).ToList(), 0) { name = "preset-category" };
            _browserCategory.tooltip = PresetText("preset_category_hint", "Filter presets by their category path.");
            RetainedWindow.Dropdown(_browserCategory); searchRow.Add(_browserCategory);

            _browserBody = new VisualElement(); _browserBody.AddToClassList("thry-preset-browser-body"); root.Add(_browserBody);
            _browserBody.style.flexGrow = 1; _browserBody.style.flexShrink = 1; _browserBody.style.minHeight = 0;
            _browserLibrary = new VisualElement(); _browserLibrary.AddToClassList("thry-preset-browser-library"); _browserBody.Add(_browserLibrary);
            _browserLibrary.style.minHeight = 0; _browserLibrary.style.minWidth = 0;
            _browserCount = BrowserLabel("", "thry-preset-muted"); _browserLibrary.Add(_browserCount);
            _browserList = new ScrollView(ScrollViewMode.Vertical) { name = "preset-library-list" };
            _browserList.AddToClassList("thry-preset-browser-list"); _browserList.style.flexGrow = 1; _browserList.style.minHeight = 0;
            _browserLibrary.Add(_browserList);
            _browserBranches.Clear(); _browserFiltering = false;
            foreach (var entry in _browserEntries)
            {
                entry.Material = string.IsNullOrEmpty(entry.Guid) ? null : Presets.GetPresetMaterial(entry.Guid);
                var item = entry;
                var row = new Toggle { name = "preset-row-" + entry.Guid, tooltip = entry.Path, value = tickedPresets.Contains(entry.Material) };
                row.AddToClassList("thry-preset-browser-row"); row.SetEnabled(entry.Material != null);
                // Native checkbox images include the current Editor skin's box. The light
                // palette draws its own box and tick so it also works in themed hosts.
                var tick = new Label("✓") { pickingMode = PickingMode.Ignore };
                tick.AddToClassList("thry-preset-checkmark");
                row.Q(className: "unity-toggle__checkmark").Add(tick);
                var caption = new VisualElement(); caption.AddToClassList("thry-preset-row-caption");
                caption.Add(BrowserLabel(entry.Name, "thry-preset-caption"));
                row.Add(caption); entry.Row = row; BrowserCategoryContainer(entry).Add(row);
                row.RegisterValueChangedCallback(e => TogglePreset(item.Material, e.newValue));
            }
            _browserEmpty = BrowserLabel("", "thry-preset-muted"); _browserEmpty.name = "preset-empty"; _browserList.Add(_browserEmpty);

            _browserDetails = new VisualElement(); _browserDetails.AddToClassList("thry-preset-browser-details"); _browserBody.Add(_browserDetails);
            _browserDetails.style.minHeight = 0; _browserDetails.style.minWidth = 0;
            var selectionHeader = new VisualElement(); selectionHeader.AddToClassList("thry-preset-selection-header"); _browserDetails.Add(selectionHeader);
            _browserSelectedTitle = BrowserLabel("", "thry-preset-subtitle"); selectionHeader.Add(_browserSelectedTitle);
            _browserClear = new Button(() => { tickedPresets.Clear(); UpdatePreview(); }) { name = "preset-clear", text = PresetText("clear_all", "Clear all") };
            _browserClear.AddToClassList("thry-preset-small-action"); selectionHeader.Add(_browserClear);
            var detailsScroll = new ScrollView(ScrollViewMode.Vertical) { name = "preset-details-scroll" };
            detailsScroll.style.flexGrow = 1; detailsScroll.style.minHeight = 0; _browserDetails.Add(detailsScroll);
            _browserSelection = new VisualElement { name = "preset-selection-list" };
            // The outer details pane owns scrolling; this list expands with its selected rows.
            detailsScroll.Add(_browserSelection);
            _browserOrderHint = BrowserLabel(PresetText("preset_apply_order", "Applied top to bottom. Later presets override earlier ones."), "thry-preset-muted");
            detailsScroll.Add(_browserOrderHint);
            _browserChangesTitle = BrowserLabel(PresetText("preset_preview", "Changes to apply"), "thry-preset-subtitle"); detailsScroll.Add(_browserChangesTitle);
            _preview = new VisualElement { name = "thry-preset-differences" }; _preview.AddToClassList("thry-preset-changes"); detailsScroll.Add(_preview);
            _browserChangesTitle.tooltip = PresetText("preset_preview_note", "Property values are previewed here. Preset actions and linked materials update when you apply.");

            var footer = new VisualElement(); footer.AddToClassList("thry-preset-browser-footer"); root.Add(footer);
            footer.style.flexShrink = 0;
            _browserSummary = BrowserLabel("", "thry-preset-summary"); _browserSummary.name = "preset-summary"; footer.Add(_browserSummary);
            footer.Add(new Button(Close) { name = "preset-cancel", text = PresetText("cancel", "Cancel") });
            _browserApply = new Button(ApplyStaged) { name = "preset-apply" }; _browserApply.AddToClassList("thry-primary-action"); footer.Add(_browserApply);
            _browserSearch.RegisterValueChangedCallback(e => FilterBrowser());
            _browserCategory.RegisterValueChangedCallback(e => FilterBrowser());
            _browserBody.RegisterCallback<GeometryChangedEvent>(e => LayoutBrowser(e.newRect.width));
            LayoutBrowser(position.width - 16); FilterBrowser(); _browserPreviewSignature = null; UpdatePreview();
            _browserWatch = root.schedule.Execute(UpdatePreview).Every(400);
        }

        void LayoutBrowser(float width)
        {
            bool stacked = width < 524;
            rootVisualElement.EnableInClassList("thry-preset-stacked", stacked);
            _browserBody.style.flexDirection = stacked ? FlexDirection.Column : FlexDirection.Row;
            _browserLibrary.style.flexBasis = 0; _browserLibrary.style.flexGrow = stacked ? 1 : 1.05f;
            _browserDetails.style.flexBasis = 0; _browserDetails.style.flexGrow = 1;
        }

        static IEnumerable<string> CategoryPaths(string category)
        {
            if (string.IsNullOrEmpty(category)) yield break;
            string path = "";
            foreach (string segment in category.Split('/'))
            {
                path = path.Length == 0 ? segment : path + "/" + segment;
                yield return path;
            }
        }

        VisualElement BrowserCategoryContainer(BrowserEntry entry)
        {
            VisualElement parent = _browserList;
            entry.Ancestors.Clear();
            foreach (string path in CategoryPaths(entry.Category))
            {
                BrowserBranch branch;
                if (!_browserBranches.TryGetValue(path, out branch))
                {
                    string key = "/" + path;
                    string scope = "presets:" + _collection;
                    var fold = new Foldout { name = "preset-category-" + path, text = path.Split('/').Last(), tooltip = path };
                    fold.AddToClassList("thry-preset-category");
                    branch = new BrowserBranch { Path = path, Foldout = fold, Expanded = RetainedUiState.Get(scope, key, false) };
                    fold.SetValueWithoutNotify(branch.Expanded);
                    var captured = branch;
                    fold.RegisterValueChangedCallback(e =>
                    {
                        // Leaf checkbox events bubble through their category. Only an explicit
                        // unfiltered category toggle changes the user's saved hierarchy state.
                        if (e.target != fold || _browserFiltering) return;
                        captured.Expanded = e.newValue;
                        RetainedUiState.Set(scope, key, e.newValue);
                    });
                    parent.Add(fold); _browserBranches.Add(path, branch);
                }
                entry.Ancestors.Add(branch); parent = branch.Foldout;
            }
            return parent;
        }

        void FilterBrowser()
        {
            if (_browserSearch == null) return;
            var tokens = (_browserSearch.value ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string category = _browserCategory.index > 0 ? _browserCategory.value : "";
            _browserFiltering = tokens.Length > 0 || category.Length > 0;
            var visibleBranches = new HashSet<BrowserBranch>();
            int count = 0;
            foreach (var entry in _browserEntries)
            {
                bool shown = (category.Length == 0 || entry.Category == category || entry.Category.StartsWith(category + "/", StringComparison.Ordinal))
                    && tokens.All(t => entry.Path.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                entry.Row.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (shown) { count++; visibleBranches.UnionWith(entry.Ancestors); }
            }
            foreach (var branch in _browserBranches.Values)
            {
                bool visible = visibleBranches.Contains(branch);
                branch.Foldout.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                branch.Foldout.SetValueWithoutNotify(_browserFiltering ? visible : branch.Expanded);
            }
            _browserCount.text = count + " " + PresetText("presets", "presets");
            _browserEmpty.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _browserEmpty.text = _browserEntries.Count == 0 ? PresetText("preset_library_empty", "No presets are available for this section.")
                : PresetText("preset_search_empty", "No matching presets. Try another search or category.");
        }

        string PreviewSignature()
        {
            Func<Material, string> key = m => m == null ? "missing" : m.GetInstanceID() + ":" + EditorUtility.GetDirtyCount(m) + ":" + m.shader?.GetInstanceID();
            return BrowserTargetsAvailable() + "|" + string.Join(";", (_browserTargets ?? Array.Empty<Material>()).Select(key))
                + "|" + string.Join(";", tickedPresets.Select(key));
        }

        void RefreshPresetPreview()
        {
            if (_preview == null) return;
            if (_browserAssetsDirty)
            {
                _browserAssetsDirty = false;
                foreach (var entry in _browserEntries)
                    entry.Material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(entry.Guid));
            }
            string signature = PreviewSignature();
            if (_browserPreviewSignature == signature) return;
            _browserPreviewSignature = signature;
            foreach (var entry in _browserEntries)
            {
                bool selected = entry.Material != null && tickedPresets.Contains(entry.Material);
                entry.Row?.SetValueWithoutNotify(selected); entry.Row?.EnableInClassList("thry-preset-row-selected", selected);
                entry.Row?.SetEnabled(entry.Material != null && BrowserTargetsAvailable());
            }
            _browserSelection.Clear();
            for (int i = 0; i < tickedPresets.Count; i++)
            {
                var material = tickedPresets[i]; int index = i;
                var row = new VisualElement { name = "preset-selected-" + i }; row.AddToClassList("thry-preset-selected-row"); _browserSelection.Add(row);
                row.Add(BrowserLabel((i + 1).ToString(), "thry-preset-order"));
                var entry = _browserEntries.FirstOrDefault(e => e.Material == material);
                var caption = BrowserLabel(material == null ? PresetText("missing_preset", "Missing preset")
                    : entry?.Name ?? material.name, "thry-preset-caption");
                caption.tooltip = entry?.Path ?? (material == null ? "" : material.name);
                row.Add(caption);
                Action<string, string, string, Action, bool> action = (name, text, tooltip, callback, enabled) => {
                    var button = new Button(callback) { name = name + "-" + index, text = text, tooltip = tooltip };
                    button.AddToClassList("thry-preset-small-action"); button.SetEnabled(enabled); row.Add(button);
                };
                action("preset-up", "↑", PresetText("move_up", "Move up"), () => MovePreset(index, -1), i > 0);
                action("preset-down", "↓", PresetText("move_down", "Move down"), () => MovePreset(index, 1), i < tickedPresets.Count - 1);
                action("preset-remove", "×", PresetText("remove", "Remove"), () => { tickedPresets.RemoveAt(index); UpdatePreview(); }, true);
            }
            if (tickedPresets.Count == 0) _browserSelection.Add(BrowserLabel(PresetText("preset_choose_hint", "Choose presets from the list to see their changes."), "thry-preset-muted"));
            _browserSelectedTitle.text = PresetText("selected", "Selected") + " (" + tickedPresets.Count + ")";
            _browserOrderHint.style.display = tickedPresets.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _browserClear.SetEnabled(tickedPresets.Count > 0);
            _preview.Clear();
            bool valid = CanApplyBrowser();
            var changes = valid ? Presets.PreviewChanges(shaderEditor, _browserTargets, tickedPresets, CurrentBrowserParent()) : new List<string>();
            _browserChangesTitle.text = PresetText("preset_preview", "Changes to apply") + (valid ? " (" + changes.Count + ")" : "");
            if (changes.Count == 0) _preview.Add(BrowserLabel(PresetText("preset_no_changes", "No property changes selected."), "thry-preset-muted"));
            foreach (var change in changes) _preview.Add(BrowserLabel(change, "thry-preset-change-row"));
            _browserSummary.text = !BrowserTargetsAvailable() ? PresetText("preset_targets_unavailable", "Reopen Presets for an unlocked material.")
                : tickedPresets.Any(m => m == null) ? PresetText("preset_missing_hint", "Remove missing presets to continue.")
                : changes.Count + " " + PresetText("property_changes", "property changes");
            _browserApply.text = tickedPresets.Count == 1 ? PresetText("apply_one_preset", "Apply 1 preset")
                : string.Format(PresetText("apply_preset_count", "Apply {0} presets"), tickedPresets.Count);
            _browserApply.SetEnabled(valid);
        }

        void MovePreset(int index, int direction)
        {
            int destination = index + direction;
            if (index < 0 || index >= tickedPresets.Count || destination < 0 || destination >= tickedPresets.Count) return;
            var preset = tickedPresets[index]; tickedPresets.RemoveAt(index); tickedPresets.Insert(destination, preset); UpdatePreview();
        }
    }
}
#endif
