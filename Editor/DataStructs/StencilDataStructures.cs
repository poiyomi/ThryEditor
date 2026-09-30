using System;
using UnityEngine;
using Thry.ThryEditor.Helpers;

namespace Thry.ThryEditor.DataStructs
{
    public struct StencilConfig
    {
        private const string StencilPropertyPrefix = "_Stencil";

        public string StencilBufferValuePropertyName;
        public string StencilRefPropertyName;
        public string StencilReadMaskPropertyName;
        public string StencilWriteMaskPropertyName;
        public string StencilCompareFunctionPropertyName;
        public string StencilPassOpPropertyName;
        public string StencilFailOpPropertyName;
        public string StencilZFailOpPropertyName;
        public string StencilCheckResultPropertyName;
        public string StencilIsOccludedPropertyName;

        public const string DefaultStencilBufferValuePropertyName = StencilPropertyPrefix + "BufferValue";
        public const string DefaultStencilRefPropertyName = StencilPropertyPrefix + "Ref";
        public const string DefaultStencilReadMaskPropertyName = StencilPropertyPrefix + "ReadMask";
        public const string DefaultStencilWriteMaskPropertyName = StencilPropertyPrefix + "WriteMask";
        public const string DefaultStencilCompareFunctionPropertyName = StencilPropertyPrefix + "CompareFunction";
        public const string DefaultStencilPassOpPropertyName = StencilPropertyPrefix + "PassOp";
        public const string DefaultStencilFailOpPropertyName = StencilPropertyPrefix + "FailOp";
        public const string DefaultStencilZFailOpPropertyName = StencilPropertyPrefix + "ZFailOp";
        public const string DefaultStencilCheckResultPropertyName = StencilPropertyPrefix + "CheckResult";
        public const string DefaultStencilIsOccludedPropertyName = StencilPropertyPrefix + "IsOccluded";

        public static StencilConfig WithDefaults(
            string bufferValueProp = null,
            string stencilRefProp = null,
            string readMaskProp = null,
            string writeMaskProp = null,
            string compareFunctionProp = null,
            string passOpProp = null,
            string failOpProp = null,
            string zFailOpProp = null,
            string checkResultProp = null,
            string isOccludedProp = null)
        {
            return new StencilConfig
            {
                StencilBufferValuePropertyName     = bufferValueProp ?? DefaultStencilBufferValuePropertyName,
                StencilRefPropertyName             = stencilRefProp ?? DefaultStencilRefPropertyName,
                StencilReadMaskPropertyName        = readMaskProp ?? DefaultStencilReadMaskPropertyName,
                StencilWriteMaskPropertyName       = writeMaskProp ?? DefaultStencilWriteMaskPropertyName,
                StencilCompareFunctionPropertyName = compareFunctionProp ?? DefaultStencilCompareFunctionPropertyName,
                StencilPassOpPropertyName          = passOpProp ?? DefaultStencilPassOpPropertyName,
                StencilFailOpPropertyName          = failOpProp ?? DefaultStencilFailOpPropertyName,
                StencilZFailOpPropertyName         = zFailOpProp ?? DefaultStencilZFailOpPropertyName,
                StencilCheckResultPropertyName     = checkResultProp ?? DefaultStencilCheckResultPropertyName,
                StencilIsOccludedPropertyName      = isOccludedProp ?? DefaultStencilIsOccludedPropertyName,
            };
        }

        // Front or Back, matched case-insensitively. Only the compare function, the three ops
        // and the check result take the face-specific names; the rest stay shared.
        public static StencilConfig WithVariant(string variant, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(variant))
            {
                return WithDefaults();
            }

