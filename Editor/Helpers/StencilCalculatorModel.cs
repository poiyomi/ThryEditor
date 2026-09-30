using Thry.ThryEditor.DataStructs;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    public class StencilCalculatorModel
    {
        private class PropertyTracker
        {
            private readonly string _propertyName;
            private readonly int _defaultValue;

            public PropertyTracker(string propertyName, int defaultValue)
            {
                _propertyName = propertyName;
                _defaultValue = defaultValue;
            }

            public int Value => StencilPropertyHelper.GetValueFromProperty(_propertyName, _defaultValue);
            public bool IsMixed => StencilPropertyHelper.HasMixedValue(_propertyName);

            public void SaveIfDifferent(int newValue)
            {
                if (newValue == Value) return;
                Save(newValue);
            }

            public void Save(int newValue)
            {
                StencilPropertyHelper.SaveValueToProperty(_propertyName, newValue);
            }
        }

        private readonly PropertyTracker _bufferValue;
        private readonly PropertyTracker _stencilRef;
        private readonly PropertyTracker _readMask;
        private readonly PropertyTracker _writeMask;
        private readonly PropertyTracker _compareFunction;
        private readonly PropertyTracker _passOp;
        private readonly PropertyTracker _failOp;
        private readonly PropertyTracker _zFailOp;
        private readonly PropertyTracker _checkResult;
        private readonly PropertyTracker _isOccluded;

        public StencilCalculatorModel(StencilConfig config)
        {
            _bufferValue = new PropertyTracker(config.StencilBufferValuePropertyName, 0);
            _stencilRef = new PropertyTracker(config.StencilRefPropertyName, 0);
            _readMask = new PropertyTracker(config.StencilReadMaskPropertyName, StencilOperationsHelper.ByteMax);
            _writeMask = new PropertyTracker(config.StencilWriteMaskPropertyName, StencilOperationsHelper.ByteMax);
            _compareFunction = new PropertyTracker(config.StencilCompareFunctionPropertyName, (int)CompareFunction.Always);
            _passOp = new PropertyTracker(config.StencilPassOpPropertyName, (int)StencilOp.Keep);
            _failOp = new PropertyTracker(config.StencilFailOpPropertyName, (int)StencilOp.Keep);
            _zFailOp = new PropertyTracker(config.StencilZFailOpPropertyName, (int)StencilOp.Keep);
            _checkResult = new PropertyTracker(config.StencilCheckResultPropertyName, 0);
            _isOccluded = new PropertyTracker(config.StencilIsOccludedPropertyName, 0);
        }

        public int GetStencilRef() => _stencilRef.Value;
        public int GetStencilReadMask() => _readMask.Value;
        public int GetStencilWriteMask() => _writeMask.Value;
        public CompareFunction GetStencilCompareFunction() => (CompareFunction)_compareFunction.Value;
        public StencilOp GetStencilPassOp() => (StencilOp)_passOp.Value;
        public StencilOp GetStencilFailOp() => (StencilOp)_failOp.Value;
        public StencilOp GetStencilZFailOp() => (StencilOp)_zFailOp.Value;

        public bool BufferValueIsMixed => _bufferValue.IsMixed;
        public bool StencilRefIsMixed => _stencilRef.IsMixed;
        public bool StencilReadMaskIsMixed => _readMask.IsMixed;
        public bool StencilWriteMaskIsMixed => _writeMask.IsMixed;
        public bool CompareFunctionIsMixed => _compareFunction.IsMixed;
        public bool PassOpIsMixed => _passOp.IsMixed;
        public bool FailOpIsMixed => _failOp.IsMixed;
        public bool ZFailOpIsMixed => _zFailOp.IsMixed;

        public void SetBufferValue(int value) => _bufferValue.Save(value);
        public void SetStencilRef(int value) => _stencilRef.Save(value);
        public void SetStencilReadMask(int value) => _readMask.Save(value);
        public void SetStencilWriteMask(int value) => _writeMask.Save(value);
        public void SetStencilCompareFunction(CompareFunction value) => _compareFunction.Save((int)value);
        public void SetStencilPassOp(StencilOp value) => _passOp.Save((int)value);
        public void SetStencilFailOp(StencilOp value) => _failOp.Save((int)value);
        public void SetStencilZFailOp(StencilOp value) => _zFailOp.Save((int)value);

        // Recomputes the stencil test from the current property values. Pure — writes nothing.
        public bool ComputeCheckResult()
        {
            bool checkPassed;
            StencilOperationsHelper.ComputeFinalStencilOutput(
                BufferValue,
                GetStencilRef(),
                GetStencilReadMask(),
                GetStencilWriteMask(),
                GetStencilCompareFunction(),
                GetStencilPassOp(),
                GetStencilFailOp(),
                GetStencilZFailOp(),
                IsOccluded,
                out checkPassed);
            return checkPassed;
        }

        // Both stencil decorators call this; SaveIfDifferent makes repeated writes idempotent.
        public void UpdateCheckResult()
        {
            _checkResult.SaveIfDifferent(ComputeCheckResult() ? 1 : 0);
        }

        // Written by the host toggle's own drawer, so this side only reads it.
        public bool IsOccluded => _isOccluded.Value == 1;

        public int BufferValue => _bufferValue.Value;
    }
}
