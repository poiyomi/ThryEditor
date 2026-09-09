#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        internal static bool HasDecalPositioning(ShaderProperty property) => property?.MaterialProperty != null
            && property.MyShader.GetPropertyAttributes(property.ShaderPropertyIndex)
                .Any(a => new DrawerAttribute(a).Name == "ThryDecalPositioning");

        private void AddDecalPositioning(VisualElement root, ShaderProperty property, string[] args)
        {
            var tools = new VisualElement { name = "decal-positioning-" + property.MaterialProperty.name };
            tools.AddToClassList("thry-positioning-tools"); root.Add(tools);
            var buttons = new VisualElement(); buttons.AddToClassList("thry-positioning-actions"); tools.Add(buttons);
            var status = new Label(); status.AddToClassList("thry-positioning-status"); tools.Add(status);
            DecalSceneTool tool = null;
            Renderer activeRenderer = null;
            Material activeMaterial = null;
            int activeUv = -1;
            Action update = null;
            Func<Renderer> findRenderer = () =>
            {
                var material = Model.Shader.Materials.FirstOrDefault();
                var selected = Selection.activeTransform != null ? Selection.activeTransform.GetComponent<Renderer>() : null;
                if (selected != null && selected.sharedMaterials.Contains(material)) return selected;
                return Model.Renderers.FirstOrDefault(r => r != null && r.sharedMaterials.Contains(material));
            };
            Func<string> unavailable = () =>
            {
                if (!RetainedMaterialModel.HasValidTargets(Model.Editor)) return "Select a material to use positioning tools.";
                if (Model.Shader.Materials.Length != 1) return "Select one material to position it in the Scene view.";
                if (args.Length != 6 || args.Skip(1).Any(a => !Model.Shader.PropertyDictionary.ContainsKey(a))) return "Positioning properties are unavailable for this shader.";
                if (args.Skip(2).Any(a => !Model.CanEdit(Model.Shader.PropertyDictionary[a]))) return "Unlock the positioning properties to use scene tools.";
                var renderer = findRenderer();
                var filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) return "Select a mesh using this material to use scene tools.";
                int uv = (int)Model.Shader.PropertyDictionary[args[1]].MaterialProperty.GetNumber();
                if (uv < 0 || uv > 3) return "Scene tools require mesh UV0–UV3; procedural coordinates are not supported.";
                if (!mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + uv))) return "The selected mesh does not contain this UV channel.";
                return null;
            };
            Action<bool> stop = cancel =>
            {
                var previous = tool; tool = null; tools.userData = null;
                if (previous != null) previous.Deactivate(cancel);
                SceneView.RepaintAll();
            };
            Action<bool> begin = raycast =>
            {
                if (unavailable() != null) return;
                var mode = raycast ? DecalSceneTool.Mode.Raycast : DecalSceneTool.Mode.Handles;
                if (tool != null && tool.GetMode() == mode) { stop(raycast); Model.Notify(); update(); return; }
                stop(tool != null && tool.GetMode() == DecalSceneTool.Mode.Raycast);
                Model.Shader.ActivateRetained();
                var properties = Model.Shader.PropertyDictionary;
                activeRenderer = findRenderer(); activeMaterial = Model.Shader.Materials[0];
                activeUv = (int)properties[args[1]].MaterialProperty.GetNumber();
                tool = DecalSceneTool.Create(activeRenderer, activeMaterial, activeUv,
                    properties[args[2]].MaterialProperty, properties[args[3]].MaterialProperty,
                    properties[args[4]].MaterialProperty, properties[args[5]].MaterialProperty);
                tools.userData = tool;
                if (raycast) tool.StartRaycastMode(); else tool.StartHandleMode();
                SceneView.RepaintAll(); update();
            };
            var raycastButton = PositioningButton("Raycast", true, () => begin(true));
            raycastButton.name = "decal-raycast-" + property.MaterialProperty.name; buttons.Add(raycastButton);
            var handlesButton = PositioningButton("Scene Tools", false, () => begin(false));
            handlesButton.AddToClassList("thry-positioning-secondary");
            handlesButton.name = "decal-handles-" + property.MaterialProperty.name; buttons.Add(handlesButton);
            update = () =>
            {
                string reason = unavailable();
                if (tool != null && (tool.GetMode() == DecalSceneTool.Mode.None || reason != null
                    || activeRenderer != findRenderer() || activeMaterial != Model.Shader.Materials.FirstOrDefault()
                    || activeUv != (int)Model.Shader.PropertyDictionary[args[1]].MaterialProperty.GetNumber())) stop(tool.GetMode() == DecalSceneTool.Mode.Raycast);
                var mode = tool == null ? DecalSceneTool.Mode.None : tool.GetMode();
                raycastButton.Q<Label>().text = mode == DecalSceneTool.Mode.Raycast ? "Cancel Raycast" : "Raycast";
                handlesButton.Q<Label>().text = mode == DecalSceneTool.Mode.Handles ? "Finish Scene Tools" : "Scene Tools";
                raycastButton.EnableInClassList("thry-positioning-active", mode == DecalSceneTool.Mode.Raycast);
                handlesButton.EnableInClassList("thry-positioning-active", mode == DecalSceneTool.Mode.Handles);
                buttons.SetEnabled(reason == null);
                raycastButton.tooltip = reason ?? "Place the decal on the mesh in the Scene view. Click to apply; Escape cancels.";
                handlesButton.tooltip = reason ?? "Move, rotate, or scale the decal with scene handles. Use W, E, R, or T; Escape cancels.";
                status.text = reason ?? (mode == DecalSceneTool.Mode.Raycast ? "Click the mesh to place · Esc to cancel"
                    : mode == DecalSceneTool.Mode.Handles ? "W Move · E Rotate · R Scale · T Edges · Esc to cancel"
                    : "Place on the mesh or adjust the values below.");
                if (tool != null)
                {
                    var p = Model.Shader.PropertyDictionary;
                    tool.SetMaterialProperties(p[args[2]].MaterialProperty, p[args[3]].MaterialProperty, p[args[4]].MaterialProperty, p[args[5]].MaterialProperty);
                }
            };
            Track(tools, update);
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) stop(tool != null && tool.GetMode() == DecalSceneTool.Mode.Raycast); });
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && tool != null) { stop(true); Model.Notify(); update(); e.StopPropagation(); } });
        }

        private static Button PositioningButton(string text, bool raycast, Action action)
        {
            var button = new Button(action); button.AddToClassList("thry-positioning-button");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore }; icon.AddToClassList("thry-positioning-icon"); button.Add(icon);
