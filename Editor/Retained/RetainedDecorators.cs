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
                        if (skinned != null) return skinned.sharedMesh != null;
                        var filter = candidate != null ? candidate.GetComponent<MeshFilter>() : null;
                        return filter != null && filter.sharedMesh != null;
                    };
                    var button = new Button(() =>
                    {
                        if (pending || bake == null || !RetainedMaterialModel.HasValidTargets(Model.Editor) || !hasMesh() || !Model.CanEdit(property)) return;
                        var targetRenderer = renderer();
                        var materials = PresentationTargets(property);
                        pending = true;
                        // The existing baker opens modal panels and pumps the editor.
                        // Start it after UI event dispatch, as the legacy decorator does.
                        EditorApplication.delayCall += () =>
                        {
                            try
                            {
                                if (targetRenderer == null || root.panel == null || !RetainedMaterialModel.HasValidTargets(Model.Editor)
                                    || !Model.CanEdit(property) || materials.Any(material => material == null || !PresentationTargets(property).Contains(material))) return;
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
                    Func<TextureImporter[]> mismatches = () => PresentationTargets(property)
                        .Where(m => m.HasProperty(property.MaterialProperty.name))
                        .Select(m => m.GetTexture(property.MaterialProperty.name)).Where(t => t != null)
                        .Select(t => AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter)
                        .Where(i => i != null && i.sRGBTexture != shouldHaveSRGB).Distinct().ToArray();
                    var fix = new Button(() =>
                    {
                        if (!RetainedMaterialModel.HasValidTargets(Model.Editor) || !Model.CanEdit(property)) return;
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
                    if (method == null || check == null) continue;
                    bool pending = false;
                    var button=new Button(()=>
                    {
                        if (pending || !RetainedMaterialModel.HasValidTargets(Model.Editor) || !Model.CanEdit(property)) return;
                        var materials = PresentationTargets(property).Where(m => (bool)check.Invoke(null, new object[] { m })).ToArray();
                        if (materials.Length == 0) return;
                        pending = true;
                        // Confirmation panels and asset imports must run outside UI event dispatch.
                        EditorApplication.delayCall += () =>
                        {
                            try
                            {
                                if (root.panel == null || !RetainedMaterialModel.HasValidTargets(Model.Editor) || !Model.CanEdit(property)) return;
                                foreach (var material in materials)
                                    if (material != null && PresentationTargets(property).Contains(material)) method.Invoke(null, new object[] { material });
                            }
                            catch (TargetInvocationException exception) { Debug.LogException(exception.InnerException ?? exception); }
                            finally { pending = false; Model.Notify(); }
                        };
                    }) { text="Bake Color Adjust", name="bake-color-adjust" };
                    Track(button,()=>button.SetEnabled(!pending && RetainedMaterialModel.HasValidTargets(Model.Editor) && Model.CanEdit(property)
                        && PresentationTargets(property).Any(m=>(bool)check.Invoke(null,new object[]{m}))));root.Add(button);
                }
                if(attribute.Name=="ThryDecalPositioning")
                {
                    AddDecalPositioning(root, property, attribute.Args);
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
