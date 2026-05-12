# Getting Started

This guide covers the basics of setting up and using UniText in your Unity project.

## 1. Adding UniText to a Scene

UniText ships two rendering components. Pick the one that matches your scene:

| Component | Use when | Renders via |
|-----------|----------|-------------|
| `UniText` | Text in a Canvas UI (screens, HUDs, inspector overlays) | `CanvasRenderer` (UGUI) |
| `UniTextWorld` | Text placed in 3D world space, particle-like text, floating labels | Combined mesh via an invisible batcher (see §1.3) |

Both components share 100% of the text processing pipeline (parsing, shaping, layout, modifiers, emoji, variable fonts, language). Only the rendering surface differs.

### 1.1 Canvas text (`UniText`)

Use the **GameObject** menu to create ready-to-use Canvas UniText objects:

- **GameObject > UI (Canvas) > UniText > Text** — text with default font and size
- **GameObject > UI (Canvas) > UniText > Button** — button with UniText label (Image + Button + UniText child)

> **Input Field** is currently in development and will be available in a future release.

Canvas and EventSystem are created automatically if not present. Default font stack from **Project Settings > UniText** is applied to all created components.

You can also override default prefabs in **Project Settings > UniText** (Text Prefab, Button Prefab) — the menu will instantiate your prefab instead.

```csharp
// Via code:
var uniText = gameObject.AddComponent<UniText>();
uniText.FontStack = myFontStack;
uniText.Text = "Hello, World!";
```

Note: Editor defaults (from Project Settings > UniText) are only applied when adding the component via the menu or Inspector.

### 1.2 World-space text (`UniTextWorld`)

Use the menu to create a ready-to-go world-space text object:

- **GameObject > UI (World) > UniText > World Text**

The menu creates a `UniTextWorld` scaled to `0.01` (so world units line up with your typical 3D scene), and auto-adds a `UniTextWorldRaycaster` to `Camera.main` so pointer events work out of the box (see §4.4). Override the prefab in **Project Settings > UniText > World Text Prefab**.

World-space text authoring is identical to Canvas text — same `FontStack`, `FontSize`, alignment, modifiers, styles, language, and so on. The component also exposes Unity's standard sorting knobs:

```csharp
var world = gameObject.AddComponent<UniTextWorld>();
world.Text = "Hello from world space!";
world.SortingOrder = 5;
world.SortingLayerID = SortingLayer.NameToID("Gameplay");
```

### 1.3 How world-space rendering works

You never attach a `MeshRenderer` to a `UniTextWorld`. An invisible `UniTextWorldBatcher` in the scene subscribes to `UniTextWorld` events and assembles combined meshes:

- All active `UniTextWorld` components sharing the same `(material, SortingLayer, OrderInLayer, SortingGroup)` are batched into one draw call.
- Large groups are split into multiple shards automatically (target shard size configured via `UniTextSettings.WorldBatcherShardTargetVertexCount`, default 8192 vertices).
- Each batched group respects Unity's sorting model, so world-space text interleaves correctly with `SpriteRenderer` and other renderers per-instance.
- When a component moves, only its slice of the shard's vertex buffer is rewritten (no full rebuild for transform-only changes).

The batcher is fully transparent — you don't configure it. If you need to observe the render pipeline from outside (custom batchers, debug overlays), `UniTextWorld` exposes public events: static `Activated` / `Deactivated`, per-instance `RenderDataAvailable` / `RenderDataCleared` / `SortingChanged` / `ParentChanged`, and a `UniTextWorld.Active` list of currently enabled instances.

---

## 2. Working with Fonts

UniText uses its own font format with two rendering modes:

| Mode | Description | Use Case |
|------|-------------|----------|
| **SDF** | Single-channel Signed Distance Field | Default. Resolution-independent, supports outlines and shadows |
| **MSDF** | Multi-channel Signed Distance Field | Sharper corners on geometric/display fonts |

Both modes use Burst-compiled curve-based rasterization (no bitmap rendering). Glyphs are stored in a shared `Texture2DArray` atlas with adaptive tile sizes (64/128/256), reference counting, and LRU eviction. Set the mode per component via `RenderMode`.

### 2.1 Creating a UniTextFont Asset

**Context Menu** (from fonts already in the project):

1. Import your font files (`.ttf`, `.otf`, or `.ttc`) into Unity
2. Select one or multiple fonts in the Project window
3. Right-click > **Create > UniText > Font Asset**
4. A `.asset` file is created next to each source font

Supports batch creation — select 10 fonts, get 10 assets in one click.

**UniText Tools Window** (also useful for creating from fonts outside the project):

If the font file is somewhere on your computer but not imported into the Unity project:

1. Open **Tools > UniText > Tools**
2. Drag-and-drop font files from the Project window, or click **Browse Files** to pick fonts from anywhere on your computer
3. Click **Create N UniText Font Asset(s)**
4. For external fonts, you will be prompted for an output folder within Assets

This is also useful for quick drag-and-drop workflow without manually importing fonts first.

Font bytes are embedded directly in the asset — there is no external file dependency at runtime.

### 2.2 Font Inspector Settings

Select a UniTextFont asset to configure in the Inspector:

| Setting | Default | Description |
|---------|---------|-------------|
| **Font Scale** | 1.0 | Visual scale multiplier. Normalizes fonts that appear too small or too large by design |
| **SDF Detail** | 1.0 | Tile detail multiplier. Higher values force larger atlas tiles for fonts with thin strokes (e.g. calligraphic) |
| **Glyph Overrides** | — | Per-glyph tile size overrides (Auto/64/128/256) for fine-tuning quality on specific glyphs |

After changing SDF Detail or Glyph Overrides, click **Apply** to rebuild the atlas. **Revert** discards pending changes.

A **Glyph Picker** is built into the inspector: type text to preview glyph rendering, select individual glyphs from the grid, and add tile size overrides directly.

The Inspector also shows:
- **Face Info** — family name, style, weight class, italic flag (read-only, extracted from font data)
- **Variable Font Axes** — if the font is variable, shows available axes with min/default/max values
- **Font Data Status** — whether font bytes are embedded
- **Runtime Data** — glyph count, character count
- **Atlas Preview** — SDF, MSDF, and Emoji atlas texture slices

### 2.3 Creating a UniTextFontStack (Font Collection)

UniTextFontStack organizes fonts into **Font Families**. Each family has:

- a **primary** font and optional **faces** (bold, italic, light, etc.) — the same family, different weights/styles;
- an optional `name` — a user-facing identifier addressable from markup (see §5 and `FontModifier`);
- an optional `preferredLanguage` — a BCP 47 tag that biases codepoint resolution toward this family when the active language matches (see §5).

Families are searched in order for glyph fallback.

There are two creation modes when you select multiple UniTextFont assets:

#### Font Stack (Combined) — Grouped by Family

1. Select 2+ **UniTextFont** assets in the Project window
2. Right-click > **Create > UniText > Font Stack (Combined)**
3. Fonts are automatically grouped by `familyName`. The closest-to-Regular font becomes the primary; others become faces.

```
Inter+Noto-Sans-Variable.asset
├── Family: Inter
│   ├── primary: Inter-Regular       (weight 400)
│   ├── face: Inter-Bold             (weight 700)
│   └── face: Inter-Italic           (weight 400, italic)
├── Family: NotoSansArabic
│   └── primary: NotoSansArabic-Regular
└── Family: NotoSansHebrew
    └── primary: NotoSansHebrew-Regular
```

When rendering "Hello مرحبا עולם":
- "Hello" — Inter family has Latin glyphs, used directly
- "مرحبا" — Inter has no Arabic glyphs, falls back to NotoSansArabic family
- "עולם" — Falls back to NotoSansHebrew family

When `<b>` is applied, the system uses CSS §5.2 weight matching to find the best face within the same family (e.g., Inter-Bold). If no matching face exists, synthesis (fake bold/italic) is applied.

**Use case:** Multilingual text with real bold/italic variants. One component handles any language.

