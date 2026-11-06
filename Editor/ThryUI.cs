using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public static class ThryUI
    {
        public sealed class MenuItem
        {
            public string Text;
            public Action Action;
            public bool Checked;
            public bool Separator;

            public MenuItem() { }
            public MenuItem(string text, Action action, bool isChecked = false) { Text = text; Action = action; Checked = isChecked; }
            public static MenuItem Divider() => new MenuItem { Separator = true };
        }
        public static void Style(VisualElement root) => RetainedWindow.Style(root);
        public static void Dropdown(DropdownField field) => RetainedWindow.Dropdown(field);
        public static void Prompt(string title, string value, System.Action<string> save) => RetainedTextPrompt.Open(title, value, save);
        public static ToolbarSearchField Search(string placeholder) => RetainedWindow.Search(placeholder);
        public static void Menu(VisualElement anchor, IEnumerable<MenuItem> items)
        {
            var converted = Convert(items);
            if (converted.Count > 0) RetainedMenu.Open(anchor.worldBound, anchor, converted);
        }
        public static void ContextMenu(VisualElement element, Func<IEnumerable<MenuItem>> items)
        {
            RetainedMenu.RegisterContext(element, position =>
            {
                var converted = Convert(items?.Invoke());
                if (converted.Count > 0) RetainedMenu.OpenContext(position, element, converted);
            });
        }
        public static void Remember(Foldout foldout, string scope, string key, bool defaultValue = false) => RetainedUiState.Bind(foldout, scope, key, defaultValue);

        static List<RetainedMenu.Item> Convert(IEnumerable<MenuItem> items) => (items ?? Enumerable.Empty<MenuItem>()).Where(item => item != null).Select(item => new RetainedMenu.Item { Text = item.Text, Action = item.Action, Checked = item.Checked, Separator = item.Separator }).ToList();
    }
}
