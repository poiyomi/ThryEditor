using Thry.ThryEditor.DataStructs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    public static class StencilOperationsHelper
    {
        public const int ByteMin = 0;
        public const int ByteMax = 255;
        public const int BitsPerByte = 8;

        // Unity stencil comparisons evaluate referenceValue COMP bufferValue, with the reference on the left.
        public static bool EvaluateStencilCompare(CompareFunction compareFunction, int referenceValue, int bufferValue)
        {
            switch (compareFunction)
            {
                case CompareFunction.Disabled:
                    return true;
                case CompareFunction.Never:
                    return false;
                case CompareFunction.Less:
                    return referenceValue < bufferValue;
                case CompareFunction.Equal:
                    return referenceValue == bufferValue;
                case CompareFunction.LessEqual:
                    return referenceValue <= bufferValue;
                case CompareFunction.Greater:
                    return referenceValue > bufferValue;
                case CompareFunction.NotEqual:
                    return referenceValue != bufferValue;
                case CompareFunction.GreaterEqual:
                    return referenceValue >= bufferValue;
                case CompareFunction.Always:
                    return true;
                default:
                    return false;
            }
        }

        public static int ApplyStencilOp(StencilOp op, int currentValue, int referenceValue)
        {
            switch (op)
            {
                case StencilOp.Keep:
                    return currentValue;
                case StencilOp.Zero:
                    return ByteMin;
                case StencilOp.Replace:
                    return referenceValue;
                case StencilOp.IncrementSaturate:
                    return Mathf.Min(currentValue + 1, ByteMax);
                case StencilOp.DecrementSaturate:
                    return Mathf.Max(currentValue - 1, ByteMin);
                case StencilOp.Invert:
                    return (~currentValue) & ByteMax;
                case StencilOp.IncrementWrap:
                    return (currentValue + 1) & ByteMax;
                case StencilOp.DecrementWrap:
                    return currentValue == ByteMin ? ByteMax : currentValue - 1;
                default:
                    return currentValue;
            }
        }

        public static int ComputeFinalStencilOutput(int initialValue, int stencilRef, int stencilReadMask, int stencilWriteMask, CompareFunction compareFunction, StencilOp passOp, StencilOp failOp, StencilOp zFailOp, bool isOccluded, out bool checkPassed)
        {
            int stencilBufferMasked = initialValue & stencilReadMask;
            int stencilReferenceMasked = stencilRef & stencilReadMask;
            checkPassed = EvaluateStencilCompare(compareFunction, stencilReferenceMasked, stencilBufferMasked);
            StencilOp appliedOp = !checkPassed ? failOp : isOccluded ? zFailOp : passOp;
            int opResult = ApplyStencilOp(appliedOp, initialValue, stencilRef);
            int finalOutput = (opResult & stencilWriteMask) | (initialValue & ((~stencilWriteMask) & ByteMax));
            return finalOutput;
        }

        internal static int GetMaterialValue(Material material, string propertyName, int fallback = ByteMin)
            => material.HasProperty(propertyName) ? Mathf.Clamp((int)material.GetFloat(propertyName), ByteMin, ByteMax) : fallback;

        internal static int ComputeFinalStencilOutput(Material material, StencilConfig c, out bool checkPassed)
            => ComputeFinalStencilOutput(
                GetMaterialValue(material, c.StencilBufferValuePropertyName), GetMaterialValue(material, c.StencilRefPropertyName),
                GetMaterialValue(material, c.StencilReadMaskPropertyName, ByteMax), GetMaterialValue(material, c.StencilWriteMaskPropertyName, ByteMax),
                (CompareFunction)GetMaterialValue(material, c.StencilCompareFunctionPropertyName, (int)CompareFunction.Always),
                (StencilOp)GetMaterialValue(material, c.StencilPassOpPropertyName), (StencilOp)GetMaterialValue(material, c.StencilFailOpPropertyName),
                (StencilOp)GetMaterialValue(material, c.StencilZFailOpPropertyName), GetMaterialValue(material, c.StencilIsOccludedPropertyName) != 0, out checkPassed);
    }
}
