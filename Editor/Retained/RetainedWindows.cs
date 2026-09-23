using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal static class RetainedWindow
    {
        private sealed class WindowShortcuts
        {
            internal Action Cancel;
            internal Action Confirm;
            internal bool ModifiedEnter;
        }
        private static readonly ConditionalWeakTable<VisualElement, WindowShortcuts> ShortcutHandlers = new ConditionalWeakTable<VisualElement, WindowShortcuts>();

        internal static void Shortcuts(VisualElement root, Action cancel, Action confirm = null, bool modifiedEnter = false)
        {
            var handlers = ShortcutHandlers.GetValue(root, element =>
            {
                var state = new WindowShortcuts();
                element.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.isDefaultPrevented) return;
                    if (e.keyCode == KeyCode.Escape && state.Cancel != null) state.Cancel();
                    else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && state.Confirm != null
                        && (!state.ModifiedEnter || e.ctrlKey || e.commandKey))
                    {
                        var target = e.target as VisualElement;
                        // Buttons/dropdowns own native submit behavior. Enter confirms these
                        // dialogs only from their text editor, never from a focused Cancel.
                        if (!(target is TextField) && target?.GetFirstAncestorOfType<TextField>() == null) return;
                        state.Confirm();
                    }
                    else return;
                    e.PreventDefault(); e.StopPropagation();
                });
                return state;
            });
            // CreateGUI may be called repeatedly on the same root after a preview is reset.
            // Replace the actions rather than accumulating stale keyboard callbacks.
            handlers.Cancel = cancel; handlers.Confirm = confirm; handlers.ModifiedEnter = modifiedEnter;
        }
        internal static ToolbarSearchField Search(string placeholder)
        {
            var field = new ToolbarSearchField(); field.AddToClassList("thry-search");
            var hint = new Label(placeholder) { pickingMode = PickingMode.Ignore }; hint.AddToClassList("thry-search-placeholder"); field.Add(hint);
            field.RegisterValueChangedCallback(e => hint.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None);
            field.RegisterCallback<FocusInEvent>(e => field.AddToClassList("thry-search-focused"));
            field.RegisterCallback<FocusOutEvent>(e => field.RemoveFromClassList("thry-search-focused"));
            return field;
        }
        internal static void Style(VisualElement root)
        {
            root.AddToClassList("thry-inspector");root.AddToClassList("thry-active");root.AddToClassList("thry-window");root.EnableInClassList("thry-light",!EditorGUIUtility.isProSkin);
            var sheet=Resources.Load<StyleSheet>("ThryTheme");if(sheet!=null && !root.styleSheets.Contains(sheet))root.styleSheets.Add(sheet);
            RetainedAppearance.Install(root);
        }
        internal static void Dropdown(DropdownField field)
        {
            Action show=()=>{
                field.Focus();
                RetainedMenu.Open(field.worldBound, field, field.choices.Select((choice, index) => new RetainedMenu.Item
                { Text = choice, Checked = field.index == index, Action = () => SelectDropdownChoice(field, choice) }));
            };
            field.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;e.PreventDefault();e.StopImmediatePropagation();show();},TrickleDown.TrickleDown);
            field.RegisterCallback<NavigationSubmitEvent>(e=>{e.PreventDefault();e.StopImmediatePropagation();show();},TrickleDown.TrickleDown);
        }

        internal static void SelectDropdownChoice(DropdownField field, string value, bool applyOnReselect = false)
        {
            if (!field.choices.Contains(value)) return;
            bool applyMixed = field.showMixedValue;
            string previous = field.value;
            field.showMixedValue = false;
            // BaseField suppresses equal-value assignments, but an explicit selection
            // must still resolve mixed values and reapply authored preset actions.
            if (applyMixed || (applyOnReselect && previous == value))
            {
                field.SetValueWithoutNotify(value);
                using (var change = ChangeEvent<string>.GetPooled(previous, value))
                { change.target = field; field.SendEvent(change); }
            }
            else field.value = value;
        }
    }

    public partial class Settings
    {
        private void OnEnable() { RetainedAppearance.Changed += RefreshAppearanceControls; }
        private void OnDisable() { RetainedAppearance.Changed -= RefreshAppearanceControls; }

        private void ChangeAppearance(Action change, bool saveProject)
        {
            try { change(); if (saveProject) Config.Instance.Save(); }
            catch (Exception e) when (e is System.IO.InvalidDataException || e is System.IO.IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { Debug.LogError("[ThryEditor] Could not save inspector appearance. " + e.Message); }
            RetainedAppearance.Refresh();
        }

        private void RefreshAppearanceControls()
        {
            var root = rootVisualElement;
            var shared = root.Q<Toggle>("use-shared-inspector-appearance");
            if (shared == null) return;
            shared.SetValueWithoutNotify(Config.Instance.useSharedInspectorAppearance);
            var appearance = InspectorAppearancePreferences.Shared.Get(Config.Instance);
            foreach (string key in InspectorAppearancePreferences.Fields)
            {
                var row = root.Q(key);
                if (row == null) continue;
                if (key == "inspectorTextSize")
                { var field = row.Q<DropdownField>(); field.SetValueWithoutNotify(field.choices[Mathf.Clamp((int)appearance.inspectorTextSize, 0, field.choices.Count - 1)]); }
                else if (key == "inspectorPropertyHeight" || key == "inspectorHeaderHeight")
                {
                    int value = (int)typeof(Config).GetField(key).GetValue(appearance);
                    int[] heights = key == "inspectorHeaderHeight" ? new[] { 18, 20, 22, 24 } : new[] { 18, 20, 22 };
                    int index = Array.IndexOf(heights, value);
                    if (index < 0) index = key == "inspectorHeaderHeight" ? 2 : 0;
                    var field = row.Q<DropdownField>(); field.SetValueWithoutNotify(field.choices[index]);
                }
                else row.Q<SliderInt>().SetValueWithoutNotify((int)typeof(Config).GetField(key).GetValue(appearance));
            }
            var reset = root.Q<Button>("reset-inspector-appearance");
            if (reset != null) reset.text = Config.Instance.useSharedInspectorAppearance
                ? RetainedText.Get("reset_shared_inspector_appearance", "Reset shared appearance")
                : RetainedText.Get("reset_inspector_appearance", "Reset appearance");
        }

        private sealed class SearchSection
        {
            internal Foldout Section;
            internal bool WasExpanded;
            internal readonly List<KeyValuePair<string, VisualElement>> Rows = new List<KeyValuePair<string, VisualElement>>();
        }
        public void CreateGUI()
        {
            EditorLocale.editor.SetSelectedLocale(Config.Instance.locale);
            var root=rootVisualElement;root.Clear();RetainedWindow.Style(root);root.AddToClassList("thry-settings");
            var title=new Label(RetainedText.Get("settings_title", "Thry Settings"));title.AddToClassList("thry-title");root.Add(title);
            var scroll=new ScrollView();scroll.style.flexGrow=1;root.Add(scroll);
            var search=RetainedWindow.Search(RetainedText.Get("search_settings", "Search settings…"));search.AddToClassList("thry-settings-search");root.Insert(1,search);
            string[][] groups={
                new[]{"Theme","inspectorDarkGray","inspectorMediumGray","inspectorLightGray","inspectorTextSize","inspectorPropertyHeight","inspectorHeaderHeight"},
                new[]{"Appearance","showRenderQueue","showColorspaceWarnings","showStarNextToNonDefaultProperties","showAnimatedDotOnHeaders","showNotes","staggeringRowColors"},
                new[]{"Editing & animation","autoMarkPropertiesAnimated","allowCustomLockingRenaming"},
                new[]{"Avatar fixes","autoSetAnchorOverride","humanBoneAnchor","anchorOverrideObjectName"},
                new[]{"Textures & gradients","default_texture_type","texturePackerCompressionWithAlphaOverwrite","texturePackerCompressionNoAlphaOverwrite","gradientEditorCompressionOverwrite","gradient_name"},
                new[]{"Texture packing","inlinePackerChrunchCompression","inlinePackerSaveLocation","inlinePackerSaveLocationCustom"},
                new[]{"Shader optimization","forceAsyncCompilationPreview","saveAfterLockUnlock","fixKeywordsWhenLocking","lockedShaderCacheBudgetMB"},
                new[]{"Developer","loggingLevel","showManualReloadButton","enableDeveloperMode","disableUnlockedShaderStrippingOnBuild"}
            };
            var sections = new List<SearchSection>();
            foreach(var group in groups)
            {
                var section=new Foldout {text=RetainedText.Get("settings_"+group[0].ToLowerInvariant().Replace(" ","_"),group[0]),value=true};RetainedUiState.Bind(section,"settings",group[0],true);scroll.Add(section);
                var searchable = new SearchSection { Section = section }; sections.Add(searchable);
                if (group[0] == "Theme")
                {
                    var shared = new Toggle(RetainedText.Get("use_shared_inspector_appearance", "Use shared appearance on this computer")) {
                        name = "use-shared-inspector-appearance", value = Config.Instance.useSharedInspectorAppearance
                    };
                    shared.labelElement.style.whiteSpace = WhiteSpace.Normal;
                    shared.labelElement.style.flexShrink = 1;
                    shared.tooltip = RetainedText.Get("use_shared_inspector_appearance_help",
                        "Share the Theme controls across projects for your user account. Turn this off to restore this project's appearance.");
                    shared.RegisterValueChangedCallback(e => ChangeAppearance(
                        () => InspectorAppearancePreferences.Shared.SetShared(Config.Instance, e.newValue), true));
                    section.Add(shared);
                    searchable.Rows.Add(new KeyValuePair<string, VisualElement>("Theme shared appearance computer projects", shared));
                }
                foreach(var key in group.Skip(1))
                {
                    var member=typeof(Config).GetField(key);string label=EditorLocale.editor.Get(key);if(string.IsNullOrEmpty(label)||label==key)label=ObjectNames.NicifyVariableName(key);
                    VisualElement value;var row=RetainedFields.Row(label,out value);row.tooltip=EditorLocale.editor.Get(key+"_tooltip");row.name=key;section.Add(row);
                    bool isTheme = group[0] == "Theme";
                    var source = isTheme ? InspectorAppearancePreferences.Shared.Get(Config.Instance) : Config.Instance;
                    Action<object> save=v=>{
                        if (isTheme) ChangeAppearance(() => InspectorAppearancePreferences.Shared.SetValue(Config.Instance, key, v),
                            !Config.Instance.useSharedInspectorAppearance);
                        else { member.SetValue(Config.Instance,v); Config.Instance.Save(); ShaderEditor.ReloadActive(); }
                    };
                    if (key == "inspectorDarkGray" || key == "inspectorMediumGray" || key == "inspectorLightGray")
                    {
                        int initial = (int)member.GetValue(source);
                        var field = new SliderInt(-20, 20) { showInputField = true, value = Mathf.Clamp(initial, -20, 20) };
                        field.RegisterValueChangedCallback(e => save(Mathf.Clamp(e.newValue, field.lowValue, field.highValue)));
                        value.Add(field);
                    }
                    else if (key == "inspectorPropertyHeight" || key == "inspectorHeaderHeight")
                    {
                        var heights = key == "inspectorHeaderHeight" ? new[] { 18, 20, 22, 24 } : new[] { 18, 20, 22 };
                        int selected = Array.IndexOf(heights, (int)member.GetValue(source));
                        int defaultIndex = key == "inspectorHeaderHeight" ? 2 : 0;
                        var captions = heights.Select((height, index) => index == defaultIndex
                            ? RetainedText.Get("default", "Default") + " (" + height + " px)"
                            : height + " px").ToList();
                        var field = new DropdownField(captions, selected < 0 ? defaultIndex : selected);
                        RetainedWindow.Dropdown(field);
                        field.RegisterValueChangedCallback(e => { if (field.index >= 0 && field.index < heights.Length) save(heights[field.index]); });
                        value.Add(field);
                    }
                    else if(member.FieldType==typeof(bool)){var field=new Toggle {value=(bool)member.GetValue(source)};field.RegisterValueChangedCallback(e=>save(e.newValue));value.Add(field);}
                    else if(member.FieldType==typeof(int)){var field=new IntegerField {value=(int)member.GetValue(source),isDelayed=true};field.RegisterValueChangedCallback(e=>save(Mathf.Max(0,e.newValue)));value.Add(field);}
                    else if(member.FieldType==typeof(string)){var field=new TextField {value=(string)member.GetValue(source),isDelayed=true};field.RegisterValueChangedCallback(e=>save(e.newValue));value.Add(field);}
                    else if(member.FieldType.IsEnum)
                    {
                        var names = Enum.GetNames(member.FieldType);
                        var captions = names.Select(name => RetainedText.EnumCaption(member.FieldType, name)).ToList();
                        int selected = Array.IndexOf(names, member.GetValue(source).ToString());
                        var field = new DropdownField(captions, Mathf.Max(0, selected));
                        RetainedWindow.Dropdown(field);
                        field.RegisterValueChangedCallback(e => { if (field.index >= 0 && field.index < names.Length) save(Enum.Parse(member.FieldType, names[field.index])); });
                        value.Add(field);
                    }
                    if (key == "inlinePackerSaveLocationCustom")
                        row.tooltip = RetainedText.Get("custom_texture_folder_help", "Used when Texture save location is Custom folder. Choose a folder inside Assets.");
                    searchable.Rows.Add(new KeyValuePair<string, VisualElement>(group[0] + " " + label + " " + key, row));
                }
                if (group[0] == "Theme")
                {
                    var reset = new Button(() => ChangeAppearance(
                        () => InspectorAppearancePreferences.Shared.Reset(Config.Instance), !Config.Instance.useSharedInspectorAppearance)) {
                        name = "reset-inspector-appearance",
                        text = Config.Instance.useSharedInspectorAppearance
                            ? RetainedText.Get("reset_shared_inspector_appearance", "Reset shared appearance")
                            : RetainedText.Get("reset_inspector_appearance", "Reset appearance")
                    };
                    section.Add(reset);
                    searchable.Rows.Add(new KeyValuePair<string, VisualElement>("Theme gray grey shades font text size property header height spacing reset", reset));
                }
            }
            var noResults = new Label(RetainedText.Get("settings_no_matches", "No settings match your search.")) { name = "settings-search-empty" };
            noResults.AddToClassList("thry-muted"); noResults.style.display = DisplayStyle.None; scroll.Add(noResults);
            bool filtering = false;
            search.RegisterValueChangedCallback(e =>
            {
                var words = (e.newValue ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool active = words.Length > 0;
                int visibleRows = 0;
                foreach (var section in sections)
                {
                    if (active && !filtering) section.WasExpanded = section.Section.value;
                    int count = 0;
                    foreach (var row in section.Rows)
                    {
                        bool match = words.All(word => row.Key.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                        row.Value.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                        if (match) count++;
                    }
                    section.Section.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    if (active && count > 0) section.Section.SetValueWithoutNotify(true);
                    else if (!active && filtering) section.Section.SetValueWithoutNotify(section.WasExpanded);
                    visibleRows += count;
                }
                noResults.style.display = active && visibleRows == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                filtering = active;
            });
            var language=new DropdownField(RetainedText.Get("editor_language","Editor language"),EditorLocale.editor.available_locales.ToList(),EditorLocale.editor.selected_locale_index);RetainedWindow.Dropdown(language);root.Add(language);
            language.RegisterValueChangedCallback(e=>{Config.Instance.locale=e.newValue;Config.Instance.Save();ShaderEditor.ReloadActive();CreateGUI();});
        }
    }
}