#if UNITY_2022_1_OR_NEWER
            icon.generateVisualContent += context =>
            {
                var p = context.painter2D; p.strokeColor = icon.resolvedStyle.color; p.lineWidth = 1.5f;
                p.BeginPath();
                if (raycast) { p.Arc(new Vector2(8, 8), 4, 0, 360); p.Stroke(); p.BeginPath(); }
                p.MoveTo(new Vector2(8, 1)); p.LineTo(new Vector2(8, 15));
                p.MoveTo(new Vector2(1, 8)); p.LineTo(new Vector2(15, 8));
                if (!raycast)
                {
                    p.MoveTo(new Vector2(5, 4)); p.LineTo(new Vector2(8, 1)); p.LineTo(new Vector2(11, 4));
                    p.MoveTo(new Vector2(12, 5)); p.LineTo(new Vector2(15, 8)); p.LineTo(new Vector2(12, 11));
                    p.MoveTo(new Vector2(4, 5)); p.LineTo(new Vector2(1, 8)); p.LineTo(new Vector2(4, 11));
                    p.MoveTo(new Vector2(5, 12)); p.LineTo(new Vector2(8, 15)); p.LineTo(new Vector2(11, 12));
                }
                p.Stroke();
            };
#endif
            button.Add(new Label(text) { pickingMode = PickingMode.Ignore }); return button;
        }
    }
}
#endif