#### Font Stack (Per Font) — Individual Stacks

1. Select 1+ **UniTextFont** assets in the Project window
2. Right-click > **Create > UniText > Font Stack (Per Font)**
3. Creates **one separate** UniTextFontStack for each selected font

**Use case:** When different components use different fonts. Swap font stacks per component.

#### Variable Fonts

Variable fonts are strongly recommended over static font files. A single variable font file replaces dozens of static weights/widths:

```
Inter-Variable.asset                    <- one file
├── wght axis: 100–900 (weight)
├── wdth axis: 75–100 (width)
└── replaces: Inter-Thin, Inter-Light, Inter-Regular, Inter-Medium,
              Inter-SemiBold, Inter-Bold, Inter-ExtraBold, Inter-Black
```

Variable font axes are controlled via modifiers. `<b>` and `<i>` automatically set the appropriate axes when the font supports them. For direct control, use the VariationModifier with `<var>` tags.

#### Three-Tier Face Resolution

When a modifier requests bold or italic, the system resolves in order:
1. **Variable font axes** — if the font has `wght`/`ital`/`slnt` axes, set them directly
2. **Static font faces** — find the closest matching face by weight/italic in the family
3. **Synthesis** — apply fake bold (SDF dilate) or fake italic (shear transform)

#### Fallback Stack Chaining

UniTextFontStack has a `fallbackStack` field that references another UniTextFontStack. The system searches primary fonts in each family first, then walks the `fallbackStack` chain. Circular references are handled automatically.

```
LanguageSupportStack                    <- create once
├── Family: NotoSansArabic
├── Family: NotoSansHebrew
├── Family: NotoSansDevanagari
└── Family: NotoSansCJK

HeadingStack                            <- for headings
├── Family: Montserrat (primary + bold/italic faces)
└── fallbackStack → LanguageSupportStack

BodyStack                               <- for body text
├── Family: Inter (primary + faces)
└── fallbackStack → LanguageSupportStack
```

All stacks get full language support through one shared reference.

### 2.4 UniText Tools Window

Open via **Tools > UniText > Tools**. Three tabs:

#### Tab 1: Create Font Asset

Batch creation of UniTextFont assets from source files.

**Adding fonts:**
- **Drag & drop** — drop `.ttf`/`.otf`/`.ttc` files into the drop area
- **Browse Files** — opens file dialog with multi-select
- **Project selection** — selecting font files in the Project window auto-adds them

Each entry shows the font name and file size. Click **Create N UniText Font Asset(s)** to generate all assets.

**Additional features:**
- **Copy All Characters** — extracts every codepoint the font supports and copies to clipboard. Useful for checking font coverage or as input for the Font Subsetter

**Output:**
- Project fonts (within Assets): saved next to the source file
- External fonts (outside Assets): prompts for output folder

#### Tab 2: Font Subsetter

Create optimized subset fonts by keeping or removing specific character ranges. Reduces font file size for builds where you don't need full Unicode coverage.

**Two modes:**

**Keep Mode** — only selected characters remain in the font:
- Select script ranges (Latin, Cyrillic, Arabic, etc.) and/or type custom text
- The output font contains only those characters (plus GSUB-related composed forms)
- Example: Keep only "Basic Latin + Cyrillic" for a game targeting English/Russian

**Remove Mode** — selected characters are removed from the font:
- Select script ranges and/or type custom text to remove
- Intelligent composition detection: combined characters (emoji sequences, ligatures) are removed as glyphs while preserving their component codepoints
- Two-pass process:
  1. Codepoint removal with GSUB closure (handles contextual forms)
  2. Composition glyph removal without closure (preserves components)
- Example: Remove CJK range from a font that covers everything

**Available script ranges (30 sets in 10 groups):**

| Group | Ranges |
|-------|--------|
| Latin | Basic Latin, Extended Latin, Vietnamese |
| European | Cyrillic, Greek, Armenian, Georgian |
| Semitic | Arabic, Hebrew |
| N. Indic | Devanagari, Bengali, Gujarati, Gurmukhi |
| S. Indic | Tamil, Telugu, Kannada, Malayalam |
| SE Asian | Thai, Lao, Myanmar, Khmer |
| E. Asian | Hiragana, Katakana |
| Other | Sinhala, Tibetan |
| Symbols (1) | Digits, Punctuation, Currency, Math |
| Symbols (2) | Arrows, Box Drawing |

**Output:** Saves a new `.ttf` file with the suffix `_subset`. Reports original size, subset size, and reduction percentage.

**Practical scenarios:**

| Scenario | Mode | Configuration |
|----------|------|---------------|
| Mobile game, English only | Keep | Basic Latin + Digits + Punctuation |
| European app, no Asian scripts | Remove | Devanagari, Bengali, Tamil, Thai, CJK, etc. |
| Localized to Arabic + English | Keep | Basic Latin + Arabic + Digits + Punctuation |
| Remove unused emoji from Noto | Remove | Custom text with emoji codepoints |

#### Tab 3: Dictionary Builder

Builds word segmentation dictionary assets for SE Asian scripts (Thai, Lao, Khmer, Myanmar) that don't use spaces between words.

1. Drag-and-drop a word list text file (one word per line)
2. Select the target script
3. Click **Build** to compile a `WordSegmentationDictionary` asset

The compiled dictionary is configured via **Project Settings > UniText > Word Segmentation > Dictionaries**. UniText ships with a Thai dictionary (26K words from ICU).

### 2.5 Materials

Materials for the base text pass (SDF Face, SDF Base, MSDF Face, MSDF Base) and the emoji pass are managed automatically by `UniTextMaterialCache` — there is no manual material assignment on `UniText`.

Outline and shadow effects render as extra quads appended to the same mesh as the face (not as separate `CanvasRenderer` objects). Any number of outline / shadow modifiers can be layered on the same text without extra sub-meshes.

If you want to apply a **custom material / shader** to a text range, see §6.

---

## 3. Markup System

UniText features an extensible markup system based on **Modifiers** and **Parse Rules**.

### 3.1 Architecture: Rule + Modifier

The system separates **what to parse** from **what to do**:

- **Parse Rule** (`IParseRule`) — finds patterns in text and produces ranges with optional parameters
- **Modifier** (`BaseModifier`) — applies a visual or structural effect to those ranges

There is **no hard coupling** between tags and modifiers. Any parse rule can drive any modifier. The tag name, the syntax, and even the parsing strategy are all independent from the effect being applied. A `<highlight>` tag can trigger a ColorModifier. A `**markdown**` wrapper can trigger an OutlineModifier. You decide.

**Example**: The same BoldModifier works with completely different syntaxes:

| Parse Rule | Syntax | Modifier |
|------------|--------|----------|
| TagRule (tagName="b") | `<b>bold</b>` | BoldModifier |
| TagRule (tagName="strong") | `<strong>bold</strong>` | BoldModifier |
| MarkdownWrapRule (marker="**") | `**bold**` | BoldModifier |
| RangeRule (range="..") | *(entire text, no markup)* | BoldModifier |

And the same TagRule (tagName="b") can be paired with any modifier — BoldModifier, ColorModifier, or your own custom modifier.

### 3.2 Built-in Modifiers

The table below shows **default pairings** (how presets configure them). These are conventions, not constraints — you can reassign any tag to any modifier.

