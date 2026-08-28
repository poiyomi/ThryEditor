using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEditor.MaterialProperty;

namespace Thry.ThryEditor
{
    public class ShaderGroup : ShaderPart
    {
        public override bool IsPropertyValueDefault
        {
            get
            {
                if(_isPropertyValueDefault == null)
                {
                    _isPropertyValueDefault = Children.All(p => p.IsPropertyValueDefault);
                }
                return _isPropertyValueDefault.Value;
            }
        }

        protected bool? _hasAnimatedDescendant;
        protected bool? _hasRenameAnimatedDescendant;

        public virtual bool HasAnimatedDescendant
        {
            get
            {
                if (_hasAnimatedDescendant == null)
                {
                    ResolveDescendantAnimatedStates();
                    _hasAnimatedDescendant = Children.Any(p =>
                        (p is ShaderGroup g && g.HasAnimatedDescendant) ||
                        (p.IsAnimated && !p.IsRenaming));
                }
                return _hasAnimatedDescendant.Value;
            }
        }

        public virtual bool HasRenameAnimatedDescendant
        {
            get
            {
                if (_hasRenameAnimatedDescendant == null)
                {
                    ResolveDescendantAnimatedStates();
                    _hasRenameAnimatedDescendant = Children.Any(p =>
                        (p is ShaderGroup g && g.HasRenameAnimatedDescendant) ||
                        (p.IsAnimated && p.IsRenaming));
                }
                return _hasRenameAnimatedDescendant.Value;
            }
        }

        // A property only reads its animated tag the first time it is drawn, and a collapsed group never draws
        // its children, so their IsAnimated would still be false when the header asks for it. Pull the state
        // straight from the material tags first so the dots reflect the section's contents whether it is
        // expanded or not. Resolving is one-time per property, and this runs only on a cache miss.
        // Done as a separate pass because the Any() below short-circuits and would leave the rest unresolved.
        private void ResolveDescendantAnimatedStates()
        {
            foreach (ShaderPart p in Children)
            {
                p.EnsureAnimatedStateResolved();
                if (p is ShaderGroup g) g.ResolveDescendantAnimatedStates();
            }
        }

        internal void SetAnimatedDescendantStateDirty()
        {
            _hasAnimatedDescendant = null;
            _hasRenameAnimatedDescendant = null;
            (Parent as ShaderGroup)?.SetAnimatedDescendantStateDirty();
        }

        private List<ShaderPart> _children = new List<ShaderPart>();
        private ReadOnlyCollection<ShaderPart> _readonlychildren => new ReadOnlyCollection<ShaderPart>(_children);
        [PublicAPI]
        public ReadOnlyCollection<ShaderPart> Children => _readonlychildren;

        private bool? _hasDrawableContent;

        /// <summary>
        /// True when this group would put something on screen beyond its own header row: a property, or a
        /// nested group that itself has content. A group carrying a reference property always counts, since
        /// that toggle is drawn in the header bar itself.
        ///
        /// A section whose properties all come from modules the shader doesn't include ends up here with no
        /// children at all — the category property in the skeleton is emitted unconditionally, its #K# sink
        /// stays empty. Drawing it advertises a section the shader does not have.
        ///
        /// The whole part tree is rebuilt by CollectAllProperties on every UI build, so a cached answer can
        /// never outlive the tree it was computed from. Reads _children rather than Children because the
        /// latter allocates a new ReadOnlyCollection per access, and this is reached from Draw.
        /// </summary>
        public bool HasDrawableContent
        {
            get
            {
                if (_hasDrawableContent == null)
                {
                    _hasDrawableContent = Options.reference_property != null
                        || (Options.reference_properties != null && Options.reference_properties.Length > 0)
                        || _children.Any(c => (c as ShaderGroup)?.HasDrawableContent ?? true);
                }
                return _hasDrawableContent.Value;
            }
        }

        protected override bool SkipDrawBecauseEmpty => !HasDrawableContent;

        private void SetHasDrawableContentDirty()
        {
            _hasDrawableContent = null;
            (Parent as ShaderGroup)?.SetHasDrawableContentDirty();
        }

        protected bool _isExpanded;
        private bool _isSearchExpanded;

        public ShaderGroup(ShaderEditor shaderEditor) : base(null, 0, "", null, shaderEditor)
        {

        }

