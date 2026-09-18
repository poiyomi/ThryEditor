#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Thry.ThryEditor;

namespace Thry
{
    public partial class ShaderEditor
    {
        internal int RetainedRevision { get; private set; }
        internal ShaderGroup RetainedRoot => _mainGroup;
        internal ShaderProperty RetainedOptimizer => ShaderOptimizerProperty;
        internal ShaderProperty RetainedPreset => InShaderPresetsProperty;
        internal IEnumerable<FooterButton> RetainedFooters => _footers;
        internal RenderQueueProperty RetainedQueue => _renderQueueProperty;
        internal VRCFallbackProperty RetainedFallback => _vRCFallbackProperty;
        private RetainedCrossSelectionProperties _retainedValueProvider;
        private int _retainedValueRevision = -1;
        private Dictionary<string, ShaderPart[]> _retainedPartsByName;
        private bool _retainedPreparedAnimation;
        private MaterialEditor _retainedHeaderEditor;
        private Material[] _retainedHeaderTargets;
        private int[] _retainedHeaderVersions;
        private double _retainedHeaderEditTime = double.NegativeInfinity;

        // The native header displays material metadata, not the thousands of
        // shader values in its SerializedObject. Let its host coalesce that
        // unused snapshot during our own rapid value edits. Any external dirty
        // change immediately invalidates this hint; the host also checks metadata.
        public bool CanDeferRetainedHeaderRefresh(MaterialEditor editor, UnityEngine.Object[] targets)
        {
            if (editor != _retainedHeaderEditor || _retainedHeaderTargets == null
                || targets == null || targets.Length != _retainedHeaderTargets.Length
                || EditorApplication.timeSinceStartup - _retainedHeaderEditTime >= .15) return false;
            for (int i = 0; i < _retainedHeaderTargets.Length; i++)
                if (_retainedHeaderTargets[i] == null
                    || targets[i] != _retainedHeaderTargets[i]
                    || EditorUtility.GetDirtyCount(_retainedHeaderTargets[i]) != _retainedHeaderVersions[i]) return false;
            return true;
        }

        internal void RecordRetainedHeaderValueEdit(MaterialEditor editor, Material[] targets)
        {
            _retainedHeaderEditor = editor;
            _retainedHeaderTargets = targets;
            if (_retainedHeaderVersions == null || _retainedHeaderVersions.Length != targets.Length)
                _retainedHeaderVersions = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++) _retainedHeaderVersions[i] = EditorUtility.GetDirtyCount(targets[i]);
            _retainedHeaderEditTime = EditorApplication.timeSinceStartup;
        }

