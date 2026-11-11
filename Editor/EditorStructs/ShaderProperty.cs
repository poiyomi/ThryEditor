using JetBrains.Annotations;
using System;
using System.Linq;
using System.Collections.Generic;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEditor.MaterialProperty;
using Thry.ThryEditor.Drawers;

namespace Thry.ThryEditor
{
    public class ShaderProperty : ShaderPart
    {
        public string Keyword { private set; get; }

        bool _isAnimatedStateResolved = false;
        internal bool HasPlainAnimatedOwners { get; private set; }
        internal bool HasRenamedAnimatedOwners { get; private set; }
        internal bool HasMixedAnimatedOwners { get; private set; }
        internal bool AllOwnersAnimated { get; private set; }
        internal string AnimatedOwnersTooltip { get; private set; } = "";

        public ShaderProperty(ShaderEditor shaderEditor, string propertyIdentifier, string displayName, string tooltip, int propertyIndex) : base(propertyIdentifier, displayName, tooltip, shaderEditor)
        {
            this.ShaderPropertyIndex = propertyIndex;
        }

        public ShaderProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, string optionsRaw, int propertyIndex) : base(shaderEditor, materialProperty, displayName, optionsRaw, propertyIndex)
        {
        }

        public void SetKeyword(string keyword)
        {
            this.Keyword = keyword;
        }

        [PublicAPI]
        public float FloatValue
        {
            get
            {
                return MaterialProperty.floatValue;
            }
            set
            {
                MaterialProperty.SetNumber(value);
                if (Keyword != null) SetKeywordState(ShaderEditor.Active.Materials, MaterialProperty.GetNumber() == 1);
                ExecuteOnValueActions(ShaderEditor.Active.Materials);
                MaterialEditor.ApplyMaterialPropertyDrawers(ShaderEditor.Active.Materials);
            }
        }

        [PublicAPI]
        public Vector4 VectorValue
        {
            get
            {
                return MaterialProperty.vectorValue;
            }
            set
            {
                MaterialProperty.vectorValue = value;
                ExecuteOnValueActions(ShaderEditor.Active.Materials);
                MaterialEditor.ApplyMaterialPropertyDrawers(ShaderEditor.Active.Materials);
            }
        }

        [PublicAPI]
        public Color ColorValue
        {
            get
            {
                return MaterialProperty.colorValue;
            }
            set
            {
                MaterialProperty.colorValue = value;
                ExecuteOnValueActions(ShaderEditor.Active.Materials);
                MaterialEditor.ApplyMaterialPropertyDrawers(ShaderEditor.Active.Materials);
            }
        }

        [PublicAPI]
        public Texture TextureValue
        {
            get
            {
                return MaterialProperty.textureValue;
            }
            set
            {
                MaterialProperty.textureValue = value;
                MaterialEditor.ApplyMaterialPropertyDrawers(ShaderEditor.Active.Materials);
            }
        }

        public override void CopyFrom(Material src, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if (skipPropertyTypes?.Contains(MaterialProperty.GetPropertyType()) == true) return;
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;

            UpdatedMaterialPropertyReference();

            MaterialHelper.CopyValue(src, MaterialProperty);
            TileLabelUtility.CopyTileLabelTag(src, MaterialProperty);
            CopyUdimSiblingsFrom(src, skipPropertyTypes, skipPropertyNames);
            if (copyReferenceProperties)
                CopyReferencePropertiesFrom(src, skipPropertyTypes, skipPropertyNames);

            if (Keyword != null) SetKeywordState(MyShaderUI.Materials, src.GetNumber(MaterialProperty) == 1);
            if (IsAnimatable)
            {
                ShaderOptimizer.CopyAnimatedTag(src, MaterialProperty);
                UpdateIsAnimatedFromTag();
            }

            RaisePropertyValueChanged();
            if (applyDrawers) MyShaderUI.ApplyDrawers();
        }