        public ShaderGroup(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, int xOffset, string optionsRaw, int propertyIndex) : base(shaderEditor, prop, xOffset, displayName, optionsRaw, propertyIndex)
        {
            PropertyValueChanged += (PropertyValueEventArgs args) => 
            {
                if(!_doOptionsNeedInitilization && Options.persistent_expand)
                    _isExpanded = this.MaterialProperty.GetNumber() == 1;
            };
        }

        protected override void InitOptions()
        {
            base.InitOptions();
            if (Options.persistent_expand) _isExpanded = this.MaterialProperty.GetNumber() == 1;
            else _isExpanded = Options.default_expand;
        }

        protected bool IsExpanded
        {
            get
            {
                return ShaderEditor.Active.IsInSearchMode ? _isSearchExpanded : _isExpanded;
            }
            set
            {
                // Any expand/collapse changes heights below this point, so every cached
                // cull box is stale from here on.
                ThryCulling.InvalidateLayout();
                if(ShaderEditor.Active.IsInSearchMode)
                {
                    _isSearchExpanded = value;
                    return;
                }
                if (Options.persistent_expand)
                {
                    if (AnimationMode.InAnimationMode())
                    {
#if UNITY_2020_1_OR_NEWER
                        // So we do this instead
                        _isExpanded = value;
#else
                        // This fails when unselecting the object in hirearchy
                        // Then reselecting it
                        // Don't know why
                        // It seems AnimationMode is not working properly in Unity 2022
                        // It worked fine in Unity 2019
                        
                        AnimationMode.StopAnimationMode();
                        this.MaterialProperty.SetNumber(value ? 1 : 0);
                        Undo.SetCurrentGroupName((value ? "Expand" : "Collapse") + $" {Content.text} of {ShaderEditor.Active.TargetName}");
                        RaisePropertyValueChanged();
                        AnimationMode.StartAnimationMode();
#endif
                    }
                    else
                    {
                        this.MaterialProperty.SetNumber(value ? 1 : 0);
                        Undo.SetCurrentGroupName((value ? "Expand" : "Collapse") + $" {Content.text} of {ShaderEditor.Active.TargetName}");
                        RaisePropertyValueChanged();
                    }
                }
                _isExpanded = value;
            }
        }

        public void SetSearchExpanded(bool value)
        {
            _isSearchExpanded = value;
        }

        protected bool DoDisableChildren
        {
            get
            {
                return Options.condition_enable_children != null && !Options.condition_enable_children.Test();
            }
        }

        public void AddPart(ShaderPart part)
        {
            part.SetParent(this);
            _children.Add(part);
            SetHasDrawableContentDirty();
        }

        public override void CopyFrom(Material src, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;
            if (copyReferenceProperties)
                CopyReferencePropertiesFrom(src, skipPropertyTypes, skipPropertyNames);

            if (deepCopy)
                foreach (ShaderPart p in Children)
                    p.CopyFrom(src, false, true, copyReferenceProperties, skipPropertyTypes, skipPropertyNames);

            if (applyDrawers) MyShaderUI.ApplyDrawers();
        }

        public override void CopyFrom(ShaderPart srcPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;
            if (skipPropertyNames?.Contains(srcPart.MaterialProperty.name) == true) return;
            if (srcPart is ShaderGroup == false) return;
            ShaderGroup src = srcPart as ShaderGroup;
            if (copyReferenceProperties)
                CopyReferencePropertiesFrom(src, skipPropertyTypes, skipPropertyNames);

            // Match children by property name rather than by index. Matching by index breaks when
            // copying between shaders whose modules have added/removed/reordered properties,
            // causing values to land on the wrong properties even when the names line up.
            //
            // Fallback to position (see BuildCopyPairs) so that copying between structurally parallel
            // section names differ only by a slot suffix.
            if (deepCopy)
            {
                foreach (var pair in BuildCopyPairs(src.Children, Children))
                {
                    pair.Value.CopyFrom(pair.Key, false, true, copyReferenceProperties, skipPropertyTypes, skipPropertyNames);
                }
            }

            if (applyDrawers) MyShaderUI.ApplyDrawers();
        }

        public override void CopyTo(Material[] targets, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;
            if (copyReferenceProperties)
                CopyReferencePropertiesTo(targets, skipPropertyTypes, skipPropertyNames);

            if (deepCopy)
                foreach (ShaderPart p in Children)
                    p.CopyTo(targets, false, true, copyReferenceProperties, skipPropertyTypes, skipPropertyNames);

            if (applyDrawers) MaterialEditor.ApplyMaterialPropertyDrawers(targets);
        }

