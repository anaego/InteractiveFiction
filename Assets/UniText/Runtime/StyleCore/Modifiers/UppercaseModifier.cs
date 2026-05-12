using System;

namespace LightSide
{
    /// <summary>
    /// Transforms text to uppercase within marked ranges.
    /// </summary>
    /// <remarks>
    /// No parameter. The transformation happens during Apply, after parsing but before shaping,
    /// ensuring correct glyph rendering for uppercase characters. Uses the bundled UCD case
    /// mapping table rather than <c>char.ToUpperInvariant</c> to avoid runtime gaps in Mono/IL2CPP
    /// (e.g. Greek final sigma U+03C2 → Σ).
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Converts text to uppercase.")]
    public class UppercaseModifier : BaseModifier
    {
        protected override void OnApply(int start, int end, string parameter)
        {
            var codepoints = buffers.codepoints.data;
            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);

            for (var i = start; i < clampedEnd; i++)
                codepoints[i] = UnicodeData.GetSimpleUppercase(codepoints[i]);
        }
    }
}
