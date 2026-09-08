#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using Thry.ThryEditor.DataStructs;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed partial class RetainedFields
    {
        private static StencilConfig StencilConfiguration(DrawerAttribute attribute)
        {
            var a = attribute.Args;
            return a.Length == 10 ? StencilConfig.WithDefaults(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9])
                : a.Length == 1 ? StencilConfig.WithVariant(a[0], attribute.Name) : StencilConfig.WithDefaults();
        }

        private bool IsLegacyStencilStatus(ShaderProperty property, DrawerAttribute[] attributes)
        {
            if (!attributes.Any(a => a.Name == "Helpbox") || !(property.Parent is ShaderGroup group)) return false;
            // Only replace the paired presentation boxes next to an actual summary decorator.
            // Its live result does not depend on a serialized editor-only check-result cache.
            foreach (var sibling in group.Children.OfType<ShaderProperty>())
            foreach (var raw in sibling.MyShader.GetPropertyAttributes(sibling.ShaderPropertyIndex))
            {
                var attribute = new DrawerAttribute(raw);
                if (attribute.Name != "ThryStencilSummary") continue;
                string result = StencilConfiguration(attribute).StencilCheckResultPropertyName;
                if (!result.EndsWith("Result", StringComparison.Ordinal)) continue;
                string prefix = result.Substring(0, result.Length - "Result".Length);
                if (property.MaterialProperty.name == prefix + "Passed" || property.MaterialProperty.name == prefix + "Failed") return true;
            }
            return false;
        }

        private static int StencilValue(Material material, string id, int fallback = 0)
            => material.HasProperty(id) ? Mathf.Clamp((int)material.GetFloat(id), 0, 255) : fallback;

        private static int StencilOutput(Material material, StencilConfig c, out bool passed)
            => StencilOperationsHelper.ComputeFinalStencilOutput(
                StencilValue(material, c.StencilBufferValuePropertyName), StencilValue(material, c.StencilRefPropertyName),
                StencilValue(material, c.StencilReadMaskPropertyName, 255), StencilValue(material, c.StencilWriteMaskPropertyName, 255),
                (CompareFunction)StencilValue(material, c.StencilCompareFunctionPropertyName, (int)CompareFunction.Always),
                (StencilOp)StencilValue(material, c.StencilPassOpPropertyName), (StencilOp)StencilValue(material, c.StencilFailOpPropertyName),
                (StencilOp)StencilValue(material, c.StencilZFailOpPropertyName), StencilValue(material, c.StencilIsOccludedPropertyName) != 0, out passed);

        private void StencilEdit(StencilConfig config, string id, Func<int, int> edit)
        {
            ShaderProperty property;
            if (!Model.Shader.PropertyDictionary.TryGetValue(id, out property) || !Model.CanEdit(property)) return;
            Model.Edit(property, p => p.SetNumber(Mathf.Clamp(edit((int)p.GetNumber()), 0, 255)), true);
            ShaderProperty result;
            if (Model.Shader.PropertyDictionary.TryGetValue(config.StencilCheckResultPropertyName, out result))
                Model.Edit(result, p => { bool passed; StencilOutput((Material)p.targets[0], config, out passed); p.SetNumber(passed ? 1 : 0); }, true);
        }

        private VisualElement StencilView(StencilConfig config, bool summary)
        {
            var block = new VisualElement { name = (summary ? "stencil-summary-" : "stencil-grid-") + config.StencilCheckResultPropertyName };
            block.AddToClassList("thry-stencil-view");
            var sheet = Resources.Load<StyleSheet>("ThryStencil"); if (sheet != null) block.styleSheets.Add(sheet);
            block.RegisterCallback<GeometryChangedEvent>(e => block.EnableInClassList("thry-stencil-narrow", block.contentRect.width < 430));
            if (summary)
            {
                StencilEnum<CompareFunction>(block, config, "Comparison", config.StencilCompareFunctionPropertyName);
                var comparison = new Label { name = "stencil-comparison" }; comparison.AddToClassList("thry-stencil-explanation"); block.Add(comparison);
                Track(comparison, () => {
                    var texts = Model.Shader.Materials.Select(m => {
                        var compare = (CompareFunction)StencilValue(m, config.StencilCompareFunctionPropertyName, (int)CompareFunction.Always);
                        int mask = StencilValue(m, config.StencilReadMaskPropertyName, 255);
                        return "Reference " + (StencilValue(m, config.StencilRefPropertyName) & mask) + "  " + StencilCompareSymbol(compare)
                            + "  buffer " + (StencilValue(m, config.StencilBufferValuePropertyName) & mask) + "  (after read mask)";
                    }).Distinct().ToArray();
                    comparison.text = texts.Length == 1 ? texts[0] : "Comparison inputs differ between selected materials.";
                });
                StencilEnum<StencilOp>(block, config, "Pass · visible", config.StencilPassOpPropertyName);
                StencilEnum<StencilOp>(block, config, "Pass · occluded", config.StencilZFailOpPropertyName);
                StencilEnum<StencilOp>(block, config, "Fail", config.StencilFailOpPropertyName);
            }
            else
            {
                var weights = new VisualElement(); weights.AddToClassList("thry-stencil-byte-row"); weights.AddToClassList("thry-stencil-weights"); block.Add(weights);
                var caption = new Label("Bit value"); caption.AddToClassList("thry-stencil-label"); weights.Add(caption);
                var values = new VisualElement(); values.AddToClassList("thry-stencil-values"); weights.Add(values);
                var bits = new VisualElement(); bits.AddToClassList("thry-stencil-bits"); values.Add(bits);
                for (int bit = 7; bit >= 0; bit--) { var label = new Label((1 << bit).ToString()); label.AddToClassList("thry-stencil-bit"); bits.Add(label); }
                var decimalLabel = new Label("Value"); decimalLabel.AddToClassList("thry-stencil-decimal"); values.Add(decimalLabel);
                StencilByte(block, config, "Existing buffer", config.StencilBufferValuePropertyName, m => StencilValue(m, config.StencilBufferValuePropertyName));
                StencilByte(block, config, "Reference", config.StencilRefPropertyName, m => StencilValue(m, config.StencilRefPropertyName));
                StencilByte(block, config, "Read mask", config.StencilReadMaskPropertyName, m => StencilValue(m, config.StencilReadMaskPropertyName, 255));
                StencilByte(block, config, "Masked buffer", null, m => StencilValue(m, config.StencilBufferValuePropertyName) & StencilValue(m, config.StencilReadMaskPropertyName, 255));
                StencilByte(block, config, "Masked reference", null, m => StencilValue(m, config.StencilRefPropertyName) & StencilValue(m, config.StencilReadMaskPropertyName, 255));
                StencilByte(block, config, "Write mask", config.StencilWriteMaskPropertyName, m => StencilValue(m, config.StencilWriteMaskPropertyName, 255));
                StencilByte(block, config, "Resulting buffer", null, m => { bool passed; return StencilOutput(m, config, out passed); });
            }
            var outcome = new Label { name = "stencil-outcome" }; outcome.AddToClassList("thry-stencil-outcome"); block.Add(outcome);
            Track(outcome, () => {
                var results = Model.Shader.Materials.Select(m => {
                    bool passed; int output = StencilOutput(m, config, out passed);
                    bool occluded = StencilValue(m, config.StencilIsOccludedPropertyName) != 0;
                    var op = (StencilOp)StencilValue(m, !passed ? config.StencilFailOpPropertyName : occluded ? config.StencilZFailOpPropertyName : config.StencilPassOpPropertyName);
                    return (passed ? "Test passes" : "Test fails") + " · " + (!passed ? "Fail" : occluded ? "Occluded" : "Visible") + " → " + ObjectNames.NicifyVariableName(op.ToString()) + " · Buffer " + output;
                }).Distinct().ToArray();
                outcome.text = results.Length == 1 ? results[0] : "Simulation results differ between selected materials.";
            });
            return block;
        }

        private void StencilByte(VisualElement block, StencilConfig config, string label, string id, Func<Material, int> read)
        {
            var row = new VisualElement { name = "stencil-row-" + (id ?? label.Replace(" ", "-")) }; row.AddToClassList("thry-stencil-byte-row"); block.Add(row);
            var caption = new Label(label); caption.AddToClassList("thry-stencil-label"); row.Add(caption);
            var valuesRow = new VisualElement(); valuesRow.AddToClassList("thry-stencil-values"); row.Add(valuesRow);
            var bits = new VisualElement(); bits.AddToClassList("thry-stencil-bits"); valuesRow.Add(bits);
            for (int i = 7; i >= 0; i--)
            {
                int bit = i; var cell = new Button { name = "stencil-bit-" + bit, tooltip = "Bit " + bit + " · value " + (1 << bit) }; cell.AddToClassList("thry-stencil-bit"); bits.Add(cell);
                if (id != null) cell.clicked += () => {
                    bool allSet = Model.Shader.Materials.All(m => (read(m) & (1 << bit)) != 0);
                    StencilEdit(config, id, v => allSet ? v & ~(1 << bit) : v | (1 << bit));
                };
                else { cell.SetEnabled(false); row.AddToClassList("thry-stencil-derived"); }
                Track(cell, () => {
                    var values = Model.Shader.Materials.Select(m => (read(m) >> bit) & 1).Distinct().ToArray();
                    cell.text = values.Length == 1 ? values[0].ToString() : "—";
                    cell.EnableInClassList("thry-stencil-bit-set", values.Length == 1 && values[0] == 1);
                    ShaderProperty p; if (id != null) cell.SetEnabled(Model.Shader.PropertyDictionary.TryGetValue(id, out p) && Model.CanEdit(p));
                });
            }
            if (id == null)
            {
                var value = new Label(); value.AddToClassList("thry-stencil-decimal"); valuesRow.Add(value);
                Track(value, () => { var values = Model.Shader.Materials.Select(read).Distinct().ToArray(); value.text = values.Length == 1 ? values[0].ToString() : "—"; });
            }
            else
            {
                var field = new IntegerField { name = "stencil-value-" + id, isDelayed = true }; field.AddToClassList("thry-stencil-decimal"); valuesRow.Add(field);
                field.RegisterValueChangedCallback(e => StencilEdit(config, id, old => e.newValue));
                Track(field, () => {
                    var values = Model.Shader.Materials.Select(read).Distinct().ToArray(); field.SetValueWithoutNotify(values.FirstOrDefault()); field.showMixedValue = values.Length > 1;
                    ShaderProperty p; field.SetEnabled(Model.Shader.PropertyDictionary.TryGetValue(id, out p) && Model.CanEdit(p));
                });
            }
        }

        private void StencilEnum<T>(VisualElement block, StencilConfig config, string label, string id) where T : struct
        {
            var choices = Enum.GetNames(typeof(T));
            var field = new DropdownField(label, choices.Select(ObjectNames.NicifyVariableName).ToList(), 0) { name = "stencil-operation-" + id };
            field.AddToClassList("thry-stencil-operation"); RetainedWindow.Dropdown(field); block.Add(field);
            field.RegisterValueChangedCallback(e => { int index = field.choices.IndexOf(e.newValue); if (index >= 0) StencilEdit(config, id, old => Convert.ToInt32(Enum.Parse(typeof(T), choices[index]))); });
            Track(field, () => {
                var values = Model.Shader.Materials.Select(m => StencilValue(m, id, typeof(T) == typeof(CompareFunction) ? (int)CompareFunction.Always : 0)).Distinct().ToArray();
                string name = Enum.GetName(typeof(T), values.FirstOrDefault()); field.SetValueWithoutNotify(name == null ? "Unknown" : ObjectNames.NicifyVariableName(name)); field.showMixedValue = values.Length > 1;
                ShaderProperty p; field.SetEnabled(Model.Shader.PropertyDictionary.TryGetValue(id, out p) && Model.CanEdit(p));
            });
        }

        private static string StencilCompareSymbol(CompareFunction compare)
        {
            switch (compare)
            {
                case CompareFunction.Equal: return "=";
                case CompareFunction.NotEqual: return "≠";
                case CompareFunction.Less: return "<";
                case CompareFunction.LessEqual: return "≤";
                case CompareFunction.Greater: return ">";
                case CompareFunction.GreaterEqual: return "≥";
                case CompareFunction.Never: return "· Never passes ·";
                default: return "· Always passes ·";
            }
        }
    }
}
#endif
