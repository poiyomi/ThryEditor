using System;
using UnityEngine;

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
}
