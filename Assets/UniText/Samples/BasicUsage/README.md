# Basic Usage Sample

Demonstrates core UniText features with interactive examples.

## Features Demonstrated

### Markup System
- **Bold**: `<b>text</b>`
- **Italic**: `<i>text</i>`
- **Underline**: `<u>text</u>`
- **Strikethrough**: `<s>text</s>`
- **Color**: `<color=#FF0000>text</color>`
- **Size**: `<size=150%>text</size>`
- **Letter Spacing**: `<cspace=10>text</cspace>`

### RTL Languages
- Arabic (العربية)
- Hebrew (עברית)
- Bidirectional mixed text

### Interactive Links
- Click events with `LinkModifier.LinkClicked`
- Hover events with `LinkModifier.LinkEntered` / `LinkModifier.LinkExited`

### Language (`<lang>` + `UniText.Language`)
- Per-range OpenType `locl` activation
- Whole-text language via component property

### Font (`<font>` + `FontFamily.name`)
- Per-range font override from a named family in the FontStack
- Whole-text family via `SetWholeText<FontModifier>`

## CJK / locl demo fonts

The Language example renders the same Han ideographs four times with different
language tags. To see visible regional glyph differences you need a font that
ships `locl` GSUB substitutions for CJK ideographs.

A ready subset of **Adobe Source Han Sans** (Japanese default + locl covering
`ZHS`/`ZHT`/`ZHH`/`KOR`) is included as `Fonts/SourceHanSans-Demo.otf` (~96 KB).
It covers 15 hand-picked CJK codepoints with strong visual differences between
regions: `直骨雪今家字漢社海高神真食言會學`.

To use it:
1. Create a `UniTextFont` asset from the `.otf` (UniText → Tools → Import Font).
2. Add it as the primary of a `FontFamily` in your `UniTextFontStack`.
3. Navigate to the Language example — the four rows should render distinct glyphs.

The subset license (SIL OFL 1.1) lives next to the file as
`SourceHanSans-LICENSE.txt` and is redistributable under the same terms.

## Scene Setup

1. Create a Canvas (UI → Canvas)
2. Add two UniText components:
   - **DemoText** — Main text display (center of screen)
   - **StatusText** — Status bar (bottom of screen)
3. Add `BasicUsageExample` script to any GameObject
4. Assign both UniText components to the script

## Controls

- **Space** or **→** — Next example
- **←** — Previous example
- **Click** on links — Opens URL

## Key Code Concepts

### Registering Modifiers at Runtime

```csharp
// Create modifier and rule pair
var register = new ModRegister
{
    Modifier = new ColorModifier(),
    Rule = new ColorParseRule()
};

// Register with UniText component
uniText.RegisterModifier(register);
```

### Handling Link Events

```csharp
// Get LinkModifier from registered modifiers
var linkModifier = new LinkModifier();
uniText.RegisterModifier(new ModRegister { Modifier = linkModifier, Rule = new LinkTagParseRule() });

// Subscribe to link events
linkModifier.LinkClicked += url => Debug.Log($"Clicked: {url}");
linkModifier.LinkEntered += url => Debug.Log($"Hovering: {url}");
linkModifier.LinkExited += () => Debug.Log("Exited link");
```

### Changing Text at Runtime

```csharp
uniText.Text = "<b>Bold</b> and <color=#FF0000>Red</color>";
```

## Scripts

- `BasicUsageExample.cs` — Main example demonstrating runtime API
