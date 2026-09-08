#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        partial void TextureTools(VisualElement parent, ShaderTextureProperty property, DrawerAttribute[] attributes)
        {
            foreach(var attribute in attributes)
            {
                if(attribute.Name=="ThryRGBAPacker")
                {
                    var args=attribute.Args;
                    var packer=args.Length==4?new Drawers.ThryRGBAPackerDrawer(args[0],args[1],args[2],args[3]):new Drawers.ThryRGBAPackerDrawer(args[0],args[1],args[2],args[3],args[4],args[5]);
                    parent.Add(packer.CreateRetained(this,property,_view));
                }
                if(attribute.Name=="Gradient")
                {
                    StyleSpecialControls(parent);
                    var settings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(property.Options.texture ?? new TextureData()));
                    string settingsKey = "gradient_texture_options_" + property.MaterialProperty.name;
                    if (!property.Options.force_texture_options)
                    {
                        string savedSettings = FileHelper.LoadValueFromFile(settingsKey, PATH.PERSISTENT_DATA);
                        if (!string.IsNullOrEmpty(savedSettings)) settings = Parser.Deserialize<TextureData>(savedSettings) ?? settings;
                    }
                    VisualElement gradientInput;
                    parent.Add(Row("Gradient", out gradientInput));
                    var field=new GradientField { name = "gradient-editor" }; field.SetValueWithoutNotify(TextureHelper.GetGradient(property.MaterialProperty.textureValue, false)); gradientInput.Add(field);
                    var apply = new Button { text = "Apply gradient" }; apply.SetEnabled(false); parent.Add(apply);
                    field.RegisterValueChangedCallback(e => apply.SetEnabled(true));
                    if (!property.Options.force_texture_options)
                    {
                        var options = new Foldout { text = "Texture options", value = false, name = "gradient-texture-options" };
                        parent.Insert(parent.IndexOf(apply), options);
                        VisualElement sizeInput, filterInput, wrapInput, anisoInput;
                        options.Add(Row("Texture size", out sizeInput));
                        var size = new Vector2IntField { value = new Vector2Int(settings.width, settings.height), name = "gradient-size" }; sizeInput.Add(size);
                        size.RegisterValueChangedCallback(e =>
                        {
                            settings.width = Mathf.Clamp(e.newValue.x, 1, 8192); settings.height = Mathf.Clamp(e.newValue.y, 1, 8192);
                            size.SetValueWithoutNotify(new Vector2Int(settings.width, settings.height)); apply.SetEnabled(true);
                        });
                        options.Add(Row("Filtering", out filterInput));
                        var filter = new DropdownField(Enum.GetNames(typeof(FilterMode)).ToList(), (int)settings.filterMode);
                        _view.UseInspectorMenu(filter); filterInput.Add(filter);
                        filter.RegisterValueChangedCallback(e => { settings.filterMode = (FilterMode)Enum.Parse(typeof(FilterMode), e.newValue); apply.SetEnabled(true); });
                        options.Add(Row("Wrap", out wrapInput));
                        var wrap = new DropdownField(Enum.GetNames(typeof(TextureWrapMode)).ToList(), (int)settings.wrapMode);
                        _view.UseInspectorMenu(wrap); wrapInput.Add(wrap);
                        wrap.RegisterValueChangedCallback(e => { settings.wrapMode = (TextureWrapMode)Enum.Parse(typeof(TextureWrapMode), e.newValue); apply.SetEnabled(true); });
                        options.Add(Row("Anisotropic filtering", out anisoInput));
                        var aniso = new SliderInt(0, 16) { value = settings.ansioLevel, showInputField = true }; anisoInput.Add(aniso);
                        aniso.RegisterValueChangedCallback(e => { settings.ansioLevel = e.newValue; apply.SetEnabled(true); });
                    }
                    apply.clicked += () => {
                        var texture=Converter.GradientToTexture(field.value,settings.width,settings.height);
                        bool gammaColorSpace = attribute.Args.Any(a=>a.Equals("gamma",StringComparison.OrdinalIgnoreCase));
                        if(gammaColorSpace) {var gamma=TextureHelper.ConvertToGamma(texture);UnityEngine.Object.DestroyImmediate(texture);texture=gamma;}
                        string path=PATH.TEXTURES_DIR+"/Gradients/"+Guid.NewGuid().ToString("N")+".png";
                        var asset=TextureHelper.SaveTextureAsPNG(texture,path,settings);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset));
                        importer.sRGBTexture = gammaColorSpace;
                        importer.textureCompression = TextureImporterCompression.CompressedHQ;
                        if (Config.Instance.gradientEditorCompressionOverwrite != TextureImporterFormat.Automatic)
                            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "PC", overridden = true, maxTextureSize = Mathf.Max(2048, settings.width, settings.height), format = Config.Instance.gradientEditorCompressionOverwrite });
                        importer.SaveAndReimport();
                        FileHelper.SaveValueToFile(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)),Parser.Serialize(field.value),PATH.GRADIENT_INFO_FILE);
                        if (!property.Options.force_texture_options) FileHelper.SaveValueToFile(settingsKey, Parser.Serialize(settings), PATH.PERSISTENT_DATA);
                        Model.Edit(property,p=>p.textureValue=asset);UnityEngine.Object.DestroyImmediate(texture);
                        apply.SetEnabled(false);
                    };
                }
                if(attribute.Name=="Curve")
                {
                    string key=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Model.Shader.Materials[0]))+"_"+property.MaterialProperty.name;
                    string json=Model.Shader.Materials[0].GetTag(property.MaterialProperty.name+"_curve",false,"");
                    VisualElement curveInput;
                    parent.Add(Row("Curve", out curveInput));
                    var field=new CurveField { name = "curve-editor" }; field.SetValueWithoutNotify(string.IsNullOrEmpty(json)?AnimationCurve.Linear(0,0,1,1):JsonUtility.FromJson<CurveData>(json).curve); curveInput.Add(field);
                    var apply = new Button { text = "Apply curve" }; apply.SetEnabled(false); parent.Add(apply);
                    field.RegisterValueChangedCallback(e => apply.SetEnabled(true));
                    apply.clicked += () => {
                        var texture=Converter.CurveToTexture(field.value,property.Options.texture??new TextureData());
                        var asset=TextureHelper.SaveTextureAsPNG(texture,PATH.TEXTURES_DIR+"/curves/"+key+"_"+Guid.NewGuid().ToString("N")+".png",null);
                        Model.Edit(property,p=>p.textureValue=asset);
                        Model.Mutate("Save curve",m=>m.SetOverrideTag(property.MaterialProperty.name+"_curve",JsonUtility.ToJson(new CurveData {curve=field.value}))); UnityEngine.Object.DestroyImmediate(texture);
                        apply.SetEnabled(false);
                    };
                }
            }
        }
        [Serializable] private class CurveData { public AnimationCurve curve; }
    }
}

