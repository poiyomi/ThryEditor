using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Thry.ThryEditor.Helpers;
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
                e.PreventDefaultHandling(); e.StopImmediatePropagation();
                view.ShowLegacyMenu(menu, target, new Rect(e.position, Vector2.zero));
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
                    var dependency = ConditionProperty(condition, part);
                    if (dependency != null) return dependency;
                }
            }
            return null;
        }

        static readonly Regex ComparisonPattern = new Regex(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=|>=|<=|>|<)\s*(-?[0-9]+(?:\.[0-9]+)?|[A-Za-z_][A-Za-z0-9_]*)");

        // Prefer the property of a comparison that currently fails, so a combined condition points at what to change.
        internal static ShaderProperty ConditionProperty(DefineableCondition condition, ShaderPart part)
        {
            var shader = part.MyShaderUI;
            string source = condition.ToString();
            ShaderProperty first = null;
            foreach (Match comparison in ComparisonPattern.Matches(source))
            {
                ShaderProperty property;
                if (!shader.PropertyDictionary.TryGetValue(comparison.Groups[1].Value, out property) || property == part) continue;
                if (!DefineableCondition.Parse(comparison.Value).Test()) return property;
                if (first == null) first = property;
            }
            if (first != null) return first;
            foreach (Match token in Regex.Matches(source, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
            {
                ShaderProperty dependency;
                if (shader.PropertyDictionary.TryGetValue(token.Value, out dependency) && dependency != part) return dependency;
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
                if (part.MaterialProperty.GetPropertyType() == UnityEngine.Rendering.ShaderPropertyType.Texture
                    && (part.MaterialProperty.GetPropertyFlags() & UnityEngine.Rendering.ShaderPropertyFlags.NonModifiableTextureData) != 0)
                    return RetainedText.Get(shader, "texture_shader_owned", "This texture is supplied by the shader and cannot be reassigned.");
                if (!part.IsExemptFromLockedDisabling && part.MaterialProperty.targets.OfType<Material>()
                    .Any(material => material != null && shader.Materials.Contains(material) && material.HasProperty(part.MaterialProperty.name) && material.IsLocked()
                        && !(part.IsAnimatable && part is ShaderProperty property && !string.IsNullOrEmpty(property.GetOwnerAnimatedTag(material)))))
                    return "Unlock the shader to edit this property.";
                foreach (var material in part.MaterialProperty.targets.OfType<Material>().Where(m => shader.Materials.Contains(m)))
                    if (material != null && material.HasProperty(part.MaterialProperty.name) && material.IsPropertyLockedByAncestor(part.MaterialProperty.name))
                        return "This property is locked by a parent material. Edit or unlock it on the parent material.";
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

        static string Describe(DefineableCondition condition, ShaderEditor shader) => "Available when " + Condition(condition, shader) + ".";

        internal static string Condition(DefineableCondition condition, ShaderEditor shader)
        {
            var choices = new List<string>();
            string source = ComparisonPattern.Replace(condition.ToString(), match =>
            {
                ShaderProperty property;
                string comparison = match.Groups[2].Value, value = match.Groups[3].Value;
                float number;
                if ((comparison != "==" && comparison != "!=") || !shader.PropertyDictionary.TryGetValue(match.Groups[1].Value, out property)
                    || property.ShaderPropertyIndex < 0 || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return match.Value;
                bool equal = comparison == "==";
                if ((value == "0" || value == "1") && property.MyShader.GetPropertyAttributes(property.ShaderPropertyIndex).Any(a => new DrawerAttribute(a).Name.Contains("Toggle")))
                    return match.Groups[1].Value + ((value == "1") == equal ? " is On" : " is Off");
                string[] names; float[] values;
                int index = EnumChoices(property, out names, out values) ? Array.IndexOf(values, number) : -1;
                if (index < 0) return match.Value;
                // The choice is put back after the identifier and operator rewrites below.
                choices.Add(names[index]);
                return match.Groups[1].Value + (equal ? " is " : " is not ") + "\u0001" + (choices.Count - 1) + "\u0001";
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
            expression = Regex.Replace(expression, "\u0001([0-9]+)\u0001", match => choices[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)]);
            return Regex.Replace(expression, @"\s+", " ").Trim();
        }

        internal static bool EnumChoices(ShaderProperty property, out string[] names, out float[] values)
        {
            names = null; values = null;
            if (property?.MyShader == null || property.ShaderPropertyIndex < 0 || property.ShaderPropertyIndex >= property.MyShader.GetPropertyCount()) return false;
            var attribute = property.MyShader.GetPropertyAttributes(property.ShaderPropertyIndex).Select(a => new DrawerAttribute(a))
                .FirstOrDefault(a => a.Name == "ThryWideEnum" || a.Name == "Enum" || a.Name == "KeywordEnum");
            if (attribute == null || attribute.Args.Length == 0) return false;
            Type enumType = attribute.Args.Length == 1 || (attribute.Name == "ThryWideEnum" && attribute.Args.Length == 2) ? EnumType(attribute.Args[0]) : null;
            if (attribute.Name == "KeywordEnum")
            {
                names = attribute.Args;
                values = Enumerable.Range(0, names.Length).Select(i => (float)i).ToArray();
            }
            else if (enumType != null)
            {
                names = Enum.GetNames(enumType);
                values = Enum.GetValues(enumType).Cast<object>().Select(Convert.ToSingle).ToArray();
            }
            else if (attribute.Args.Length > 1)
            {
                names = new string[attribute.Args.Length / 2];
                values = new float[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = attribute.Args[i * 2];
                    try { values[i] = DrawerAttribute.Number(attribute.Args[i * 2 + 1]); }
                    catch (FormatException) { names = null; values = null; return false; }
                }
            }
            else return false;
            var locale = property.MyShaderUI?.Locale;
            if (locale != null) names = names.Select(name => locale.Get(name, name)).ToArray();
            return true;
        }

        static readonly Dictionary<string, Type> EnumTypes = new Dictionary<string, Type>();
        static Type EnumType(string name)
        {
            Type type;
            if (EnumTypes.TryGetValue(name, out type)) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { type = assembly.GetType(name, false); }
                catch (ArgumentException) { type = null; }
                if (type != null && type.IsEnum) break;
                type = null;
            }
            EnumTypes[name] = type;
            return type;
        }
    }
}
