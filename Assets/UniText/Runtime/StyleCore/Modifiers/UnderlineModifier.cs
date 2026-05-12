using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Renders an underline below text using the font's underline metrics.
    /// </summary>
    /// <remarks>
    /// No parameter. The underline position is determined by the font's underlineOffset property.
    /// Supports line breaks and color inheritance from the text.
    /// </remarks>
    [Serializable]
    [TypeGroup("Decoration", 1)]
    [TypeDescription("Draws a line beneath the text.")]
    [ParameterField(0, "Thickness", "unit:px|em", "auto")]
    [ParameterField(1, "Offset", "unit:px|em", "auto")]
    [ParameterField(2, "Style", "enum:solid|double|dotted|dashed|wavy", "solid")]
    [ParameterField(3, "Skip Ink", "bool", "false")]
    [ParameterField(4, "Overlay", "bool", "false")]
    public class UnderlineModifier : BaseLineModifier
    {
        protected override string AttributeKey => AttributeKeys.Underline;

        protected override float GetLineOffset(FaceInfo faceInfo, float scale)
        {
            return faceInfo.underlineOffset * scale;
        }
    }
}
