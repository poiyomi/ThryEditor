#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class DrawerAttribute
    {
        internal readonly string Name;
        internal readonly string[] Args;
        internal DrawerAttribute(string source)
        {
            int bracket = source.IndexOf('(');
            Name = bracket < 0 ? source : source.Substring(0, bracket);
            Args = bracket < 0 ? Array.Empty<string>() : source.Substring(bracket + 1).TrimEnd(')').Split(',').Select(s => s.Trim().Trim('"')).ToArray();
        }
        internal static float Number(string text)
        {
            if (text.StartsWith("n")) text = "-" + text.Substring(1);
            else if (text.StartsWith("p")) text = text.Substring(1);
            return float.Parse(text, CultureInfo.InvariantCulture);
        }
    }

    internal sealed partial class RetainedFields
    {
        internal readonly RetainedMaterialModel Model;
        private readonly MaterialInspectorView _view;
        private sealed class Binding
        {
            internal VisualElement Element;
            internal Action Update;
            internal bool Tracked = true;
        }
        private readonly List<Binding> _updates = new List<Binding>();
        private readonly Dictionary<Tuple<ShaderProperty, ShaderProperty>, ShaderProperty> _referenceProjections = new Dictionary<Tuple<ShaderProperty, ShaderProperty>, ShaderProperty>();
        private ILookup<string, ShaderProperty> _referenceSources;
        private IEnumerable<ShaderProperty> ScopedReferences(ShaderProperty owner, string name)
        {
            if (string.IsNullOrEmpty(name) || name == owner.MaterialProperty.name) yield break;
            if (_referenceSources == null) _referenceSources = Model.Shader.ShaderParts.OfType<ShaderProperty>().Where(p => p.MaterialProperty != null).ToLookup(p => p.MaterialProperty.name);
            foreach (var source in _referenceSources[name])
            {
                var targets = source.MaterialProperty.targets.OfType<Material>().Where(m => m != null
                    && Model.Shader.Materials.Contains(m) && owner.MaterialProperty.targets.Contains(m)).ToArray();
                if (targets.Length == 0) continue;
                var key = Tuple.Create(owner, source);
                if (!_referenceProjections.TryGetValue(key, out var reference))
                { reference = source.CreateRetainedProjection(owner, targets); _referenceProjections.Add(key, reference); }
                if (reference.RefreshRetainedProjection(Model.Renderers)) yield return reference;
            }
        }
        internal RetainedFields(RetainedMaterialModel model, MaterialInspectorView view) { Model = model; _view = view; }
        internal void Synchronize() => Synchronize(null);
        internal int LastSynchronizeCount { get; private set; }
        internal void Synchronize(Func<VisualElement, bool> include)
        {
            LastSynchronizeCount = 0;
            using (RetainedPropertyDefaults.BeginEvaluation(Model.Shader))
                foreach (var binding in _updates.ToArray())
                    if (include == null || include(binding.Element)) { binding.Update(); LastSynchronizeCount++; }
        }
        internal void Track(VisualElement element, Action update)
        {
            var binding = new Binding { Element = element, Update = update };
            _updates.Add(binding);
            element.RegisterCallback<DetachFromPanelEvent>(e => {
                if (e.target != element) return;
                _updates.Remove(binding); binding.Tracked = false;
            });
            element.RegisterCallback<AttachToPanelEvent>(e => {
                if (e.target != element) return;
                if (!binding.Tracked) { _updates.Add(binding); binding.Tracked = true; }
                update();
            });
            update();
        }
        internal static bool CanRenderProperty(ShaderProperty property)
        {
            if (property?.MaterialProperty == null || property.MyShader == null || property.ShaderPropertyIndex < 0
                || property.ShaderPropertyIndex >= property.MyShader.GetPropertyCount()) return false;
            var attributes = property.MyShader.GetPropertyAttributes(property.ShaderPropertyIndex).Select(a => new DrawerAttribute(a)).ToArray();
            return !IsLegacyStencilStatus(property, attributes);
        }
        internal static VisualElement Row(string label, out VisualElement value)
        {
            var row = new VisualElement(); row.AddToClassList("thry-property-row");
            var caption = new Label(label); caption.AddToClassList("thry-property-label"); row.Add(caption);
            value = new VisualElement(); value.AddToClassList("thry-property-value"); row.Add(value);
            row.RegisterCallback<GeometryChangedEvent>(e => {
                row.GetFirstAncestorOfType<MaterialInspectorView>()?.RequestRowBands();
                var body = row.GetFirstAncestorOfType<RetainedMaterialBody>(); if(body == null) return;
                float width = Mathf.Max(40,body.worldBound.x + Mathf.Min(240,body.contentRect.width * .38f) - row.worldBound.x);
                var currentLabel = row.Q(className: "thry-property-label");
                if(currentLabel != null && Mathf.Abs(currentLabel.resolvedStyle.width-width)>.5f) currentLabel.style.width=width;
            });
            return row;
        }
        internal VisualElement Field(ShaderProperty property, bool inline = false)
        {
            var attributes = property.MyShader.GetPropertyAttributes(property.ShaderPropertyIndex).Select(a => new DrawerAttribute(a)).ToArray();
            var root = new VisualElement { name = "property-" + property.MaterialProperty.name, userData = property, tooltip = property.TooltipText };
            root.AddToClassList("thry-property");
            if (!CanRenderProperty(property))
            {
                root.style.display = DisplayStyle.None;
                return root;
            }
            Track(root, () => property.RefreshRetainedProjection(Model.Renderers));
            DecorateMultiMaterialProperty(root, property);
            DecorateAnimation(root, property, inline);
            Decorators(root,property,attributes);
            Track(root, () => {
                root.style.display = property.RetainedVisible ? DisplayStyle.Flex : DisplayStyle.None;
                // Texture references have their own lock/animation state. Keep their
                // container interactive and gate only the texture's actual controls.
                root.SetEnabled(property is ShaderTextureProperty
                    ? property.Options.condition_enable == null || property.Options.condition_enable.Test()
                    : Model.CanEdit(property));
                root.EnableInClassList("thry-animated", property.IsAnimated);
                root.EnableInClassList("thry-recording", Model.Shader.IsInAnimationMode && property.IsAnimated);
            });
            if(attributes.Any(a=>a.Name=="PoiBakeColorAdjust"))return root;
            if (Presentation(root, property, attributes)) return root;
            var texture = property as ShaderTextureProperty;
            if (texture != null) { Texture(root, texture, attributes); return root; }
            VisualElement input;
            var row = Row(inline ? "" : RetainedText.PropertyCaption(property), out input);
            var label = row.Q<Label>(className: "thry-property-label");
            Track(label, () => { label.text = inline ? "" : RetainedText.PropertyCaption(property).Split('|')[0];
            label.tooltip = RetainedMaterialBody.Hover(RetainedMaterialBody.SectionCaption(property), property.TooltipText, property.Note); });
            if (!inline) ChangedPropertyIndicator(row, label, property);
            if (inline) row.AddToClassList("thry-inline");
            root.Add(row);
            Context(root, property);
            if (!inline && property.Options.reference_property != null)
            {
                foreach (var reference in ScopedReferences(property, property.Options.reference_property))
                { input.AddToClassList("thry-with-reference"); input.Add(Field(reference,true)); }
            }
            if (Special(input, property, attributes)) return root;
            var enumeration = attributes.FirstOrDefault(a => a.Name == "ThryWideEnum" || a.Name == "Enum" || a.Name == "KeywordEnum");
            if (enumeration != null) { Enumeration(input, property, enumeration); return root; }
            if (attributes.Any(a => a.Name.Contains("Toggle"))) { Toggle(input, property); return root; }
            if (property.MaterialProperty.GetPropertyType() == UnityEngine.Rendering.ShaderPropertyType.Int)
            {
                var integerField = new IntegerField(); Bind(integerField, property, p => p.intValue, (p, v) => p.intValue = v); input.Add(integerField); return root;
            }
            switch (property.MaterialProperty.type)
            {
                case MaterialProperty.PropType.Color:
                    var color = new ColorField { hdr = attributes.Any(a => a.Name == "HDR"), showAlpha = true, showEyeDropper = true };
                    Bind(color, property, p => p.colorValue, (p,v) => p.colorValue = v); input.Add(color); break;
                case MaterialProperty.PropType.Vector:
                    var vector = attributes.FirstOrDefault(a => a.Name == "VectorLabel" || a.Name == "Vector2" || a.Name == "Vector3" || a.Name == "Vector31");
                    string[] labels = vector?.Name == "VectorLabel" ? vector.Args.Where(a => !a.Equals("link", StringComparison.OrdinalIgnoreCase)).ToArray() :
                        new[] { "X", "Y", "Z", "W" }.Take(vector?.Name == "Vector2" ? 2 : vector?.Name == "Vector3" ? 3 : 4).ToArray();
                    Vector(input, property, labels, 0, false, vector?.Args.Any(a => a.Equals("link", StringComparison.OrdinalIgnoreCase)) == true); break;
                case MaterialProperty.PropType.Range:
                    var power = attributes.FirstOrDefault(a => a.Name == "PowerSlider");
                    if (power != null && power.Args.Length > 0 && DrawerAttribute.Number(power.Args[0]) > 0)
                    { PowerRange(input, property, DrawerAttribute.Number(power.Args[0])); break; }
                    var slider = new Slider(property.MaterialProperty.rangeLimits.x, property.MaterialProperty.rangeLimits.y) { showInputField = true };
                    bool integer = attributes.Any(a => a.Name == "IntRange" || a.Name == "ThryIntRange");
                    bool inverted = attributes.Any(a => a.Name == "InvertedSlider");
                    if (integer)
                    {
                        var integerSlider = new SliderInt((int)property.MaterialProperty.rangeLimits.x, (int)property.MaterialProperty.rangeLimits.y) { showInputField = true };
                        Bind(integerSlider, property, p => (int)(inverted ? -p.GetNumber() : p.GetNumber()), (p,v) => p.SetNumber(inverted ? -v : v));
                        input.Add(integerSlider); break;
                    }
                    Bind(slider, property, p => inverted ? -p.GetNumber() : p.GetNumber(), (p,v) => p.SetNumber((inverted ? -1 : 1) * (integer ? Mathf.Round(v) : v))); input.Add(slider); break;
                default:
                    var number = new FloatField(); Bind(number, property, p => p.GetNumber(), (p,v) => p.SetNumber(v)); input.Add(number); break;
            }
            return root;
        }
        internal static float PowerValue(float value, float power) => Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), power);
        private void PowerRange(VisualElement input, ShaderProperty property, float power)
        {
            var limits = property.MaterialProperty.rangeLimits;
            float low = PowerValue(limits.x, 1 / power), high = PowerValue(limits.y, 1 / power);
            var slider = new Slider(low, high) { name = "power-slider-" + property.MaterialProperty.name };
            var number = new FloatField(); input.style.flexDirection = FlexDirection.Row;
            slider.style.flexGrow = 1; number.style.width = 52; number.style.flexGrow = 0;
            input.Add(slider); input.Add(number);
            Track(slider, () => { slider.SetValueWithoutNotify(PowerValue(property.MaterialProperty.GetNumber(), 1 / power)); slider.showMixedValue = property.MaterialProperty.hasMixedValue; });
            slider.RegisterValueChangedCallback(e => Model.Number(property, Mathf.Clamp(PowerValue(e.newValue, power), limits.x, limits.y)));
            Bind(number, property, p => p.GetNumber(), (p,v) => p.SetNumber(Mathf.Clamp(v, limits.x, limits.y)));
        }
        internal void Bind<T>(BaseField<T> field, ShaderProperty property, Func<MaterialProperty,T> read, Action<MaterialProperty,T> write)
        {
            field.AddToClassList("thry-input"); field.name = "value-" + property.MaterialProperty.name;
            Track(field, () => { field.SetValueWithoutNotify(read(property.MaterialProperty)); field.showMixedValue = property.MaterialProperty.hasMixedValue; });
            field.RegisterValueChangedCallback(e => Model.Edit(property, p => write(p, e.newValue)));
            var numeric = field as IValueField<T>;
            if (numeric != null)
            {
                bool attached = false;
                field.RegisterCallback<AttachToPanelEvent>(e =>
                {
                    if (attached) return;
                    var row = field.GetFirstAncestorOfType<VisualElement>();
                    while (row != null && !row.ClassListContains("thry-property-row")) row = row.parent;
                    if (row == null || row.ClassListContains("thry-inline")) return;
                    var caption = row.Q<Label>(className: "thry-property-label");
                    if (caption == null) return;
                    attached = true;
                    caption.focusable = true;
                    caption.AddToClassList(BaseField<T>.labelDraggerVariantUssClassName);
                    caption.RegisterCallback<PointerDownEvent>(pointer =>
                    {
                        if (pointer.button != 0) return;
                        if (!field.enabledInHierarchy || !Model.CanEdit(property))
                        { pointer.StopImmediatePropagation(); return; }
                        caption.Focus();
                    });
                    // Native dragging supplies sensitivity, Alt/Shift modifiers, range
                    // clamping, capture and Escape cancellation through the bound field.
                    var dragger = new FieldMouseDragger<T>(new MaterialNumericDrag<T>(numeric, property, read, write, Model));
                    dragger.SetDragZone(caption);
                });
            }
        }
        private sealed class MaterialNumericDrag<T> : IValueField<T>
        {
            private readonly IValueField<T> _field;
            private readonly ShaderProperty _property;
            private readonly Func<MaterialProperty,T> _read;
            private readonly Action<MaterialProperty,T> _write;
            private readonly RetainedMaterialModel _model;
            private readonly Dictionary<Material,(Shader Shader,T Value)> _originals = new Dictionary<Material,(Shader,T)>();
            private bool _dragging;

            internal MaterialNumericDrag(IValueField<T> field,ShaderProperty property,Func<MaterialProperty,T> read,Action<MaterialProperty,T> write,RetainedMaterialModel model)
            { _field=field; _property=property; _read=read; _write=write; _model=model; }

            public T value
            {
                get => _field.value;
                set
                {
                    // FieldMouseDragger assigns its single startValue only on Escape.
                    // That scalar cannot restore a mixed selection; use each owner's snapshot.
                    if (!_dragging) { _field.value=value; return; }
                    _model.Edit(_property,p=>
                    {
                        var material=p.targets.OfType<Material>().FirstOrDefault();
                        if(material!=null && _originals.TryGetValue(material,out var original) && material.shader==original.Shader)
                            _write(p,original.Value);
                    },true);
                }
            }
            public void StartDragging()
            {
                _originals.Clear();
                foreach(var material in _property.MaterialProperty.targets.OfType<Material>().Where(m=>m!=null && _model.Shader.Materials.Contains(m)))
                {
                    var property=MaterialEditor.GetMaterialProperty(new UnityEngine.Object[]{material},_property.MaterialProperty.name);
                    _originals[material]=(material.shader,_read(property));
                }
                _dragging=true; _field.StartDragging();
            }
            public void ApplyInputDeviceDelta(Vector3 delta,DeltaSpeed speed,T startValue) => _field.ApplyInputDeviceDelta(delta,speed,startValue);
            public void StopDragging() { _field.StopDragging(); _dragging=false; _originals.Clear(); }
        }
        private void ChangedPropertyIndicator(VisualElement row, Label caption, ShaderProperty property)
        {
            // Keep indicators beside TextElements; children suppress their text rendering.
            caption.AddToClassList("thry-caption-has-changed-indicator");
            var dot = new VisualElement { name = "changed-property-" + property.MaterialProperty.name };
            dot.AddToClassList("thry-property-changed-dot"); row.Add(dot);
            dot.RegisterCallback<TooltipEvent>(e => { e.tooltip = RetainedValueDetails.Describe(property); e.rect = dot.worldBound; e.StopImmediatePropagation(); });
            Action position = () =>
            {
                if (caption.panel == null || caption.contentRect.width <= 0) return;
                Rect bounds = caption.ChangeCoordinatesTo(row, caption.contentRect);
                float measured = caption.MeasureTextSize(caption.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                if (float.IsNaN(measured) || float.IsNaN(bounds.x)) return;
                dot.style.left = bounds.x + Mathf.Min(measured, bounds.width) + 4;
                dot.style.top = bounds.center.y - 2.5f;
            };
            caption.RegisterCallback<GeometryChangedEvent>(e => position());
            row.RegisterCallback<GeometryChangedEvent>(e => position());
            Track(dot, () =>
            {
                dot.style.display = RetainedMaterialBody.HasChangedValue(property) || RetainedMaterialBody.HasChangedTextureTransform(property)
                    ? DisplayStyle.Flex : DisplayStyle.None;
                dot.style.backgroundColor = caption.resolvedStyle.color;
                position();
            });
        }
        internal void Toggle(VisualElement parent, ShaderProperty property)
        {
            var toggle = new Toggle(); Bind(toggle, property, p => Mathf.Abs(p.GetNumber()) > .001f, (p,v) => p.SetNumber(v ? 1 : 0)); Track(toggle,()=>toggle.SetEnabled(Model.CanEdit(property))); parent.Add(toggle);
        }
        internal void Vector(VisualElement parent, ShaderProperty property, string[] labels, int start = 0, bool texture = false, bool link = false)
        {
            var vector = new VisualElement(); vector.AddToClassList("thry-components"); parent.Add(vector);
            bool linked = false;
            var linkBaselines = new Dictionary<Material, Vector4>();
            Toggle linkToggle = null;
            if (link)
            {
                StyleSpecialControls(vector);
                string key = "vector-link:" + property.MaterialProperty.name + ":" + start + ":" + labels.Length + ":" + texture;
                linked = RetainedUiState.Get(property.MyShader.name, key);
                linkToggle = new Toggle { name = "vector-link-" + property.MaterialProperty.name, value = linked, focusable = true, tabIndex = 0 };
                linkToggle.AddToClassList("thry-vector-link");
                var icon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("thry-vector-link-icon"); linkToggle.Add(icon);
                Action synchronizeLink = () =>
                {
                    linkToggle.EnableInClassList("thry-vector-linked", linked);
                    vector.EnableInClassList("thry-vector-components-linked", linked);
                    icon.image = Resources.Load<Texture2D>(linked ? "thryEditor_vectorLinkRatio" : "thryEditor_vectorLinkOff");
                    linkToggle.tooltip = linked ? "Linked: changing any component preserves each material's proportions. Zero values without a ratio use the same change on each axis. Click to edit independently."
                        : "Independent: edit each component separately. Click to link proportions.";
                };
                linkToggle.RegisterValueChangedCallback(e =>
                {
                    linked = e.newValue; linkBaselines.Clear();
                    RetainedUiState.Set(property.MyShader.name, key, linked); synchronizeLink();
                });
                synchronizeLink();
                Track(linkToggle, () => { linkToggle.SetEnabled(Model.CanEdit(property)); icon.tintColor = EditorGUIUtility.isProSkin ? Color.white : new Color(.35f, .35f, .35f); });
            }
            for (int i = 0; i < labels.Length; i++)
            {
                int index = start + i; var field = new FloatField(labels[i]); field.AddToClassList("thry-component"); field.name = "component-" + index;
                if (labels[i] == "X" || labels[i] == "Y" || labels[i] == "Z" || labels[i] == "W") field.AddToClassList("thry-component-axis");
                if (i > 0) field.AddToClassList("thry-component-spaced");
                Track(field, () => {
                    var p = property.MaterialProperty; var v = texture ? p.textureScaleAndOffset : p.vectorValue;
                    field.SetValueWithoutNotify(v[index]);
                    field.showMixedValue = p.targets.OfType<Material>().Where(m => m.HasProperty(p.name)).Select(m => texture ? new Vector4(m.GetTextureScale(p.name).x, m.GetTextureScale(p.name).y, m.GetTextureOffset(p.name).x, m.GetTextureOffset(p.name).y)[index] : m.GetVector(p.name)[index]).Distinct().Skip(1).Any();
                });
                var gesture = new RetainedPropertyGesture(Model, property);
                var originalBaselines = new Dictionary<Material, Vector4>();
                gesture.Attach(field, () => linked ? ((1 << labels.Length) - 1) << start : 1 << index, texture,
                    () => { linkBaselines.Clear(); foreach (var entry in originalBaselines) linkBaselines[entry.Key] = entry.Value; },
                    () => { originalBaselines.Clear(); foreach (var entry in linkBaselines) originalBaselines[entry.Key] = entry.Value; });
                field.RegisterValueChangedCallback(e => {
                    if (gesture.Cancelling) return;
                    if (!linked) Model.VectorComponent(property, index, e.newValue, texture);
                    else Model.Edit(property, p =>
                    {
                        var value = texture ? p.textureScaleAndOffset : p.vectorValue;
                        var owner = p.targets.OfType<Material>().FirstOrDefault();
                        var baseline = value;
                        bool allZero = true;
                        for (int j = start; j < start + labels.Length; j++) allZero &= Mathf.Abs(value[j]) <= .000001f;
                        // Keep the last nonzero shape when a uniform scale passes through zero.
                        // A partially zero or externally edited vector uses its current shape.
                        if (allZero && owner != null && linkBaselines.TryGetValue(owner, out var previous)) baseline = previous;
                        double denominator = baseline[index];
                        bool ratio = Math.Abs(denominator) > .000001d;
                        var result = value;
                        for (int j = start; j < start + labels.Length; j++)
                        {
                            double component = ratio ? baseline[j] * (double)e.newValue / denominator : value[j] + (double)e.newValue - value[index];
                            if (double.IsNaN(component) || double.IsInfinity(component) || Math.Abs(component) > float.MaxValue) return;
                            result[j] = (float)component;
                        }
                        result[index] = e.newValue;
                        if (owner != null)
                        {
                            bool nonzero = false;
                            for (int j = start; j < start + labels.Length; j++) nonzero |= Mathf.Abs(result[j]) > .000001f;
                            linkBaselines[owner] = nonzero ? result : baseline;
                        }
                        if (texture) p.textureScaleAndOffset = result; else p.vectorValue = result;
                    }, true);
                }); vector.Add(field);
            }
            if (linkToggle != null) vector.Add(linkToggle);
        }
        internal void Enumeration(VisualElement parent, ShaderProperty property, DrawerAttribute attribute)
        {
            string[] names; float[] values;
            // ThryWideEnum also accepts (enumType, unusedInteger). Do not confuse
            // it with the equally valid one-choice (label, value) overload.
            Type enumType = null;
            if (attribute.Args.Length == 1 || (attribute.Name == "ThryWideEnum" && attribute.Args.Length == 2))
                enumType = ResolveEnumType(attribute.Args[0]);
            if (attribute.Name == "KeywordEnum") { names = attribute.Args; values = Enumerable.Range(0,names.Length).Select(i=>(float)i).ToArray(); }
            else if (enumType != null || attribute.Args.Length == 1)
            {
                if (enumType == null) throw new InvalidOperationException("Unknown shader enum " + attribute.Args[0]);
                names = Enum.GetNames(enumType); values = Enum.GetValues(enumType).Cast<object>().Select(Convert.ToSingle).ToArray();
            }
            else { names = attribute.Args.Where((s,i)=>i%2==0).ToArray(); values = attribute.Args.Where((s,i)=>i%2==1).Select(DrawerAttribute.Number).ToArray(); }
            names = names.Select(n => Model.Shader.Locale.Get(n,n)).ToArray();
            var field = new DropdownField(names.ToList(), 0); field.AddToClassList("thry-input"); field.name = "value-" + property.MaterialProperty.name;
            _view.UseInspectorMenu(field);
            Track(field, () => { int selected = Array.IndexOf(values, property.MaterialProperty.GetNumber()); field.SetValueWithoutNotify(selected < 0 ? "—" : names[selected]); field.showMixedValue = property.MaterialProperty.hasMixedValue; });
            field.RegisterValueChangedCallback(e => { int index = Array.IndexOf(names, e.newValue); if(index >= 0) Model.Number(property, values[index]); }); parent.Add(field);
        }
        private static readonly Dictionary<string,Type> EnumTypes = new Dictionary<string,Type>();
        private static Type ResolveEnumType(string name)
        {
            Type type;
            if(!EnumTypes.TryGetValue(name,out type))
            {
                type=AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=>{try{return a.GetTypes();}catch(System.Reflection.ReflectionTypeLoadException e){return e.Types.Where(t=>t!=null).ToArray();}}).FirstOrDefault(t=>t.IsEnum&&(t.Name==name||t.FullName==name));
                EnumTypes[name]=type;
            }
            return type;
        }
        private void Texture(VisualElement root, ShaderTextureProperty property, DrawerAttribute[] attributes)
        {
            root.AddToClassList("thry-texture-property");
            VisualElement value; var row = Row("", out value); root.Add(row);
            row.AddToClassList("thry-texture-heading-row");
            value.AddToClassList("thry-texture-value");
            var caption = row.Q<Label>(); caption.RemoveFromHierarchy();
            // The label is the only hoverable part of a texture row, so it also has to carry the
            // tooltip that a plain property row shows on its own label.
            var foldout = new Button();
            foldout.AddToClassList("thry-property-label"); foldout.AddToClassList("thry-texture-label"); row.Insert(0,foldout);
            var foldIcon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            foldIcon.AddToClassList("thry-header-icon"); foldIcon.AddToClassList("thry-texture-caret"); foldout.Add(foldIcon);
            var foldCaption = new Label(RetainedText.PropertyCaption(property)) { pickingMode = PickingMode.Ignore };
            foldCaption.AddToClassList("thry-texture-caption"); foldout.Add(foldCaption);
            Track(foldCaption, () => { foldCaption.text = RetainedText.PropertyCaption(property);
            foldout.tooltip = RetainedMaterialBody.Hover(foldCaption.text, property.TooltipText, property.Note, "Expand or collapse texture settings"); });
            ChangedPropertyIndicator(row, foldCaption, property);
            var dimension = property.MaterialProperty.textureDimension;
            var objectField = new ObjectField { name = "value-" + property.MaterialProperty.name, objectType = typeof(Texture), allowSceneObjects = false };
            objectField.AddToClassList("thry-input");
            Action<MaterialProperty, Texture2DArray, float> updateArrayReferences = null;
            Track(objectField, () => { objectField.SetValueWithoutNotify(property.MaterialProperty.textureValue); objectField.showMixedValue = property.MaterialProperty.hasMixedValue; });
            objectField.RegisterValueChangedCallback(e => {
                var texture = e.newValue as Texture;
                if (texture != null && dimension != UnityEngine.Rendering.TextureDimension.Any && texture.dimension != dimension)
                { objectField.SetValueWithoutNotify(property.MaterialProperty.textureValue); e.StopImmediatePropagation(); return; }
                Model.Edit(property, p => { p.textureValue = texture; Drawers.ThryRGBAPackerDrawer.ClearPendingPreview(p, texture); updateArrayReferences?.Invoke(p, texture as Texture2DArray, 0); });
            }); value.Add(objectField);
            Track(objectField, () => objectField.SetEnabled(Model.CanEdit(property)));
            TextureAssetDisplay(objectField, property);
            var array = attributes.FirstOrDefault(a => a.Name == "TextureArray");
            if (array != null)
            {
                if (!Model.Shader.TextureArrayProperties.Contains(property)) Model.Shader.TextureArrayProperties.Add(property);
                objectField.tooltip = "Assign a texture array, or drop image frames or a GIF to create one.";
                updateArrayReferences = (owner, texture, fps) =>
                {
                    if (texture == null) return;
                    string framesId = array.Args.Length > 0 ? array.Args[0] : property.Options.reference_property;
                    string fpsId = array.Args.Length > 1 ? array.Args[1] : property.Options.fps_property;
                    Action<string, float> assign = (id, number) =>
                    {
                        ShaderProperty target;
                        if (id == null || !Model.Shader.PropertyDictionary.TryGetValue(id, out target) || !Model.CanEdit(target)) return;
                        var targets = owner.targets.OfType<Material>().Where(m => Model.Shader.Materials.Contains(m)
                            && target.MaterialProperty.targets.Contains(m) && m.HasProperty(id)).Cast<UnityEngine.Object>().ToArray();
                        if (targets.Length > 0) MaterialEditor.GetMaterialProperty(targets, id).SetNumber(number);
                    };
                    assign(framesId, texture.depth);
                    if (fps > 0) assign(fpsId, fps);
                };
                objectField.RegisterCallback<DragUpdatedEvent>(e =>
                {
                    if (DragAndDrop.paths.Length == 0 || DragAndDrop.objectReferences.OfType<Texture2DArray>().Any()) return;
                    DragAndDrop.visualMode = Model.CanEdit(property) && RetainedTextureCard.CanConvertFrames(DragAndDrop.paths)
                        ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected; e.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                objectField.RegisterCallback<DragPerformEvent>(e =>
                {
                    if (DragAndDrop.paths.Length == 0 || DragAndDrop.objectReferences.OfType<Texture2DArray>().Any()) return;
                    if (!Model.CanEdit(property) || !RetainedTextureCard.CanConvertFrames(DragAndDrop.paths)) { e.StopImmediatePropagation(); return; }
                    DragAndDrop.AcceptDrag();
                    float fps;
                    var texture = Thry.ThryEditor.Helpers.Converter.PathsToTexture2DArray(DragAndDrop.paths, out fps);
                    if (texture != null) Model.Edit(property, p => { p.textureValue = texture; updateArrayReferences(p, texture, fps); });
                    e.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
            }
            var details = new VisualElement(); details.AddToClassList("thry-texture-details"); root.Add(details);
            string foldoutKey = "texture-details-" + property.MaterialProperty.name;
            if (!property.showFoldoutProperties) property.showFoldoutProperties = RetainedUiState.Get(property.MyShader.name, foldoutKey);
            bool built = false;
            Action expand = () => {
                root.EnableInClassList("thry-texture-expanded", property.showFoldoutProperties);
                details.style.display = property.showFoldoutProperties ? DisplayStyle.Flex : DisplayStyle.None;
                foldIcon.image = Resources.Load<Texture2D>("ThryToolbar/header-caret-" + (property.showFoldoutProperties ? "down" : "right"));
                if (!property.showFoldoutProperties || built) return; built = true;
                var card = new RetainedTextureCard(Model, property, objectField, array != null);
                var gradient = attributes.FirstOrDefault(a => a.Name == "Gradient");
                if (gradient != null) card.SetGradientAction(() => OpenGradientCreator(property, gradient, card));
                details.Add(card); Track(card, card.Synchronize);
                if (property.hasScaleOffset)
                {
                    VisualElement tiling, offset; details.Add(Row("Tiling", out tiling)); Vector(tiling, property, new[] { "X", "Y" }, 0, true);
                    details.Add(Row("Offset", out offset)); Vector(offset, property, new[] { "X", "Y" }, 2, true);
                    Track(tiling, () => tiling.SetEnabled(Model.CanEdit(property)));
                    Track(offset, () => offset.SetEnabled(Model.CanEdit(property)));
                }
                if (property.Options.reference_properties != null)
                    foreach (var name in property.Options.reference_properties)
                        foreach (var reference in ScopedReferences(property, name)) details.Add(Field(reference));
                var textureTools = new VisualElement(); details.Add(textureTools);
                TextureTools(textureTools, card, property, attributes);
                Track(textureTools, () => textureTools.SetEnabled(Model.CanEdit(property)));
            };
            foldout.clicked += () => {
                property.showFoldoutProperties = !property.showFoldoutProperties;
                RetainedUiState.Set(property.MyShader.name, foldoutKey, property.showFoldoutProperties); expand();
            };
            Track(root, expand);
            if (property.Options.reference_property != null)
            {
                foreach (var reference in ScopedReferences(property, property.Options.reference_property))
                {
                    value.AddToClassList("thry-texture-has-reference");
                    var inlineReference = Field(reference, true);
                    inlineReference.AddToClassList("thry-texture-reference"); value.Add(inlineReference);
                    // Preserve the adjacent strength/control even in narrow inspectors;
                    // secondary asset metadata gives up its space before the control does.
                    value.RegisterCallback<GeometryChangedEvent>(e => value.EnableInClassList("thry-texture-value-compact", e.newRect.width < 320));
                }
            }
            Context(root, property);
        }
        private void Context(VisualElement element, ShaderProperty property)
        {
            element.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                var target = e.target as VisualElement;
                // Inline references and expanded texture settings are nested properties.
                // Let the closest property's own handler receive the gesture.
                for (var owner = target; owner != null; owner = owner.parent)
                {
                    if (!owner.ClassListContains("thry-property")) continue;
                    if (owner != element) return;
                    break;
                }
                // UV tile buttons have their own rename menu; their row label still
                // receives the normal material-property menu.
                if (Drawers.TileLabelUtility.IsUdimProperty(property.MaterialProperty.name))
                    for (var child = target; child != null && child != element; child = child.parent)
                        if (child is Button && child.parent != null && child.parent.ClassListContains("thry-components")) return;
                // Handle the gesture before native text/color/object fields focus or
                // consume it. Their default focus action otherwise closes the new menu.
                e.PreventDefault(); e.StopImmediatePropagation();
                Model.Shader.ActivateRetained();
                var menu = property.RetainedContextMenu();
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(RetainedText.Get(Model.Shader, "favorite", "Favorite")), _view.IsFavorite(property), () => _view.ToggleFavorite(property));
                _view.ShowLegacyMenu(menu, element);
            }, TrickleDown.TrickleDown);
        }
        private void TextureAssetDisplay(ObjectField field, ShaderTextureProperty property)
        {
            TextureAssetDisplay(field, () => property.MaterialProperty.textureValue, () => property.MaterialProperty.hasMixedValue,
                () => Model.CanEdit(property), () => Model.Edit(property, p => { p.textureValue = null; Drawers.ThryRGBAPackerDrawer.ClearPendingPreview(p, null); }));
        }

        internal void TextureAssetDisplay(ObjectField field, Func<Texture> getTexture, Func<bool> isMixed, Func<bool> canEdit, Action clearTexture)
        {
            Action clearValue = () => { if (field.enabledInHierarchy && canEdit()) clearTexture(); };
            field.AddToClassList("thry-texture-asset");
            var display = field.Q(className: "unity-object-field-display");
            var selector = field.Q(className: "unity-object-field__selector");
            display.RegisterCallback<MouseDownEvent>(e => {
                if (e.target is VisualElement target && (target == selector || selector.Contains(target))) return;
                if (e.button != 0 || getTexture() != null || isMixed()
                    || !field.enabledInHierarchy || !canEdit()) return;
                // Use the field's native selector so picking preserves its type filter,
                // change callback, material Undo, and animation recording behavior.
                e.PreventDefault(); e.StopImmediatePropagation();
                display.Focus();
                // Native keyboard activation is independent of mouse capture from
                // the click which opened this field.
                using (var submit = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }))
                { submit.target = display; display.SendEvent(submit); }
            }, TrickleDown.TrickleDown);
            var nativeIcon = display.Q<Image>();
            var assetName = display.Q<Label>();
            var size = new Label { name = "texture-asset-size" }; size.AddToClassList("thry-asset-size"); display.Add(size);
            var clear = new Button(clearValue) { name = "clear-texture-asset", tooltip = "Clear texture" };
            clear.AddToClassList("thry-asset-clear"); display.Add(clear);
            var clearIcon = new Image { image = Resources.Load<Texture2D>("ThryToolbar/texture-clear"), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            clearIcon.AddToClassList("thry-texture-clear-icon"); clear.Add(clearIcon);
            clear.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            clear.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            clear.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefault(); e.StopImmediatePropagation(); clearValue(); }, TrickleDown.TrickleDown);
            Texture measuredTexture = null; int measuredVersion = -1, measuredProjectVersion = -1;
            nativeIcon.style.display = DisplayStyle.None;
            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("thry-asset-tile"); display.Insert(0, tile);
            var preview = new Image { name = "texture-asset-thumbnail", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            tile.Add(preview);
            // Keep Unity's native picker beside the thumbnail, with metadata and
            // the separate clear action at the trailing edge of the field.
            display.Insert(1, selector);
            Track(field, () => {
                var texture = getTexture();
                bool mixed = isMixed();
                bool assigned = texture != null && !mixed;
                tile.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
                clear.style.display = texture != null || mixed ? DisplayStyle.Flex : DisplayStyle.None;
                clear.SetEnabled(canEdit());
                if (assigned) size.style.display = StyleKeyword.Null;
                else size.style.display = DisplayStyle.None;
                if (assigned && (measuredTexture != texture || measuredVersion != EditorUtility.GetDirtyCount(texture) || measuredProjectVersion != RetainedTextureRevision.Version))
                {
                    measuredTexture = texture; measuredVersion = EditorUtility.GetDirtyCount(texture);
                    measuredProjectVersion = RetainedTextureRevision.Version;
                    var path = AssetDatabase.GetAssetPath(texture);
                    long bytes = RetainedTexturePreview.EstimateMemory(texture);
                    size.tooltip = "Estimated texture memory";
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    {
                        bytes = new System.IO.FileInfo(path).Length;
                        size.tooltip = "Source file size";
                    }
                    size.text = Helpers.TextureHelper.VRAM.ToByteString(bytes);
                }
                // Render the texture itself instead of Unity's tiny, padded asset icon.
                preview.image = !assigned ? null : texture.dimension == UnityEngine.Rendering.TextureDimension.Tex2D
                    ? texture : AssetPreview.GetAssetPreview(texture) ?? AssetPreview.GetMiniThumbnail(texture);
                preview.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
                assetName.text = mixed ? RetainedText.Get(Model.Shader,"multiple_textures","Multiple textures") : assigned
                    ? RetainedText.TextureCaption(texture) : RetainedText.Get(Model.Shader,"choose_texture","Choose texture…");
                assetName.tooltip = assigned ? AssetDatabase.GetAssetPath(texture) : "Click to choose a texture, or drag one here";
                field.EnableInClassList("thry-asset-unassigned", !assigned);
            });
        }
        partial void TextureTools(VisualElement parent, RetainedTextureCard card, ShaderTextureProperty property, DrawerAttribute[] attributes);
        private bool Special(VisualElement parent, ShaderProperty property, DrawerAttribute[] attributes) { bool handled = false; SpecialFields(parent,property,attributes,ref handled); return handled; }
        partial void SpecialFields(VisualElement parent, ShaderProperty property, DrawerAttribute[] attributes, ref bool handled);
    }
}
#endif
