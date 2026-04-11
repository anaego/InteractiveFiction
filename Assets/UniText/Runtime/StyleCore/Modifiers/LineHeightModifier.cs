using System;

namespace LightSide
{
    /// <summary>
    /// Adjusts line height/spacing for text ranges.
    /// </summary>
    /// <remarks>
    /// Parameter: line height or spacing value.
    /// <list type="bullet">
    /// <item><c>1.5</c> — 150% of default line height (multiplier)</item>
    /// <item><c>40</c> — absolute 40 pixels</item>
    /// <item><c>+10</c> — add 10 pixels to default (delta)</item>
    /// <item><c>-5</c> — reduce by 5 pixels (delta)</item>
    /// </list>
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Adjusts the vertical spacing between lines.")]
    [ParameterField(0, "Mode", "enum:h|s", "h")]
    [ParameterField(1, "Value", "unit:px|%|delta", "24")]
    public class LineHeightModifier : BaseModifier
    {
        private enum UnitMode : byte { Multiplier, Absolute, Delta }

        private struct Range
        {
            public int start;
            public int end;
            public float value;
            public UnitMode unitMode;
            public bool isSpacing;
        }

        private PooledList<Range> ranges;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(4);
            ranges.FakeClear();
            uniText.TextProcessor.OnCalculateLineHeight += OnCalculateLineHeight;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.OnCalculateLineHeight -= OnCalculateLineHeight;
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            var reader = new ParameterReader(parameter);
            if (!reader.Next(out var first))
                return;

            var isSpacing = false;

            bool hasMode = first.Length == 1 &&
                           (first[0] == 'h' || first[0] == 'H' || first[0] == 's' || first[0] == 'S');
            if (hasMode)
                isSpacing = first[0] == 's' || first[0] == 'S';

            var valueReader = hasMode ? reader : new ParameterReader(parameter);
            if (!valueReader.NextUnitFloat(out var value, out var unit))
                return;

            UnitMode unitMode;
            switch (unit)
            {
                case ParameterReader.UnitKind.Delta:
                    unitMode = UnitMode.Delta;
                    break;
                case ParameterReader.UnitKind.Absolute:
                    unitMode = UnitMode.Absolute;
                    break;
                default:
                    unitMode = UnitMode.Multiplier;
                    if (unit == ParameterReader.UnitKind.Percent)
                        value /= 100f;
                    break;
            }

            ranges.Add(new Range
            {
                start = start,
                end = end,
                value = value,
                unitMode = unitMode,
                isSpacing = isSpacing
            });
        }

        private void OnCalculateLineHeight(int lineIndex, int lineStartCluster, int lineEndCluster, ref float lineAdvance)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            var defaultAdvance = lineAdvance;
            var hasHeight = false;
            var heightValue = 0f;
            var hasSpacing = false;
            var spacingValue = 0f;

            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];

                if (range.end <= lineStartCluster || range.start >= lineEndCluster)
                    continue;

                if (range.isSpacing)
                {
                    float spacing;
                    if (range.unitMode == UnitMode.Multiplier)
                        spacing = defaultAdvance * (range.value - 1f);
                    else
                        spacing = range.value;

                    if (!hasSpacing || Math.Abs(spacing) > Math.Abs(spacingValue))
                    {
                        hasSpacing = true;
                        spacingValue = spacing;
                    }
                }
                else
                {
                    float effective;
                    switch (range.unitMode)
                    {
                        case UnitMode.Absolute:
                            effective = range.value;
                            break;
                        case UnitMode.Delta:
                            effective = defaultAdvance + range.value;
                            break;
                        default:
                            effective = defaultAdvance * range.value;
                            break;
                    }

                    if (!hasHeight || effective > heightValue)
                    {
                        hasHeight = true;
                        heightValue = effective;
                    }
                }
            }

            if (hasHeight)
                lineAdvance = heightValue + spacingValue;
            else if (hasSpacing)
                lineAdvance = defaultAdvance + spacingValue;
        }
    }

}
