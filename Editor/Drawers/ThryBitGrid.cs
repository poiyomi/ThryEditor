using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Drawers
{
    // On a Vector property each component holds the bits that use one operator: x |, y &, z ~, w ^.
    public class ThryBitGridDrawer : MaterialPropertyDrawer
    {
        private const float RowSpacing = 2f;
        private const float OperatorWidth = 18f;
        private static readonly string[] Operators = { "|", "&", "~", "^" };
        private static readonly string[] OperatorNames = { "|  Or", "&  And", "~  Not", "^  Xor" };
        private readonly int _bits;
        private readonly int _columns;
        private readonly string[] _stock;
        private EnumLabels.Source _labels;

        public ThryBitGridDrawer(float bits, float columns)
        {
            _bits = Mathf.Clamp((int)bits, 1, 24);
            _columns = Mathf.Max(1, (int)columns);
            _stock = Enumerable.Range(1, _bits).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        }

        private int Rows => (_bits + _columns - 1) / _columns;

        private static int Operator(Vector4 value, int mask)
        {
            for (int c = 0; c < 4; c++) if (((int)value[c] & mask) != 0) return c;
            return -1;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            if (_labels == null) _labels = new EnumLabels.Source(prop.name, _stock);
            var renderer = ShaderEditor.Active?.ActiveRenderer;
            _labels.Update(prop.targets, renderer != null ? new[] { renderer } : System.Array.Empty<Renderer>());
            bool operators = prop.GetPropertyType() == ShaderPropertyType.Vector;
            float line = EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(new Rect(position.x, position.y, position.width, line), label);
            Rect grid = EditorGUI.IndentedRect(position);
            float width = grid.width / _columns;
            Vector4 value = operators ? prop.vectorValue : new Vector4(prop.GetNumber(), 0, 0, 0);
            Vector4 next = value;
            for (int bit = 0; bit < _bits; bit++)
            {
                int mask = 1 << bit, row = bit / _columns + 1, column = bit % _columns;
                var cell = new Rect(grid.x + column * width, position.y + row * (line + RowSpacing), width, line);
                int current = Operator(value, mask);
                bool on = current >= 0 && !prop.hasMixedValue;
                var pick = operators ? new Rect(cell.x, cell.y, cell.width - OperatorWidth, line) : cell;
                if (GUI.Toggle(pick, on, new GUIContent(_labels.Labels[bit], "Tag " + _stock[bit]), operators ? EditorStyles.miniButtonLeft : EditorStyles.miniButton) != on)
                {
                    if (on) for (int c = 0; c < 4; c++) next[c] = (int)next[c] & ~mask;
                    else next.x = (int)next.x | mask;
                }
                if (!operators) continue;
                var op = new Rect(pick.xMax, cell.y, OperatorWidth, line);
                if (GUI.Button(op, current >= 0 ? Operators[current] : "▾", EditorStyles.miniButtonRight))
                {
                    var menu = new GenericMenu();
                    for (int c = 0; c < 4; c++)
                    {
                        int chosen = c;
                        menu.AddItem(new GUIContent(OperatorNames[c]), c == current, () =>
                        {
                            var v = prop.vectorValue;
                            for (int k = 0; k < 4; k++) v[k] = (int)v[k] & ~mask;
                            v[chosen] = (int)v[chosen] | mask;
                            prop.vectorValue = v;
                            _labels.Picked();
                        });
                    }
                    menu.DropDown(op);
                }
            }
            if (next == value) return;
            if (operators) prop.vectorValue = next;
            else prop.SetNumber(next.x);
            _labels.Picked();
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return (Rows + 1) * EditorGUIUtility.singleLineHeight + Rows * RowSpacing;
        }
    }
}