            string canonicalVariant;
            if (string.Equals(variant, "Front", StringComparison.OrdinalIgnoreCase))
            {
                canonicalVariant = "Front";
            }
            else if (string.Equals(variant, "Back", StringComparison.OrdinalIgnoreCase))
            {
                canonicalVariant = "Back";
            }
            else
            {
                Debug.LogWarning($"[{attributeName}] Unrecognised stencil variant \"{variant}\". Expected \"Front\" or \"Back\"; using default property names.");
                return WithDefaults();
            }

            return WithDefaults(
                compareFunctionProp: InsertVariant(DefaultStencilCompareFunctionPropertyName, canonicalVariant),
                passOpProp: InsertVariant(DefaultStencilPassOpPropertyName, canonicalVariant),
                failOpProp: InsertVariant(DefaultStencilFailOpPropertyName, canonicalVariant),
                zFailOpProp: InsertVariant(DefaultStencilZFailOpPropertyName, canonicalVariant),
                checkResultProp: InsertVariant(DefaultStencilCheckResultPropertyName, canonicalVariant));
        }

        private static string InsertVariant(string defaultPropertyName, string variant)
        {
            return defaultPropertyName.Insert(StencilPropertyPrefix.Length, variant);
        }
    }

    [Flags]
    public enum BitRowOptions
    {
        Editable = 1 << 0,
        ShowDecimal = 1 << 1,
        DecimalEditable = 1 << 2
    }

    public struct ReadMaskValues
    {
        public int StencilRef;
        public int StencilReadMask;
        public int BufferValue;
        public bool StencilRefIsMixed;
        public bool StencilReadMaskIsMixed;
        public bool BufferValueIsMixed;
        public bool StencilRefChanged;
        public bool StencilReadMaskChanged;
        public bool BufferValueChanged;
    }

    public struct WriteMaskValues
    {
        public int StencilRef;
        public int StencilWriteMask;
        public bool StencilRefIsMixed;
        public bool StencilWriteMaskIsMixed;
        public bool StencilRefChanged;
        public bool StencilWriteMaskChanged;
    }

    public struct BitRow
    {
        public string Label;
        public Color LitColor;
        public BitRowOptions Options;
        public int MaskBits;
        public Func<bool, string> LabelProvider;
        public bool HideLedBackground;
        public string Tooltip;
        public bool HasMixedValue;
    }

    public struct BitRowLayout
    {
        private const int BitCount = StencilOperationsHelper.BitsPerByte;

        public const float BitWidth = 20f;
        public const float ColumnGap = 8f;
        public const float BinaryColumnLeftPadding = 2f;
        public const float BinaryColumnWidth = BitCount * BitWidth;

        public float StartX;
        public float YPos;
        public float RowHeight;
        public float AvailableWidth;
        public float LabelWidth;
        // Kept explicit because dividers and spacers make Y-position-derived parity unreliable.
        public int RowIndex;

        public float BinaryStartX => StartX + LabelWidth + BinaryColumnLeftPadding;
        public float DecimalStartX => StartX + LabelWidth + BinaryColumnLeftPadding + BinaryColumnWidth + ColumnGap;
        public float FixedWidth => LabelWidth + BinaryColumnLeftPadding + BinaryColumnWidth + ColumnGap;
        public float DecimalWidth => Mathf.Max(AvailableWidth - FixedWidth, 0);
        public float TotalWidth => FixedWidth + DecimalWidth;
        public Rect RowBgRect => new Rect(StartX, YPos, TotalWidth, RowHeight);
        public Rect LabelRect => new Rect(StartX, YPos, LabelWidth, RowHeight);
        public Rect GetBitRect(int bitIndex) => new Rect(BinaryStartX + (BitCount - 1 - bitIndex) * BitWidth, YPos, BitWidth, RowHeight);
        // Deriving this from the bit rectangle prevents cell and divider positions from drifting apart.
        public float GetBitDividerX(int dividerIndex) => GetBitRect(BitCount - 1 - dividerIndex).xMin;
        public Rect DecimalRect => new Rect(DecimalStartX, YPos, DecimalWidth, RowHeight);
    }
}
