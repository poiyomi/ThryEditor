using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public class CrossEditor : EditorWindow
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
            _incompatibleMaterials = new HashSet<Material>(
                _materialList.Where(t => t != null && !t.shader.IsBroken() && !ShaderHelper.IsShaderUsingThryEditor(t)));
            _disabledMaterials.IntersectWith(_materialList);
            _targets = _materialList.Where(t => t != null && !t.shader.IsBroken() && !_incompatibleMaterials.Contains(t) && !_disabledMaterials.Contains(t)).ToList();

            DiscardShaderEditor();
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
        }

        private void OnDestroy()
        {
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

            public PropertyOccurrence(string name, int occurrence)
            {
                Name = name;
                Occurrence = occurrence;
            }

            public bool Equals(PropertyOccurrence other) => Occurrence == other.Occurrence && Name == other.Name;
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
                occurrences[i] = new PropertyOccurrence(name, seen);
            }
            return occurrences;
        }

        private void CreateShaderEditor()
        {
            if (_shaderEditor != null) return;

            _shaderEditor = new ShaderEditor(){ IsCrossEditor = true };
            _materialEditor = Editor.CreateEditor(_targets.ToArray()) as MaterialEditor;

            // group targets by shader, take one material per shader
            IEnumerable<Material> materialsToSearchProperties = _targets.GroupBy(t => t.shader).Select(g => g.First());
            // get properties for each shader, keeping declaration order rather than leaning on the
            // enumeration order of a set, since the merge below is order sensitive
            List<PropertyOccurrence[]> propertiesPerShader = new List<PropertyOccurrence[]>();
            Dictionary<Shader, HashSet<PropertyOccurrence>> shaderProperties = new Dictionary<Shader, HashSet<PropertyOccurrence>>();
            foreach (Material material in materialsToSearchProperties)
            {
                PropertyOccurrence[] occurrences = GetPropertyOccurrences(material);
                propertiesPerShader.Add(occurrences);
                shaderProperties[material.shader] = new HashSet<PropertyOccurrence>(occurrences);
            }
            // create intersection of all properties
            List<PropertyOccurrence> propertiesOrdered = propertiesPerShader.Aggregate((a, b) => a.Intersect(b).ToArray()).ToList();
            // expand the intersection to be a union, but add each property after the occurence of their predecessor
            foreach (PropertyOccurrence[] properties in propertiesPerShader)
            {
                int index = 0;
                foreach (PropertyOccurrence property in properties)
                {
                    if (!propertiesOrdered.Contains(property))
                    {
                        if (index == 0)
                            propertiesOrdered.Insert(0, property);
                        else
                            propertiesOrdered.Insert(propertiesOrdered.IndexOf(properties[index - 1]) + 1, property);
                    }
                    index++;
                }
            }
            // For each property get all materials, whos shader has this property. A material only counts
            // for the second declaration of a name if its own shader declares that name twice as well.
            Dictionary<PropertyOccurrence, Material[]> propertyMaterials = new Dictionary<PropertyOccurrence, Material[]>();
            foreach (PropertyOccurrence property in propertiesOrdered)
            {
                propertyMaterials[property] = _targets.Where(t => shaderProperties[t.shader].Contains(property)).ToArray();
            }
            // Get MaterialProperties of all materials. Repeated declarations resolve to the same
            // underlying property, exactly as they do in the normal inspector.
            _materialProperties = propertiesOrdered.Select(p => MaterialEditor.GetMaterialProperty(propertyMaterials[p], p.Name)).ToArray();

            // This array is now the snapshot everything draws from, so baseline the dirty counts against it.
            RecordTargetDirtyCounts();
        }
    }
}