        public override void CopyFrom(ShaderPart srcPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if (skipPropertyTypes?.Contains(MaterialProperty.GetPropertyType()) == true) return;
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;
            if (skipPropertyNames?.Contains(srcPart.MaterialProperty.name) == true) return;
            if (srcPart is ShaderProperty == false) return;
            ShaderProperty src = srcPart as ShaderProperty;

            UpdatedMaterialPropertyReference();
            src.UpdatedMaterialPropertyReference();

            MaterialHelper.CopyValue(src.MaterialProperty, MaterialProperty);
            TileLabelUtility.CopyTileLabelTag(src.MaterialProperty, MaterialProperty);
            CopyUdimSiblingsFrom(src, skipPropertyTypes, skipPropertyNames);
            if (copyReferenceProperties)
                CopyReferencePropertiesFrom(src, skipPropertyTypes, skipPropertyNames);

            if (Keyword != null) SetKeywordState(MyShaderUI.Materials, (src.MaterialProperty.targets[0] as Material).GetNumber(MaterialProperty) == 1);
            if (IsAnimatable)
            {
                ShaderOptimizer.CopyAnimatedTag(src.MaterialProperty, MaterialProperty);
                UpdateIsAnimatedFromTag();
            }

            RaisePropertyValueChanged();
            if (applyDrawers) MyShaderUI.ApplyDrawers();
        }

        public override void CopyTo(Material[] targets, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if(skipPropertyTypes?.Contains(MaterialProperty.GetPropertyType()) == true) return;
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;

            UpdatedMaterialPropertyReference();

            MaterialHelper.CopyValue(MaterialProperty, targets);
            TileLabelUtility.CopyTileLabelTag(MaterialProperty, targets);
            CopyUdimSiblingsTo(targets, skipPropertyTypes, skipPropertyNames);
            if (copyReferenceProperties)
                CopyReferencePropertiesTo(targets, skipPropertyTypes, skipPropertyNames);

            if (Keyword != null) SetKeywordState(targets, MaterialProperty.GetNumber() == 1);
            if (IsAnimatable)
            {
                ShaderOptimizer.CopyAnimatedTag(MaterialProperty, targets);
            }

            if (applyDrawers) MaterialEditor.ApplyMaterialPropertyDrawers(targets);
        }

        public override void CopyTo(ShaderPart targetPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            if(skipPropertyTypes?.Contains(MaterialProperty.GetPropertyType()) == true) return;
            if (skipPropertyNames?.Contains(MaterialProperty.name) == true) return;
            if (skipPropertyNames?.Contains(targetPart.MaterialProperty.name) == true) return;
            if (targetPart is ShaderProperty == false) return;
            ShaderProperty target = targetPart as ShaderProperty;

            UpdatedMaterialPropertyReference();
            target.UpdatedMaterialPropertyReference();

            MaterialHelper.CopyValue(MaterialProperty, target.MaterialProperty);
            TileLabelUtility.CopyTileLabelTag(MaterialProperty, target.MaterialProperty);
            CopyUdimSiblingsTo(target, skipPropertyTypes, skipPropertyNames);
            if (copyReferenceProperties)
                CopyReferencePropertiesTo(target, skipPropertyTypes, skipPropertyNames);

            if (Keyword != null) SetKeywordState(target.MaterialProperty.targets as Material[], MaterialProperty.GetNumber() == 1);
            if (IsAnimatable)
            {
                ShaderOptimizer.CopyAnimatedTag(MaterialProperty, target.MaterialProperty.targets as Material[]);
            }

            target.RaisePropertyValueChanged();
            if (applyDrawers) MaterialEditor.ApplyMaterialPropertyDrawers(target.MaterialProperty.targets as Material[]);
        }