        public override void CopyTo(ShaderPart targetPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;
            if (skipPropertyNames?.Contains(targetPart.MaterialProperty.name) == true) return;
            if (targetPart is ShaderGroup == false) return;
            ShaderGroup target = targetPart as ShaderGroup;
            if (copyReferenceProperties)
                CopyReferencePropertiesTo(target, skipPropertyTypes, skipPropertyNames);

            // Match children by property name rather than by index, so copying between shaders whose
            // modules have added/removed/reordered properties still aligns correctly.
            //
            // Fallback to position (see BuildCopyPairs) so that copying between structurally parallel
            // section names differ only by a slot suffix.
            if (deepCopy)
            {
                foreach (var pair in BuildCopyPairs(Children, target.Children))
                {
                    pair.Key.CopyTo(pair.Value, false, true, copyReferenceProperties, skipPropertyTypes, skipPropertyNames);
                }
            }

            if (applyDrawers) MaterialEditor.ApplyMaterialPropertyDrawers(target.MaterialProperty.targets);
        }

        // Builds a property name -> child lookup for name-based copy matching. Children without a backing
        // MaterialProperty (e.g. labels) carry no value and are skipped; on duplicate names, the first wins.
        // 
        // Pairs each source child with the target child it should copy to. Matching is done in two passes:
        //   1. Exact property-name match. This keeps copies between shader versions correct even when a
        //      module has added/removed/reordered properties, so values never land on the wrong property.
        //   2. Positional fallback for source children whose name has no counterpart in the target. Each
        //      is paired with the next still-unmatched target child of the same structural kind (group vs.
        //      leaf) and property type, in order. This restores copying between structurally parallel
        //      sections whose properties differ only by a slot suffix (e.g. Poiyomi's Emission slots:
        //      _EmissionColor -> _EmissionColor1), which pure name matching silently dropped.
        // Children without a backing MaterialProperty (e.g. labels) carry no value and are skipped.
        private static List<KeyValuePair<ShaderPart, ShaderPart>> BuildCopyPairs(IList<ShaderPart> sourceChildren, IList<ShaderPart> targetChildren)
        {
            var pairs = new List<KeyValuePair<ShaderPart, ShaderPart>>();
            bool[] targetConsumed = new bool[targetChildren.Count];

            // Name -> first target index lookup (duplicates keep the first, matching the old behavior).
            var targetIndexByName = new Dictionary<string, int>();
            for (int i = 0; i < targetChildren.Count; i++)
            {
                ShaderPart t = targetChildren[i];
                if (t.MaterialProperty == null) continue;
                if (!targetIndexByName.ContainsKey(t.MaterialProperty.name)) targetIndexByName.Add(t.MaterialProperty.name, i);
            }

            // Pass 1: exact name matches. Unmatched source children are collected for the positional pass.
            var unmatchedSource = new List<ShaderPart>();
            foreach (ShaderPart srcChild in sourceChildren)
            {
                if (srcChild.MaterialProperty == null) continue;
                if (targetIndexByName.TryGetValue(srcChild.MaterialProperty.name, out int ti) && !targetConsumed[ti])
                {
                    targetConsumed[ti] = true;
                    pairs.Add(new KeyValuePair<ShaderPart, ShaderPart>(srcChild, targetChildren[ti]));
                }
                else
                {
                    unmatchedSource.Add(srcChild);
                }
            }

            // Pass 2: positional fallback, constrained to the same kind and property type so a value is
            // never copied onto an incompatible property.
            foreach (ShaderPart srcChild in unmatchedSource)
            {
                for (int i = 0; i < targetChildren.Count; i++)
                {
                    if (targetConsumed[i]) continue;
                    ShaderPart targetChild = targetChildren[i];
                    if (targetChild.MaterialProperty == null) continue;
                    if ((srcChild is ShaderGroup) != (targetChild is ShaderGroup)) continue;
                    if (srcChild.MaterialProperty.GetPropertyType() != targetChild.MaterialProperty.GetPropertyType()) continue;

                    targetConsumed[i] = true;
                    pairs.Add(new KeyValuePair<ShaderPart, ShaderPart>(srcChild, targetChild));
                    break;
                }
            }

            return pairs;
        }

