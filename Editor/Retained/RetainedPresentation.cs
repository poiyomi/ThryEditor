#if UNITY_2021_3_OR_NEWER
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        private bool Presentation(VisualElement root, ShaderProperty property, DrawerAttribute[] attributes)
        {
            bool handled = false;
            foreach (var attribute in attributes)
            {
                var args = attribute.Args;
                switch (attribute.Name)
                {
                    case "Header":
                    case "ThryHeaderLabel":
                        var heading = new Label(args.FirstOrDefault() ?? "") { enableRichText = true };
                        heading.AddToClassList("thry-field-heading");
                        if (attribute.Name == "ThryHeaderLabel" && args.Length > 1) heading.style.fontSize = Mathf.Max(1, DrawerAttribute.Number(args[1]));
                        root.Add(heading);
                        break;
                    case "Space":
                    case "ThrySpace":
                        var space = new VisualElement { name = "thry-space", pickingMode = PickingMode.Ignore };
                        space.style.height = args.Length > 0 ? Mathf.Max(0, DrawerAttribute.Number(args[0])) : 10;
                        space.style.flexShrink = 0; root.Add(space);
                        break;
                    case "ThrySeperator":
                        root.Add(PresentationSeparator(args));
                        break;
                    case "Helpbox":
                        var help = new HelpBox(property.Content.text, args.Length == 0 ? HelpBoxMessageType.Info
                            : (HelpBoxMessageType)Mathf.Clamp((int)DrawerAttribute.Number(args[0]), 0, 3));
                        help.name = "helpbox-" + property.MaterialProperty.name;
                        if (args.Length > 1)
                        {
                            int lines = Mathf.Max(0, (int)DrawerAttribute.Number(args[1]));
                            var text = help.Q<Label>(className: "unity-help-box__label");
                            if (text != null && lines > 0) text.style.minHeight = lines * EditorGUIUtility.singleLineHeight;
                        }
                        if (args.Length > 2)
                        {
                            string[] resources = { "help", "thryEditor_Help", "thryEditor_Warning", "thryEditor_Danger", "thryEditor_Pass", "thryEditor_Fail" };
                            int requested = (int)DrawerAttribute.Number(args[2]);
                            int iconIndex = requested >= 0 && requested < resources.Length ? requested : 0;
                            var texture = Resources.Load<Texture2D>(resources[iconIndex]) ?? Resources.Load<Texture2D>(resources[0]);
                            var icon = help.Q(className: "unity-help-box__icon");
                            if (texture != null)
                            {
                                if (icon == null) { icon = new VisualElement { pickingMode = PickingMode.Ignore }; icon.AddToClassList("unity-help-box__icon"); help.Insert(0, icon); }
                                icon.style.display = DisplayStyle.Flex; icon.style.backgroundImage = texture;
                            }
                        }
                        if (property.Options.onClick != null)
                        {
                            help.focusable = true;
                            help.RegisterCallback<ClickEvent>(e => { if (e.button == 0) PerformPresentationAction(property, property.Options.onClick); });
                            help.RegisterCallback<NavigationSubmitEvent>(e => { PerformPresentationAction(property, property.Options.onClick); e.StopPropagation(); });
                        }
                        root.Add(help); handled = true;
                        break;
                    case "ThryHeader":
                    case "ThryRichLabel":
                    case "ThryDescription":
                        var label = new Label(property.Content.text) { enableRichText = true };
                        label.AddToClassList(attribute.Name == "ThryDescription" ? "thry-description" : "thry-field-heading");
                        label.style.whiteSpace = WhiteSpace.Normal;
                        if (args.Length > 0) label.style.fontSize = Mathf.Max(1, DrawerAttribute.Number(args[0]));
                        if (attribute.Name == "ThryHeader" || attribute.Name == "ThryDescription")
                        {
                            label.style.marginTop = Mathf.Max(0, property.Options.margin_top);
                            label.style.marginBottom = Mathf.Max(0, property.Options.margin_bottom);
                        }
                        root.Add(label); handled = true;
                        break;
                    case "LocalMessage":
                    case "RemoteMessage":
                        Drawers.LocalMessageDrawer message = attribute.Name == "RemoteMessage" ? new Drawers.RemoteMessageDrawer() : new Drawers.LocalMessageDrawer();
                        root.Add(message.CreateRetained(property.MaterialProperty.displayName, PresentationTargets(property),
                            action => PerformPresentationAction(property, action), () => RetainedMaterialModel.HasValidTargets(Model.Editor)));
                        handled = true;
                        break;
                    case "ThryCustomGUI":
                        AddCustomPresentation(root, property, args); handled = true;
                        break;
                }
            }
            return handled;
        }

        private static VisualElement PresentationSeparator(string[] args)
        {
            int offset = 0; Color color = default(Color); bool explicitColor = false;
            float numeric;
            if (args.Length > 0 && !float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out numeric))
            {
                string text = args[0].StartsWith("#") ? args[0] : "#" + args[0];
                explicitColor = ColorUtility.TryParseHtmlString(text, out color); offset = 1;
            }
            int count = args.Length - offset;
            float thickness = count > 0 ? Mathf.Max(0, DrawerAttribute.Number(args[offset])) : 1;
            float top = count > 1 ? Mathf.Max(0, DrawerAttribute.Number(args[offset + 1])) : count > 0 ? 5 : 0;
            float bottom = count > 2 ? Mathf.Max(0, DrawerAttribute.Number(args[offset + 2])) : top;
            var line = new VisualElement { name = "thry-separator", pickingMode = PickingMode.Ignore };
            line.style.height = thickness; line.style.flexShrink = 0;
            line.style.marginTop = top; line.style.marginBottom = bottom;
            if (explicitColor) line.style.backgroundColor = color;
            return line;
        }

        private void PerformPresentationAction(ShaderProperty property, DefineableAction action)
        {
            if (action == null || !RetainedMaterialModel.HasValidTargets(Model.Editor) || !Model.CanEdit(property)) return;
            Model.Shader.ActivateRetained();
            bool mutation = action.type == DefineableActionType.SET_PROPERTY || action.type == DefineableActionType.SET_TAG || action.type == DefineableActionType.SET_SHADER;
            var materials = PresentationTargets(property);
            if (materials.Length == 0) return;
            // Override tags and shader changes need a complete native material snapshot.
            if (mutation) Undo.RegisterCompleteObjectUndo(materials, property.Content.text);
            if (action.type == DefineableActionType.SET_PROPERTY) SetPresentationProperty(action.data, materials);
            else action.Perform(materials);
            if (mutation)
            {
                foreach (var material in materials) EditorUtility.SetDirty(material);
                Model.Editor.PropertiesChanged(); Model.Notify();
            }
        }

        private Material[] PresentationTargets(ShaderProperty property) => property.MaterialProperty.targets.OfType<Material>()
            .Where(material => material != null && Model.Shader.Materials.Contains(material)).ToArray();

        private void SetPresentationProperty(string definition, Material[] materials)
        {
            var assignment = (definition ?? "").Split(new[] { '=' }, 2);
            if (assignment.Length != 2) return;
            string key = assignment[0].Trim(), value = assignment[1].Trim();
            ShaderProperty target;
            if (Model.Shader.PropertyDictionary.TryGetValue(key, out target))
            {
                if (!Model.CanEdit(target)) return;
                foreach (var material in materials.Where(material => target.MaterialProperty.targets.Contains(material)))
                {
                    MaterialHelper.SetValue(MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { material }, key), value);
                    MaterialEditor.ApplyMaterialPropertyDrawers(new UnityEngine.Object[] { material });
                }
            }
            else foreach (var material in materials)
            {
                int queue;
                if (key == "render_queue" && int.TryParse(value, out queue)) material.renderQueue = queue;
                else if (key == "render_type") material.SetOverrideTag("RenderType", value);
                else if (key == "preview_type") material.SetOverrideTag("PreviewType", value);
                else if (key == "ignore_projector") material.SetOverrideTag("IgnoreProjector", value);
            }
        }

        private void AddCustomPresentation(VisualElement root, ShaderProperty property, string[] args)
        {
            var type = args.Length == 3 ? Type.GetType(args[0] + ", " + args[1]) : null;
            var method = type?.GetMethod(args[2], BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Rect), typeof(MaterialProperty), typeof(GUIContent), typeof(MaterialEditor), typeof(ShaderEditor) }, null);
            if (method == null)
            {
                root.Add(new HelpBox("The custom inspector control is unavailable.", HelpBoxMessageType.Warning)); return;
            }
            root.Add(new IMGUIContainer(() =>
            {
                if (!RetainedMaterialModel.HasValidTargets(Model.Editor)) return;
                Model.Shader.ActivateRetained(); Model.Shader.CurrentProperty = property;
                using (new EditorGUI.DisabledScope(!Model.CanEdit(property)))
                {
                    EditorGUI.BeginChangeCheck();
                    bool completed = false, changed;
                    try
                    {
                        method.Invoke(null, new object[] { EditorGUILayout.GetControlRect(false, 0), property.MaterialProperty, property.Content, Model.Editor, Model.Shader });
                        completed = true;
                    }
                    catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
                    finally { changed = EditorGUI.EndChangeCheck(); }
                    if (completed && changed) Model.Notify();
                }
            }) { name = "custom-gui-" + property.MaterialProperty.name });
        }
    }
}
#endif
