using System;
using UnityEditor;
using UnityEngine;
using Thry.ThryEditor.DataStructs;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    public static class StencilBitVisualizerHelper
    {
        private enum BitRowSectionElementType
        {
            BufferValueRow,
            ReferenceValueRow,
            ReadMaskRow,
            ReadMaskOutputRow,
            WriteMaskRow,
            FinalOutputRow,
            SmallGap,
            Gap
        }

        private static readonly BitRowSectionElementType[] ReadMaskLayout =
        {
            BitRowSectionElementType.BufferValueRow,
            BitRowSectionElementType.SmallGap,
            BitRowSectionElementType.ReferenceValueRow,
            BitRowSectionElementType.SmallGap,
            BitRowSectionElementType.ReadMaskRow,
            BitRowSectionElementType.Gap,
            BitRowSectionElementType.ReadMaskOutputRow
        };

        private static readonly BitRowSectionElementType[] WriteMaskLayout =
        {
            BitRowSectionElementType.ReferenceValueRow,
            BitRowSectionElementType.SmallGap,
            BitRowSectionElementType.WriteMaskRow,
            BitRowSectionElementType.Gap,
            BitRowSectionElementType.FinalOutputRow
        };

        private static readonly Func<bool, string> MaskSymbol = bit => bit ? "\u2193" : "\u2715";

        public const float RowSpacing = 4f;
        private const float RowSpacingSmall = 2f;

        private const float LedCornerRadius = 3f;
        // How far a read-only row is dimmed relative to an editable one.
        private const float ReadoutRowAlpha = 0.6f;
        // How far the ghost symbol behind a blocked mask cell is darkened and faded.
        private const float GhostSymbolDarken = 0.6f;
        private const float GhostSymbolAlpha = 0.5f;
        // How much of the lit colour bleeds through a cell that is set but masked off.
        private const float MaskedBitBleedAlpha = 0.22f;

        private static Color GhostSymbolColor(Color litColor)
        {
            return new Color(litColor.r * GhostSymbolDarken, litColor.g * GhostSymbolDarken,
                litColor.b * GhostSymbolDarken, GhostSymbolAlpha);
        }

        private static Color WithAlpha(Color c, float multiplier)
        {
            return new Color(c.r, c.g, c.b, c.a * multiplier);
        }

        // Rounded corners come from GUI.DrawTexture's borderRadius, the same way Gradient and
        // ThryDecalPositioning draw their rounded panels.
        private static void DrawLedRect(Rect rect, Color color)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, LedCornerRadius);
        }

        private static float DrawBitRow(ref int value, BitRow row, BitRowLayout layout, GUIStyle labelStyle)
        {
            if (layout.RowIndex % 2 != 0)
                EditorGUI.DrawRect(layout.RowBgRect, Colors.stencilRowStripe);

            // Read-only rows are readouts, not controls, so the whole row is dimmed.
            bool editable = (row.Options & BitRowOptions.Editable) != 0;
            Color previousGuiColor = GUI.color;
            if (!editable) GUI.color = previousGuiColor * new Color(1f, 1f, 1f, ReadoutRowAlpha);

            DrawBitRowLabel(layout.LabelRect, row.Label, labelStyle, row.Tooltip);
            DrawBits(layout, ref value, row.LitColor, editable, row.MaskBits, row.LabelProvider, row.HideLedBackground);

            if ((row.Options & BitRowOptions.ShowDecimal) != 0)
                DrawDecimalValue(layout, ref value, row.Options, row.HasMixedValue);

            GUI.color = previousGuiColor;
            return layout.YPos + layout.RowHeight;
        }

        private static void DrawBitRowLabel(Rect rect, string label, GUIStyle labelStyle, string tooltip = null)
        {
            GUI.Label(rect, new GUIContent(label, tooltip), labelStyle);
        }

        private static void DrawBits(
            BitRowLayout layout,
            ref int value,
            Color litColor,
            bool editable,
            int maskBits = StencilOperationsHelper.ByteMax,
            Func<bool, string> labelProvider = null,
            bool hideLedBackground = false)
        {
            GUIStyle labelStyle = hideLedBackground ? Styles.stencilBitSymbol : Styles.stencilBitDigit;
            // GUI.color does not reach GUI.DrawTexture when a colour is passed explicitly,
            // so the read-only dimming the caller set has to be applied to the fills here.
            float ledAlpha = editable ? 1f : ReadoutRowAlpha;

            for (int i = StencilOperationsHelper.BitsPerByte - 1; i >= 0; i--)
            {
                bool bit      = ((value >> i) & 1) == 1;
                bool isMasked = ((maskBits >> i) & 1) == 0;
                Rect bitRect  = layout.GetBitRect(i);

                bool isHovered = editable && bitRect.Contains(Event.current.mousePosition);

                const float pad = 1f;
                Rect inner = new Rect(
                    bitRect.x + pad,
                    bitRect.y + pad,
                    bitRect.width - pad * 2f,
                    bitRect.height - pad * 2f);

                if (!hideLedBackground)
                {
                    // Four states: set, unset, masked-and-set, masked-and-unset.
                    if (!isMasked && bit)
                        DrawLedRect(inner, WithAlpha(litColor, ledAlpha));
                    else if (!isMasked)
                        DrawLedRect(inner, WithAlpha(Colors.stencilLedOff, ledAlpha));
                    else
                        DrawLedRect(inner, WithAlpha(Colors.stencilLedMasked, ledAlpha));

                    if (isMasked && bit)
                        DrawLedRect(inner, WithAlpha(new Color(litColor.r, litColor.g, litColor.b, MaskedBitBleedAlpha), ledAlpha));

                    if (isHovered)
                        DrawLedRect(inner, Colors.stencilLedHover);
                }

                Color textColor;
                if (hideLedBackground)
                {
                    textColor = bit ? litColor : Colors.stencilBlockedSymbol;
                    // Symbol-only cells have no LED fill to brighten, so hover comes from the glyph.
                    if (isHovered) textColor = Color.Lerp(textColor, Colors.stencilSymbolHover, 0.5f);
                }
                else if (isMasked)
                {
                    textColor = Colors.stencilBitTextMasked;
                }
                else
                {
                    textColor = new Color(0f, 0f, 0f, bit ? 0.6f : 0.65f);
                }

                // In symbol-only mode a blocked bit draws the "passes through" symbol behind the
                // block marker, dimmed.
                if (hideLedBackground && !bit && labelProvider != null)
                {
                    labelStyle.normal.textColor = labelStyle.hover.textColor = GhostSymbolColor(litColor);
                    GUI.Label(bitRect, labelProvider(true), labelStyle);
                }

                labelStyle.normal.textColor = textColor;
                labelStyle.hover.textColor  = textColor; // prevent implicit hover highlight on non-editable cells
                string bitLabel = labelProvider != null ? labelProvider(bit) : (bit ? "1" : "0");
                GUI.Label(bitRect, bitLabel, labelStyle);

                if (editable)
                {
                    // Cursor as well as tint, so the cells advertise themselves the way every other
                    // clickable thing in the editor does. Same pairing as GUILib.ButtonWithCursor.
                    EditorGUIUtility.AddCursorRect(bitRect, MouseCursor.Link);
                    if (GUI.Button(bitRect, GUIContent.none, GUIStyle.none))
                        value ^= 1 << i;
                }
            }
        }

        private static void DrawDecimalValue(
            BitRowLayout layout,
            ref int value,
            BitRowOptions options,
            bool hasMixedValue)
        {
            Rect decimalRect = layout.DecimalRect;
            EditorGUI.showMixedValue = hasMixedValue;

            if ((options & BitRowOptions.DecimalEditable) != 0)
            {
                value = EditorGUI.IntField(decimalRect, value);
                value = Mathf.Clamp(value, StencilOperationsHelper.ByteMin, StencilOperationsHelper.ByteMax);
            }
            else
            {
                // A disabled field, not a label, so a derived value reads like the ones above it.
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.IntField(decimalRect, value);
            }

            EditorGUI.showMixedValue = false;
        }

        public static float GetReadMaskBitRowsHeight(float rowHeight)
        {
            return GetBitRowSectionHeight(ReadMaskLayout, rowHeight);
        }

        public static float GetWriteMaskBitRowsHeight(float rowHeight)
        {
            return GetBitRowSectionHeight(WriteMaskLayout, rowHeight);
        }

        private static float GetBitRowSectionHeight(BitRowSectionElementType[] elements, float rowHeight)
        {
            float height = 0;
            foreach (BitRowSectionElementType element in elements)
            {
                switch (element)
                {
                    case BitRowSectionElementType.SmallGap:
                        height += RowSpacingSmall;
                        break;
                    case BitRowSectionElementType.Gap:
                        height += RowSpacing;
                        break;
                    default:
                        height += rowHeight;
                        break;
                }
            }
            return height;
        }

        public static ReadMaskValues DrawReadMaskBitRows(
            ReadMaskValues values,
            float currentY, float startX, float availableWidth,
            GUIStyle rowLabelStyle, float rowHeight)
        {
            var updated = values;
            BitRowLayout layout = new BitRowLayout
            {
                StartX = startX,
                YPos = currentY,
                RowHeight = rowHeight,
                AvailableWidth = availableWidth,
                LabelWidth = EditorGUIUtility.labelWidth
            };
            foreach (BitRowSectionElementType element in ReadMaskLayout)
            {
                switch (element)
                {
                    case BitRowSectionElementType.BufferValueRow:
                    {
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_existing_buffer"),
                            LitColor = Colors.stencilBufferValue,
                            MaskBits = StencilOperationsHelper.ByteMax,
                            Options = BitRowOptions.Editable
                                | BitRowOptions.ShowDecimal
                                | BitRowOptions.DecimalEditable,
                            Tooltip = EditorLocale.editor.Get("stencil_row_existing_buffer_tooltip"),
                            HasMixedValue = updated.BufferValueIsMixed
                        };
                        currentY = DrawBitRow(ref updated.BufferValue, row, layout, rowLabelStyle);
                        layout.RowIndex++;
                        break;
                    }
                    case BitRowSectionElementType.ReferenceValueRow:
                    {
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_reference"),
                            LitColor = Colors.stencilReference,
                            MaskBits = updated.StencilReadMask,
                            Options = BitRowOptions.Editable
                                | BitRowOptions.ShowDecimal
                                | BitRowOptions.DecimalEditable,
                            Tooltip = EditorLocale.editor.Get("stencil_row_reference_tooltip"),
                            HasMixedValue = updated.StencilRefIsMixed
                        };
                        currentY = DrawBitRow(ref updated.StencilRef, row, layout, rowLabelStyle);
                        layout.RowIndex++;
                        break;
                    }
                    case BitRowSectionElementType.ReadMaskRow:
                    {
                        // The Reference row used the pre-edit mask this event; the repaint uses this updated value.
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_readmask"),
                            LitColor = Colors.stencilMask,
                            MaskBits = StencilOperationsHelper.ByteMax,
                            Options = BitRowOptions.Editable
                                | BitRowOptions.ShowDecimal
                                | BitRowOptions.DecimalEditable,
                            LabelProvider = MaskSymbol,
                            HideLedBackground = true,
                            Tooltip = EditorLocale.editor.Get("stencil_row_readmask_tooltip"),
                            HasMixedValue = updated.StencilReadMaskIsMixed
                        };
                        currentY = DrawBitRow(ref updated.StencilReadMask, row, layout, rowLabelStyle);
                        layout.RowIndex++;
                        break;
                    }
                    case BitRowSectionElementType.ReadMaskOutputRow:
                    {
                        int readMaskOutput = updated.BufferValue & updated.StencilReadMask;
                        // Computed row: the value is a local, so an editable decimal field would silently discard edits.
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_read_buffer"),
                            Tooltip = EditorLocale.editor.Get("stencil_row_read_buffer_tooltip"),
                            LitColor = Colors.stencilOutput,
                            MaskBits = StencilOperationsHelper.ByteMax,
                            Options = BitRowOptions.ShowDecimal
                        };
                        currentY = DrawBitRow(ref readMaskOutput, row, layout, rowLabelStyle);
                        break;
                    }
                    case BitRowSectionElementType.SmallGap:
                        currentY += RowSpacingSmall;
                        break;
                    case BitRowSectionElementType.Gap:
                        currentY += RowSpacing;
                        break;
                }

                layout.YPos = currentY;
            }
            return updated;
        }

        public static WriteMaskValues DrawWriteMaskBitRows(
            WriteMaskValues values, int initialValue, int stencilReadMask,
            CompareFunction compareFunction, StencilOp passOp, StencilOp failOp, StencilOp zFailOp, bool isOccluded,
            float currentY, float startX, float availableWidth,
            GUIStyle rowLabelStyle, float rowHeight)
        {
            var updated = values;
            BitRowLayout layout = new BitRowLayout
            {
                StartX = startX,
                YPos = currentY,
                RowHeight = rowHeight,
                AvailableWidth = availableWidth,
                LabelWidth = EditorGUIUtility.labelWidth
            };
            foreach (BitRowSectionElementType element in WriteMaskLayout)
            {
                switch (element)
                {
                    case BitRowSectionElementType.ReferenceValueRow:
                    {
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_reference"),
                            LitColor = Colors.stencilReference,
                            MaskBits = updated.StencilWriteMask,
                            Options = BitRowOptions.Editable
                                | BitRowOptions.ShowDecimal
                                | BitRowOptions.DecimalEditable,
                            Tooltip = EditorLocale.editor.Get("stencil_row_reference_tooltip"),
                            HasMixedValue = updated.StencilRefIsMixed
                        };
                        currentY = DrawBitRow(ref updated.StencilRef, row, layout, rowLabelStyle);
                        layout.RowIndex++;
                        break;
                    }
                    case BitRowSectionElementType.WriteMaskRow:
                    {
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_writemask"),
                            LitColor = Colors.stencilMask,
                            MaskBits = StencilOperationsHelper.ByteMax,
                            Options = BitRowOptions.Editable
                                | BitRowOptions.ShowDecimal
                                | BitRowOptions.DecimalEditable,
                            LabelProvider = MaskSymbol,
                            HideLedBackground = true,
                            Tooltip = EditorLocale.editor.Get("stencil_row_writemask_tooltip"),
                            HasMixedValue = updated.StencilWriteMaskIsMixed
                        };
                        currentY = DrawBitRow(ref updated.StencilWriteMask, row, layout, rowLabelStyle);
                        layout.RowIndex++;
                        break;
                    }
                    case BitRowSectionElementType.FinalOutputRow:
                    {
                        int finalOutput = StencilOperationsHelper.ComputeFinalStencilOutput(
                            initialValue,
                            updated.StencilRef,
                            stencilReadMask,
                            updated.StencilWriteMask,
                            compareFunction,
                            passOp,
                            failOp,
                            zFailOp,
                            isOccluded,
                            // The stored result is refreshed by the model after the rows are drawn.
                            out _);
                        BitRow row = new BitRow
                        {
                            Label = EditorLocale.editor.Get("stencil_row_new_buffer"),
                            Tooltip = EditorLocale.editor.Get("stencil_row_new_buffer_tooltip"),
                            LitColor = Colors.stencilOutput,
                            MaskBits = StencilOperationsHelper.ByteMax,
                            Options = BitRowOptions.ShowDecimal
                        };
                        currentY = DrawBitRow(ref finalOutput, row, layout, rowLabelStyle);
                        break;
                    }
                    case BitRowSectionElementType.SmallGap:
                        currentY += RowSpacingSmall;
                        break;
                    case BitRowSectionElementType.Gap:
                        currentY += RowSpacing;
                        break;
                }

                layout.YPos = currentY;
            }
            return updated;
        }
    }
}