        // UV Tile Discard rows expose only their column-0 tile in the inspector; columns 1-3 are
        // [HideInInspector] and so are absent from a section's copyable children. When the visible column
        // is copied, pull its three hidden siblings along (resolved from the PropertyDictionary) so the
        // whole row's on/off values travel. Returns early for every non-tile property, and the gate on
        // the column-0 name means the siblings themselves never re-enter this and recurse.
        void CopyUdimSiblingsFrom(Material src, HashSet<ShaderPropertyType> skipPropertyTypes, HashSet<string> skipPropertyNames)
        {
            var siblings = TileLabelUtility.GetHiddenSiblingPropertyNames(MaterialProperty.name);
            if (siblings == null || MyShaderUI?.PropertyDictionary == null) return;
            foreach (var name in siblings)
            {
                if (MyShaderUI.PropertyDictionary.TryGetValue(name, out var tgt))
                {
                    tgt.CopyFrom(src, false, false, false, skipPropertyTypes, skipPropertyNames);
                }
            }
        }
        void CopyUdimSiblingsFrom(ShaderProperty src, HashSet<ShaderPropertyType> skipPropertyTypes, HashSet<string> skipPropertyNames)
        {
            var siblings = TileLabelUtility.GetHiddenSiblingPropertyNames(MaterialProperty.name);
            if (siblings == null || MyShaderUI?.PropertyDictionary == null || src.MyShaderUI?.PropertyDictionary == null) return;
            foreach (var name in siblings)
            {
                if (MyShaderUI.PropertyDictionary.TryGetValue(name, out var tgt) && src.MyShaderUI.PropertyDictionary.TryGetValue(name, out var srcSib))
                {
                    tgt.CopyFrom(srcSib, false, false, false, skipPropertyTypes, skipPropertyNames);
                }
            }
        }
        void CopyUdimSiblingsTo(Material[] targets, HashSet<ShaderPropertyType> skipPropertyTypes, HashSet<string> skipPropertyNames)
        {
            var siblings = TileLabelUtility.GetHiddenSiblingPropertyNames(MaterialProperty.name);
            if (siblings == null || MyShaderUI?.PropertyDictionary == null) return;
            foreach (var name in siblings)
            {
                if (MyShaderUI.PropertyDictionary.TryGetValue(name, out var srcSib))
                {
                    srcSib.CopyTo(targets, false, false, false, skipPropertyTypes, skipPropertyNames);
                }
            }
        }
        void CopyUdimSiblingsTo(ShaderProperty target, HashSet<ShaderPropertyType> skipPropertyTypes, HashSet<string> skipPropertyNames)
        {
            var siblings = TileLabelUtility.GetHiddenSiblingPropertyNames(MaterialProperty.name);
            if (siblings == null || MyShaderUI?.PropertyDictionary == null || target.MyShaderUI?.PropertyDictionary == null) return;
            foreach (var name in siblings)
            {
                if (MyShaderUI.PropertyDictionary.TryGetValue(name, out var srcSib) && target.MyShaderUI.PropertyDictionary.TryGetValue(name, out var tgtSib))
                {
                    srcSib.CopyTo(tgtSib, false, false, false, skipPropertyTypes, skipPropertyNames);
                }
            }
        }

        private void SetKeywordState(Material[] materials, bool enabled)
        {
            if (enabled) foreach (Material m in materials) m.EnableKeyword(Keyword);
            else foreach (Material m in materials) m.DisableKeyword(Keyword);
        }

        private void SetKeywordState(Material m, bool enabled)
        {
            if (enabled) m.EnableKeyword(Keyword);
            else m.DisableKeyword(Keyword);
        }

        public void UpdateKeywordFromValue()
        {
            if (Keyword != null) SetKeywordState(MyShaderUI.Materials, MaterialProperty.GetNumber() == 1);
        }

        internal override void EnsureAnimatedStateResolved()
        {
            if (_isAnimatedStateResolved) return;
            if (MaterialProperty == null) return;
            UpdateIsAnimatedFromTag();
        }

        internal void RefreshRetainedAnimatedState()
        {
            if (MaterialProperty != null && MaterialProperty.targets.Length > 0) UpdateIsAnimatedFromTag();
        }

        private sealed class AnimatedOwnerMetadata
        {
            internal int DirtyCount = int.MinValue;
            internal string Suffix;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Material, AnimatedOwnerMetadata> AnimatedOwnerCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Material, AnimatedOwnerMetadata>();
        internal static void InvalidateRetainedAnimatedOwner(Material material) => AnimatedOwnerCache.Remove(material);

