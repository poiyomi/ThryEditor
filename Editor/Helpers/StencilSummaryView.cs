using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    public class StencilSummaryView
    {
        private readonly InspectorPopup _comparePopup = new InspectorPopup();
        private readonly InspectorPopup _passPopup = new InspectorPopup();
        private readonly InspectorPopup _zFailPopup = new InspectorPopup();
        private readonly InspectorPopup _failPopup = new InspectorPopup();
        private const float BlockPadding = 6f;
        private const float OperationColumnGap = 4f;
        private const float ConnectorThickness = 2f;
        private const float BranchLabelHeight = 10f;
        private const float ConnectorStageHeight = 16f;
        private const float ConnectorBottomLength = 6f;

        private struct SummaryStyles
        {
            public GUIStyle Label;
            public GUIStyle Explanation;
            public GUIStyle Popup;
            public GUIStyle Branch;
        }

        private struct SummaryValues
        {
            public int ExistingBuffer;
            public int StencilRef;
            public int ReadMask;
            public int WriteMask;
            public CompareFunction CompareFunction;
            public StencilOp PassOp;
            public StencilOp ZFailOp;
            public StencilOp FailOp;
            public bool IsOccluded;
            public bool CompareFunctionIsMixed;
            public bool PassOpIsMixed;
            public bool ZFailOpIsMixed;
            public bool FailOpIsMixed;
        }

        private struct SummaryLayout
        {
            public float CompareBlockHeight;
            public float ConnectorHeight;
            public float CompareExplanationHeight;
            public float OperationColumnWidth;
            public float OperationColumnHeight;
            // The compare explanation is deliberately absent: it is rebuilt at draw time from the
            // post-popup compare function, so that picking a new one updates the text that frame.
            public string PassExplanation;
            public string ZFailExplanation;
            public string FailExplanation;

            public SummaryLayout(float compareBlockHeight, float connectorHeight,
                float compareExplanationHeight, float operationColumnWidth,
                float operationColumnHeight, string passExplanation,
                string zFailExplanation, string failExplanation)
            {
                CompareBlockHeight = compareBlockHeight;
                ConnectorHeight = connectorHeight;
                CompareExplanationHeight = compareExplanationHeight;
                OperationColumnWidth = operationColumnWidth;
                OperationColumnHeight = operationColumnHeight;
                PassExplanation = passExplanation;
                ZFailExplanation = zFailExplanation;
                FailExplanation = failExplanation;
            }
        }

        private readonly StencilCalculatorModel _model;

        private float _rowHeight;
        private Color _summaryStyleNeutralColor;
        private SummaryStyles _neutralStyles;
        private SummaryStyles _passStyles;
        private SummaryStyles _failStyles;
        private SummaryStyles _dimStyles;

        public StencilSummaryView(StencilCalculatorModel model)
        {
            _model = model;
        }

        public void Draw()
        {
            _rowHeight = EditorGUIUtility.singleLineHeight;
            SummaryValues values = new SummaryValues
            {
                ExistingBuffer = _model.BufferValue,
                StencilRef = _model.GetStencilRef(),
                ReadMask = _model.GetStencilReadMask(),
                WriteMask = _model.GetStencilWriteMask(),
                CompareFunction = _model.GetStencilCompareFunction(),
                PassOp = _model.GetStencilPassOp(),
                ZFailOp = _model.GetStencilZFailOp(),
                FailOp = _model.GetStencilFailOp(),
                IsOccluded = _model.IsOccluded,
                CompareFunctionIsMixed = _model.CompareFunctionIsMixed,
                PassOpIsMixed = _model.PassOpIsMixed,
                ZFailOpIsMixed = _model.ZFailOpIsMixed,
                FailOpIsMixed = _model.FailOpIsMixed
            };
            Color neutralColor = EditorStyles.label.normal.textColor;
            InitializeSummaryStyles(neutralColor, Colors.stencilPass, Colors.stencilFail, Colors.stencilDim);

            int xOffset = ShaderEditor.Active?.CurrentProperty?.XOffset ?? 0;
            float leftX = GUILib.GetPropertyX(xOffset);

            // The block's height depends on how the explanations wrap, which depends on its width,
            // so nothing is reserved in GetPropertyHeight and the space is claimed here. A
            // zero-height rect reports the width the section actually hands out; measuring against
            // that rather than a currentViewWidth estimate is what keeps the space reserved below
            // and the flow drawn into it the same size. Same probe as [LocalMessage].
            Rect probeRect = EditorGUILayout.GetControlRect(false, 0);
            float rightEdge = probeRect.x + probeRect.width - GUILib.SectionContentRightPadding - 1;
            float width = Mathf.Max(1f, rightEdge - leftX);

            SummaryLayout layout = BuildSummaryLayout(_rowHeight, width, values);
            float height = layout.CompareBlockHeight + layout.ConnectorHeight
                + layout.OperationColumnHeight;
            Rect position = EditorGUILayout.GetControlRect(false, height);
            position.x = leftX;
            position.width = width;

            bool checkPassed = _model.ComputeCheckResult();

            DrawSummaryFlow(position.y, position.x, position.width, layout, values, checkPassed);

            // The popups can change the operations during drawing, so compute the stored result again.
            _model.UpdateCheckResult();
        }

        private SummaryLayout BuildSummaryLayout(float rowHeight, float availableWidth, SummaryValues values)
        {
            float compareContentWidth = Mathf.Max(1f, availableWidth - BlockPadding * 2f);
            float operationColumnWidth = Mathf.Max(1f,
                (availableWidth - OperationColumnGap * 2f) / 3f);
            float operationContentWidth = Mathf.Max(1f, operationColumnWidth - BlockPadding * 2f);

            int maskedReference = values.StencilRef & values.ReadMask;
            bool hasWriteMask = values.WriteMask != StencilOperationsHelper.ByteMax;
            string compareExplanation = GetCompareExplanation(values.CompareFunction, maskedReference);
            string passExplanation = GetStencilOpExplanation(values.PassOp, values.ExistingBuffer, values.StencilRef, hasWriteMask);
            string zFailExplanation = GetStencilOpExplanation(values.ZFailOp, values.ExistingBuffer, values.StencilRef, hasWriteMask);
            string failExplanation = GetStencilOpExplanation(values.FailOp, values.ExistingBuffer, values.StencilRef, hasWriteMask);

            float compareExplanationHeight = _neutralStyles.Explanation.CalcHeight(
                new GUIContent(compareExplanation), compareContentWidth);
            float passExplanationHeight = GetExplanationHeight(passExplanation, operationContentWidth);
            float zFailExplanationHeight = GetExplanationHeight(zFailExplanation, operationContentWidth);
            float failExplanationHeight = GetExplanationHeight(failExplanation, operationContentWidth);

            float compareBlockHeight = BlockPadding * 2f + rowHeight * 2f + compareExplanationHeight;
            float connectorHeight = ConnectorStageHeight * 2f + ConnectorBottomLength;
            // Tallest wrapped explanation, so every column's controls and bottoms align.
            float operationExplanationHeight = Mathf.Max(passExplanationHeight,
                Mathf.Max(zFailExplanationHeight, failExplanationHeight));
            float operationColumnHeight = BlockPadding * 2f + rowHeight * 2f
                + operationExplanationHeight;
            float height = Mathf.Ceil(compareBlockHeight + connectorHeight + operationColumnHeight);
            operationColumnHeight = height - compareBlockHeight - connectorHeight;

            return new SummaryLayout(compareBlockHeight, connectorHeight,
                compareExplanationHeight, operationColumnWidth, operationColumnHeight,
                passExplanation, zFailExplanation, failExplanation);
        }

        private void DrawSummaryFlow(float currentY, float startX, float width,
            SummaryLayout layout, SummaryValues values, bool checkPassed)
        {
            bool passApplies = checkPassed && !values.IsOccluded;
            bool zFailApplies = checkPassed && values.IsOccluded;
            bool failApplies = !checkPassed;

            SummaryStyles active = checkPassed ? _passStyles : _failStyles;
            SummaryStyles passStyles = passApplies ? active : _dimStyles;
            SummaryStyles zFailStyles = zFailApplies ? active : _dimStyles;
            SummaryStyles failStyles = failApplies ? active : _dimStyles;

            Rect compareRect = new Rect(startX, currentY, width, layout.CompareBlockHeight);
            Rect connectorRect = new Rect(startX, compareRect.yMax, width, layout.ConnectorHeight);
            Rect passRect = new Rect(startX, connectorRect.yMax, layout.OperationColumnWidth,
                layout.OperationColumnHeight);
            Rect zFailRect = new Rect(passRect.xMax + OperationColumnGap, connectorRect.yMax,
                layout.OperationColumnWidth, layout.OperationColumnHeight);
            Rect failRect = new Rect(zFailRect.xMax + OperationColumnGap, connectorRect.yMax,
                layout.OperationColumnWidth, layout.OperationColumnHeight);
            DrawGroupingBoxes(compareRect, passRect, zFailRect, failRect);
            DrawFlowConnectors(compareRect, connectorRect, passRect, zFailRect, failRect,
                passApplies, zFailApplies, failApplies);

            float compareContentX = compareRect.x + BlockPadding;
            float compareContentWidth = Mathf.Max(1f, compareRect.width - BlockPadding * 2f);
            float compareY = compareRect.y + BlockPadding;
            GUI.Label(new Rect(compareContentX, compareY, compareContentWidth, _rowHeight),
                EditorLocale.editor.Get("stencil_summary_compare_function"), _neutralStyles.Label);
            compareY += _rowHeight;
            EditorGUI.showMixedValue = values.CompareFunctionIsMixed;
            values.CompareFunction = (CompareFunction)_comparePopup.DrawEnum(
                new Rect(compareContentX, compareY, compareContentWidth, _rowHeight),
                values.CompareFunction);
            EditorGUI.showMixedValue = false;
            compareY += _rowHeight;

            int maskedReference = values.StencilRef & values.ReadMask;
            // Preserve the same-frame update after the compare popup changes its value.
            string compareExplanation = GetCompareExplanation(values.CompareFunction, maskedReference);
            GUI.Label(new Rect(compareContentX, compareY, compareContentWidth,
                    layout.CompareExplanationHeight),
                new GUIContent(compareExplanation, compareExplanation), _neutralStyles.Explanation);

            DrawOperationColumn(GetColumnContentRect(passRect),
                EditorLocale.editor.Get("stencil_summary_pass_op"), ref values.PassOp,
                layout.PassExplanation, passStyles, values.PassOpIsMixed, _passPopup);
            DrawOperationColumn(GetColumnContentRect(zFailRect),
                EditorLocale.editor.Get("stencil_summary_zfail_op"), ref values.ZFailOp,
                layout.ZFailExplanation, zFailStyles, values.ZFailOpIsMixed, _zFailPopup);
            DrawOperationColumn(GetColumnContentRect(failRect),
                EditorLocale.editor.Get("stencil_summary_fail_op"), ref values.FailOp,
                layout.FailExplanation, failStyles, values.FailOpIsMixed, _failPopup);

            _model.SaveStencilOperations(values.CompareFunction, values.PassOp, values.FailOp, values.ZFailOp);
        }

        private void DrawOperationColumn(Rect rect, string label, ref StencilOp operation,
            string explanation, SummaryStyles styles, bool hasMixedValue, InspectorPopup popup)
        {
            GUI.Label(new Rect(rect.x, rect.y, rect.width, _rowHeight), label, styles.Label);

            float popupY = rect.y + _rowHeight;
            EditorGUI.showMixedValue = hasMixedValue;
            operation = (StencilOp)popup.DrawEnum(
                new Rect(rect.x, popupY, rect.width, _rowHeight), operation);
            EditorGUI.showMixedValue = false;

            float explanationY = popupY + _rowHeight;
            float explanationHeight = Mathf.Max(1f, rect.yMax - explanationY);
            GUI.Label(new Rect(rect.x, explanationY, rect.width, explanationHeight),
                new GUIContent(explanation, explanation), styles.Explanation);
        }

        private void DrawFlowConnectors(Rect compareRect, Rect connectorRect, Rect passRect,
            Rect zFailRect, Rect failRect, bool passApplies, bool zFailApplies, bool failApplies)
        {
            bool checkPassed = passApplies || zFailApplies;
            Color firstSegmentColor = checkPassed ? Colors.stencilPass : Colors.stencilFail;
            Color passesColor = checkPassed ? Colors.stencilPass : Colors.stencilDim;
            Color failBranchColor = failApplies ? Colors.stencilFail : Colors.stencilDim;
            Color passBranchColor = passApplies ? Colors.stencilPass : Colors.stencilDim;
            Color zFailBranchColor = zFailApplies ? Colors.stencilPass : Colors.stencilDim;

            float compareX = compareRect.center.x;
            float passX = passRect.center.x;
            float zFailX = zFailRect.center.x;
            float failX = failRect.center.x;
            float passSplitX = (passX + zFailX) * 0.5f;
            float compareSplitY = connectorRect.y + ConnectorStageHeight;
            float depthSplitY = compareSplitY + ConnectorStageHeight;

            // Failing exits at the comparison split. Only the passing side reaches the depth split.
            DrawVerticalConnector(compareX, compareRect.yMax, compareSplitY, firstSegmentColor);
            DrawHorizontalConnector(passSplitX, compareX, compareSplitY, passesColor);
            DrawHorizontalConnector(compareX, failX, compareSplitY, failBranchColor);
            DrawVerticalConnector(failX, compareSplitY, failRect.y, failBranchColor);

            DrawVerticalConnector(passSplitX, compareSplitY, depthSplitY, passesColor);
            DrawHorizontalConnector(passX, passSplitX, depthSplitY, passBranchColor);
            DrawHorizontalConnector(passSplitX, zFailX, depthSplitY, zFailBranchColor);
            DrawVerticalConnector(passX, depthSplitY, passRect.y, passBranchColor);
            DrawVerticalConnector(zFailX, depthSplitY, zFailRect.y, zFailBranchColor);

            GUIStyle passesBranchStyle = checkPassed ? _passStyles.Branch : _dimStyles.Branch;
            GUIStyle failBranchStyle = failApplies ? _failStyles.Branch : _dimStyles.Branch;
            GUIStyle passBranchStyle = passApplies ? _passStyles.Branch : _dimStyles.Branch;
            GUIStyle zFailBranchStyle = zFailApplies ? _passStyles.Branch : _dimStyles.Branch;

            DrawBranchLabel(passSplitX, compareX, compareSplitY,
                EditorLocale.editor.Get("stencil_branch_passes"), passesBranchStyle);
            DrawBranchLabel(compareX, failX, compareSplitY,
                EditorLocale.editor.Get("stencil_branch_fails"), failBranchStyle);
            DrawBranchLabel(passX, passSplitX, depthSplitY,
                EditorLocale.editor.Get("stencil_branch_visible"), passBranchStyle);
            DrawBranchLabel(passSplitX, zFailX, depthSplitY,
                EditorLocale.editor.Get("stencil_branch_occluded"), zFailBranchStyle);
        }

        private static void DrawHorizontalConnector(float startX, float endX, float y, Color color)
        {
            float x = Mathf.Min(startX, endX);
            EditorGUI.DrawRect(new Rect(x, y - ConnectorThickness * 0.5f,
                Mathf.Abs(endX - startX), ConnectorThickness), color);
        }

        private static void DrawVerticalConnector(float x, float startY, float endY, Color color)
        {
            float y = Mathf.Min(startY, endY);
            EditorGUI.DrawRect(new Rect(x - ConnectorThickness * 0.5f, y,
                ConnectorThickness, Mathf.Abs(endY - startY)), color);
        }

        private static void DrawBranchLabel(float startX, float endX, float lineY, string label,
            GUIStyle style)
        {
            GUI.Label(new Rect(startX, lineY - BranchLabelHeight, Mathf.Max(1f, endX - startX),
                BranchLabelHeight), label, style);
        }

        private static void DrawGroupingBoxes(Rect compareBox, Rect passBox, Rect zFailBox,
            Rect failBox)
        {
            Color fillColor = EditorStyles.label.normal.textColor;
            fillColor.a = 0.055f;
            EditorGUI.DrawRect(compareBox, fillColor);
            EditorGUI.DrawRect(passBox, fillColor);
            EditorGUI.DrawRect(zFailBox, fillColor);
            EditorGUI.DrawRect(failBox, fillColor);
        }

        private float GetExplanationHeight(string explanation, float explanationWidth)
        {
            return _neutralStyles.Explanation.CalcHeight(
                new GUIContent(explanation), explanationWidth);
        }

        private static Rect GetColumnContentRect(Rect columnRect)
        {
            return new Rect(columnRect.x + BlockPadding, columnRect.y + BlockPadding,
                Mathf.Max(1f, columnRect.width - BlockPadding * 2f),
                Mathf.Max(1f, columnRect.height - BlockPadding * 2f));
        }

        private static string GetCompareExplanation(CompareFunction compareFunction, int maskedReference)
        {
            switch (compareFunction)
            {
                case CompareFunction.Disabled:
                case CompareFunction.Always:
                    return EditorLocale.editor.Get("stencil_compare_always");
                case CompareFunction.Never:
                    return EditorLocale.editor.Get("stencil_compare_never");
                case CompareFunction.Equal:
                    return EditorLocale.editor.Get("stencil_compare_equal").ReplaceVariables(maskedReference);
                case CompareFunction.NotEqual:
                    return EditorLocale.editor.Get("stencil_compare_notequal").ReplaceVariables(maskedReference);
                case CompareFunction.Less:
                    return EditorLocale.editor.Get("stencil_compare_less").ReplaceVariables(maskedReference);
                case CompareFunction.LessEqual:
                    return EditorLocale.editor.Get("stencil_compare_lessequal").ReplaceVariables(maskedReference);
                case CompareFunction.Greater:
                    return EditorLocale.editor.Get("stencil_compare_greater").ReplaceVariables(maskedReference);
                case CompareFunction.GreaterEqual:
                    return EditorLocale.editor.Get("stencil_compare_greaterequal").ReplaceVariables(maskedReference);
                default:
                    return EditorLocale.editor.Get("stencil_compare_unknown");
            }
        }

        private static string GetStencilOpExplanation(StencilOp op, int existingBuffer, int stencilRef,
            bool hasWriteMask)
        {
            int result = StencilOperationsHelper.ApplyStencilOp(op, existingBuffer, stencilRef);
            string key = op == StencilOp.Keep
                ? (hasWriteMask ? "stencil_op_leaves_masked" : "stencil_op_leaves")
                : (hasWriteMask ? "stencil_op_writes_masked" : "stencil_op_writes");
            return EditorLocale.editor.Get(key).ReplaceVariables(result);
        }

        private void InitializeSummaryStyles(Color neutralColor, Color passColor, Color failColor,
            Color dimColor)
        {
            // neutralColor tracks the editor theme, so rebuild when the user switches light/dark.
            if (_neutralStyles.Label != null && _summaryStyleNeutralColor == neutralColor)
            {
                return;
            }
            _summaryStyleNeutralColor = neutralColor;

            _neutralStyles = MakeSummaryStyles(neutralColor);
            _passStyles = MakeSummaryStyles(passColor);
            _failStyles = MakeSummaryStyles(failColor);
            _dimStyles = MakeSummaryStyles(dimColor);
        }

        private static SummaryStyles MakeSummaryStyles(Color color)
        {
            return new SummaryStyles
            {
                Label = MakeCenteredLabel(color),
                Explanation = MakeCenteredExplanation(color),
                Popup = MakeCenteredPopup(color),
                Branch = MakeCenteredLabel(color, 8),
            };
        }

        private static GUIStyle MakeCenteredLabel(Color color, int fontSize = 0)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
            };
            SetStyleTextColor(style, color);
            if (fontSize > 0)
            {
                style.fontSize = fontSize;
            }
            return style;
        }

        private static GUIStyle MakeCenteredExplanation(Color color)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                wordWrap = true,
            };
            SetStyleTextColor(style, color);
            return style;
        }

        private static GUIStyle MakeCenteredPopup(Color color)
        {
            var style = new GUIStyle(EditorStyles.popup)
            {
                alignment = TextAnchor.MiddleCenter,
            };
            SetStyleTextColor(style, color);
            return style;
        }

        private static void SetStyleTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }
    }
}
