#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>Explain disabled fields without adding a second set of editing rules.</summary>
    internal static class RetainedAvailability
    {
        internal static void Install(VisualElement root)
        {
            root.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1 || !(root is MaterialInspectorView view)) return;
                var target = e.target as VisualElement;
                if (target == null || target.enabledInHierarchy) return;
                ShaderPart part = null;
                for (var element = target; element != null && element != root; element = element.parent)
                    if (element.userData is ShaderPart found) { part = found; break; }
                if (part == null) return;
                var dependency = ControllingProperty(part);
                if (dependency == null) return;
                var menu = new UnityEditor.GenericMenu();
                menu.AddItem(new GUIContent(RetainedText.Get(part.MyShaderUI, "find_controller", "Find controlling property") + ": " + RetainedMaterialBody.SectionCaption(dependency)), false, () => view.SearchProperty(dependency));
                e.PreventDefault(); e.StopImmediatePropagation();
                view.ShowLegacyMenu(menu, target);
            }, TrickleDown.TrickleDown);
            // Listen on the enabled inspector so disabled descendants can still explain
            // themselves. Resolve at hover time to reflect changes without polling.
            root.RegisterCallback<TooltipEvent>(e =>
            {
                var target = e.target as VisualElement;
                if (target == null || target.enabledInHierarchy) return;
                for (var element = target; element != null && element != root; element = element.parent)
                {
                    var part = element.userData as ShaderPart;
                    if (part == null) continue;
                    string reason = Reason(part);
                    if (string.IsNullOrEmpty(reason)) return;
                    string original = part.Content.tooltip;
                    e.tooltip = string.IsNullOrEmpty(original) ? reason : original + "\n\n" + reason;
                    e.rect = (target ?? element).worldBound;
                    e.StopImmediatePropagation();
                    return;
                }
            }, TrickleDown.TrickleDown);
        }

        internal static ShaderProperty ControllingProperty(ShaderPart part)
        {
            part.MyShaderUI.ActivateRetained();
            for (var current = part; current != null; current = current.Parent)
            {
                var conditions = new[] { current.Options.condition_enable, current == part ? null : current.Options.condition_enable_children };
                foreach (var condition in conditions)
                {
                    if (condition == null || condition.Test()) continue;
                    foreach (Match token in Regex.Matches(condition.ToString(), @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
                    {
                        ShaderProperty dependency;
                        if (part.MyShaderUI.PropertyDictionary.TryGetValue(token.Value, out dependency) && dependency != part) return dependency;
                    }
                }
            }
            return null;
        }

        internal static string Reason(ShaderPart part)
        {
            var shader = part.MyShaderUI;
            if (shader == null) return null;
            shader.ActivateRetained();
            if (shader.IsInAnimationMode && !RetainedAnimation.IsSupported)
                return RetainedText.Get(shader, "animation_unavailable", "Animation editing is unavailable in this Unity version. Stop animation preview to edit the material.");
            if (part.MaterialProperty != null)
            {
                if ((part.MaterialProperty.flags & UnityEditor.MaterialProperty.PropFlags.NonModifiableTextureData) != 0)
                    return RetainedText.Get(shader, "texture_shader_owned", "This texture is supplied by the shader and cannot be reassigned.");
                if (!part.IsExemptFromLockedDisabling && part.MaterialProperty.targets.OfType<Material>()
                    .Any(material => material != null && shader.Materials.Contains(material) && material.HasProperty(part.MaterialProperty.name) && material.IsLocked()
                        && !(part.IsAnimatable && part is ShaderProperty property && !string.IsNullOrEmpty(property.GetOwnerAnimatedTag(material)))))
                    return "Unlock the shader to edit this property.";
#if UNITY_2022_1_OR_NEWER
                foreach (var material in part.MaterialProperty.targets.OfType<Material>().Where(m => shader.Materials.Contains(m)))
                    if (material != null && material.HasProperty(part.MaterialProperty.name) && material.IsPropertyLockedByAncestor(part.MaterialProperty.name))
                        return "This property is locked by a parent material. Edit or unlock it on the parent material.";
#endif
            }
            if (part.Options.condition_enable != null && !part.Options.condition_enable.Test())
                return Describe(part.Options.condition_enable, shader);
            for (var parent = part.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.Options.condition_enable != null && !parent.Options.condition_enable.Test())
                    return Describe(parent.Options.condition_enable, shader);
                if (parent.Options.condition_enable_children != null && !parent.Options.condition_enable_children.Test())
                    return Describe(parent.Options.condition_enable_children, shader);
            }
            return null;
        }

        static string Describe(DefineableCondition condition, ShaderEditor shader)
        {
            string source = Regex.Replace(condition.ToString(), @"\b([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=)\s*([01])\b", match =>
            {
                ShaderProperty toggle;
                if (!shader.PropertyDictionary.TryGetValue(match.Groups[1].Value, out toggle) || toggle.ShaderPropertyIndex < 0
                    || !toggle.MyShader.GetPropertyAttributes(toggle.ShaderPropertyIndex).Any(a => new DrawerAttribute(a).Name.Contains("Toggle"))) return match.Value;
                bool on = match.Groups[3].Value == "1";
                if (match.Groups[2].Value == "!=") on = !on;
                return match.Groups[1].Value + (on ? " is On" : " is Off");
            });
            string expression = Regex.Replace(source, @"\b[A-Za-z_][A-Za-z0-9_]*\b", match =>
            {
                ShaderProperty property;
                if (shader.PropertyDictionary.TryGetValue(match.Value, out property))
                {
                    var label = RetainedMaterialBody.SectionCaption(property).Split('|')[0].Trim();
                    return string.IsNullOrEmpty(label) ? UnityEditor.ObjectNames.NicifyVariableName(match.Value.TrimStart('_')) : label;
                }
                if (match.Value.Equals("RenderQueue", StringComparison.OrdinalIgnoreCase)) return "Render Queue";
                if (match.Value == "VRCSDK") return "SDK version";
                if (match.Value == "ThryEditor") return "Editor version";
                return match.Value;
            });
            expression = expression.Replace("&&", " and ").Replace("||", " or ")
                .Replace(">=", " is at least ").Replace("<=", " is at most ")
                .Replace("!=", " is not ").Replace("==", " is ")
                .Replace(">", " is greater than ").Replace("<", " is less than ");
            return "Available when " + Regex.Replace(expression, @"\s+", " ").Trim() + ".";
        }
    }
}
#endif
