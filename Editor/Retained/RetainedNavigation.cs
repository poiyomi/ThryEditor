using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // Shows one property or section in the unfiltered inspector. Fold states change for the view only;
    // nothing is written to the material.
    internal sealed class RetainedNavigation
    {
        const string TargetClass = "thry-navigate-target", FlashClass = "thry-navigate-flash";
        const int Attempts = 40, SettledTicks = 5;
        readonly IVisualElementScheduledItem _step, _hold, _fade;
        readonly List<ShaderGroup> _open = new List<ShaderGroup>();
        readonly List<VisualElement> _hidden = new List<VisualElement>();
        RetainedMaterialBody _body;
        ShaderPart _anchor;
        string _name, _sub;
        int _revision, _attempts, _settled;
        VisualElement _target, _flash;
        ScrollView _scroll;
        float _offset;
        bool _flashed;

        internal RetainedNavigation(VisualElement view)
        {
            _step = view.schedule.Execute(Step).Every(16); _step.Pause();
            _hold = view.schedule.Execute(() => _flash?.RemoveFromClassList(FlashClass)); _hold.Pause();
            _fade = view.schedule.Execute(Unflash); _fade.Pause();
        }

        internal static ShaderPart Find(ShaderEditor shader, string name)
        {
            if (string.IsNullOrEmpty(name) || shader?.PropertyDictionary == null) return null;
            if (shader.PropertyDictionary.TryGetValue(name, out var property)) return property;
            return shader.ShaderParts.FirstOrDefault(part => part.MaterialProperty?.name == name);
        }

        // False when the part has no place in this inspector. The caller synchronizes the body and then calls Step.
        internal bool Start(RetainedMaterialBody body, ShaderPart part)
        {
            var shader = body.Model.Shader;
            shader.ActivateRetained();
            if (part.MyShaderUI != shader) part = Find(shader, part.MaterialProperty?.name);
            if (part == null || !Route(shader, part)) return false;
            _body = body; _target = null; _scroll = null;
            _attempts = 0; _settled = 0; _flashed = false;
            _step.Resume();
            return true;
        }

        static bool Placed(ShaderPart part) => part.MaterialProperty != null && part.Parent is ShaderGroup group && group.Children.Contains(part);

        static bool References(ShaderPart part, string name) => part.Options.reference_property == name
            || (part.Options.reference_properties != null && Array.IndexOf(part.Options.reference_properties, name) >= 0);

        // A property without a row of its own is drawn by the part that references it.
        static ShaderPart Owner(ShaderEditor shader, ShaderPart part)
        {
            string name = part.MaterialProperty?.name;
            if (name == null) return null;
            ShaderPart partial = null;
            foreach (var candidate in shader.ShaderParts)
            {
                if (candidate == part || candidate.IsHidden || candidate.MaterialProperty == null || !References(candidate, name)) continue;
                if (part.MaterialProperty.targets.All(target => candidate.MaterialProperty.targets.Contains(target))) return candidate;
                if (partial == null) partial = candidate;
            }
            return partial;
        }

        bool Route(ShaderEditor shader, ShaderPart part)
        {
            var anchor = part;
            string sub = null;
            for (int hop = 0; hop < 3 && !Placed(anchor); hop++)
            {
                var owner = anchor.Parent ?? Owner(shader, anchor);
                if (owner == null) return false;
                sub = hop == 0 && !(owner is ShaderGroup) && anchor.RetainedVisible ? anchor.MaterialProperty?.name : null;
                anchor = owner;
            }
            if (!Placed(anchor)) return false;
            var chain = new List<ShaderGroup>();
            for (var group = anchor.Parent as ShaderGroup; group != null && group.MaterialProperty != null; group = group.Parent as ShaderGroup)
                chain.Insert(0, group);
            int shown = 0;
            while (shown < chain.Count && chain[shown].RetainedVisible) shown++;
            if (shown < chain.Count || !anchor.RetainedVisible)
            {
                // Hidden by a condition or by section editing: the deepest section that is shown stands in for it.
                if (shown == 0) return false;
                anchor = chain[shown - 1]; sub = null;
                chain.RemoveRange(shown - 1, chain.Count - shown + 1);
            }
            var root = chain.Count > 0 ? chain[0] : anchor as ShaderGroup;
            if (shader.FocusedCategory != null && root != null && root.MaterialProperty.name != shader.FocusedCategory)
                shader.FocusCategory(root.MaterialProperty.name);
            foreach (var group in chain) group.ExpandForNavigation(true);
            if (sub != null && anchor is ShaderTextureProperty texture && texture.Options.reference_properties != null
                && Array.IndexOf(texture.Options.reference_properties, sub) >= 0) texture.showFoldoutProperties = true;
            _open.Clear(); _open.AddRange(chain);
            _anchor = anchor; _sub = sub; _name = part.MaterialProperty.name; _revision = shader.RetainedRevision;
            return true;
        }

        VisualElement Section(ShaderGroup group) => _body.Query<VisualElement>(className: "thry-section").Where(element => element.userData == group).First();

        static bool Binds(object data, string name) => data is ShaderProperty property ? property.MaterialProperty?.name == name
            : data is ShaderProperty[] properties && properties.Any(p => p?.MaterialProperty?.name == name);

        static VisualElement Bound(VisualElement scope, string name) => scope.Query<VisualElement>().Where(element => element != scope && Binds(element.userData, name)).First();

        VisualElement Locate()
        {
            var section = _anchor as ShaderGroup;
            if (section != null) return Section(section)?.Q(className: "thry-section-header");
            string name = _anchor.MaterialProperty.name;
            VisualElement scope = _open.Count > 0 ? Section(_open[_open.Count - 1]) : _body;
            if (scope == null) return null;
            var row = scope.Query<VisualElement>("property-" + name).Where(element => element.userData == _anchor).First() ?? Bound(scope, name);
            if (row == null)
            {
                // Custom section layouts leave some properties without an element of their own.
                var content = scope == _body ? null : scope.Q(className: "thry-section-content");
                return content != null && content.childCount > 0 ? scope.Q(className: "thry-section-header") : null;
            }
            if (_sub != null) row = row.Q("property-" + _sub) ?? Bound(row, _sub) ?? row;
            return row.ClassListContains("thry-property-row") ? row : row.Q(className: "thry-property-row") ?? row;
        }

        static bool Unfold(VisualElement hidden)
        {
            var owner = hidden.parent;
            if (hidden.ClassListContains("thry-section-content") && owner?.userData is ShaderGroup group)
            {
                if (group.RetainedExpanded) return false;
                group.ExpandForNavigation(true); return true;
            }
            if (hidden.ClassListContains("thry-texture-details") && owner?.userData is ShaderTextureProperty texture)
            {
                if (texture.showFoldoutProperties) return false;
                texture.showFoldoutProperties = true; return true;
            }
            if (owner is RetainedSubcategory subcategory) { subcategory.Reveal(); return true; }
            if (owner is Foldout foldout && !foldout.value) { foldout.SetValueWithoutNotify(true); return true; }
            return false;
        }

        bool Retarget(VisualElement blocked)
        {
            if (_sub != null) { _sub = null; return true; }
            for (var element = blocked.parent; element != null && element != _body; element = element.parent)
                if (element.ClassListContains("thry-section") && element.userData is ShaderGroup group) { _anchor = group; return true; }
            return false;
        }

        // The row or header to show, once nothing above it is still folded. Null while it is not built.
        VisualElement Reveal()
        {
            bool settled = false;
            for (int pass = 0; pass < 8; pass++)
            {
                var target = Locate();
                if (target == null) return null;
                _hidden.Clear();
                for (var element = target; element != null && element != _body; element = element.parent)
                    if (element.style.display == DisplayStyle.None) _hidden.Add(element);
                if (_hidden.Count == 0) return target;
                bool opened = false;
                VisualElement blocked = null;
                for (int i = _hidden.Count - 1; i >= 0 && blocked == null; i--)
                    if (Unfold(_hidden[i])) opened = true; else blocked = _hidden[i];
                // A section updated before its parent in the same pass is still hidden, so one more pass decides.
                if (opened) settled = false;
                else if (!settled) settled = true;
                else if (Retarget(blocked)) settled = false;
                else return null;
                _body.Synchronize();
            }
            return null;
        }

        static Rect Visible(VisualElement element)
        {
            Rect bounds = element.panel.visualTree.worldBound;
            for (var ancestor = element.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is ScrollView scroll)
                {
                    Rect viewport = scroll.contentViewport.worldBound;
                    bounds = Rect.MinMaxRect(Mathf.Max(bounds.xMin, viewport.xMin), Mathf.Max(bounds.yMin, viewport.yMin),
                        Mathf.Min(bounds.xMax, viewport.xMax), Mathf.Min(bounds.yMax, viewport.yMax));
                }
            return bounds;
        }

        internal void Step()
        {
            if (_anchor == null) return;
            if (_body.panel == null || ++_attempts > Attempts) { Stop(); return; }
            var shader = _body.Model.Shader;
            shader.ActivateRetained();
            if (shader.RetainedRevision != _revision)
            {
                // The parts were rebuilt during the navigation.
                var part = Find(shader, _name);
                _target = null;
                if (part == null || !Route(shader, part)) { Stop(); return; }
                _body.Synchronize();
            }
            if (_target == null || _target.panel == null)
            {
                if (_attempts > 1 && _attempts < 5) _body.Synchronize();
                _target = Reveal();
                if (_target == null) return;
            }
            // New and newly shown rows have no size until the next layout pass.
            Rect bounds = _target.worldBound;
            if (!(bounds.height > 0) || float.IsNaN(bounds.y)) return;
            var scroll = _target.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) _settled = SettledTicks;
            else
            {
                var scroller = scroll.verticalScroller;
                // Scrolled by the user since the last step.
                if (scroll == _scroll && Mathf.Abs(scroller.value - _offset) > 1 && scroller.highValue >= _offset - 1) { Stop(); return; }
                Rect visible = Visible(_target);
                float value = scroller.value + bounds.yMin - (visible.yMin + visible.height / 3);
                value = Mathf.Max(scroller.lowValue, Mathf.Min(value, scroller.highValue));
                // Lazily built content can still change height, so stop only after the row has stayed in place.
                if (Mathf.Abs(value - scroller.value) > 1) { scroller.value = value; _settled = 0; }
                else _settled++;
                _scroll = scroll; _offset = scroller.value;
            }
            if (!_flashed) { _flashed = true; Flash(_target); }
            if (_settled >= SettledTicks) Stop();
        }

        void Stop()
        {
            _step.Pause();
            _open.Clear(); _hidden.Clear();
            _body = null; _anchor = null; _target = null; _scroll = null;
        }

        void Flash(VisualElement element)
        {
            if (_flash != element) Unflash();
            _flash = element;
            element.AddToClassList(TargetClass); element.AddToClassList(FlashClass);
            _hold.ExecuteLater(350); _fade.ExecuteLater(1500);
        }

        void Unflash()
        {
            _flash?.RemoveFromClassList(FlashClass);
            _flash?.RemoveFromClassList(TargetClass);
            _flash = null;
        }
    }
}