        protected override void DrawInternal(GUIContent content, Rect? rect = null, bool useEditorIndent = false, bool isInHeader = false)
        {
            if (Options.margin_top > 0)
            {
                GUILayoutUtility.GetRect(0, Options.margin_top);
            }
            ThryCulling.DrawChildren(Children);
        }

        public override void FindUnusedTextures(List<string> unusedList, bool isEnabled)
        {
            if (isEnabled && Options.condition_enable != null)
            {
                isEnabled &= Options.condition_enable.Test();
            }
            foreach (ShaderPart p in (this as ShaderGroup).Children)
                p.FindUnusedTextures(unusedList, isEnabled);
        }

        public void UpdateLinkedMaterials()
        {
            if(ShaderEditor.Active.IsInAnimationMode) return;
            IEnumerable<Material> linked_materials = MaterialLinker.GetLinked(MaterialProperty);
            if (linked_materials != null)
                this.CopyTo(linked_materials.ToArray());
        }

        protected void FoldoutArrow(Rect rect, Event e)
        {
            if (e.type == EventType.Repaint)
            {
                Rect arrowRect = new RectOffset(4, 0, 0, 0).Remove(rect);
                arrowRect.width = 13;
                EditorStyles.foldout.Draw(arrowRect, false, false, IsExpanded, false);
            }
        }