namespace Thry.ThryEditor.Drawers
{
    public partial class ThryRGBAPackerDrawer
    {
        internal VisualElement CreateRetained(RetainedFields fields,ShaderTextureProperty property,MaterialInspectorView view)
        {
            _prop=property.MaterialProperty;
            _current=new ThryRGBAPackerData(); fields.Model.Shader.ActivateRetained(); Init(); LoadLabels();
            var root=new VisualElement(); root.AddToClassList("thry-packer"); RetainedFields.StyleSpecialControls(root);
            var channels=new[]{_current._input_r,_current._input_g,_current._input_b,_current._input_a};
            Undo.UndoRedoCallback refreshAfterUndo = () =>
            {
                fields.Model.Shader.ActivateRetained(); _prop = property.MaterialProperty;
                for (int i = 0; i < channels.Length; i++)
                {
                    var restored = LoadForChannel(fields.Model.Shader.Materials[0], _prop.name, "rgba"[i].ToString());
                    channels[i].Source = restored.Source; channels[i].Channel = restored.Channel;
                    channels[i].Invert = restored.Invert; channels[i].Fallback = restored.Fallback; channels[i].Remapping = restored.Remapping;
                }
                _current._isInit = true;
                _current._packedTexture = _prop.textureValue as Texture2D;
                _current._hasTextureChanged = false; _current._hasConfigChanged = false;
            };
            Undo.undoRedoPerformed += refreshAfterUndo;
            var labels=new[]{_label1,_label2,_label3,_label4};
            Action changed=()=>{fields.Model.Shader.ActivateRetained();_prop=property.MaterialProperty;
                Undo.RecordObjects(fields.Model.Editor.targets,"Edit texture channels");_current._hasConfigChanged=true;Save();
                fields.Model.Edit(property,p=>{_prop=p;Pack();});};
            for(int i=0;i<4;i++)
            {
                if(labels[i]==null)continue;var input=channels[i];VisualElement value;
                var sourceRow=RetainedFields.Row("",out value);sourceRow.AddToClassList("thry-packer-source-row");root.Add(sourceRow);
                var detail=new Foldout {text=labels[i]+" channel options",value=false,name="packer-options-"+i};detail.AddToClassList("thry-packer-channel-options");root.Add(detail);
                // The assignment's caption is the disclosure control. The options still
                // occupy the full inspector grid below it when expanded.
                sourceRow.Q<Label>(className:"thry-property-label").RemoveFromHierarchy();
                var disclosure=new Button(()=>detail.value=!detail.value) {name="packer-options-toggle-"+i,tooltip="Show or hide "+labels[i]+" channel options"};
                disclosure.AddToClassList("thry-property-label");disclosure.AddToClassList("thry-texture-label");
                var caret=new Image {image=Resources.Load<Texture2D>("ThryToolbar/header-caret-right"),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
                caret.AddToClassList("thry-header-icon");caret.AddToClassList("thry-texture-caret");disclosure.Add(caret);
                var caption=new Label(labels[i]) {pickingMode=PickingMode.Ignore};caption.AddToClassList("thry-texture-caption");disclosure.Add(caption);
                sourceRow.Insert(0,disclosure);
                disclosure.RegisterCallback<NavigationSubmitEvent>(e=>{e.PreventDefault();e.StopImmediatePropagation();detail.value=!detail.value;},TrickleDown.TrickleDown);
                detail.RegisterValueChangedCallback(e=>{if(e.target==detail)caret.image=Resources.Load<Texture2D>("ThryToolbar/header-caret-"+(e.newValue?"down":"right"));});
                var texture=new ObjectField {objectType=typeof(Texture2D),allowSceneObjects=false,name="packer-source-"+i};value.Add(texture);
                fields.Track(texture,()=>texture.SetValueWithoutNotify(input.Source.Texture));
                texture.RegisterValueChangedCallback(e=>{input.Source.SetInputTexture(e.newValue as Texture2D);changed();});
                VisualElement channelInput, fallbackInput, invertInput, remapInput;
                detail.Add(RetainedFields.Row("Source channel",out channelInput));
                var channel=new DropdownField(Enum.GetNames(typeof(TexturePacker.TextureChannelIn)).ToList(),0);view.UseInspectorMenu(channel);channelInput.Add(channel);
                channel.SetEnabled(!_firstTextureIsRGB || i != 0);
                fields.Track(channel,()=>channel.SetValueWithoutNotify(input.Channel.ToString()));
                channel.RegisterValueChangedCallback(e=>{input.Channel=(TexturePacker.TextureChannelIn)Enum.Parse(typeof(TexturePacker.TextureChannelIn),e.newValue);changed();});
                detail.Add(RetainedFields.Row("Fallback",out fallbackInput));
                var fallback=new Slider(0,1) {showInputField=true};fallbackInput.Add(fallback);fields.Track(fallback,()=>fallback.SetValueWithoutNotify(input.Fallback));fallback.RegisterValueChangedCallback(e=>{input.Fallback=e.newValue;changed();});
                detail.Add(RetainedFields.Row("Invert",out invertInput));
                var invert=new Toggle();invertInput.Add(invert);fields.Track(invert,()=>invert.SetValueWithoutNotify(input.Invert));invert.RegisterValueChangedCallback(e=>{input.Invert=e.newValue;changed();});
                detail.Add(RetainedFields.Row("Remap",out remapInput));
                var remap=new Vector4Field { tooltip="X / Y: input minimum and maximum. Z / W: output minimum and maximum." };remapInput.Add(remap);fields.Track(remap,()=>remap.SetValueWithoutNotify(input.Remapping));remap.RegisterValueChangedCallback(e=>{input.Remapping=e.newValue;changed();});
            }
            var actions=new VisualElement();actions.AddToClassList("thry-components");actions.AddToClassList("thry-packer-actions");root.Add(actions);
            string[] names={"Merge","Revert","Clear"};Action[] callbacks={Confirm,Revert,Clear};
            for(int i=0;i<names.Length;i++){int index=i;var button=new Button(()=>{fields.Model.Shader.ActivateRetained();fields.Model.Edit(property,p=>{_prop=p;callbacks[index]();});}) {text=names[i]};button.style.flexGrow=1;actions.Add(button);}
            var advanced = new Button(() =>
            {
                fields.Model.Shader.ActivateRetained();
                var studio = TexturePacker.NodeGUI.Open(GetConfig());
                // Preview stays in the studio; only a saved asset is assigned to the material.
                studio.OnSave += texture => fields.Model.Edit(property, p =>
                {
                    _prop = p;
                    FullTexturePackerOnSave(texture);
                });
            }) { text = "Open texture studio", name = "open-texture-studio" };
            advanced.AddToClassList("thry-packer-studio-action");
            root.Add(advanced);
            root.RegisterCallback<DetachFromPanelEvent>(e=>{
                Undo.undoRedoPerformed -= refreshAfterUndo;
#if UNITY_2022_1_OR_NEWER
                Undo.undoRedoEvent-=OnUndoRedo;
#endif
            });
            return root;
        }
    }
}
#endif
