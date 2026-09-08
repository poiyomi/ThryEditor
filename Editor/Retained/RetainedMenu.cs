#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>A panel-local menu with consistent pointer, keyboard and mixed-value behavior.</summary>
    internal sealed class RetainedMenu : VisualElement
    {
        internal sealed class Item
        {
            internal string Text;
            internal bool Checked;
            internal Action Action;
            internal bool Separator;
        }

        readonly List<VisualElement> _rows = new List<VisualElement>();
        readonly Item[] _items;
        readonly VisualElement _target;
        readonly ScrollView _scroll;
        int _selected = -1;
        bool _closed;

        internal static void Open(Rect anchor, VisualElement target, IEnumerable<Item> items)
        {
            if (target.panel == null) return;
            // Unity scopes its editor font and built-in control styles to the window
            // root. A sibling at the raw panel root has no font, so its labels measure
            // to zero even though the menu rows themselves still receive input.
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(candidate => candidate.rootVisualElement.panel == target.panel && candidate.rootVisualElement.Contains(target));
            var panelRoot = window == null ? target.panel.visualTree : window.rootVisualElement;
            var previous = panelRoot.Q<RetainedMenu>();
            previous?.Close(false);
            var menu = new RetainedMenu(anchor, target, items.ToArray(), panelRoot);
            panelRoot.Add(menu);
            menu._scroll.contentContainer.Focus();
        }

        RetainedMenu(Rect anchor, VisualElement target, Item[] items, VisualElement panelRoot)
        {
            name = "thry-dropdown-menu";
            _items = items;
            _target = target;
            AddToClassList("thry-dropdown-menu");
            RetainedWindow.Style(this);
            RemoveFromClassList("thry-window");
            // The overlay catches outside clicks, but must not paint the inspector's
            // opaque window background over the material underneath the menu card.
            RemoveFromClassList("thry-inspector");
            RemoveFromClassList("thry-active");
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;

            Vector2 local = panelRoot.WorldToLocal(anchor.position);
            float width = Mathf.Min(Mathf.Max(anchor.width, 220), panelRoot.layout.width - 8);
            // Include padding, borders and a little pixel-rounding room. A fixed height
            // cap or missing border space gives even short menus a needless scrollbar.
            float desiredHeight = items.Sum(i => i.Separator ? 9 : 26) + 12;
            float below = panelRoot.layout.height - local.y - anchor.height - 4;
            float above = local.y - 4;
            bool openBelow = below >= desiredHeight || below >= above;
            float height = Mathf.Min(desiredHeight, Mathf.Max(0, panelRoot.layout.height - 8));
            _scroll = new ScrollView();
            _scroll.AddToClassList("thry-menu-card");
            _scroll.style.position = Position.Absolute;
            _scroll.style.left = Mathf.Clamp(local.x, 4, Mathf.Max(4, panelRoot.layout.width - width - 4));
            _scroll.style.top = Mathf.Clamp(openBelow ? local.y + anchor.height : local.y - height,
                4, Mathf.Max(4, panelRoot.layout.height - height - 4));
            _scroll.style.width = width;
            _scroll.style.height = height;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _scroll.contentContainer.focusable = true;
            Add(_scroll);

            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var item = items[i];
                var row = new VisualElement();
                row.AddToClassList(item.Separator ? "thry-menu-separator" : "thry-menu-item");
                if (!item.Separator)
                {
                    var check = new Label(item.Checked ? "✓" : "");
                    check.AddToClassList("thry-menu-check");
                    row.Add(check);
                    row.Add(new Label(item.Text) { tooltip = item.Text });
                    row.EnableInClassList("thry-menu-disabled", item.Action == null);
                    row.RegisterCallback<PointerMoveEvent>(e => Select(index));
                    row.RegisterCallback<PointerUpEvent>(e =>
                    {
                        if (e.button != 0 || item.Action == null) return;
                        e.StopPropagation();
                        Commit(index);
                    });
                }
                _rows.Add(row);
                _scroll.Add(row);
            }
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == this) Close();
                e.StopPropagation();
            });
            RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction != NavigationMoveEvent.Direction.Down && e.direction != NavigationMoveEvent.Direction.Up) return;
                Move(e.direction == NavigationMoveEvent.Direction.Down ? 1 : -1);
                e.PreventDefault(); e.StopPropagation();
            });
            RegisterCallback<NavigationSubmitEvent>(e => { Commit(_selected); e.PreventDefault(); e.StopPropagation(); });
            RegisterCallback<NavigationCancelEvent>(e => { Close(); e.PreventDefault(); e.StopPropagation(); });
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Home) { _selected = -1; Move(1); }
                else if (e.keyCode == KeyCode.End) { _selected = _items.Length; Move(-1); }
                else if (e.keyCode == KeyCode.Escape) Close();
                else return;
                e.PreventDefault(); e.StopPropagation();
            });
            RegisterCallback<FocusOutEvent>(e =>
            {
                var next = e.relatedTarget as VisualElement;
                if (next != null && !Contains(next)) Close(false);
            });
            panelRoot.RegisterCallback<GeometryChangedEvent>(OnPanelGeometry);
            RegisterCallback<DetachFromPanelEvent>(e => panelRoot.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometry));
        }

        void OnPanelGeometry(GeometryChangedEvent e) { if (e.oldRect.size != e.newRect.size) Close(); }

        void Move(int direction)
        {
            int index = _selected;
            if (index < 0 && direction < 0) index = _items.Length;
            do { index += direction; }
            while (index >= 0 && index < _items.Length && (_items[index].Separator || _items[index].Action == null));
            if (index >= 0 && index < _items.Length) Select(index);
        }

        void Select(int index)
        {
            if (_items[index].Separator || _items[index].Action == null) return;
            if (_selected >= 0 && _selected < _rows.Count) _rows[_selected].RemoveFromClassList("thry-menu-selected");
            _selected = index;
            _rows[index].AddToClassList("thry-menu-selected");
            _scroll.ScrollTo(_rows[index]);
        }

        void Commit(int index)
        {
            if (index < 0 || index >= _items.Length || _items[index].Action == null) return;
            var action = _items[index].Action;
            Close();
            action();
        }

        void Close(bool focus = true)
        {
            if (_closed) return;
            _closed = true;
            RemoveFromHierarchy();
            if (focus && _target.panel != null) _target.Focus();
        }
    }
}
#endif
