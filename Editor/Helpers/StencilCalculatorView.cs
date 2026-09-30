using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Thry.ThryEditor.Drawers;
using Thry.ThryEditor.DataStructs;

namespace Thry.ThryEditor.Helpers
{
    public class StencilCalculatorView
    {
        private const float HorizontalLineThickness = 1f;
        private const float HorizontalLineThickThickness = 1.5f;
        private const float VerticalDividerThickness = 1f;
        private const float VerticalDividerAlpha = 0.5f;
        private const float VerticalDividerOffset = 0.5f;
        private const float VerticalDividerLeftOffset = 1.5f;
        private const float VerticalDividerDecimalOffset = 2.5f;

        private enum CalculatorLayoutElementType
        {
            InitialValue,
            Gap,
            ColumnHeader,
            ThickDivider,
            ReadMaskRows,
            ThinDivider,
            WriteMaskRows
        }

        private struct CalculatorLayoutElement
        {
            public CalculatorLayoutElementType Type;
            public float Height;

            public CalculatorLayoutElement(CalculatorLayoutElementType type, float height)
            {
                Type = type;
                Height = height;
            }
        }

        private class CalculatorLayout
        {
            public MaterialProperty InitialValueProperty;
            public List<CalculatorLayoutElement> Elements;

            // Y of the first element of this type, measured from the top of the calculator.
            public float GetElementY(CalculatorLayoutElementType type, float startY)
            {
                float y = startY;
                foreach (CalculatorLayoutElement element in Elements)
                {
                    if (element.Type == type) return y;
                    y += element.Height;
                }
                return startY;
            }
        }

        private readonly string _bufferValuePropertyName;
        private readonly StencilCalculatorModel _model;
        private readonly ByteSliderDrawer _initialValueDrawer = new ByteSliderDrawer();

        private float _rowHeight;
        private MaterialEditor _editor;

        public StencilCalculatorView(string bufferValuePropertyName, StencilCalculatorModel model)
        {
            _bufferValuePropertyName = bufferValuePropertyName;
            _model = model;
        }

        public void SetEditor(MaterialEditor editor)
        {
            _editor = editor;
        }

        public void Draw(Rect position)
        {
            _rowHeight = EditorGUIUtility.singleLineHeight;
            CalculatorLayout layout = BuildCalculatorLayout(_rowHeight);
            float currentY = position.y;
            float startX = position.x;
            BitRowLayout columnLayout = new BitRowLayout
            {
                StartX = startX,
                YPos = layout.GetElementY(CalculatorLayoutElementType.ColumnHeader, position.y),
                RowHeight = _rowHeight,
                AvailableWidth = position.width,
                LabelWidth = EditorGUIUtility.labelWidth
            };
            ReadMaskValues updatedRead = default(ReadMaskValues);

            foreach (CalculatorLayoutElement element in layout.Elements)
            {
                switch (element.Type)
                {
                    case CalculatorLayoutElementType.InitialValue:
                    {
                        Rect rect = new Rect(startX, currentY, position.width, element.Height);
                        GUIContent existingBufferLabel = new GUIContent(
                            EditorLocale.editor.Get("stencil_row_existing_buffer"),
                            EditorLocale.editor.Get("stencil_prop_existing_buffer_tooltip"));
                        _initialValueDrawer.OnGUI(rect, layout.InitialValueProperty, existingBufferLabel, _editor);
                        break;
                    }
                    case CalculatorLayoutElementType.Gap:
                        break;
                    case CalculatorLayoutElementType.ColumnHeader:
                        DrawColumnHeaderRect(columnLayout);
                        break;
                    case CalculatorLayoutElementType.ThickDivider:
                        DrawHorizontalLineThick(currentY, startX, position.width);
                        break;
                    case CalculatorLayoutElementType.ReadMaskRows:
                    {
                        int stencilRef = _model.GetStencilRef();
                        int stencilReadMask = _model.GetStencilReadMask();
                        int bufferValue = _model.BufferValue;
                        ReadMaskValues readValues = new ReadMaskValues
                        {
                            StencilRef = stencilRef,
                            StencilReadMask = stencilReadMask,
                            BufferValue = bufferValue,
                            StencilRefIsMixed = _model.StencilRefIsMixed,
                            StencilReadMaskIsMixed = _model.StencilReadMaskIsMixed,
                            BufferValueIsMixed = _model.BufferValueIsMixed
                        };
                        updatedRead = StencilBitVisualizerHelper.DrawReadMaskBitRows(readValues, currentY, startX, position.width, Styles.stencilRowLabel, _rowHeight);
                        if (updatedRead.BufferValueChanged) _model.SetBufferValue(updatedRead.BufferValue);
                        break;
                    }
                    case CalculatorLayoutElementType.ThinDivider:
                        DrawHorizontalLine(currentY, startX, position.width);
                        break;
                    case CalculatorLayoutElementType.WriteMaskRows:
                    {
                        WriteMaskValues writeValues = new WriteMaskValues
                        {
                            StencilRef = updatedRead.StencilRef,
                            StencilWriteMask = _model.GetStencilWriteMask(),
                            StencilRefIsMixed = _model.StencilRefIsMixed,
                            StencilWriteMaskIsMixed = _model.StencilWriteMaskIsMixed
                        };
                        var compareFunction = _model.GetStencilCompareFunction();
                        var passOp = _model.GetStencilPassOp();
                        var failOp = _model.GetStencilFailOp();
                        var zFailOp = _model.GetStencilZFailOp();
                        bool isOccluded = _model.IsOccluded;
                        WriteMaskValues updatedWrite = StencilBitVisualizerHelper.DrawWriteMaskBitRows(writeValues, _model.BufferValue, updatedRead.StencilReadMask, compareFunction, passOp, failOp, zFailOp, isOccluded, currentY, startX, position.width, Styles.stencilRowLabel, _rowHeight);

                        if (updatedRead.StencilRefChanged || updatedWrite.StencilRefChanged) _model.SetStencilRef(updatedWrite.StencilRef);
                        if (updatedRead.StencilReadMaskChanged) _model.SetStencilReadMask(updatedRead.StencilReadMask);
                        if (updatedWrite.StencilWriteMaskChanged) _model.SetStencilWriteMask(updatedWrite.StencilWriteMask);
                        _model.UpdateCheckResult();
                        break;
                    }
                }

                currentY += element.Height;
            }

            float headerStartY = layout.GetElementY(CalculatorLayoutElementType.ColumnHeader, position.y);
            float binaryStartY = layout.GetElementY(CalculatorLayoutElementType.ReadMaskRows, position.y);
            float binaryEndY = layout.GetElementY(CalculatorLayoutElementType.WriteMaskRows, position.y)
                + StencilBitVisualizerHelper.GetWriteMaskBitRowsHeight(_rowHeight);
            DrawVerticalDividers(columnLayout, headerStartY, binaryStartY, binaryEndY);
        }

