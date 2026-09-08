#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal static class RetainedWindow
    {
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
            var sheet=Resources.Load<StyleSheet>("ThryInspector");if(sheet!=null)root.styleSheets.Add(sheet);
            var auxiliary = Resources.Load<StyleSheet>("ThryAuxiliary"); if (auxiliary != null) root.styleSheets.Add(auxiliary);
        }
        internal static void Dropdown(DropdownField field)
        {
            Action show=()=>{
                field.Focus();
                RetainedMenu.Open(field.worldBound, field, field.choices.Select((choice, index) => new RetainedMenu.Item
                { Text = choice, Checked = field.index == index, Action = () => { field.showMixedValue = false; field.value = choice; } }));
            };
            field.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;e.PreventDefault();e.StopImmediatePropagation();show();},TrickleDown.TrickleDown);
            field.RegisterCallback<NavigationSubmitEvent>(e=>{e.PreventDefault();e.StopImmediatePropagation();show();},TrickleDown.TrickleDown);
        }
    }

    public partial class Settings
    {
        private sealed class SearchSection
        {
            internal Foldout Section;
            internal bool WasExpanded;
            internal readonly List<KeyValuePair<string, VisualElement>> Rows = new List<KeyValuePair<string, VisualElement>>();
        }
        public void CreateGUI()
        {
            var root=rootVisualElement;root.Clear();RetainedWindow.Style(root);root.AddToClassList("thry-settings");
            var title=new Label("Thry Settings");title.AddToClassList("thry-title");root.Add(title);
            var scroll=new ScrollView();scroll.style.flexGrow=1;root.Add(scroll);
            var search=RetainedWindow.Search("Search settings…");search.style.marginTop=8;search.style.marginBottom=8;root.Insert(1,search);
            string[][] groups={
                new[]{"Appearance","showRenderQueue","showColorspaceWarnings","showStarNextToNonDefaultProperties","showAnimatedDotOnHeaders","showNotes"},
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
                var section=new Foldout {text=group[0],value=true};scroll.Add(section);
                var searchable = new SearchSection { Section = section }; sections.Add(searchable);
                foreach(var key in group.Skip(1))
                {
                    var member=typeof(Config).GetField(key);string label=EditorLocale.editor.Get(key);if(string.IsNullOrEmpty(label)||label==key)label=ObjectNames.NicifyVariableName(key);
                    VisualElement value;var row=RetainedFields.Row(label,out value);row.tooltip=EditorLocale.editor.Get(key+"_tooltip");row.name=key;section.Add(row);
                    Action<object> save=v=>{member.SetValue(Config.Instance,v);Config.Instance.Save();ShaderEditor.ReloadActive();};
                    if(member.FieldType==typeof(bool)){var field=new Toggle {value=(bool)member.GetValue(Config.Instance)};field.RegisterValueChangedCallback(e=>save(e.newValue));value.Add(field);}
                    else if(member.FieldType==typeof(int)){var field=new IntegerField {value=(int)member.GetValue(Config.Instance),isDelayed=true};field.RegisterValueChangedCallback(e=>save(Mathf.Max(0,e.newValue)));value.Add(field);}
                    else if(member.FieldType==typeof(string)){var field=new TextField {value=(string)member.GetValue(Config.Instance),isDelayed=true};field.RegisterValueChangedCallback(e=>save(e.newValue));value.Add(field);}
                    else if(member.FieldType.IsEnum){var field=new DropdownField(Enum.GetNames(member.FieldType).ToList(),0);field.SetValueWithoutNotify(member.GetValue(Config.Instance).ToString());RetainedWindow.Dropdown(field);field.RegisterValueChangedCallback(e=>save(Enum.Parse(member.FieldType,e.newValue)));value.Add(field);}
                    searchable.Rows.Add(new KeyValuePair<string, VisualElement>(group[0] + " " + label + " " + key, row));
                }
            }
            var noResults = new Label("No settings match your search.") { name = "settings-search-empty" };
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
            var language=new DropdownField("Language",EditorLocale.editor.available_locales.ToList(),EditorLocale.editor.selected_locale_index);RetainedWindow.Dropdown(language);root.Add(language);
            language.RegisterValueChangedCallback(e=>{Config.Instance.locale=e.newValue;Config.Instance.Save();ShaderEditor.ReloadActive();CreateGUI();});
        }
    }
}
#endif
