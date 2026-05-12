using System;

namespace LightSide
{
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class BoldParseRule : TagParseRule { protected override string TagName => "b"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ItalicParseRule : TagParseRule { protected override string TagName => "i"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ColorParseRule : TagParseRule { protected override string TagName => "color"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class SizeParseRule : TagParseRule { protected override string TagName => "size"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class UnderlineParseRule : TagParseRule { protected override string TagName => "u"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class StrikethroughParseRule : TagParseRule { protected override string TagName => "s"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class CSpaceParseRule : TagParseRule { protected override string TagName => "cspace"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class LineSpacingParseRule : TagParseRule { protected override string TagName => "line-spacing"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class LineHeightParseRule : TagParseRule { protected override string TagName => "line-height"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class OutlineParseRule : TagParseRule { protected override string TagName => "outline"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ShadowParseRule : TagParseRule { protected override string TagName => "shadow"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ObjParseRule : TagParseRule { protected override string TagName => "obj"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class EllipsisTagRule : TagParseRule { protected override string TagName => "ellipsis"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class UppercaseParseRule : TagParseRule { protected override string TagName => "upper"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class GradientParseRule : TagParseRule { protected override string TagName => "gradient"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class LinkTagParseRule : TagParseRule { protected override string TagName => "link"; }
}