        private static string OwnerSuffix(Material material)
        {
            var state = AnimatedOwnerCache.GetValue(material, _ => new AnimatedOwnerMetadata());
            int dirty = EditorUtility.GetDirtyCount(material);
            if (state.DirtyCount != dirty)
            { state.Suffix = ShaderOptimizer.GetRenamedPropertySuffix(material); state.DirtyCount = dirty; }
            return state.Suffix;
        }

        private bool OwnsAnimatedState(Material material) => material != null && MyShaderUI.Materials.Contains(material)
            && material.HasProperty(MaterialProperty.name);

        private void UpdateIsAnimatedFromTag()
        {
            _isAnimatedStateResolved = true;
            bool previousPlain = HasPlainAnimatedOwners, previousRenamed = HasRenamedAnimatedOwners;
            bool plain = false, renamed = false, mixed = false, allAnimated = true, allDuplicated = true;
            int count = 0; string firstTag = null;
            foreach (var target in MaterialProperty.targets)
            {
                var material = target as Material;
                if (!OwnsAnimatedState(material)) continue;
                bool duplicate;
                string tag = ReadOwnerAnimatedTag(material, out duplicate);
                if (!IsAnimatable) tag = "";
                if (count++ == 0) firstTag = tag; else mixed |= tag != firstTag;
                plain |= tag != "" && tag != "2";
                renamed |= tag == "2";
                allAnimated &= tag != "";
                allDuplicated &= duplicate;
            }
            if (count > 0 && allDuplicated) IsAnimatable = false;
            HasPlainAnimatedOwners = plain;
            HasRenamedAnimatedOwners = renamed;
            HasMixedAnimatedOwners = mixed;
            AllOwnersAnimated = count > 0 && allAnimated;
            IsAnimated = plain || renamed;
            IsRenaming = renamed;
            // Most properties are unanimated. Avoid building thousands of unused hover strings.
            AnimatedOwnersTooltip = "";
            if (IsAnimated)
            {
                var tooltip = new System.Text.StringBuilder();
                foreach (var target in MaterialProperty.targets)
                {
                    var material = target as Material;
                    if (!OwnsAnimatedState(material)) continue;
                    string tag = IsAnimatable ? GetOwnerAnimatedTag(material) : "";
                    if (tooltip.Length > 0) tooltip.Append('\n');
                    tooltip.Append(material.name).Append(": ").Append(tag == "" ? "Not animated" : tag == "2" ? "RA — Animated and renamed" : "A — Animated");
                }
                AnimatedOwnersTooltip = tooltip.ToString();
            }
            if (previousPlain != plain || previousRenamed != renamed)
                (Parent as ShaderGroup)?.SetAnimatedDescendantStateDirty();
        }

        internal string GetOwnerAnimatedTag(Material material) => ReadOwnerAnimatedTag(material, out _);

