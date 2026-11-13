using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class RetainedTextPrompt : EditorWindow
    {
        string _value, _option;
        bool _optionValue;
        Action<string> _save;
        Action<string, bool> _saveWithOption;
        internal static void Open(string title, string value, Action<string> save)
        {
            var window = CreateInstance<RetainedTextPrompt>();
            window.titleContent = new GUIContent(title); window._value = value; window._save = save;
            window.minSize = new Vector2(320, 100); window.ShowUtility();
        }
        internal static void Open(string title, string value, string option, bool optionValue, Action<string, bool> save)
        {
            var window = CreateInstance<RetainedTextPrompt>();
            window.titleContent = new GUIContent(title); window._value = value; window._saveWithOption = save;
            window._option = option; window._optionValue = optionValue;
            window.minSize = new Vector2(320, 124); window.ShowUtility();
        }
        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog");
            var field = new TextField { value = _value }; root.Add(field);
            Toggle option = null;
            if (_option != null) { option = new Toggle(_option) { value = _optionValue }; root.Add(option); }
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); actions.AddToClassList("thry-dialog-actions"); root.Add(actions);
            Action save = () => { _save?.Invoke(field.value); _saveWithOption?.Invoke(field.value, option != null && option.value); Close(); };
            var saveButton = new Button(save) { text = RetainedText.Get("save", "Save") }; saveButton.AddToClassList("thry-primary-action"); actions.Add(saveButton); actions.Add(new Button(Close) { text = RetainedText.Get("cancel", "Cancel") });
            RetainedWindow.Shortcuts(root, Close, save);
            field.schedule.Execute(field.Focus);
        }
    }
}
