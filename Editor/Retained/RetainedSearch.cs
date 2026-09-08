#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
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
        }
        readonly MaterialInspectorView _view;
        readonly Label _summary;
        readonly VisualElement _results;
        RetainedMaterialModel _model;
        RetainedFields _fields;
        string _signature, _query;
        int _revision = -1;
        double _nextRefresh;
        internal int MatchCount { get; private set; }

        internal RetainedSearch(MaterialInspectorView view, Action clear)
        {
            _view = view;
            name = "thry-property-search-results";
            AddToClassList("thry-property-search-results");
            var toolbar = new VisualElement(); toolbar.AddToClassList("thry-filter-toolbar"); Add(toolbar);
            _summary = new Label(); _summary.AddToClassList("thry-search-summary"); toolbar.Add(_summary);
            var clearButton = new Button(clear) { name = "thry-clear-filter", text = "Clear filters", tooltip = "Clear the text, type, and changed filters to show the full inspector again." };
            clearButton.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefault(); e.StopImmediatePropagation(); clear(); }, TrickleDown.TrickleDown);
            toolbar.Add(clearButton);
            _results = new VisualElement(); _results.AddToClassList("thry-search-matches"); _results.AddToClassList("thry-material-controls"); Add(_results);
            style.display = DisplayStyle.None;
        }
        internal static string TextQuery(string query) => string.Join(" ", (query ?? "").Split(' ').Where(token => !IsFilter(token))).Trim();
        static bool IsFilter(string token) => token.Equals("is:changed", StringComparison.OrdinalIgnoreCase) || token.Equals("is:animated", StringComparison.OrdinalIgnoreCase) || token.Equals("is:missing", StringComparison.OrdinalIgnoreCase) || TypeFilter(token) != null;
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
            var candidates = _results.Query<VisualElement>().ToList().Where(e => e.focusable && e.enabledInHierarchy && IsDisplayed(e)).ToList();
            var input = candidates.FirstOrDefault(e => !(e is Button) && e.GetFirstAncestorOfType<Button>() == null) ?? candidates.FirstOrDefault();
            if (input == null) return false;
            input.Focus(); input.schedule.Execute(() => { if (input.panel != null) input.Focus(); }).StartingIn(0); return true;
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
                style.display = DisplayStyle.None;
                if (_signature != null || _model != null) _results.Clear();
                _signature = null; _query = null; _model = null; _fields = null; _revision = -1; MatchCount = 0; return;
            }
            model.Shader.ActivateRetained();
            bool modelChanged = _model != model || _revision != model.Shader.RetainedRevision;
            bool queryChanged = _query != query;
            // Refresh values without replacing active fields or resetting slider drags.
            if (!modelChanged && _fields != null) { _fields.Synchronize(); AlignColumns(); }
            style.display = DisplayStyle.Flex;
            if (!modelChanged && !queryChanged && EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + .35;
            var matches = FindMatches(model.Shader, query);
            string signature = query + "|" + string.Join("|", matches.Select(m => m.Property.MaterialProperty.name + ":" + m.Path));
            if (!modelChanged && _signature == signature) return;
            if (!modelChanged && !queryChanged && IsEditingResult()) return;
            _results.Clear();
            _model = model; _revision = model.Shader.RetainedRevision; _query = query; _signature = signature;
            _fields = new RetainedFields(model, _view);
            MatchCount = matches.Count;
            _summary.text = MatchCount == 0 ? "No matching properties" : MatchCount == 1 ? "1 matching property" : MatchCount + " matching properties";
            if (matches.Count == 0)
            {
                var empty = new Label("Try a property or section name, such as Normal Map, Emission, or Alpha Cutoff. Remove a filter to include more properties.");
                empty.AddToClassList("thry-search-hint"); _results.Add(empty); return;
            }
            foreach (var group in matches.GroupBy(m => m.Path))
            {
                var section = new VisualElement(); section.AddToClassList("thry-filter-group"); _results.Add(section);
                var breadcrumb = new Label(string.IsNullOrEmpty(group.Key) ? "General" : group.Key) { tooltip = group.Key };
                breadcrumb.AddToClassList("thry-filter-breadcrumb"); section.Add(breadcrumb);
                foreach (var match in group)
                {
                    var wrapper = new VisualElement { name = "filtered-property-" + match.Property.MaterialProperty.name };
                    wrapper.AddToClassList("thry-filter-control"); section.Add(wrapper);
                    wrapper.Add(_fields.Field(match.Property));
                    _fields.Track(wrapper, () => {
                        wrapper.style.display = IsVisible(match.Property, match.Owner) ? DisplayStyle.Flex : DisplayStyle.None;
                        wrapper.SetEnabled(AncestorsEnabled(match.Property, match.Owner));
                    });
                }
            }
            _fields.Synchronize(); _results.schedule.Execute(AlignColumns).StartingIn(0);
        }
        bool IsEditingResult()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && _results.Contains(focused)) return true;
            // Dropdown focus temporarily lives in a sibling overlay.
            return panel?.visualTree.Q("thry-dropdown-menu") != null;
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
        static List<Match> FindMatches(ShaderEditor shader, string query)
        {
            var tokens = query.Split(' ');
            bool changed = tokens.Any(t => t.Equals("is:changed", StringComparison.OrdinalIgnoreCase));
            bool animated = tokens.Any(t => t.Equals("is:animated", StringComparison.OrdinalIgnoreCase));
            bool missing = tokens.Any(t => t.Equals("is:missing", StringComparison.OrdinalIgnoreCase));
            var types = new HashSet<string>(tokens.Select(TypeFilter).Where(t => t != null));
            string text = TextQuery(query);
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var missingNames = missing ? MissingTextureReferences(shader.Materials) : null;
            var owners = new Dictionary<string, ShaderPart>();
            foreach (var part in shader.ShaderParts)
            {
                if (!string.IsNullOrEmpty(part.Options.reference_property)) owners[part.Options.reference_property] = part;
                if (part.Options.reference_properties != null) foreach (var reference in part.Options.reference_properties) owners[reference] = part;
            }
            var roots = new HashSet<ShaderGroup>(shader.RootCategories);
            var matches = new List<Match>();
            foreach (var property in shader.PropertyDictionary.Values.OrderBy(p => p.ShaderPropertyIndex))
            {
                if (property.MaterialProperty == null || property.ShaderPropertyIndex < 0) continue;
                ShaderPart owner; owners.TryGetValue(property.MaterialProperty.name, out owner);
                if (property.IsHidden && owner == null) continue;
                if (!IsVisible(property, owner)) continue;
                if (types.Count > 0 && !types.Contains(PropertyType(property))) continue;
                var ancestry = Ancestors(owner ?? property).Where(p => p.MaterialProperty != null).Reverse().ToList();
                if (owner is ShaderTextureProperty || owner is ShaderGroup) ancestry.Add(owner);
                string path = string.Join(" › ", ancestry.Select(p => Clean(p.Content.text)).Where(label => label.Length > 0));
                // Root category words should not make "normal" match all Color & Normals properties.
                var context = ancestry.Where(p => !(p is ShaderGroup && roots.Contains((ShaderGroup)p)) || Clean(p.Content.text).Equals(text, StringComparison.OrdinalIgnoreCase));
                string haystack = Clean(property.Content.text) + " " + property.MaterialProperty.name + " " + string.Join(" ", context.Select(p => Clean(p.Content.text)));
                if (words.Any(word => haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (changed && !HasChanged(property, shader)) continue;
                if (animated && !property.IsAnimated) continue;
                if (missing && !missingNames.Contains(property.MaterialProperty.name)) continue;
                matches.Add(new Match { Property = property, Owner = owner, Path = path });
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
                        if (texture != null && texture.objectReferenceValue == null && texture.objectReferenceInstanceIDValue != 0)
                            result.Add(entry.FindPropertyRelative("first").stringValue);
                    }
                }
            }
            return result;
        }
    }
}
#endif