        internal bool PrepareRetained(MaterialEditor editor, Renderer[] renderers, Func<MaterialProperty[]> propertyProvider = null)
        {
            if (!RetainedMaterialModel.HasValidTargets(editor)) return false;
            Active = this;
            ReleaseOrphanedEditors(this);
            var valueProvider = propertyProvider?.Target as RetainedCrossSelectionProperties;
            bool prepareAnimation = RetainedAnimation.RequiresPreparation(renderers);
            // Property-block reads modify the snapshots in place. Re-read while
            // blocks are active and once when they disappear, restoring asset values.
            if (prepareAnimation || _retainedPreparedAnimation) valueProvider?.InvalidateValues();
            _retainedPreparedAnimation = prepareAnimation;
            var previousProperties = Properties;
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.NativeValues))
                Properties = propertyProvider != null ? propertyProvider() : MaterialEditor.GetMaterialProperties(editor.targets);
            bool valuesChanged = valueProvider == null || valueProvider != _retainedValueProvider
                || valueProvider.ValueRevision != _retainedValueRevision || !ReferenceEquals(previousProperties, Properties);
            string patchedName = valueProvider != null && valueProvider == _retainedValueProvider
                && valueProvider.ValueRevision == _retainedValueRevision + 1 ? valueProvider.PatchedPropertyName : null;
            _retainedValueProvider = valueProvider;
            _retainedValueRevision = valueProvider?.ValueRevision ?? -1;
            // Providers may discover a secondary material's shader change and request a rebuild.
            bool rebuild = _isFirstOnGUICall || _doReloadNextDraw || Shader != ((Material)editor.target).shader;
            if (valuesChanged || rebuild) materialPropertyDictionary = null;
            if (rebuild)
            {
                InitEditorData(editor);
                InitlizeThryUI();
                _doReloadNextDraw = false;
                // IMGUI initializes a part's options the first time it draws. Retained inspectors
                // never call Draw, so tooltips, offsets and stored foldout states would stay unread.
                foreach (var part in ShaderParts) part.EnsureOptionsInitialized();
                // The model resolves animation after refreshing owner metadata below.
                // Reading every tag here would duplicate that complete pass, and would
                // run before declaration attributes have ruled out animation.
                foreach (var property in ShaderParts.OfType<ShaderProperty>()) property.PrepareRetainedDeclaration();
                _retainedPartsByName = ShaderParts.Where(p => p.ThryPropertyIndex >= 0)
                    .GroupBy(p => Properties[p.ThryPropertyIndex].name).ToDictionary(g => g.Key, g => g.ToArray());
                RetainedRevision++;
            }
            IsInAnimationMode = AnimationMode.InAnimationMode();
            IsLockedMaterial = Materials.Any(m => m.IsLocked());
            ActiveRenderer = renderers.FirstOrDefault();
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.AnimationPreparation))
                RetainedAnimation.Prepare(Properties, renderers);
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.PropertyReferences))
            {
                if (valuesChanged || rebuild || prepareAnimation)
                {
                    IEnumerable<ShaderPart> parts = ShaderParts;
                    if (!rebuild && !prepareAnimation && patchedName != null
                        && _retainedPartsByName != null && _retainedPartsByName.TryGetValue(patchedName, out var patchedParts))
                        parts = patchedParts;
                    foreach (var part in parts)
                    {
                        part.UpdatedMaterialPropertyReference();
                        if (part is ShaderProperty property && !property.IsAnimatable && property.MaterialProperty != null)
                            property.MaterialProperty.applyPropertyCallback = null;
                    }
                }
            }
            // Shader swap actions used to run at the end of the IMGUI event pass.
            // Consume the pending actions here so retained inspectors preserve that one-shot behavior.
            if (_didSwapToShader)
            {
                var actions = _onSwapToActions;
                _onSwapToActions = null;
                _didSwapToShader = false;
                if (actions != null)
                    foreach (var action in actions) action.Perform(Materials);
            }
            IsFirstCall = false;
            return rebuild;
        }

        internal void ActivateRetained() { Active = this; IsInAnimationMode = AnimationMode.InAnimationMode(); }
    }
}

namespace Thry.ThryEditor
{
    internal static class RetainedAnimation
    {
        // The public overload finds renderers through the current IMGUI container. Retained views
        // supply their inspector's renderers explicitly to the same Unity recording implementation.
        private static readonly MethodInfo PrepareMethod = typeof(MaterialEditor).GetMethod(
            "PrepareMaterialPropertiesForAnimationMode", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(MaterialProperty[]), typeof(Renderer[]), typeof(bool) }, null);
        private static bool _failed;
        internal static bool IsSupported => PrepareMethod != null && !_failed;

        internal static bool RequiresPreparation(Renderer[] renderers) => renderers.Length > 0
            && (AnimationMode.InAnimationMode() || (renderers[0] != null && renderers[0].HasPropertyBlock()));

