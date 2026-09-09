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
        protected bool _doCustomDrawLogic = false;
        protected bool _doForceIntoOneLine = false;
        protected bool _doDrawTwoFields = false;

        //Done for e.g. Vectors cause they draw in 2 lines for some fucking reasons
        private bool _doCustomHeightOffset { set; get; } = false;
        private float _customHeightOffset { set; get; } = 0;

        public string Keyword { private set; get; }

        protected List<MaterialPropertyDrawer> _customDecorators = new List<MaterialPropertyDrawer>();
        protected Rect[] _customDecoratorRects;
        protected MaterialPropertyDrawer _drawer = null;
        private InspectorPopup _enumPopup;
        private GUIContent[] _enumNames;
        private int[] _enumValues;

        bool _needsDrawerInitlization = true;
        bool _isAnimatedStateResolved = false;
        internal bool HasPlainAnimatedOwners { get; private set; }
        internal bool HasRenamedAnimatedOwners { get; private set; }
        internal bool HasMixedAnimatedOwners { get; private set; }
        internal bool AllOwnersAnimated { get; private set; }
        internal string AnimatedOwnersTooltip { get; private set; } = "";

        public ShaderProperty(ShaderEditor shaderEditor, string propertyIdentifier, int xOffset, string displayName, string tooltip, int propertyIndex) : base(propertyIdentifier, xOffset, displayName, tooltip, shaderEditor)
        {
            this.ShaderPropertyIndex = propertyIndex;
        }

        public ShaderProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, int xOffset, string optionsRaw, bool forceOneLine, int propertyIndex) : base(shaderEditor, materialProperty, xOffset, displayName, optionsRaw, propertyIndex)
        {
            this._doCustomDrawLogic = false;
            this._doForceIntoOneLine = forceOneLine;
        }

        protected override void InitOptions()
        {
            base.InitOptions();
            this._doDrawTwoFields = Options.reference_property != null;
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

        public enum DrawerType{ None, Toggle, Slider }
        [PublicAPI]
        public DrawerType GetDrawerType()
        {
            if (_drawer == null) return DrawerType.None;
            if (_drawer.GetType().Name.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) != -1) return DrawerType.Toggle;
            if (_drawer.GetType().Name.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) != -1) return DrawerType.Slider;
            return DrawerType.None;
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

        private static ShaderProperty _activeProperty;
        public static void RegisterDrawer(MaterialPropertyDrawer drawer)
        {
            if(_activeProperty == null) return;
            _activeProperty._drawer = drawer;
        }
        public static void RegisterDecorator(MaterialPropertyDrawer drawer)
        {
            if(_activeProperty == null) return;
            if(_activeProperty._customDecorators.Contains(drawer) == false)
            {
                _activeProperty._customDecorators.Add(drawer);
                _activeProperty._customDecoratorRects = new Rect[_activeProperty._customDecorators.Count];
            }
        }
        public static void DisallowAnimation()
        {
            if(_activeProperty == null) return;
            _activeProperty.IsAnimatable = false;
        }

        void InitializeDrawers()
        {
            if(!_needsDrawerInitlization) return;
            if(MaterialProperty == null) return;
            _needsDrawerInitlization = false;
            // Makes Drawers and Decorators Register themself
            _activeProperty = this;
            MyMaterialEditor.GetPropertyHeight(MaterialProperty, MaterialProperty.displayName);
            _activeProperty = null;

            if (MaterialProperty.GetPropertyType() == ShaderPropertyType.Vector && _doForceIntoOneLine == false)
            {
                this._doCustomHeightOffset = _drawer == null;
                this._customHeightOffset = -EditorGUIUtility.singleLineHeight;
            }
            UpdateIsAnimatedFromTag();
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
            return tag == "0" ? "" : tag;
        }
        protected override void GUILocaleEditing(bool isInHeader)
        {
            if(!isInHeader && _doEditLocale && ShaderEditor.Active.Locale.EditInUI && MaterialProperty != null)
            {
                EditorGUI.BeginChangeCheck();
                Rect translationRect = EditorGUILayout.GetControlRect();
                translationRect.x += 15;
                translationRect.width -= 15;
                string newTranslation = EditorGUI.DelayedTextField(translationRect, new GUIContent(MaterialProperty.name, ShaderEditor.GetMaterialPropertyDisplayNameWithoutOptions(MaterialProperty)), _content.text);
                if(EditorGUI.EndChangeCheck())
                {
                    _content.text = newTranslation;
                    Content = _content;
                    ShaderEditor.Active.Locale.Set(MaterialProperty, newTranslation);
                    ShaderEditor.Active.Locale.Save();
                }
            }
        }

        protected override void DrawInternal(GUIContent content, Rect? rect = null, bool useEditorIndent = false, bool isInHeader = false)
        {
            if(content == null)
                content = GUIContent.none;

            _drawnAsLabellessInline = rect != null && string.IsNullOrEmpty(content.text) && !isInHeader;

            MyShaderUI.CurrentProperty = this;
            InitializeDrawers();
            PreDraw();
            if (MyShaderUI.IsLockedMaterial)
                EditorGUI.BeginDisabledGroup(!(IsAnimatable && (IsAnimated || IsRenaming)) && !IsExemptFromLockedDisabling);

            using (useEditorIndent ? null : new GUILib.PropertyIndentScope(XOffset, alignToColumn: rect == null))
            {

            if (_customDecoratorRects != null && _doCustomDrawLogic)
            {
                for (int i = 0; i < _customDecoratorRects.Length; i++)
                {
                    float decoratorHeight = _customDecorators[i].GetPropertyHeight(MaterialProperty, content.text, MyMaterialEditor);
                    if (decoratorHeight > 0)
                        _customDecoratorRects[i] = EditorGUILayout.GetControlRect(false, GUILayout.Height(decoratorHeight));
                    else
                        _customDecoratorRects[i] = new Rect();
                }
            }

            EditorGUI.BeginChangeCheck();

            // Own the mixed-value flag for every branch below that draws one value control.
            //
            // MaterialProperty.BeginProperty is the ONLY place Unity sets EditorGUI.showMixedValue for a
            // material property - and on 2022.1+ ShaderEditor.Draw wraps the whole property tree in
            // UnityHelper.DetourMaterialPropertyVariantIcon, which detours BeginProperty and EndProperty to
            // empty stubs to suppress Unity's material-variant chrome. The mixed-value flag is collateral:
            // nothing sets it, so every property Unity draws by default (floats, ranges, colors, vectors)
            // rendered a multi-selection as though all the materials agreed. Drawers are a second hole -
            // BeginProperty does not run for them even un-detoured, and several of the drawers here never
            // set the flag themselves (ByteSlider, MultiSlider, ThryIntRange, Ramp4, Curve, ...).
            //
            // Setting it here covers both, and EndProperty being a stub is why the reset below matters.
            // Where Unity is left to set it (2021 and older), it sets the same value.
            //
            // Skipped for _doCustomDrawLogic (textures, render queue, VRC fallback, GI, instancing): those
            // draw several sub-controls with independent mixed states and set the flag per control already.
            bool drawAsMixedValue = !_doCustomDrawLogic && MaterialProperty != null && MaterialProperty.hasMixedValue;
            if (drawAsMixedValue) EditorGUI.showMixedValue = true;

            if (_doCustomDrawLogic)
            {
                DrawDefault();
            }
            else if (_doDrawTwoFields)
            {
                Rect r = useEditorIndent 
                    ? EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight)
                    : GUILib.GetPropertyRect(XOffset, EditorGUIUtility.singleLineHeight);
                float labelWidth = (r.width - EditorGUIUtility.labelWidth) / 2;
                r.width -= labelWidth;
                DrawAlignedProperty(r, content);

                r.x += r.width;
                r.width = labelWidth;
                float prevLabelW = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 0;

                if (MyShaderUI.IsLockedMaterial) EditorGUI.EndDisabledGroup();
                MyShaderUI.PropertyDictionary[Options.reference_property].Draw(r, new GUIContent());
                if (MyShaderUI.IsLockedMaterial) EditorGUI.BeginDisabledGroup(false);
                EditorGUIUtility.labelWidth = prevLabelW;
            }
            else if (_doForceIntoOneLine)
            {
                Rect r = useEditorIndent
                    ? EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight)
                    : GUILib.GetPropertyRect(XOffset, EditorGUIUtility.singleLineHeight);
                DrawAlignedProperty(r, content);
            }
            else if (_doCustomHeightOffset)
            {
                float propHeight = MyMaterialEditor.GetPropertyHeight(this.MaterialProperty, content.text) + _customHeightOffset;
                Rect r = useEditorIndent ? EditorGUILayout.GetControlRect(false, propHeight) : GUILib.GetPropertyRect(XOffset, propHeight);
                DrawAlignedProperty(r, content);
            }
            else if (rect != null)
            {
                // Custom Drawing for Range, because it doesn't draw correctly if inside the big texture property
                if (_drawer == null && MaterialProperty.GetPropertyType() == ShaderPropertyType.Range)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.showMixedValue = MaterialProperty.hasMixedValue;
                    float newSliderValue = EditorGUI.Slider(rect.Value, content, MaterialProperty.floatValue, MaterialProperty.rangeLimits.x, MaterialProperty.rangeLimits.y);
                    EditorGUI.showMixedValue = false;
                    if (EditorGUI.EndChangeCheck()) MaterialProperty.floatValue = newSliderValue;
                }
                else
                {
                    DrawAlignedProperty(rect.Value, content);
                }
            }
            else
            {
                float propHeight = MyMaterialEditor.GetPropertyHeight(this.MaterialProperty, content.text);
                Rect r;
                if (useEditorIndent)
                    r = EditorGUILayout.GetControlRect(false, propHeight == -2 ? 0 : propHeight);
                else
                    r = GUILib.GetPropertyRect(XOffset, propHeight);
                DrawAlignedProperty(r, content);
            }

            if (_customDecorators != null && _doCustomDrawLogic)
            {
                for (int i = 0; i < _customDecorators.Count; i++)
                {
                    _customDecorators[i].OnGUI(_customDecoratorRects[i], MaterialProperty, content, MyMaterialEditor);
                }
            }

            if(EditorGUI.EndChangeCheck())
            {
                Undo.SetCurrentGroupName($"Modify {content.text} of {ShaderEditor.Active.TargetName}");
                RaisePropertyValueChanged();
                ExecuteOnValueActions(ShaderEditor.Active.Materials);
                AutomaticAnimatedMarking();
            }

            } // end PropertyIndentScope

            if (rect == null) DrawingData.LastGuiObjectRect = GUILayoutUtility.GetLastRect();
            else DrawingData.LastGuiObjectRect = rect.Value;
            if (MyShaderUI.IsLockedMaterial)
                EditorGUI.EndDisabledGroup();
        }

        private void AutomaticAnimatedMarking()
        {
            if (Config.Instance.autoMarkPropertiesAnimated && MyShaderUI.ActiveRenderer != null && MyShaderUI.IsInAnimationMode && IsAnimatable && !IsAnimated)
            {
                if (MaterialProperty.GetPropertyType() == ShaderPropertyType.Texture ?
                AnimationMode.IsPropertyAnimated(MyShaderUI.ActiveRenderer, "material." + MaterialProperty.name + "_ST.x") :
                    AnimationMode.IsPropertyAnimated(MyShaderUI.ActiveRenderer, "material." + MaterialProperty.name))
                    SetAnimated(true, false);
            }
        }

