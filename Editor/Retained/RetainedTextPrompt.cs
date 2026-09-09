#if UNITY_2021_3_OR_NEWER
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class RetainedTextPrompt : EditorWindow
    {
        string _value;
        Action<string> _save;
        internal static void Open(string title, string value, Action<string> save)
        {
            var window = CreateInstance<RetainedTextPrompt>();
            window.titleContent = new GUIContent(title); window._value = value; window._save = save;
            window.minSize = new Vector2(320, 100); window.ShowUtility();
        }
        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            root.AddToClassList("thry-dialog");
            var field = new TextField { value = _value }; root.Add(field);
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); actions.AddToClassList("thry-dialog-actions"); root.Add(actions);
            Action save = () => { _save?.Invoke(field.value); Close(); };
            var saveButton = new Button(save) { text = RetainedText.Get("save", "Save") }; saveButton.AddToClassList("thry-primary-action"); actions.Add(saveButton); actions.Add(new Button(Close) { text = RetainedText.Get("cancel", "Cancel") });
            RetainedWindow.Shortcuts(root, Close, save);
            field.schedule.Execute(field.Focus);
        }
    }
}
#endif