| Default Tag | Modifier | Example |
|-------------|----------|---------|
| `<b>` | BoldModifier | `<b>bold</b>` or `<b=700>weight 700</b>` |
| `<i>` | ItalicModifier | `<i>italic</i>` |
| `<u>` | UnderlineModifier | `<u>underline</u>` |
| `<s>` | StrikethroughModifier | `<s>strike</s>` |
| `<color>` | ColorModifier | `<color=#FF0000>red</color>` |
| `<size>` | SizeModifier | `<size=24>large</size>` |
| `<gradient>` | GradientModifier | `<gradient=rainbow>text</gradient>` |
| `<cspace>` | LetterSpacingModifier | `<cspace=5>wider</cspace>` |
| `<line-height>` | LineHeightModifier | `<line-height=1.5>text</line-height>` |
| `<line-spacing>` | LineHeightModifier | `<line-spacing=10>text</line-spacing>` |
| `<upper>` | UppercaseModifier | `<upper>text</upper>` |
| `<ellipsis>` | EllipsisModifier | `<ellipsis=1>long text</ellipsis>` |
| `<li>` | ListModifier | `<li>bullet item</li>` |
| `<link>` | LinkModifier | `<link=url>click</link>` |
| `<obj>` | ObjModifier | `<obj=icon/>` |
| `<outline>` | OutlineModifier | `<outline=#000>text</outline>` or `<outline=0.3,#FF0000>` |
| `<shadow>` | ShadowModifier | `<shadow=#00000080>text</shadow>` or `<shadow=0.1,#000,2,2,0.5>` |
| `<var>` | VariationModifier | `<var=700>weight</var>` (direct axis control) |
| `<font>` | FontModifier | `<font=pixel>Score</font>` — selects a family by `FontFamily.name` (see §5) |
| `<lang>` | LanguageModifier | `<lang=zh-Hans>汉字</lang>` — BCP 47 tag (see §5) |
| `<mat>` | MaterialModifier | `<mat>text</mat>` or `<mat=#FF8800>` — custom material (see §6) |

### 3.3 Custom Tags with Default Parameters

TagRule has a `defaultParameter` field that lets you create custom tags with pre-configured values. This way your text stays clean — no need to repeat parameter values in every tag.

**Example**: Create a `<warning>` tag that always applies red color:

```
Style:
  Rule: TagRule (tagName = "warning", defaultParameter = "#FF0000")
  Modifier: ColorModifier
```