        private string ReadOwnerAnimatedTag(Material material, out bool duplicate)
        {
            string name = MaterialProperty.name;
            string suffix = OwnerSuffix(material);
            // A retained source slot beside its optimized duplicate is not the animated slot.
            duplicate = suffix.Length > 0 && material.HasProperty(name + "_" + suffix);
            if (duplicate) return "";
            if (suffix.Length > 0 && name.EndsWith("_" + suffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - suffix.Length - 1);
            string tag = ShaderOptimizer.GetAnimatedTag(material, name);
            if (tag == "2" && !ShaderOptimizer.CanRename(name)) return "1";
            return tag == "0" ? "" : tag;
        }

        static readonly string[] s_animatedTexturePaths = { "_ST.x", "_ST.y", "_ST.z", "_ST.w" };
        static readonly string[] s_animatedColorPaths = { ".r", ".g", ".b", ".a" };
        static readonly string[] s_animatedVectorPaths = { ".x", ".y", ".z", ".w" };
        static readonly string[] s_animatedFloatPaths = { "" };

        private string _keyframePathsName, _keyframePathsSuffix;
        private string[] _keyframePaths;

        // Marks whether the clip being previewed or recorded on a property in the Shader UI.
        internal bool HasAnimationKeyframes(Renderer renderer)
        {
            if (renderer == null || MaterialProperty == null) return false;
            string name = MaterialProperty.name;
            string suffix = IsRenaming && !MyShaderUI.IsLockedMaterial && !string.IsNullOrEmpty(MyShaderUI.RenamedPropertySuffix) ? MyShaderUI.RenamedPropertySuffix : null;
            if (_keyframePaths == null || _keyframePathsName != name || _keyframePathsSuffix != suffix)
            {
                string[] components;
                switch (MaterialProperty.GetPropertyType())
                {
                    case ShaderPropertyType.Texture: components = s_animatedTexturePaths; break;
                    case ShaderPropertyType.Color: components = s_animatedColorPaths; break;
                    case ShaderPropertyType.Vector: components = s_animatedVectorPaths; break;
                    default: components = s_animatedFloatPaths; break;
                }
                var paths = new List<string>(components.Length * 2);
                foreach (string component in components)
                {
                    paths.Add("material." + name + component);
                }
                if (suffix != null)
                {
                    foreach (string component in components)
                    {
                        paths.Add("material." + name + "_" + suffix + component);
                    }
                }
                _keyframePaths = paths.ToArray();
                _keyframePathsName = name;
                _keyframePathsSuffix = suffix;
            }
            foreach (string path in _keyframePaths)
            {
                if (AnimationMode.IsPropertyAnimated(renderer, path)) return true;
            }
            return false;
        }

        private void AutomaticAnimatedMarking()
        {
            if (!Config.Instance.autoMarkPropertiesAnimated || !MyShaderUI.IsInAnimationMode || !IsAnimatable || IsAnimated) return;
            if (HasAnimationKeyframes(MyShaderUI.ActiveRenderer)) SetAnimated(true, false);
        }

        private ShaderProperty _retainedProjectionSource;
        private Material[] _retainedProjectionOwners;
        private Shader[] _retainedProjectionShaders;
        private int[] _retainedProjectionVersions;
        private int _retainedProjectionRevision;
        private bool _retainedProjectionPrepared;

        internal ShaderProperty CreateRetainedProjection(ShaderPart owner, Material[] targets)
        {
            var value = MaterialEditor.GetMaterialProperty(targets, MaterialProperty.name);
            ShaderProperty projection = this is ShaderTextureProperty texture
                ? new ShaderTextureProperty(MyShaderUI, value, _content.text, _optionsRaw, texture.hasScaleOffset, ThryPropertyIndex)
                : new ShaderProperty(MyShaderUI, value, _content.text, _optionsRaw, ThryPropertyIndex);
            projection.MyShader = SectionLock.GetSourceShader(targets[0].shader) ?? targets[0].shader;
            projection.ShaderPropertyIndex = projection.MyShader.FindPropertyIndex(value.name);
            projection.UseMaterialEditorFor(targets);
            projection.ThryPropertyIndex = -1;
            projection.SetParent(owner);
            projection.SetHidden(IsHidden);
            projection.AdditionalDefaultCheckProperties = AdditionalDefaultCheckProperties;
            projection._retainedProjectionSource = this;
            projection._retainedProjectionOwners = targets;
            // Originals, so a section-lock swap doesn't invalidate the projection.
            projection._retainedProjectionShaders = targets.Select(m => SectionLock.GetSourceShader(m.shader)).ToArray();
            projection._retainedProjectionVersions = targets.Select(EditorUtility.GetDirtyCount).ToArray();
            projection._retainedProjectionRevision = MyShaderUI.RetainedRevision;
            projection.EnsureOptionsInitialized();
            projection.PrepareRetainedMetadata();
            return projection;
        }

        internal bool RefreshRetainedProjection(Renderer[] renderers = null)
        {
            if (_retainedProjectionSource == null) return true;
            if (_retainedProjectionRevision != MyShaderUI.RetainedRevision) return false;
            bool changed = false;
            for (int i = 0; i < _retainedProjectionOwners.Length; i++)
            {
                var material = _retainedProjectionOwners[i];
                if (material == null || SectionLock.GetSourceShader(material.shader) != _retainedProjectionShaders[i] || !MyShaderUI.Materials.Contains(material)
                    || !_retainedProjectionSource.MaterialProperty.targets.Contains(material)) return false;
                int dirty = EditorUtility.GetDirtyCount(material);
                changed |= _retainedProjectionVersions[i] != dirty;
                _retainedProjectionVersions[i] = dirty;
            }
            // Animation previews and play-mode animators write property blocks without
            // dirtying the material. Re-read while one is active and once after it ends.
            bool prepare = AnimationMode.InAnimationMode() || (renderers != null && RetainedAnimation.RequiresPreparation(renderers));
            bool wasPrepared = _retainedProjectionPrepared;
            _retainedProjectionPrepared = prepare;
            if (changed || prepare || wasPrepared)
            {
                var value = MaterialEditor.GetMaterialProperty(_retainedProjectionOwners, MaterialProperty.name);
                if (renderers != null) RetainedAnimation.Prepare(new[] { value }, renderers);
                if (!IsAnimatable) value.applyPropertyCallback = null;
                SetRetainedProperty(value);
                RefreshRetainedAnimatedState();
            }
            return true;
        }

        private readonly Dictionary<Shader, DrawerAttribute[]> _retainedKeywordDeclarations = new Dictionary<Shader, DrawerAttribute[]>();
        private int _retainedKeywordRevision = -1;
        internal void RetainedValueChanged()
        {
            var affectedMaterials = MaterialProperty.targets.OfType<Material>()
                .Where(m => MyShaderUI.Materials.Contains(m) && m.HasProperty(MaterialProperty.name)).Distinct().ToArray();
            if (_retainedKeywordRevision != MyShaderUI.RetainedRevision)
            { _retainedKeywordDeclarations.Clear(); _retainedKeywordRevision = MyShaderUI.RetainedRevision; }
            foreach (var material in affectedMaterials)
            {
                Shader shader = SectionLock.GetSourceShader(material.shader) ?? material.shader;
                if (!_retainedKeywordDeclarations.TryGetValue(shader, out var attributes))
                {
                    int index = shader.FindPropertyIndex(MaterialProperty.name);
                    attributes = index < 0 ? Array.Empty<DrawerAttribute>() : shader.GetPropertyAttributes(index)
                        .Select(a => new DrawerAttribute(a)).Where(a => a.Name == "TextureKeyword" || a.Name == "ThryToggle" || a.Name == "ThryToggleUI").ToArray();
                    _retainedKeywordDeclarations.Add(shader, attributes);
                }
                foreach (var attribute in attributes)
                {
                    string keyword;
                    bool enabled;
                    if (attribute.Name == "TextureKeyword")
                    {
                        keyword = attribute.Args.Length == 0 || string.IsNullOrEmpty(attribute.Args[0])
                            ? "PROP_" + MaterialProperty.name.TrimStart('_').ToUpperInvariant() : attribute.Args[0];
                        enabled = material.GetTexture(MaterialProperty.name) != null;
                    }
                    else
                    {
                        if (attribute.Args.Length == 0 || attribute.Args[0].Equals("true", StringComparison.OrdinalIgnoreCase)
                            || attribute.Args[0].Equals("false", StringComparison.OrdinalIgnoreCase)) continue;
                        keyword = attribute.Args[0];
                        enabled = Math.Abs(material.GetNumber(MaterialProperty)) > .001f;
                    }
                    if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
                }
            }
            RaisePropertyValueChanged();
            ExecuteOnValueActions(affectedMaterials);
            AutomaticAnimatedMarking();
            // RetainedMaterialModel propagates ancestor links once after these
            // callbacks, with reloadUI:false so a drag keeps its live controls.
        }

        internal void PrepareRetainedMetadata()
        {
            PrepareRetainedDeclaration();
            EnsureAnimatedStateResolved();
        }

        internal void PrepareRetainedDeclaration()
        {
            if (MaterialProperty == null || MyShader == null || ShaderPropertyIndex < 0) return;
            var declaration = RetainedDeclaration ?? RetainedShaderDeclarations.Get(MyShader, ShaderPropertyIndex);
            IsAnimatable &= declaration.IsAnimatable;
            if (declaration.Keyword != null) SetKeyword(declaration.Keyword);
        }
    }

}
