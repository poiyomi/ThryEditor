using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public static class ThryUI
    {
        public static void Style(VisualElement root) => RetainedWindow.Style(root);
        public static void Dropdown(DropdownField field) => RetainedWindow.Dropdown(field);
        public static void Prompt(string title, string value, System.Action<string> save) => RetainedTextPrompt.Open(title, value, save);
    }
}