#if UNITY_2021_3_OR_NEWER
        private ShaderProperty _retainedProjectionSource;
        private Material[] _retainedProjectionOwners;
        private Shader[] _retainedProjectionShaders;
        private int[] _retainedProjectionVersions;
        private int _retainedProjectionRevision;

        internal ShaderProperty CreateRetainedProjection(ShaderPart owner, Material[] targets)
        {
            var value = MaterialEditor.GetMaterialProperty(targets, MaterialProperty.name);
            ShaderProperty projection = this is ShaderTextureProperty texture
                ? new ShaderTextureProperty(MyShaderUI, value, _content.text, XOffset, _optionsRaw, texture.hasScaleOffset, false, ThryPropertyIndex)
                : new ShaderProperty(MyShaderUI, value, _content.text, XOffset, _optionsRaw, false, ThryPropertyIndex);
            projection.MyShader = targets[0].shader;
            projection.ShaderPropertyIndex = targets[0].shader.FindPropertyIndex(value.name);
            projection.MyMaterialEditor = MyShaderUI.GetMaterialEditor(targets);
            projection.ThryPropertyIndex = -1;
            projection.SetParent(owner);
            projection.SetHidden(IsHidden);
            projection.AdditionalDefaultCheckProperties = AdditionalDefaultCheckProperties;
            projection._retainedProjectionSource = this;
            projection._retainedProjectionOwners = targets;
            projection._retainedProjectionShaders = targets.Select(m => m.shader).ToArray();
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
                if (material == null || material.shader != _retainedProjectionShaders[i] || !MyShaderUI.Materials.Contains(material)
                    || !_retainedProjectionSource.MaterialProperty.targets.Contains(material)) return false;
                int dirty = EditorUtility.GetDirtyCount(material);
                changed |= _retainedProjectionVersions[i] != dirty;
                _retainedProjectionVersions[i] = dirty;
            }
            if (changed || AnimationMode.InAnimationMode())
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
                if (!_retainedKeywordDeclarations.TryGetValue(material.shader, out var attributes))
                {
                    int index = material.shader.FindPropertyIndex(MaterialProperty.name);
                    attributes = index < 0 ? Array.Empty<DrawerAttribute>() : material.shader.GetPropertyAttributes(index)
                        .Select(a => new DrawerAttribute(a)).Where(a => a.Name == "TextureKeyword" || a.Name == "ThryToggle" || a.Name == "ThryToggleUI").ToArray();
                    _retainedKeywordDeclarations.Add(material.shader, attributes);
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
                        if (attribute.Args.Length == 0 || attribute.Args[0] == "true" || attribute.Args[0] == "false") continue;
                        keyword = attribute.Args[0];
                        enabled = Math.Abs(material.GetNumber(MaterialProperty)) > .001f;
                    }
                    if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
                }
            }
            RaisePropertyValueChanged();
            ExecuteOnValueActions(affectedMaterials);
            AutomaticAnimatedMarking();
            GlobalLinker.OnPropertyChanged(this);
        }

        internal void PrepareRetainedMetadata()
        {
            if (MaterialProperty == null || MyShader == null || ShaderPropertyIndex < 0) return;
            var options = Options;
            EnsureAnimatedStateResolved();
            var attributes = MyShader.GetPropertyAttributes(ShaderPropertyIndex);
            if (Array.Exists(attributes, a => a.StartsWith("DoNotAnimate", StringComparison.Ordinal)
                || a.StartsWith("ThryStencil", StringComparison.Ordinal) || a.StartsWith("ThryShaderOptimizer", StringComparison.Ordinal)
                || a.StartsWith("TextureKeyword", StringComparison.Ordinal))) IsAnimatable = false;
            foreach(var attribute in attributes.Select(a=>new DrawerAttribute(a)))
                if((attribute.Name=="ThryToggle" || attribute.Name=="ThryToggleUI") && attribute.Args.Length>0 && attribute.Args[0]!="true" && attribute.Args[0]!="false")
                { SetKeyword(attribute.Args[0]); IsAnimatable=false; }
        }

