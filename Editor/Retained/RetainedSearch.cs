#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>Filtered native controls sharing the complete inspector's material bindings.</summary>
    internal sealed class RetainedSearch : VisualElement
    {
        sealed class Match
        {
            internal ShaderProperty Property;
            internal ShaderPart Owner;
            internal string Path;
            internal string Type, Haystack, RootCaption;
        }
        sealed class PendingField
        {
            internal VisualElement Element;
            internal Action Build;
        }
        readonly MaterialInspectorView _view;
        readonly Label _summary;
        readonly Button _clearButton;
        string T(string key, string fallback) => _view.Text(key, fallback);
        readonly VisualElement _results;
        RetainedMaterialModel _model;
        RetainedFields _fields;
        string _signature, _query;
        int _revision = -1;
        ShaderEditor _metadataShader;
        int _metadataRevision = -1, _stateStamp;
        List<Match> _metadata;
        RetainedMaterialModel _observedModel;
        bool _stateInvalid = true;
        readonly List<PendingField> _pendingBuild = new List<PendingField>();
        readonly List<VisualElement> _rows = new List<VisualElement>();
        readonly IVisualElementScheduledItem _buildSchedule;
        VisualElement _firstResult;
        ScrollView _anchorScroll;
        VisualElement _anchorRow;
        float _anchorY, _anchorOffset, _anchorBottomGap;
        bool _anchorPending, _anchorBottom;
        internal int MetadataBuildCount { get; private set; }
        internal int MatchEvaluationCount { get; private set; }
        internal int ResultBuildCount { get; private set; }
        internal int PendingFieldCount => _pendingBuild.Count;
        internal int MatchCount { get; private set; }
        internal int SynchronizedFieldCount => _fields?.LastSynchronizeCount ?? 0;

        internal RetainedSearch(MaterialInspectorView view, Action clear)
        {
            _view = view;
            name = "thry-property-search-results";
            AddToClassList("thry-property-search-results");
            var toolbar = new VisualElement(); toolbar.AddToClassList("thry-filter-toolbar"); Add(toolbar);
            _summary = new Label { name = "thry-filter-summary" }; _summary.AddToClassList("thry-search-summary"); toolbar.Add(_summary);
            var clearButton = new Button(clear) { name = "thry-clear-filter", text = "Clear filters", tooltip = "Clear the text, type, and changed filters to show the full inspector again." };
            _clearButton = clearButton;
            clearButton.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefault(); e.StopImmediatePropagation(); clear(); }, TrickleDown.TrickleDown);
            toolbar.Add(clearButton);
            _results = new VisualElement(); _results.AddToClassList("thry-search-matches"); _results.AddToClassList("thry-material-controls"); Add(_results);
            _results.RegisterCallback<GeometryChangedEvent>(e => {
                AlignColumns();
                if (_anchorPending) schedule.Execute(RestoreScrollAnchor).StartingIn(1);
            });
            _buildSchedule = schedule.Execute(BuildNext).Every(80); _buildSchedule.Pause();
            RegisterCallback<DetachFromPanelEvent>(e => { Observe(null); _buildSchedule.Pause(); });
            style.display = DisplayStyle.None;
        }
        internal void PauseBuild() { _buildSchedule.Pause(); _anchorPending = false; }
        internal void Invalidate() { _stateInvalid = true; }
        internal void RefreshLanguage()
        {
            _clearButton.text = T("clear_filters", "Clear filters");
            _clearButton.tooltip = T("clear_filters_help", "Clear all filters to show the full inspector again.");
            _signature = null; _stateInvalid = true;
        }
        void BuildNext()
        {
            if (panel == null) return;
            if (_anchorPending && (_anchorScroll?.panel == null || Mathf.Abs(_anchorScroll.scrollOffset.y - _anchorOffset) > 1))
                _anchorPending = false;
            // Hydrate only controls near the visible Inspector viewport. Retain
            // built fields so scrolling never discards an active edit or preview.
            Rect viewport = panel.visualTree.worldBound;
            ScrollView inspectorScroll = null;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                var scroll = ancestor as ScrollView;
                if (inspectorScroll == null && scroll != null) inspectorScroll = scroll;
                Rect bounds = scroll != null ? scroll.contentViewport.worldBound : ancestor.worldBound;
                if (scroll == null) continue;
                viewport = Rect.MinMaxRect(Mathf.Max(viewport.xMin, bounds.xMin), Mathf.Max(viewport.yMin, bounds.yMin),
                    Mathf.Min(viewport.xMax, bounds.xMax), Mathf.Min(viewport.yMax, bounds.yMax));
            }
            Rect visibleViewport = viewport;
            viewport.yMin -= 200; viewport.yMax += 200;
            double start = EditorApplication.timeSinceStartup;
            int count = 0;
            for (int pass = 0; pass < 2 && count < 8; pass++)
            {
                for (int i = 0; i < _pendingBuild.Count && count < 8; i++)
                {
                    var pending = _pendingBuild[i];
                    Rect bounds = pending.Element.worldBound;
                    if (pending.Element.layout.width <= 0 || !(pass == 0 ? visibleViewport : viewport).Overlaps(bounds)) continue;
                    if (count == 0 && !_anchorPending) CaptureScrollAnchor(inspectorScroll, visibleViewport);
                    _pendingBuild.RemoveAt(i--); pending.Build(); count++;
                    if (EditorApplication.timeSinceStartup - start > .005) break;
                }
                if (EditorApplication.timeSinceStartup - start > .005) break;
            }
            if (_anchorPending) schedule.Execute(RestoreScrollAnchor).StartingIn(1);
            _buildSchedule.Every(count > 0 ? 16 : 80);
            if (_pendingBuild.Count == 0) _buildSchedule.Pause();
        }
        void CaptureScrollAnchor(ScrollView scroll, Rect viewport)
        {
            if (scroll == null) return;
            _anchorScroll = scroll; _anchorOffset = scroll.scrollOffset.y;
            _anchorBottomGap = Mathf.Max(0, scroll.verticalScroller.highValue - _anchorOffset);
            // ScrollTo(last) can leave the container's trailing padding below the
            // viewport. Detect the last result itself, rather than a pixel threshold.
            _anchorBottom = scroll.verticalScroller.highValue > 0 && (_anchorBottomGap <= 1
                || (_rows.Count > 0 && _rows[_rows.Count - 1].worldBound.yMax <= viewport.yMax + 1));
            _anchorRow = _rows.FirstOrDefault(row => row.worldBound.yMax >= viewport.yMin && row.worldBound.yMin < viewport.yMax);
            if (!_anchorBottom && _anchorRow == null) return;
            _anchorY = _anchorRow == null ? 0 : _anchorRow.worldBound.yMin;
            _anchorPending = true;
        }
        void RestoreScrollAnchor()
        {
            if (!_anchorPending) return;
            if (_anchorScroll?.panel == null || Mathf.Abs(_anchorScroll.scrollOffset.y - _anchorOffset) > 1)
            { _anchorPending = false; return; }
            // Keep the anchor through later drawer layout passes, until the user
            // scrolls. Some native drawers resize again after their first layout.
            if (_anchorBottom) _anchorScroll.verticalScroller.value = _anchorScroll.verticalScroller.highValue - _anchorBottomGap;
            else if (_anchorRow?.panel != null)
                _anchorScroll.verticalScroller.value = _anchorOffset + _anchorRow.worldBound.yMin - _anchorY;
            _anchorOffset = _anchorScroll.scrollOffset.y;
        }
        void Observe(RetainedMaterialModel model)
        {
            if (_observedModel == model) return;
            if (_observedModel != null) _observedModel.Changed -= InvalidateState;
            _observedModel = model;
            if (model != null) model.Changed += InvalidateState;
            _stateInvalid = true;
        }
        void InvalidateState() => _stateInvalid = true;
        static int StateStamp(ShaderEditor shader)
        {
            unchecked
            {
                int stamp = RetainedSearchPreferences.Revision * 31 + (shader.IsInAnimationMode ? 1 : 0);
                stamp = stamp * 31 + (shader.IsLockedMaterial ? 1 : 0);
                foreach (var material in shader.Materials)
                    if (material != null) stamp = (stamp * 31 + material.GetObjectId()) * 31 + EditorUtility.GetDirtyCount(material);
                foreach (var group in shader.RootCategories) stamp = stamp * 31 + (SectionEditing.IsEditing(group) ? 1 : 0);
                return stamp;
            }
        }
        internal static string TextQuery(string query) => string.Join(" ", (query ?? "").Split(' ').Where(token => !IsFilter(token))).Trim();
        static bool IsFilter(string token) => new[] { "is:changed", "is:animated", "is:missing", "is:assigned", "is:empty", "is:favorite" }.Contains(token, StringComparer.OrdinalIgnoreCase)
            || token.StartsWith("in:", StringComparison.OrdinalIgnoreCase) || TypeFilter(token) != null;
        static string TypeFilter(string token)
        {
            if (!token.StartsWith("t:", StringComparison.OrdinalIgnoreCase)) return null;
            switch (token.Substring(2).ToLowerInvariant())
            {
                case "texture": return "texture";
                case "color": return "color";
                case "number": return "number";
                case "vector": return "vector";
                case "toggle": return "toggle";
                default: return null;
            }
        }
        static string PropertyType(ShaderProperty property)
        {
            switch (property.MaterialProperty.type)
            {
                case MaterialProperty.PropType.Texture: return "texture";
                case MaterialProperty.PropType.Color: return "color";
                case MaterialProperty.PropType.Vector: return "vector";
                default:
                    // Match the native field renderer's toggle metadata, rather than
                    // treating every numeric value currently equal to 0 or 1 as a toggle.
                    return property.MyShader.GetPropertyAttributes(property.ShaderPropertyIndex)
                        .Select(a => new DrawerAttribute(a)).Any(a => a.Name.Contains("Toggle")) ? "toggle" : "number";
            }
        }

        internal bool FocusFirst()
        {
            var firstPending = _pendingBuild.FirstOrDefault(p => p.Element == _firstResult);
            if (firstPending != null) { _pendingBuild.Remove(firstPending); firstPending.Build(); }
            // Draggable labels and foldout headers are focusable too. Entering
            // results should land on an actual editable value.
            var input = _results.Query<VisualElement>().ToList().FirstOrDefault(e => e.focusable
                && !(e is Label) && !(e is Foldout) && e.enabledInHierarchy && IsDisplayed(e) && IsNativeField(e));
            if (input == null) return false;
            input.Focus(); input.schedule.Execute(() => { if (input.panel != null) input.Focus(); }).StartingIn(0); return true;
        }
        static bool IsNativeField(VisualElement element)
        {
            for (var type = element.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseField<>)) return true;
            return false;
        }
        string DescribeFilters(string query)
        {
            var descriptions = new List<string>();
            foreach (var token in query.Split(' '))
            {
                string type = TypeFilter(token);
                if (type != null)
                    descriptions.Add(type == "texture" ? "Textures" : type == "color" ? "Colors" : type == "number" ? "Numbers" : type == "vector" ? "Vectors" : "Toggles");
                else if (token.Equals("is:changed", StringComparison.OrdinalIgnoreCase)) descriptions.Add("Changed only");
                else if (token.Equals("is:animated", StringComparison.OrdinalIgnoreCase)) descriptions.Add("Animated only");
                else if (token.Equals("is:missing", StringComparison.OrdinalIgnoreCase)) descriptions.Add("Missing references");
                else if (token.Equals("is:assigned", StringComparison.OrdinalIgnoreCase)) descriptions.Add("Assigned textures");
                else if (token.Equals("is:empty", StringComparison.OrdinalIgnoreCase)) descriptions.Add("Empty textures");
                else if (token.Equals("is:favorite", StringComparison.OrdinalIgnoreCase)) descriptions.Add("Favorites");
                else if (token.StartsWith("in:", StringComparison.OrdinalIgnoreCase))
                {
                    var section = _model?.Shader.ShaderParts.FirstOrDefault(p => p.MaterialProperty?.name == token.Substring(3));
                    descriptions.Add(section == null ? token.Substring(3) : Clean(section.Content.text));
                }
            }
            return string.Join(" · ", descriptions.Distinct().Select(s => T(s.ToLowerInvariant().Replace(' ', '_'), s)));
        }
        static bool IsDisplayed(VisualElement element)
        {
            for (var ancestor = element; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        internal void Refresh(RetainedMaterialModel model, string query)
        {
            query = query ?? "";
            if (model == null || string.IsNullOrWhiteSpace(query))
            {
                _buildSchedule.Pause(); _pendingBuild.Clear(); _rows.Clear(); _firstResult = null; _anchorPending = false;
                style.display = DisplayStyle.None;
                if (_signature != null || _model != null) _results.Clear();
                _signature = null; _query = null; _model = null; _fields = null; _revision = -1; MatchCount = 0;
                Observe(null);
                if (model == null) { _metadata = null; _metadataShader = null; _metadataRevision = -1; }
                return;
            }
            model.Shader.ActivateRetained();
            bool modelChanged = _model != model || _revision != model.Shader.RetainedRevision;
            bool queryChanged = _query != query;
            Observe(model);
            // Refresh values without replacing active fields or resetting slider drags.
            if (!modelChanged && _fields != null)
            {
                Rect viewport = VisibleViewport();
                var focused = panel?.focusController?.focusedElement as VisualElement;
                _fields.Synchronize(element => element.panel != null && (viewport.Overlaps(element.worldBound)
                    || element == focused || (focused != null && element.Contains(focused))));
            }
            style.display = DisplayStyle.Flex;
            int stamp = StateStamp(model.Shader);
            if (!modelChanged && !queryChanged && !_stateInvalid && stamp == _stateStamp && !model.Shader.IsInAnimationMode)
            { if (_pendingBuild.Count > 0) _buildSchedule.Resume(); return; }
            EnsureMetadata(model.Shader);
            var matches = FindMatches(model.Shader, query);
            string signature = query + "|" + string.Join("|", matches.Select(m => m.Property.MaterialProperty.name + ":" + m.Path));
            if (!modelChanged && _signature == signature)
            { _stateStamp = stamp; _stateInvalid = false; if (_pendingBuild.Count > 0) _buildSchedule.Resume(); return; }
            if (!modelChanged && !queryChanged && IsEditingResult()) return;
            _stateStamp = stamp; _stateInvalid = false; ResultBuildCount++;
            _buildSchedule.Pause(); _pendingBuild.Clear(); _rows.Clear(); _firstResult = null; _anchorPending = false;
            _results.Clear();
            _model = model; _revision = model.Shader.RetainedRevision; _query = query; _signature = signature;
            _fields = new RetainedFields(model, _view);
            MatchCount = matches.Count;
            string filters = DescribeFilters(query);
            _summary.text = string.Format(MatchCount == 1 ? T("property_count_one", "{0} property") : T("property_count", "{0} properties"), MatchCount) + (filters.Length == 0 ? "" : " · " + filters);
            _summary.tooltip = T("matching_properties", "Matching properties") + (TextQuery(query).Length == 0 ? "" : " — “" + TextQuery(query) + "”")
                + (filters.Length == 0 ? "" : "\n" + filters);
            if (matches.Count == 0)
            {
                string text = TextQuery(query);
                string message = text.Length == 0 ? T("no_filter_matches", "No properties match these filters.") : string.Format(T("no_query_matches", "No properties match “{0}”."), text);
                message += " " + (filters.Length > 0 ? T("search_reduce_filters", "Turn off a filter or clear filters to see more properties.") : T("search_examples", "Try a property or section name, such as Normal Map or Emission."));
                if (query.Split(' ').Any(t => t.Equals("is:missing", StringComparison.OrdinalIgnoreCase)))
                    message += " " + T("missing_texture_help", "Empty texture slots use shader defaults and are not missing references.");
                var empty = new Label(message) { name = "thry-filter-empty" };
                empty.AddToClassList("thry-search-hint"); _results.Add(empty); return;
            }
            foreach (var group in matches.GroupBy(m => m.Path))
            {
                var section = new VisualElement(); section.AddToClassList("thry-filter-group"); _results.Add(section);
                var breadcrumb = new Label(string.IsNullOrEmpty(group.Key) ? T("general", "General") : group.Key) { tooltip = group.Key };
                breadcrumb.AddToClassList("thry-filter-breadcrumb"); section.Add(breadcrumb);
                foreach (var match in group)
                {
                    var wrapper = new VisualElement { name = "filtered-property-" + match.Property.MaterialProperty.name };
                    wrapper.AddToClassList("thry-filter-control"); section.Add(wrapper);
                    _rows.Add(wrapper);
                    if (_firstResult == null) _firstResult = wrapper;
                    wrapper.style.minHeight = 22;
                    wrapper.focusable = true; wrapper.tabIndex = 0;
                    wrapper.RegisterCallback<FocusInEvent>(e => {
                        if (e.target != wrapper) return;
                        var pending = _pendingBuild.FirstOrDefault(p => p.Element == wrapper);
                        if (pending == null) return;
                        bool backwards = e.relatedTarget is VisualElement previous && previous.worldBound.yMin > wrapper.worldBound.yMin;
                        _pendingBuild.Remove(pending); pending.Build();
                        var inputs = wrapper.Query<VisualElement>().ToList().Where(element => element.focusable
                            && !(element is Label) && !(element is Foldout) && element.enabledInHierarchy && IsNativeField(element)).ToList();
                        var input = backwards ? inputs.LastOrDefault() : inputs.FirstOrDefault();
                        input?.Focus();
                    });
                    _pendingBuild.Add(new PendingField { Element = wrapper, Build = () => {
                        wrapper.Add(_fields.Field(match.Property));
                        InstallHighlights(wrapper, TextQuery(query));
                        wrapper.focusable = false;
                        wrapper.style.minHeight = StyleKeyword.Null;
                        _fields.Track(wrapper, () => {
                            wrapper.style.display = IsVisible(match.Property, match.Owner) ? DisplayStyle.Flex : DisplayStyle.None;
                            wrapper.SetEnabled(AncestorsEnabled(match.Property, match.Owner));
                        });
                    }});
                }
            }
            BuildNext();
            if (_pendingBuild.Count > 0) _buildSchedule.Resume();
            _results.schedule.Execute(AlignColumns).StartingIn(0);
        }
        bool IsEditingResult()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && _results.Contains(focused)) return true;
            // Dropdown focus temporarily lives in a sibling overlay.
            return panel?.visualTree.Q("thry-dropdown-menu") != null;
        }
        Rect VisibleViewport()
        {
            Rect bounds = panel == null ? Rect.zero : panel.visualTree.worldBound;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is ScrollView scroll)
                {
                    Rect visible = scroll.contentViewport.worldBound;
                    bounds = Rect.MinMaxRect(Mathf.Max(bounds.xMin, visible.xMin), Mathf.Max(bounds.yMin, visible.yMin),
                        Mathf.Min(bounds.xMax, visible.xMax), Mathf.Min(bounds.yMax, visible.yMax));
                }
            bounds.yMin -= 100; bounds.yMax += 100; return bounds;
        }
        void InstallHighlights(VisualElement wrapper, string text)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return;
            string pattern = string.Join("|", words.OrderByDescending(w => w.Length).Select(System.Text.RegularExpressions.Regex.Escape));
            foreach (var label in wrapper.Query<Label>(className: "thry-property-label").ToList())
            {
                string last = null;
                _fields.Track(label, () => {
                    if (label.text == last) return;
                    string color = EditorGUIUtility.isProSkin ? "#b4d9ea" : "#215f7d";
                    last = System.Text.RegularExpressions.Regex.Replace(label.text ?? "", pattern,
                        m => "<color=" + color + ">" + m.Value + "</color>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    label.enableRichText = true; label.text = last;
                });
            }
        }
        void AlignColumns()
        {
            if (_results.panel == null || _results.contentRect.width <= 0) return;
            float column = _results.worldBound.xMin + Mathf.Min(240, _results.contentRect.width * .38f);
            foreach (var row in _results.Query<VisualElement>(className: "thry-property-row").ToList())
            {
                var caption = row.Q(className: "thry-property-label"); if (caption == null) continue;
                float width = Mathf.Max(40, column - row.worldBound.xMin);
                if (Mathf.Abs(caption.resolvedStyle.width - width) > .5f) caption.style.width = width;
            }
        }
        void EnsureMetadata(ShaderEditor shader)
        {
            if (_metadataShader == shader && _metadataRevision == shader.RetainedRevision) return;
            _metadataShader = shader; _metadataRevision = shader.RetainedRevision;
            _metadata = new List<Match>(); MetadataBuildCount++;
            var owners = new Dictionary<string, ShaderPart>();
            foreach (var part in shader.ShaderParts)
            {
                if (!string.IsNullOrEmpty(part.Options.reference_property)) owners[part.Options.reference_property] = part;
                if (part.Options.reference_properties != null) foreach (var reference in part.Options.reference_properties) owners[reference] = part;
            }
            var roots = new HashSet<ShaderGroup>(shader.RootCategories);
            foreach (var property in shader.PropertyDictionary.Values.OrderBy(p => p.ShaderPropertyIndex))
            {
                if (!RetainedFields.CanRenderProperty(property)) continue;
                ShaderPart owner; owners.TryGetValue(property.MaterialProperty.name, out owner);
                if (property.IsHidden && owner == null) continue;
                var ancestry = Ancestors(owner ?? property).Where(p => p.MaterialProperty != null).Reverse().ToList();
                if (owner is ShaderTextureProperty || owner is ShaderGroup) ancestry.Add(owner);
                string path = string.Join(" › ", ancestry.Select(p => Clean(p.Content.text)).Where(label => label.Length > 0));
                // Root category words should not make "normal" match all Color & Normals properties.
                var context = ancestry.Where(p => !(p is ShaderGroup && roots.Contains((ShaderGroup)p)));
                string haystack = Clean(property.Content.text) + " " + property.MaterialProperty.name + " " + string.Join(" ", context.Select(p => Clean(p.Content.text)));
                string rootCaption = ancestry.Where(p => p is ShaderGroup && roots.Contains((ShaderGroup)p)).Select(p => Clean(p.Content.text)).FirstOrDefault() ?? "";
                _metadata.Add(new Match { Property = property, Owner = owner, Path = path, Type = PropertyType(property), Haystack = haystack, RootCaption = rootCaption });
            }
        }
        List<Match> FindMatches(ShaderEditor shader, string query)
        {
            MatchEvaluationCount++;
            var tokens = query.Split(' ');
            bool changed = tokens.Any(t => t.Equals("is:changed", StringComparison.OrdinalIgnoreCase));
            bool animated = tokens.Any(t => t.Equals("is:animated", StringComparison.OrdinalIgnoreCase));
            bool missing = tokens.Any(t => t.Equals("is:missing", StringComparison.OrdinalIgnoreCase));
            bool assigned = tokens.Any(t => t.Equals("is:assigned", StringComparison.OrdinalIgnoreCase));
            bool empty = tokens.Any(t => t.Equals("is:empty", StringComparison.OrdinalIgnoreCase));
            bool favorite = tokens.Any(t => t.Equals("is:favorite", StringComparison.OrdinalIgnoreCase));
            var scopes = tokens.Where(t => t.StartsWith("in:", StringComparison.OrdinalIgnoreCase)).Select(t => t.Substring(3)).ToArray();
            var types = new HashSet<string>(tokens.Select(TypeFilter).Where(t => t != null));
            string text = TextQuery(query);
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var missingNames = missing ? MissingTextureReferences(shader.Materials) : null;
            var matches = new List<Match>();
            foreach (var match in _metadata)
            {
                var property = match.Property;
                if (types.Count > 0 && !types.Contains(match.Type)) continue;
                if (scopes.Length > 0 && !Ancestors(match.Owner ?? property).Concat(new[] { match.Owner ?? property })
                    .Any(p => p.MaterialProperty != null && scopes.Contains(p.MaterialProperty.name, StringComparer.OrdinalIgnoreCase))) continue;
                if (favorite && !RetainedSearchPreferences.IsFavorite(property)) continue;
                if ((assigned || empty) && match.Type != "texture") continue;
                if (assigned && !shader.Materials.Any(m => m.HasProperty(property.MaterialProperty.name) && m.GetTexture(property.MaterialProperty.name) != null)) continue;
                if (empty && !shader.Materials.Any(m => m.HasProperty(property.MaterialProperty.name) && m.GetTexture(property.MaterialProperty.name) == null)) continue;
                string haystack = match.RootCaption.Equals(text, StringComparison.OrdinalIgnoreCase) ? match.Haystack + " " + match.RootCaption : match.Haystack;
                if (words.Any(word => haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (!IsVisible(property, match.Owner)) continue;
                if (changed && !HasChanged(property, shader)) continue;
                if (animated && !property.IsAnimated) continue;
                if (missing && !missingNames.Contains(property.MaterialProperty.name)) continue;
                matches.Add(match);
            }
            var included = new HashSet<ShaderProperty>(matches.Select(m => m.Property));
            return matches.Where(m => !(m.Owner is ShaderProperty && included.Contains((ShaderProperty)m.Owner)
                && (m.Owner is ShaderTextureProperty || m.Owner.Options.reference_property == m.Property.MaterialProperty.name))).ToList();
        }
        static IEnumerable<ShaderPart> Ancestors(ShaderPart property)
        {
            for (var parent = property.Parent; parent != null; parent = parent.Parent) yield return parent;
        }
        static bool IsVisible(ShaderProperty property, ShaderPart owner)
        {
            if (!property.RetainedVisible) return false;
            for (var part = owner ?? property.Parent; part != null; part = part.Parent)
                if (SectionEditing.IsHidden(part) || (!SectionEditing.IsEditing(part) && !part.Options.condition_show.Test())) return false;
            return true;
        }
        static bool AncestorsEnabled(ShaderProperty property, ShaderPart owner)
        {
            for (var part = owner ?? property.Parent; part != null; part = part.Parent)
            {
                // A group's Enable reference sits outside the child block it enables.
                if (part == owner && owner is ShaderGroup) continue;
                var group = part as ShaderGroup;
                if (group != null && !group.RetainedChildrenEnabled) return false;
                if (part.Options.condition_enable != null && !part.Options.condition_enable.Test()) return false;
            }
            return true;
        }
        static string Clean(string text) => (text ?? "").Trim().TrimEnd('*').Split('|')[0];
        static bool HasChanged(ShaderProperty property, ShaderEditor shader, HashSet<string> visited = null)
        {
            visited = visited ?? new HashSet<string>();
            if (!visited.Add(property.MaterialProperty.name)) return false;
            var value = property.MaterialProperty;
            if (value.hasMixedValue) return true;
            bool changed;
            switch (value.type)
            {
                case MaterialProperty.PropType.Texture:
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(property.MyShader)) as ShaderImporter;
                    var defaultTexture = importer == null ? null : importer.GetDefaultTexture(value.name);
                    changed = (value.textureValue != null && value.textureValue != defaultTexture)
                        || value.textureScaleAndOffset != new Vector4(1, 1, 0, 0);
                    break;
                case MaterialProperty.PropType.Color:
                    changed = (Vector4)value.colorValue != (Vector4)property.PropertyDefaultValue; break;
                case MaterialProperty.PropType.Vector:
                    changed = value.vectorValue != (Vector4)property.PropertyDefaultValue; break;
#if UNITY_2022_1_OR_NEWER
                case MaterialProperty.PropType.Int:
                    changed = value.intValue != Convert.ToInt32(property.PropertyDefaultValue); break;
#endif
                default:
                    changed = value.floatValue != Convert.ToSingle(property.PropertyDefaultValue); break;
            }
            if (changed) return true;
            if (property.AdditionalDefaultCheckProperties == null) return false;
            foreach (string name in property.AdditionalDefaultCheckProperties)
            {
                ShaderProperty additional;
                if (shader.PropertyDictionary.TryGetValue(name, out additional) && HasChanged(additional, shader, visited)) return true;
            }
            return false;
        }
        // An unassigned texture uses the shader's default and is not a broken reference.
        // Only serialized references which Unity can no longer resolve qualify as missing.
        static HashSet<string> MissingTextureReferences(Material[] materials)
        {
            var result = new HashSet<string>();
            foreach (var material in materials)
            {
                using (var serialized = new SerializedObject(material))
                {
                    var textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                    if (textures == null) continue;
                    for (int i = 0; i < textures.arraySize; i++)
                    {
                        var entry = textures.GetArrayElementAtIndex(i);
                        var texture = entry.FindPropertyRelative("second.m_Texture");
                        if (texture != null && texture.objectReferenceValue == null
#if UNITY_6000_5_OR_NEWER
                            && texture.objectReferenceEntityIdValue != EntityId.None)
#else
                            && texture.objectReferenceInstanceIDValue != 0)
#endif
                            result.Add(entry.FindPropertyRelative("first").stringValue);
                    }
                }
            }
            return result;
        }
    }
}
#endif
