using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.Drawers
{
    public sealed class ThryMaskLevelsDecorator : MaterialPropertyDrawer
    {
        public ThryMaskLevelsDecorator(float channels) { }
        public ThryMaskLevelsDecorator(float channels,string invert) { }
        public override float GetPropertyHeight(MaterialProperty prop,string label,MaterialEditor editor)
        { return 0; }
        public override void OnGUI(Rect position,MaterialProperty prop,GUIContent label,MaterialEditor editor)
        { }
    }
}
namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        void MaskLevels(VisualElement parent, ShaderProperty texture, DrawerAttribute attribute)
        {
            var properties = new ShaderProperty[MaskLevelsData.Suffixes.Length];
            for(int i=0;i<properties.Length;i++)
                if(!Model.Shader.PropertyDictionary.TryGetValue(i==5 && attribute.Args.Length>1 ? attribute.Args[1] : MaskLevelsData.Name(texture.MaterialProperty.name,i),out properties[i]))return;
            int channels=15; if(attribute.Args.Length>0)int.TryParse(attribute.Args[0],out channels);
            var levels=new RetainedMaskLevels(Model,texture,properties,channels,ShowPropertyMenu);
            parent.Add(levels); TrackVisible(levels,levels.Synchronize);
        }
    }
    internal sealed class RetainedMaskLevels : VisualElement
    {
        readonly RetainedMaterialModel model;
        readonly ShaderProperty textureProperty;
        readonly ShaderProperty[] properties;
        readonly int activeChannels;
        bool LegacyInvert => properties[5].MaterialProperty.type != MaterialProperty.PropType.Vector;
        readonly Action<VisualElement,ShaderProperty> propertyMenu;
        readonly MaskLevelsPreview preview=new MaskLevelsPreview();
        readonly Vector4[] values=new Vector4[9];
        readonly bool[,] mixed=new bool[9,4];
        readonly List<Button> tabs=new List<Button>();
        readonly List<int> tabChannels=new List<int>();
        readonly List<FloatField> numbers=new List<FloatField>();
        readonly MaskLevelsTrack input,output;
        readonly Toggle normalize,invert;
        readonly VisualElement previews;
        readonly Image before,after;
        readonly Button previewButton, bakeButton;
        int channel=-1;
        bool showPreview, initialized;
        Texture lastTexture; uint lastVersion;
        int Current { get { for(int c=0;c<4;c++)if(channel==c || (channel<0 && (activeChannels&(1<<c))!=0))return c;return 0; } }
        bool Applies(int c,int selected) => (activeChannels&(1<<c))!=0 && (selected<0||selected==c);
        public RetainedMaskLevels(RetainedMaterialModel model,ShaderProperty texture,ShaderProperty[] properties,int activeChannels,Action<VisualElement,ShaderProperty> propertyMenu)
        {
            this.model=model;textureProperty=texture;this.properties=properties;this.activeChannels=activeChannels;this.propertyMenu=propertyMenu;
            name="mask-levels-"+texture.MaterialProperty.name;
            AddToClassList("thry-property"); // Its menus belong to these controls, not the parent texture.
            AddToClassList("thry-inspector");AddToClassList("poi-mask-levels");
            styleSheets.Add(Resources.Load<StyleSheet>("MaskLevels"));RetainedAppearance.Install(this);
            Array.Copy(MaskLevelsData.Defaults,values,9);
            var toolbar=Row(this);toolbar.AddToClassList("poi-levels-tabs");
            int count=Enumerable.Range(0,4).Count(c=>(activeChannels&(1<<c))!=0);
            if(count==1)channel=Current;
            for(int c=-1;c<4;c++)
            {
                if(c<0 && count==1 || c>=0 && (activeChannels&(1<<c))==0)continue;
                int selected=c;var button=new Button(()=>{channel=selected;RefreshControls();Render();}){text=c<0?"RGBA":"RGBA"[c].ToString()};
                button.AddToClassList("poi-levels-tab");
                Menu(button,null,()=>Reset(selected));
                button.RegisterCallback<KeyDownEvent>(e=>{
                    int index=tabChannels.IndexOf(channel);
                    if(e.keyCode!=KeyCode.LeftArrow&&e.keyCode!=KeyCode.RightArrow)return;
                    index=Mathf.Clamp(index+(e.keyCode==KeyCode.RightArrow?1:-1),0,tabs.Count-1);
                    channel=tabChannels[index];RefreshControls();Render();tabs[index].Focus();e.StopPropagation();e.PreventDefault();
                });
                tabs.Add(button);tabChannels.Add(c);toolbar.Add(button);
            }
            previewButton=new Button(()=>{showPreview=!showPreview;UpdatePreviewVisibility();});
            previewButton.AddToClassList("poi-levels-preview-toggle");
            var previewIcon=new Image {image=EditorGUIUtility.IconContent("scenevis_visible_hover").image,pickingMode=PickingMode.Ignore};
            previewIcon.style.width=16;previewIcon.style.height=16;previewButton.Add(previewIcon);
            var previewCaption=new Label("Preview"){pickingMode=PickingMode.Ignore};
            previewCaption.AddToClassList("poi-levels-preview-caption");previewButton.Add(previewCaption);
            previewButton.tooltip="Show original and adjusted masks";
            toolbar.Add(previewButton);
            var page=new VisualElement();page.AddToClassList("poi-levels-page");Add(page);
            previews=Row(page);previews.AddToClassList("poi-levels-previews");before=Image(previews,"Original");after=Image(previews,"Adjusted");previews.style.display=DisplayStyle.None;
            var inputRow=Row(page);inputRow.Add(new Label("Input"){style={width=62}});
            input=new MaskLevelsTrack(()=>new[]{values[0][Current],Display(1,Current),values[1][Current]},SetInput,()=>Undo.IncrementCurrentGroup());inputRow.Add(input);
            Menu(input,null,()=>ResetRow(true));
            for(int i=0;i<3;i++)Number(inputRow,i);
            var outputRow=Row(page);outputRow.Add(new Label("Output"){style={width=62}});
            output=new MaskLevelsTrack(()=>new[]{values[3][Current],values[4][Current]},(i,v)=>Edit(i+3,v),()=>Undo.IncrementCurrentGroup());outputRow.Add(output);
            Menu(output,null,()=>ResetRow(false));Number(outputRow,3);Number(outputRow,4);
            var options=Row(page);
            bakeButton=new Button(Bake){text="Bake",name="mask-bake"};options.Add(bakeButton);
            var optionsSpacer=new VisualElement();optionsSpacer.style.flexGrow=1;options.Add(optionsSpacer);
            normalize=new Toggle("Normalize"){tooltip="Stretch each channel’s darkest and brightest values to the output range."};
            normalize.RegisterValueChangedCallback(e=>Edit(6,e.newValue?1:0));options.Add(normalize);Menu(normalize,properties[6],()=>Edit(6,0));
            invert=new Toggle("Invert");invert.RegisterValueChangedCallback(e=>Edit(5,e.newValue?1:0));options.Add(invert);Menu(invert,properties[5],()=>Edit(5,0));
            RegisterCallback<DetachFromPanelEvent>(e=>{if(e.target==this){preview.Dispose();initialized=false;}});
            Synchronize();
        }
        static VisualElement Row(VisualElement parent){var row=new VisualElement();row.AddToClassList("poi-levels-row");parent.Add(row);return row;}
        static Image Image(VisualElement parent,string label){var box=new VisualElement();box.style.flexGrow=1;box.style.flexBasis=0;parent.Add(box);box.Add(new Label(label));var image=new Image{scaleMode=ScaleMode.ScaleToFit};image.AddToClassList("poi-levels-image");box.Add(image);return image;}
        void Menu(VisualElement control,ShaderProperty property,Action reset)
        {
            control.AddManipulator(new ContextualMenuManipulator(e=>{
                e.menu.AppendAction("Reset",a=>reset(),a=>CanEdit()?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
                if(property!=null)e.menu.AppendAction("Property Options…",a=>propertyMenu(control,property));
                e.StopPropagation();
            }));
        }
        bool CanEdit()=>properties.Take(7).Any(model.CanEdit);
        void Bake()
        {
            var owners=model.Owners(textureProperty).Where(m=>m!=null).ToArray();
            if(!model.CanEdit(textureProperty) || properties.Take(7).Where((p,i)=>i!=5 || !LegacyInvert).Any(p=>!model.CanEdit(p)))return;
            Dictionary<Material,Texture2D> baked=null;
            int undo=-1;
            try
            {
                baked=MaskLevelsBaker.Export(owners,textureProperty.MaterialProperty.name,activeChannels);
                Undo.IncrementCurrentGroup();undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Bake mask adjustments");
                model.Edit(textureProperty,p=>{
                    var owner=p.targets.OfType<Material>().Single();
                    p.textureValue=baked[owner];
                    MaskLevelsBaker.Reset(owner,textureProperty.MaterialProperty.name,activeChannels);
                },true);
                Undo.SetCurrentGroupName("Bake mask adjustments");
                Undo.CollapseUndoOperations(undo);
                Synchronize();
            }
            catch(Exception exception)
            {
                if(undo>=0)Undo.RevertAllDownToGroup(undo);
                if(baked!=null)foreach(var texture in baked.Values.Distinct())AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(texture));
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Mask bake failed",exception.Message,"OK");
            }
        }
        static int PropertyIndex(int field)=>field==0?0:field==1?2:field==2?1:field;
        void Number(VisualElement parent,int index)
        {
            var field=new FloatField {name="mask-level-"+index,isDelayed=true};field.AddToClassList("poi-levels-number");field.AddToClassList("thry-input");parent.Add(field);numbers.Add(field);
            field.tooltip=new[]{"Black point","Midpoint (input that becomes 50%)","White point","Output minimum","Output maximum"}[index];
            field.RegisterValueChangedCallback(e=>{if(index<3)SetInput(index,e.newValue);else Edit(index,e.newValue);});
            int p=PropertyIndex(index);Menu(field,properties[p],()=>Edit(p,MaskLevelsData.Defaults[p][Current]));
        }
        void SetInput(int index,float value)=>Edit(PropertyIndex(index),value,index==1);
        void Edit(int index,float value,bool absoluteMidpoint=false,int? selectedChannel=null)
        {
            if(float.IsNaN(value)||float.IsInfinity(value)||!model.CanEdit(properties[index])){RefreshControls();return;}
            int selected=selectedChannel??channel;
            if(index==5 && LegacyInvert) { model.Number(properties[5],value); Synchronize(); return; }
            model.EditSingleProperty(properties[index],p=>{
                var v=p.vectorValue;var owner=p.targets.OfType<Material>().First();
                var min=owner.GetVector(MaskLevelsData.Name(textureProperty.MaterialProperty.name,index==3||index==4?3:0));
                var max=owner.GetVector(MaskLevelsData.Name(textureProperty.MaterialProperty.name,index==3||index==4?4:1));
                for(int c=0;c<4;c++)if(Applies(c,selected))
                {
                    float next=value;
                    if(absoluteMidpoint)next=Mathf.InverseLerp(min[c],max[c],value);
                    v[c]=index==0||index==3?Mathf.Clamp(next,0,max[c]):index==1||index==4?Mathf.Clamp(next,min[c],1):index==2?Mathf.Clamp(next,.01f,.99f):next;
                }
                p.vectorValue=v;
            },true);
            Synchronize();
        }
        void Reset(int selected)
        {
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            for(int i=0;i<7;i++)Edit(i,MaskLevelsData.Defaults[i][0],false,selected);
            Undo.CollapseUndoOperations(group);
        }
        void ResetRow(bool isInput)
        {Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();for(int i=isInput?0:3;i<(isInput?3:5);i++)Edit(i,MaskLevelsData.Defaults[i][0]);Undo.CollapseUndoOperations(group);}
        float Display(int field,int c)=>field==1?Mathf.Lerp(values[0][c],values[1][c],values[2][c]):values[PropertyIndex(field)][c];
        bool Mixed(int index)
        {
            int first=Current;
            for(int c=0;c<4;c++)if(Applies(c,channel)&&(mixed[index,c]||values[index][c]!=values[index][first]))return true;
            return false;
        }
        void RefreshControls()
        {
            for(int i=0;i<numbers.Count;i++)
            {numbers[i].SetValueWithoutNotify(Display(i,Current));numbers[i].showMixedValue=Mixed(PropertyIndex(i))||(i==1&&(Mixed(0)||Mixed(1)));numbers[i].SetEnabled(model.CanEdit(properties[PropertyIndex(i)]));}
            for(int i=0;i<tabs.Count;i++)tabs[i].EnableInClassList("thry-selected",tabChannels[i]==channel);
            input.SetEnabled(model.CanEdit(properties[0])&&model.CanEdit(properties[1])&&model.CanEdit(properties[2]));output.SetEnabled(model.CanEdit(properties[3])&&model.CanEdit(properties[4]));
            input.RefreshHandles();output.RefreshHandles();
            normalize.SetValueWithoutNotify(values[6][Current]>.5f);normalize.showMixedValue=Mixed(6);normalize.SetEnabled(model.CanEdit(properties[6]));
            invert.SetValueWithoutNotify(values[5][Current]>.5f);invert.showMixedValue=Mixed(5);invert.SetEnabled(model.CanEdit(properties[5]));
        }
        internal void Synchronize()
        {
            var owners=model.Owners(textureProperty).Where(m=>m!=null).ToArray();if(owners.Length==0)return;
            bool changed=!initialized;
            foreach(var owner in owners)if(!ShaderOptimizer.IsShaderLocked(owner.shader))changed|=MaskLevelsData.SynchronizeBounds(owner,textureProperty.MaterialProperty.name);
            for(int i=0;i<9;i++)
            {
                var first=i==5&&LegacyInvert ? Vector4.one*properties[i].MaterialProperty.GetNumber() : properties[i].MaterialProperty.vectorValue;
                // Bounds are authoring-derived, so read their freshly computed material values.
                if(i>=7)first=owners[0].GetVector(properties[i].MaterialProperty.name);
                if(first!=values[i])changed=true;values[i]=first;
                for(int c=0;c<4;c++)
                {
                    bool differs=owners.Any(m=>(i==5&&LegacyInvert?m.GetFloat(properties[i].MaterialProperty.name):m.GetVector(properties[i].MaterialProperty.name)[c])!=first[c]);
                    if(differs!=mixed[i,c])changed=true;mixed[i,c]=differs;
                }
            }
            var texture=owners[0].GetTexture(textureProperty.MaterialProperty.name);
            uint version=texture!=null?texture.updateCount:0;
            changed|=texture!=lastTexture||version!=lastVersion;lastTexture=texture;lastVersion=version;
            previewButton.style.display=texture!=null?DisplayStyle.Flex:DisplayStyle.None;
            string bakeReason=MaskLevelsBaker.UnavailableReason(owners,textureProperty.MaterialProperty.name);
            bool editable=model.CanEdit(textureProperty) && properties.Take(7).Where((p,i)=>i!=5 || !LegacyInvert).All(model.CanEdit);
            bakeButton.SetEnabled(editable && bakeReason==null);
            bakeButton.tooltip=bakeReason ?? (editable ? "Save adjustments to a new texture and reset the baked controls." : "These mask controls cannot be edited.");
            if(texture==null && showPreview) { showPreview=false; UpdatePreviewVisibility(); }
            RefreshControls();if(changed)Render();initialized=true;
        }
        void UpdatePreviewVisibility()
        {
            previews.style.display=showPreview?DisplayStyle.Flex:DisplayStyle.None;
            previewButton.tooltip=showPreview?"Hide mask previews":"Show original and adjusted masks";
            previewButton.EnableInClassList("thry-selected",showPreview);
            if(showPreview) Render();
            else { before.image=null; after.image=null; preview.Dispose(); }
        }
        void Render(){if(!showPreview)return;preview.Render(lastTexture,values,channel,LegacyInvert);before.image=preview.Before;after.image=preview.After;before.MarkDirtyRepaint();after.MarkDirtyRepaint();}
    }
}
