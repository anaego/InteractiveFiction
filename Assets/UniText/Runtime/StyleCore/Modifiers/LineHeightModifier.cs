using System;

namespace LightSide
{
    /// <summary>
    /// Adjusts line height for text ranges.
    /// </summary>
    /// <remarks>
    /// Parameter: line height value with optional unit.
    /// <list type="bullet">
    /// <item><c>1.5</c> or <c>150%</c> — 150% of default line height</item>
    /// <item><c>40</c> — absolute 40 pixels (replaces default)</item>
    /// <item><c>+10</c> — add 10 pixels to base (default or absolute)</item>
    /// <item><c>-5</c> — reduce by 5 pixels</item>
    /// </list>
    /// When multiple overlapping ranges set an absolute height, the largest absolute wins
    /// and serves as the base for any delta or multiplier on the same line.
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Adjusts the vertical spacing between lines.")]
    [ParameterField(0, "Value", "unit:px|%|delta", "24")]
    public class LineHeightModifier : BaseModifier
    {
        private enum UnitMode : byte { Multiplier, Absolute, Delta }

        private struct Range
        {
            public int start;
            public int end;
            public float value;
            public UnitMode unitMode;
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

            var afterLegacyMode = reader;
            if (afterLegacyMode.Next(out var first) &&
                first.Length == 1 &&
                (first[0] == 'h' || first[0] == 'H' || first[0] == 's' || first[0] == 'S'))
            {
                reader = afterLegacyMode;
            }

            if (!reader.NextUnitFloat(out var value, out var unit))
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
            });
        }

        private void OnCalculateLineHeight(int lineIndex, int lineStartCluster, int lineEndCluster, ref float lineAdvance)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            var defaultAdvance = lineAdvance;

            var hasAbsolute = false;
            var absoluteValue = 0f;
            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.end <= lineStartCluster || range.start >= lineEndCluster)
                    continue;
                if (range.unitMode != UnitMode.Absolute)
                    continue;
                if (!hasAbsolute || range.value > absoluteValue)
                {
                    absoluteValue = range.value;
                    hasAbsolute = true;
                }
            }

            var baseAdvance = hasAbsolute ? absoluteValue : defaultAdvance;

            var hasResult = hasAbsolute;
            var result = absoluteValue;

            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.end <= lineStartCluster || range.start >= lineEndCluster)
                    continue;

                float candidate;
                switch (range.unitMode)
                {
                    case UnitMode.Absolute:
                        continue;
                    case UnitMode.Delta:
                        candidate = baseAdvance + range.value;
                        break;
                    default:
                        candidate = baseAdvance * range.value;
                        break;
                }

                if (!hasResult || candidate > result)
                {
                    result = candidate;
                    hasResult = true;
                }
            }

            if (hasResult)
                lineAdvance = result;
        }
    }

}
