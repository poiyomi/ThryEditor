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
        /// latter allocates a new ReadOnlyCollection per access.
        /// </summary>
        public bool HasDrawableContent
        {
            get
            {
                if (_hasDrawableContent == null)
                {
                    _hasDrawableContent = Options.reference_property != null
                        || (Options.reference_properties != null && Options.reference_properties.Length > 0)
                        || SectionExtensions.Has(MaterialProperty?.name)
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

        public ShaderGroup(ShaderEditor shaderEditor) : base(null, "", null, shaderEditor)
        {

        }

        public ShaderGroup(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, string optionsRaw, int propertyIndex) : base(shaderEditor, prop, displayName, optionsRaw, propertyIndex)
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
                return _isExpanded;
            }
            set
            {
                if (_doOptionsNeedInitilization && MaterialProperty != null) InitOptions();
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

        // Explicit header actions write the foldout to the material, except in animation mode.
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
        }

        /// <summary>
        /// A section whose foldout state lives on the material, rather than only in this editor.
        /// The root group owns no property, so it can only ever expand in memory.
        /// </summary>
        internal bool PersistsExpanded => Options.persistent_expand && MaterialProperty != null;

        // Retained inspectors toggle through this, so a click persists.
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
        // Pairs each source child with the target child it should copy to. Matching is done in three passes:
        //   1. Exact property-name match. This keeps copies between shader versions correct even when a
        //      module has added/removed/reordered properties, so values never land on the wrong property.
        //   2. Slot-insensitive name match: names compared with their digits removed, so structurally
        //      parallel sections whose properties differ only by a slot number still pair up by name
        //      (e.g. Poiyomi's Emission slots: _EmissionColor -> _EmissionColor1,
        //      _EmissionAL0Enabled -> _EmissionAL1Enabled).
        //   3. Positional fallback for whatever is still unmatched. Each is paired with the next
        //      still-unmatched target child of the same structural kind (group vs. leaf) and property type,
        //      in order. Running this last matters: a property that exists in only one slot (Emission 0's
        //      _EmissionFallbackHelp, an Int and so Float-backed) would otherwise consume the first Float of
        //      the other slot and shift every later Float by one, turning _EmissionBaseColorAsMap into
        //      _EmissionReplace1.
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

            // Pass 2: slot-insensitive name matches. Several children can share a slotless name; those pair
            // up in order. Unmatched source children are collected for the positional pass.
            string[] targetSlotlessNames = new string[targetChildren.Count];
            for (int i = 0; i < targetChildren.Count; i++)
            {
                if (targetChildren[i].MaterialProperty != null) targetSlotlessNames[i] = RemoveSlotNumbers(targetChildren[i].MaterialProperty.name);
            }

            var positionalSource = new List<ShaderPart>();
            foreach (ShaderPart srcChild in unmatchedSource)
            {
                string slotlessName = RemoveSlotNumbers(srcChild.MaterialProperty.name);
                int match = -1;
                for (int i = 0; i < targetChildren.Count; i++)
                {
                    if (targetConsumed[i] || targetSlotlessNames[i] != slotlessName) continue;
                    if (!AreCopyCompatible(srcChild, targetChildren[i])) continue;
                    match = i;
                    break;
                }

                if (match < 0)
                {
                    positionalSource.Add(srcChild);
                    continue;
                }
                targetConsumed[match] = true;
                pairs.Add(new KeyValuePair<ShaderPart, ShaderPart>(srcChild, targetChildren[match]));
            }

            // Pass 3: positional fallback, constrained to the same kind and property type so a value is
            // never copied onto an incompatible property.
            foreach (ShaderPart srcChild in positionalSource)
            {
                for (int i = 0; i < targetChildren.Count; i++)
                {
                    if (targetConsumed[i]) continue;
                    ShaderPart targetChild = targetChildren[i];
                    if (targetChild.MaterialProperty == null) continue;
                    if (!AreCopyCompatible(srcChild, targetChild)) continue;

                    targetConsumed[i] = true;
                    pairs.Add(new KeyValuePair<ShaderPart, ShaderPart>(srcChild, targetChild));
                    break;
                }
            }

            return pairs;
        }

        // Same structural kind (group vs. leaf) and property type.
        private static bool AreCopyCompatible(ShaderPart a, ShaderPart b)
        {
            if ((a is ShaderGroup) != (b is ShaderGroup)) return false;
            return a.MaterialProperty.GetPropertyType() == b.MaterialProperty.GetPropertyType();
        }

        // _EmissionReplace0 / _EmissionReplace1 -> _EmissionReplace, _EmissionAL0Enabled -> _EmissionALEnabled
        private static string RemoveSlotNumbers(string propertyName)
        {
            var sb = new System.Text.StringBuilder(propertyName.Length);
            foreach (char c in propertyName)
            {
                if (c < '0' || c > '9') sb.Append(c);
            }
            return sb.ToString();
        }
    }

}
