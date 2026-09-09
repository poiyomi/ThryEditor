using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public partial class CrossEditor : EditorWindow
    {
        public static CrossEditor GetInstance()
        {
            CrossEditor window = GetWindow(typeof(CrossEditor)) as CrossEditor;
            window.name = "Cross Shader Editor";

            return window;
        }

        [MenuItem("Assets/Thry/Materials/Add to Cross Shader Editor", false , 400)]
        private static void OpenInCrossShaderEditor()
        {
            List<Material> materials = ShaderOptimizer.FindMaterials(ShaderOptimizer.GetSelectedFolders());
            materials.AddRange(Selection.objects.Where(o => o is Material).Cast<Material>());

            GetInstance().UpdateTargets(materials, true);
        }

        [MenuItem("Assets/Thry/Materials/Add to Cross Shader Editor", true, 400)]
        private static bool OpenInCrossShaderEditorValidation()
        {
            return Selection.objects.Any(o => o is Material) || ShaderOptimizer.GetSelectedFolders().Any();
        }

        [MenuItem("GameObject/Thry/Materials/Open All in Cross Shader Editor", false, 10)]
        private static void OpenAllInCrossShaderEditor()
        {
            GetInstance().UpdateTargets(Selection.gameObjects.SelectMany(o => o.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials));
        }

        List<Material> _materialList = new List<Material>();
        List<Material> _targets = new List<Material>();
        HashSet<Material> _incompatibleMaterials = new HashSet<Material>();
        HashSet<Material> _disabledMaterials = new HashSet<Material>();
        Dictionary<Material,Shader> _targetShaders = new Dictionary<Material, Shader>();
        ShaderEditor _shaderEditor = null;
        MaterialEditor _materialEditor = null;
        MaterialProperty[] _materialProperties = null;
        Vector2 _scrollPosition = Vector2.zero;
        bool _showMaterials = true;
        // Dirty count per target as of the last build, so an edit made outside this window can be noticed.
        Dictionary<Material, int> _targetDirtyCounts = new Dictionary<Material, int>();
        bool _isStale = false;

        public void UpdateTargets(IEnumerable<Material> materials, bool add = false)
        {
            _materialList = (add ?
                _materialList.Concat(materials) : // add
                materials) // replace
                .Distinct().ToList(); // deduplicate

            UpdateTargets();
        }

        private void UpdateTargets()
        {
            PruneInvalidTargets();
            _incompatibleMaterials = new HashSet<Material>(
                _materialList.Where(t => t != null && t.shader != null && !t.shader.IsBroken() && !ShaderHelper.IsShaderUsingThryEditor(t)));
            _disabledMaterials.IntersectWith(_materialList);
            _targets = _materialList.Where(t => t != null && t.shader != null && !t.shader.IsBroken() && !_incompatibleMaterials.Contains(t) && !_disabledMaterials.Contains(t)).ToList();

            DiscardShaderEditor();
#if UNITY_2021_3_OR_NEWER
            CreateGUI();
#endif
        }

        /// <summary>
        /// The only way a shader editor leaves this window. Nulling the field alone left its undo
        /// subscription and its MaterialEditors behind, and each of those pinned the full property tree of
        /// every target in memory - after enough rebuilds the whole editor stayed sluggish, whatever was
        /// selected, until the next domain reload.
        /// </summary>
        private void DiscardShaderEditor()
        {
            if (_shaderEditor != null) _shaderEditor.Release();
            _shaderEditor = null;
            if (_materialEditor != null) DestroyImmediate(_materialEditor);
            _materialEditor = null;
            _materialProperties = null;
        }

        private void PruneInvalidTargets()
        {
            // Keep intentional empty ObjectField rows, but remove destroyed Unity objects.
            _materialList.RemoveAll(m => m == null && !ReferenceEquals(m, null));
            _disabledMaterials.RemoveWhere(m => m == null);
            _incompatibleMaterials.RemoveWhere(m => m == null);
            if (_targets.RemoveAll(m => m == null || m.shader == null || m.shader.IsBroken()) == 0) return;
            _targetShaders.Clear(); _targetDirtyCounts.Clear();
            DiscardShaderEditor();
        }

        private void OnDestroy()
        {
            DiscardShaderEditor();
        }

        private void OnDisable()
        {
#if UNITY_2021_3_OR_NEWER
            // Detach scheduled retained views before destroying their material editor.
            rootVisualElement.Clear();
#endif
            DiscardShaderEditor();
        }

        /// <summary>
        /// The property array is built once and reused, unlike the inspector which is handed a fresh one by
        /// Unity every frame, so an edit made to a target from anywhere else would otherwise never show up
        /// here. Rebuilding is far too expensive to do per repaint - roughly 185ms for three materials,
        /// since it re-fetches every one of ~4900 merged properties - but noticing that a rebuild is needed
        /// costs nothing, so the two are split: check constantly, rebuild only when it matters.
        /// </summary>
        private bool HaveTargetsChangedExternally()
        {
            if (_targetDirtyCounts.Count != _targets.Count) return true;
            foreach (Material m in _targets)
            {
                if (m == null) return true;
                int dirtyCount;
                if (!_targetDirtyCounts.TryGetValue(m, out dirtyCount)) return true;
                if (EditorUtility.GetDirtyCount(m) != dirtyCount) return true;
            }
            return false;
        }

        private void RecordTargetDirtyCounts()
        {
            _targetDirtyCounts.Clear();
            foreach (Material m in _targets)
                if (m != null) _targetDirtyCounts[m] = EditorUtility.GetDirtyCount(m);
            _isStale = false;
        }

        // Deliberately deferred to focus rather than rebuilt the moment a target goes dirty. Editing a
        // material in the inspector marks it dirty on every drag frame, and rebuilding each time would cost
        // 185ms a frame - the exact stutter this is meant to avoid. Nothing is looking at the stale values
        // while the window is unfocused anyway.
        private void OnFocus()
        {
            if (HaveTargetsChangedExternally()) _isStale = true;
            Repaint();
        }

        private void OnGUI()
        {
#if UNITY_2021_3_OR_NEWER
            if (rootVisualElement.childCount > 0) return;
#endif
            // Unlike the inspector's container, a plain window does not reset this between passes, and the
            // material list below is drawn before the shader editor gets a chance to clean up after itself.
            EditorGUI.showMixedValue = false;
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            // Drawers are allowed to end the GUI pass early by throwing out of GUIUtility.ExitGUI - the lock
            // button does exactly that, because locking swaps the shader and every control drawn afterwards
            // would be mismatched. Unwinding past EndScrollView would leave IMGUI's layout group stack
            // unbalanced, which throws an index out of range on the following repaint and leaves the window
            // stuck. The inspector never hit this since it does not wrap the shader GUI in its own group.
            try
            {
                _showMaterials = EditorGUILayout.Foldout(_showMaterials, "Materials");

                EditorGUI.BeginChangeCheck();
                DrawMaterials();

                // Check if targets have changed
                bool didShadersChange = EditorGUI.EndChangeCheck();
                foreach (Material m in _materialList)
                {
                    if (m == null || // Material is null
                        _targetShaders.ContainsKey(m) && _targetShaders[m] == m.shader) // Shader hasn't changed
                        continue;

                    didShadersChange = true;
                    _targetShaders[m] = m.shader;
                }

                if (didShadersChange) UpdateTargets();

                // Free every frame; the rebuild it may trigger is not, which is why it only acts while this
                // window has focus. Elsewhere it just records that a refresh is owed.
                if (Event.current.type == EventType.Layout && HaveTargetsChangedExternally())
                    _isStale = true;

                if (_isStale && EditorWindow.focusedWindow == this && Event.current.type == EventType.Layout)
                    DiscardShaderEditor();

                DrawShaderEditor();

                // An edit made through this window's own controls dirties the target just like an external
                // one does, but it went through the very property array being drawn, so that array is already
                // current. Without re-baselining here the check above mistook every one of this window's
                // edits for an external one and rebuilt the whole editor on the next Layout - once per drag
                // frame for a slider, which brought the window down to about one frame a second.
                //
                // Anything that changes a target during this window's own GUI pass came from this window, so
                // the counts as they stand at the end of the pass are the new baseline. External edits happen
                // between passes and are still caught by the Layout check at the top of the next one. Skipped
                // while stale so a refresh owed to an unfocused window is not forgotten by a repaint.
                if (!_isStale) RecordTargetDirtyCounts();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        // List of materials, remove button next to each
        // Add and Remove All buttons at bottom
        private void DrawMaterials()
        {
            if (!_showMaterials) return;

            for (int i = 0; i < _materialList.Count; i++) DrawMaterial(i);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(15);
                if (GUILayout.Button("Add", GUILayout.Width(100))) _materialList.Add(null);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Remove All", GUILayout.Width(100))) _materialList.Clear();
            }
        }

        private void DrawMaterial(int i)
        {
            Material current = _materialList[i];
            bool isIncompatible = current != null && _incompatibleMaterials.Contains(current);
            bool isDisabled = current != null && _disabledMaterials.Contains(current);
            Color prevColor = GUI.backgroundColor;
            if (isIncompatible) GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(15);

                using (new EditorGUI.DisabledGroupScope(current == null || isIncompatible))
                {
                    bool enabled = current != null && !isDisabled && !isIncompatible;
                    bool newEnabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(15));
                    if (newEnabled != enabled && current != null && !isIncompatible)
                    {
                        if (newEnabled) _disabledMaterials.Remove(current);
                        else _disabledMaterials.Add(current);
                        UpdateTargets();
                    }
                }

                Material material = (Material)EditorGUILayout.ObjectField(_materialList[i], typeof(Material), false);

                if (material != _materialList[i])
                {
                    if (_materialList.Contains(material)) material = null;

                    _materialList[i] = material;
                }

                if (GUILayout.Button("Remove", GUILayout.Width(60))) _materialList.RemoveAt(i);
            }

            GUI.backgroundColor = prevColor;
            if (isIncompatible)
                EditorGUILayout.HelpBox($"'{current.shader.name}' is not compatible with the Cross Shader Editor.", MessageType.None);
        }

        private void DrawShaderEditor()
        {
            if (_targets.Count == 0) return;

            // Create shader editor
            CreateShaderEditor();
            if (_shaderEditor == null || _materialEditor == null) return;

            // Seperator
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            bool wideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.wideMode = true;
            _shaderEditor.OnGUI(_materialEditor, _materialProperties);
            EditorGUIUtility.wideMode = wideMode;
        }

        // A property name plus which declaration of that name this is.
        //
        // A shader may declare the same property more than once: Poiyomi's rim lighting declares
        // _RimBlur in both its Poiyomi style and its LilToon style section, each gated on _RimStyle, and
        // Unity returns one entry per declaration. Keying the collection below on the name alone
        // collapsed those into a single entry, so the property could only be placed in one of its
        // sections and went missing from the other. Keying on the occurrence keeps them apart while
        // staying unique within a shader, which is what the intersect/union merge relies on.
        private readonly struct PropertyOccurrence : IEquatable<PropertyOccurrence>
        {
            public readonly string Name;
            public readonly int Occurrence;
            private readonly int _type, _dimension, _editFlags;
            private readonly Vector2 _range;

            public PropertyOccurrence(MaterialProperty property, int occurrence)
            {
                Name = property.name;
                Occurrence = occurrence;
                _type = (int)property.GetPropertyType();
                _dimension = property.GetPropertyType() == UnityEngine.Rendering.ShaderPropertyType.Texture ? (int)property.textureDimension : 0;
                _editFlags = (int)(property.flags & MaterialProperty.PropFlags.NonModifiableTextureData);
                _range = property.GetPropertyType() == UnityEngine.Rendering.ShaderPropertyType.Range ? property.rangeLimits : Vector2.zero;
            }

            public bool Equals(PropertyOccurrence other) => Occurrence == other.Occurrence && Name == other.Name
                && _type == other._type && _dimension == other._dimension && _editFlags == other._editFlags && _range.Equals(other._range);
            public override bool Equals(object obj) => obj is PropertyOccurrence other && Equals(other);
            public override int GetHashCode() => unchecked(((Name?.GetHashCode() ?? 0) * 397) ^ Occurrence);
        }

        // Declaration order, with repeated declarations numbered so they stay distinct.
        private static PropertyOccurrence[] GetPropertyOccurrences(Material material)
        {
            MaterialProperty[] properties = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
            Dictionary<string, int> counts = new Dictionary<string, int>();
            PropertyOccurrence[] occurrences = new PropertyOccurrence[properties.Length];
            for (int i = 0; i < properties.Length; i++)
            {
                string name = properties[i].name;
                counts.TryGetValue(name, out int seen);
                counts[name] = seen + 1;
                // Different native types/dimensions cannot share a MaterialProperty. Different
                // ranges get separate rows so neither owner's limits silently win. Compatible
                // declarations retain the first shader's presentation, with keyword behavior
                // resolved separately from each owner's declaration when an edit commits.
                occurrences[i] = new PropertyOccurrence(properties[i], seen);
            }
            return occurrences;
        }

        // Group boundaries cannot be merged as ordinary property names: two shaders may
        // contain different descendants between a shared opening and closing marker.
        private sealed class PropertyTree
        {
            internal PropertyOccurrence? Property;
            internal PropertyOccurrence? End;
            internal readonly List<PropertyTree> Children = new List<PropertyTree>();
        }

        private static PropertyTree ReadPropertyTree(ShaderEditor shaderEditor, Material material, PropertyOccurrence[] occurrences)
        {
            var root = new PropertyTree();
            var preamble = new PropertyTree(); root.Children.Add(preamble);
            var stack = new Stack<PropertyTree>(); stack.Push(root); stack.Push(preamble);
            var properties = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
            for (int i = 0; i < properties.Length; i++)
            {
                var type = shaderEditor.GetPropertyType(properties[i]);
                bool opens = type == ShaderEditor.ThryPropertyType.header || type == ShaderEditor.ThryPropertyType.header_start
                    || type == ShaderEditor.ThryPropertyType.group_start || type == ShaderEditor.ThryPropertyType.section_start
                    || type == ShaderEditor.ThryPropertyType.subsection_start;
                bool closes = type == ShaderEditor.ThryPropertyType.header_end || type == ShaderEditor.ThryPropertyType.group_end
                    || type == ShaderEditor.ThryPropertyType.section_end || type == ShaderEditor.ThryPropertyType.subsection_end;
                if (closes)
                {
                    if (stack.Count <= 1) throw new InvalidOperationException("Unmatched inspector group end in " + material.shader.name + ": " + properties[i].name);
                    stack.Pop().End = occurrences[i];
                    continue;
                }
                if (type == ShaderEditor.ThryPropertyType.header && stack.Count > 1) stack.Pop();
                var node = new PropertyTree { Property = occurrences[i] };
                stack.Peek().Children.Add(node);
                if (opens) stack.Push(node);
            }
            return root;
        }

        private static void MergePropertyTrees(PropertyTree target, PropertyTree source)
        {
            int insertion = 0;
            foreach (var child in source.Children)
            {
                int existing = target.Children.FindIndex(n => Nullable.Equals(n.Property, child.Property));
                if (existing < 0)
                {
                    target.Children.Insert(insertion, child);
                    insertion++;
                }
                else
                {
                    var match = target.Children[existing];
                    MergePropertyTrees(match, child);
                    insertion = existing + 1;
                }
            }
        }

        private static IEnumerable<PropertyOccurrence> FlattenPropertyTree(PropertyTree tree)
        {
            if (tree.Property.HasValue) yield return tree.Property.Value;
            foreach (var child in tree.Children)
                foreach (var property in FlattenPropertyTree(child)) yield return property;
            if (tree.End.HasValue) yield return tree.End.Value;
        }

        private void CreateShaderEditor()
        {
            PruneInvalidTargets();
            if (_targets.Count == 0) return;
            if (_shaderEditor != null) return;

            _shaderEditor = new ShaderEditor(){ IsCrossEditor = true };
            _materialEditor = Editor.CreateEditor(_targets.ToArray()) as MaterialEditor;
            _materialProperties = CollectProperties(_shaderEditor, _targets.ToArray());

            // This array is now the snapshot everything draws from, so baseline the dirty counts against it.
            RecordTargetDirtyCounts();
        }

        // Shared by the cross-shader window and retained inspectors with mixed shader targets.
        // Callers own target validation and decide when this declaration snapshot needs rebuilding.
        internal static MaterialProperty[] CollectProperties(ShaderEditor shaderEditor, Material[] targets)
        {
            // group targets by shader, take one material per shader
            IEnumerable<Material> materialsToSearchProperties = targets.GroupBy(t => t.shader).Select(g => g.First());
            // get properties for each shader, keeping declaration order rather than leaning on the
            // enumeration order of a set, since the merge below is order sensitive
            var merged = new PropertyTree();
            Dictionary<Shader, HashSet<PropertyOccurrence>> shaderProperties = new Dictionary<Shader, HashSet<PropertyOccurrence>>();
            foreach (Material material in materialsToSearchProperties)
            {
                PropertyOccurrence[] occurrences = GetPropertyOccurrences(material);
                MergePropertyTrees(merged, ReadPropertyTree(shaderEditor, material, occurrences));
                shaderProperties[material.shader] = new HashSet<PropertyOccurrence>(occurrences);
            }
            var propertiesOrdered = FlattenPropertyTree(merged).ToList();
            // For each property get all materials, whos shader has this property. A material only counts
            // for the second declaration of a name if its own shader declares that name twice as well.
            Dictionary<PropertyOccurrence, Material[]> propertyMaterials = new Dictionary<PropertyOccurrence, Material[]>();
            foreach (PropertyOccurrence property in propertiesOrdered)
            {
                propertyMaterials[property] = targets.Where(t => shaderProperties[t.shader].Contains(property)).ToArray();
            }
            // Get MaterialProperties of all materials. Repeated declarations resolve to the same
            // underlying property, exactly as they do in the normal inspector.
            return propertiesOrdered.Select(p => MaterialEditor.GetMaterialProperty(propertyMaterials[p], p.Name)).ToArray();
        }
    }
}