        private void DrawHorizontalLine(float yPos, float startX, float width)
        {
            EditorGUI.DrawRect(new Rect(startX, yPos, width, HorizontalLineThickness), Colors.stencilDivider);
        }

        private void DrawHorizontalLineThick(float yPos, float startX, float width)
        {
            EditorGUI.DrawRect(new Rect(startX, yPos, width, HorizontalLineThickThickness), Colors.stencilDivider);
        }

        private CalculatorLayout BuildCalculatorLayout(float rowHeight)
        {
            MaterialProperty initialProp = StencilPropertyHelper.GetProperty(_bufferValuePropertyName);
            float rowSpacing = StencilBitVisualizerHelper.RowSpacing;
            var elements = new List<CalculatorLayoutElement>();

            if (initialProp != null)
            {
                // GetHeight(), not GetPropertyHeight() — the latter calls ShaderProperty.RegisterDrawer
                // and would claim the calculator's host property for the nested byte slider.
                elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.InitialValue, _initialValueDrawer.GetHeight(initialProp)));
            }

            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.Gap, rowSpacing));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.ColumnHeader, rowHeight));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.ThickDivider, HorizontalLineThickThickness));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.Gap, rowSpacing));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.ReadMaskRows, StencilBitVisualizerHelper.GetReadMaskBitRowsHeight(rowHeight)));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.Gap, rowSpacing));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.ThinDivider, HorizontalLineThickness));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.Gap, rowSpacing));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.WriteMaskRows, StencilBitVisualizerHelper.GetWriteMaskBitRowsHeight(rowHeight)));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.Gap, rowSpacing));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.ThinDivider, HorizontalLineThickness));
            elements.Add(new CalculatorLayoutElement(CalculatorLayoutElementType.Gap, rowSpacing));

            return new CalculatorLayout { InitialValueProperty = initialProp, Elements = elements };
        }

        private void DrawColumnHeaderRect(BitRowLayout layout)
        {
            GUIStyle headerStyle = Styles.stencilRowLabel;

            GUI.Label(layout.LabelRect, EditorLocale.editor.Get("stencil_col_label"), headerStyle);

            for (int i = StencilOperationsHelper.BitsPerByte - 1; i >= 0; i--)
                GUI.Label(layout.GetBitRect(i), (1 << i).ToString(), Styles.stencilBitWeight);

            GUI.Label(layout.DecimalRect, EditorLocale.editor.Get("stencil_col_decimal"), headerStyle);
        }

        public float GetCalculatorHeight()
        {
            CalculatorLayout layout = BuildCalculatorLayout(EditorGUIUtility.singleLineHeight);
            float height = 0;
            foreach (CalculatorLayoutElement element in layout.Elements)
            {
                height += element.Height;
            }
            return height;
        }

        private void DrawVerticalDividers(BitRowLayout layout, float headerStartY, float binaryStartY, float binaryEndY)
        {
            for (int i = 1; i < StencilOperationsHelper.BitsPerByte; i++)
            {
                float dividerX = layout.GetBitDividerX(i) - VerticalDividerOffset;
                EditorGUI.DrawRect(new Rect(dividerX, binaryStartY, VerticalDividerThickness, binaryEndY - binaryStartY), new Color(Colors.stencilDivider.r, Colors.stencilDivider.g, Colors.stencilDivider.b, VerticalDividerAlpha));
            }

            float leftOfBinaryX = layout.LabelRect.xMax - VerticalDividerLeftOffset;
            float leftOfDecimalX = layout.DecimalRect.xMin - VerticalDividerDecimalOffset;
            EditorGUI.DrawRect(new Rect(leftOfBinaryX, headerStartY, VerticalDividerLeftOffset, binaryEndY - headerStartY), Colors.stencilDivider);
            EditorGUI.DrawRect(new Rect(leftOfDecimalX, headerStartY, VerticalDividerLeftOffset, binaryEndY - headerStartY), Colors.stencilDivider);
        }
    }
}
