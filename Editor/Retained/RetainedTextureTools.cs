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
        void OpenGradientCreator(ShaderTextureProperty property, DrawerAttribute attribute)
        {
            if (!Model.CanEdit(property)) return;
            var settings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(property.Options.texture ?? new TextureData()));
            string settingsKey = "gradient_texture_options_" + property.MaterialProperty.name;
            int direction;
            int.TryParse(FileHelper.LoadValueFromFile(settingsKey + "_direction", PATH.PERSISTENT_DATA), out direction);
            if (!property.Options.force_texture_options)
            {
                string saved = FileHelper.LoadValueFromFile(settingsKey, PATH.PERSISTENT_DATA);
                if (!string.IsNullOrEmpty(saved)) settings = Parser.Deserialize<TextureData>(saved) ?? settings;
            }
            // Keep drafts inside the tool. Opening, changing settings, and cancelling
            // must not create assets or alter any of the selected materials.
            GradientEditor2.OpenTexture(TextureHelper.GetGradient(property.MaterialProperty.textureValue, false), settings,
                property.Options.force_texture_options, (gradient, output, outputDirection) =>
                {
                    if (!Model.CanEdit(property)) return;
                    var texture = GradientEditor2.CreateTexture(gradient, output.width, output.height, outputDirection);
                    try
                    {
                        bool gammaColorSpace = attribute.Args.Any(a => a.Equals("gamma", StringComparison.OrdinalIgnoreCase));
                        if (gammaColorSpace) { var gamma = TextureHelper.ConvertToGamma(texture); UnityEngine.Object.DestroyImmediate(texture); texture = gamma; }
                        string path = PATH.TEXTURES_DIR + "/Gradients/" + Guid.NewGuid().ToString("N") + ".png";
                        var asset = TextureHelper.SaveTextureAsPNG(texture, path, output);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset));
                        importer.sRGBTexture = gammaColorSpace;
                        importer.textureCompression = TextureImporterCompression.CompressedHQ;
                        if (Config.Instance.gradientEditorCompressionOverwrite != TextureImporterFormat.Automatic)
                            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "PC", overridden = true, maxTextureSize = Mathf.Max(2048, output.width, output.height), format = Config.Instance.gradientEditorCompressionOverwrite });
                        importer.SaveAndReimport();
                        FileHelper.SaveValueToFile(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)), Parser.Serialize(gradient), PATH.GRADIENT_INFO_FILE);
                        if (!property.Options.force_texture_options) FileHelper.SaveValueToFile(settingsKey, Parser.Serialize(output), PATH.PERSISTENT_DATA);
                        FileHelper.SaveValueToFile(settingsKey + "_direction", outputDirection.ToString(), PATH.PERSISTENT_DATA);
                        Model.Edit(property, p => p.textureValue = asset);
                    }
                    finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
                }, () => Model.CanEdit(property), direction);
        }

        partial void TextureTools(VisualElement parent, RetainedTextureCard card, ShaderTextureProperty property, DrawerAttribute[] attributes)
        {
            foreach(var attribute in attributes)
            {
                if(attribute.Name=="ThryRGBAPacker")
                {
                    var args=attribute.Args;
                    var packer=args.Length==4?new Drawers.ThryRGBAPackerDrawer(args[0],args[1],args[2],args[3]):new Drawers.ThryRGBAPackerDrawer(args[0],args[1],args[2],args[3],args[4],args[5]);
                    var inputs = packer.CreateRetained(this,property,_view);
                    card.AddToClassList("thry-texture-card-with-inputs");
                    card.Add(inputs);
                    Track(inputs, () => inputs.SetEnabled(Model.CanEdit(property)));
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
                        try
                        {
                        var asset=TextureHelper.SaveTextureAsPNG(texture,PATH.TEXTURES_DIR+"/curves/"+key+"_"+Guid.NewGuid().ToString("N")+".png",null);
                        Model.Edit(property,p=>p.textureValue=asset);
                        Model.Mutate("Save curve",m=>m.SetOverrideTag(property.MaterialProperty.name+"_curve",JsonUtility.ToJson(new CurveData {curve=field.value})));
                        apply.SetEnabled(false);
                        }
                        finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
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
            var root=new VisualElement { name = "texture-channel-inputs" }; root.AddToClassList("thry-packer"); RetainedFields.StyleSpecialControls(root);
            var heading = new VisualElement(); heading.AddToClassList("thry-packer-heading"); root.Add(heading);
            var headingLabel = new Label("Channel inputs"); headingLabel.AddToClassList("thry-packer-heading-label"); heading.Add(headingLabel);
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
#if UNITY_2022_1_OR_NEWER
            // Init also serves the immediate inspector. Retained subscriptions
            // follow panel attachment so temporary removal can safely reattach.
            Undo.undoRedoEvent -= OnUndoRedo;
#endif
            root.RegisterCallback<AttachToPanelEvent>(e =>
            {
                Undo.undoRedoPerformed -= refreshAfterUndo;
                Undo.undoRedoPerformed += refreshAfterUndo;
#if UNITY_2022_1_OR_NEWER
                Undo.undoRedoEvent -= OnUndoRedo;
                Undo.undoRedoEvent += OnUndoRedo;
#endif
                refreshAfterUndo();
            });
            var labels=new[]{_label1,_label2,_label3,_label4};
            Action changed=()=>{fields.Model.Shader.ActivateRetained();_prop=property.MaterialProperty;
                Undo.RecordObjects(fields.Model.Editor.targets,"Edit texture channels");_current._hasConfigChanged=true;Save();
                fields.Model.Edit(property,p=>{_prop=p;Pack();});};
            for(int i=0;i<4;i++)
            {
                if(labels[i]==null)continue;var input=channels[i];VisualElement value;
                var sourceRow=RetainedFields.Row("",out value);sourceRow.AddToClassList("thry-packer-source-row");root.Add(sourceRow);
                var detail=new Foldout {text=labels[i]+" channel options",value=false,name="packer-options-"+i};detail.AddToClassList("thry-packer-channel-options");root.Add(detail);
                RetainedUiState.Bind(detail, fields.Model.Shader.Materials[0].shader.name, property.MaterialProperty.name + ".packer-channel-" + i);
                // The assignment's caption is the disclosure control. The options still
                // occupy the full inspector grid below it when expanded.
                sourceRow.Q<Label>(className:"thry-property-label").RemoveFromHierarchy();
                var disclosure=new Button(()=>detail.value=!detail.value) {name="packer-options-toggle-"+i,tooltip="Show or hide "+labels[i]+" channel options"};
                disclosure.AddToClassList("thry-property-label");disclosure.AddToClassList("thry-texture-label");
                var caret=new Image {image=Resources.Load<Texture2D>("ThryToolbar/header-caret-" + (detail.value ? "down" : "right")),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
                caret.AddToClassList("thry-header-icon");caret.AddToClassList("thry-texture-caret");disclosure.Add(caret);
                int outputIndex = _firstTextureIsRGB && i == 1 ? 3 : i;
                bool rgbSource = _firstTextureIsRGB && i == 0;
                string outputChannel = rgbSource ? "RGB" : "RGBA"[outputIndex].ToString();
                var badge = new Label(outputChannel) { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("thry-packer-channel-badge"); badge.AddToClassList("thry-packer-channel-" + outputChannel.ToLowerInvariant()); disclosure.Add(badge);
                string channelCaption = ChannelCaption(labels[i], outputChannel, outputIndex);
                var caption=new Label(channelCaption) {pickingMode=PickingMode.Ignore};caption.AddToClassList("thry-texture-caption");disclosure.Add(caption);
                disclosure.tooltip = labels[i] + " · " + outputChannel + " output. Click to edit fallback, inversion, and remapping.";
                sourceRow.Insert(0,disclosure);
                disclosure.RegisterCallback<NavigationSubmitEvent>(e=>{e.PreventDefault();e.StopImmediatePropagation();detail.value=!detail.value;},TrickleDown.TrickleDown);
                detail.RegisterValueChangedCallback(e=>{if(e.target==detail)caret.image=Resources.Load<Texture2D>("ThryToolbar/header-caret-"+(e.newValue?"down":"right"));});
                var texture=new ObjectField {objectType=typeof(Texture2D),allowSceneObjects=false,name="packer-source-"+i};value.Add(texture);
                value.AddToClassList("thry-packer-source-value");
                fields.Track(texture,()=>texture.SetValueWithoutNotify(input.Source.Texture));
                texture.RegisterValueChangedCallback(e=>{input.Source.SetInputTexture(e.newValue as Texture2D);changed();});
                fields.TextureAssetDisplay(texture, () => input.Source.Texture, () => false,
                    () => fields.Model.CanEdit(property), () => texture.value = null);
                texture.RegisterCallback<GeometryChangedEvent>(e => texture.EnableInClassList("thry-packer-source-compact", e.newRect.width < 210));
                VisualElement invertInput, remapInput;
                var channel=new DropdownField((rgbSource ? new[] { "RGB" } : Enum.GetNames(typeof(TexturePacker.TextureChannelIn))).ToList(),0)
                    { name = "packer-source-channel-" + i, tooltip = "Source channel to read into the " + outputChannel + " output" };
                channel.AddToClassList("thry-packer-source-channel"); view.UseInspectorMenu(channel); value.Add(channel);
                channel.SetEnabled(!rgbSource);
                fields.Track(channel,()=> {
                    channel.SetValueWithoutNotify(rgbSource ? "RGB" : input.Channel.ToString());
                    channel.style.display = input.Source.Texture != null ? DisplayStyle.Flex : DisplayStyle.None;
                });
                channel.RegisterValueChangedCallback(e=>{input.Channel=(TexturePacker.TextureChannelIn)Enum.Parse(typeof(TexturePacker.TextureChannelIn),e.newValue);changed();});
                var fallback = new FloatField { name = "packer-source-fallback-" + i, isDelayed = true,
                    tooltip = "Fallback value (0–1) used for the " + outputChannel + " output when no texture is assigned" };
                fallback.AddToClassList("thry-packer-source-fallback"); value.Add(fallback);
                fields.Track(fallback, () => {
                    fallback.SetValueWithoutNotify(input.Fallback);
                    fallback.style.display = input.Source.Texture == null ? DisplayStyle.Flex : DisplayStyle.None;
                });
                fallback.RegisterValueChangedCallback(e => {
                    float next = float.IsNaN(e.newValue) ? 0 : Mathf.Clamp01(e.newValue);
                    fallback.SetValueWithoutNotify(next);
                    if (!fields.Model.CanEdit(property) || input.Fallback == next) return;
                    input.Fallback = next; changed();
                });
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
            }) { text = "Texture Studio", name = "open-texture-studio", tooltip = "Open the texture studio for advanced channel packing." };
            advanced.AddToClassList("thry-packer-studio-action");
            heading.Add(advanced);
            root.RegisterCallback<DetachFromPanelEvent>(e=>{
                Undo.undoRedoPerformed -= refreshAfterUndo;
#if UNITY_2022_1_OR_NEWER
                Undo.undoRedoEvent-=OnUndoRedo;
#endif
            });
            return root;
        }

        static string ChannelCaption(string label, string channel, int index)
        {
            string caption = label.Trim();
            string fullName = new[] { "Red", "Green", "Blue", "Alpha" }[index];
            foreach (string prefix in new[] { channel, fullName })
            {
                if (caption.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return "Source";
                if (caption.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) return caption.Substring(prefix.Length).TrimStart();
            }
            return caption;
        }
    }
}
#endif