        public override bool Search(string searchTerm, List<ShaderGroup> foundHeaders, bool isParentInSearch = false)
        {
            bool found = isParentInSearch
                || this.Content.text.IndexOf(searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0
                || this.MaterialProperty?.name.IndexOf(searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool foundInChild = false;
            foreach (ShaderPart p in Children)
            {
                if (p.Search(searchTerm, foundHeaders, isParentInSearch || found))
                    foundInChild = true;
            }
            found |= foundInChild;
            if(found && this is ShaderHeader) foundHeaders.Add(this);
            this.has_not_searchedFor = !found;
            return found;
        }
    }


    /// <summary>
    /// Viewport culling for the shader inspector.
    ///
    /// A Poiyomi material can put tens of thousands of pixels of UI into a window a thousand pixels
    /// tall, and IMGUI draws all of it on every event. Each part remembers the box it last occupied;
    /// parts whose box falls outside the viewport reserve their height and skip drawing entirely.
    ///
    /// The cull decision reads only the PREVIOUS frame's viewport snapshot, never a live value.
    /// That matters: Layout and Repaint must emit an identical number of layout entries or IMGUI
    /// throws, and ShaderEditor.OnGUI already carries a comment about undo crashing on exactly that
    /// mismatch.
    /// </summary>
    internal static class ThryCulling
    {
        public static bool Enabled = true;

        /// <summary>Extra margin above and below the viewport kept drawn, to absorb small scrolls.</summary>
        public static float Margin = 200f;

        /// <summary>
        /// Draw texture fields through Unity's inner routine rather than
        /// MaterialEditor.TexturePropertyMiniThumbnail. See GUILib.DrawTextureMiniThumbnail.
        /// </summary>
        public static bool FastTextureField = true;

        /// <summary>
        /// Bumped whenever something changes a part's height. Cached boxes from an older epoch are
        /// not trusted, so everything redraws once and re-measures. Without this, a section expanded
        /// while scrolled offscreen keeps reserving its old collapsed height.
        /// </summary>
        public static int Epoch;

        public static void InvalidateLayout() { Epoch++; }

        static bool _needsInvalidate;

        /// <summary>
        /// Request invalidation from inside a draw. Bumping Epoch mid-frame would make Repaint take
        /// different branches than Layout did, so it is deferred to the next frame boundary.
        /// </summary>
        static void InvalidateLayoutDeferred() { _needsInvalidate = true; }

        /// <summary>Viewport snapshot used for cull decisions. Constant for a whole frame.</summary>
        static Rect _frame;

        static Rect _pending;
        static bool _hasPending;
        static System.Reflection.PropertyInfo _visibleRect;

        static Rect Live
        {
            get
            {
                if (_visibleRect == null)
                {
                    var t = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
                    if (t != null)
                        _visibleRect = t.GetProperty("visibleRect",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                }
                if (_visibleRect == null) return new Rect(0, 0, 0, 0);
                try { return (Rect)_visibleRect.GetValue(null, null); } catch { return new Rect(0, 0, 0, 0); }
            }
        }

        public static void BeginFrame()
        {
            if (!Enabled) return;
            if (Event.current.type == EventType.Layout)
            {
                if (_needsInvalidate) { _needsInvalidate = false; Epoch++; }
                if (_hasPending) _frame = _pending;
            }
        }

        public static void EndFrame()
        {
            if (!Enabled) return;
            if (Event.current.type == EventType.Repaint) { _pending = Live; _hasPending = true; }
        }

        /// <summary>Draws a child list, skipping parts whose last measured box is outside the viewport.</summary>
        public static void DrawChildren(System.Collections.Generic.IList<ShaderPart> children)
        {
            if (!Enabled)
            {
                for (int i = 0; i < children.Count; i++) children[i].Draw();
                return;
            }

            // Wrapping every child in a measuring group is not free. If this whole child list
            // already fits inside the viewport there is nothing worth culling, so draw it plainly.
            // Uses only cached, epoch-valid data, so Layout and Repaint make the same call.
            if (_frame.height > 0)
            {
                float total = 0f;
                bool allValid = true;
                for (int i = 0; i < children.Count; i++)
                {
                    ShaderPart c = children[i];
                    if (c.CullEpoch != Epoch || c.CullHeight <= 0f) { allValid = false; break; }
                    total += c.CullHeight;
                }
                if (allValid && total <= _frame.height)
                {
                    for (int i = 0; i < children.Count; i++) children[i].Draw();
                    return;
                }
            }

            bool repaint = Event.current.type == EventType.Repaint;

            // One group around the whole list gives an exact end position, so the final child gets a
            // real pitch like every other child. Approximating it with its own group rect left a
            // ~1.6px disagreement that retriggered the self-heal every frame.
            Rect listBox = EditorGUILayout.BeginVertical(GUIStyle.none);

            ShaderPart pending = null;   // height is unknown until the next sibling starts
            float pendingY = 0f;
            float pendingOldHeight = 0f; // previous frame's value, for the self-heal comparison
            float pendingBox = 0f;

            for (int i = 0; i < children.Count; i++)
            {
                ShaderPart part = children[i];
                float startY;
                float boxHeight;

                bool offscreen = part.CullHeight > 0 && part.CullEpoch == Epoch && _frame.height > 0
                    && (part.CullY + part.CullHeight < _frame.y - Margin || part.CullY > _frame.yMax + Margin);

                if (offscreen)
                {
                    // Reserve inside the same unstyled group the draw path uses, so both branches
                    // present identical structure to GUILayout and pick up identical spacing.
                    EditorGUILayout.BeginVertical(GUIStyle.none);
                    Rect reserved = GUILayoutUtility.GetRect(0, part.CullHeight, GUIStyle.none);
                    EditorGUILayout.EndVertical();
                    startY = reserved.y;
                    boxHeight = part.CullHeight;
                }
                else
                {
                    Rect box = EditorGUILayout.BeginVertical(GUIStyle.none);
                    part.Draw();
                    EditorGUILayout.EndVertical();
                    startY = box.y;
                    boxHeight = box.height;
                }

                if (repaint)
                {
                    part.CullY = startY;

                    if (pending != null)
                    {
                        // Pitch (this start -> next start) includes the inter-sibling spacing that
                        // the group rect omits. Using bare group height under-reserved by ~1.6px per
                        // part, which made culled content creep upward while scrolling.
                        float pitch = startY - pendingY;

                        // Self-heal: anything that changes a height without going through the
                        // IsExpanded setter (texture foldouts, conditional properties, search) shows
                        // up as a pitch that disagrees with what we cached.
                        if (pending.CullEpoch == Epoch && pendingOldHeight > 0f
                            && Mathf.Abs(pendingOldHeight - pitch) > 0.5f)
                            InvalidateLayoutDeferred();

                        pending.CullHeight = pitch;
                        pending.CullEpoch = Epoch;
                    }

                    pending = part;
                    pendingY = startY;
                    pendingOldHeight = part.CullHeight;
                    pendingBox = boxHeight;
                }
            }

            EditorGUILayout.EndVertical();

            if (repaint && pending != null)
            {
                float pitch = listBox.yMax - pendingY;
                if (pitch <= 0f) pitch = pendingBox;
                if (pending.CullEpoch == Epoch && pendingOldHeight > 0f
                    && Mathf.Abs(pendingOldHeight - pitch) > 0.5f)
                    InvalidateLayoutDeferred();
                if (pitch > 0f) pending.CullHeight = pitch;
                pending.CullEpoch = Epoch;
            }
        }
    }
}
