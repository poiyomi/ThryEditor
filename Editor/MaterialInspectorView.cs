#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        private readonly VisualElement _body;
        private IMGUIContainer _fallback;
        private RetainedMaterialBody _retained;
        private RetainedCrossSelectionProperties _crossProperties;
        private readonly Renderer[] _rendererContext;
        private Renderer[] _cachedRenderers;
        private readonly ShaderEditor _shaderOverride;
        private readonly Func<MaterialProperty[]> _propertyProvider;
        private readonly VisualElement _chrome, _tools;
        private readonly Label _title, _version, _state, _placeholder;
        private readonly ToolbarSearchField _search;
        private readonly DropdownField _typeFilter;
        private readonly DropdownField _statusFilter;
        private readonly Button _savedFilters;
        private readonly RetainedSearch _propertySearch;
        private ShaderEditor _shader;
        private bool _builtTools;
        private bool _toolTheme;
        private readonly IVisualElementScheduledItem _searchRefresh;
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
            AddToClassList("thry-inspector");
            var sheet = Resources.Load<StyleSheet>("ThryInspector");
            if (sheet != null) styleSheets.Add(sheet);
            var searchSheet = Resources.Load<StyleSheet>("ThrySearch");
            if (searchSheet != null) styleSheets.Add(searchSheet);
            var polishSheet = Resources.Load<StyleSheet>("ThryControlPolish");
            if (polishSheet != null) styleSheets.Add(polishSheet);
            RetainedAvailability.Install(this);

            _chrome = new VisualElement { name = "thry-chrome" };
            _chrome.AddToClassList("thry-chrome");
            var masthead = new VisualElement(); masthead.AddToClassList("thry-masthead");
            var branding = new VisualElement(); branding.AddToClassList("thry-branding");
            var titleRow = new VisualElement(); titleRow.AddToClassList("thry-title-row");
            _title = new Label("Material"); _title.AddToClassList("thry-title");
            _version = new Label(); _version.AddToClassList("thry-version");
            titleRow.Add(_title); titleRow.Add(_version);
            _state = new Label(); _state.AddToClassList("thry-state");
            branding.Add(titleRow); branding.Add(_state); masthead.Add(branding);
            _tools = new VisualElement { name = "thry-tools" }; _tools.AddToClassList("thry-tools");
            masthead.Add(_tools); _chrome.Add(masthead);

            var navigationGroup = new VisualElement { name = "thry-navigation-group" };
            navigationGroup.AddToClassList("thry-navigation-group");
            var navigation = new VisualElement(); navigation.AddToClassList("thry-navigation");
            _search = new ToolbarSearchField { name = "thry-search", tooltip = "Filter properties and edit them here. Combine the type and changed filters, or type t:texture, is:changed, is:animated, or is:missing. Escape clears all filters." };
            _search.AddToClassList("thry-search");
#if UNITY_2022_1_OR_NEWER
            var searchIcon = _search.Q(className: "unity-search-field-base__search-button");
            searchIcon.style.backgroundImage = StyleKeyword.None;
            searchIcon.generateVisualContent += context => {
                var painter = context.painter2D;
                painter.lineWidth = 1.8f;
                painter.strokeColor = EditorGUIUtility.isProSkin ? new Color(.74f,.74f,.74f) : new Color(.35f,.35f,.35f);
                painter.BeginPath(); painter.Arc(new Vector2(7,7),4.5f,0,360); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(new Vector2(10.3f,10.3f)); painter.LineTo(new Vector2(15,15)); painter.Stroke();
            };
