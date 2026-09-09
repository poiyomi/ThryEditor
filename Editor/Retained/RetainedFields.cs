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
        private readonly List<Action> _updates = new List<Action>();
        internal RetainedFields(RetainedMaterialModel model, MaterialInspectorView view) { Model = model; _view = view; }
        internal void Synchronize() { foreach (var update in _updates.ToArray()) update(); }
        internal void Track(VisualElement element, Action update)
        {
            _updates.Add(update);
            element.RegisterCallback<DetachFromPanelEvent>(e => _updates.Remove(update));
            update();
        }
        internal static VisualElement Row(string label, out VisualElement value)
        {
            var row = new VisualElement(); row.AddToClassList("thry-property-row");
            var caption = new Label(label); caption.AddToClassList("thry-property-label"); row.Add(caption);
            value = new VisualElement(); value.AddToClassList("thry-property-value"); row.Add(value);
            row.RegisterCallback<GeometryChangedEvent>(e => {
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
            if (IsLegacyStencilStatus(property, attributes))
            {
                root.style.display = DisplayStyle.None;
                return root;
            }
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
            foreach (var attribute in attributes)
            {
                if (attribute.Name == "Header" || attribute.Name == "ThryHeaderLabel")
                { var heading = new Label(string.Join(", ", attribute.Args)); heading.AddToClassList("thry-field-heading"); root.Add(heading); }
                if (attribute.Name == "Helpbox") root.Add(new HelpBox(property.Content.text, attribute.Args.Length == 0 ? HelpBoxMessageType.Info : (HelpBoxMessageType)Mathf.Clamp((int)DrawerAttribute.Number(attribute.Args[0]), 0, 3)));
                if (attribute.Name == "ThrySeperator") root.Add(new VisualElement { name = "thry-separator" });
                if (attribute.Name == "LocalMessage" || attribute.Name == "RemoteMessage")
                {
                    Drawers.LocalMessageDrawer message = attribute.Name == "RemoteMessage" ? new Drawers.RemoteMessageDrawer() : new Drawers.LocalMessageDrawer();
                    root.Add(message.CreateRetained(property.MaterialProperty.displayName, Model.Shader.Materials));
                    return root;
                }
            }
            if (attributes.Any(a => a.Name == "Helpbox" || a.Name == "ThryRichLabel" || a.Name == "ThryDescription"))
            {
                if (!attributes.Any(a => a.Name == "Helpbox"))
                {
                    var description = new Label(property.Content.text);
                    description.AddToClassList("thry-description"); root.Add(description);
                }
                return root;
            }
            if(attributes.Any(a=>a.Name=="PoiBakeColorAdjust"))return root;
            var texture = property as ShaderTextureProperty;
            if (texture != null) { Texture(root, texture, attributes); return root; }
            VisualElement input;
            var row = Row(inline ? "" : property.Content.text, out input);
            var label = row.Q<Label>(className: "thry-property-label");
            Track(label, () => { label.text = inline ? "" : RetainedMaterialBody.SectionCaption(property).Split('|')[0];
            label.tooltip = RetainedMaterialBody.Hover(RetainedMaterialBody.SectionCaption(property), property.TooltipText, property.Note); });
            if (!inline) ChangedPropertyIndicator(row, label, property);
            if (inline) row.AddToClassList("thry-inline");
            root.Add(row);
            Context(root, property);
            if (!inline && property.Options.reference_property != null)
            {
                ShaderProperty reference;
                if(Model.Shader.PropertyDictionary.TryGetValue(property.Options.reference_property,out reference) && reference != property)
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
                    var slider = new Slider(property.MaterialProperty.rangeLimits.x, property.MaterialProperty.rangeLimits.y) { showInputField = true };
                    bool integer = attributes.Any(a => a.Name == "IntRange" || a.Name == "ThryIntRange");
                    bool inverted = attributes.Any(a => a.Name == "InvertedSlider");
                    Bind(slider, property, p => inverted ? -p.GetNumber() : p.GetNumber(), (p,v) => p.SetNumber((inverted ? -1 : 1) * (integer ? Mathf.Round(v) : v))); input.Add(slider); break;
                default:
                    var number = new FloatField(); Bind(number, property, p => p.GetNumber(), (p,v) => p.SetNumber(v)); input.Add(number); break;
            }
            return root;
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
                    var dragger = new FieldMouseDragger<T>(numeric);
                    dragger.SetDragZone(caption);
                });
            }
        }
        private void ChangedPropertyIndicator(VisualElement row, Label caption, ShaderProperty property)
        {
            // Keep indicators beside TextElements; children suppress their text rendering.
            caption.AddToClassList("thry-caption-has-changed-indicator");
            var dot = new VisualElement { name = "changed-property-" + property.MaterialProperty.name, pickingMode = PickingMode.Ignore };
            dot.AddToClassList("thry-property-changed-dot"); row.Add(dot);
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
            var toggle = new Toggle(); Bind(toggle, property, p => p.GetNumber() != 0, (p,v) => p.SetNumber(v ? 1 : 0)); Track(toggle,()=>toggle.SetEnabled(Model.CanEdit(property))); parent.Add(toggle);
        }
        internal void Vector(VisualElement parent, ShaderProperty property, string[] labels, int start = 0, bool texture = false, bool link = false)
        {
            var vector = new VisualElement(); vector.AddToClassList("thry-components"); parent.Add(vector);
            int mode = 0;
            if (link) { Button button = null; button = new Button(() => { mode = (mode + 1) % 3; button.text = new[] { "○", "×", "+" }[mode]; button.tooltip = new[] { "Independent components", "Link by ratio", "Link by offset" }[mode]; }) { text = "○", tooltip = "Link components" }; vector.Add(button); }
            for (int i = 0; i < labels.Length; i++)
            {
                int index = start + i; var field = new FloatField(labels[i]); field.AddToClassList("thry-component"); field.name = "component-" + index;
                if (i > 0) field.AddToClassList("thry-component-spaced");
                Track(field, () => {
                    var p = property.MaterialProperty; var v = texture ? p.textureScaleAndOffset : p.vectorValue;
                    field.SetValueWithoutNotify(v[index]);
                    field.showMixedValue = p.targets.OfType<Material>().Where(m => m.HasProperty(p.name)).Select(m => texture ? new Vector4(m.GetTextureScale(p.name).x, m.GetTextureScale(p.name).y, m.GetTextureOffset(p.name).x, m.GetTextureOffset(p.name).y)[index] : m.GetVector(p.name)[index]).Distinct().Skip(1).Any();
                });
                field.RegisterValueChangedCallback(e => {
                    if (mode == 0) Model.VectorComponent(property, index, e.newValue, texture);
                    else Model.Edit(property, p => { var v = p.vectorValue; float old = v[index]; for (int j = 0; j < labels.Length; j++) v[j] = mode == 1 && Mathf.Abs(old) > .00001f ? v[j] * e.newValue / old : v[j] + e.newValue - old; v[index] = e.newValue; p.vectorValue = v; }, true);
                }); vector.Add(field);
            }
        }
        internal void Enumeration(VisualElement parent, ShaderProperty property, DrawerAttribute attribute)
        {
            string[] names; float[] values;
            if (attribute.Name == "KeywordEnum") { names = attribute.Args; values = Enumerable.Range(0,names.Length).Select(i=>(float)i).ToArray(); }
            else if (attribute.Args.Length == 1)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); } }).FirstOrDefault(t => t.IsEnum && (t.Name == attribute.Args[0] || t.FullName == attribute.Args[0]));
                if (type == null) throw new InvalidOperationException("Unknown shader enum " + attribute.Args[0]);
                names = Enum.GetNames(type); values = Enum.GetValues(type).Cast<object>().Select(Convert.ToSingle).ToArray();
            }
            else { names = attribute.Args.Where((s,i)=>i%2==0).ToArray(); values = attribute.Args.Where((s,i)=>i%2==1).Select(DrawerAttribute.Number).ToArray(); }
            names = names.Select(n => Model.Shader.Locale.Get(n,n)).ToArray();
            var field = new DropdownField(names.ToList(), 0); field.AddToClassList("thry-input"); field.name = "value-" + property.MaterialProperty.name;
            _view.UseInspectorMenu(field);
            Track(field, () => { int selected = Array.IndexOf(values, property.MaterialProperty.GetNumber()); field.SetValueWithoutNotify(selected < 0 ? "—" : names[selected]); field.showMixedValue = property.MaterialProperty.hasMixedValue; });
            field.RegisterValueChangedCallback(e => { int index = Array.IndexOf(names, e.newValue); if(index >= 0) Model.Number(property, values[index]); }); parent.Add(field);
        }
        private void Texture(VisualElement root, ShaderTextureProperty property, DrawerAttribute[] attributes)
        {
            VisualElement value; var row = Row("", out value); root.Add(row);
            value.AddToClassList("thry-texture-value");
            var caption = row.Q<Label>(); caption.RemoveFromHierarchy();
            // The label is the only hoverable part of a texture row, so it also has to carry the
            // tooltip that a plain property row shows on its own label.
            var foldout = new Button();
            foldout.AddToClassList("thry-property-label"); foldout.AddToClassList("thry-texture-label"); row.Insert(0,foldout);
            var foldIcon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            foldIcon.AddToClassList("thry-header-icon"); foldIcon.AddToClassList("thry-texture-caret"); foldout.Add(foldIcon);
            var foldCaption = new Label(property.Content.text) { pickingMode = PickingMode.Ignore };
            foldCaption.AddToClassList("thry-texture-caption"); foldout.Add(foldCaption);
            Track(foldCaption, () => { foldCaption.text = RetainedMaterialBody.SectionCaption(property);
            foldout.tooltip = RetainedMaterialBody.Hover(foldCaption.text, property.TooltipText, property.Note, "Expand or collapse texture settings"); });
            ChangedPropertyIndicator(row, foldCaption, property);
            var dimension = property.MaterialProperty.textureDimension;
            var textureType = dimension == UnityEngine.Rendering.TextureDimension.Cube ? typeof(Cubemap)
                : dimension == UnityEngine.Rendering.TextureDimension.Tex3D ? typeof(Texture3D)
                : dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray ? typeof(Texture2DArray)
                : dimension == UnityEngine.Rendering.TextureDimension.CubeArray ? typeof(CubemapArray) : typeof(Texture);
            var objectField = new ObjectField { objectType = textureType, allowSceneObjects = false };
            Bind<UnityEngine.Object>(objectField, property, p => p.textureValue, (p,v) => p.textureValue = v as Texture); value.Add(objectField);
            Track(objectField, () => objectField.SetEnabled(Model.CanEdit(property)));
            TextureAssetDisplay(objectField, property);
            var array = attributes.FirstOrDefault(a => a.Name == "TextureArray");
            if (array != null)
            {
                if (!Model.Shader.TextureArrayProperties.Contains(property)) Model.Shader.TextureArrayProperties.Add(property);
                objectField.tooltip = "Assign a texture array, or drop image frames or a GIF to create one.";
                Action<Texture2DArray, float> updateFrames = (texture, fps) =>
                {
                    if (texture == null) return;
                    string framesId = array.Args.Length > 0 ? array.Args[0] : property.Options.reference_property;
                    string fpsId = array.Args.Length > 1 ? array.Args[1] : property.Options.fps_property;
                    ShaderProperty target;
                    if (framesId != null && Model.Shader.PropertyDictionary.TryGetValue(framesId, out target)) Model.Number(target, texture.depth);
                    if (fps > 0 && fpsId != null && Model.Shader.PropertyDictionary.TryGetValue(fpsId, out target)) Model.Number(target, fps);
                };
                objectField.RegisterValueChangedCallback(e => updateFrames(e.newValue as Texture2DArray, 0));
                objectField.RegisterCallback<DragUpdatedEvent>(e =>
                {
                    if (DragAndDrop.paths.Length == 0 || DragAndDrop.objectReferences.OfType<Texture2DArray>().Any()) return;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                objectField.RegisterCallback<DragPerformEvent>(e =>
                {
                    if (DragAndDrop.paths.Length == 0 || DragAndDrop.objectReferences.OfType<Texture2DArray>().Any()) return;
                    DragAndDrop.AcceptDrag();
                    float fps;
                    var texture = Thry.ThryEditor.Helpers.Converter.PathsToTexture2DArray(DragAndDrop.paths, out fps);
                    if (texture != null) { Model.Edit(property, p => p.textureValue = texture); updateFrames(texture, fps); }
                    e.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
            }
            var details = new VisualElement(); details.AddToClassList("thry-texture-details"); root.Add(details);
            bool built = false;
            Action expand = () => {
                details.style.display = property.showFoldoutProperties ? DisplayStyle.Flex : DisplayStyle.None;
                foldIcon.image = Resources.Load<Texture2D>("ThryToolbar/header-caret-" + (property.showFoldoutProperties ? "down" : "right"));
                if (!property.showFoldoutProperties || built) return; built = true;
                var card = new RetainedTextureCard(Model, property, objectField, array != null);
                details.Add(card); Track(card, card.Synchronize);
                if (property.hasScaleOffset)
                {
                    VisualElement tiling, offset; details.Add(Row("Tiling", out tiling)); Vector(tiling, property, new[] { "X", "Y" }, 0, true);
                    details.Add(Row("Offset", out offset)); Vector(offset, property, new[] { "X", "Y" }, 2, true);
                    Track(tiling, () => tiling.SetEnabled(Model.CanEdit(property)));
                    Track(offset, () => offset.SetEnabled(Model.CanEdit(property)));
                }
                if (property.Options.reference_properties != null)
                    foreach (var name in property.Options.reference_properties) { ShaderProperty reference; if(Model.Shader.PropertyDictionary.TryGetValue(name,out reference)) details.Add(Field(reference)); }
                var textureTools = new VisualElement(); details.Add(textureTools);
                TextureTools(textureTools, property, attributes);
                Track(textureTools, () => textureTools.SetEnabled(Model.CanEdit(property)));
            };
            foldout.clicked += () => { property.showFoldoutProperties = !property.showFoldoutProperties; expand(); };
            Track(root, expand);
            if (property.Options.reference_property != null)
            {
                ShaderProperty reference;
                if (Model.Shader.PropertyDictionary.TryGetValue(property.Options.reference_property, out reference))
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
                Model.Shader.ActivateRetained(); _view.ShowLegacyMenu(property.RetainedContextMenu(), element);
            }, TrickleDown.TrickleDown);
        }
        private void TextureAssetDisplay(ObjectField field, ShaderTextureProperty property)
        {
            field.AddToClassList("thry-texture-asset");
            var display = field.Q(className: "unity-object-field-display");
            var selector = field.Q(className: "unity-object-field__selector");
            display.RegisterCallback<MouseDownEvent>(e => {
                if (e.target is VisualElement target && (target == selector || selector.Contains(target))) return;
                if (e.button != 0 || property.MaterialProperty.textureValue != null || property.MaterialProperty.hasMixedValue
                    || !field.enabledInHierarchy || !Model.CanEdit(property)) return;
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
            var clear = new Button(() => Model.Edit(property, p => p.textureValue = null)) { name = "clear-texture-asset", tooltip = "Clear texture" };
            clear.AddToClassList("thry-asset-clear"); display.Add(clear);
            var clearIcon = new Image { image = Resources.Load<Texture2D>("ThryToolbar/texture-clear"), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            clearIcon.AddToClassList("thry-texture-clear-icon"); clear.Add(clearIcon);
            clear.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            clear.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            clear.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefault(); e.StopImmediatePropagation(); Model.Edit(property, p => p.textureValue = null); }, TrickleDown.TrickleDown);
            Texture measuredTexture = null; int measuredVersion = -1;
            nativeIcon.style.display = DisplayStyle.None;
            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("thry-asset-tile"); display.Insert(0, tile);
            var preview = new Image { name = "texture-asset-thumbnail", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            tile.Add(preview);
            // Keep Unity's native picker beside the thumbnail, with metadata and
            // the separate clear action at the trailing edge of the field.
            display.Insert(1, selector);
            Track(field, () => {
                var texture = property.MaterialProperty.textureValue;
                bool mixed = property.MaterialProperty.hasMixedValue;
                bool assigned = texture != null && !mixed;
                tile.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
                clear.style.display = texture != null || mixed ? DisplayStyle.Flex : DisplayStyle.None;
                clear.SetEnabled(Model.CanEdit(property));
                size.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
                if (assigned && (measuredTexture != texture || measuredVersion != EditorUtility.GetDirtyCount(texture)))
                {
                    measuredTexture = texture; measuredVersion = EditorUtility.GetDirtyCount(texture);
                    var path = AssetDatabase.GetAssetPath(texture);
                    long bytes = Helpers.TextureHelper.VRAM.CalcSize(texture).size;
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
                assetName.text = mixed ? "Multiple textures" : assigned ? texture.name : "Choose texture…";
                assetName.tooltip = assigned ? AssetDatabase.GetAssetPath(texture) : "Click to choose a texture, or drag one here";
                field.EnableInClassList("thry-asset-unassigned", !assigned);
            });
        }
        partial void TextureTools(VisualElement parent, ShaderTextureProperty property, DrawerAttribute[] attributes);
        private bool Special(VisualElement parent, ShaderProperty property, DrawerAttribute[] attributes) { bool handled = false; SpecialFields(parent,property,attributes,ref handled); return handled; }
        partial void SpecialFields(VisualElement parent, ShaderProperty property, DrawerAttribute[] attributes, ref bool handled);
    }
}
#endif
