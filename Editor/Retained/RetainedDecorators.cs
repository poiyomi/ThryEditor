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
        private void NormalMapImportWarning(VisualElement root, ShaderTextureProperty property)
        {
            if ((property.MyShader.GetPropertyFlags(property.ShaderPropertyIndex) & ShaderPropertyFlags.Normal) == 0) return;
            var warning = new VisualElement { name = "normal-map-warning-" + property.MaterialProperty.name };
            warning.Add(new HelpBox("Import the assigned textures as Normal Maps. These changes affect every material that uses them.", HelpBoxMessageType.Warning));
            Func<TextureImporter[]> mismatches = () => PresentationTargets(property)
                .Where(m => m.HasProperty(property.MaterialProperty.name))
                .Select(m => m.GetTexture(property.MaterialProperty.name)).OfType<Texture2D>()
                .Select(t => AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter)
                .Where(i => i != null && i.textureType != TextureImporterType.NormalMap).Distinct().ToArray();
            bool pending = false;
            Action<bool> fixImport = useBC5 =>
            {
                if (pending || !RetainedMaterialModel.HasValidTargets(Model.Editor) || !Model.CanEdit(property)) return;
                if (useBC5 && !TextureImporter.IsPlatformTextureFormatValid(TextureImporterType.NormalMap, BuildTarget.StandaloneWindows64, TextureImporterFormat.BC5)) return;
                var paths = mismatches().Select(i => i.assetPath).ToArray();
                if (paths.Length == 0) return;
                pending = true;
                // Reimport outside event dispatch and recheck the current assignments.
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (root.panel == null || !RetainedMaterialModel.HasValidTargets(Model.Editor) || !Model.CanEdit(property)) return;
                        foreach (var importer in mismatches().Where(i => paths.Contains(i.assetPath)))
                        {
                            Undo.RecordObject(importer, "Fix normal map import");
                            // textureType's importer setter applies type defaults (including
                            // mipmaps). Change the saved settings instead to retain user choices.
                            var settings = new TextureImporterSettings();
                            importer.ReadTextureSettings(settings);
                            settings.textureType = TextureImporterType.NormalMap;
                            importer.SetTextureSettings(settings);
                            if (useBC5)
                            {
                                var desktop = importer.GetPlatformTextureSettings("Standalone");
                                // A new override starts with inherited values, not stale disabled overrides.
                                if (!desktop.overridden) desktop = importer.GetDefaultPlatformTextureSettings();
                                desktop.name = "Standalone";
                                desktop.overridden = true;
                                desktop.format = TextureImporterFormat.BC5;
                                desktop.crunchedCompression = false;
                                importer.SetPlatformTextureSettings(desktop);
                            }
                            importer.SaveAndReimport();
                        }
                    }
                    finally { pending = false; Model.Notify(); }
                };
            };
            var actions = new VisualElement { name = "normal-map-actions" };
            actions.AddToClassList("thry-normal-map-actions");
            var fix = new Button(() => fixImport(false)) { text = "Fix Now", name = "fix-normal-map" };
            var fixBC5 = new Button(() => fixImport(true))
            {
                text = "Fix + BC5 (Desktop)", name = "fix-normal-map-bc5",
                tooltip = "Also sets desktop compression to BC5 and disables desktop Crunch. Creates or replaces the desktop format override, keeping the current size and other import settings."
            };
            fix.AddToClassList("thry-action-button");
            fixBC5.AddToClassList("thry-action-button");
            fix.AddToClassList("thry-normal-map-button");
            fixBC5.AddToClassList("thry-normal-map-button");
            fixBC5.AddToClassList("thry-normal-map-secondary");
            actions.Add(fix); actions.Add(fixBC5);
            warning.Add(actions); root.Add(warning);
            Track(warning, () =>
            {
                warning.style.display = mismatches().Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                fix.SetEnabled(!pending && Model.CanEdit(property));
                fixBC5.SetEnabled(!pending && Model.CanEdit(property));
            });
        }

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
                    fix.AddToClassList("thry-action-button");
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
                    button.AddToClassList("thry-action-button");
                    button.AddToClassList("thry-bake-color-button");
                    button.text = "";
                    var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                    icon.AddToClassList("thry-bake-color-icon");
                    icon.generateVisualContent += context => {
                        var painter = context.painter2D;
                        painter.strokeColor = icon.resolvedStyle.color;
                        painter.fillColor = icon.resolvedStyle.color;
                        painter.lineWidth = 1.25f;
                        painter.BeginPath(); painter.MoveTo(new Vector2(13, 7));
                        painter.BezierCurveTo(new Vector2(13, 3), new Vector2(10, 1), new Vector2(7, 1));
                        painter.BezierCurveTo(new Vector2(3.7f, 1), new Vector2(1, 3.6f), new Vector2(1, 7));
                        painter.BezierCurveTo(new Vector2(1, 10.4f), new Vector2(3.7f, 13), new Vector2(7, 13));
                        painter.BezierCurveTo(new Vector2(8.6f, 13), new Vector2(9.4f, 12), new Vector2(8.5f, 10.6f));
                        painter.BezierCurveTo(new Vector2(7.8f, 9.6f), new Vector2(8.5f, 8.4f), new Vector2(10.2f, 8.7f));
                        painter.BezierCurveTo(new Vector2(12, 9), new Vector2(13, 8.4f), new Vector2(13, 7));
                        painter.ClosePath(); painter.Stroke();
                        painter.BeginPath(); painter.Arc(new Vector2(3.8f, 6), .8f, 0, 360); painter.Fill();
                        painter.BeginPath(); painter.Arc(new Vector2(5.6f, 3.6f), .8f, 0, 360); painter.Fill();
                        painter.BeginPath(); painter.Arc(new Vector2(8.5f, 3.4f), .8f, 0, 360); painter.Fill();
                        painter.BeginPath(); painter.Arc(new Vector2(10.6f, 5.3f), .8f, 0, 360); painter.Fill();
                    };
                    button.Add(icon); button.Add(new Label("Bake Color Adjust") { pickingMode = PickingMode.Ignore });
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
