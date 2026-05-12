using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Overrides the font used for a text range by selecting a <see cref="FontFamily"/> from the
    /// component's <see cref="UniTextFontStack"/> by its <see cref="FontFamily.name"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter: the target family name (case-sensitive). Example: <c>&lt;font=pixel&gt;Score&lt;/font&gt;</c>.
    /// </para>
    /// <para>
    /// <b>Priority:</b> a matched font wins over <see cref="FontFamily.preferredLanguage"/>
    /// selection and over the regular FontStack fallback chain. If the chosen family's primary
    /// lacks a glyph for a codepoint, the normal fallback chain still kicks in for that codepoint.
    /// </para>
    /// <para>
    /// If the name is not found in the FontStack, a warning is logged once per unresolved name
    /// and affected codepoints render with the default fallback behavior.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 3)]
    [TypeDescription("Selects a font by FontFamily.name from the component's FontStack for this range.")]
    [ParameterField(0, "Family Name", "string", "")]
    public class FontModifier : BaseModifier
    {
        private PooledArrayAttribute<int> attribute;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.Font);
        }

        protected override void OnDisable()
        {
            attribute?.ClearAll();
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.Font);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            var fp = uniText?.FontProvider;
            if (fp == null) return;

            var fontId = fp.TryGetFontIdByFamilyName(parameter);
            if (fontId == 0)
            {
                Debug.LogWarning($"[FontModifier] Family \"{parameter}\" not found in FontStack. " +
                                 $"Check FontFamily.name entries on {uniText?.cachedTransformData.name}.");
                return;
            }

            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buf = attribute.buffer.data;

            for (var i = start; i < clampedEnd; i++)
                buf[i] = fontId;
        }
    }
}
