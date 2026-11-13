using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>Retained navigation and chrome around the material drawer surface.</summary>
    public sealed class MaterialInspectorView : VisualElement
    {
        private readonly MaterialEditor _editor;
        internal MaterialEditor Editor => _editor;
        private readonly VisualElement _body;
        private IMGUIContainer _fallback;
        private readonly Label _loading = new Label("Loading shader inspector…") { name = "thry-inspector-loading" };
        private RetainedMaterialBody _retained;
        private RetainedCrossSelectionProperties _crossProperties;
        private readonly Renderer[] _rendererContext;
        private Renderer[] _cachedRenderers;
        private readonly ShaderEditor _shaderOverride;
        private readonly Func<MaterialProperty[]> _propertyProvider;
        private readonly VisualElement _chrome, _tools;
        private readonly Label _title, _version, _state, _placeholder;
        private readonly Button _titleButton;
        private readonly Button _versionUpdate;
        const string UpdateVersionKey = "Poiyomi.PackageManager.UpdateVersion";
        const string UpdateMenuItem = "Poi/Pro/Update to Latest";
        private readonly ToolbarSearchField _search;
        private readonly DropdownField _typeFilter;
        private readonly DropdownField _statusFilter;
        static readonly string[] SearchTypes = { "", "t:texture", "t:color", "t:number", "t:vector", "t:toggle" };
        static readonly string[] SearchStates = { "", "is:changed", "is:assigned", "is:empty", "is:animated", "is:missing", "is:favorite" };
        bool _typeFromQuery, _statusFromQuery, _multipleQueryTypes, _multipleQueryStates;
        private readonly VisualElement _typeIcon, _statusIcon;
        private readonly RetainedSearch _propertySearch;
        private readonly RetainedNavigation _navigation;
        private ShaderEditor _shader;
        private bool _builtTools, _toolTheme;
        private readonly IVisualElementScheduledItem _searchRefresh;
        private readonly IVisualElementScheduledItem _rowBandRefresh;
        private bool _searchPending;
        private string _sectionScope;
        private int _localeIndex = -1;
        internal string Text(string key, string fallback) => RetainedText.Get(_shader, key, fallback);

        public MaterialInspectorView(MaterialEditor editor, Action drawInspector, Renderer[] rendererContext = null,
            ShaderEditor shaderOverride = null, Func<MaterialProperty[]> propertyProvider = null)
        {
            _editor = editor;
            _rendererContext = rendererContext;
            _shaderOverride = shaderOverride;
            _propertyProvider = propertyProvider;
            name = "thry-material-inspector";
            _rowBandRefresh = schedule.Execute(UpdateRowBands); _rowBandRefresh.Pause();
            RegisterCallback<GeometryChangedEvent>(e => RequestRowBands());
            AddToClassList("thry-inspector");
            RetainedTheme.Install(this);
            RetainedAppearance.Install(this);
            RetainedAvailability.Install(this);

            _chrome = new VisualElement { name = "thry-chrome" };
            _chrome.AddToClassList("thry-chrome");
            var masthead = new VisualElement(); masthead.AddToClassList("thry-masthead");
            var branding = new VisualElement(); branding.AddToClassList("thry-branding");
            var titleRow = new VisualElement(); titleRow.AddToClassList("thry-title-row");
            _titleButton = new Button(ShowVariants) { name = "thry-title-button" }; _titleButton.AddToClassList("thry-title-button");
            _title = new Label("Material") { pickingMode = PickingMode.Ignore }; _title.AddToClassList("thry-title");
            var titleCaret = RetainedMaterialBody.HeaderIcon("caret-down"); titleCaret.AddToClassList("thry-title-caret");
            _titleButton.Add(_title); _titleButton.Add(titleCaret);
            _version = new Label(); _version.AddToClassList("thry-version");
            _versionUpdate = new Button(() => EditorApplication.ExecuteMenuItem(UpdateMenuItem));
            _versionUpdate.AddToClassList("thry-version-update");
            _versionUpdate.generateVisualContent += context => {
                var center = _versionUpdate.contentRect.center;
                var painter = context.painter2D;
                painter.lineWidth = 2f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.strokeColor = _versionUpdate.resolvedStyle.color;
                painter.BeginPath(); painter.MoveTo(center + new Vector2(0, 5)); painter.LineTo(center + new Vector2(0, -5)); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(center + new Vector2(-4, -1)); painter.LineTo(center + new Vector2(0, -5)); painter.LineTo(center + new Vector2(4, -1)); painter.Stroke();
            };
            titleRow.Add(_titleButton); titleRow.Add(_version); titleRow.Add(_versionUpdate);
            _state = new Label(); _state.AddToClassList("thry-state");
            branding.Add(titleRow); branding.Add(_state); masthead.Add(branding);
            _tools = new VisualElement { name = "thry-tools" }; _tools.AddToClassList("thry-tools");
            masthead.Add(_tools); _chrome.Add(masthead);

            var navigationGroup = new VisualElement { name = "thry-navigation-group" };
            navigationGroup.AddToClassList("thry-navigation-group");
            var navigation = new VisualElement(); navigation.AddToClassList("thry-navigation");
            _search = new ToolbarSearchField { name = "thry-search", tooltip = "Filter properties and edit them here. Click a name to go to that property. Combine the type and changed filters, or type t:texture, is:changed, is:animated, or is:missing. Escape clears all filters. Ctrl+F jumps here." };
            _search.AddToClassList("thry-search");
            var searchIcon = _search.Q(className: "unity-search-field-base__search-button");
            searchIcon.style.backgroundImage = StyleKeyword.None;
            searchIcon.generateVisualContent += context => {
                var painter = context.painter2D;
                painter.lineWidth = 1.8f;
                painter.strokeColor = searchIcon.resolvedStyle.color;
                painter.BeginPath(); painter.Arc(new Vector2(7,7),4.5f,0,360); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(new Vector2(10.3f,10.3f)); painter.LineTo(new Vector2(15,15)); painter.Stroke();
            };
            _search.RegisterCallback<FocusInEvent>(evt => _search.AddToClassList("thry-search-focused"));
            _search.RegisterCallback<FocusOutEvent>(evt => _search.RemoveFromClassList("thry-search-focused"));
            _placeholder = new Label("Search...") { pickingMode = PickingMode.Ignore };
            _placeholder.AddToClassList("thry-search-placeholder"); _search.Add(_placeholder);
            _searchRefresh = schedule.Execute(ApplySearchNow); _searchRefresh.Pause();
            _search.RegisterValueChangedCallback(evt => {
                string value = evt.newValue;
                SynchronizeQueryFilters();
                _placeholder.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.Flex : DisplayStyle.None;
                _searchPending = true;
                _propertySearch?.PauseBuild();
                _searchRefresh.ExecuteLater(130);
            });
            _search.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.altKey || evt.ctrlKey || evt.commandKey || evt.shiftKey) return;
                if (evt.keyCode == KeyCode.Escape) HandleFilterEscape(evt);
                else if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    if (_searchPending) ApplySearchNow();
                    if (_propertySearch.FocusFirst()) { evt.PreventDefaultHandling(); evt.StopImmediatePropagation(); }
                }
            }, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(HandleFilterEscape);
            // Find reaches the inspector's panel root when nothing in it has focus, so listen there.
            RegisterCallback<AttachToPanelEvent>(evt => {
                if (evt.target != this || _findRoot != null) return;
                _findRoot = panel.visualTree;
                _findRoot.RegisterCallback<ValidateCommandEvent>(ValidateFind);
                _findRoot.RegisterCallback<ExecuteCommandEvent>(ExecuteFind);
                _findRoot.RegisterCallback<KeyDownEvent>(FindKey);
            });
            RegisterCallback<DetachFromPanelEvent>(evt => {
                if (evt.target != this || _findRoot == null) return;
                _findRoot.UnregisterCallback<ValidateCommandEvent>(ValidateFind);
                _findRoot.UnregisterCallback<ExecuteCommandEvent>(ExecuteFind);
                _findRoot.UnregisterCallback<KeyDownEvent>(FindKey);
                _findRoot = null;
            });
            navigation.Add(_search);
            var filters = new VisualElement(); filters.AddToClassList("thry-search-filters");
            _typeFilter = new DropdownField(new List<string> { "All types", "Textures", "Colors", "Numbers", "Vectors", "Toggles" }, 0)
                { name = "thry-type-filter", tooltip = "Filter by property type. Combine Textures with Changed only to find assigned or modified textures." };
            _typeFilter.AddToClassList("thry-filter-button"); _typeIcon = AddFilterIcon(_typeFilter, DrawTypeIcon); UseInspectorMenu(_typeFilter);
            _typeFilter.RegisterValueChangedCallback(evt => SelectSearchFilter(true)); filters.Add(_typeFilter);
            _statusFilter = new DropdownField(new List<string> { "Any status", "Changed only", "Assigned textures", "Empty textures", "Animated", "Missing references", "Favorites" }, 0)
                { name = "thry-status-filter", tooltip = "Filter by value or state. Assigned textures finds actual texture assignments, independent of tiling and offset." };
            _statusFilter.AddToClassList("thry-filter-button"); _statusIcon = AddFilterIcon(_statusFilter, DrawStatusIcon); UseInspectorMenu(_statusFilter);
            _statusFilter.RegisterValueChangedCallback(evt => SelectSearchFilter(false)); filters.Add(_statusFilter);
            navigation.Add(filters); navigationGroup.Add(navigation);
            _chrome.Add(navigationGroup); Add(_chrome);
            _propertySearch = new RetainedSearch(this, ClearSearch);
            Add(_propertySearch);
            _body = new VisualElement { name = "thry-unfiltered-body" }; Add(_body);
            _navigation = new RetainedNavigation(this);
            _fallback = new IMGUIContainer(() =>
            {
                // A shader can change between retained refreshes. Never let a
                // still-attached native fallback run Thry's ShaderGUI.
                if (RetainedMaterialModel.HasValidTargets(_editor)) return;
                drawInspector();
            });
            RegisterCallback<AttachToPanelEvent>(evt => Undo.undoRedoPerformed += OnUndo);
            RegisterCallback<DetachFromPanelEvent>(evt => Undo.undoRedoPerformed -= OnUndo);
            RegisterCallback<AttachToPanelEvent>(evt => { _cachedRenderers = null; Selection.selectionChanged += InvalidateRenderers; EditorApplication.hierarchyChanged += InvalidateRenderers; });
            RegisterCallback<DetachFromPanelEvent>(evt => { Selection.selectionChanged -= InvalidateRenderers; EditorApplication.hierarchyChanged -= InvalidateRenderers; _cachedRenderers = null; });
            _chrome.style.display = DisplayStyle.None;
            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                EnableInClassList("thry-compact", evt.newRect.width < 420);
                EnableInClassList("thry-narrow", evt.newRect.width < 310);
            });
            RegisterCallback<DetachFromPanelEvent>(evt => ReleaseShaderHooks());
            // The Inspector host and its material foldout are established on attach.
            // Build visible controls now rather than leaving the first paint empty
            // until the periodic refresh. Collapsed embedded materials still return early.
            RegisterCallback<AttachToPanelEvent>(evt => { if (evt.target == this) UpdateState(); });
            schedule.Execute(UpdateState).Every(150);
        }

        private void Queue(Action action)
        {
            if (_shader == null || !RetainedMaterialModel.HasValidTargets(_editor)) return;
            _shader.ActivateRetained(); action(); _retained?.Model.Notify();
            _body.MarkDirtyRepaint();
        }


        internal void UseInspectorMenu(DropdownField field, bool applyOnReselect = false, Func<IEnumerable<EnumAction>> actions = null)
        {
            Action open = () => {
                field.Focus();
                var items = field.choices.Select((choice, i) => new RetainedMenu.Item
                {
                    Text = choice,
                    Checked = i == field.index,
                    Action = () => { if (i < field.choices.Count) RetainedWindow.SelectDropdownChoice(field, field.choices[i], applyOnReselect); }
                }).ToList();
                var extra = actions?.Invoke()?.ToList();
                if (extra != null && extra.Count > 0)
                {
                    items.Add(new RetainedMenu.Item { Separator = true });
                    items.AddRange(extra.Select(a => new RetainedMenu.Item { Text = a.Text, Action = a.Run }));
                }
                Rect anchor = field.worldBound;
                var caption = field.Q(className: "thry-dropdown-caption");
                if (caption != null) anchor.xMin = caption.worldBound.xMax;
                RetainedMenu.Open(anchor, field, items);
            };
            field.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0) return;
                evt.PreventDefaultHandling(); evt.StopImmediatePropagation(); open();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<NavigationSubmitEvent>(evt => {
                evt.PreventDefaultHandling(); evt.StopImmediatePropagation(); open();
            }, TrickleDown.TrickleDown);
        }

        internal void ShowMenu(Rect anchor, GUIContent[] choices, int[] selected, Action<int> select, VisualElement target)
        {
            RetainedMenu.Open(anchor, target, choices.Select((choice, index) => new RetainedMenu.Item
            {
                Text = choice.text,
                Checked = Array.IndexOf(selected, index) >= 0,
                Action = () => select(index)
            }));
        }

        private Button AddTool(string tooltip, Texture texture, Action action)
        {
            var button = new Button(() => Queue(action)) { tooltip = tooltip, name = "thry-tool-" + tooltip.ToLowerInvariant().Replace(' ', '-') };
            button.AddToClassList("thry-icon-button");
            var icon = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            button.Add(icon); _tools.Add(button);
            return button;
        }

        private Button AddDrawnTool(string name, string className, Action<Painter2D, VisualElement> draw, Action action)
        {
            var button = AddTool(name, null, action);
            button.Clear(); button.AddToClassList(className);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = 18; icon.style.height = 18;
            icon.generateVisualContent += context => draw(context.painter2D, icon);
            button.Add(icon);
            return button;
        }

        private void AddPresetsTool()
        {
            var presets = AddTool("Presets", Resources.Load<Texture2D>("ThryToolbar/header-presets"), () =>
            {
                var window = ScriptableObject.CreateInstance<PresetsPopupGUI>();
                window.Init("_full_", Presets.GetFullPresetNames(), Presets.GetFullPresetGuids(), _shader);
                window.titleContent = new GUIContent(Text("presets", "Presets"));
                window.ShowUtility();
            });
            presets.AddToClassList("thry-presets-tool");
            presets.tooltip = Text("presets", "Presets");
        }

        // The strip-disabled-sections switch: full color while on, dimmed while off. Only touched when the state, the
        // shader or the language changes.
        private string _sectionLockToolState;
        private void UpdateSectionLockTool()
        {
            var tool = _tools.Q<Button>("thry-tool-section-lock");
            if (tool == null || _shader == null) return;
            bool applies = SectionLockService.AppliesTo(_shader.Shader);
            bool on = SectionLockService.Enabled;
            string state = applies + "|" + on + "|" + Config.Instance.locale;
            if (state == _sectionLockToolState) return;
            _sectionLockToolState = state;
            tool.style.display = applies ? DisplayStyle.Flex : DisplayStyle.None;
            tool.EnableInClassList("thry-off", !on);
            tool.tooltip = SectionLockService.ToolbarTooltip;
        }

        private void AddLanguageTool()
        {
            Button language = null;
            language = AddDrawnTool("Language", "thry-language-tool", DrawLanguageIcon, () => {
                var source = _shader;
                if (source?.Locale == null) return;
                RetainedMenu.Open(language.worldBound, language, source.Locale.LanguageNames.Select((label, index) => new RetainedMenu.Item {
                    Text = label, Checked = index == source.Locale.LanguageIndex,
                    Action = () => {
                        if (_shader != source) return;
                        Queue(() => { source.Locale.SelectLanguage(index); source.Reload(); });
                    }
                }));
            });
        }

        // Variants are the shaders published beside this one, such as Poiyomi Pro Outline Early or Poiyomi Pro URP.
        private void ShowVariants()
        {
            if (_shader == null || _shader.IsLockedMaterial || !RetainedMaterialModel.HasValidTargets(_editor)) return;
            var names = _shader.Materials.Select(DisplayShaderName).Distinct(StringComparer.Ordinal).ToArray();
            string current = names.Length == 1 ? names[0] : null;
            string first = names.FirstOrDefault(n => !string.IsNullOrEmpty(n));
            if (first == null) return;
            string folder = first.Substring(0, first.LastIndexOf('/') + 1);
            var variants = ShaderUtil.GetAllShaderInfo().Select(info => info.name)
                .Where(n => n.StartsWith(folder, StringComparison.Ordinal) && n.IndexOf('/', folder.Length) < 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            if (variants.Length < 2) return;
            var items = new List<RetainedMenu.Item>();
            foreach (bool urp in new[] { false, true })
                foreach (string variant in variants)
                {
                    string leaf = variant.Substring(folder.Length);
                    if ((" " + leaf + " ").Contains(" URP ") != urp) continue;
                    string target = variant;
                    items.Add(new RetainedMenu.Item { Text = (urp ? "URP/" : "") + leaf, Checked = variant == current, Action = () =>
                    {
                        var shader = Shader.Find(target);
                        if (shader != null && RetainedMaterialModel.HasValidTargets(_editor)) _editor.SetShader(shader);
                    } });
                }
            RetainedMenu.Open(_titleButton.worldBound, _titleButton, items);
        }

        private const string ScreenshotFolderKey = "Thry.Inspector.ScreenshotFolder";
        private async void ScreenshotInspector()
        {
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(candidate => candidate.rootVisualElement.panel == panel && candidate.rootVisualElement.Contains(this));
            if (window == null || _shader == null) return;
            string folder = EditorPrefs.GetString(ScreenshotFolderKey, "");
            if (!System.IO.Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string name = _shader.Materials.FirstOrDefault(material => material != null)?.name ?? "Inspector";
            name = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars()));
            string path = EditorUtility.SaveFilePanel(Text("screenshot_inspector", "Screenshot Inspector"), folder, name + ".png", "png");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(ScreenshotFolderKey, System.IO.Path.GetDirectoryName(path));
            // The save dialog has to leave the screen before the window is read back.
            await System.Threading.Tasks.Task.Delay(250);
            if (window != null) await InspectorCapture.CaptureWindowToFile(window, path);
        }

        internal void RequestRowBands() => _rowBandRefresh.ExecuteLater(0);

        private void UpdateRowBands()
        {
            bool banded = Config.Instance.staggeringRowColors;
            var indices = new Dictionary<VisualElement, int>();
            foreach (var row in this.Query<VisualElement>(className: "thry-property-row").ToList())
            {
                if (!banded)
                {
                    row.RemoveFromClassList("thry-row-alt");
                    continue;
                }
                bool visible = true;
                VisualElement section = this;
                for (var ancestor = row; ancestor != null && ancestor != this; ancestor = ancestor.parent)
                {
                    if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.style.display == DisplayStyle.None) { visible = false; break; }
                    if (section == this && ancestor.ClassListContains("thry-section-content")) section = ancestor;
                }
                if (!visible) { row.RemoveFromClassList("thry-row-alt"); continue; }
                indices.TryGetValue(section, out int index);
                row.EnableInClassList("thry-row-alt", index % 2 == 1);
                indices[section] = index + 1;
            }
        }

        private void OnUndo()
        {
            if (panel == null || !RetainedMaterialModel.HasValidTargets(_editor)) return;
            InvalidateRenderers(); _retained?.Model.Notify();
        }
        private VisualElement _findRoot;
        private bool OwnsFind(IEventHandler target)
        {
            if (!ClassListContains("thry-active") || resolvedStyle.display == DisplayStyle.None) return false;
            var element = target as VisualElement;
            if (element != null && element != _findRoot) return element == this || Contains(element);
            return _findRoot.Query<MaterialInspectorView>().Where(view => view.ClassListContains("thry-active") && view.resolvedStyle.display != DisplayStyle.None).First() == this;
        }
        private void ValidateFind(ValidateCommandEvent evt)
        {
            if (evt.commandName == "Find" && OwnsFind(evt.target)) evt.StopPropagation();
        }
        private void ExecuteFind(ExecuteCommandEvent evt)
        {
            if (evt.commandName != "Find" || !OwnsFind(evt.target)) return;
            FocusSearch(); evt.StopPropagation();
        }
        private void FindKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.F || !evt.actionKey || evt.shiftKey || evt.altKey || !OwnsFind(evt.target)) return;
            FocusSearch(); evt.StopPropagation();
        }
        private void FocusSearch()
        {
            _search.Focus();
            var input = _search.Q<TextField>();
            input?.schedule.Execute(() => { if (input.panel != null) input.SelectAll(); });
        }
        private void ApplySearchNow()
        {
            _searchPending = false;
            _searchRefresh?.Pause();
            UpdateSearch();
        }
        private void HandleFilterEscape(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || !ClassListContains("thry-filtering")) return;
            if (panel?.visualTree.Q("thry-dropdown-menu") != null) return;
            var target = evt.target as VisualElement;
            bool fromSearch = target == _search || (target != null && _search.Contains(target));
            if (!fromSearch)
            {
                // Numeric and text editors own Escape to cancel their active edit.
                // Keep their native cancellation from clearing or rebuilding results.
                for (var element = target; element != null && element != this; element = element.parent)
                    for (var type = element.GetType(); type != null; type = type.BaseType)
                        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TextInputBaseField<>)) return;
            }
            ClearSearch(); evt.PreventDefaultHandling(); evt.StopPropagation();
        }
        private void UpdateSearch()
        {
            if (_searchPending) return;
            if (_propertySearch == null || _body == null) return;
            string query = CurrentQuery();
            bool filtering = _retained != null && !string.IsNullOrWhiteSpace(query);
            EnableInClassList("thry-filtering", filtering);
            _body.style.display = filtering ? DisplayStyle.None : DisplayStyle.Flex;
            _placeholder.style.display = string.IsNullOrEmpty(_search.value) ? DisplayStyle.Flex : DisplayStyle.None;
            _propertySearch.Refresh(_retained?.Model, query);
        }
        private string CurrentQuery()
        {
            string query = _search.value;
            if (!_typeFromQuery && _typeFilter.index > 0 && _typeFilter.index < SearchTypes.Length) query += " " + SearchTypes[_typeFilter.index];
            if (!_statusFromQuery && _statusFilter.index > 0 && _statusFilter.index < SearchStates.Length) query += " " + SearchStates[_statusFilter.index];
            if (!string.IsNullOrEmpty(_sectionScope)) query += " in:" + _sectionScope;
            return query.Trim();
        }
        void SynchronizeQueryFilters()
        {
            if (_typeFilter == null || _statusFilter == null) return;
            var tokens = (_search.value ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            SynchronizeQueryFilter(_typeFilter, SearchTypes, tokens, true, ref _typeFromQuery, ref _multipleQueryTypes);
            SynchronizeQueryFilter(_statusFilter, SearchStates, tokens, false, ref _statusFromQuery, ref _multipleQueryStates);
        }
        void SynchronizeQueryFilter(DropdownField field, string[] values, string[] tokens, bool types, ref bool fromQuery, ref bool multiple)
        {
            var matches = Enumerable.Range(1, values.Length - 1).Where(i => tokens.Contains(values[i], StringComparer.OrdinalIgnoreCase)).ToArray();
            int index = matches.Length > 1 ? values.Length : matches.Length == 1 ? matches[0] : fromQuery ? 0 : Mathf.Max(0, field.index);
            fromQuery = matches.Length > 0;
            multiple = matches.Length > 1;
            SetFilterChoices(field, types, multiple);
            field.SetValueWithoutNotify(field.choices[Mathf.Clamp(index, 0, field.choices.Count - 1)]);
        }
        void SetFilterChoices(DropdownField field, bool types, bool multiple)
        {
            var captions = (types ? new[] { "All types", "Textures", "Colors", "Numbers", "Vectors", "Toggles" }
                : new[] { "Any status", "Changed only", "Assigned textures", "Empty textures", "Animated", "Missing references", "Favorites" }).ToList();
            if (multiple) captions.Add(types ? "Multiple types" : "Multiple states");
            field.choices = captions.Select(s => Text(s.ToLowerInvariant().Replace(' ', '_'), s)).ToList();
        }
        void SelectSearchFilter(bool types)
        {
            var field = types ? _typeFilter : _statusFilter;
            var values = types ? SearchTypes : SearchStates;
            int selected = field.index;
            if (selected < 0 || selected >= values.Length) return;
            // A deliberate dropdown selection replaces tokens of that family; typed
            // combinations remain visible as Multiple until the user replaces them.
            var tokens = (_search.value ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _search.SetValueWithoutNotify(string.Join(" ", tokens.Where(t => !values.Skip(1).Contains(t, StringComparer.OrdinalIgnoreCase))));
            if (types) { _typeFromQuery = false; _multipleQueryTypes = false; }
            else { _statusFromQuery = false; _multipleQueryStates = false; }
            SetFilterChoices(field, types, false);
            field.SetValueWithoutNotify(field.choices[selected]);
            _placeholder.style.display = string.IsNullOrEmpty(_search.value) ? DisplayStyle.Flex : DisplayStyle.None;
            ApplySearchNow();
        }
        internal void SearchSection(ShaderPart section)
        {
            if (section?.MaterialProperty == null) return;
            _sectionScope = section.MaterialProperty.name;
            ApplySearchNow(); _search.Focus();
        }
        internal bool Navigate(string property) => _retained != null && Navigate(RetainedNavigation.Find(_retained.Model.Shader, property));
        // Leaves search, opens what hides the part for this view only, then scrolls to it and flashes it.
        // False when the part has no place in this inspector.
        internal bool Navigate(ShaderPart part)
        {
            if (part == null || _retained == null || panel == null || style.display == DisplayStyle.None
                || !RetainedMaterialModel.HasValidTargets(_editor) || !_navigation.Start(_retained, part)) return false;
            if (_searchPending || CurrentQuery().Length > 0) ResetSearch();
            _retained.Synchronize();
            _navigation.Step();
            return true;
        }
        private void ReleaseShaderHooks()
        {
            if (_shader == null) return;
            _shader.Navigate = null;
        }
        internal bool IsFavorite(ShaderProperty property) => RetainedSearchPreferences.IsFavorite(property);
        internal void ToggleFavorite(ShaderProperty property)
        {
            RetainedSearchPreferences.ToggleFavorite(property);
            _propertySearch.Invalidate(); ApplySearchNow();
        }
        private static VisualElement AddFilterIcon(DropdownField field, Action<Painter2D, VisualElement> draw)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("thry-filter-icon");
            icon.generateVisualContent += context => draw(context.painter2D, icon);
            field.Q(className: BasePopupField<string, string>.inputUssClassName).Insert(0, icon);
            field.RegisterCallback<PointerEnterEvent>(e => icon.MarkDirtyRepaint());
            field.RegisterCallback<PointerLeaveEvent>(e => icon.MarkDirtyRepaint());
            return icon;
        }
        private static void DrawTypeIcon(Painter2D painter, VisualElement icon)
        {
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 1.5f; painter.lineCap = LineCap.Round; painter.lineJoin = LineJoin.Round;
            painter.BeginPath(); painter.MoveTo(new Vector2(9, 2.5f)); painter.LineTo(new Vector2(12.8f, 8)); painter.LineTo(new Vector2(5.2f, 8)); painter.ClosePath(); painter.Stroke();
            painter.BeginPath(); painter.MoveTo(new Vector2(2.8f, 10.8f)); painter.LineTo(new Vector2(7.6f, 10.8f)); painter.LineTo(new Vector2(7.6f, 15.6f)); painter.LineTo(new Vector2(2.8f, 15.6f)); painter.ClosePath(); painter.Stroke();
            painter.BeginPath(); painter.Arc(new Vector2(12.7f, 13.2f), 2.6f, 0, 360); painter.Stroke();
        }
        private static void DrawStatusIcon(Painter2D painter, VisualElement icon)
        {
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 1.5f; painter.lineCap = LineCap.Round; painter.lineJoin = LineJoin.Round;
            painter.BeginPath(); painter.MoveTo(new Vector2(2.8f, 3.6f)); painter.LineTo(new Vector2(15.2f, 3.6f)); painter.LineTo(new Vector2(10.6f, 9.4f));
            painter.LineTo(new Vector2(10.6f, 14.6f)); painter.LineTo(new Vector2(7.4f, 13)); painter.LineTo(new Vector2(7.4f, 9.4f)); painter.ClosePath(); painter.Stroke();
        }
        private static void DrawCollapseIcon(Painter2D painter, VisualElement icon)
        {
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 2; painter.lineCap = LineCap.Round; painter.lineJoin = LineJoin.Round;
            for (int y = 0; y <= 6; y += 6)
            {
                painter.BeginPath(); painter.MoveTo(new Vector2(4, 8 + y)); painter.LineTo(new Vector2(9, 3 + y)); painter.LineTo(new Vector2(14, 8 + y)); painter.Stroke();
            }
        }
        private static void DrawLanguageIcon(Painter2D painter, VisualElement icon)
        {
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 1.4f;
            painter.BeginPath(); painter.Arc(new Vector2(9, 9), 6.5f, 0, 360); painter.Stroke();
            painter.BeginPath(); painter.MoveTo(new Vector2(2.5f, 9)); painter.LineTo(new Vector2(15.5f, 9)); painter.Stroke();
            painter.BeginPath();
            for (int i = 0; i <= 24; i++)
            {
                float angle = i * Mathf.PI / 12;
                var point = new Vector2(9 + Mathf.Cos(angle) * 3, 9 + Mathf.Sin(angle) * 6.5f);
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            painter.Stroke();
        }
        private void UpdateFilterButtons()
        {
            UpdateFilterButton(_typeFilter, _typeIcon, Text("type_filter_help", "Filter by property type."));
            UpdateFilterButton(_statusFilter, _statusIcon, Text("status_filter_help", "Filter by value or state. Assigned textures finds texture assignments, independent of tiling and offset."));
        }
        private static void UpdateFilterButton(DropdownField field, VisualElement icon, string help)
        {
            bool active = field.index > 0;
            if (field.ClassListContains("thry-filter-active") != active)
            {
                field.EnableInClassList("thry-filter-active", active);
                icon.MarkDirtyRepaint();
            }
            string tooltip = field.value + "\n" + help;
            if (field.tooltip != tooltip) field.tooltip = tooltip;
        }
        private void ClearSearch()
        {
            ResetSearch();
            _retained?.Synchronize();
            _search.Focus();
        }
        private void ResetSearch()
        {
            _searchPending = false;
            _searchRefresh.Pause();
            _search.SetValueWithoutNotify("");
            _sectionScope = null;
            _typeFromQuery = _statusFromQuery = _multipleQueryTypes = _multipleQueryStates = false;
            SetFilterChoices(_typeFilter, true, false);
            SetFilterChoices(_statusFilter, false, false);
            _typeFilter.SetValueWithoutNotify(_typeFilter.choices[0]);
            _statusFilter.SetValueWithoutNotify(_statusFilter.choices[0]);
            UpdateSearch();
        }
        private void InvalidateRenderers() { _cachedRenderers = null; }
        private Renderer[] FindRenderers()
        {
            if (_cachedRenderers != null && _cachedRenderers.All(r => r != null)) return _cachedRenderers;
            if(_rendererContext != null) return _cachedRenderers = _rendererContext.Where(r=>r!=null).ToArray();
            for (VisualElement ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                var contract = ancestor.GetType().GetInterfaces().FirstOrDefault(t => t.Name == "IEditorElement");
                var editors = contract?.GetProperty("Editors")?.GetValue(ancestor) as Editor[];
                if (editors != null) return _cachedRenderers = editors.Where(e => e != null).SelectMany(e => e.targets).OfType<Renderer>()
                    .Where(r => r.sharedMaterials.Any(m => _editor.targets.Contains(m))).Distinct().ToArray();
            }
            return _cachedRenderers = Array.Empty<Renderer>();
        }
        internal void ShowLegacyMenu(GenericMenu source, VisualElement target, Rect? anchor = null)
        {
            var position = anchor ?? target.worldBound;
            List<RetainedMenu.Item> choices;
            if (!TryConvertLegacyMenu(source, target, out choices))
            {
                // Native menus remain usable when Unity changes its private storage.
                source.DropDown(position);
                return;
            }
            RetainedMenu.TrimSeparators(choices);
            RetainedMenu.Open(position, target, choices);
        }
        private bool TryConvertLegacyMenu(GenericMenu source, VisualElement target, out List<RetainedMenu.Item> choices)
        {
            choices = new List<RetainedMenu.Item>();
            try
            {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var items = typeof(GenericMenu).GetProperty("menuItems",flags)?.GetValue(source) as System.Collections.IEnumerable;
            if (items == null) items = typeof(GenericMenu).GetField("m_MenuItems",flags)?.GetValue(source) as System.Collections.IEnumerable;
            if (items == null) return false;
            foreach(var item in items)
            {
                var type = item.GetType();
                if ((bool)type.GetField("separator",flags).GetValue(item))
                {
                    choices.Add(new RetainedMenu.Item { Separator = true, Text = (type.GetField("content", flags).GetValue(item) as GUIContent)?.text });
                    continue;
                }
                var label = (GUIContent)type.GetField("content",flags).GetValue(item);
                var callback = type.GetField("func",flags).GetValue(item) as GenericMenu.MenuFunction;
                var callback2 = type.GetField("func2",flags).GetValue(item) as GenericMenu.MenuFunction2;
                var data = type.GetField("userData",flags).GetValue(item);
                var part = target.userData as ShaderPart ?? target.parent?.userData as ShaderPart;
                choices.Add(new RetainedMenu.Item { Text = label.text == "Reset" && part != null ? RetainedValueDetails.ResetLabel(part) : label.text, Checked = (bool)type.GetField("on",flags).GetValue(item), IconDraw = RetainedMenu.IconFor(label.tooltip),
                    Action = callback == null && callback2 == null ? (Action)null : () => { _shader.ActivateRetained(); if(callback != null) callback(); else callback2(data); _retained.Model.Notify(); } });
            }
            return true;
            }
            catch (Exception exception) when (exception is NullReferenceException || exception is ArgumentException
                || exception is InvalidCastException || exception is System.Reflection.TargetInvocationException || exception is MemberAccessException)
            { choices.Clear(); return false; }
        }
        // Unity draws a material below a renderer behind a foldout: it renders the header itself and
        // then gates the body on MaterialEditor.isVisible. A UI Toolkit inspector is built once and
        // never asked, so a collapsed material slot still showed the whole inspector. Reading the same
        // flag restores the foldout, but only where a foldout actually exists: the editor Unity lists
        // first owns no header of its own, and a view hosted outside the inspector window (the cross
        // editor, tests) has no foldout at all. Both of those, and a Unity that no longer exposes the
        // internal flag, keep the view visible - this may only ever hide a nested, collapsed material.
        static readonly System.Reflection.PropertyInfo FirstInspectedEditor = typeof(Editor) .GetProperty("firstInspectedEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private bool IsCollapsedBehindItsFoldout()
        {
            if (FirstInspectedEditor == null) return false;
            if (GetFirstAncestorOfType<InspectorElement>() == null) return false;
            if (FirstInspectedEditor.GetValue(_editor) as bool? != false) return false;
            return !_editor.isVisible;
        }
        private void UpdateHostInsets(bool active)
        {
            // Unity reserves label indentation around custom inspectors. Our surface already
            // supplies its own 3px inset; cancel the host padding only for this material view.
            var inspector = parent as InspectorElement;
            float left = active && inspector != null ? inspector.resolvedStyle.paddingLeft : 0;
            float right = active && inspector != null ? inspector.resolvedStyle.paddingRight : 0;
            if (!float.IsNaN(left) && resolvedStyle.marginLeft != -left) style.marginLeft = -left;
            if (!float.IsNaN(right) && resolvedStyle.marginRight != -right) style.marginRight = -right;
        }

        // GetOriginalShader is slow and the header refreshes every 150 ms
        private readonly Dictionary<Material, (Shader locked, int version, Shader original)> _originalShaders = new Dictionary<Material, (Shader, int, Shader)>();
        private string _titleSource, _titleText;
        private Match _titleVersion;

        private string DisplayShaderName(Material material)
        {
            if (material == null) return "";
            Shader shader;
            if (material.IsLocked())
            {
                int version = EditorUtility.GetDirtyCount(material);
                if (!_originalShaders.TryGetValue(material, out var cached) || cached.locked != material.shader || cached.version != version)
                    _originalShaders[material] = cached = (material.shader, version, ShaderOptimizer.GetOriginalShader(material, false));
                shader = cached.original;
            }
            else shader = SectionLock.GetSourceShader(material.shader);
            return shader != null ? shader.name : material.GetTag(ShaderOptimizer.TAG_ORIGINAL_SHADER, false, "");
        }

        private void UpdateState()
        {
            // Leave typing responsive; the debounced refresh uses the latest query.
            if (_searchPending) return;
            if (!RetainedMaterialModel.HasLiveTargets(_editor)) { style.display = DisplayStyle.None; return; }
            bool collapsed = IsCollapsedBehindItsFoldout();
            style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (collapsed) return;
            bool compatible = RetainedMaterialModel.HasValidTargets(_editor);
            var current = compatible ? _shaderOverride ?? _editor.customShaderGUI as ShaderEditor : null;
            if (compatible && current == null)
            {
                typeof(MaterialEditor).GetMethod("CreateCustomShaderEditorIfNeeded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_editor,new object[] { ((Material)_editor.target).shader });
                current = _editor.customShaderGUI as ShaderEditor;
            }
            if (current != null && (_retained == null || _retained.Model.Shader != current))
            {
                var model = new RetainedMaterialModel(_editor,current);
                _crossProperties = _propertyProvider == null ? new RetainedCrossSelectionProperties(_editor, current) : null;
                model.PropertyProvider = _propertyProvider ?? _crossProperties.Read;
                model.Renderers = FindRenderers();
                _body.Clear(); _retained = new RetainedMaterialBody(model,this); _body.Add(_retained);
            }
            if (current != null)
            {
                // Detaching clears this, so set it on every refresh
                current.Navigate = Navigate;
                _retained.Model.Renderers = FindRenderers(); _retained.Model.Refresh();
                if (_retained.childCount == 0 || !ClassListContains("thry-filtering")) _retained.Synchronize();
            }
            else if (compatible)
            {
                // A Thry shader waiting for its ShaderGUI must never show the native
                // fallback just to bridge the first retained refresh.
                if (_loading.parent == null)
                {
                    ReleaseShaderHooks();
                    _shader = null; _crossProperties = null; _retained = null;
                    _body.Clear(); _body.Add(_loading);
                }
            }
            else if (_fallback.parent == null)
            {
                ReleaseShaderHooks();
                _shader = null; _crossProperties = null; _retained = null;
                _body.Clear(); _body.Add(_fallback);
            }
            bool visible = current != null && current.Editor != null && current.SupportsRetainedToolbar;
            _chrome.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("thry-active", visible);
            UpdateHostInsets(visible);
            EnableInClassList("thry-light", !EditorGUIUtility.isProSkin);
            EnableInClassList("thry-dark", EditorGUIUtility.isProSkin);
            if (!visible) { EnableInClassList("thry-filtering", false); _propertySearch.Refresh(null, ""); _body.style.display = DisplayStyle.Flex; return; }
            if (_shader != current) { _shader = current; _shader.FocusCategory(null); _builtTools = false; _localeIndex = -1; }
            if (_titleText == null || _titleSource != _shader.InspectorTitle)
            {
                _titleSource = _shader.InspectorTitle;
                _titleText = Regex.Replace(_titleSource, "<[^>]+>", "");
                _titleVersion = Regex.Match(_titleText, @"\s+(?:v)?\d+\.\d+[\w.\-]*$", RegexOptions.IgnoreCase);
            }
            string title = _titleText;
            var version = _titleVersion;
            if (_originalShaders.Count > _shader.Materials.Length) _originalShaders.Clear();
            var shaderNames = _shader.Materials.Select(DisplayShaderName).Distinct(StringComparer.Ordinal).ToArray();
            string shaderName = shaderNames.FirstOrDefault();
            bool mixedPoiyomi = shaderNames.Length > 1 && shaderNames.All(n => n.Split('/').Last().StartsWith("Poiyomi", StringComparison.OrdinalIgnoreCase));
            _title.text = mixedPoiyomi ? "Poiyomi Mixed" : !string.IsNullOrEmpty(shaderName) ? shaderName.Split('/').Last() : version.Success ? title.Substring(0, version.Index) : title;
            bool canSwitch = !_shader.IsLockedMaterial;
            _titleButton.tooltip = mixedPoiyomi ? string.Join("\n", shaderNames) : (shaderName ?? _title.text) + (canSwitch ? "\n" + Text("switch_variant", "Click to switch to another variant.") : "");
            _titleButton.EnableInClassList("thry-title-fixed", !canSwitch);
            _version.text = version.Value.Trim();
            _version.style.display = mixedPoiyomi ? DisplayStyle.None : DisplayStyle.Flex;
            // The package manager in com.poiyomi.pro publishes this; installs without it never offer an update
            string update = SessionState.GetString(UpdateVersionKey, "");
            bool offerUpdate = update.Length > 0 && !mixedPoiyomi && shaderName != null && shaderName.Split('/').Last().StartsWith("Poiyomi", StringComparison.OrdinalIgnoreCase);
            _version.EnableInClassList("thry-version-outdated", offerUpdate);
            _version.tooltip = offerUpdate ? update + " is available" : "";
            _versionUpdate.tooltip = "Update to " + update;
            _versionUpdate.style.display = offerUpdate ? DisplayStyle.Flex : DisplayStyle.None;
            bool editing = _shader.RootCategories.Any(g => SectionEditing.IsEditing(g));
            int selectedCount = _retained.Model.SelectedMaterials.Length;
            _state.text = editing ? "Editing shader sections" : _shader.IsLockedMaterial ? "Optimized material"
                : selectedCount > 1 ? selectedCount + " materials selected" : "Material editor";
            _state.EnableInClassList("thry-optimized", _shader.IsLockedMaterial);
            _state.style.display = editing || _shader.IsLockedMaterial || selectedCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateSearch();

            UpdateFilterButtons();
            if (!_builtTools || _toolTheme != EditorGUIUtility.isProSkin)
            {
                _toolTheme = EditorGUIUtility.isProSkin;
                _tools.Clear();
                AddPresetsTool();
                var collapse = AddDrawnTool("Collapse all", "thry-collapse-tool", DrawCollapseIcon, () => _shader.CollapseCategories());
                collapse.name = "thry-collapse-all";
                collapse.tooltip = Text("collapse_all", "Collapse all sections");
                foreach (var extra in TopBarButtons.All)
                {
                    var button = extra;
                    AddTool(button.Tooltip, button.Icon()?.normal.background, () => button.OnClick());
                }
                var sectionLock = AddTool("Section lock", ToolbarIcons.SectionLock?.normal.background, () =>
                {
                    SectionLockService.SetEnabled(!SectionLockService.Enabled);
                    UpdateSectionLockTool();
                });
                // Clicking would focus it, and the focus outline reads as an on state.
                sectionLock.focusable = false;
                _sectionLockToolState = null;
                var screenshot = AddTool("Screenshot", ToolbarIcons.Camera?.normal.background, ScreenshotInspector);
                screenshot.tooltip = Text("screenshot_inspector", "Screenshot Inspector");
                Button toolsButton = null;
                toolsButton = AddTool("Tools", ToolbarIcons.Tools?.normal.background, () =>
                    ShowLegacyMenu(_shader.RetainedToolsMenu(), toolsButton));
                AddLanguageTool();
                AddTool("Settings", ToolbarIcons.Settings?.normal.background, () => EditorWindow.GetWindow<Settings>(false, "Thry Settings", true));
                _builtTools = true;
            }
            _tools.Q<Button>("thry-tool-edit-shader-sections")?.EnableInClassList("thry-selected", editing);
            UpdateSectionLockTool();
            var languageTool = _tools.Q<Button>("thry-tool-language");
            languageTool?.SetEnabled(_shader.Locale != null && _shader.Locale.LanguageNames.Length > 1);
            if (languageTool != null && _shader.Locale != null)
                languageTool.tooltip = "Language: " + _shader.Locale.LanguageNames[Mathf.Clamp(_shader.Locale.LanguageIndex, 0, _shader.Locale.LanguageNames.Length - 1)];
            if (_shader.Locale != null && _localeIndex != _shader.Locale.LanguageIndex) RefreshSearchLanguage();
        }
        private void RefreshSearchLanguage()
        {
            _localeIndex = _shader.Locale.LanguageIndex;
            int typeIndex = _typeFilter.index, statusIndex = _statusFilter.index;
            SetFilterChoices(_typeFilter, true, _multipleQueryTypes);
            SetFilterChoices(_statusFilter, false, _multipleQueryStates);
            _typeFilter.SetValueWithoutNotify(_typeFilter.choices[Mathf.Max(0, typeIndex)]);
            _statusFilter.SetValueWithoutNotify(_statusFilter.choices[Mathf.Max(0, statusIndex)]);
            _placeholder.text = Text("search", "Search...");
            _search.tooltip = Text("search_help", "Filter properties and edit them here. Click a name to go to that property. Combine type and status filters. Escape clears all filters. Ctrl+F jumps here.");
            _propertySearch.RefreshLanguage();
            var presets = _tools.Q<Button>("thry-tool-presets");
            if (presets != null) presets.tooltip = Text("presets", "Presets");
        }
    }
}