        internal static void Prepare(MaterialProperty[] properties, Renderer[] renderers)
        {
            // Unity allocates a block and walks every property even for an empty
            // renderer block. Outside animation that walk cannot change any values.
            if (!RequiresPreparation(renderers)) return;
            if (!IsSupported) return;
            try { PrepareMethod.Invoke(null, new object[] { properties, renderers, true }); }
            catch (TargetInvocationException) { _failed = true; }
            catch (ArgumentException) { _failed = true; }
            catch (MemberAccessException) { _failed = true; }
        }
    }

    /// <summary>Shared property transactions for all UI Toolkit material controls.</summary>
    internal sealed class RetainedMaterialModel
    {
        private static readonly Type PropertyHandlerType = typeof(MaterialEditor).Assembly.GetType("UnityEditor.MaterialPropertyHandler");
        private static readonly MethodInfo GetPropertyHandler = PropertyHandlerType?.GetMethod("GetHandler",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Shader), typeof(string) }, null);
        private static readonly PropertyInfo PropertyDrawer = PropertyHandlerType?.GetProperty("propertyDrawer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo DrawersDisabled = typeof(MaterialEditor).Assembly.GetType("UnityEditor.EditorMaterialUtility")
            ?.GetProperty("disableApplyMaterialPropertyDrawers", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        internal readonly MaterialEditor Editor;
        internal readonly ShaderEditor Shader;
        internal readonly Material[] SelectedMaterials;
        internal Renderer[] Renderers = Array.Empty<Renderer>();
        internal Func<MaterialProperty[]> PropertyProvider;
        internal event Action Changed;
        private readonly Dictionary<Material, int> _animatedMaterialVersions = new Dictionary<Material, int>();
        private readonly RetainedTextureKeywords _textureKeywords = new RetainedTextureKeywords();
        private readonly RetainedAnimationMetadata _animationMetadata = new RetainedAnimationMetadata();
        internal int AnimationRefreshCount { get; private set; }
        internal bool DeferSummaryRefresh { get; private set; }
        internal RetainedMaterialModel(MaterialEditor editor, ShaderEditor shader)
        {
            Editor = editor; Shader = shader;
            SelectedMaterials = editor.targets.OfType<Material>().Where(m => m != null).ToArray();
        }
        internal void ReleaseCaches() => _animationMetadata.Dispose();

        // Undo and inspector teardown can leave a live editor holding destroyed native targets.
        // Unity's material-property API does not safely reject those references.
        internal static bool HasLiveTargets(MaterialEditor editor)
        {
            if (editor == null || !(editor.target is Material material) || material == null) return false;
            var targets = editor.targets;
            if (targets.Length == 0) return false;
            foreach (var target in targets)
                if (!(target is Material m) || m == null || m.shader == null) return false;
            return true;
        }

        internal static bool HasValidTargets(MaterialEditor editor)
        {
            if (!HasLiveTargets(editor)) return false;
            foreach (Material material in editor.targets)
                if (Helpers.ShaderHelper.IsShaderUsingThryEditor(material)) return true;
            return false;
        }

        internal void Refresh(bool forceAnimatedState = false)
        {
            if (!HasValidTargets(Editor)) return;
            var readTargets = Editor.targets.OfType<Material>().ToArray();
            var readVersions = readTargets.Select(EditorUtility.GetDirtyCount).ToArray();
            bool rebuilt;
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.NativeRefresh))
                rebuilt = Shader.PrepareRetained(Editor, Renderers, PropertyProvider);
            var targets = Shader.Materials.Where(m => m != null).ToArray();
            var changedTargets = targets.Where(m => rebuilt || !_animatedMaterialVersions.ContainsKey(m)
                || _animatedMaterialVersions[m] != EditorUtility.GetDirtyCount(m)).ToArray();
            bool animationChanged;
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.AnimationMetadata))
                animationChanged = (!DeferSummaryRefresh || rebuilt || forceAnimatedState) && _animationMetadata.Update(targets, Editor);
            bool changed = rebuilt || forceAnimatedState || animationChanged || _animatedMaterialVersions.Count != targets.Length || changedTargets.Length > 0;
            if (!changed) { StampSnapshotsIfUnchanged(readTargets, readVersions); return; }
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.TextureKeywords))
                _textureKeywords.Synchronize(Shader, changedTargets);
            // Unlike IMGUI, retained inspectors do not receive UndoRedoPerformed GUI events.
            // Refresh tag state after material changes without rebuilding or opening sections.
            if (rebuilt || forceAnimatedState || animationChanged)
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.AnimationIndicators))
            {
                // Parent tags and names can change without dirtying their variant.
                foreach (var material in targets) ShaderProperty.InvalidateRetainedAnimatedOwner(material);
                foreach (var property in Shader.ShaderParts.OfType<ShaderProperty>()) property.RefreshRetainedAnimatedState();
                AnimationRefreshCount++;
            }
            _animatedMaterialVersions.Clear();
            foreach (var material in targets) _animatedMaterialVersions[material] = EditorUtility.GetDirtyCount(material);
            StampSnapshotsIfUnchanged(readTargets, readVersions);
        }

        private void StampSnapshotsIfUnchanged(Material[] targets, int[] versions)
        {
            // Shader-swap actions and keyword repair can run after the native read.
            // Do not certify an older property snapshot against their newer version.
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] == null || versions[i] != EditorUtility.GetDirtyCount(targets[i])) return;
            RetainedPropertyDefaults.MarkSnapshotsFresh(Shader);
        }

        internal bool CanEdit(ShaderPart part)
        {
            if (part == null || Editor == null) return false;
            if (part is ShaderProperty projected && !projected.RefreshRetainedProjection(Renderers)) return false;
            Shader.ActivateRetained();
            if (Shader.IsInAnimationMode && Renderers.Length > 0 && !RetainedAnimation.IsSupported) return false;
            if (part.MaterialProperty != null)
            {
                if ((part.MaterialProperty.flags & MaterialProperty.PropFlags.NonModifiableTextureData) != 0) return false;
                bool hasOwner = false;
                foreach (var target in part.MaterialProperty.targets)
                {
                    if (!(target is Material owner) || owner == null || !Shader.Materials.Contains(owner)
                        || !owner.HasProperty(part.MaterialProperty.name)) continue;
                    hasOwner = true;
                    if (!part.IsExemptFromLockedDisabling && owner.IsLocked()
                        && !(part.IsAnimatable && part is ShaderProperty property && !string.IsNullOrEmpty(property.GetOwnerAnimatedTag(owner)))) return false;
#if UNITY_2022_1_OR_NEWER
                    if (owner.IsPropertyLockedByAncestor(part.MaterialProperty.name)) return false;
#endif
                }
                if (!hasOwner) return false;
            }
            for (var parent = part.Parent; parent != null; parent = parent.Parent)
            {
                // Header enable references sit outside the block they enable, so they
                // must remain usable while that block is disabled.
                string name = part.MaterialProperty?.name;
                if (parent is ShaderGroup && name != null && (parent.Options.reference_property == name
                    || (parent.Options.reference_properties != null && parent.Options.reference_properties.Contains(name)))) continue;
                if (parent.Options.condition_enable != null && !parent.Options.condition_enable.Test()) return false;
                if (parent is ShaderGroup group && !group.RetainedChildrenEnabled) return false;
            }
            return part.Options.condition_enable == null || part.Options.condition_enable.Test();
        }

        internal IEnumerable<Material> Owners(ShaderPart part) => part.MaterialProperty == null
            ? Enumerable.Empty<Material>() : part.MaterialProperty.targets.OfType<Material>()
                .Where(m => m != null && Shader.Materials.Contains(m) && m.HasProperty(part.MaterialProperty.name));

        internal void Edit(ShaderProperty property, Action<MaterialProperty> mutation, bool perMaterial = false)
            => EditCore(property, mutation, perMaterial, false);

        // Only callers whose mutation writes this property alone may use this path.
        internal void EditSingleProperty(ShaderProperty property, Action<MaterialProperty> mutation, bool perMaterial = false)
            => EditCore(property, mutation, perMaterial, true);

        private void EditCore(ShaderProperty property, Action<MaterialProperty> mutation, bool perMaterial, bool singleProperty)
        {
            bool previousDeferral = DeferSummaryRefresh;
            DeferSummaryRefresh = true;
            try
            {
            using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.Total))
            {
                bool nativeUndo;
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.Prepare))
                {
                    if (!HasValidTargets(Editor)) return;
                    Refresh();
                    if (!CanEdit(property)) return;
                    Shader.ActivateRetained();
                    // MaterialProperty's native setter already records a complete
                    // Undo snapshot for its owners. Recording them again here adds
                    // full-material diff work to every slider/color update. Keep
                    // the explicit record where a callback can replace that setter,
                    // or the operation can affect owners outside its native write.
                    nativeUndo = singleProperty && !perMaterial && !AnimationMode.InAnimationMode()
                        && property.MaterialProperty.applyPropertyCallback == null
                        && property.MaterialProperty.targets.Length == SelectedMaterials.Length;
                    if (!nativeUndo) Editor.RegisterPropertyChangeUndo(property.Content.text);
                    Shader.CurrentProperty = property;
                }
                var cachedProperties = PropertyProvider?.Target as RetainedCrossSelectionProperties;
                bool patch = singleProperty && !Renderers.Any(r => r != null && r.HasPropertyBlock()) && cachedProperties != null
                    && cachedProperties.CanPatchProperty(property.MaterialProperty);
                var editVersions = patch || nativeUndo ? SelectedMaterials.Select(EditorUtility.GetDirtyCount).ToArray() : null;
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.Mutation))
                {
                    if (perMaterial)
                    {
                        foreach (var material in property.MaterialProperty.targets.OfType<Material>()
                            .Where(m => m != null && Shader.Materials.Contains(m)))
                        {
                            if (!material.HasProperty(property.MaterialProperty.name)) continue;
                            var p = MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { material }, property.MaterialProperty.name);
                            RetainedAnimation.Prepare(new[] { p }, Renderers.Where(r => r != null && r.sharedMaterials.Contains(material)).ToArray());
                            if (!property.IsAnimatable) p.applyPropertyCallback = null;
                            mutation(p);
                        }
                    }
                    else mutation(property.MaterialProperty);
                }
                // A clamped/no-op setter can skip Unity's native Undo registration,
                // while an authored callback still changes another value below.
                // Nothing has changed yet in this case, so record before callbacks.
                if (nativeUndo)
                {
                    bool changed = false;
                    for (int i = 0; i < SelectedMaterials.Length; i++)
                        if (EditorUtility.GetDirtyCount(SelectedMaterials[i]) != editVersions[i]) { changed = true; break; }
                    if (!changed) Editor.RegisterPropertyChangeUndo(property.Content.text);
                }
                if (patch)
                {
                    cachedProperties.PatchProperty(property.MaterialProperty.name);
                    RetainedPropertyDefaults.RecordValueEdit(Shader, property.MaterialProperty.name, SelectedMaterials, editVersions);
                }
                // A slider changes one property. Unity's bulk helper reads every shader
                // property and applies every drawer, which dominates a Pro edit transaction.
                // Resolve each shader's own drawer, retaining the bulk fallback for Unity
                // versions that do not expose the internal handler API.
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.ApplyDrawers))
                    ApplyEditedPropertyDrawer(property.MaterialProperty);
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.PropertyCallbacks)) property.RetainedValueChanged();
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.LinkedMaterials))
                    for(var parent = property.Parent as ShaderGroup; parent != null; parent = parent.Parent as ShaderGroup)
                    {
                        if (parent.MaterialProperty == null) continue;
                        parent.UpdateLinkedMaterials();
                        GlobalLinker.OnSectionChanged(parent, reloadUI: false);
                    }
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.EditorInvalidation)) Editor.PropertiesChanged();
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.FinalRefresh)) Refresh();
                using (RetainedEditMetrics.Measure(RetainedEditMetrics.Phase.Synchronize)) Changed?.Invoke();
                if (singleProperty && !perMaterial && !AnimationMode.InAnimationMode())
                    Shader.RecordRetainedHeaderValueEdit(Editor, SelectedMaterials);
            }
            }
            finally { DeferSummaryRefresh = previousDeferral; }
        }

        private void ApplyEditedPropertyDrawer(MaterialProperty property)
        {
            if (DrawersDisabled != null && (bool)DrawersDisabled.GetValue(null)) return;
            foreach (var group in property.targets.OfType<Material>()
                .Where(m => m != null && Shader.Materials.Contains(m) && m.HasProperty(property.name)).GroupBy(m => m.shader))
            {
                var targets = group.Cast<UnityEngine.Object>().ToArray();
                if (GetPropertyHandler == null || PropertyDrawer == null || DrawersDisabled == null)
                {
                    MaterialEditor.ApplyMaterialPropertyDrawers(targets);
                    continue;
                }
                var handler = GetPropertyHandler.Invoke(null, new object[] { group.Key, property.name });
                var drawer = handler == null ? null : PropertyDrawer.GetValue(handler) as MaterialPropertyDrawer;
                if (drawer == null) continue;
                // Projections can span different shaders or a subset of the selection.
                // Apply uses a fresh value snapshot with only this shader's owners.
                drawer.Apply(MaterialEditor.GetMaterialProperty(targets, property.name));
            }
        }

        internal void Number(ShaderProperty property, float value) => EditSingleProperty(property, p => p.SetNumber(value));
        internal void VectorComponent(ShaderProperty property, int component, float value, bool textureTransform = false)
            => EditSingleProperty(property, p => { var vector = textureTransform ? p.textureScaleAndOffset : p.vectorValue; vector[component] = value;
                if (textureTransform) p.textureScaleAndOffset = vector; else p.vectorValue = vector; }, true);

        internal void Mutate(string label, Action<Material> mutation)
        {
            if (!HasValidTargets(Editor)) return;
            Shader.ActivateRetained();
            Undo.RecordObjects(Editor.targets, label);
            foreach (var material in Shader.Materials) { mutation(material); EditorUtility.SetDirty(material); }
            Editor.PropertiesChanged(); Refresh(); Changed?.Invoke();
        }

        /// <summary>
        /// Toggles a section's foldout. A persistent foldout stores its state in the section's own
        /// material property, so opening one is a material edit and has to be undoable and saved.
        /// </summary>
        internal void SetExpanded(ShaderGroup group, bool expanded)
        {
            if (!HasValidTargets(Editor)) return;
            Shader.ActivateRetained();
            // Searching and animation recording both keep the foldout in memory only, so writing
            // an undo entry for them would put an empty step on the stack.
            if (!group.PersistsExpanded || Shader.IsInSearchMode || Shader.IsInAnimationMode)
            { group.RetainedExpanded = expanded; return; }
            Editor.RegisterPropertyChangeUndo(group.Content.text);
            group.RetainedExpanded = expanded;
            Editor.PropertiesChanged();
            Refresh();
            Changed?.Invoke();
        }

        internal void Notify() { if (!HasValidTargets(Editor)) return; Refresh(true); Changed?.Invoke(); }
    }
}
#endif
