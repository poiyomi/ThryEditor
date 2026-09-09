#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class RetainedMenu : VisualElement
    {
        internal sealed class Item
        {
            internal string Text;
            internal bool Checked;
            internal Action Action;
            internal bool Separator;
            internal List<Item> Children;
        }
        readonly List<VisualElement> _rows = new List<VisualElement>();
        readonly Stack<Item[]> _parents = new Stack<Item[]>();
        Item[] _items;
        readonly VisualElement _target, _panelRoot;
        readonly ScrollView _scroll;
        readonly Rect _anchor;
        int _selected = -1;
        bool _closed, _populating;
        string _typed = "";
        double _typedAt;

        internal static void Open(Rect anchor, VisualElement target, IEnumerable<Item> items)
        {
            if (target.panel == null) return;
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(candidate => candidate.rootVisualElement.panel == target.panel && candidate.rootVisualElement.Contains(target));
            var panelRoot = window == null ? target.panel.visualTree : window.rootVisualElement;
            panelRoot.Q<RetainedMenu>()?.Close(false);
            var menu = new RetainedMenu(anchor, target, BuildHierarchy(items), panelRoot);
            panelRoot.Add(menu);
            menu._scroll.contentContainer.Focus();
            menu.schedule.Execute(() => { if (!menu._closed && menu._selected >= 0) menu._scroll.ScrollTo(menu._rows[menu._selected]); });
        }
        static bool Enabled(Item item) => !item.Separator && (item.Action != null || item.Children != null);
        internal static Item[] BuildHierarchy(IEnumerable<Item> source)
        {
            var root = new List<Item>();
            foreach (var original in source)
            {
                var path = (original.Text ?? "").Split('/'); var level = root;
                for (int i = 0; i < path.Length - 1; i++)
                {
                    if (string.IsNullOrEmpty(path[i])) continue;
                    var branch = level.FirstOrDefault(item => item.Text == path[i] && item.Children != null);
                    if (branch == null) { branch = new Item { Text = path[i], Children = new List<Item>() }; level.Add(branch); }
                    branch.Checked |= original.Checked;
                    level = branch.Children;
                }
                level.Add(new Item { Text = path[path.Length - 1], Action = original.Action, Checked = original.Checked,
                    Separator = original.Separator, Children = original.Children });
            }
            return root.ToArray();
        }
        RetainedMenu(Rect anchor, VisualElement target, Item[] items, VisualElement panelRoot)
        {
            name = "thry-dropdown-menu"; _items = items; _target = target; _panelRoot = panelRoot; _anchor = anchor;
            AddToClassList("thry-dropdown-menu"); RetainedWindow.Style(this);
            RemoveFromClassList("thry-window"); RemoveFromClassList("thry-inspector"); RemoveFromClassList("thry-active");
            style.position = Position.Absolute; style.left = style.right = style.top = style.bottom = 0;
            var local = panelRoot.WorldToLocal(anchor.position);
            float width = Mathf.Min(Mathf.Max(anchor.width, 220), Mathf.Max(0, panelRoot.layout.width - 8));
            _scroll = new ScrollView(); _scroll.AddToClassList("thry-menu-card");
            _scroll.style.position = Position.Absolute;
            _scroll.style.left = Mathf.Clamp(local.x, 4, Mathf.Max(4, panelRoot.layout.width - width - 4));
            _scroll.style.width = width;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _scroll.contentContainer.focusable = true; Add(_scroll); Populate();
            RegisterCallback<PointerDownEvent>(e => { if (e.target == this) Close(); e.StopPropagation(); });
            RegisterCallback<NavigationMoveEvent>(e => {
                if (e.direction == NavigationMoveEvent.Direction.Right) { if (_selected >= 0 && _items[_selected].Children != null) Commit(_selected); }
                else if (e.direction == NavigationMoveEvent.Direction.Left) Back();
                else if (e.direction == NavigationMoveEvent.Direction.Down || e.direction == NavigationMoveEvent.Direction.Up)
                    Move(e.direction == NavigationMoveEvent.Direction.Down ? 1 : -1);
                else return;
                e.PreventDefault(); e.StopPropagation();
            });
            RegisterCallback<NavigationSubmitEvent>(e => { if (e.target is Button) return; Commit(_selected); e.PreventDefault(); e.StopPropagation(); });
            RegisterCallback<NavigationCancelEvent>(e => { Close(); e.PreventDefault(); e.StopPropagation(); });
            RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode == KeyCode.Home) { _selected = -1; Move(1); }
                else if (e.keyCode == KeyCode.End) { _selected = _items.Length; Move(-1); }
                else if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Tab) Close();
                else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.Backspace) Back();
                else if (!e.ctrlKey && !e.commandKey && !e.altKey && !char.IsControl(e.character)) FindTyped(e.character);
                else return;
                e.PreventDefault(); e.StopPropagation();
            });
            RegisterCallback<FocusOutEvent>(e => { var next = e.relatedTarget as VisualElement; if (!_populating && (next == null || !Contains(next))) Close(false); });
            panelRoot.RegisterCallback<GeometryChangedEvent>(OnPanelGeometry);
            RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == this) panelRoot.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometry); });
        }
        void Populate()
        {
            _populating = true;
            _scroll.Clear(); _rows.Clear(); _selected = -1; _typed = "";
            if (_parents.Count > 0)
            {
                var back = new Button(Back) { text = "\u2039 " + RetainedText.Get("back", "Back"), name = "thry-menu-back" };
                back.AddToClassList("thry-menu-back"); _scroll.Add(back);
            }
            for (int i = 0; i < _items.Length; i++)
            {
                int index = i; var item = _items[i]; var row = new VisualElement();
                row.AddToClassList(item.Separator ? "thry-menu-separator" : "thry-menu-item");
                if (!item.Separator)
                {
                    var check = new Label(item.Checked ? "\u2713" : ""); check.AddToClassList("thry-menu-check"); row.Add(check);
                    var caption = new Label(item.Text) { tooltip = item.Text }; caption.style.flexGrow = 1; row.Add(caption);
                    if (item.Children != null) row.Add(new Label("\u203a"));
                    row.EnableInClassList("thry-menu-disabled", !Enabled(item));
                    row.RegisterCallback<PointerMoveEvent>(e => Select(index));
                    row.RegisterCallback<PointerUpEvent>(e => { if (e.button != 0 || !Enabled(item)) return; e.StopPropagation(); Commit(index); });
                }
                _rows.Add(row); _scroll.Add(row);
            }
            float height = Mathf.Min(_items.Sum(item => item.Separator ? 9 : 26) + 12 + (_parents.Count > 0 ? 26 : 0), Mathf.Max(0, _panelRoot.layout.height - 8));
            _scroll.style.height = height;
            var point = _panelRoot.WorldToLocal(_anchor.position);
            bool below = _panelRoot.layout.height - point.y - _anchor.height >= height;
            _scroll.style.top = Mathf.Clamp(below ? point.y + _anchor.height : point.y - height, 4, Mathf.Max(4, _panelRoot.layout.height - height - 4));
            int selected = Array.FindIndex(_items, item => item.Checked && Enabled(item));
            if (selected < 0) selected = Array.FindIndex(_items, Enabled);
            if (selected >= 0) Select(selected, false);
            _scroll.contentContainer.Focus(); _populating = false;
        }
        void Back() { if (_parents.Count == 0) return; _items = _parents.Pop(); Populate(); }
        void FindTyped(char character)
        {
            double now = EditorApplication.timeSinceStartup;
            _typed = now - _typedAt > .8 ? character.ToString() : _typed + character; _typedAt = now;
            int index = Array.FindIndex(_items, item => Enabled(item) && item.Text.StartsWith(_typed, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Select(index);
        }
        void OnPanelGeometry(GeometryChangedEvent e) { if (e.oldRect.size != e.newRect.size) Close(); }
        void Move(int direction)
        {
            int index = _selected; if (index < 0 && direction < 0) index = _items.Length;
            do { index += direction; } while (index >= 0 && index < _items.Length && !Enabled(_items[index]));
            if (index >= 0 && index < _items.Length) Select(index);
        }
        void Select(int index, bool reveal = true)
        {
            if (!Enabled(_items[index])) return;
            if (_selected == index) return;
            if (_selected >= 0 && _selected < _rows.Count) _rows[_selected].RemoveFromClassList("thry-menu-selected");
            _selected = index; _rows[index].AddToClassList("thry-menu-selected");
            if (reveal) _scroll.ScrollTo(_rows[index]);
        }
        void Commit(int index)
        {
            if (index < 0 || index >= _items.Length || !Enabled(_items[index])) return;
            if (_items[index].Children != null) { _parents.Push(_items); _items = _items[index].Children.ToArray(); Populate(); return; }
            var action = _items[index].Action; Close(); action();
        }
        void Close(bool focus = true)
        {
            if (_closed) return; _closed = true; RemoveFromHierarchy();
            if (focus && _target.panel != null) _target.Focus();
        }
    }
}
#endif
