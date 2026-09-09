#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        internal static void StyleSpecialControls(VisualElement root)
        {
            var sheet = Resources.Load<StyleSheet>("ThrySpecialControls");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }
        private static int ReadBitValue(Material material, ShaderProperty property)
        {
#if UNITY_2022_1_OR_NEWER
            if (property.MaterialProperty.type == MaterialProperty.PropType.Int) return material.GetInteger(property.MaterialProperty.name);
#endif
            return (int)material.GetFloat(property.MaterialProperty.name);
        }
        partial void SpecialFields(VisualElement parent, ShaderProperty property, DrawerAttribute[] attributes, ref bool handled)
        {
            if(property is InstancingProperty || property is DSGIProperty)
            {
                bool instancing=property is InstancingProperty;var field=new Toggle();parent.Add(field);
                Func<Material,bool> read=m=>instancing?m.enableInstancing:m.doubleSidedGI;
                Track(field,()=>{field.SetValueWithoutNotify(read(Model.Shader.Materials[0]));field.showMixedValue=Model.Shader.Materials.Select(read).Distinct().Skip(1).Any();});
                field.RegisterValueChangedCallback(e=>Model.Mutate(property.Content.text,m=>{if(instancing)m.enableInstancing=e.newValue;else m.doubleSidedGI=e.newValue;}));handled=true;return;
            }
            if(property is GIProperty)
            {
                var field=new DropdownField(GIProperty.lightmapEmissiveStrings.Select(c=>c.text).ToList(),0);_view.UseInspectorMenu(field);parent.Add(field);
                Track(field,()=>{int value=(int)(Model.Shader.Materials[0].globalIlluminationFlags&MaterialGlobalIlluminationFlags.AnyEmissive);int index=Array.IndexOf(GIProperty.lightmapEmissiveValues,value);field.SetValueWithoutNotify(index<0?"—":field.choices[index]);field.showMixedValue=Model.Shader.Materials.Select(m=>m.globalIlluminationFlags&MaterialGlobalIlluminationFlags.AnyEmissive).Distinct().Skip(1).Any();});
                field.RegisterValueChangedCallback(e=>{if(field.index>=0)Model.Mutate("Global Illumination",m=>{m.globalIlluminationFlags=(MaterialGlobalIlluminationFlags)GIProperty.lightmapEmissiveValues[field.index];GIProperty.FixupEmissiveFlag(m);});});handled=true;return;
            }
            foreach (var attribute in attributes)
            {
                switch(attribute.Name)
                {
                    case "Vector3Slider":
                        var vectorSliderLabels = property.Content.text.Split('|');
                        var vectorSliderLabel = parent.parent.Q<Label>(className: "thry-property-label");
                        Track(vectorSliderLabel, () => vectorSliderLabel.text = property.Content.text.Split('|')[0]);
                        Vector(parent, property, new[] { "X", "Y", "Z" });
                        VisualElement lengthInput;
                        parent.parent.parent.Add(Row(vectorSliderLabels.Length > 1 ? vectorSliderLabels[1] : "Length", out lengthInput));
                        float lengthMin = attribute.Args.Length > 0 ? DrawerAttribute.Number(attribute.Args[0]) : 0;
                        float lengthMax = attribute.Args.Length > 1 ? DrawerAttribute.Number(attribute.Args[1]) : 1;
                        bool unboundedLength = attribute.Args.Length > 2 && DrawerAttribute.Number(attribute.Args[2]) == 1;
                        var lengthSlider = new Slider(lengthMin, lengthMax) { name = "value-" + property.MaterialProperty.name + "-length" };
                        lengthInput.style.flexDirection = FlexDirection.Row; lengthInput.style.alignItems = Align.Center;
                        lengthSlider.style.flexGrow = 1; lengthSlider.style.flexShrink = 1; lengthSlider.style.minWidth = 0;
                        var lengthNumber = new FloatField { name = "component-3" };
                        lengthNumber.style.width = 54; lengthNumber.style.flexShrink = 0; lengthNumber.style.marginLeft = 6;
                        lengthInput.Add(lengthSlider); lengthInput.Add(lengthNumber);
                        Track(lengthSlider, () =>
                        {
                            float length = property.MaterialProperty.vectorValue.w;
                            bool mixed = Model.Shader.Materials.Where(m => m.HasProperty(property.MaterialProperty.name)).Select(m => m.GetVector(property.MaterialProperty.name).w).Distinct().Skip(1).Any();
                            lengthSlider.SetValueWithoutNotify(length); lengthNumber.SetValueWithoutNotify(length);
                            lengthSlider.showMixedValue = mixed; lengthNumber.showMixedValue = mixed;
                        });
                        lengthSlider.RegisterValueChangedCallback(e => Model.VectorComponent(property, 3, Mathf.Clamp(e.newValue, lengthMin, lengthMax)));
                        lengthNumber.RegisterValueChangedCallback(e => Model.VectorComponent(property, 3, unboundedLength ? e.newValue : Mathf.Clamp(e.newValue, lengthMin, lengthMax)));
                        handled = true; return;
                    case "Vector31":
                        var labels31 = property.Content.text.Split('|');
                        var label31 = parent.parent.Q<Label>(className: "thry-property-label");
                        if (label31 != null) label31.text = labels31[0];
                        Vector(parent, property, new[] { "X", "Y", "Z" });
                        VisualElement scalar31;
                        parent.parent.parent.Add(Row(labels31.Length > 1 ? labels31[1] : "W", out scalar31));
                        Vector(scalar31, property, new[] { "" }, 3);
                        handled = true; return;
                    case "MultiSlider":
                        MinMax(parent,property,0,property.MaterialProperty.vectorValue.z,property.MaterialProperty.vectorValue.w); handled=true; return;
                    case "VectorToSliders":
                        var args = attribute.Args; bool pairs = args.Length % 3 == 1; int offset = pairs ? 1 : 0;
                        StyleSpecialControls(parent);
                        parent.parent.style.alignItems = Align.FlexStart;
                        parent.parent.Q<Label>(className: "thry-property-label").style.marginTop = 3;
                        for(int n=offset;n+2<args.Length;n+=3)
                        {
                            int component = (n-offset)/3;
                            var group = new VisualElement();
                            if (!pairs) group.AddToClassList("thry-special-slider-row");
                            var componentLabel = new Label(args[n]) { tooltip = args[n] }; componentLabel.AddToClassList("thry-special-component-label");
                            group.Add(componentLabel); parent.Add(group);
                            if(pairs) MinMax(group,property,component*2,DrawerAttribute.Number(args[n+1]),DrawerAttribute.Number(args[n+2]));
                            else { var slider=new Slider(DrawerAttribute.Number(args[n+1]),DrawerAttribute.Number(args[n+2])) { showInputField=true, name="component-slider-"+component }; group.Add(slider);
                                Track(slider,()=>{slider.SetValueWithoutNotify(property.MaterialProperty.vectorValue[component]); slider.showMixedValue=Model.Shader.Materials.Where(m=>m.HasProperty(property.MaterialProperty.name)).Select(m=>m.GetVector(property.MaterialProperty.name)[component]).Distinct().Skip(1).Any();});
                                slider.RegisterValueChangedCallback(e=>Model.VectorComponent(property,component,e.newValue)); }
                        }
                        handled=true; return;
                    case "ButtonVector": case "Vector4Toggles":
                        var buttons = new VisualElement(); buttons.AddToClassList("thry-components"); parent.Add(buttons);
                        for(int i=0;i<4;i++)
                        {
                            int index=i; string label=attribute.Args.Length>i?attribute.Args[i]:new[]{"X","Y","Z","W"}[i];
                            if(label=="NA") continue;
                            var button = new Button(()=>Model.VectorComponent(property,index,property.MaterialProperty.vectorValue[index]==0?1:0)) { text=label }; button.style.flexGrow=1;
                            Track(button,()=>button.EnableInClassList("thry-selected",property.MaterialProperty.vectorValue[index]!=0)); buttons.Add(button);
                        }
                        handled=true; return;
                    case "ThryMultiFloatHeaderDrawer":
                        var headers = new VisualElement(); headers.AddToClassList("thry-components"); parent.Add(headers);
                        StyleSpecialControls(headers); headers.AddToClassList("thry-multi-value-header");
                        foreach(var label in attribute.Args) { var heading=new Label(label) {tooltip=label}; headers.Add(heading); }
                        handled=true; return;
                    case "ThryMultiFloatButtons": case "ThryMultiFloats":
                        var multi = new VisualElement(); multi.AddToClassList("thry-components"); parent.Add(multi);
                        StyleSpecialControls(multi); multi.AddToClassList("thry-multi-value-controls");
                        bool tiles=attribute.Name=="ThryMultiFloatButtons";
                        string[] ids = new[]{property.MaterialProperty.name}.Concat(attribute.Args.Skip(tiles?4:1)).ToArray();
                        property.AdditionalDefaultCheckProperties=ids;
                        for(int i=0;i<ids.Length;i++)
                        {
                            ShaderProperty target; if(!Model.Shader.PropertyDictionary.TryGetValue(ids[i],out target)) continue;
                            // The authored label is a UV coordinate; the grid reads it as row and column.
                            if(tiles) { string defaultLabel = Drawers.TileLabelUtility.FormatTileLabel(attribute.Args[i]);
                                var button=new Button(()=>{Model.Number(target,target.MaterialProperty.GetNumber()==0?1:0); foreach(var id in ids) Model.Shader.PropertyDictionary[id].SetAnimated(target.IsAnimated,target.IsRenaming);}) {text=defaultLabel,name="tile-"+target.MaterialProperty.name}; button.style.flexGrow=1;
                                Track(button,()=>{button.EnableInClassList("thry-selected",target.MaterialProperty.GetNumber()!=0&&!target.MaterialProperty.hasMixedValue); button.EnableInClassList("thry-tile-mixed",target.MaterialProperty.hasMixedValue); button.text = Drawers.TileLabelUtility.GetTileLabel(Model.Shader.Materials[0], target.MaterialProperty.name) ?? defaultLabel;});
                                if (Drawers.TileLabelUtility.IsUdimProperty(target.MaterialProperty.name))
                                {
                                    button.tooltip = Drawers.TileLabelUtility.ROW_TOOLTIP;
                                    button.RegisterCallback<PointerDownEvent>(e =>
                                    {
                                        if (e.button != 1) return;
                                        RetainedMenu.Open(button.worldBound, button, new[]
                                        {
                                            new RetainedMenu.Item { Text = "Rename tile", Action = () => Drawers.TileLabelUtility.TileLabelRenamePopup.Show(Model.Editor.targets, Drawers.TileLabelUtility.CanonicalPropertyName(target.MaterialProperty.name), defaultLabel, EditorWindow.focusedWindow.position.position + button.worldBound.position) },
                                            new RetainedMenu.Item { Text = "Reset label", Action = () => Drawers.TileLabelUtility.ApplyTagToTargets(Model.Editor.targets, Drawers.TileLabelUtility.CanonicalPropertyName(target.MaterialProperty.name), "") }
                                        });
                                        e.StopPropagation();
                                    });
                                }
                                multi.Add(button); }
                            else if(attribute.Args[0]=="1"||attribute.Args[0].Equals("true",StringComparison.OrdinalIgnoreCase)) Toggle(multi,target);
                            else { var number=new FloatField(); Bind(number,target,p=>p.GetNumber(),(p,v)=>p.SetNumber(v)); multi.Add(number); }
                        }
                        handled=true; return;
                    case "ByteSlider": case "ByteBitField":
                        StyleSpecialControls(parent);
                        parent.parent.style.alignItems = Align.FlexStart;
                        parent.parent.Q<Label>(className:"thry-property-label").style.marginTop = 3;
                        if(attribute.Name=="ByteSlider") { var slider=new SliderInt(0,255) {showInputField=true}; Bind(slider,property,p=>(int)p.GetNumber(),(p,v)=>p.SetNumber(v)); parent.Add(slider); }
                        var bits=new Foldout {text="Bits",value=false,name="byte-bits-"+property.MaterialProperty.name};bits.AddToClassList("thry-bit-options");parent.Add(bits);
                        var bitrow=new VisualElement();bitrow.AddToClassList("thry-bit-row");bits.Add(bitrow);
                        for(int bit=7;bit>=0;bit--) { int mask=1<<bit; var toggle=new Toggle(bit.ToString()) {name="byte-bit-"+bit,tooltip="Bit "+bit+" · value "+mask};toggle.AddToClassList("thry-bit-cell");
                            Track(toggle,()=>{toggle.SetValueWithoutNotify(((int)property.MaterialProperty.GetNumber()&mask)!=0);toggle.showMixedValue=Model.Shader.Materials.Where(m=>m.HasProperty(property.MaterialProperty.name)).Select(m=>(ReadBitValue(m,property)&mask)!=0).Distinct().Skip(1).Any();});
                            toggle.RegisterValueChangedCallback(e=>Model.Edit(property,p=>p.SetNumber(e.newValue?(int)p.GetNumber()|mask:(int)p.GetNumber()&~mask),true)); bitrow.Add(toggle); }
                        handled=true; return;
                    case "Curve4":
                        var curve = new CurveField(); parent.Add(curve); Vector4 last=new Vector4(float.NaN,0,0,0);
                        Track(curve,()=>{ curve.showMixedValue=property.MaterialProperty.hasMixedValue; var v=property.MaterialProperty.vectorValue; if(last==v) return; last=v; var c=new AnimationCurve(new Keyframe(0,v.x),new Keyframe(1f/3,v.y),new Keyframe(2f/3,v.z),new Keyframe(1,v.w));
                            for(int i=0;i<4;i++){AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.ClampedAuto);} curve.SetValueWithoutNotify(c); curve.showMixedValue=property.MaterialProperty.hasMixedValue; });
                        curve.RegisterValueChangedCallback(e=>Model.Edit(property,p=>p.vectorValue=new Vector4(Mathf.Clamp01(e.newValue.Evaluate(0)),Mathf.Clamp01(e.newValue.Evaluate(1f/3)),Mathf.Clamp01(e.newValue.Evaluate(2f/3)),Mathf.Clamp01(e.newValue.Evaluate(1)))));
                        handled=true; return;
                    case "Ramp4":
                        var ramp=new RetainedRamp(()=>property.MaterialProperty.vectorValue,v=>Model.Edit(property,p=>p.vectorValue=v),attribute.Args); parent.Add(ramp); Track(ramp,ramp.MarkDirtyRepaint);
                        Vector(parent,property,new[]{"V0","V1","T0","T1"}); handled=true; return;
                    case "ThryMask":
                        var type=AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=> {try{return a.GetTypes();}catch(System.Reflection.ReflectionTypeLoadException e){return e.Types.Where(t=>t!=null).ToArray();}}).First(t=>t.IsEnum&&(t.Name==attribute.Args[0]||t.FullName==attribute.Args[0]));
                        var names=Enum.GetNames(type); var maskButton=new Button(); parent.Add(maskButton);
                        StyleSpecialControls(maskButton); maskButton.AddToClassList("thry-mask-picker");
                        Track(maskButton,()=>{string selected=string.Join(", ",names.Where((s,i)=>((int)property.MaterialProperty.GetNumber()&(1<<i))!=0));maskButton.text=property.MaterialProperty.hasMixedValue?"Mixed values":string.IsNullOrEmpty(selected)?"None":selected;maskButton.tooltip=maskButton.text;});
                        maskButton.clicked+=()=>_view.ShowMenu(maskButton.worldBound,names.Select(n=>new GUIContent(n)).ToArray(),Enumerable.Range(0,names.Length).Where(i=>((int)property.MaterialProperty.GetNumber()&(1<<i))!=0).ToArray(),i=>Model.Edit(property,p=>p.SetNumber((int)p.GetNumber()^(1<<i)),true),maskButton);
                        handled=true; return;
                    case "PoiPrefabSpawner":
                        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(attribute.Args[0]));
                        // An action describes itself on the button; a second caption
                        // repeats the same text and needlessly reserves a label column.
                        var actionCaption = parent.parent.Q<Label>(className: "thry-property-label");
                        if (actionCaption != null) actionCaption.style.display = DisplayStyle.None;
                        parent.parent.AddToClassList("thry-action-row");
                        var spawn=new Button(()=>{ var instance=PrefabUtility.InstantiatePrefab(prefab); Undo.RegisterCreatedObjectUndo(instance,"Spawn "+prefab.name); Selection.activeObject=instance; }) {name="action-"+property.MaterialProperty.name,text=property.Content.text};
                        spawn.style.marginLeft = 0; spawn.style.marginRight = 0;
                        spawn.SetEnabled(prefab!=null); parent.Add(spawn); handled=true; return;
                }
            }
        }
        private void MinMax(VisualElement parent,ShaderProperty property,int component,float min,float max)
        {
            var slider=new MinMaxSlider(min,max,min,max); parent.Add(slider);
            Track(slider,()=>{var v=property.MaterialProperty.vectorValue;slider.SetValueWithoutNotify(new Vector2(v[component],v[component+1]));slider.showMixedValue=property.MaterialProperty.hasMixedValue;});
            slider.RegisterValueChangedCallback(e=>Model.Edit(property,p=>{var v=p.vectorValue;v[component]=e.newValue.x;v[component+1]=e.newValue.y;p.vectorValue=v;},true));
            Vector(parent,property,new[]{"Min","Max"},component);
        }
    }

    internal sealed class RetainedRamp : VisualElement
    {
        private readonly Func<Vector4> _read; private readonly Action<Vector4> _write;
        private readonly bool _normalized, _unclampedZ, _unclampedW;
        private int _handle=-1;
        private Vector3 _dragBounds;
        internal RetainedRamp(Func<Vector4> read,Action<Vector4> write,string[] modes)
        {
            _read=read;_write=write;_unclampedZ=modes.Any(m=>m.Equals("unclampedZ",StringComparison.OrdinalIgnoreCase));_unclampedW=modes.Any(m=>m.Equals("unclampedW",StringComparison.OrdinalIgnoreCase));
            _normalized = modes.Any(m => m.Equals("normalized", StringComparison.OrdinalIgnoreCase));
            AddToClassList("thry-ramp"); style.height=54; generateVisualContent+=Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !enabledInHierarchy) return;
                var v = _read(); _dragBounds = Bounds(v);
                var a = Point(v.z, v.x, v); var b = Point(v.w, v.y, v);
                _handle = Vector2.Distance(e.localPosition, a) < Vector2.Distance(e.localPosition, b) ? 0 : 1;
                this.CapturePointer(e.pointerId); e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_handle < 0 || !this.HasPointerCapture(e.pointerId)) return;
                var v = _read();
                // Freeze the plot's scale for this gesture and stop at its edges.
                // Recomputing it from each new value causes runaway magnification.
                float time = Mathf.Clamp01((e.localPosition.x - 6) / Mathf.Max(1, contentRect.width - 12)) * _dragBounds.x;
                float value = Mathf.Lerp(_dragBounds.z, _dragBounds.y,
                    Mathf.Clamp01((e.localPosition.y - 6) / Mathf.Max(1, contentRect.height - 12)));
                if (_handle == 0)
                {
                    v.z = Mathf.Clamp(time, 0, Mathf.Max(0, Mathf.Min(v.w, _unclampedZ ? _dragBounds.x : 1)));
                    v.x = value;
                }
                else
                {
                    float maxTime = _unclampedW ? _dragBounds.x : 1;
                    v.w = Mathf.Clamp(time, Mathf.Clamp(v.z, 0, maxTime), maxTime);
                    v.y = value;
                }
                _write(v); MarkDirtyRepaint(); e.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(e => { _handle = -1; this.ReleasePointer(e.pointerId); MarkDirtyRepaint(); });
            RegisterCallback<PointerCaptureOutEvent>(e => { _handle = -1; MarkDirtyRepaint(); });
        }
        private Vector3 Bounds(Vector4 v) => new Vector3(
            _unclampedZ || _unclampedW ? Mathf.Max(1, v.z, v.w) : 1,
            _normalized ? Mathf.Min(0, v.x, v.y) : 0,
            _normalized ? Mathf.Max(1, v.x, v.y) : 1);
        private Vector2 Point(float t, float value, Vector4 v)
        {
            var bounds = _handle >= 0 ? _dragBounds : Bounds(v);
            return new Vector2(6 + Mathf.Clamp01(t / bounds.x) * (contentRect.width - 12),
                6 + Mathf.InverseLerp(bounds.z, bounds.y, value) * (contentRect.height - 12));
        }
        private void Draw(MeshGenerationContext context)
        {
            var v=_read(); var p=context.painter2D;p.lineWidth=2;p.strokeColor=new Color(.48f,.68f,.84f);p.BeginPath();p.MoveTo(Point(0,v.x,v));p.LineTo(Point(v.z,v.x,v));p.LineTo(Point(v.w,v.y,v));p.LineTo(Point(Mathf.Max(1,v.w),v.y,v));p.Stroke();
            p.fillColor=new Color(.8f,.8f,.8f);foreach(var point in new[]{Point(v.z,v.x,v),Point(v.w,v.y,v)}){p.BeginPath();p.Arc(point,4,0,360);p.Fill();}
        }
    }
}
#endif
