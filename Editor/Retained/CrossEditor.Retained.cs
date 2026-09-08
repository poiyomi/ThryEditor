#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class CrossEditor
    {
        public void CreateGUI()
        {
            PruneInvalidTargets();
            var root = rootVisualElement;
            root.Clear(); RetainedWindow.Style(root);
            root.RemoveFromClassList("thry-window");
            root.style.paddingLeft = 12; root.style.paddingRight = 12;
            titleContent = new GUIContent("Cross Shader Editor");
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; root.Add(scroll);
            var materials = new Foldout { text = "Materials · " + _materialList.Count, value = _showMaterials };
            materials.AddToClassList("thry-cross-materials");
            materials.RegisterValueChangedCallback(e => { if (e.target == materials) _showMaterials = e.newValue; });
            scroll.Add(materials);
            for (int i = 0; i < _materialList.Count; i++)
            {
                int index = i;
                var material = _materialList[i];
                var row = new VisualElement(); row.AddToClassList("thry-components"); materials.Add(row);
                bool compatible = material != null && !_incompatibleMaterials.Contains(material);
                var enabled = new Toggle { value = compatible && !_disabledMaterials.Contains(material) }; enabled.SetEnabled(compatible); row.Add(enabled);
                enabled.RegisterValueChangedCallback(e => { if (e.newValue) _disabledMaterials.Remove(material); else _disabledMaterials.Add(material); UpdateTargets(); });
                var field = new ObjectField { objectType = typeof(Material), allowSceneObjects = false, value = material }; field.style.flexGrow = 1; row.Add(field);
                field.RegisterValueChangedCallback(e => { var next = e.newValue as Material; if (next != material && _materialList.Contains(next)) return; _materialList[index] = next; UpdateTargets(); });
                row.Add(new Button(() => { _materialList.RemoveAt(index); UpdateTargets(); }) { text = "Remove" });
                if (material != null && !compatible) materials.Add(new HelpBox((material.shader != null ? material.shader.name : "Missing shader") + " does not use Thry.", HelpBoxMessageType.Info));
            }
            var actions = new VisualElement(); actions.AddToClassList("thry-components"); materials.Add(actions);
            actions.Add(new Button(() => { _materialList.Add(null); UpdateTargets(); }) { text = "Add material" });
            actions.Add(new Button(() => UpdateTargets(Selection.objects.OfType<Material>(), true)) { text = "Add selected" });
            actions.Add(new Button(() => { _materialList.Clear(); UpdateTargets(); }) { text = "Clear" });
            scroll.RegisterCallback<DragUpdatedEvent>(e => { if (DragAndDrop.objectReferences.OfType<Material>().Any()) DragAndDrop.visualMode = DragAndDropVisualMode.Copy; });
            scroll.RegisterCallback<DragPerformEvent>(e => { var added = DragAndDrop.objectReferences.OfType<Material>().ToArray(); if (added.Length == 0) return; DragAndDrop.AcceptDrag(); UpdateTargets(added, true); e.StopPropagation(); });
            if (_targets.Count == 0) return;
            CreateShaderEditor();
            if (_materialEditor == null || _shaderEditor == null) return;
            var view = new MaterialInspectorView(_materialEditor, () => { }, shaderOverride: _shaderEditor, propertyProvider: () => _materialProperties);
            scroll.Add(view);
            view.schedule.Execute(() =>
            {
                if (_targets.Any(m => m == null || m.shader == null)) { UpdateTargets(); return; }
                bool shadersChanged = _targets.Any(m => !_targetShaders.ContainsKey(m) || _targetShaders[m] != m.shader);
                if (shadersChanged)
                {
                    foreach (var material in _targets) _targetShaders[material] = material.shader;
                    UpdateTargets(); return;
                }
                if (_isStale && focusedWindow == this)
                {
                    for (int i = 0; i < _materialProperties.Length; i++)
                    {
                        var property = _materialProperties[i];
                        _materialProperties[i] = MaterialEditor.GetMaterialProperty(property.targets, property.name);
                    }
                    _isStale = false; RecordTargetDirtyCounts();
                }
            }).Every(250);
        }
    }
}
#endif
