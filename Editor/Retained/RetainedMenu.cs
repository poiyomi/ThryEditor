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

        /// <summary>
        /// One row of one card. A slash in an item's text starts a category, so "Standard/Opaque/OneSided"
        /// becomes three rows across three cards rather than one very long row.
        /// </summary>
        sealed class Node
        {
            internal string Text;
            internal string Path;
            internal Item Item;
            internal bool Separator;
            internal List<Node> Children;
            // A category cannot be checked itself, so it reports whether the selection is somewhere
            // inside it. Without that, finding the current value means opening every branch in turn.
            internal bool HoldsChecked;
            internal bool IsCategory { get { return Children != null; } }
            internal bool IsSelectable { get { return !Separator && (Children != null || Item.Action != null); } }
        }

        const float RowHeight = 26, SeparatorHeight = 9, CardPadding = 12, MinWidth = 220, Margin = 4;

        readonly VisualElement _panel;
        readonly VisualElement _target;
        Level _root, _active;
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
            menu._root.Focus();
        }

        RetainedMenu(Rect anchor, VisualElement target, Item[] items, VisualElement panelRoot)
        {
            name = "thry-dropdown-menu";
            _panel = panelRoot;
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

            var nodes = BuildTree(items);
            float width = Mathf.Min(Mathf.Max(anchor.width, MinWidth), panelRoot.layout.width - 2 * Margin);
            _active = _root = new Level(this, null, nodes, width);

            Vector2 local = panelRoot.WorldToLocal(anchor.position);
            float height = _root.Height;
            float below = panelRoot.layout.height - local.y - anchor.height - Margin;
            float above = local.y - Margin;
            bool openBelow = below >= _root.DesiredHeight || below >= above;
            _root.Place(local.x, openBelow ? local.y + anchor.height : local.y - height);
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == this) Close();
                e.StopPropagation();
            });
            RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction == NavigationMoveEvent.Direction.Down || e.direction == NavigationMoveEvent.Direction.Up)
                    _active.Move(e.direction == NavigationMoveEvent.Direction.Down ? 1 : -1);
                else if (e.direction == NavigationMoveEvent.Direction.Right) _active.Descend();
                else if (e.direction == NavigationMoveEvent.Direction.Left) Ascend();
                else return;
                e.PreventDefault(); e.StopPropagation();
            });
            RegisterCallback<NavigationSubmitEvent>(e => { _active.Submit(); e.PreventDefault(); e.StopPropagation(); });
            RegisterCallback<NavigationCancelEvent>(e => { Close(); e.PreventDefault(); e.StopPropagation(); });
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Home) _active.MoveToEdge(1);
                else if (e.keyCode == KeyCode.End) _active.MoveToEdge(-1);
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

        /// <summary>Groups items by the slash-separated categories in their text, keeping author order.</summary>
        static List<Node> BuildTree(Item[] items)
        {
            var roots = new List<Node>();
            var categories = new Dictionary<string, Node>();
            foreach (var item in items)
            {
                if (item.Separator) { roots.Add(new Node { Separator = true }); continue; }
                var segments = (item.Text ?? "").Split('/');
                // A stray slash would otherwise produce a nameless category nobody can read or click.
                if (segments.Length == 1 || segments.Any(string.IsNullOrEmpty))
                { roots.Add(new Node { Text = item.Text, Path = item.Text, Item = item }); continue; }
                var level = roots;
                string path = null;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    path = path == null ? segments[i] : path + "/" + segments[i];
                    Node category;
                    if (!categories.TryGetValue(path, out category))
                    {
                        category = new Node { Text = segments[i], Path = path, Children = new List<Node>() };
                        categories.Add(path, category);
                        level.Add(category);
                    }
                    level = category.Children;
                }
                level.Add(new Node { Text = segments[segments.Length - 1], Path = item.Text, Item = item });
            }
            MarkChecked(roots);
            return roots;
        }

        static bool MarkChecked(List<Node> nodes)
        {
            bool any = false;
            foreach (var node in nodes)
            {
                if (node.IsCategory) node.HoldsChecked = MarkChecked(node.Children);
                any |= node.HoldsChecked || (node.Item != null && node.Item.Checked);
            }
            return any;
        }

        void OnPanelGeometry(GeometryChangedEvent e) { if (e.oldRect.size != e.newRect.size) Close(); }

        void Ascend()
        {
            if (_active.Parent == null) return;
            _active.Parent.CloseChild();
        }

        void Commit(Action action)
        {
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

        /// <summary>One open card. A category row opens the next card beside it, as a nested menu does.</summary>
        sealed class Level
        {
            internal readonly Level Parent;
            internal float DesiredHeight, Height;
            readonly RetainedMenu _menu;
            readonly List<Node> _nodes;
            readonly List<VisualElement> _rows = new List<VisualElement>();
            readonly ScrollView _scroll;
            readonly float _width;
            Level _child;
            int _open = -1, _selected = -1;

            internal Level(RetainedMenu menu, Level parent, List<Node> nodes, float width)
            {
                _menu = menu; Parent = parent; _nodes = nodes; _width = width;
                // Include padding, borders and a little pixel-rounding room. A fixed height
                // cap or missing border space gives even short menus a needless scrollbar.
                DesiredHeight = nodes.Sum(n => n.Separator ? SeparatorHeight : RowHeight) + CardPadding;
                Height = Mathf.Min(DesiredHeight, Mathf.Max(0, menu._panel.layout.height - 2 * Margin));
                _scroll = new ScrollView();
                _scroll.AddToClassList("thry-menu-card");
                _scroll.style.position = Position.Absolute;
                _scroll.style.width = width;
                _scroll.style.height = Height;
                _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _scroll.contentContainer.focusable = true;
                menu.Add(_scroll);

                for (int i = 0; i < nodes.Count; i++)
                {
                    int index = i;
                    var node = nodes[i];
                    var row = new VisualElement();
                    row.AddToClassList(node.Separator ? "thry-menu-separator" : "thry-menu-item");
                    if (!node.Separator)
                    {
                        bool ticked = node.IsCategory ? node.HoldsChecked : node.Item.Checked;
                        var check = new Label(ticked ? "✓" : "");
                        check.AddToClassList("thry-menu-check");
                        row.Add(check);
                        var label = new Label(node.Text) { tooltip = node.Path };
                        label.AddToClassList("thry-menu-label");
                        row.Add(label);
                        if (node.IsCategory)
                        {
                            var arrow = new Label("▸");
                            arrow.AddToClassList("thry-menu-arrow");
                            row.Add(arrow);
                        }
                        row.EnableInClassList("thry-menu-disabled", !node.IsSelectable);
                        row.RegisterCallback<PointerMoveEvent>(e => Select(index, true));
                        row.RegisterCallback<PointerUpEvent>(e =>
                        {
                            if (e.button != 0 || !node.IsSelectable) return;
                            e.StopPropagation();
                            Select(index, true);
                            if (!node.IsCategory) _menu.Commit(node.Item.Action);
                        });
                    }
                    _rows.Add(row);
                    _scroll.Add(row);
                }
            }

            internal void Focus() { _scroll.contentContainer.Focus(); }

            internal void Place(float left, float top)
            {
                var panel = _menu._panel.layout;
                _scroll.style.left = Mathf.Clamp(left, Margin, Mathf.Max(Margin, panel.width - _width - Margin));
                _scroll.style.top = Mathf.Clamp(top, Margin, Mathf.Max(Margin, panel.height - Height - Margin));
            }

            /// <summary>
            /// Opens beside the row that owns this card. An inspector too narrow for two full cards
            /// slides the new one over the old, but never past the edge that shows the way back.
            /// </summary>
            void PlaceBeside(VisualElement row)
            {
                var panel = _menu._panel.layout;
                // A one pixel overlap keeps the pointer inside a menu the whole way across the seam.
                float beside = _scroll.layout.x + _width - 1;
                float left = Mathf.Min(beside, panel.width - _child._width - Margin);
                _child.Place(left, _menu.WorldToLocal(row.worldBound.position).y - Margin);
            }

            internal void Select(int index, bool openCategory)
            {
                var node = _nodes[index];
                if (!node.IsSelectable) return;
                // Pointer movement reports every pixel. Re-scrolling to a row the pointer already
                // rests on would fight the user's own wheel.
                if (_selected == index && (!node.IsCategory || _open == index)) return;
                if (_selected >= 0 && _selected < _rows.Count) _rows[_selected].RemoveFromClassList("thry-menu-selected");
                _selected = index;
                _rows[index].AddToClassList("thry-menu-selected");
                _scroll.ScrollTo(_rows[index]);
                if (node.IsCategory && openCategory) OpenChild(index);
                else CloseChild();
            }

            internal void Move(int direction)
            {
                int index = _selected;
                if (index < 0 && direction < 0) index = _nodes.Count;
                do { index += direction; }
                while (index >= 0 && index < _nodes.Count && !_nodes[index].IsSelectable);
                if (index >= 0 && index < _nodes.Count) Select(index, false);
            }

            internal void MoveToEdge(int direction)
            {
                if (_selected >= 0 && _selected < _rows.Count) _rows[_selected].RemoveFromClassList("thry-menu-selected");
                _selected = direction > 0 ? -1 : _nodes.Count;
                Move(direction);
            }

            internal void Descend()
            {
                if (_selected < 0 || !_nodes[_selected].IsCategory) return;
                OpenChild(_selected);
                // Hovering already opened this card, and stepping into it should land on its first
                // row rather than skip past wherever the pointer happened to leave the highlight.
                if (_child._selected < 0) _child.Move(1);
            }

            internal void Submit()
            {
                if (_selected < 0 || !_nodes[_selected].IsSelectable) return;
                if (_nodes[_selected].IsCategory) Descend();
                else _menu.Commit(_nodes[_selected].Item.Action);
            }

            void OpenChild(int index)
            {
                if (_open == index && _child != null) return;
                CloseChild();
                _open = index;
                _child = new Level(_menu, this, _nodes[index].Children,
                    Mathf.Min(MinWidth, _menu._panel.layout.width - 2 * Margin));
                PlaceBeside(_rows[index]);
                _menu._active = _child;
            }

            internal void CloseChild()
            {
                if (_child == null) return;
                _child.CloseChild();
                _child._scroll.RemoveFromHierarchy();
                _child = null; _open = -1;
                _menu._active = this;
            }
        }
    }
}
#endif