Now in text:
- `<warning>error occurred</warning>` — uses default red (#FF0000)
- `<warning=#FFA500>caution</warning>` — overrides with orange

**Multi-parameter defaults**: For modifiers with multiple parameters (like OutlineModifier: dilate, color), defaults fill in missing values:

```
Style:
  Rule: TagRule (tagName = "glow", defaultParameter = "0.3,#00FF00")
  Modifier: OutlineModifier
```

- `<glow>text</glow>` — dilate 0.3, green outline
- `<glow=0.5>text</glow>` — dilate 0.5, green outline (color from default)

This works because TagRule merges text parameters with defaults: values from the tag take priority, remaining parameters come from `defaultParameter`.

MarkdownWrapRule also supports `defaultParameter` the same way.

### 3.4 Parse Rule Types

#### Tag-Based Rules

All tag-based rules use the universal **TagRule** class with a configurable tag name. Parameters are always optional. Self-closing is syntax-driven (`<tag/>` or `<tag=value/>`).

#### Markdown-Style Rules

| Parse Rule | Syntax | Typical Modifier |
|------------|--------|----------|
| MarkdownWrapRule (`**`) | `**bold**` | BoldModifier |
| MarkdownWrapRule (`*`) | `*italic*` | ItalicModifier |
| MarkdownWrapRule (`~~`) | `~~strike~~` | StrikethroughModifier |
| MarkdownLinkParseRule | `[text](url)` | LinkModifier |
| MarkdownListParseRule | `- item`, `* item`, `1. item` | ListModifier |
| RawUrlParseRule | Auto-detects `https://...` URLs | LinkModifier |

#### Utility Rules

| Parse Rule | Purpose |
|------------|---------|
| RangeRule | Apply modifier to specific character ranges without any markup in text |
| StringParseRule | Match and optionally replace literal string patterns |
| CompositeParseRule | Groups multiple rules under one modifier — each position in text is checked against child rules in order until one matches |

#### Protection Rules (standalone)

Protection rules shield their content from being consumed by any other parse rule. They are **standalone** — they implement `IParseRule.IsStandalone = true` and register without a paired modifier (the rule acts on its own). The text is passed through unaltered except that the delimiters themselves are stripped.

| Parse Rule | Syntax | Behavior |
|------------|--------|----------|
| NoparseTagRule | `<noparse>...</noparse>` | Everything inside is treated as literal text. Missing closer = rest of string protected |
| CodeSpanRule | `` `x` ``, ` ``x`` `, ` ```x``` ` | Balanced backtick runs per CommonMark §6.1 |
| BackslashEscapeRule | `\*`, `\[`, `\#`, …  | Escapes a single ASCII punctuation character after `\` |

Register standalone rules with `AddRule`:

```csharp
uniText.AddRule(new NoparseTagRule());
uniText.AddRule(new BackslashEscapeRule());

// Remove later if needed:
uniText.RemoveRule(myRule);
```

`AddRule` enforces `IParseRule.IsStandalone == true` — passing a non-standalone rule logs an error and does nothing (use `AddStyle` with a modifier for those). Your own rules can opt into standalone behavior by overriding `IsStandalone => true`.

### 3.5 Parameter Formats Reference

The tag names below (`<color>`, `<font>`, `<mat>`, …) are the conventional names used by the built-in presets — they are not hard-coded into the modifiers. Any modifier can be registered under any name via `Style.Tag(new XxxModifier(), "yourName")`, or driven by `MarkdownWrapRule` / `RangeRule` / `StringParseRule` / a custom rule with no tag at all (see §3.1). The **parameter** syntax shown for each modifier is what the modifier itself parses, regardless of how the range was matched.

**Color** (`ColorModifier`):
- Hex: `#RGB`, `#RRGGBB`, `#RRGGBBAA`
- Named (20 colors): white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold

**Size** (`SizeModifier`):
- Absolute: `<size=24>` — 24 pixels
- Percentage: `<size=150%>` — 150% of base size
- Relative: `<size=+10>` / `<size=-5>` — offset from base

**Gradient** (`GradientModifier`):
- Format: `<gradient=name[,shape][,angle]>`
- Shapes: `linear` (default), `radial`, `angular`
- Angle: 0–360 degrees (0=right, 90=up). Used by `linear` and `angular`
- Examples:
  - `<gradient=rainbow>` — linear, horizontal
  - `<gradient=rainbow,radial>` — radial from center
  - `<gradient=rainbow,angular,90>` — conic sweep, rotated 90°
  - `<gradient=rainbow,linear,45>` — linear, rotated 45°

Gradients are defined in the **UniTextGradients** asset (Project Settings > UniText > Gradients).

**Letter spacing** (`LetterSpacingModifier`):
- Format: `spacing[,monospace]`
- Pixels: `<cspace=5>` — 5px extra spacing
- Em units: `<cspace=0.1em>` — 0.1 em extra spacing
- Monospace: `<cspace=0.5em,true>` — equal advance width for all glyphs
- For cursive scripts (Arabic, Syriac, etc.), positive spacing renders visual tatweel (kashida) to preserve connections

**Outline** (`OutlineModifier`):
- `<outline>` — default (dilate=0.2, black)
- `<outline=0.3>` — custom dilate
- `<outline=#FF0000>` — custom color
- `<outline=0.3,#FF0000>` — both

**Shadow** (`ShadowModifier`):
- `<shadow>` — default (black 50% alpha)
- `<shadow=#00000080>` — custom color
- `<shadow=0.1,#000,2,2,0.5>` — dilate, color, offsetX, offsetY, softness

**Variable font axes** (`VariationModifier`):
- Positional axis values in order: wght, wdth, ital, slnt, opsz
- Use `~` to skip an axis
- Absolute: `<var=700>` — weight 700
- Percentage: `<var=150%>` — 150% of default weight
- Delta: `<var=+200>` — +200 from default weight
- Multiple axes: `<var=700,80>` — weight 700, width 80
- Skip axes: `<var=~,~,~,-12>` — only set slant to -12

**Ellipsis** (`EllipsisModifier`):
- `<ellipsis=1>` — truncate end (default): `Hello Wo...`
- `<ellipsis=0>` — truncate start: `...o World`
- `<ellipsis=0.5>` — truncate middle: `Hel...rld`
- Any float 0-1 for fine-grained control

**Font** (`FontModifier`):
- Parameter is a `FontFamily.name` from the component's font stack
- `<font=pixel>Score</font>` — render "Score" in the family named `pixel`

**Language** (`LanguageModifier`):
- Parameter is a BCP 47 tag
- `<lang=zh-Hans>汉字</lang>`, `<lang=ja>...</lang>`, `<lang=ko>...</lang>`, `<lang=en-US>...</lang>`

**Material** (`MaterialModifier`):
- Parameter is an optional tint color (same syntax as Color)
- `<mat>text</mat>` — use the material's tint as-is
- `<mat=#FF8800>text</mat>` — multiply the vertex color by orange

### 3.6 Adding Styles to a Component

#### In the Inspector

1. Expand **Styles** list on the UniText component
2. Click **+** — a searchable selector opens with predefined presets (Bold, Italic, Color, Font, Language, Material, Markdown variants, Protection rules, and more)
3. Select a preset — both the Rule and Modifier are configured automatically

Each entry is a Rule+Modifier pair. Tags from the Rule are parsed in text, and the Modifier applies the effect to matched ranges. You can also configure Rule and Modifier manually for custom combinations.

#### Via Code — Fluent Builders

`Style` exposes three static builders that cover the common cases:

```csharp
// Whole-text application (equivalent to RangeRule with ".."):
uniText.AddStyle(Style.WholeText(new ColorModifier(), "#FF6600"));

// Fixed codepoint range:
uniText.AddStyle(Style.Range(new ColorModifier(), start: 0, end: 5, parameter: "#FF0000"));

// Tag-based:
uniText.AddStyle(Style.Tag(new ColorModifier(), "color"));
uniText.AddStyle(Style.Tag(new ColorModifier(), "warning", defaultParameter: "#FF0000"));
```

For custom combinations (StringParseRule, CompositeParseRule, custom rules) use the explicit form:

```csharp
uniText.AddStyle(new Style
{
    Rule = new TagRule { tagName = "color" },
    Modifier = new ColorModifier()
});
```

Remove at runtime:

```csharp
bool removed = uniText.RemoveStyle(style);
uniText.ClearStyles();
```

#### Querying and Mutating Styles at Runtime

```csharp
// Check presence
bool hasBold = uniText.HasModifier<BoldModifier>();

// Find the first style backed by a given modifier type
if (uniText.TryGetStyle<ColorModifier>(out var colorStyle)) { ... }

// Enumerate every matching style (local + preset copies)
foreach (var s in uniText.GetStylesOfType<LinkModifier>()) { ... }

// Whole-text convenience — add/update/toggle/clear a style that covers the full text
uniText.SetWholeText<BoldModifier>();                      // bold everything
uniText.SetWholeText<ColorModifier>("#FF0000");            // red everything
bool isBold = uniText.ToggleWholeText<BoldModifier>();     // invert
string currentColor = uniText.GetWholeTextParameter<ColorModifier>();
uniText.ClearWholeText<ColorModifier>();
```

`SetWholeText` / `ClearWholeText` / `ToggleWholeText` operate on the component's **local** Styles list only — they never mutate Style Presets (those are shared assets).

### 3.7 Style Preset — Shared Configuration

**Problem:** You have 50 UniText components that all need the same set of modifiers (bold, italic, color, links). Setting up each one manually is tedious and error-prone.

**Solution:** Style Preset is a ScriptableObject that stores a reusable list of Rule+Modifier pairs.

#### Setup

1. **Assets > Create > UniText > Style Preset**
2. Add your modifier pairs:

```
MyModConfig.asset
├── [0] BoldModifier + TagRule (b)
├── [1] ItalicModifier + TagRule (i)
├── [2] ColorModifier + TagRule (color)
├── [3] LinkModifier + TagRule (link)
└── [4] UnderlineModifier + TagRule (u)
```

3. On each UniText component, add this config to the **Style Presets** list

#### Benefits

- **Single source of truth** — change the config, all components update
- **No duplication** — define modifiers once, reference everywhere
- **Combinable** — a component can have multiple configs plus its own local Styles. They all work together
- **Version control friendly** — one asset to track rather than per-component settings

#### Local vs Config

| Feature | Local Styles | Style Presets |
|---------|-------------------|-------------------|
| Scope | Per-component | Shared across components |
| Edit location | UniText Inspector | Preset asset Inspector |
| Use case | Component-specific markup | Project-wide standard markup |

A component's effective set of modifiers = its local Styles + all Style Presets.

#### Runtime API

```csharp
uniText.AddStylePreset(myPreset);
bool removed = uniText.RemoveStylePreset(myPreset);
uniText.ClearStylePresets();
```

Useful for toggling a markup configuration at runtime (e.g., apply a "chat formatting" preset while the chat panel is open, remove it when it closes) without building individual styles.

### 3.8 RangeRule — Applying Modifiers Without Markup

RangeRule lets you apply a modifier to specific text ranges **programmatically**, without any tags in the text itself.

#### Use Case: Apply to All Text

To apply a modifier to the entire text (e.g., make everything a specific color), use the range `".."`:

```csharp
// Shortest form — Style.WholeText:
uniText.AddStyle(Style.WholeText(new ColorModifier(), "#FF0000"));

// Explicit form:
var rangeRule = new RangeRule();
rangeRule.data.Add(new RangeRule.Data
{
    range = "..",          // ".." means the full text range
    parameter = "#FF0000"
});
uniText.AddStyle(new Style { Rule = rangeRule, Modifier = new ColorModifier() });
```

#### Range Syntax

RangeRule uses C#-style range notation:

| Range | Meaning |
|-------|---------|
| `".."` | Entire text (start to end) |
| `"0..10"` | Codepoints 0 through 9 |
| `"5.."` | From codepoint 5 to end |
| `"..5"` | From start to codepoint 4 |
| `"2..^3"` | From codepoint 2 to 3 from end |
| `"^5.."` | Last 5 codepoints |

`RangeEx.WholeText` is the canonical `".."` constant, and `RangeEx.IsWholeText(expr)` accepts any equivalent form (`".."`, `"..^0"`, `"0.."`).

#### Multiple Ranges

```csharp
var rangeRule = new RangeRule();
rangeRule.data.Add(new RangeRule.Data { range = "0..5", parameter = "#FF0000" });
rangeRule.data.Add(new RangeRule.Data { range = "10..20", parameter = "#00FF00" });

uniText.AddStyle(new Style { Rule = rangeRule, Modifier = new ColorModifier() });
// Codepoints 0-4 are red, 10-19 are green
```

#### Practical Scenarios

| Scenario | Range | Modifier |
|----------|-------|----------|
| Bold the entire text | `".."` | BoldModifier |
| Highlight first word (5 chars) | `"0..5"` | ColorModifier with color parameter |
| Underline last 10 chars | `"^10.."` | UnderlineModifier |
| Apply size to specific range | `"3..8"` | SizeModifier with size parameter |

### 3.9 StringParseRule — Literal Pattern Matching

StringParseRule matches literal string patterns in text (no XML/HTML syntax):

```csharp
var emojiRule = new StringParseRule();
emojiRule.patterns = new[] { ":)", ":(", ":D" };
emojiRule.hasReplacement = true;
emojiRule.replacement = "😊";

uniText.AddStyle(new Style
{
    Rule = emojiRule,
    Modifier = new EmptyModifier()  // no visual effect, just replacement
});
// ":)" in text gets replaced with "😊"
```

### 3.10 CompositeParseRule — Combining Rules

CompositeParseRule groups multiple rules into one. It tries child rules in order and returns the first match:

```csharp
var composite = new CompositeParseRule();
composite.rules.Add(new TagRule { tagName = "link" }); // <link=url>text</link>
composite.rules.Add(new MarkdownLinkParseRule()); // [text](url)
composite.rules.Add(new RawUrlParseRule());       // auto-detect https://...

uniText.AddStyle(new Style
{
    Rule = composite,
    Modifier = new LinkModifier()
});
// All three link syntaxes work with a single modifier
```

### 3.11 Priority System

Parse rules have a `Priority` property that controls matching order (higher = matched first):

| Priority | Use Case | Example |
|----------|----------|---------|
| Positive (e.g., 10) | Explicit markup should match before anything else | Custom rules |
| 0 (default) | Standard tag-based and markdown rules | TagRule, MarkdownWrapRule, MarkdownLinkParseRule |
| Negative (e.g., -100) | Auto-detection, should only match if nothing else did | RawUrlParseRule (-100) |

This prevents conflicts: `<link=url>https://example.com</link>` won't be double-matched by both TagRule and RawUrlParseRule.

### 3.12 Creating Custom Parse Rules

Implement `IParseRule` to create your own markup syntax:

```csharp
public interface IParseRule
{
    int Priority => 0;
    bool IsStandalone => false;   // true = register without a modifier (protection rules)
    int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results);
    void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results) { }
    void Reset() { }
}
```

**Simplest approach — use TagRule:**

If your syntax follows the `<tag>content</tag>` pattern, use the built-in `TagRule` with a custom tag name — no subclassing needed:

```csharp
// In Inspector: add a TagRule, set tagName = "highlight"
// Now <highlight=yellow>text</highlight> works automatically
```

Parameters are always optional. Self-closing is purely syntax-driven (`<tag/>` or `<tag=value/>`).

### 3.13 Creating Custom Modifiers

UniText has several modifier base classes for different use cases:

#### Pattern 1: Text Transformation (BaseModifier)

For modifiers that transform codepoints before rendering (like uppercase):

```csharp
[Serializable]
public class LowercaseModifier : BaseModifier
{
    protected override void OnEnable() { }
    protected override void OnDisable() { }
    protected override void OnDestroy() { }

    protected override void OnApply(int start, int end, string parameter)
    {
        var codepoints = buffers.codepoints.data;
        var count = buffers.codepoints.count;
        var clampedEnd = Math.Min(end, count);

        for (var i = start; i < clampedEnd; i++)
            codepoints[i] = char.ToLowerInvariant((char)codepoints[i]);
    }
}
```

#### Pattern 2: Per-Glyph Visual Effect (GlyphModifier\<T\>)

For modifiers that change glyph appearance during mesh generation (color, underline, etc.):

```csharp
[Serializable]
public class HighlightModifier : GlyphModifier<byte>
{
    [SerializeField] private Color highlightColor = Color.yellow;

    protected override string AttributeKey => "highlight";

    protected override Action GetOnGlyphCallback() => OnGlyph;

    protected override void DoApply(int start, int end, string parameter)
    {
        var buffer = attribute.buffer.data;
        buffer.SetFlagRange(start, Math.Min(end, buffers.codepoints.count));
    }

    private void OnGlyph()
    {
        var gen = uniText.MeshGenerator;
        if (!attribute.buffer.data.HasFlag(gen.currentCluster))
            return;

        var colors = gen.Colors;
        var baseIdx = gen.faceBaseIdx;   // stable index of the face quad for this glyph
        colors[baseIdx] = colors[baseIdx + 1] =
        colors[baseIdx + 2] = colors[baseIdx + 3] = highlightColor;
    }
}
```

> Use `gen.faceBaseIdx` to address the current glyph's face quad. Never use `gen.vertexCount - 4` — other modifiers can append geometry before your `onGlyph` runs and shift the last-four assumption.

#### Pattern 3: Effect Quads (EffectModifier)

For effects like outline, shadow, glow — duplicate geometry rendered behind/ahead of the face, painted per effect:

```csharp
[Serializable]
public class MyGlowModifier : EffectModifier
{
    [SerializeField] private Color glowColor = Color.cyan;
    [SerializeField] private float dilate = 0.3f;

    protected override void OnGlyphEffect()
    {
        var gen = uniText.MeshGenerator;
        if (gen.font.IsColor) return;                // skip emoji

        var baseIdx = gen.faceBaseIdx;
        var packed = EffectPacking.PackColor(glowColor);
        EnqueueEffectQuad(
            baseIdx,
            new Vector4(dilate, packed.x, packed.y, 0f),
            expandDelta: 0f);
    }
}
```

`EnqueueEffectQuad` records a request for an extra quad that renders behind the face in registration order. All outline-modifier quads render before all shadow-modifier quads, which render before the face — painter order is grouped per modifier, not per glyph.

#### Pattern 4: Sub-mesh With Its Own Material (SubMeshModifier)

For effects that need a separate `Material` / shader (like `MaterialModifier`). Inherit `SubMeshModifier` and override `ShouldIncludeCurrentGlyph`, `GetMaterialForSlot`, `GetRenderOrder`, `GetSortIndex` — see `MaterialModifier.cs` for a full reference.

#### Pattern 5: Interactive Region (InteractiveModifier)

For clickable/hoverable text regions:

```csharp
[Serializable]
public class HashtagModifier : InteractiveModifier
{
    public override string RangeType => "hashtag";
    public override int Priority => 50;

    public event Action<string> HashtagClicked;

    protected override void OnApply(int start, int end, string parameter)
    {
        AddRange(start, end, parameter); // Register clickable region
    }

    protected override void HandleRangeClicked(InteractiveRange range, TextHitResult hit)
    {
        HashtagClicked?.Invoke(range.data);
    }

    protected override void HandleRangeEntered(InteractiveRange range, TextHitResult hit) { }
    protected override void HandleRangeExited(InteractiveRange range) { }
}
```

#### Modifier Lifecycle

```
SetOwner(uniText)           <- attached to component
    |
Prepare()                   <- lazy init on first Apply (allocate buffers)
    |
PrepareForParallel()        <- cache main-thread-only values before worker threads
    |
Apply(start, end, param)    <- called per matched range (calls OnApply)
    |
OnDisable()                 <- text changed, unsubscribe from events
    |
OnDestroy()                 <- component destroyed, release all resources
```

#### Best Practices for Custom Modifiers

- **No `new T[]` at runtime** — use `UniTextArrayPool<T>.Rent/Return` or `buffers.GetOrCreateAttributeData<T>()`
- **Subscribe in OnEnable, unsubscribe in OnDisable** — prevents stale callbacks
- **Use `PrepareForParallel()`** for anything that calls a Unity API (`Material.GetFloat()`, transform reads, etc.)
- **Address the face quad via `gen.faceBaseIdx`**, not `gen.vertexCount - 4`
- **Skip color (emoji) glyphs in effects** — `if (gen.font.IsColor) return;`

---

## 4. Interactive Text

UniText provides built-in support for clickable regions, hover detection, and visual feedback. Everything in this section works for both `UniText` (Canvas) and `UniTextWorld` (world-space) — only the raycasting setup differs (see §4.4 for world-space).

### 4.1 Click and Hover Events

```csharp
// Any text click
uniText.TextClicked += hit => Debug.Log($"Clicked cluster: {hit.cluster}");

// Interactive range events (links, custom ranges)
uniText.RangeClicked += hit => Debug.Log($"Clicked: {hit.range.data}");
uniText.RangeEntered += hit => Debug.Log($"Hover enter: {hit.range.data}");
uniText.RangeExited += hit => Debug.Log($"Hover exit: {hit.range.data}");

// Continuous hover tracking
uniText.HoverChanged += hit => Debug.Log($"Hover at cluster: {hit.cluster}");
```

### 4.2 Hit Testing

For custom interaction logic:

```csharp
// Local space
TextHitResult hit = uniText.HitTest(localPosition);

// Screen space
TextHitResult hit = uniText.HitTestScreen(screenPosition, eventCamera);

// Get visual bounds for a cluster range
var bounds = new List<Rect>();
uniText.GetRangeBounds(startCluster, endCluster, bounds);
```

### 4.3 Text Highlighter

The `Highlighter` property controls visual feedback — clicks, hover, and programmatic selection. It lives on `UniTextBase`, so it works identically on Canvas and world-space text.

The built-in `DefaultTextHighlighter` provides click flash (with fade-out), hover tint, and selection highlight:

```csharp
if (uniText.Highlighter is DefaultTextHighlighter highlighter)
{
    highlighter.ClickColor = new Color(1, 0, 0, 0.5f);
    highlighter.HoverColor = new Color(0, 0, 1, 0.1f);
    highlighter.SelectionColor = new Color(0.3f, 0.6f, 1f, 0.3f);
    highlighter.FadeDuration = 0.5f;

    // Programmatic selection (e.g., for searching, cursor, etc.)
    highlighter.SetSelection(startCluster: 10, endCluster: 20);
    highlighter.ClearSelection();
}

// Disable highlighting entirely
uniText.Highlighter = null;
```

#### Custom Highlighters

Extend `TextHighlighter` (or `DefaultTextHighlighter` to keep its click/hover/selection logic). The two `CreateHighlightRenderer` overloads — one taking `UniText`, one taking `UniTextWorld` — are the type-safe extension points: override either or both to plug a custom visual on the chosen backend. Inside event handlers, call `CreateHighlightRenderer(name, order)` (no owner argument) — it dispatches to the correct typed overload based on the actual owner.

```csharp
public class MyHighlighter : TextHighlighter
{
    private TextHighlightRenderer myRenderer;

    protected override TextHighlightRenderer CreateHighlightRenderer(UniText owner, string name, HighlightOrder order)
        => new MyCanvasRenderer(owner, name, order);   // your custom Canvas-side visual

    protected override TextHighlightRenderer CreateHighlightRenderer(UniTextWorld owner, string name, HighlightOrder order)
        => new MyWorldRenderer(owner, name, order);    // your custom mesh-based visual

    public override void OnRangeClicked(InteractiveRange range, List<Rect> bounds)
    {
        myRenderer ??= CreateHighlightRenderer("MyHighlight", HighlightOrder.Behind);
        myRenderer.Color = Color.yellow;
        myRenderer.SetRects(bounds);   // rects are in text-local space
    }

    public override void Destroy()
    {
        myRenderer?.Destroy();
        myRenderer = null;
        base.Destroy();
    }
}
```

To customize only the visual on one backend while keeping the default click flash / hover / selection logic, subclass `DefaultTextHighlighter` and override only the relevant `CreateHighlightRenderer` overload(s).

`HighlightOrder.Behind` renders below the text (selection, hover glow), `HighlightOrder.Above` renders above it (click flash, cursor).

### 4.4 World-Space Pointer Routing (`UniTextWorldRaycaster`)

Canvas text receives `EventSystem` pointer events automatically through the Canvas's `GraphicRaycaster`. For world-space text, add a `UniTextWorldRaycaster` component to the camera that should pick up pointer events:

```csharp
var camera = Camera.main;
camera.gameObject.AddComponent<UniTextWorldRaycaster>();
```

The raycaster is **not added automatically** — pick the camera explicitly. If a `UniTextWorld` with `RaycastTarget = true` enters a play-mode scene without any `UniTextWorldRaycaster`, a one-time warning is logged with the same instruction.

Properties:

- **BlockingObjects** (`None` / `TwoD` / `ThreeD` / `All`) — physical geometry that should occlude clicks between the camera and the text. Leave as `None` if the scene already has a `PhysicsRaycaster` / `Physics2DRaycaster` on the same camera (Unity's `EventSystem` distance-sorts across raycasters automatically).
- **BlockingMask** — layer mask used when `BlockingObjects` is non-None.

Per-instance opt-out: `UniTextWorld.RaycastTarget` (default true). Set to false on purely decorative text — the raycaster skips it entirely, the same way Canvas `Graphic.raycastTarget = false` works for `UniText`.

Once the raycaster is on the camera, `UniTextWorld` receives the same events as `UniText`: `TextClicked`, `RangeClicked`, `RangeEntered`, `RangeExited`, `HoverChanged`, plus link / hashtag / custom interactive range events.

### 4.5 Text Resolver (`IUniTextResolver`)

The resolver hook substitutes a component's source text *before* parsing / shaping / layout, **without writing to the serialized `text` field**. Scenes and prefabs stay clean — ideal for editor-time localization preview, template expansion, or runtime key-to-string binding.

```csharp
public class LocalizationResolver : IUniTextResolver
{
    private UniTextBase owner;
    private Action<string> onLanguageChanged;

    private Dictionary<string, string> table;

    public void OnAttached(UniTextBase owner)
    {
        this.owner = owner;
        onLanguageChanged = _ => owner.SetDirty(UniTextDirtyFlags.Text);
        LocalizationSignal.LanguageChanged += onLanguageChanged;
    }

    public void OnDetached(UniTextBase owner)
    {
        if (onLanguageChanged != null)
            LocalizationSignal.LanguageChanged -= onLanguageChanged;
        onLanguageChanged = null;
        this.owner = null;
        table = null;
    }

    public void PrepareForParallel()
    {
        // Cache main-thread-only values here — TryResolve below may run off-thread.
        table = LocalizationTables.GetTable(LocalizationSignal.CurrentLanguage);
    }

    public bool TryResolve(ReadOnlyMemory<char> source, out ReadOnlyMemory<char> result)
    {
        var key = source.ToString();
        if (table != null && table.TryGetValue(key, out var translated))
        {
            result = translated.AsMemory();
            return true;
        }
        result = default;
        return false;
    }
}

uniText.TextResolver = new LocalizationResolver();
uniText.Text = "greeting.hello";   // serialized key; rendered as the localized translation

// Later, to detach:
uniText.TextResolver = null;       // OnDetached is called automatically, signal is unsubscribed
```

Always implement `OnDetached` if you subscribe to anything in `OnAttached` — the resolver stays alive until GC collects it, and an orphan subscription keeps the owner reference around and fires `SetDirty` on a destroyed component.

`TryResolve` may run on a worker thread — don't call Unity APIs directly inside it; populate caches in `PrepareForParallel` and read them from `TryResolve`. To know whether a resolver is currently active, inspect `uniText.TextOverride & TextOverrideSource.Resolver`.

---

## 5. Language & Internationalization

UniText routes a BCP 47 language tag through the shaping pipeline. Three things depend on this tag:

1. **OpenType `locl` feature** — pan-CJK fonts (Noto Sans CJK, Source Han Sans, etc.) render the correct regional form for Han ideographs depending on whether the text is tagged Simplified Chinese, Traditional Chinese, Japanese, or Korean.
2. **`FontFamily.preferredLanguage`** — during codepoint-to-font resolution, families whose `preferredLanguage` matches the current tag are preferred over the normal fallback order. Useful for holding SC/TC/JP/KR cuts in one stack.
3. **Any custom modifier** that reads per-codepoint language via `AttributeKeys.Language`.

### 5.1 Three places to set the language

| Scope | API | Wins over |
|-------|-----|-----------|
| Per-range | `LanguageModifier` via `<lang=...>...</lang>` or `Style.Tag` / `Style.Range` / `Style.WholeText` | Everything below |
| Per-component | `uniText.Language = "zh-Hans"` | Project-wide default |
| Project-wide | `UniTextSettings.Language` (**Project Settings > UniText > Localization > Language**) | (base) |

`UniText.Language` is a runtime shortcut: the setter finds or creates a whole-text `LanguageModifier` style in the component's local Styles list. There's no serialized inspector field — components that never set a language see nothing extra.

```csharp
uniText.Language = "zh-Hant";   // whole text
uniText.Language = null;        // clear — back to component/project default
```

### 5.2 Per-range language in markup

```csharp
// Register the modifier once (either directly, via a preset, or on a Style Preset asset):
uniText.AddStyle(Style.Tag(new LanguageModifier(), "lang"));

// Then in text:
uniText.Text = "日本語: <lang=ja>骨</lang>, 中文简: <lang=zh-Hans>骨</lang>, 中文繁: <lang=zh-Hant>骨</lang>";
```

Itemization splits runs on language boundaries, so each run shapes with its own OpenType language tag.

### 5.3 Picking the right font family by language

Attach `preferredLanguage` to each region-specific family in one stack:

```
CJKStack.asset
├── Family: NotoSansCJK-SC   (preferredLanguage: "zh-Hans")
├── Family: NotoSansCJK-TC   (preferredLanguage: "zh-Hant")
├── Family: NotoSansCJK-JP   (preferredLanguage: "ja")
└── Family: NotoSansCJK-KR   (preferredLanguage: "ko")
```

With `UniText.Language = "zh-Hans"`, codepoints are resolved against the SC family first; unmatched codepoints fall through the normal chain as usual. A matching family wins over the default fallback order for that codepoint.

### 5.4 Naming families (`FontFamily.name` + `FontModifier`)

You can give each family a user-facing name and address it from markup or code:

```
UIStack.asset
├── Family: name="body"   primary: Inter-Regular
├── Family: name="pixel"  primary: PressStart2P
└── Family: name="icons"  primary: MyIconFont
```

```csharp
uniText.AddStyle(Style.Tag(new FontModifier(), "font"));
uniText.Text = "Score: <font=pixel>100</font> <font=icons>♥</font>";
```

A matched name wins over both `preferredLanguage` selection and the default fallback chain. If the chosen family doesn't have a glyph for a particular codepoint, the normal fallback chain still kicks in for that codepoint. Unknown names log a one-time warning.

---

## 6. Custom Materials & Shaders

`MaterialModifier` applies an arbitrary `Material` to a text range by emitting a dedicated sub-mesh. Use it for dissolve effects, hologram shaders, flame text, custom SDF looks, anything a shader can do.

### 6.1 Quick start — use a ready material

UniText ships example materials in `UniText/Defaults/Materials/`:

| Material | Effect |
|----------|--------|
| `UniTextLit` | World-space lit SDF (ambient + directional light + fog) |
| `UniTextEmojiLit` | World-space lit emoji |
| `UniTextHologram` | Scanlines + flicker + edge glow |
| `UniTextDisolve` | Noise-driven dissolve |
| `UniTextRainbow` | Animated color cycle |

Set up a `MaterialModifier` in the inspector — paired with a `TagRule` whose `tagName` you choose (`mat` is the convention used here) — and point its `Material` field at one of these. From code:

```csharp
var mat = new MaterialModifier { Material = myDissolveMaterial };
uniText.AddStyle(Style.Tag(mat, "mat"));     // pick any name; "mat" is just the convention

uniText.Text = "Hello <mat>dissolving</mat> world!";
```

For `UniTextWorld`, you can also assign these materials as the component's base material instead of using `MaterialModifier` (useful for whole-text effects, no tag setup required).

### 6.2 Authoring your own shader

Use the asset creation menu:

**Assets > Create > UniText > Custom Material Shader**

This scaffolds a new `.shader` file pre-wired for `MaterialModifier` — includes `UniText_Custom.cginc`, binds `_MainTex` as the glyph atlas `Texture2DArray`, exposes the standard UV layout UniText writes. Rename it, tweak the fragment function, you're done.

Three example shaders ship as starting points (in `UniText/Shaders/Templates/Examples/`):

- `UniText/Custom/Dissolve`
- `UniText/Custom/Hologram`
- `UniText/Custom/Rainbow`

### 6.3 Compose modes

`MaterialModifier.renderOrder` controls how the custom material composes with the base text pass on the range:

| Mode | Effect |
|------|--------|
| `Replace` (default) | Base SDF pass is suppressed on the range (face alpha zeroed); only the custom material renders |
| `Over` | Custom material renders in front of the base text |
| `Under` | Custom material renders behind the base text |

**Ordering note (Replace mode):** `Replace` zeroes the face alpha during the `onGlyph` callback. UniText invokes `onGlyph` subscribers in the order styles appear in the component's Styles list. If a `ColorModifier` / `GradientModifier` comes *after* `MaterialModifier`, it will overwrite the zeroed alpha and make the base face reappear. Place `MaterialModifier` **after** any color-writing modifiers.

### 6.4 Per-text and per-glyph shader data

- **Per-text constants** — `ConstantUv2` / `ConstantUv3` (`Vector4` each) are written into `TEXCOORD2` / `TEXCOORD3` of every glyph vertex in this modifier's sub-mesh. Animate them at runtime without touching `Material.Set*` (which would affect every component sharing the cached material clone):

  ```csharp
  var mat = GetComponent<MyMaterialAnimator>().mod; // your MaterialModifier reference
  mat.ConstantUv2 = new Vector4(Time.time, 0, 0, 0);
  ```

- **Per-glyph writer** — set `glyphDataWriter` (a `MaterialGlyphWriter` delegate) to compute `uv2` / `uv3` per glyph at sub-mesh build time. Useful for staggered effects (wave, cascade, per-character dissolve).

- **Emoji material slot** — `emojiMaterial` accepts a separate material for emoji glyphs in the range. Leave null and emoji render through the base emoji pass (the modifier does nothing for them).

### 6.5 Noise texture generator

Many custom shaders need noise textures. **Tools > UniText > Noise Generator** produces seamless grayscale value-noise / FBM PNG assets (64–1024 px, configurable seed / frequency / octaves / lacunarity / gain / invert / tileable). The shipped Dissolve and Hologram examples use this.

### 6.6 Lit shaders for world-space text

Two SDF/emoji shader variants with lighting are provided for 3D scenes:

- `UniText/Lit/SDF` — SDF text that picks up ambient + one directional light + fog
- `UniText/Lit/Emoji` — same but for emoji

Assign them via `UniTextWorld`'s material or through `MaterialModifier`. `_LightInfluence` controls the mix between unlit and fully lit.

---

## 7. RTL and Bidirectional Text

UniText automatically handles:
- **RTL scripts** (Arabic, Hebrew) — text flows right-to-left
- **BiDi mixing** — "Hello עולם World" renders correctly
- **Complex shaping** — Arabic ligatures, Indic conjuncts, etc. (via HarfBuzz)

### Direction Settings

- **Auto** (default) — detects from first strong directional character
- **LeftToRight** — force left-to-right
- **RightToLeft** — force right-to-left

```csharp
uniText.BaseDirection = TextDirection.Auto;
uniText.Text = "مرحبا بالعالم"; // Renders right-to-left
```

---

## 8. Emoji

Emoji work automatically — the system emoji font is detected and used:

```csharp
uniText.Text = "Hello! 👋 Great job! 🎉";
```

| Platform | Emoji Font |
|----------|------------|
| Windows | Segoe UI Emoji |
| macOS | Apple Color Emoji |
| iOS | Core Text (native API) |
| Android | NotoColorEmoji (via fonts.xml) |
| Linux | NotoColorEmoji / Symbola |
| WebGL | Browser Canvas 2D |

Emoji are rendered as color bitmaps in a separate atlas. The emoji font is checked first for emoji-presentation codepoints, then falls back to the regular font stack.

---

## 9. Text Model

When you read `uniText.Text`, you see the serialized authored value — what's stored on disk. What's actually drawn can be different. Five properties cover the full pipeline from authoring to rendering:

| Property | Type | What it is |
|----------|------|------------|
| `Text` | `string` | Serialized authored value (setter persists into the scene/prefab) |
| `RawText` | `ReadOnlyMemory<char>` | Runtime source — `Text`, or the buffer passed to `SetText`, before any resolver |
| `ResolvedText` | `ReadOnlyMemory<char>` | Resolver's substitute from the last rebuild, or empty if none |
| `RenderedText` | `ReadOnlyMemory<char>` | What actually goes through shaping/layout: resolver output if active, else `RawText` |
| `CleanText` | `ReadOnlySpan<char>` | `RenderedText` with markup stripped |
| `TextOverride` | `TextOverrideSource` flags | Tells you which runtime source(s) currently diverge from `Text`: `None`, `SetText`, `Resolver`, or a combination |

Everything except `Text` is zero-allocation. `CleanText`'s backing buffer is pooled and may be rewritten on the next rebuild — copy to a string via `new string(span)` if you need to keep it.

### 9.1 Assigning text at runtime

Three ways:

```csharp
// 1) Standard — writes to the serialized field (scene/prefab becomes dirty).
uniText.Text = "Hello";

// 2) Zero-alloc buffer assignment — does NOT touch the serialized field, no dirty flag.
char[] buffer = ...;
uniText.SetText(buffer, offset: 0, length: 5);

// 3) Zero-alloc memory assignment — same semantics as (2).
ReadOnlyMemory<char> mem = "Hello".AsMemory();
uniText.SetText(mem);
uniText.SetText("Hello");   // convenience overload (null → empty)
```

After a `SetText(buffer, ...)` call, the `Text` getter returns the *serialized* value, not the buffer. Read `RawText` (or `RenderedText`) to see what the component actually holds.

---

## 10. Common Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | string | `""` | Text content with optional markup |
| `FontStack` | UniTextFontStack | — | Font collection with font families and fallback chain |
| `RenderMode` | UniTextRenderMode | SDF | SDF (single-channel) or MSDF (multi-channel) |
| `FontSize` | float | 36 | Base font size in points |
| `color` | Color | white | Base text color |
| `BaseDirection` | TextDirection | Auto | LTR, RTL, or Auto |
| `WordWrap` | bool | true | Enable/disable word wrapping |
| `HorizontalAlignment` | HorizontalAlignment | Left | Left, Center, Right |
| `VerticalAlignment` | VerticalAlignment | Top | Top, Middle, Bottom |
| `AutoSize` | bool | false | Auto-fit text to container |
| `MinFontSize` | float | 10 | Auto-size minimum |
| `MaxFontSize` | float | 72 | Auto-size maximum |
| `Language` | string | null | Whole-text BCP 47 language tag (shortcut over `LanguageModifier`) |
| `Highlighter` | TextHighlighter | DefaultTextHighlighter | Interaction visual feedback |
| `TextResolver` | IUniTextResolver | null | Hook that overrides source text before parsing |

Additional on `UniTextWorld`:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SortingOrder` | int | 0 | OrderInLayer for batching/sorting |
| `SortingLayerID` | int | 0 (Default) | Sorting layer for batching/sorting |

### Read-Only Properties

Text pipeline stages (in order — each row reads its predecessor):

| Property | Type | Description |
|----------|------|-------------|
| `RawText` | ReadOnlyMemory\<char\> | Runtime input — `Text` or the buffer passed to `SetText`, before any resolver |
| `ResolvedText` | ReadOnlyMemory\<char\> | Resolver substitute from the last rebuild, or empty |
| `RenderedText` | ReadOnlyMemory\<char\> | What goes through shaping/layout: resolver output if active, else `RawText` |
| `CleanText` | ReadOnlySpan\<char\> | `RenderedText` with markup stripped. Backing buffer is pooled — don't store the span |
| `TextOverride` | TextOverrideSource \[Flags\] | `None`, `SetText`, `Resolver`, or a combination — the runtime source(s) overriding `Text` |
| `CurrentFontSize` | float | Effective font size (after auto-sizing) |
| `ResultSize` | Vector2 | Computed text dimensions |
| `ResultGlyphs` | ReadOnlySpan\<PositionedGlyph\> | All positioned glyphs after layout |

### Events

| Event | Type | Description |
|-------|------|-------------|
| `TextClicked` | Action\<TextHitResult\> | Any text click |
| `RangeClicked` | Action\<InteractiveRangeHit\> | Interactive range clicked |
| `RangeEntered` | Action\<InteractiveRangeHit\> | Pointer enters interactive range |
| `RangeExited` | Action\<InteractiveRangeHit\> | Pointer exits interactive range |
| `HoverChanged` | Action\<TextHitResult\> | Pointer moved over text |
| `Rebuilding` | Action | Before text rebuild |
| `RectHeightChanged` | Action | RectTransform height changed |

`UniTextWorld` additionally raises `RenderDataAvailable` / `RenderDataCleared` / `SortingChanged` / `ParentChanged` (per-instance) and static `Activated` / `Deactivated`.

---

## 11. Code Examples

### Basic Usage

```csharp
public class Example : MonoBehaviour
{
    [SerializeField] private UniText uniText;

    void Start()
    {
        uniText.Text = "Hello, World!";
        uniText.FontSize = 24;
        uniText.HorizontalAlignment = HorizontalAlignment.Center;
    }
}
```

### Clickable Links

```csharp
private LinkModifier linkModifier;

void Start()
{
    linkModifier = new LinkModifier();
    linkModifier.AutoOpenUrl = false;
    uniText.AddStyle(Style.Tag(linkModifier, "link"));

    uniText.Text = "Visit <link=https://example.com>our website</link> for more info.";

    linkModifier.LinkClicked += url => Application.OpenURL(url);
    linkModifier.LinkEntered += url => Debug.Log($"Hovering: {url}");
    linkModifier.LinkExited += () => Debug.Log("Left link");
}
```

### Markdown Links and Auto-URL Detection

```csharp
uniText.AddStyle(new Style { Modifier = new LinkModifier(), Rule = new MarkdownLinkParseRule() });
uniText.Text = "Visit [our website](https://example.com) for details.";

uniText.AddStyle(new Style { Modifier = new LinkModifier(), Rule = new RawUrlParseRule() });
uniText.Text = "Check https://example.com for updates.";
```

### Inline Objects (Icons in Text)

```csharp
// Requires: ObjModifier + TagRule("obj") registered
// ObjModifier must have an InlineObject named "coin" with a RectTransform prefab
uniText.Text = "You earned <obj=coin/> 100 gold!";
```

### Lists

```csharp
// With MarkdownListParseRule + ListModifier registered:
uniText.Text = "Shopping list:\n- Apples\n- Bananas\n- Oranges";

// Ordered list:
uniText.Text = "Steps:\n1. Open app\n2. Click button\n3. Done";
```

### Apply Color to Entire Text (RangeRule)

```csharp
uniText.AddStyle(Style.WholeText(new ColorModifier(), "#FF6600"));
uniText.Text = "This entire text is orange.";
```

### Whole-text via component API

```csharp
uniText.SetWholeText<BoldModifier>();                // make everything bold
uniText.SetWholeText<ColorModifier>("#FF0000");      // everything red
bool isBold = uniText.ToggleWholeText<BoldModifier>();
uniText.ClearWholeText<ColorModifier>();
```

### Language and font switching

```csharp
// Project-wide default
UniTextSettings.Language = "zh-Hans";

// Per-component
uniText.Language = "ja";

// Per-range (requires LanguageModifier registered):
uniText.AddStyle(Style.Tag(new LanguageModifier(), "lang"));
uniText.Text = "日: <lang=ja>骨</lang>  中: <lang=zh-Hans>骨</lang>";

// Named font families (requires FontModifier registered):
uniText.AddStyle(Style.Tag(new FontModifier(), "font"));
uniText.Text = "Score: <font=pixel>100</font>";
```

### Emoji

```csharp
uniText.Text = "Hello! 👋 Great job! 🎉";
```

### World-Space text

```csharp
public class WorldLabel : MonoBehaviour
{
    [SerializeField] private UniTextWorld label;

    void Start()
    {
        label.Text = "Target <color=red>acquired</color>";
        label.SortingOrder = 10;
        label.FontSize = 48;

        label.RangeClicked += hit => Debug.Log($"Clicked: {hit.range.data}");
    }
}
// Make sure Camera.main has a UniTextWorldRaycaster (added automatically by the menu).
```

### Custom Material via `MaterialModifier`

```csharp
var mat = new MaterialModifier { Material = myDissolveMaterial };
uniText.AddStyle(Style.Tag(mat, "mat"));

uniText.Text = "Attacked: <mat>*HIT*</mat>";

// Animate a shader parameter (e.g., dissolve progress) via the per-text UV:
void Update()
{
    mat.ConstantUv2 = new Vector4(Mathf.PingPong(Time.time, 1f), 0, 0, 0);
}
```
