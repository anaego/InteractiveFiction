using System;

namespace LightSide
{
    /// <summary>
    /// Applies a BCP 47 language tag to a text range for OpenType-aware shaping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The language tag is forwarded to HarfBuzz as <c>hb_language_t</c>, which drives
    /// the OpenType <c>locl</c> (Localized Forms) GSUB feature. This is essential for
    /// pan-CJK fonts such as Noto Sans CJK and Source Han Sans, where a single code point
    /// renders with different region-specific glyphs (Simplified Chinese, Traditional Chinese,
    /// Japanese, Korean) depending on the language tag.
    /// </para>
    /// <para>
    /// Common tags: <c>zh-Hans</c>, <c>zh-Hant</c>, <c>zh-HK</c>, <c>ja</c>, <c>ko</c>,
    /// <c>en</c>, <c>ar</c>, <c>he</c>. HarfBuzz converts BCP 47 to OpenType language tags
    /// automatically (<c>zh-Hans</c> → <c>ZHS</c>, <c>ja</c> → <c>JAN</c>, etc.).
    /// </para>
    /// <para>
    /// Apply to the entire text via <c>RangeRule</c> (<c>".."</c>) for a global default,
    /// or per-range via the rich-text tag <c>&lt;lang=zh-Hans&gt;...&lt;/lang&gt;</c> for
    /// mixed-language content.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 2)]
    [TypeDescription("Applies a BCP 47 language tag that activates OpenType 'locl' region-specific glyph variants (critical for CJK).")]
    [ParameterField(0, "Language", "string", "en")]
    public class LanguageModifier : BaseModifier
    {
        private PooledArrayAttribute<byte> attribute;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.Language);
        }

        protected override void OnDisable()
        {
            attribute?.ClearAll();
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.Language);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter)) return;

            var index = LanguageRegistry.Register(parameter);
            if (index == LanguageRegistry.Unset) return;

            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buf = attribute.buffer.data;

            for (var i = start; i < clampedEnd; i++)
                buf[i] = index;
        }
    }
}
