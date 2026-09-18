#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // Only presentation bindings use this filter. Layout/visibility decisions and
    // the material model must still update when their controls are offscreen.
    internal sealed class RetainedViewport
    {
        readonly VisualElement _root;
        readonly List<ScrollView> _scrolls = new List<ScrollView>();
        readonly Dictionary<VisualElement, bool> _visibility = new Dictionary<VisualElement, bool>();
        readonly IVisualElementScheduledItem _refresh;
        Rect _bounds;
        VisualElement _focused, _captured;
        bool _ready, _bodyVisible;

        internal RetainedViewport(VisualElement root, Action refresh)
        {
            _root = root;
            _refresh = root.schedule.Execute(refresh); _refresh.Pause();
            root.RegisterCallback<AttachToPanelEvent>(e => { if (e.target == root) Observe(); });
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) { Unobserve(); _refresh.Pause(); _visibility.Clear(); } });
            root.RegisterCallback<GeometryChangedEvent>(GeometryChanged);
        }

        void Observe()
        {
            Unobserve();
            for (var ancestor = _root.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is ScrollView scroll)
                {
                    _scrolls.Add(scroll);
                    scroll.verticalScroller.valueChanged += Scrolled;
                    scroll.horizontalScroller.valueChanged += Scrolled;
                    scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(GeometryChanged);
                }
            _refresh.ExecuteLater(0);
        }

        void Unobserve()
        {
            foreach (var scroll in _scrolls)
            {
                scroll.verticalScroller.valueChanged -= Scrolled;
                scroll.horizontalScroller.valueChanged -= Scrolled;
                scroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(GeometryChanged);
            }
            _scrolls.Clear();
        }

        void GeometryChanged(GeometryChangedEvent e) => _refresh.ExecuteLater(0);
        void Scrolled(float value) => _refresh.ExecuteLater(0);

        internal void Begin()
        {
            _visibility.Clear();
            var panel = _root.panel;
            _ready = panel != null && _root.layout.width > 0 && _root.layout.height > 0;
            if (!_ready) return; // Preserve initialization before the first layout.
            _bounds = panel.visualTree.worldBound;
            foreach (var scroll in _scrolls)
            {
                Rect viewport = scroll.contentViewport.worldBound;
                _bounds = Rect.MinMaxRect(Mathf.Max(_bounds.xMin, viewport.xMin), Mathf.Max(_bounds.yMin, viewport.yMin),
                    Mathf.Min(_bounds.xMax, viewport.xMax), Mathf.Min(_bounds.yMax, viewport.yMax));
            }
            // Refresh just before controls enter the viewport, including fast wheel scrolling.
            _bounds.yMin -= 100; _bounds.yMax += 100;
            _focused = panel.focusController?.focusedElement as VisualElement;
            _captured = panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
            _bodyVisible = _bounds.Overlaps(_root.worldBound) || ContainsActive(_root);
        }

        internal static VisualElement Anchor(VisualElement element)
        {
            // Resolve once per attachment: numeric label capture belongs to the same
            // editing surface as its sibling field, including zero-sized change dots.
            for (var parent = element; parent != null; parent = parent.parent)
                if (parent.ClassListContains("thry-property-row")) return parent;
            return element;
        }

        bool ContainsActive(VisualElement element) => element == _focused || (_focused != null && element.Contains(_focused))
            || element == _captured || (_captured != null && element.Contains(_captured));

        internal bool IsOffscreen => _ready && !_bodyVisible;

        internal bool Includes(VisualElement anchor)
        {
            if (!_ready) return true;
            if (!_bodyVisible) return false;
            if (_visibility.TryGetValue(anchor, out var result)) return result;
            result = anchor.panel != null && (_bounds.Overlaps(anchor.worldBound) || ContainsActive(anchor));
            _visibility.Add(anchor, result);
            return result;
        }
    }
}
#endif
