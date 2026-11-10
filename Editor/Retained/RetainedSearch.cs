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
            internal ShaderPart Owner, Section;
            internal string Path;
            internal string Type, Caption, RootCaption;
            // Upper-case label, name and section words, then tooltip and dropdown choices from PrimaryLength on.
            internal string Text;
            internal int PrimaryLength;
        }
        const int PrimaryBand = 0, SecondaryBand = 1, HiddenBand = 2, HiddenRowLimit = 20;
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
        List<(Match Match, int Band, string Hint, ShaderProperty Controller)> _matchedMetadata;
        readonly Dictionary<ShaderPart, string> _hints = new Dictionary<ShaderPart, string>();
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
        Label _pressed;
        Vector2 _pressPosition;
        int _pressPointer;
        float _pressSlop;
        bool _pressMoved, _pressSwallowed;
        ScrollView _anchorScroll;
        VisualElement _anchorRow;
        float _anchorY, _anchorOffset, _anchorBottomGap;
        bool _anchorPending, _anchorBottom;
        bool _buildNeedsScan = true;
        Rect _idleViewport, _idleResultsBounds;
        int _idlePendingCount;
        internal int MetadataBuildCount { get; private set; }
        internal int MatchEvaluationCount { get; private set; }
        internal int ResultBuildCount { get; private set; }
        internal int HydrationScanCount { get; private set; }
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
            clearButton.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefaultHandling(); e.StopImmediatePropagation(); clear(); }, TrickleDown.TrickleDown);
            toolbar.Add(clearButton);
            _results = new VisualElement(); _results.AddToClassList("thry-search-matches"); _results.AddToClassList("thry-material-controls"); Add(_results);
            _results.RegisterCallback<GeometryChangedEvent>(e => {
                _buildNeedsScan = true;
                AlignColumns();
                if (_anchorPending) schedule.Execute(RestoreScrollAnchor).StartingIn(1);
            });
            _results.RegisterCallback<PointerDownEvent>(PressLink, TrickleDown.TrickleDown);
            _results.RegisterCallback<MouseDownEvent>(e => { if (_pressSwallowed && e.button == 0 && e.target == _pressed) e.StopPropagation(); }, TrickleDown.TrickleDown);
            _results.RegisterCallback<PointerMoveEvent>(DragLink, TrickleDown.TrickleDown);
            _results.RegisterCallback<PointerUpEvent>(ReleaseLink, TrickleDown.TrickleDown);
            _buildSchedule = schedule.Execute(BuildNext).Every(80); _buildSchedule.Pause();
            RegisterCallback<DetachFromPanelEvent>(e => { Observe(null); _buildSchedule.Pause(); });
            style.display = DisplayStyle.None;
        }
        internal void PauseBuild() { _buildSchedule.Pause(); _anchorPending = false; }
        internal void Invalidate() => InvalidateState();
        internal void RefreshLanguage()
        {
            _clearButton.text = T("clear_filters", "Clear filters");
            _clearButton.tooltip = T("clear_filters_help", "Clear all filters to show the full inspector again.");
            _signature = null; _stateInvalid = true; _hints.Clear();
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
            // The viewport and result bounds together include scrolling, resizing and
            // drawer layout changes. Once no nearby fields remain, avoid revisiting
            // thousands of offscreen placeholders on every scheduled check.
            Rect resultBounds = _results.worldBound;
            if (!_buildNeedsScan && _idlePendingCount == _pendingBuild.Count
                && _idleViewport == viewport && _idleResultsBounds == resultBounds) return;
            HydrationScanCount++;
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
            _buildNeedsScan = count > 0;
            _idleViewport = visibleViewport; _idleResultsBounds = resultBounds; _idlePendingCount = _pendingBuild.Count;
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
        void InvalidateState() { _stateInvalid = true; _buildNeedsScan = true; }
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
        static readonly string[] StatusFilters = { "is:changed", "is:animated", "is:missing", "is:assigned", "is:empty", "is:favorite" };
        static bool IsFilter(string token) => StatusFilters.Contains(token, StringComparer.OrdinalIgnoreCase)
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
            switch (property.MaterialProperty.GetPropertyType())
            {
                case UnityEngine.Rendering.ShaderPropertyType.Texture: return "texture";
                case UnityEngine.Rendering.ShaderPropertyType.Color: return "color";
                case UnityEngine.Rendering.ShaderPropertyType.Vector: return "vector";
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
                && !(e is Label) && !(e is Foldout) && e.enabledInHierarchy && IsDisplayed(e) && IsNativeField(e))
                ?? _results.Q<Button>(className: "thry-search-hidden-link");
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
                _signature = null; _matchedMetadata = null; _query = null; _model = null; _fields = null; _revision = -1; MatchCount = 0;
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
            _buildNeedsScan = true;
            EnsureMetadata(model.Shader);
            var matches = FindMatches(model.Shader, query);
            // Metadata objects are stable until a shader/locale rebuild. Compare
            // their identities instead of formatting thousands of row identifiers
            // into a temporary string whenever a material's value changes.
            if (!modelChanged && _signature == query && _matchedMetadata != null && _matchedMetadata.SequenceEqual(matches))
            { _stateStamp = stamp; _stateInvalid = false; if (_pendingBuild.Count > 0) _buildSchedule.Resume(); return; }
            if (!modelChanged && !queryChanged && IsEditingResult()) return;
            _stateStamp = stamp; _stateInvalid = false; ResultBuildCount++;
            _buildNeedsScan = true;
            _buildSchedule.Pause(); _pendingBuild.Clear(); _rows.Clear(); _firstResult = null; _anchorPending = false;
            _results.Clear();
            _model = model; _revision = model.Shader.RetainedRevision; _query = query; _signature = query; _matchedMetadata = matches;
            _fields = new RetainedFields(model, _view);
            MatchCount = matches.Count(m => m.Band != HiddenBand);
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
            string highlight = HighlightPattern(TextQuery(query));
            VisualElement section = null;
            string sectionPath = null;
            int band = PrimaryBand;
            for (int index = 0; index < matches.Count; index++)
            {
                var entry = matches[index];
                if (entry.Band != band)
                {
                    band = entry.Band; section = null;
                    var heading = new Label(band == SecondaryBand ? T("search_other_matches", "Found in tooltips and dropdown choices")
                        : string.Format(T("search_hidden_matches", "{0} hidden by other settings"), matches.Count - MatchCount));
                    heading.AddToClassList("thry-search-band"); _results.Add(heading);
                }
                if (band == HiddenBand)
                {
                    var hidden = new VisualElement(); hidden.AddToClassList("thry-search-hidden"); _results.Add(hidden);
                    var rest = matches.Skip(index).ToList();
                    foreach (var row in rest.Take(HiddenRowLimit)) hidden.Add(HiddenRow(row.Match, row.Hint, row.Controller, highlight));
                    if (rest.Count > HiddenRowLimit)
                    {
                        Button more = null;
                        more = new Button(() => {
                            more.RemoveFromHierarchy();
                            foreach (var row in rest.Skip(HiddenRowLimit)) hidden.Add(HiddenRow(row.Match, row.Hint, row.Controller, highlight));
                        }) { text = string.Format(T("show_more", "Show {0} more"), rest.Count - HiddenRowLimit) };
                        more.AddToClassList("thry-search-hidden-more"); hidden.Add(more);
                    }
                    break;
                }
                var match = entry.Match;
                if (section == null || sectionPath != match.Path)
                {
                    section = new VisualElement(); section.AddToClassList("thry-filter-group"); _results.Add(section);
                    sectionPath = match.Path;
                    var breadcrumb = new Label(string.IsNullOrEmpty(match.Path) ? T("general", "General") : match.Path) { tooltip = match.Path, userData = match.Section };
                    breadcrumb.AddToClassList("thry-filter-breadcrumb"); breadcrumb.EnableInClassList("thry-search-link", match.Section != null); section.Add(breadcrumb);
                }
                var wrapper = new VisualElement { name = "filtered-property-" + match.Property.MaterialProperty.name };
                wrapper.AddToClassList("thry-filter-control"); section.Add(wrapper);
                _rows.Add(wrapper);
                if (_firstResult == null) _firstResult = wrapper;
                wrapper.AddToClassList("thry-filter-pending");
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
                    InstallHighlights(wrapper, highlight);
                    wrapper.focusable = false;
                    wrapper.RemoveFromClassList("thry-filter-pending");
                    _fields.Track(wrapper, () => {
                        wrapper.style.display = IsVisible(match.Property, match.Owner) ? DisplayStyle.Flex : DisplayStyle.None;
                        wrapper.SetEnabled(AncestorsEnabled(match.Property, match.Owner));
                    });
                }});
            }
            BuildNext();
            if (_pendingBuild.Count > 0) _buildSchedule.Resume();
            _results.schedule.Execute(AlignColumns).StartingIn(0);
        }
        bool IsEditingResult()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && _results.Contains(focused)) return true;
            // The inspector loses focus to the color picker window, so its row is tracked separately.
            var picking = RetainedColorPicker.ActiveField;
            if (picking != null && picking.panel == panel && _results.Contains(picking)) return true;
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
        static string HighlightPattern(string text)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return null;
            // Tags match first and are kept, so words never highlight inside them.
            return "(" + RichTextTag + ")|" + string.Join("|", words.OrderByDescending(w => w.Length).Select(System.Text.RegularExpressions.Regex.Escape));
        }
        static string Highlight(string text, string pattern)
        {
            if (pattern == null) return text ?? "";
            string color = EditorGUIUtility.isProSkin ? "#b4d9ea" : "#215f7d";
            return System.Text.RegularExpressions.Regex.Replace(text ?? "", pattern,
                m => m.Groups[1].Success ? m.Value : "<color=" + color + ">" + m.Value + "</color>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        void InstallHighlights(VisualElement wrapper, string pattern)
        {
            if (pattern == null) return;
            foreach (var label in wrapper.Query<Label>(className: "thry-property-label").ToList())
            {
                string last = null;
                _fields.Track(label, () => {
                    if (label.text == last) return;
                    last = Highlight(label.text, pattern);
                    label.enableRichText = true; label.text = last;
                });
            }
        }
        VisualElement HiddenRow(Match match, string hint, ShaderProperty controller, string pattern)
        {
            var row = new VisualElement { name = "hidden-property-" + match.Property.MaterialProperty.name };
            row.AddToClassList("thry-search-hidden-row");
            var title = new VisualElement(); title.AddToClassList("thry-search-hidden-title"); row.Add(title);
            var caption = new Label(Highlight(match.Caption, pattern)) { enableRichText = true, tooltip = match.Property.TooltipText, userData = match.Property };
            caption.AddToClassList("thry-search-hidden-caption"); caption.AddToClassList("thry-search-link"); title.Add(caption);
            if (match.Path.Length > 0)
            {
                var path = new Label(match.Path) { tooltip = match.Path };
                path.AddToClassList("thry-search-hidden-path"); title.Add(path);
            }
            TextElement reason;
            if (controller == null) reason = new Label(hint);
            else
            {
                reason = new Button(() => _view.Navigate(controller)) { text = hint,
                    tooltip = T("find_controller", "Find controlling property") + ": " + Clean(RetainedText.PropertyCaption(controller)) };
                reason.AddToClassList("thry-search-hidden-link");
            }
            reason.AddToClassList("thry-search-hidden-reason"); row.Add(reason);
            _rows.Add(row);
            return row;
        }
        // A result's name, a breadcrumb and a hidden match's name go to their place in the inspector when
        // clicked. The press still reaches the name, so dragging a number's name scrubs it as usual.
        void PressLink(PointerDownEvent e)
        {
            Unpress();
            var label = e.target as Label;
            if (label == null || e.button != 0 || e.altKey || e.ctrlKey || e.shiftKey || e.commandKey) return;
            bool texture = label.ClassListContains("thry-texture-caption");
            if (!texture && !label.ClassListContains("thry-property-label") && !label.ClassListContains("thry-search-link")) return;
            bool scrubs = label.enabledInHierarchy && label.ClassListContains(BaseField<float>.labelDraggerVariantUssClassName);
            // A texture's name sits inside its fold-out button, which would toggle as well. Pressing the name of
            // a number that differs between materials writes one value to all of them before any drag.
            _pressSwallowed = texture || (scrubs && LinkTarget(label)?.MaterialProperty?.hasMixedValue == true);
            _pressed = label; _pressPosition = e.position; _pressPointer = e.pointerId; _pressMoved = false;
            // Any movement on a number's name already changes the value.
            _pressSlop = scrubs && !_pressSwallowed ? 0 : 4;
            // A captured pointer reports to its owner alone in Unity 2022.3.
            label.RegisterCallback<PointerMoveEvent>(DragLink); label.RegisterCallback<PointerUpEvent>(ReleaseLink);
            if (_pressSwallowed) e.StopPropagation();
        }
        void DragLink(PointerMoveEvent e)
        {
            if (_pressed != null && e.pointerId == _pressPointer && ((Vector2)e.position - _pressPosition).magnitude > _pressSlop) _pressMoved = true;
        }
        void ReleaseLink(PointerUpEvent e)
        {
            var label = _pressed;
            if (label == null || e.pointerId != _pressPointer || e.button != 0) return;
            bool clicked = !_pressMoved && ((Vector2)e.position - _pressPosition).magnitude <= _pressSlop && label.worldBound.Contains(e.position);
            Unpress();
            // Leaving search removes the results, so let this event finish on them first.
            if (clicked) schedule.Execute(() => Go(label));
        }
        void Unpress()
        {
            if (_pressed == null) return;
            _pressed.UnregisterCallback<PointerMoveEvent>(DragLink); _pressed.UnregisterCallback<PointerUpEvent>(ReleaseLink);
            _pressed = null; _pressSwallowed = false;
        }
        ShaderPart LinkTarget(VisualElement link)
        {
            var part = link.userData as ShaderPart;
            for (var element = link.parent; part == null && element != null && element != _results; element = element.parent)
                part = element.userData as ShaderProperty ?? (element.userData as ShaderProperty[])?.FirstOrDefault();
            return part;
        }
        internal bool Go(VisualElement link)
        {
            var part = link.panel == null ? null : LinkTarget(link);
            if (part == null) return false;
            part.MyShaderUI?.ActivateRetained();
            // Only a breadcrumb targets a section, and its matches are inside it.
            var group = part as ShaderGroup;
            bool expanded = group != null && group.RetainedExpanded;
            group?.ExpandForNavigation(true);
            if (_view.Navigate(part)) return true;
            group?.ExpandForNavigation(expanded);
            return false;
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
            _metadata = new List<Match>(); MetadataBuildCount++; _hints.Clear();
            var owners = new Dictionary<string, List<ShaderPart>>();
            Action<string, ShaderPart> addOwner = (name, owner) => {
                if (string.IsNullOrEmpty(name)) return;
                if (!owners.TryGetValue(name, out var candidates)) owners.Add(name, candidates = new List<ShaderPart>());
                candidates.Add(owner);
            };
            foreach (var part in shader.ShaderParts)
            {
                if (part.IsHidden) continue;
                addOwner(part.Options.reference_property, part);
                if (part.Options.reference_properties != null) foreach (var reference in part.Options.reference_properties) addOwner(reference, part);
            }
            var roots = new HashSet<ShaderGroup>(shader.RootCategories);
            var properties = shader.ShaderParts.OfType<ShaderProperty>().Distinct().OrderBy(p => p.ThryPropertyIndex).Where(RetainedFields.CanRenderProperty).ToList();
            var choiceLists = new Dictionary<ShaderProperty, string>();
            foreach (var property in properties)
            {
                string[] choices; float[] values;
                if (RetainedAvailability.EnumChoices(property, out choices, out values)) choiceLists[property] = string.Join(" ", choices.Select(StripTags));
            }
            // Choice lists repeated on many dropdowns, like UV, channel or blend pickers, would match almost any search.
            var sharedChoices = new HashSet<string>(choiceLists.Values.GroupBy(list => list).Where(group => group.Count() > 3).Select(group => group.Key));
            foreach (var property in properties)
            {
                ShaderPart owner = null;
                if (owners.TryGetValue(property.MaterialProperty.name, out var candidates))
                    owner = candidates.FirstOrDefault(candidate => candidate != property && candidate.MaterialProperty != null
                        && property.MaterialProperty.targets.All(target => candidate.MaterialProperty.targets.Contains(target)));
                var presentations = new List<KeyValuePair<ShaderProperty, ShaderPart>>();
                if ((property.IsHidden || property.Parent == null) && owner == null)
                {
                    if (candidates == null) continue;
                    foreach (var candidate in candidates)
                    {
                        if (candidate == property || candidate.MaterialProperty == null) continue;
                        var targets = property.MaterialProperty.targets.OfType<Material>().Where(m => m != null
                            && shader.Materials.Contains(m) && candidate.MaterialProperty.targets.Contains(m)).ToArray();
                        if (targets.Length == 0) continue;
                        presentations.Add(new KeyValuePair<ShaderProperty, ShaderPart>(property.CreateRetainedProjection(candidate, targets), candidate));
                    }
                }
                else presentations.Add(new KeyValuePair<ShaderProperty, ShaderPart>(property, owner));
                string choiceText;
                if (!choiceLists.TryGetValue(property, out choiceText) || sharedChoices.Contains(choiceText)) choiceText = "";
                string secondary = (StripTags(property.TooltipText) + " " + choiceText).Trim().ToUpperInvariant();
                foreach (var presentation in presentations)
                {
                    var displayed = presentation.Key; var displayedOwner = presentation.Value;
                    var ancestry = Ancestors(displayedOwner ?? displayed).Where(p => p.MaterialProperty != null).Reverse().ToList();
                    if (displayedOwner is ShaderTextureProperty || displayedOwner is ShaderGroup) ancestry.Add(displayedOwner);
                    string path = string.Join(" › ", ancestry.Select(p => Clean(p.Content.text)).Where(label => label.Length > 0));
                    // Root category words should not make "normal" match all Color & Normals properties.
                    var context = ancestry.Where(p => !(p is ShaderGroup && roots.Contains((ShaderGroup)p)));
                    string caption = Clean(RetainedText.PropertyCaption(displayed));
                    string primary = (caption + " " + displayed.MaterialProperty.name + " " + string.Join(" ", context.Select(p => Clean(p.Content.text)))).ToUpperInvariant();
                    string rootCaption = ancestry.Where(p => p is ShaderGroup && roots.Contains((ShaderGroup)p)).Select(p => Clean(p.Content.text)).FirstOrDefault() ?? "";
                    _metadata.Add(new Match { Property = displayed, Owner = displayedOwner, Section = ancestry.LastOrDefault(), Path = path, Type = PropertyType(displayed), Caption = caption,
                        Text = secondary.Length == 0 ? primary : primary + "\n" + secondary, PrimaryLength = primary.Length, RootCaption = rootCaption });
                }
            }
        }
        List<(Match Match, int Band, string Hint, ShaderProperty Controller)> FindMatches(ShaderEditor shader, string query)
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
            var keys = words.Select(word => word.ToUpperInvariant()).ToArray();
            string phrase = string.Join(" ", words);
            var missingNames = missing ? MissingTextureReferences(shader.Materials) : null;
            // Tier 0 is an exact label or property name, 1 any other label, name or section match,
            // 2 a word found only in the tooltip or dropdown choices.
            var visible = new List<(Match Match, int Tier)>();
            var hidden = new List<(Match Match, int Tier, ShaderPart HiddenBy)>();
            foreach (var match in _metadata)
            {
                var property = match.Property;
                if (types.Count > 0 && !types.Contains(match.Type)) continue;
                // Text/type rejection does not need native material values. Avoid
                // projection refreshes and per-row predicate closures for misses.
                bool rootMatch = match.RootCaption.Equals(text, StringComparison.OrdinalIgnoreCase);
                int tier = 1;
                for (int i = 0; i < keys.Length && tier > 0; i++)
                {
                    int index = match.Text.IndexOf(keys[i], StringComparison.Ordinal);
                    if (index >= 0 && index < match.PrimaryLength) continue;
                    if (rootMatch && match.RootCaption.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    tier = index < 0 ? -1 : 2;
                }
                if (tier < 0 || !property.RefreshRetainedProjection()) continue;
                if (tier == 1 && keys.Length > 0 && (match.Caption.Equals(phrase, StringComparison.OrdinalIgnoreCase)
                    || property.MaterialProperty.name.Equals(phrase, StringComparison.OrdinalIgnoreCase))) tier = 0;
                if (scopes.Length > 0 && !Ancestors(match.Owner ?? property).Concat(new[] { match.Owner ?? property })
                    .Any(p => p.MaterialProperty != null && scopes.Contains(p.MaterialProperty.name, StringComparer.OrdinalIgnoreCase))) continue;
                if (favorite && !RetainedSearchPreferences.IsFavorite(property)) continue;
                if ((assigned || empty) && match.Type != "texture") continue;
                if (assigned && !property.MaterialProperty.targets.OfType<Material>().Any(m => OwnsTexture(m, property) && m.GetTexture(property.MaterialProperty.name) != null)) continue;
                if (empty && !property.MaterialProperty.targets.OfType<Material>().Any(m => OwnsTexture(m, property) && m.GetTexture(property.MaterialProperty.name) == null)) continue;
                ShaderPart hiddenBy = null;
                if (!IsVisible(property, match.Owner) && (keys.Length == 0 || (hiddenBy = HiddenBy(property, match.Owner)) == null)) continue;
                if (changed && !HasChanged(property, shader)) continue;
                if (animated && !property.IsAnimated) continue;
                if (missing && !property.MaterialProperty.targets.OfType<Material>().Any(m => OwnsTexture(m, property)
                    && missingNames.TryGetValue(m, out var names) && names.Contains(property.MaterialProperty.name))) continue;
                if (hiddenBy == null) visible.Add((match, tier));
                else hidden.Add((match, tier, hiddenBy));
            }
            var included = new HashSet<ShaderProperty>(visible.Select(m => m.Match.Property));
            var includedHidden = new HashSet<ShaderProperty>(hidden.Select(m => m.Match.Property));
            Func<Match, HashSet<ShaderProperty>, bool> drawnByOwner = (m, owners) => m.Owner is ShaderProperty && owners.Contains((ShaderProperty)m.Owner)
                && (m.Owner is ShaderTextureProperty || m.Owner.Options.reference_property == m.Property.MaterialProperty.name);
            visible.RemoveAll(m => drawnByOwner(m.Match, included));
            hidden.RemoveAll(m => drawnByOwner(m.Match, includedHidden));
            var results = new List<(Match Match, int Band, string Hint, ShaderProperty Controller)>();
            // Sections holding an exact match come first, with that match at their top.
            results.AddRange(visible.Where(m => m.Tier < 2).GroupBy(m => m.Match.Path).OrderBy(g => g.Any(m => m.Tier == 0) ? 0 : 1)
                .SelectMany(g => g.OrderBy(m => m.Tier)).Select(m => (m.Match, PrimaryBand, (string)null, (ShaderProperty)null)));
            results.AddRange(visible.Where(m => m.Tier == 2).GroupBy(m => m.Match.Path).SelectMany(g => g)
                .Select(m => (m.Match, SecondaryBand, (string)null, (ShaderProperty)null)));
            var controllers = new Dictionary<ShaderPart, ShaderProperty>();
            foreach (var entry in hidden.OrderBy(m => m.Tier))
            {
                var condition = entry.HiddenBy.Options.condition_show;
                string hint;
                if (!_hints.TryGetValue(entry.HiddenBy, out hint))
                    _hints[entry.HiddenBy] = hint = string.Format(T("shown_when", "Shown when {0}"), RetainedAvailability.Condition(condition, shader));
                ShaderProperty controller;
                if (!controllers.TryGetValue(entry.HiddenBy, out controller))
                    controllers[entry.HiddenBy] = controller = RetainedAvailability.ConditionProperty(condition, entry.HiddenBy);
                results.Add((entry.Match, HiddenBand, hint, controller));
            }
            return results;
        }
        // The part whose condition hides the property, or null when it is visible, hidden by the user, or can never show.
        static ShaderPart HiddenBy(ShaderProperty property, ShaderPart owner)
        {
            ShaderPart hiddenBy = null;
            for (ShaderPart part = property; part != null; part = part == property ? owner ?? property.Parent : part.Parent)
            {
                if (SectionEditing.IsHidden(part)) return null;
                if (SectionEditing.IsEditing(part) || part.Options.condition_show.Test()) continue;
                if (part.Options.condition_show.IsConstantCondition) return null;
                if (hiddenBy == null) hiddenBy = part;
            }
            return hiddenBy;
        }
        static IEnumerable<ShaderPart> Ancestors(ShaderPart property)
        {
            for (var parent = property.Parent; parent != null; parent = parent.Parent) yield return parent;
        }
        static bool OwnsTexture(Material material, ShaderProperty property)
        {
            if (material == null || material.shader == null || !property.MyShaderUI.Materials.Contains(material)) return false;
            int index = material.shader.FindPropertyIndex(property.MaterialProperty.name);
            return index >= 0 && material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Texture
                && material.shader.GetPropertyTextureDimension(index) == property.MaterialProperty.textureDimension;
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
        const string RichTextTag = @"</?[A-Za-z][^<>]*>";
        static string Clean(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", RichTextTag, "").Trim().TrimEnd('*').Split('|')[0];
        static string StripTags(string text) => string.IsNullOrEmpty(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text, RichTextTag, "").Replace('\n', ' ');
        static bool HasChanged(ShaderProperty property, ShaderEditor shader, HashSet<ShaderProperty> visited = null)
        {
            return RetainedPropertyDefaults.HasChanged(property, shader, visited);
        }
        // An unassigned texture uses the shader's default and is not a broken reference.
        // Only serialized references which Unity can no longer resolve qualify as missing.
        static Dictionary<Material, HashSet<string>> MissingTextureReferences(Material[] materials)
        {
            var result = new Dictionary<Material, HashSet<string>>();
            foreach (var material in materials)
            {
                if (material == null) continue;
                var names = new HashSet<string>();
                result[material] = names;
                using (var serialized = new SerializedObject(material))
                {
                    var textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                    if (textures == null) continue;
                    for (int i = 0; i < textures.arraySize; i++)
                    {
                        var entry = textures.GetArrayElementAtIndex(i);
                        var texture = entry.FindPropertyRelative("second.m_Texture");
                        if (texture != null && texture.objectReferenceValue == null
#if UNITY_6000_4_OR_NEWER
                            && texture.objectReferenceEntityIdValue != EntityId.None)
#else
                            && texture.objectReferenceInstanceIDValue != 0)
#endif
                            names.Add(entry.FindPropertyRelative("first").stringValue);
                    }
                }
            }
            return result;
        }
    }
}
