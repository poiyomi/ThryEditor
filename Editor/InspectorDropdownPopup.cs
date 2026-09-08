using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>Inspector-colored choices without the operating system's menu styling.</summary>
    public sealed class InspectorDropdownPopup : PopupWindowContent
    {
        private const float RowHeight = 26;
        private readonly GUIContent[] _choices;
        private readonly Action<int> _select;
        private readonly int[] _selected;
        private readonly float _width;
        private int _highlighted;
        private Vector2 _scroll;
        private GUIStyle _label;

        public InspectorDropdownPopup(GUIContent[] choices, int selected, float width, Action<int> select)
            : this(choices, new[] { selected }, width, select) { }

        public InspectorDropdownPopup(GUIContent[] choices, int[] selected, float width, Action<int> select)
        {
            _choices = choices;
            _selected = selected;
            _highlighted = Mathf.Clamp(selected.FirstOrDefault(), 0, choices.Length - 1);
            _width = Mathf.Max(190, width);
            _select = select;
        }

        public override Vector2 GetWindowSize() => new Vector2(_width, Mathf.Min(320, _choices.Length * RowHeight + 8));
        public override void OnOpen() { editorWindow.wantsMouseMove = true; }

        private void Choose(int index)
        {
            editorWindow.Close();
            _select(index);
        }

        public override void OnGUI(Rect rect)
        {
            if (_label == null) _label = new GUIStyle(EditorStyles.label) {
                fontSize = 12, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = InspectorTheme.Text }
            };
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { editorWindow.Close(); e.Use(); return; }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Choose(_highlighted); e.Use(); return; }
                if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
                {
                    _highlighted = (_highlighted + (e.keyCode == KeyCode.DownArrow ? 1 : _choices.Length - 1)) % _choices.Length;
                    _scroll.y = Mathf.Clamp(_scroll.y, (_highlighted + 1) * RowHeight - rect.height + 8, _highlighted * RowHeight);
                    e.Use(); editorWindow.Repaint();
                }
            }
            InspectorTheme.Fill(rect, InspectorTheme.Background, 0);
            var viewport = new Rect(4, 4, rect.width - 8, rect.height - 8);
            bool scrolls = _choices.Length * RowHeight > viewport.height;
            var content = new Rect(0, 0, viewport.width - (scrolls ? 14 : 0), _choices.Length * RowHeight);
            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            for (int i = 0; i < _choices.Length; i++)
            {
                var row = new Rect(0, i * RowHeight, content.width, RowHeight);
                if (e.type == EventType.MouseMove && row.Contains(e.mousePosition)) { _highlighted = i; editorWindow.Repaint(); }
                if (i == _highlighted) InspectorTheme.Fill(row, InspectorTheme.Hover, 4);
                else if (_selected.Contains(i)) InspectorTheme.Fill(row, InspectorTheme.Surface, 4);
                GUI.Label(new Rect(row.x + 28, row.y, row.width - 36, row.height), _choices[i], _label);
                if (_selected.Contains(i)) GUI.Label(new Rect(row.x + 8, row.y, 18, row.height), "✓", _label);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { GUI.EndScrollView(); Choose(i); return; }
            }
            GUI.EndScrollView();
        }
    }
}
