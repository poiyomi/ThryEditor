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
        private readonly Renderer[] _rendererContext;
        private readonly ShaderEditor _shaderOverride;
        private readonly Func<MaterialProperty[]> _propertyProvider;
        private readonly VisualElement _chrome, _tools;
        private readonly Label _title, _version, _state, _placeholder;
        private readonly ToolbarSearchField _search;
        private readonly DropdownField _typeFilter, _language;
        private readonly Toggle _changedFilter;
        private readonly RetainedSearch _propertySearch;
        private ShaderEditor _shader;
        private bool _builtTools;
        private bool _toolTheme;

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
            _search.RegisterValueChangedCallback(evt => {
                string value = evt.newValue;
                _placeholder.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateSearch();
            });
            _search.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode == KeyCode.Escape) { ClearSearch(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.DownArrow && _propertySearch.FocusFirst()) { evt.PreventDefault(); evt.StopPropagation(); }
                else if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && _propertySearch.FocusFirst()) { evt.PreventDefault(); evt.StopPropagation(); }
            });
            navigation.Add(_search);
            var filters = new VisualElement(); filters.AddToClassList("thry-search-filters");
            _typeFilter = new DropdownField(new List<string> { "All types", "Textures", "Colors", "Numbers", "Vectors", "Toggles" }, 0)
                { name = "thry-type-filter", tooltip = "Filter by property type. Combine Textures with Changed only to find assigned or modified textures." };
            _typeFilter.AddToClassList("thry-type-picker"); UseInspectorMenu(_typeFilter);
            _typeFilter.RegisterValueChangedCallback(evt => UpdateSearch()); filters.Add(_typeFilter);
            _changedFilter = new Toggle { name = "thry-changed-filter", text = "Changed only", tooltip = "Show properties that differ from the shader defaults on any selected material." };
            _changedFilter.AddToClassList("thry-changed-filter");
            _changedFilter.RegisterValueChangedCallback(evt => UpdateSearch()); filters.Add(_changedFilter);
            navigation.Add(filters); _chrome.Add(navigation);
            var utility = new VisualElement(); utility.AddToClassList("thry-utility");
            var collapse = new Button(() => Queue(() => _shader.CollapseCategories())) { text = "Collapse all", tooltip = "Collapse category foldouts without changing material settings." };
            collapse.AddToClassList("thry-text-button"); utility.Add(collapse);
            _language = new DropdownField { name = "thry-language", tooltip = "Inspector language" };
            _language.AddToClassList("thry-language");
            UseInspectorMenu(_language);
            _language.RegisterValueChangedCallback(evt => {
                int index = _language.index;
                Queue(() => { _shader.Locale.SelectLanguage(index); _shader.Reload(); });
            });
            utility.Add(_language); _chrome.Add(utility); Add(_chrome);
            _propertySearch = new RetainedSearch(this, ClearSearch);
            Add(_propertySearch);
            _body = new VisualElement { name = "thry-unfiltered-body" }; Add(_body);
            _fallback = new IMGUIContainer(drawInspector);
            RegisterCallback<AttachToPanelEvent>(evt => Undo.undoRedoPerformed += OnUndo);
            RegisterCallback<DetachFromPanelEvent>(evt => Undo.undoRedoPerformed -= OnUndo);
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
            if (_shader == null || _editor == null) return;
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

        private void OnUndo() { _retained?.Model.Notify(); }
        private void UpdateSearch()
        {
            if (_propertySearch == null || _body == null) return;
            string query = _search.value;
            string[] types = { "", "texture", "color", "number", "vector", "toggle" };
            if (_typeFilter.index > 0) query += " t:" + types[_typeFilter.index];
            if (_changedFilter.value) query += " is:changed";
            bool filtering = _retained != null && !string.IsNullOrWhiteSpace(query);
            EnableInClassList("thry-filtering", filtering);
            _body.style.display = filtering ? DisplayStyle.None : DisplayStyle.Flex;
            _placeholder.style.display = string.IsNullOrEmpty(_search.value) ? DisplayStyle.Flex : DisplayStyle.None;
            _propertySearch.Refresh(_retained?.Model, query);
        }
        private void ClearSearch()
        {
            _search.SetValueWithoutNotify("");
            _typeFilter.SetValueWithoutNotify("All types");
            _changedFilter.SetValueWithoutNotify(false);
            UpdateSearch();
        }
        private Renderer[] FindRenderers()
        {
            if(_rendererContext != null) return _rendererContext.Where(r=>r!=null).ToArray();
            for (VisualElement ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                var contract = ancestor.GetType().GetInterfaces().FirstOrDefault(t => t.Name == "IEditorElement");
                var editors = contract?.GetProperty("Editors")?.GetValue(ancestor) as Editor[];
                if (editors != null) return editors.Where(e => e != null).SelectMany(e => e.targets).OfType<Renderer>()
                    .Where(r => r.sharedMaterials.Any(m => _editor.targets.Contains(m))).Distinct().ToArray();
            }
            return Array.Empty<Renderer>();
        }
        internal void ShowLegacyMenu(GenericMenu source, VisualElement target)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var items = typeof(GenericMenu).GetProperty("menuItems",flags)?.GetValue(source) as System.Collections.IEnumerable;
            if (items == null) items = typeof(GenericMenu).GetField("m_MenuItems",flags)?.GetValue(source) as System.Collections.IEnumerable;
            if (items == null) throw new NotSupportedException("Unity's menu item collection is unavailable.");
            var choices = new List<RetainedMenu.Item>();
            foreach(var item in items)
            {
                var type = item.GetType();
                if ((bool)type.GetField("separator",flags).GetValue(item)) { choices.Add(new RetainedMenu.Item { Separator = true }); continue; }
                var label = (GUIContent)type.GetField("content",flags).GetValue(item);
                var callback = type.GetField("func",flags).GetValue(item) as GenericMenu.MenuFunction;
                var callback2 = type.GetField("func2",flags).GetValue(item) as GenericMenu.MenuFunction2;
                var data = type.GetField("userData",flags).GetValue(item);
                choices.Add(new RetainedMenu.Item { Text = label.text, Checked = (bool)type.GetField("on",flags).GetValue(item),
                    Action = callback == null && callback2 == null ? (Action)null : () => { _shader.ActivateRetained(); if(callback != null) callback(); else callback2(data); _retained.Model.Notify(); } });
            }
            RetainedMenu.Open(target.worldBound, target, choices);
        }
        private void UpdateState()
        {
            if (_editor == null || _editor.target == null) return;
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
                model.PropertyProvider = _propertyProvider;
                model.Renderers = FindRenderers();
                model.Refresh(); current.HasRetainedToolbar = true; current.ShowDropdown = ShowDropdown;
                _body.Clear(); _retained = new RetainedMaterialBody(model,this); _body.Add(_retained);
            }
            if (current != null) { _retained.Model.Renderers = FindRenderers(); _retained.Model.Refresh(); _retained.Synchronize(); }
            else if (_fallback.parent == null) { _retained = null; _body.Clear(); _body.Add(_fallback); }
            bool visible = current != null && current.Editor != null && current.SupportsRetainedToolbar;
            _chrome.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("thry-active", visible);
            EnableInClassList("thry-light", !EditorGUIUtility.isProSkin);
            if (!visible) { EnableInClassList("thry-filtering", false); _propertySearch.Refresh(null, ""); _body.style.display = DisplayStyle.Flex; return; }
            if (_shader != current) { _shader = current; _shader.FocusCategory(null); _builtTools = false; }
            string title = Regex.Replace(_shader.InspectorTitle, "<[^>]+>", "");
            var version = Regex.Match(title, @"\s+(?:v)?\d+\.\d+[\w.\-]*$", RegexOptions.IgnoreCase);
            var material = _editor.target as Material;
            var displayShader = material != null && material.IsLocked() ? ShaderOptimizer.GetOriginalShader(material, false) : material?.shader;
            string shaderName = displayShader != null ? displayShader.name : material?.GetTag(ShaderOptimizer.TAG_ORIGINAL_SHADER, false, "");
            _title.text = !string.IsNullOrEmpty(shaderName) ? shaderName.Split('/').Last() : version.Success ? title.Substring(0, version.Index) : title;
            _title.tooltip = shaderName ?? _title.text;
            _version.text = version.Value.Trim();
            bool editing = _shader.RootCategories.Any(g => SectionEditing.IsEditing(g));
            _state.text = editing ? "Editing shader sections" : _shader.IsLockedMaterial ? "Optimized material"
                : _shader.Materials.Length > 1 ? _shader.Materials.Length + " materials selected" : "Material editor";
            _state.EnableInClassList("thry-optimized", _shader.IsLockedMaterial);
            _state.style.display = editing || _shader.IsLockedMaterial || _shader.Materials.Length > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            // Filtered controls have their own view; legacy search expansion must not
            // modify the user's normal section/foldout state while they type.
            if (_shader.IsInSearchMode) _shader.SearchFromView("");
            UpdateSearch();

            if (!_builtTools || _toolTheme != EditorGUIUtility.isProSkin)
            {
                _toolTheme = EditorGUIUtility.isProSkin;
                _tools.Clear();
                AddTool("Settings", ToolbarIcons.Settings?.normal.background, () => EditorWindow.GetWindow<Settings>(false, "Thry Settings", true));
                Button toolsButton = null;
                toolsButton = AddTool("Tools", ToolbarIcons.Tools?.normal.background, () =>
                    ShowLegacyMenu(_shader.RetainedToolsMenu(), toolsButton));
                foreach (var extra in TopBarButtons.All)
                {
                    var button = extra;
                    AddTool(button.Tooltip, button.Icon()?.normal.background, () => button.OnClick());
                }
                _builtTools = true;
            }
            _tools.Q<Button>("thry-tool-edit-shader-sections")?.EnableInClassList("thry-selected", editing);
            if (_shader.Locale != null)
            {
                var names = _shader.Locale.LanguageNames;
                if (!_language.choices.SequenceEqual(names)) _language.choices = names.ToList();
                _language.SetValueWithoutNotify(names[Mathf.Clamp(_shader.Locale.LanguageIndex, 0, names.Length - 1)]);
            }
        }
    }
}
#endif