#endif
            _search.RegisterCallback<FocusInEvent>(evt => _search.AddToClassList("thry-search-focused"));
            _search.RegisterCallback<FocusOutEvent>(evt => _search.RemoveFromClassList("thry-search-focused"));
            _placeholder = new Label("Search...") { pickingMode = PickingMode.Ignore };
            _placeholder.AddToClassList("thry-search-placeholder"); _search.Add(_placeholder);
            _searchRefresh = schedule.Execute(ApplySearchNow); _searchRefresh.Pause();
            _search.RegisterValueChangedCallback(evt => {
                string value = evt.newValue;
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
                    if (_propertySearch.FocusFirst()) { evt.PreventDefault(); evt.StopImmediatePropagation(); }
                }
            }, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(HandleFilterEscape);
            navigation.Add(_search);
            var filters = new VisualElement(); filters.AddToClassList("thry-search-filters");
            _typeFilter = new DropdownField(new List<string> { "All types", "Textures", "Colors", "Numbers", "Vectors", "Toggles" }, 0)
                { name = "thry-type-filter", tooltip = "Filter by property type. Combine Textures with Changed only to find assigned or modified textures." };
            _typeFilter.AddToClassList("thry-type-picker"); UseInspectorMenu(_typeFilter);
            _typeFilter.RegisterValueChangedCallback(evt => ApplySearchNow()); filters.Add(_typeFilter);
            _statusFilter = new DropdownField(new List<string> { "Any status", "Changed only", "Assigned textures", "Empty textures", "Animated", "Missing references", "Favorites" }, 0)
                { name = "thry-status-filter", tooltip = "Filter by value or state. Assigned textures finds actual texture assignments, independent of tiling and offset." };
            _statusFilter.AddToClassList("thry-status-filter"); UseInspectorMenu(_statusFilter);
            _statusFilter.RegisterValueChangedCallback(evt => ApplySearchNow()); filters.Add(_statusFilter);
            _savedFilters = new Button(ShowSavedFilters) { name = "thry-saved-filters", text = "☆", tooltip = "Favorites and saved searches" };
            _savedFilters.AddToClassList("thry-saved-filters"); filters.Add(_savedFilters);
            navigation.Add(filters); navigationGroup.Add(navigation);
            _chrome.Add(navigationGroup); Add(_chrome);
            _propertySearch = new RetainedSearch(this, ClearSearch);
            Add(_propertySearch);
            _body = new VisualElement { name = "thry-unfiltered-body" }; Add(_body);
            _fallback = new IMGUIContainer(drawInspector);
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
            RegisterCallback<DetachFromPanelEvent>(evt => { if (_shader != null) { _shader.HasRetainedToolbar = false; _shader.ShowDropdown = null; } });
            schedule.Execute(UpdateState).Every(150);
        }

        private void Queue(Action action)
        {
            if (_shader == null || !RetainedMaterialModel.HasValidTargets(_editor)) return;
            _shader.ActivateRetained(); action(); _retained?.Model.Notify();
            _body.MarkDirtyRepaint();
        }


        internal void UseInspectorMenu(DropdownField field)
        {
            Action open = () => {
                field.Focus();
                ShowMenu(field.worldBound, field.choices.Select(s => new GUIContent(s)).ToArray(), new[] { field.index }, i =>
                {
                    field.showMixedValue = false;
                    field.value = field.choices[i];
                }, field);
            };
            field.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0) return;
                evt.PreventDefault(); evt.StopImmediatePropagation(); open();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<NavigationSubmitEvent>(evt => {
                evt.PreventDefault(); evt.StopImmediatePropagation(); open();
            }, TrickleDown.TrickleDown);
        }

        private void ShowDropdown(Rect anchor, GUIContent[] choices, int[] selected, Action<int> select)
        {
            ShowMenu(new Rect(_body.LocalToWorld(anchor.position), anchor.size), choices, selected, select, _body);
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

        private void AddCollapseTool()
        {
            var collapse = AddTool("Collapse all", null, () => _shader.CollapseCategories());
            collapse.name = "thry-collapse-all";
            collapse.tooltip = Text("collapse_all", "Collapse all sections");
            collapse.Clear(); collapse.AddToClassList("thry-collapse-tool");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = 18; icon.style.height = 18;
#if UNITY_2022_1_OR_NEWER
            icon.generateVisualContent += context => {
                var painter = context.painter2D;
                painter.lineWidth = 2; painter.strokeColor = icon.resolvedStyle.color;
                painter.lineCap = LineCap.Round; painter.lineJoin = LineJoin.Round;
                for (int y = 0; y <= 6; y += 6) {
                    painter.BeginPath(); painter.MoveTo(new Vector2(4, 8 + y));
                    painter.LineTo(new Vector2(9, 3 + y)); painter.LineTo(new Vector2(14, 8 + y)); painter.Stroke();
                }
            };
#else
            icon.Add(new Label("⌃") { pickingMode = PickingMode.Ignore });
#endif
            collapse.Add(icon);
        }

        private void AddLanguageTool()
        {
            Button language = null;
            language = AddTool("Language", null, () => {
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
            language.Clear(); language.AddToClassList("thry-language-tool");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = 18; icon.style.height = 18;
#if UNITY_2022_1_OR_NEWER
            icon.generateVisualContent += context => {
                var painter = context.painter2D;
                painter.lineWidth = 1.4f; painter.strokeColor = icon.resolvedStyle.color;
                painter.BeginPath(); painter.Arc(new Vector2(9, 9), 6.5f, 0, 360); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(new Vector2(2.5f, 9)); painter.LineTo(new Vector2(15.5f, 9)); painter.Stroke();
                painter.BeginPath();
                for (int i = 0; i <= 24; i++) {
                    float angle = i * Mathf.PI / 12;
                    var point = new Vector2(9 + Mathf.Cos(angle) * 3, 9 + Mathf.Sin(angle) * 6.5f);
                    if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
                }
                painter.Stroke();
            };
#else
            icon.Add(new Label("A") { pickingMode = PickingMode.Ignore });
#endif
            language.Add(icon);
        }

        private void OnUndo()
        {
            if (panel == null || !RetainedMaterialModel.HasValidTargets(_editor)) return;
            InvalidateRenderers(); _retained?.Model.Notify();
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
            ClearSearch(); evt.PreventDefault(); evt.StopPropagation();
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
            string[] types = { "", "texture", "color", "number", "vector", "toggle" };
            if (_typeFilter.index > 0) query += " t:" + types[_typeFilter.index];
            string[] states = { "", "changed", "assigned", "empty", "animated", "missing", "favorite" };
            if (_statusFilter.index > 0) query += " is:" + states[_statusFilter.index];
            if (!string.IsNullOrEmpty(_sectionScope)) query += " in:" + _sectionScope;
            return query.Trim();
        }
        internal void SearchSection(ShaderPart section)
        {
            if (section?.MaterialProperty == null) return;
            _sectionScope = section.MaterialProperty.name;
            ApplySearchNow(); _search.Focus();
        }
        internal void SearchProperty(ShaderProperty property)
        {
            if (property?.MaterialProperty != null) LoadSearch(property.MaterialProperty.name);
        }
        internal bool IsFavorite(ShaderProperty property) => RetainedSearchPreferences.IsFavorite(property);
        internal void ToggleFavorite(ShaderProperty property)
        {
            RetainedSearchPreferences.ToggleFavorite(property);
            _propertySearch.Invalidate(); ApplySearchNow();
        }
        private void ShowSavedFilters()
        {
            if (_shader == null) return;
            var items = new List<RetainedMenu.Item>();
            items.Add(new RetainedMenu.Item { Text = Text("favorites", "Favorites"), Action = () => LoadSearch("is:favorite") });
            string query = CurrentQuery();
            items.Add(new RetainedMenu.Item { Text = Text("save_current_search", "Save current search…"), Action = string.IsNullOrWhiteSpace(query) ? (Action)null :
                () => RetainedTextPrompt.Open(Text("save_search", "Save search"), RetainedSearch.TextQuery(query), name => RetainedSearchPreferences.Save(_shader, name, query)) });
            var saved = RetainedSearchPreferences.Filters(_shader);
            if (saved.Length > 0) items.Add(new RetainedMenu.Item { Separator = true });
            foreach (var filter in saved)
            {
                var entry = filter;
                items.Add(new RetainedMenu.Item { Text = entry.name, Action = () => LoadSearch(entry.query) });
                items.Add(new RetainedMenu.Item { Text = Text("remove_saved_search", "Remove saved search") + "/" + entry.name, Action = () => RetainedSearchPreferences.Remove(_shader, entry.name) });
            }
            RetainedMenu.Open(_savedFilters.worldBound, _savedFilters, items);
        }
        private void LoadSearch(string query)
        {
            var tokens = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string[] types = { "", "t:texture", "t:color", "t:number", "t:vector", "t:toggle" };
            string[] statuses = { "", "is:changed", "is:assigned", "is:empty", "is:animated", "is:missing", "is:favorite" };
            int typeIndex = Array.FindIndex(types, t => tokens.Contains(t));
            int statusIndex = Array.FindIndex(statuses, t => tokens.Contains(t));
            if (typeIndex > 0) tokens.Remove(types[typeIndex]);
            if (statusIndex > 0) tokens.Remove(statuses[statusIndex]);
            _sectionScope = tokens.FirstOrDefault(t => t.StartsWith("in:", StringComparison.OrdinalIgnoreCase))?.Substring(3);
            tokens.RemoveAll(t => t.StartsWith("in:", StringComparison.OrdinalIgnoreCase));
            _search.SetValueWithoutNotify(string.Join(" ", tokens));
            _typeFilter.SetValueWithoutNotify(_typeFilter.choices[Mathf.Max(0, typeIndex)]);
            _statusFilter.SetValueWithoutNotify(_statusFilter.choices[Mathf.Max(0, statusIndex)]);
            ApplySearchNow(); _search.Focus();
        }
        private void ClearSearch()
        {
            _searchPending = false;
            _searchRefresh.Pause();
            _search.SetValueWithoutNotify("");
            _sectionScope = null;
            _typeFilter.SetValueWithoutNotify(_typeFilter.choices[0]);
            _statusFilter.SetValueWithoutNotify(_statusFilter.choices[0]);
            UpdateSearch();
            _retained?.Synchronize();
            _search.Focus();
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
        internal void ShowLegacyMenu(GenericMenu source, VisualElement target)
        {
            List<RetainedMenu.Item> choices;
            if (!TryConvertLegacyMenu(source, target, out choices))
            {
                // Native menus remain usable when Unity changes its private storage.
                source.DropDown(target.worldBound);
                return;
            }
            RetainedMenu.Open(target.worldBound, target, choices);
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
                choices.Add(new RetainedMenu.Item { Text = label.text == "Reset" && part != null ? RetainedValueDetails.ResetLabel(part) : label.text, Checked = (bool)type.GetField("on",flags).GetValue(item),
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

        private static string DisplayShaderName(Material material)
        {
            if (material == null) return "";
            var shader = material.IsLocked() ? ShaderOptimizer.GetOriginalShader(material, false) : material.shader;
            return shader != null ? shader.name : material.GetTag(ShaderOptimizer.TAG_ORIGINAL_SHADER, false, "");
        }

        private void UpdateState()
        {
            // Leave typing responsive; the debounced refresh uses the latest query.
            if (_searchPending) return;
            if (!RetainedMaterialModel.HasValidTargets(_editor)) { style.display = DisplayStyle.None; return; }
            bool collapsed = IsCollapsedBehindItsFoldout();
            style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (collapsed) return;
            var current = _shaderOverride ?? _editor.customShaderGUI as ShaderEditor;
            if (current == null && _editor.customShaderGUI == null)
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
                model.Refresh(); current.HasRetainedToolbar = true; current.ShowDropdown = ShowDropdown;
                _body.Clear(); _retained = new RetainedMaterialBody(model,this); _body.Add(_retained);
            }
            if (current != null)
            {
                _retained.Model.Renderers = FindRenderers(); _retained.Model.Refresh();
                if (_retained.childCount == 0 || !ClassListContains("thry-filtering")) _retained.Synchronize();
            }
            else if (_fallback.parent == null) { _retained = null; _body.Clear(); _body.Add(_fallback); }
            bool visible = current != null && current.Editor != null && current.SupportsRetainedToolbar;
            _chrome.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("thry-active", visible);
            UpdateHostInsets(visible);
            EnableInClassList("thry-light", !EditorGUIUtility.isProSkin);
            if (!visible) { EnableInClassList("thry-filtering", false); _propertySearch.Refresh(null, ""); _body.style.display = DisplayStyle.Flex; return; }
            if (_shader != current) { _shader = current; _shader.FocusCategory(null); _builtTools = false; _localeIndex = -1; }
            string title = Regex.Replace(_shader.InspectorTitle, "<[^>]+>", "");
            var version = Regex.Match(title, @"\s+(?:v)?\d+\.\d+[\w.\-]*$", RegexOptions.IgnoreCase);
            var shaderNames = _shader.Materials.Select(DisplayShaderName).Distinct(StringComparer.Ordinal).ToArray();
            string shaderName = shaderNames.FirstOrDefault();
            bool mixedPoiyomi = shaderNames.Length > 1 && shaderNames.All(n => n.Split('/').Last().StartsWith("Poiyomi", StringComparison.OrdinalIgnoreCase));
            _title.text = mixedPoiyomi ? "Poiyomi Mixed" : !string.IsNullOrEmpty(shaderName) ? shaderName.Split('/').Last() : version.Success ? title.Substring(0, version.Index) : title;
            _title.tooltip = mixedPoiyomi ? string.Join("\n", shaderNames) : shaderName ?? _title.text;
            _version.text = version.Value.Trim();
            _version.style.display = mixedPoiyomi ? DisplayStyle.None : DisplayStyle.Flex;
            bool editing = _shader.RootCategories.Any(g => SectionEditing.IsEditing(g));
            int selectedCount = _retained.Model.SelectedMaterials.Length;
            _state.text = editing ? "Editing shader sections" : _shader.IsLockedMaterial ? "Optimized material"
                : selectedCount > 1 ? selectedCount + " materials selected" : "Material editor";
            _state.EnableInClassList("thry-optimized", _shader.IsLockedMaterial);
            _state.style.display = editing || _shader.IsLockedMaterial || selectedCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            // Filtered controls have their own view; legacy search expansion must not
            // modify the user's normal section/foldout state while they type.
            if (_shader.IsInSearchMode) _shader.SearchFromView("");
            UpdateSearch();

            if (!_builtTools || _toolTheme != EditorGUIUtility.isProSkin)
            {
                _toolTheme = EditorGUIUtility.isProSkin;
                _tools.Clear();
                AddCollapseTool();
                foreach (var extra in TopBarButtons.All)
                {
                    var button = extra;
                    AddTool(button.Tooltip, button.Icon()?.normal.background, () => button.OnClick());
                }
                Button toolsButton = null;
                toolsButton = AddTool("Tools", ToolbarIcons.Tools?.normal.background, () =>
                    ShowLegacyMenu(_shader.RetainedToolsMenu(), toolsButton));
                AddLanguageTool();
                AddTool("Settings", ToolbarIcons.Settings?.normal.background, () => EditorWindow.GetWindow<Settings>(false, "Thry Settings", true));
                _builtTools = true;
            }
            _tools.Q<Button>("thry-tool-edit-shader-sections")?.EnableInClassList("thry-selected", editing);
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
            _typeFilter.choices = new[] { "All types", "Textures", "Colors", "Numbers", "Vectors", "Toggles" }
                .Select(s => Text(s.ToLowerInvariant().Replace(' ', '_'), s)).ToList();
            _statusFilter.choices = new[] { "Any status", "Changed only", "Assigned textures", "Empty textures", "Animated", "Missing references", "Favorites" }
                .Select(s => Text(s.ToLowerInvariant().Replace(' ', '_'), s)).ToList();
            _typeFilter.SetValueWithoutNotify(_typeFilter.choices[Mathf.Max(0, typeIndex)]);
            _statusFilter.SetValueWithoutNotify(_statusFilter.choices[Mathf.Max(0, statusIndex)]);
            _placeholder.text = Text("search", "Search...");
            _search.tooltip = Text("search_help", "Filter properties and edit them here. Combine type and status filters. Escape clears all filters.");
            _typeFilter.tooltip = Text("type_filter_help", "Filter by property type.");
            _statusFilter.tooltip = Text("status_filter_help", "Filter by value or state. Assigned textures finds texture assignments, independent of tiling and offset.");
            _savedFilters.tooltip = Text("saved_searches", "Favorites and saved searches");
            _propertySearch.RefreshLanguage();
        }
    }
}
#endif
