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
            get { ResolveAnimatedSummary(); return _hasAnimatedDescendant.Value; }
        }

        public virtual bool HasRenameAnimatedDescendant
        {
            get { ResolveAnimatedSummary(); return _hasRenameAnimatedDescendant.Value; }
        }

        private void ResolveAnimatedSummary()
        {
            if (_hasAnimatedDescendant != null && _hasRenameAnimatedDescendant != null) return;
            ShaderAnimationSummary.Resolve(this, out var animated, out var renamed);
            _hasAnimatedDescendant = animated;
            _hasRenameAnimatedDescendant = renamed;
        }

        internal void SetAnimatedDescendantStateDirty()
        {
            _hasAnimatedDescendant = null;
            _hasRenameAnimatedDescendant = null;
            (Parent as ShaderGroup)?.SetAnimatedDescendantStateDirty();
        }

        private List<ShaderPart> _children = new List<ShaderPart>();
        private ReadOnlyCollection<ShaderPart> _readonlychildren;
        [PublicAPI]
        public ReadOnlyCollection<ShaderPart> Children => _readonlychildren ?? (_readonlychildren = _children.AsReadOnly());

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
                if(!_doOptionsNeedInitilization && PersistsExpanded)
                    _isExpanded = this.MaterialProperty.GetNumber() == 1;
            };
        }

        protected override void InitOptions()
        {
            base.InitOptions();
            if (PersistsExpanded) _isExpanded = this.MaterialProperty.GetNumber() == 1;
            else _isExpanded = Options.default_expand;
        }

        protected bool IsExpanded
        {
            get
            {
                if (_doOptionsNeedInitilization && MaterialProperty != null) InitOptions();
                return ShaderEditor.Active.IsInSearchMode ? _isSearchExpanded : _isExpanded;
            }
            set
            {
                if (_doOptionsNeedInitilization && MaterialProperty != null) InitOptions();
                if(ShaderEditor.Active.IsInSearchMode)
                {
                    _isSearchExpanded = value;
                    return;
                }
                if (PersistsExpanded)
                {
                    if (AnimationMode.InAnimationMode())
                    {
                        // So we do this instead
                        _isExpanded = value;
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

        // Explicit header actions use the same persistence and animation rules as IMGUI.
        internal void SetExpandedFromView(bool expanded)
        {
            MyShaderUI.ActivateRetained();
            IsExpanded = expanded;
        }

        // Navigation is a view operation; it must not write persistent material foldout properties.
        internal void ExpandForNavigation(bool expanded)
        {
            if (_doOptionsNeedInitilization && MaterialProperty != null) InitOptions();
            _isExpanded = expanded;
            _isSearchExpanded = expanded;
        }

        /// <summary>
        /// A section whose foldout state lives on the material, rather than only in this editor.
        /// The root group owns no property, so it can only ever expand in memory.
        /// </summary>
        internal bool PersistsExpanded => Options.persistent_expand && MaterialProperty != null;

        // Retained inspectors toggle through this, so a click persists exactly like the IMGUI header.
        internal bool RetainedExpanded { get { return IsExpanded; } set { IsExpanded = value; } }
        internal bool RetainedChildrenEnabled => !DoDisableChildren;

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
            // Plain groups have no header chrome; expose their inclusion beside their existing label.
            if (SectionEditing.IsEditing(this))
            {
                Rect row = EditorGUILayout.GetControlRect();
                row.xMin = GUILib.GetPropertyX(XOffset);
                SectionEditing.DrawExcludedBackground(this, row);
                using (new SectionEditing.HeaderTintScope(this))
                    GUI.Label(new Rect(row.x + SlidingToggle.ReservedWidth, row.y, row.width - SlidingToggle.ReservedWidth, row.height), content);
                SectionEditing.DrawHeaderToggle?.Invoke(this, new Rect(row.x, row.y, SlidingToggle.Width, 16));
            }
            if (Options.margin_top > 0)
            {
                GUILayoutUtility.GetRect(0, Options.margin_top);
            }
            foreach (ShaderPart part in Children)
            {
                part.Draw();
            }
        }

        protected void DrawSection(bool isSubsection)
        {
            const int BORDER_WIDTH = 2;
            const int HEADER_HEIGHT = InspectorTheme.SectionHeight;
            const int CHECKBOX_OFFSET = 20;
            if (Options.margin_top > 0)
            {
                GUILayoutUtility.GetRect(0, Options.margin_top);
            }

            ShaderProperty reference = Options.reference_property != null ? MyShaderUI.PropertyDictionary[Options.reference_property] : null;
            int sectionToggleWidth = SectionEditing.IsEditing(this) ? SlidingToggle.ReservedWidth : 0;
            bool has_header = string.IsNullOrWhiteSpace(this.Content.text) == false || reference != null || sectionToggleWidth > 0;

            int headerTextX = 18 + sectionToggleWidth;
            int height = (has_header ? HEADER_HEIGHT : 0) + 4; // 4 for border margin

            // Draw border
            Rect border = EditorGUILayout.BeginVertical();
            float rightEdge = border.x + border.width;
            if (isSubsection) rightEdge -= GUILib.SectionContentRightPadding + 3;
            border.x = GUILib.GetPropertyX(this.XOffset) - BORDER_WIDTH;
            border.width = rightEdge - border.x - (isSubsection ? 0 : 1);
            border = new RectOffset(0, 0, -2, -2).Add(border);
            InspectorTheme.DrawSection(this, border, IsExpanded, has_header, isSubsection);

            // Draw Reference
            Rect clickCheckRect = GUILayoutUtility.GetRect(0, height);
            if (reference != null)
            {
                EditorGUI.BeginChangeCheck();
                Rect referenceRect = new Rect(border.x + CHECKBOX_OFFSET + sectionToggleWidth, border.y + (HEADER_HEIGHT - 18) / 2, 18, 18);
                using (new SectionEditing.HeaderTintScope(this))
                    reference.Draw(referenceRect, new GUIContent(), isInHeader: true, useEditorIndent: true);
                headerTextX = CHECKBOX_OFFSET + 20 + sectionToggleWidth;
                // Change expand state if reference is toggled
                if (EditorGUI.EndChangeCheck() && Options.ref_float_toggles_expand)
                {
                    IsExpanded = reference.MaterialProperty.GetNumber() == 1;
                }
            }

            // Draw Header (GUIContent)
            Rect top_border = new Rect(border.x, border.y, border.width - 20, HEADER_HEIGHT);
            if (has_header)
            {
                Rect header_rect = new RectOffset(headerTextX, 0, 0, 0).Remove(top_border);
                using (new SectionEditing.HeaderTintScope(this))
                    GUI.Label(header_rect, this.Content, isSubsection ? InspectorTheme.Subsection : InspectorTheme.Section);
            }

            // Draw menu icon
            if (has_header)
            {
                using (new SectionEditing.HeaderTintScope(this, graphics: true))
                    DrawMenuIcon(border, Event.current);
            }

            // Toggling + Draw Arrow
            if (sectionToggleWidth > 0)
                SectionEditing.DrawHeaderToggle?.Invoke(this, new Rect(border.x + 18, border.y + (HEADER_HEIGHT - 16) / 2, SlidingToggle.Width, 16));
            FoldoutArrow(top_border, Event.current);
            if (Event.current.type == EventType.MouseDown && clickCheckRect.Contains(Event.current.mousePosition))
            {
                IsExpanded = !IsExpanded;
                Event.current.Use();
            }

            // Draw Children
            if (IsExpanded)
            {
                GUILib.SectionContentPadding = isSubsection ? GUILib.SectionContentPadding + 2 : 4;
                GUILib.SectionContentRightPadding = isSubsection ? GUILib.SectionContentRightPadding + 2 : 2;
                EditorGUI.BeginDisabledGroup(DoDisableChildren);
                foreach (ShaderPart part in Children)
                {
                    part.Draw();
                }
                EditorGUI.EndDisabledGroup();
                GUILib.SectionContentPadding = isSubsection ? GUILib.SectionContentPadding - 2 : 0;
                GUILib.SectionContentRightPadding = isSubsection ? GUILib.SectionContentRightPadding - 2 : 0;
                if (!isSubsection) GUILayoutUtility.GetRect(0, 5);
            }
            EditorGUILayout.EndVertical();
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

        protected void DrawMenuIcon(Rect border, Event e)
        {
            Rect buttonRect = new Rect(border);
            buttonRect.x = border.x + border.width - 18;
            buttonRect.y = border.y + (InspectorTheme.SectionHeight - 16) / 2;
            buttonRect.width = 16;
            buttonRect.height = 16;

            if (GUILib.Button(buttonRect, Icons.menu))
            {
                ShaderEditor.Input.Use();
                ShowContextMenu(buttonRect);
            }
        }

        protected void ShowContextMenu(Rect position)
        {
            ShaderGroup section = this;
            Material[] materials = MyShaderUI.Materials;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Reset"), false, delegate()
            {
                section.ResetSection(materials);
            });
            menu.DropDown(position);
        }

        private void ResetSection(Material[] materials)
        {
            int undoGroup = Undo.GetCurrentGroup();
            var defaults = new Material(materials[0].shader);
            try
            {
                CopyFrom(defaults, true);
                var linkedMaterials = MaterialLinker.GetLinked(MaterialProperty);
                if (linkedMaterials != null)
                    foreach (Material material in linkedMaterials)
                        CopyTo(material, true);
                Undo.SetCurrentGroupName($"Reset {Content.text}");
            }
            finally
            {
                Object.DestroyImmediate(defaults);
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        protected void FoldoutArrow(Rect rect, Event e)
        {
            if (e.type == EventType.Repaint)
            {
                using (new SectionEditing.HeaderTintScope(this, graphics: true))
                    InspectorTheme.Chevron(rect, IsExpanded);
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

}