#endif
        protected virtual void PreDraw() { }

        private void DrawAlignedProperty(Rect position, GUIContent label)
        {
            var type = MaterialProperty.GetPropertyType();
            string drawerName = _drawer?.GetType().Name;
            if (drawerName == "MaterialEnumDrawer" || drawerName == "MaterialKeywordEnumDrawer")
            {
                if (_enumPopup == null)
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var typeInfo = _drawer.GetType();
                    _enumNames = typeInfo.GetField(drawerName == "MaterialEnumDrawer" ? "names" : "keywords", flags)?.GetValue(_drawer) as GUIContent[];
                    _enumValues = typeInfo.GetField("values", flags)?.GetValue(_drawer) as int[];
                    _enumPopup = new InspectorPopup();
                }
                if (_enumNames != null)
                {
                    int selected = _enumValues == null ? (int)MaterialProperty.GetNumber() : Array.IndexOf(_enumValues, (int)MaterialProperty.GetNumber());
                    EditorGUI.BeginChangeCheck();
                    bool mixed = EditorGUI.showMixedValue;
                    EditorGUI.showMixedValue = MaterialProperty.hasMixedValue;
                    selected = _enumPopup.Draw(position, label, selected, _enumNames);
                    EditorGUI.showMixedValue = mixed;
                    if (EditorGUI.EndChangeCheck() && selected >= 0)
                    {
                        MyMaterialEditor.RegisterPropertyChangeUndo(label.text);
                        MaterialProperty.SetNumber(_enumValues == null ? selected : _enumValues[selected]);
                        if (drawerName == "MaterialKeywordEnumDrawer") _drawer.Apply(MaterialProperty);
                    }
                    return;
                }
            }
            if (_drawer == null && type == ShaderPropertyType.Vector && GUILib.ValueColumnX > 0
                && (_customDecorators == null || _customDecorators.Count == 0))
            {
                EditorGUI.BeginChangeCheck();
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = MaterialProperty.hasMixedValue;
                Vector4 value = GUILib.VectorField(position, label, MaterialProperty.vectorValue, 4);
                EditorGUI.showMixedValue = mixed;
                if (EditorGUI.EndChangeCheck())
                {
                    MyMaterialEditor.RegisterPropertyChangeUndo(label.text);
                    MaterialProperty.vectorValue = value;
                }
                return;
            }
            if (drawerName == "MaterialToggleUIDrawer" || drawerName == "MaterialToggleDrawer" || drawerName == "MaterialToggleOffDrawer")
            {
                position.y += Mathf.Max(0, (position.height - 16) / 2);
                position.height = Mathf.Min(position.height, 16);
            }
            if (GUILib.ValueColumnX > 0 && _drawer == null && (_customDecorators == null || _customDecorators.Count == 0)
                && (type == ShaderPropertyType.Vector || type == ShaderPropertyType.Range) && !string.IsNullOrEmpty(label.text))
            {
                // MaterialEditor's vector and range controls reset labelWidth to Unity's default.
                // Give them an already separated value rect so they cannot move the column boundary.
                position = EditorGUI.PrefixLabel(position, label);
                label = GUIContent.none;
            }
            MyMaterialEditor.ShaderProperty(position, MaterialProperty, label);
        }

        protected virtual void DrawDefault() { }

        // Where inside LastGuiObjectRect does the property's label actually sit?
        // When _doCustomDrawLogic is true Thry reserves a separate rect for each decorator above,
        // so the rect is just the property body and the label is at its top → 0.
        // Otherwise Unity stacks every decorator ([Space], [Header], [ThryDecalPositioning], plus
        // any other built-in or third-party decorator) above the drawer's body inside the rect.
        // The drawer's body occupies the BOTTOM `drawerOwnHeight` of the rect, so the offset to
        // where the label gets drawn is `rect.height - drawerOwnHeight`. This works whether the
        // property uses a Unity drawer (Vector2, Ramp4, etc.) or Unity's default per-type
        // rendering, and it implicitly covers decorators that don't register with Thry.
        protected override float GetLabelYOffsetWithinRect()
        {
            if (_doCustomDrawLogic) return 0f;
            float drawerOwnHeight;
            if (_drawer != null)
            {
                string labelText = _content != null ? _content.text : (MaterialProperty != null ? MaterialProperty.displayName : "");
                drawerOwnHeight = _drawer.GetPropertyHeight(MaterialProperty, labelText, MyMaterialEditor);
            }
            else
            {
                // No Unity drawer: Thry either renders the body in a single line (Vector squeeze,
                // _doForceIntoOneLine, _doDrawTwoFields) or falls back to Unity's per-type default
                // which is one line for the simple types we care about here.
                drawerOwnHeight = EditorGUIUtility.singleLineHeight;
            }
            float offset = DrawingData.LastGuiObjectRect.height - drawerOwnHeight;
            return Mathf.Max(0f, offset);
        }

        public override void FindUnusedTextures(List<string> unusedList, bool isEnabled)
        {
            if (isEnabled && Options.condition_enable != null)
            {
                isEnabled &= Options.condition_enable.Test();
            }
            if (!isEnabled && MaterialProperty != null && MaterialProperty.GetPropertyType() == ShaderPropertyType.Texture && MaterialProperty.textureValue != null)
            {
                unusedList.Add(MaterialProperty.name);
            }
        }

        public override bool Search(string searchTerm, List<ShaderGroup> foundHeaders, bool isParentInSearch = false)
        {
            return isParentInSearch
                || this.Content.text.IndexOf(searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0
                || this.MaterialProperty?.name.IndexOf(searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

}
