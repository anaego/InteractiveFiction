using System;

namespace LightSide
{
    /// <summary>Flags indicating which parts of the text need rebuilding.</summary>
    [Flags]
    public enum UniTextDirtyFlags
    {
        /// <summary>No rebuild needed.</summary>
        None = 0,
        /// <summary>Color changed, vertex colors need update.</summary>
        Color = 1 << 0,
        /// <summary>Alignment changed, positions need recalculation.</summary>
        Alignment = 1 << 1,
        /// <summary>Layout changed, line breaking needs recalculation.</summary>
        Layout = 1 << 2,
        /// <summary>Font size changed.</summary>
        FontSize = 1 << 3,
        /// <summary>Font asset changed, full rebuild required.</summary>
        Font = 1 << 4,
        /// <summary>Text direction changed.</summary>
        Direction = 1 << 5,
        /// <summary>Text content changed, full rebuild required.</summary>
        Text = 1 << 6,
        /// <summary>Material changed (atlas texture, render mode).</summary>
        Material = 1 << 7,
        /// <summary>Sorting order or layer changed (world-space only).</summary>
        Sorting = 1 << 8,
        /// <summary>Layout or font size changed.</summary>
        LayoutRebuild = Layout | FontSize,
        /// <summary>Text, font, or direction changed.</summary>
        FullRebuild = Text | Font | Direction,
        /// <summary>Everything needs rebuilding.</summary>
        All = Color | Alignment | Layout | FontSize | FullRebuild | Sorting
    }
}