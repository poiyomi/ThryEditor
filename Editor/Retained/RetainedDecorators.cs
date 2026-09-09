#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using System.Reflection;
using Thry.ThryEditor.DataStructs;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        private void Decorators(VisualElement root,ShaderProperty property,DrawerAttribute[] attributes)
        {
            foreach(var attribute in attributes)
            {
                if (attribute.Name == "PoiApplySDFBaker")
                {
                    var bakerType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(assembly => assembly.GetType("Poi.Raymarching.PoiApplySDFBakerDecorator")).FirstOrDefault(type => type != null);
                    var bake = bakerType?.GetMethod("RunFullBake", BindingFlags.Static | BindingFlags.NonPublic, null,
                        new[] { typeof(Renderer), typeof(Material[]), typeof(string) }, null);
                    bool pending = false;
                    Func<Renderer> renderer = () => Model.Renderers.FirstOrDefault(candidate => candidate != null) ?? Model.Shader.ActiveRenderer;
                    Func<bool> hasMesh = () =>
                    {
                        var candidate = renderer();
                        var skinned = candidate as SkinnedMeshRenderer;
                        return skinned != null ? skinned.sharedMesh != null : candidate != null && candidate.GetComponent<MeshFilter>()?.sharedMesh != null;
                    };
                    var button = new Button(() =>
                    {
                        if (pending || bake == null || !hasMesh() || !Model.CanEdit(property)) return;
                        var targetRenderer = renderer();
                        var materials = Model.Shader.Materials.Where(material => material != null).ToArray();
                        pending = true;
                        // The existing baker opens modal panels and pumps the editor.
                        // Start it after UI event dispatch, as the legacy decorator does.
                        EditorApplication.delayCall += () =>
                        {
                            try
                            {
                                if (targetRenderer == null) return;
                                string path = EditorUtility.SaveFilePanelInProject(RetainedText.Get(Model.Shader, "saveSdfTexture", "Save SDF Texture"),
                                    targetRenderer.name + "_SDF", "asset", RetainedText.Get(Model.Shader, "saveSdfTexture", "Save SDF Texture"));
                                if (!string.IsNullOrEmpty(path)) bake.Invoke(null, new object[] { targetRenderer, materials, path });
                            }
                            catch (TargetInvocationException exception) { Debug.LogException(exception.InnerException ?? exception); }
                            finally { pending = false; Model.Notify(); }
                        };
                    }) { name = "bake-sdf-bind-data" };
                    Track(button, () =>
                    {
                        bool ready = hasMesh();
                        button.text = RetainedText.Get(Model.Shader, "bakeSdfBindData", "Bake SDF + Bind Data");
                        button.tooltip = bake == null ? RetainedText.Get(Model.Shader, "sdfBakerUnavailable", "The SDF baker is unavailable.")
                            : ready ? RetainedText.Get(Model.Shader, "sdfBakeDescription", "Bake the mesh volume and bind data, then assign the results to the selected materials.")
                            : RetainedText.Get(Model.Shader, "sdfBakeSelectMesh", "Select a mesh object using this material to bake its volume and bind data.");
                        button.SetEnabled(!pending && bake != null && ready && Model.CanEdit(property));
                    });
                    root.Add(button);
                }
                if (attribute.Name == "sRGBWarning")
                {
                    bool shouldHaveSRGB = attribute.Args.Any(a => a.Equals("gamma", StringComparison.OrdinalIgnoreCase) || a.Equals("true", StringComparison.OrdinalIgnoreCase));
                    var warning = new VisualElement { name = "colorspace-warning-" + property.MaterialProperty.name };
                    string message = EditorLocale.editor.Get(shouldHaveSRGB ? "colorSpaceWarningSRGB" : "colorSpaceWarningLinear");
                    warning.Add(new HelpBox(message, HelpBoxMessageType.Warning));
                    Func<TextureImporter[]> mismatches = () => Model.Shader.Materials
                        .Where(m => m.HasProperty(property.MaterialProperty.name))
                        .Select(m => m.GetTexture(property.MaterialProperty.name)).Where(t => t != null)
                        .Select(t => AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter)
                        .Where(i => i != null && i.sRGBTexture != shouldHaveSRGB).Distinct().ToArray();
                    var fix = new Button(() =>
                    {
                        foreach (var importer in mismatches())
                        {
                            Undo.RecordObject(importer, "Fix texture color space");
                            importer.sRGBTexture = shouldHaveSRGB;
                            importer.SaveAndReimport();
                        }
                        Model.Notify();
                    }) { text = "Fix Now", name = "fix-colorspace" };
                    warning.Add(fix); root.Add(warning);
                    Track(warning, () => warning.style.display = Config.Instance.showColorspaceWarnings && mismatches().Length > 0 ? DisplayStyle.Flex : DisplayStyle.None);
                }
                if(attribute.Name=="PoiBakeColorAdjust")
                {
                    var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Poi.Tools.PoiColorAdjustBaker")).FirstOrDefault(t=>t!=null);
                    if(type==null)continue;
                    var method=type.GetMethod("BakeColorAdjust");var check=type.GetMethod("HasColorAdjustChanges");
                    var button=new Button(()=>{foreach(var material in Model.Shader.Materials)method.Invoke(null,new object[]{material});Model.Notify();}){text="Bake Color Adjust"};
                    Track(button,()=>button.SetEnabled(Model.Shader.Materials.Any(m=>(bool)check.Invoke(null,new object[]{m}))));root.Add(button);
                }
                if(attribute.Name=="ThryDecalPositioning")
                {
                    var args=attribute.Args;DecalSceneTool tool=null;var row=new VisualElement();row.AddToClassList("thry-components");root.Add(row);
                    Action<bool> begin=raycast=>{
                        if(tool!=null){tool.Deactivate(false);tool=null;return;} Model.Shader.ActivateRetained();
                        var renderer=Model.Renderers.FirstOrDefault()??Selection.activeTransform?.GetComponent<Renderer>();if(renderer==null)return;
                        var properties=Model.Shader.PropertyDictionary;
                        tool=DecalSceneTool.Create(renderer,Model.Shader.Materials[0],(int)properties[args[1]].MaterialProperty.GetNumber(),properties[args[2]].MaterialProperty,properties[args[3]].MaterialProperty,properties[args[4]].MaterialProperty,properties[args[5]].MaterialProperty);
                        if(raycast)tool.StartRaycastMode();else tool.StartHandleMode();
                    };
                    row.Add(new Button(()=>begin(true)){text="Raycast"});row.Add(new Button(()=>begin(false)){text="Scene Tools"});
                    Track(row,()=>{row.SetEnabled(Model.Renderers.Length>0||Selection.activeTransform?.GetComponent<Renderer>()!=null);if(tool!=null){var p=Model.Shader.PropertyDictionary;tool.SetMaterialProperties(p[args[2]].MaterialProperty,p[args[3]].MaterialProperty,p[args[4]].MaterialProperty,p[args[5]].MaterialProperty);}});
                    root.RegisterCallback<DetachFromPanelEvent>(e=>{if(tool!=null)tool.Deactivate(false);});
                    root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape&&tool!=null){tool.Deactivate(true);tool=null;Model.Notify();e.StopPropagation();}});
                }
                if(attribute.Name=="ThryStencilCalculator"||attribute.Name=="ThryStencilSummary")
                {
                    root.Add(StencilView(StencilConfiguration(attribute), attribute.Name == "ThryStencilSummary"));
                }
            }
        }
    }
}
#endif
