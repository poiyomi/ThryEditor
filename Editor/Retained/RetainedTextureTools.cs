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
        void OpenGradientCreator(ShaderTextureProperty property, DrawerAttribute attribute, VisualElement owner)
        {
            Func<bool> canApply = () => owner.panel != null && Model.Editor != null && Model.Editor.target != null && Model.CanEdit(property);
            if (!canApply()) return;
            var settings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(property.Options.texture ?? new TextureData()));
            string settingsKey = "gradient_texture_options_" + property.MaterialProperty.name;
            int direction;
            int.TryParse(FileHelper.LoadValueFromFile(settingsKey + "_direction", PATH.PERSISTENT_DATA), out direction);
            if (!property.Options.force_texture_options)
            {
                string saved = FileHelper.LoadValueFromFile(settingsKey, PATH.PERSISTENT_DATA);
                if (!string.IsNullOrEmpty(saved)) settings = Parser.Deserialize<TextureData>(saved) ?? settings;
            }
            var assignedSettings = RetainedGradientOutput.Load(property.MaterialProperty.textureValue);
            if (assignedSettings != null)
            {
                direction = Mathf.Clamp(assignedSettings.direction, 0, 2);
                if (!property.Options.force_texture_options && assignedSettings.settings != null) settings = assignedSettings.settings;
            }
            // Keep drafts inside the tool. Opening, changing settings, and cancelling
            // must not create assets or alter any of the selected materials.
            GradientEditor2.OpenTexture(TextureHelper.GetGradient(property.MaterialProperty.textureValue, false), settings,
                property.Options.force_texture_options, (gradient, output, outputDirection) =>
                {
                    if (!canApply()) return;
                    bool gammaColorSpace = attribute.Args.Any(a => a.Equals("gamma", StringComparison.OrdinalIgnoreCase));
                    string path = PATH.TEXTURES_DIR + "/Gradients/" + Guid.NewGuid().ToString("N") + ".png";
                    var asset = RetainedGradientOutput.Save(gradient, output, outputDirection, gammaColorSpace, path);
                    if (!property.Options.force_texture_options) FileHelper.SaveValueToFile(settingsKey, Parser.Serialize(output), PATH.PERSISTENT_DATA);
                    FileHelper.SaveValueToFile(settingsKey + "_direction", outputDirection.ToString(), PATH.PERSISTENT_DATA);
                    Model.Edit(property, p => p.textureValue = asset);
                }, canApply, direction);
        }

        partial void TextureTools(VisualElement parent, RetainedTextureCard card, ShaderTextureProperty property, DrawerAttribute[] attributes)
        {
            foreach(var attribute in attributes)
            {
                if (attribute.Name == "ThryExternalTextureTool")
                {
                    var tool = new RetainedExternalTextureTool(Model, property, attribute);
                    parent.Add(tool); Track(tool, tool.Synchronize);
                }
                if(attribute.Name=="ThryRGBAPacker")
                {
                    var args=attribute.Args;
                    var packer=args.Length==4?new Drawers.ThryRGBAPackerDrawer(args[0],args[1],args[2],args[3]):new Drawers.ThryRGBAPackerDrawer(args[0],args[1],args[2],args[3],args[4],args[5]);
                    var inputs = packer.CreateRetained(this,property,_view);
                    card.AddToClassList("thry-texture-card-with-inputs");
                    card.Add(inputs);
                    var animationHint = new Label(RetainedText.Get(Model.Shader, "packer_animation_hint", "Exit Animation mode to edit texture channels. Temporary previews cannot be recorded in an animation."));
                    animationHint.style.whiteSpace = WhiteSpace.Normal; card.Add(animationHint);
                    Track(inputs, () => { bool recording = AnimationMode.InAnimationMode(); inputs.SetEnabled(Model.CanEdit(property) && !recording);
                        animationHint.style.display = recording ? DisplayStyle.Flex : DisplayStyle.None; });
                }
                if(attribute.Name=="Curve")
                {
                    string key=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Model.Shader.Materials[0]))+"_"+property.MaterialProperty.name;
                    VisualElement curveInput;
                    parent.Add(Row("Curve", out curveInput));
                    var field=new CurveField { name = "curve-editor" }; curveInput.Add(field);
                    var apply = new Button { text = "Apply curve", name = "apply-curve" }; apply.SetEnabled(false); parent.Add(apply);
                    bool draft = false; string signature = null;
                    Track(field, () =>
                    {
                        var values = property.MaterialProperty.targets.OfType<Material>().Where(m => Model.Shader.Materials.Contains(m) && m.HasProperty(property.MaterialProperty.name))
                            .Select(m => m.GetTag(property.MaterialProperty.name + "_curve", false, "")).ToArray();
                        string current = string.Join("|", values);
                        if (signature != current)
                        {
                            signature = current; draft = false;
                            AnimationCurve curve = null;
                            try { if (values.Length > 0 && !string.IsNullOrEmpty(values[0])) curve = JsonUtility.FromJson<CurveData>(values[0])?.curve; }
                            catch (ArgumentException) { }
                            field.SetValueWithoutNotify(curve ?? AnimationCurve.Linear(0, 0, 1, 1));
                        }
                        field.showMixedValue = !draft && values.Distinct().Skip(1).Any();
                        apply.SetEnabled(draft && Model.CanEdit(property));
                    });
                    field.RegisterValueChangedCallback(e => { draft = true; field.showMixedValue = false; apply.SetEnabled(Model.CanEdit(property)); });
                    apply.clicked += () => {
                        if (!draft || parent.panel == null || !Model.CanEdit(property)) return;
                        var settings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(property.Options.texture ?? new TextureData()));
                        var texture=Converter.CurveToTexture(field.value,settings);
                        try
                        {
                            var asset=TextureHelper.SaveTextureAsPNG(texture,PATH.TEXTURES_DIR+"/curves/"+key+"_"+Guid.NewGuid().ToString("N")+".png",settings);
                            string json = JsonUtility.ToJson(new CurveData { curve = field.value });
                            var targets = property.MaterialProperty.targets.OfType<Material>().Where(m => Model.Shader.Materials.Contains(m) && m.HasProperty(property.MaterialProperty.name)).ToArray();
                            Model.Edit(property, p =>
                            {
                                var material = p.targets.OfType<Material>().FirstOrDefault();
                                if (material == null || !targets.Contains(material)) return;
                                Undo.RegisterCompleteObjectUndo(material, "Apply texture curve");
                                p.textureValue = asset; material.SetOverrideTag(property.MaterialProperty.name + "_curve", json);
                            }, true);
                            draft = false; apply.SetEnabled(false);
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
            string Text(string key, string fallback) => RetainedText.Get(fields.Model.Shader, key, fallback);
            Action<int, Action<InlinePackerChannelConfig>> change = (index, mutation) => RunRetainedPackerAction(() => ChangeRetainedChannel(index, mutation));
            _prop=property.MaterialProperty;
            _current=new ThryRGBAPackerData(); fields.Model.Shader.ActivateRetained(); Init(); LoadLabels();
            InitializeRetainedPacker(fields.Model, property);
            var root=new VisualElement { name = "texture-channel-inputs" }; root.AddToClassList("thry-packer"); RetainedFields.StyleSpecialControls(root);
            var heading = new VisualElement(); heading.AddToClassList("thry-packer-heading"); root.Add(heading);
            var headingLabel = new Label(Text("packer_channel_inputs", "Channel inputs")); headingLabel.AddToClassList("thry-packer-heading-label"); heading.Add(headingLabel);
            _retainedPackerMessage = new Label { name = "packer-error" }; _retainedPackerMessage.style.whiteSpace = WhiteSpace.Normal;
            _retainedPackerMessage.style.display = DisplayStyle.None; root.Add(_retainedPackerMessage);
            var channels=new[]{_current._input_r,_current._input_g,_current._input_b,_current._input_a};
            Undo.UndoRedoCallback refreshAfterUndo = RefreshRetainedPacker;
            fields.Track(root, RefreshRetainedPackerIfChanged);
#if UNITY_2022_1_OR_NEWER
            // Init also serves the immediate inspector. Retained subscriptions
            // follow panel attachment so temporary removal can safely reattach.
            Undo.undoRedoEvent -= OnUndoRedo;
#endif
            root.RegisterCallback<AttachToPanelEvent>(e =>
            {
                Undo.undoRedoPerformed -= refreshAfterUndo;
                Undo.undoRedoPerformed += refreshAfterUndo;
                refreshAfterUndo();
            });
            var labels=new[]{_label1,_label2,_label3,_label4};
            for(int i=0;i<4;i++)
            {
                int channelIndex = i;
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
                fields.Track(texture,()=>{ texture.SetValueWithoutNotify(input.Source.Texture); texture.showMixedValue = RetainedChannelMixed(channelIndex, c => c.Source.Texture); });
                texture.RegisterValueChangedCallback(e=>change(channelIndex, c => c.Source.SetInputTexture(e.newValue as Texture2D)));
                fields.TextureAssetDisplay(texture, () => input.Source.Texture, () => RetainedChannelMixed(channelIndex, c => c.Source.Texture),
                    () => fields.Model.CanEdit(property), () => texture.value = null);
                texture.RegisterCallback<GeometryChangedEvent>(e => texture.EnableInClassList("thry-packer-source-compact", e.newRect.width < 210));
                VisualElement invertInput, remapInput;
                var channelValues = Enum.GetValues(typeof(TexturePacker.TextureChannelIn)).Cast<TexturePacker.TextureChannelIn>().ToArray();
                var channel=new DropdownField((rgbSource ? new[] { "RGB" } : channelValues.Select(item => RetainedText.EnumCaption(typeof(TexturePacker.TextureChannelIn), item.ToString())).ToArray()).ToList(),0)
                    { name = "packer-source-channel-" + i, tooltip = "Source channel to read into the " + outputChannel + " output" };
                channel.AddToClassList("thry-packer-source-channel"); view.UseInspectorMenu(channel); value.Add(channel);
                channel.SetEnabled(!rgbSource);
                fields.Track(channel,()=> {
                    channel.SetValueWithoutNotify(rgbSource ? "RGB" : RetainedText.EnumCaption(typeof(TexturePacker.TextureChannelIn), input.Channel.ToString()));
                    channel.showMixedValue = !rgbSource && RetainedChannelMixed(channelIndex, c => c.Channel);
                    channel.style.display = input.Source.Texture != null ? DisplayStyle.Flex : DisplayStyle.None;
                });
                channel.RegisterValueChangedCallback(e=>{ if (!rgbSource && channel.index >= 0 && channel.index < channelValues.Length) change(channelIndex, c => c.Channel=channelValues[channel.index]); });
                var fallback = new FloatField { name = "packer-source-fallback-" + i, isDelayed = true,
                    tooltip = "Fallback value (0–1) used for the " + outputChannel + " output when no texture is assigned" };
                fallback.AddToClassList("thry-packer-source-fallback"); value.Add(fallback);
                fields.Track(fallback, () => {
                    fallback.SetValueWithoutNotify(input.Fallback);
                    fallback.showMixedValue = RetainedChannelMixed(channelIndex, c => c.Fallback);
                    fallback.style.display = input.Source.Texture == null ? DisplayStyle.Flex : DisplayStyle.None;
                });
                fallback.RegisterValueChangedCallback(e => {
                    float next = float.IsNaN(e.newValue) ? 0 : Mathf.Clamp01(e.newValue);
                    fallback.SetValueWithoutNotify(next);
                    if (!fields.Model.CanEdit(property) || (input.Fallback == next && !RetainedChannelMixed(channelIndex, c => c.Fallback))) return;
                    change(channelIndex, c => c.Fallback = next);
                });
                detail.Add(RetainedFields.Row(Text("packer_invert", "Invert"),out invertInput));
                var invert=new Toggle();invertInput.Add(invert);fields.Track(invert,()=>{invert.SetValueWithoutNotify(input.Invert);invert.showMixedValue=RetainedChannelMixed(channelIndex,c=>c.Invert);});invert.RegisterValueChangedCallback(e=>change(channelIndex,c=>c.Invert=e.newValue));
                detail.Add(RetainedFields.Row(Text("packer_remap", "Remap"),out remapInput));
                var remap=new Vector4Field { tooltip=Text("packer_remap_hint", "X / Y: input minimum and maximum. Z / W: output minimum and maximum.") };remapInput.Add(remap);fields.Track(remap,()=>{remap.SetValueWithoutNotify(input.Remapping);remap.showMixedValue=RetainedChannelMixed(channelIndex,c=>c.Remapping);});remap.RegisterValueChangedCallback(e=>change(channelIndex,c=>c.Remapping=e.newValue));
            }
            var actions=new VisualElement();actions.AddToClassList("thry-components");actions.AddToClassList("thry-packer-actions");root.Add(actions);
            string[] names={"Merge","Revert","Clear"};Action[] callbacks={MergeRetainedPacker,RevertRetainedPacker,ClearRetainedPacker};
            for(int i=0;i<names.Length;i++)
            {
                int index=i;var button=new Button(()=>RunRetainedPackerAction(callbacks[index])) {text=Text("packer_" + names[i].ToLowerInvariant(), names[i]),name="packer-"+names[i].ToLowerInvariant()};button.style.flexGrow=1;actions.Add(button);
                fields.Track(button,()=>button.SetEnabled(fields.Model.CanEdit(property) && (index == 0 ? _current._hasConfigChanged : index == 1 ? _current._hasTextureChanged
                    : RetainedTargets().Any(m=>m.GetTexture(property.MaterialProperty.name)!=null)||channels.Any(c=>c.Source.Texture!=null))));
            }
            var advanced = new Button(() =>
            {
                fields.Model.Shader.ActivateRetained();
                if (!fields.Model.CanEdit(property)) return;
                var studio = TexturePacker.NodeGUI.Open(RetainedStudioConfig());
                // Preview stays in the studio; only a saved asset is assigned to the material.
                studio.OnSave += texture =>
                {
                    if (root.panel == null || !fields.Model.CanEdit(property)) return;
                    if (texture == null) return;
                    fields.Model.Edit(property, p =>
                    {
                        var material = p.targets.OfType<Material>().FirstOrDefault();
                        if (material == null) return;
                        Undo.RegisterCompleteObjectUndo(material, "Save texture channels");
                        p.textureValue = texture; material.SetOverrideTag(SavedInputsTag, RetainedInputSignature(material));
                        material.SetOverrideTag(PreviewStateTag, "");
                    }, true);
                    RefreshRetainedPacker();
                };
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
