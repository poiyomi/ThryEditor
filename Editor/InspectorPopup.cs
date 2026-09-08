using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>A menu selection is consumed by its owning control on the next layout pass.</summary>
    public sealed class InspectorPopup
    {
        private readonly Dictionary<int, int> _pending = new Dictionary<int, int>();

        public int Draw(Rect rect, GUIContent label, int selected, GUIContent[] choices, GUIStyle style = null)
        {
            var inspector = ShaderEditor.Active;
            int owner = inspector.Editor.GetInstanceID();
            int pending;
            if (Event.current.type == EventType.Layout && _pending.TryGetValue(owner, out pending))
            {
                _pending.Remove(owner);
                if (pending >= 0 && pending < choices.Length) { selected = pending; GUI.changed = true; }
            }
            if (!string.IsNullOrEmpty(label.text)) rect = EditorGUI.PrefixLabel(rect, label);
            var current = EditorGUI.showMixedValue ? new GUIContent("—")
                : selected >= 0 && selected < choices.Length ? choices[selected] : GUIContent.none;
            bool changed = GUI.changed;
            bool open = GUI.Button(rect, current, style ?? EditorStyles.popup);
            GUI.changed = changed;
            if (open) Open(rect, choices, selected, inspector);
            return selected;
        }

        public int DrawMask(Rect rect, GUIContent label, int mask, string[] options)
        {
            var inspector = ShaderEditor.Active;
            int owner = inspector.Editor.GetInstanceID();
            int pending;
            if (Event.current.type == EventType.Layout && _pending.TryGetValue(owner, out pending))
            {
                _pending.Remove(owner); mask = pending; GUI.changed = true;
            }
            int all = options.Length >= 32 ? -1 : (1 << options.Length) - 1;
            var choices = new[] { "Nothing", "Everything" }.Concat(options).Select(s => new GUIContent(s)).ToArray();
            string text = mask == 0 ? "Nothing" : mask == all ? "Everything"
                : string.Join(", ", options.Where((s, i) => (mask & (1 << i)) != 0));
            if (!string.IsNullOrEmpty(label.text)) rect = EditorGUI.PrefixLabel(rect, label);
            bool changed = GUI.changed;
            bool open = GUI.Button(rect, new GUIContent(EditorGUI.showMixedValue ? "—" : text), EditorStyles.popup);
            GUI.changed = changed;
            if (open)
            {
                int current = mask;
                var selected = Enumerable.Range(0, choices.Length).Where(i => i == 0 ? mask == 0 : i == 1 ? mask == all : (mask & (1 << (i - 2))) != 0).ToArray();
                var editor = inspector.Editor;
                Action<int> select = index => { if (editor == null) return; _pending[owner] = index == 0 ? 0 : index == 1 ? all : current ^ (1 << (index - 2)); editor.Repaint(); };
                if (inspector.ShowDropdown != null) inspector.ShowDropdown(rect, choices, selected, select);
                else PopupWindow.Show(rect, new InspectorDropdownPopup(choices, selected, rect.width, select));
            }
            return mask;
        }

        public int Draw(Rect rect, GUIContent label, int selected, string[] choices, GUIStyle style = null)
            => Draw(rect, label, selected, choices.Select(c => new GUIContent(c)).ToArray(), style);

        public Enum DrawEnum(Rect rect, Enum value)
        {
            var values = Enum.GetValues(value.GetType()).Cast<Enum>().ToArray();
            var names = values.Select(v => new GUIContent(ObjectNames.NicifyVariableName(v.ToString()))).ToArray();
            int selected = Draw(rect, GUIContent.none, Array.IndexOf(values, value), names);
            return selected >= 0 ? values[selected] : value;
        }

        internal void Open(Rect rect, GUIContent[] choices, int selected, ShaderEditor inspector)
        {
            var editor = inspector.Editor;
            int owner = editor.GetInstanceID();
            Action<int> select = index => { if (editor == null) return; _pending[owner] = index; editor.Repaint(); };
            if (inspector.ShowDropdown != null) inspector.ShowDropdown(rect, choices, new[] { selected }, select);
            else PopupWindow.Show(rect, new InspectorDropdownPopup(choices, selected, rect.width, select));
        }
    }
}
