# Changelog

All notable changes to UniText will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.5] - 2026-04-29

### Added

- **`UniTextWorld.RaycastTarget`** (default `true`, inspector + property): turn off on purely decorative world-space text and the camera's `UniTextWorldRaycaster` skips it entirely, mirroring Canvas `Graphic.raycastTarget`.
- **One-time warning when an interactive `UniTextWorld` plays in a scene without a `UniTextWorldRaycaster`**: instead of pointer events silently doing nothing, a single Console warning points at the camera that needs the raycaster (or at `RaycastTarget = false` for decorative text).
- **`UniTextBase.CollectRangeEntries(int, int, PooledList<LineRangeEntry>)`** + public `LineRangeEntry` struct: one entry per contiguous glyph run within a line for a cluster range, with X clamped to the line's visible content extent — usable by custom modifiers and tools that need glyph-accurate spans, not just bounding rects.
- **`TextLine.glyphStart` / `glyphCount` / `widthPx` / `IsRtl`** (public): the positioned-glyph range and mesh-local content width are now exposed on each line for custom modifiers reading layout output, along with a paragraph-direction flag from the BiDi level.
- **`CanvasHighlightRenderer` and `WorldHighlightRenderer` are now subclassable** (public): plug a custom Canvas-side or world-space highlight visual without reimplementing the lifecycle.
- **Type-safe per-backend `TextHighlighter` extension points**: subclasses now override `CreateHighlightRenderer(UniText, ...)` and / or `CreateHighlightRenderer(UniTextWorld, ...)` to plug a custom visual on the chosen backend; subclassing `DefaultTextHighlighter` keeps its click / hover / selection logic and only swaps the visual.

### Changed

- **`UniTextBase.CreateHighlightRenderer(string, HighlightOrder)` removed** (breaking for custom highlighter authors): the abstract owner-side hook is gone — implement the typed `TextHighlighter.CreateHighlightRenderer(UniText, ...)` / `CreateHighlightRenderer(UniTextWorld, ...)` overloads instead, and call the protected untyped `CreateHighlightRenderer(name, order)` from event handlers.
- **`TextHighlighter.OnSelectionChanged` removed** (breaking for custom highlighter authors): drive selection visuals from your own state — `DefaultTextHighlighter.SetSelection` shows the intended pattern.
- **`GameObject > UI (World) > UniText > World Text` no longer auto-adds `UniTextWorldRaycaster` to `Camera.main`**: pick the camera explicitly; the new in-scene warning will tell you when it's missing.
- **Double-line decoration**: `<u double>` / `<s double>` now renders each sub-line at the full requested thickness with a same-thickness gap (was: 35% / 30% / 35% summing to the requested thickness), making double underlines and strikethroughs noticeably bolder.
- **Underline / strikethrough end-caps scale with the line's thickness instead of the underscore-glyph height**: thinner lines get proportionally narrower caps for cleaner edges; thick lines keep close to the previous look.

### Fixed

- **AutoSize text shrunk and didn't grow back when the rect grew taller**: after the shrink-to-fit pass reduced the effective font size to fit the height, increasing the rect's height (without changing the width) left the text shrunken until the width changed.
- **Thick underlines / strikethroughs clipped at the top and bottom edges**: when the requested line thickness exceeded the underscore glyph's natural rendered height, SDF sampling fell off the glyph and lost ink near the top / bottom of the line; the sampled region now grows with the requested thickness.
- **Dotted and dashed underlines / strikethroughs drew a partial last mark past the line end**: the pattern loop now stops at the last mark that fits entirely within the segment.

## [2.1.4] - 2026-04-29

### Added

- **Underline / strikethrough styles**: `UnderlineModifier` and `StrikethroughModifier` accept a 5-field parameter — thickness (em or px), offset (em or px), style (`solid`, `double`, `dotted`, `dashed`), skip-ink (line breaks around descenders like g, j, p, q, y), and overlay (line draws above the text instead of behind it); bare `<u>`/`<s>` still defaults to a solid line at the font's metrics.
- **Scene Visibility opt-out**: a Scene view overlay and `Tools > UniText > Respect Scene Visibility` menu toggle whether hiding a UniText / UniTextWorld GameObject in the Hierarchy clears its rendered text (default: on; per-developer, stored in `EditorPrefs`).
- **`UniTextMeshGenerator.EffectPass`** + **`currentEffectPass`** (for custom effect modifiers): a modifier can place its duplicate quads above (`PostFace`) or below (`PreFace`) the face of the current glyph, so e.g. an outline modifier draws around an overlay decoration line on top of the text rather than behind it.
- **`UniTextMeshGenerator.isVirtualGlyph`** (for custom modifiers): the per-glyph callback now fires for modifier-injected quads (decoration lines, kashida, list markers); read this flag to skip them when only real shaped glyphs matter.
- **`UniTextMeshGenerator.QueueEffectTriangle`** + **`RequestBandUpgradeIfNeeded`** (for custom modifiers that emit their own quads): public helpers to route effect triangles through the shared pre/post-face buffer and to request a wider SDF tile for the current quad without duplicating internal logic.
- **`LineRenderHelper.DrawDot`** (for custom decoration modifiers): emits one bullet-shaped dot quad sampled from the font's bullet glyph (U+2022), with a stretched-underscore fallback when the font has no bullet.
- **`UniText/Lit/SDF` and `UniText/Lit/Emoji` now render under URP**: world-space lit text works in Universal Render Pipeline projects from URP 12 (Unity 2021.3 LTS) through URP 17 (Unity 6), receives main-light shadows and additional-light shadows, and uses Forward+ cluster lighting on URP 14+.
- **Lit shaders cast shadows**: world-space text using `UniText/Lit/SDF` or `UniText/Lit/Emoji` now contributes to shadow maps in both Built-in and URP — SDF silhouette is driven by glyph dilate (effect-mode outlines also cast their inflated shape), and emoji uses bitmap alpha with the new `_ShadowCutoff` material property.
- **Lit shaders react to nearby point and spot lights**: additional non-important point/spot lights now affect world-space lit text in both pipelines (up to 4 vertex-evaluated in Built-in; per-pixel with shadow attenuation in URP).

### Changed

- **`UniTextMeshGenerator.Current` removed** (breaking for custom modifiers): replace with the per-component instance `uniText.MeshGenerator`.
- **`UniTextMeshGenerator.onAfterPage` split** (breaking for custom modifiers) into `onMainPassComplete` (emit decoration geometry — also runs through the per-glyph pipeline) and `onMainPassFinalize` (effect modifier flushes); subscribe to whichever your previous handler was for.
- **`LineRenderHelper.DrawLine` signature** (breaking for custom decoration-line modifiers): now takes the generator, cluster, UV cap range, and an explicit thickness override; color is no longer a parameter — it flows through the per-glyph color / gradient / effect pipeline.

### Fixed

- **Parameter field reset when switching the inspector between objects sharing the same Style layout**: when two assets or components each had a Style at the same index (e.g. a `StylePreset` and a `UniText`), switching selection between them reset the newly-selected object's parameter field to the modifier's defaults.
- **`<b>` / `<i>` / `<var>` ignored the family chosen by `FontModifier`**: combining `<font=X>` with `<b>`, `<i>`, or `<var>` resolved the bold/italic face or variable axis from the fallback family instead of the family named by `FontModifier`, so e.g. `<font=Roboto><b>` could render Roboto Regular instead of Roboto Bold.
- **`UniTextWorld` reported infinite mesh bounds**: every world-space text shard reported a 2 km axis-aligned bounding box, breaking frustum culling, shadow caster volumes, and `Renderer.bounds` queries; bounds are now computed from the actual mesh vertices in each shard.
- **`UniTextWorld` ignored GameObject Layer**: world-space text drew through every camera regardless of culling masks, because the batcher merged all components into one shared layer; the batch now keys on the component's Layer (and re-routes when the Layer changes at runtime), so cameras honor their culling mask.
- **Underline, strikethrough, and Arabic kashida ignored per-glyph effects**: gradient, outline, shadow, and custom-material modifiers applied to text but skipped its decoration lines and kashida elongation; decoration geometry now runs through the same per-glyph pipeline and picks up all active modifiers uniformly.

## [2.1.3] - 2026-04-27

### Added

- **`ExtrudeModifier`**: adds a 3D extrude / bevel stack behind the text with a per-step color gradient from near to far, configurable offset, dilate, and softness; an optional bevel mode adds intermediate side-faces for chamfered depth. Step count and bevel toggle live on the modifier; tag parameter format: `offsetX,offsetY,#nearColor,#farColor,dilate,softness`.
- **`EffectModifier` per-layer flush hooks** (for custom multi-layer effect subclasses): `ApplyOwnRequests` is now `protected virtual` and `AppendSharedEffectQuad` is `protected static`, so a subclass can buffer its own per-layer requests and flush them in painter order across all glyphs instead of the default per-glyph order.

### Changed

- **`EffectPacking.PackColor` returns `Vector2`** (breaking for custom effect modifiers and shaders): packed color now occupies `texcoord2.y` and `texcoord2.z`, and custom shaders must call `UnpackColor(input.texcoord2.y, input.texcoord2.z)` — the single-float `UnpackColor(float)` overload is gone.
- **Color alpha is composited with the component's base alpha**: `<color=#RRGGBBAA>` ranges, gradient stops, and underline / strikethrough colours now multiply their alpha with the component alpha instead of discarding it, so `<color=#FF000080>` renders at 50% opacity (was: forced to component alpha). Use a fully opaque parameter to restore the previous look.
- **`LineHeightModifier`: single value parameter, no mode**: the inspector now shows one `Value` field, and `<lh=N>` always sets line height. Existing `<lh=h,N>` markup still parses unchanged; existing `<lh=s,N>` parses but now sets height — use `<lh=+N>` for the additive equivalent.
- **`Glyph Diagnostic` menu moved**: now under `Tools > UniText > Glyph Diagnostic` (was top-level `UniText > Glyph Diagnostic`).

### Fixed

- **Outline and shadow color randomly tinted on some GPUs**: the previous color packing produced NaN/Inf bit patterns that some drivers canonicalize at the vertex–fragment interpolator boundary, randomly altering the green channel as colors crossed certain thresholds.
- **Multiple `GradientModifier` instances on one component overwrote each other**: each instance kept a private 1-based gradient list and stomped the shared per-codepoint index buffer, so the second modifier silently replaced the first one's gradient assignments.
- **Parameter field stuck on stale values after changing the modifier type**: switching a `Style`'s `Modifier` (or a child of `CompositeModifier`) in the inspector now resets the parameter field to the new modifier's defaults instead of keeping the previous modifier's text.

## [2.1.2] - 2026-04-26

### Added

- **`UniTextBase.Animated` event**: raised after Unity Animator applies animated property values to a `UniText` / `UniTextWorld`; modifiers with their own animatable fields can subscribe, diff their state, and call `SetDirty` with the matching `UniTextDirtyFlags`.
- **`AnimationHandlerBase<T>`**: public base class for extending the built-in Animator diff with subclass-specific animatable fields when authoring a custom `UniTextBase` subclass.

### Fixed

- **Unity Animator did not update rendered text**: animating `fontSize`, `color`, `wordWrap`, `autoSize`, `minFontSize` / `maxFontSize`, `baseDirection`, `horizontalAlignment` / `verticalAlignment`, `overEdge` / `underEdge`, `leadingDistribution` — and on `UniTextWorld` also `sortingOrder` / `sortingLayerID` — silently had no visual effect.

## [2.1.1] - 2026-04-26

### Added

- **`IGradientProvider`** with three built-in implementations — `GlobalSettingsGradientProvider` (default, reads `UniTextSettings.Gradients`), `AssetGradientProvider` (per-modifier asset reference), `InlineGradientProvider` (inline list edited on the modifier itself); pick the source for each `GradientModifier` from the inspector.
- **Live gradient preview in the `GradientModifier` parameter dropdown**: each row shows the actual gradient swatch on the right and reflects the provider currently assigned to that modifier, not just the project-wide settings.
- **`GradientNotifier`**: static `AnyChanged` event raised when any gradient source visible to `GradientModifier` is edited (asset, inline list, or a custom provider invoking `NotifyChanged`); affected text rebuilds on the next frame without manual refresh.
- **Public Unicode character properties for modifier / parse-rule authors**: `UnicodeData.GetSimpleUppercase` / `GetSimpleLowercase` / `GetSimpleTitlecase`, `GetGeneralCategory` (+ public `GeneralCategory` enum), `GetScript`, `IsExtendedPictographic` / `IsEmojiPresentation` / `IsEmojiModifierBase`, `IsDefaultIgnorable` — backed by bundled UCD tables and identical across Mono, IL2CPP, and standard .NET.
- **`ParameterOption` + `ContextualParameterOptionsProvider`**: extension API for `[ParameterField("enum:@key")]` dropdowns — options can carry a display label, a per-row preview decorator, and a description, and can be derived from the owning modifier instance.

### Changed

- **`UppercaseModifier` / `LowercaseModifier` / `SmallCapsModifier` resolve case via the bundled Unicode case mapping table** instead of `char.ToUpper/LowerInvariant`; behavior is identical across Mono, IL2CPP, and standard .NET runtimes.

### Fixed

- **Emoji rendered as `.notdef` inside a `FontModifier` range**: emoji codepoints in a range covered by `FontModifier` were forced through the chosen text font, which has no emoji glyphs; emoji now always resolve to the emoji font regardless of any explicit font override.
- **`FontModifier` did not fall back to the FontStack chain**: codepoints not covered by the named family produced `.notdef` instead of falling through the standard fallback chain (as the docs already promised).
- **`UppercaseModifier` skipped Greek final sigma (U+03C2 ς)**: the last character of words like "πόνος" was left lowercase due to a runtime gap in Mono's case tables.

## [2.1.0] - 2026-04-25

### Added

- **Language-aware shaping (BCP 47 + OpenType `locl`)**: fonts with language-specific glyphs (pan-CJK like Noto Sans CJK / Source Han Sans) now render the correct regional forms. Apply per-range with `LanguageModifier`, per-component via `UniText.Language`, or project-wide via `UniTextSettings.Language`.
- **`FontModifier`**: override the font on a text range by referencing a `FontFamily.name` from the component's `UniTextFontStack`. A matched family wins over both `preferredLanguage` selection and the default fallback chain; the normal chain still kicks in for codepoints the chosen family can't render. Unknown names log a one-time warning.
- **Per-family language hint**: `FontFamily.preferredLanguage` — one font stack can hold region-specific cuts (SC/TC/JP/KR) and pick the right one automatically from the active language.
- **Named font families**: `FontFamily.name` lets you address a family directly from `FontModifier` or code instead of relying on fallback order.
- **World-space batcher shard size**: `UniTextSettings.WorldBatcherShardTargetVertexCount` to tune batching granularity vs. rebuild cost for dense world-space scenes.
- **Custom sub-mesh emission**: a modifier can now emit its own geometry with a custom material/atlas that renders `Under`, `Above`, or alongside the base text, ordered by a `sortIndex` — via `UniTextMeshGenerator.onCollectSubMeshes` and `UniTextRenderData`.
- **Quad expansion API**: `UniTextMeshGenerator.ExpandQuad` + `faceBaseIdx` + `DefaultSdfPadding` — a supported way for effect modifiers to grow a glyph quad so wide outlines / fake-bold / soft shadow don't clip at the quad edge.
- **Text-model properties on `UniTextBase`**: four zero-alloc views covering the full pipeline from authored text to what's drawn.
  - `Text` — the serialized authored value.
  - `RawText` (`ReadOnlyMemory<char>`) — the runtime source assigned via `Text`/`SetText` before any resolver substitution.
  - `RenderedText` (`ReadOnlyMemory<char>`) — what's actually fed into parsing/shaping/layout: the resolver's output if one is active, otherwise `RawText`.
  - `CleanText` (`ReadOnlySpan<char>`) — `RenderedText` with markup stripped.
  - `TextOverride` — flags (`SetText` / `Resolver`) indicating which runtime sources currently diverge from the serialized `Text`.
- **Text resolver hook (`IUniTextResolver` + `UniTextBase.TextResolver`)**: override a component's source text (localization preview, template expansion, key-to-string lookup) without writing to the serialized `text` field, so scenes and prefabs don't get marked dirty.
- **`SetText(ReadOnlyMemory<char>)` / `SetText(string)`**: assign text at runtime without writing to the serialized field and without marking the scene/prefab dirty.
- **`UniText.Language` property**: one-line way to apply a BCP 47 language to the whole text from code, without building a style manually.
- **Click / hover / selection highlighting on `UniTextWorld`**: the `Highlighter` slot now lives on `UniTextBase` and works unchanged on both Canvas and world-space text.
- **Custom highlighter API**: `TextHighlighter` subclasses can now target both Canvas and world-space text by requesting a backend-agnostic surface — `owner.CreateHighlightRenderer(name, HighlightOrder.Behind | Above)` returns a `TextHighlightRenderer` with `Color`, `SetRects(...)`, `Clear()`, `Destroy()`.
- **Style/modifier query and mutation API on `UniTextBase`**: `HasModifier<T>()`, `TryGetStyle<T>()`, `SetWholeText<T>(parameter)`, `ClearWholeText<T>()`, `ToggleWholeText<T>(parameter)`, `GetWholeTextParameter<T>()` and non-generic `Type` overloads. Replaces the manual `new Style { Rule = new RangeRule { data = ... }, Modifier = ... }` boilerplate for programmatic styling.
- **`UniTextWorld` public events + active registry**: static `Activated` / `Deactivated`, per-instance `RenderDataAvailable` / `RenderDataCleared` / `SortingChanged` / `ParentChanged`, and a `UniTextWorld.Active` list of currently enabled instances. Observe world-space text state without scene scans.
- **Click / hover on `UniTextWorld`**: add a `UniTextWorldRaycaster` component to a `Camera` and world-space text receives the same pointer events that worked on Canvas — `RangeClicked` / `RangeEntered` / `RangeExited`, link and hashtag events. No per-text colliders needed. Optional `BlockingObjects` setting to respect 2D/3D physical geometry as occluders.
- **`UniText` in Add Component menu**: discoverable under `UI (Canvas) > UniText` in the inspector's Add Component dropdown.
- **`MaterialModifier`**: apply a custom `Material` to a text range. Shader gets the glyph atlas as a `Texture2DArray`, two constant per-text UV4 channels (`ConstantUv2`/`ConstantUv3`) for runtime-animated shader params, and an optional per-glyph UV writer for staggered effects. Three compose modes — `Replace` (hide the base text on the range), `Over`, `Under`. Parameter is an optional tint color. Separate `emojiMaterial` slot for emoji glyphs inside the range.
- **Protection parse rules**: three standalone rules that shield content from any other parse rule (no pairing modifier needed) — `NoparseTagRule` (`<noparse>…</noparse>`, forgiving close), `CodeSpanRule` (balanced backtick runs per CommonMark §6.1: `` `x` ``, ` ``x`` `, ` ```x``` `), and `BackslashEscapeRule` (`\*`, `\[`, …, full CommonMark ASCII punctuation set).
- **Standalone parse rules**: a rule can be registered on `UniText` without a paired modifier (opt-in via `IParseRule.IsStandalone`) — it applies its effect on its own. Used by the three protection rules above.
- **`Style` static builders**: `Style.WholeText(modifier, parameter)`, `Style.Range(modifier, start, end, parameter)`, `Style.Tag(modifier, tagName, defaultParameter)` — replace the `new Style { Modifier = ..., Rule = new RangeRule { data = new() { ... } } }` boilerplate when building styles in code.
- **`RangeEx.WholeText` / `RangeEx.IsWholeText(...)`**: canonical `".."` constant and a predicate that accepts any equivalent syntactic form (`".."`, `"..^0"`, `"0.."`) — useful when building rules from user input.
- **`SubMeshModifier` abstract base class**: base class for writing your own modifiers that produce a separate sub-mesh with its own material (the same surface `MaterialModifier` is built on).
- **Custom shader authoring**: `Assets/Create > UniText > Custom Material Shader` menu scaffolds a new shader pre-wired for `MaterialModifier` (uses `UniText_Custom.cginc`, binds the glyph atlas `Texture2DArray`). Three example shaders ship as starting points (Dissolve, Hologram, Rainbow).
- **Noise generator**: `Tools > UniText > Noise Generator` window produces seamless grayscale value / FBM PNG textures (64–1024 px, configurable seed / frequency / octaves / lacunarity / gain / invert / tileable). Used by the example shaders; available for any procedural need.
- **Lit shaders for world-space text**: `UniText/Lit/SDF` and `UniText/Lit/Emoji` pick up ambient + a single directional light + fog, suitable for `UniTextWorld` in a 3D scene.
- **Default materials**: ready-to-use `UniTextLit`, `UniTextEmojiLit`, `UniTextDisolve`, `UniTextHologram`, `UniTextRainbow` materials in `Defaults/Materials/` (drop on a `MaterialModifier` or assign as the material of a `UniTextWorld`).
- **`GameObject > UI (World) > UniText > World Text` menu item**: creates a ready-to-go `UniTextWorld` object and auto-adds a `UniTextWorldRaycaster` to `Camera.main` so pointer events work out of the box.
- **Basic Usage sample extended**: new Language and Font sections, plus a bundled Source Han Sans subset (`Fonts/SourceHanSans-Demo.otf`, ~96 KB, SIL OFL 1.1) that actually shows CJK regional-glyph differences in the Language example.
- **Language APIs (public)**: `LanguageRegistry.Register/GetHandle/GetTag`, `LanguageMatching.Matches`, `Shaper.ShapeInto(..., IntPtr language)` overload, `HB.LanguageFromString` / `HB.SetLanguage` / `HB.ShapeRun(..., IntPtr language, ...)`, `UniTextFontStack.FindFontForCodepoint(uint, string preferredLanguage, ...)`, `UniTextFontProvider.FindFontForCodepoint(int, byte language)` — for code that drives shaping manually.
- **`UniTextFontStack.TryGetFamilyByName(name, out family)` / `UniTextFontProvider.TryGetFontIdByFamilyName(name)`**: resolve a `FontFamily.name` to a family or fontId at runtime.
- **`SharedFontCache.TryGet` / `Set` language overloads**: per-codepoint font-cache key now includes the active language, so the same codepoint can cache different results under different language tags.
- **`UniTextBuffers.PrepareStartMargins()`**: for modifier authors writing start-margin values (list indentation etc.) — lazily allocates the buffer to fit the current codepoint count.
- **`PooledBuffer<T>.ClearAll()` / `PooledArrayAttribute<T>.ClearAll()`**: clear the entire backing array (not just the `[0..count)` prefix) — matches the modifier-attribute usage pattern where the buffer is read at arbitrary indices.
- **`UniTextMaterialCache.Highlight`**: shared flat-colour transparent material used for range highlights (exposed for custom highlighter renderers).
- **`Run.language` / `ShapedRun.language`** (public struct fields): carries the language-registry index through the pipeline.
- **Project-wide language in Project Settings**: a Localization section in the UniText Settings panel edits `UniTextSettings.Language` without writing code.
- **`Tile Size Offset` per-font setting** (UniTextFont inspector): nudge the auto-classified SDF atlas tile size up or down by ±2 steps to force higher quality or save atlas memory; ignored on glyphs that have an explicit per-glyph tile override.

### Changed

- **Faster first-frame glyph generation**: SDF/MSDF preparation scales much better with glyph complexity — the internal contour-overlap check is now linear instead of quadratic in the number of curve segments per glyph. Biggest wins on CJK, decorative, and symbol fonts.
- **Inspector modifier/rule picker**: the popup now sizes to its content and resizes when groups expand/collapse, instead of truncating at 15 items.
- **FontStack inspector (collapsed family row)**: shows `name` and `primary` inline so you can rename / swap fonts without expanding the foldout.
- **Dirty flags / render mode enums lifted to top level** (breaking): `UniTextBase.DirtyFlags` → `UniTextDirtyFlags`, `UniTextBase.RenderModee` → `UniTextRenderMode`. Code referencing the old nested enums will not compile.
- **`CleanText` return type** (breaking): `string` → `ReadOnlySpan<char>`. The backing buffer is pooled and may be rewritten on the next rebuild; copy via `new string(span)` if you need a stable string.
- **`Text` getter semantics** (potentially breaking): always returns the serialized authored value, even when a buffer-based `SetText` has overridden the runtime text. Read the runtime-assigned text via `RawText` (or `RenderedText` if a resolver is in play).
- **`TextHighlighter.Initialize(UniText)` → `Initialize(UniTextBase)`** (breaking for custom highlighter subclasses). The `owner` field type switched from `UniText` to `UniTextBase` for the same reason — highlighter now works on both Canvas and world-space text.
- **Per-tag parse-rule classes are now `internal [Obsolete]`** (breaking if referenced in code): `BoldParseRule`, `ItalicParseRule`, `ColorParseRule`, `SizeParseRule`, `UnderlineParseRule`, `StrikethroughParseRule`, `CSpaceParseRule`, `LineSpacingParseRule`, `LineHeightParseRule`, `OutlineParseRule`, `ShadowParseRule`, `ObjParseRule`, `EllipsisTagRule`, `UppercaseParseRule`, `GradientParseRule`, `LinkTagParseRule`. Existing serialized assets still deserialize; new code should use `TagRule` (directly or via `Style.Tag(modifier, "name")`).
- **Custom `EffectModifier` subclasses** (breaking): the extension hook is now `OnGlyphEffect()` + `EnqueueEffectQuad(...)` instead of `RecordEffectGlyph(...)`, and the `HasVertexShifts()` override is gone. Built-in outline/shadow are unchanged for consumers; this only matters if you wrote your own effect subclass.

### Fixed

- **Auto-size on `UniTextWorld`**: `autoSize` on world-space text silently fell back to `maxFontSize` — the size-fitting step was Canvas-only. It now runs for world-space components too.
- **`UniTextWorld` sorting vs. other renderers**: world-space text was batched into one mesh regardless of each instance's sorting layer/order, so it rendered in front of or behind `SpriteRenderer` and other renderers as one block instead of interleaving per-instance. The batcher now groups by `(material, SortingLayer, OrderInLayer, SortingGroup)`, so each group becomes its own draw with the correct sorting.
- **Outline / shadow artifacts on emoji**: `OutlineModifier` and `ShadowModifier` applied their effect passes to emoji glyphs too, which rendered color bitmaps through an SDF effect shader and produced garbage. Both now skip color bitmap glyphs.



## [2.0.15] - 2026-04-20

### Fixed

- **WebGL emoji disappearing after text change**: Reusing the same emoji in a later text update left a transparent gap where the emoji should be rendered.
- **WebGL emoji missing when not at the start of text**: Emoji appearing after any preceding characters — including dual-presentation symbols like ⬅, ➡, ❤, ☀ — were not rendered and took no layout space.
- **`<size>` tag spreading letters apart at non-100% scales**: Letters inside a size-scaled range kept their original spacing and bearings while only the glyph quads shrank or grew, so small scales (e.g. `<size=10%>`) produced tiny letters scattered across the original word width instead of a proportionally compact word.
- **Diacritics detached from base letter inside `<size>`**: Arabic and other combining marks floated far from their base glyph when a per-range size scale was applied, because mark offsets were not scaled along with the base advance.

## [2.0.14] - 2026-04-18

### Fixed

- **RTL list marker position unstable**: Bullet and number markers in right-to-left lists (Arabic, Hebrew) shifted unpredictably when the item text changed and could render in the wrong position depending on the first character of the line.

## [2.0.13] - 2026-04-18

### Changed

- **Temporarily disabled native atlas upload path**: Atlas texture updates fall back to the standard upload to avoid crashes on macOS and glyph corruption seen in 2.0.0–2.0.12. Native path will return once stabilized.

## [2.0.12] - 2026-04-17

### Fixed

- **Korean text breaking mid-word**: The line breaker could split Korean (Hangul) text between adjacent syllables at optional break points, producing broken line wraps inside words.
- **SDF/MSDF artifacts inside glyphs with holes**: Bridge regions between overlapping contours (letters such as O, A, B, D, e) produced false distance gradients, visible as faint streaks or specks inside the hollow areas of the glyph.
- **`enableWordWrap` toggle not re-flowing text**: Switching the word-wrap setting between updates could reuse the cached line layout from the previous mode, leaving text wrapped (or unwrapped) incorrectly until another layout-invalidating change.

## [2.0.11] - 2026-04-15

### Fixed

- **Emoji ignoring RectMask2D soft edges**: Emoji glyphs were clipped with a hard edge under a `RectMask2D` with non-zero softness, while surrounding UI elements faded smoothly across the same boundary.

## [2.0.10] - 2026-04-15

### Fixed

- **Style preset effects leaking onto other text**: Modifiers inside a `StylePreset` (bold/italic/underline used via `CompositeModifier`) kept their attribute flags and event subscriptions between text updates, so italic, underline, and bold visibly appeared on unrelated characters after switching to text that did not use the preset tags.

## [2.0.9] - 2026-04-15

### Fixed

- **Underline/Strikethrough skipping lines at small font sizes**: At Font Size ≤3.6, every other line rendered without its underline or strikethrough — lines 1 and 3 got a line, lines 2 and 4 were bare.
- **UniText Text/Button created outside prefab in Prefab Mode**: Right-clicking empty space in the Hierarchy while editing a prefab placed the new `UniText - Text` or `UniText - Button` under a Canvas in the open scene instead of inside the prefab.

## [2.0.8] - 2026-04-14

### Added

- **`ParameterReader` exposed as public API**: Custom modifier authors can now parse tag parameters (floats, unit floats, colors, tokens) using the same locale-safe reader as built-in modifiers.
- **`GlyphAtlas` read-only introspection API**: The SDF and MSDF atlases are now reachable via `GlyphAtlas.GetInstance(RenderMode)`, with `TryGetEntry` returning a public `GlyphEntry` (page index, encoded tile, glyph metrics, pixel size). Key-building helpers `MakeKey`, `DefaultVarHash`, `ComputeVarHash48`, the `TileSizeFromEncoded` decoder, and constants `Pad`/`PageStride`/`DefaultBandPixels` are also accessible for tooling and custom renderers.

### Changed

- **Editor menus reorganized**: `Tools/UniText Tools` and `Tools/UniText Migration` consolidated under the `Tools/UniText/` submenu. GameObject creation moved from `GameObject/UI/UniText - Text|Button` to `GameObject/UI (Canvas)/UniText/Text|Button`.

### Fixed

- **Trailing empty line missing from range bounds**: `GetRangeBounds` skipped the empty line produced by a trailing newline (e.g. `"abc\n"`), so selection highlighting and link/hashtag bounds covering that line returned no rectangle.
- **Stencil material showing wrong atlas texture**: Text under a `Mask` could render with a stale or mismatched atlas texture after an atlas resize, especially when a renderer group mixed text and emoji glyphs.
- **Atlas shrink corrupting glyphs**: Trimming unused atlas pages used a mixed GPU/CPU copy path that could leave stale slice data and drop or corrupt glyphs after the atlas compacted.

## [2.0.7] - 2026-04-08

### Fixed

- **Font Subsetter dropping OpenType layout tables**: Subset fonts lost all GSUB/GPOS/GDEF/kern tables, breaking contextual shaping (Arabic connected forms, Indic conjuncts, ligatures, kerning).
- **Android 16KB page size compatibility**: Native GPU library failed to load on Android 15+ devices with 16KB memory pages.

## [2.0.3] - 2026-04-07

### Added

- **Runtime Style Preset API**: `AddStylePreset()`, `RemoveStylePreset()`, and `ClearStylePresets()` methods for assigning shared style presets to text components through code.

### Fixed

- **Text invisible after reparenting out of a Mask**: Moving a UniText object from under a `Mask` at runtime via `SetParent` left stale stencil material, causing text to disappear.
- **Editor errors when reverting a prefab with nested UniText**: "Revert All" on a prefab containing a nested UniText component produced `MissingReferenceException` for destroyed `CanvasRenderer`.
- **Click events not reaching parent Button**: UniText nested inside a Button blocked `OnPointerClick` from reaching the parent. Now pointer events propagate to the parent unless an interactive range (link, hashtag, etc.) is clicked.
- **RTL list marker offset on wrapped lines**: List markers shifted closer to text when an RTL line was wrapped by the line breaking algorithm.

## [2.0.2] - 2026-04-06

### Fixed

- **iOS emoji sequences not combining**: Emoji with skin tone modifiers, ZWJ sequences, and flag sequences rendered as separate glyphs on iOS. Fixed by shaping emoji through CoreText with `kCTTypesetterOptionAllowUnboundedLayout`.
- **`<lh>` delta units**: Delta values (`<lh=+5px>`) were treated as absolute instead of adding to the default line advance.
- **`<lh>` overridden by globalMinAdvance**: Custom line heights set by `<lh>` were silently replaced by the global minimum advance. Now only applies to lines with default height.

## [2.0.1] - 2026-04-04

### Fixed

- **GPU texture upload on Vulkan (Windows)**: Native GPU upload path was disabled for Vulkan, falling back to `Texture2D.Apply()`.
- **GlyphAtlas resize crash**: Atlas resize with 1 slice caused Unity to collapse `Texture2DArray` into `Texture2D`, breaking native upload.
- **GlyphAtlas resize losing glyphs**: Resize used `Graphics.CopyTexture` which could fail. Now re-uploads dirty slices via native GPU upload.
- **Unity 2021 compatibility**: `TextureCreationFlags.DontInitializePixels | DontUploadUponCreate` guarded behind `UNITY_2022_1_OR_NEWER`.
- **Scene Visibility not hiding UniText/UniTextWorld**: Eye icon toggle in hierarchy had no effect.
- **UniTextWorld invisible in Prefab Stage**: World-space text was invisible when editing prefabs. Batcher now creates a separate instance per Prefab Stage

## [2.0.0] - 2026-04-01

### Added

#### SDF/MSDF Rendering Pipeline

- **GlyphAtlas** (`Runtime/FontCore/GlyphAtlas.cs`): Shared `Texture2DArray`-backed glyph atlas with two singleton instances — one for SDF (`RHalf`) and one for MSDF (`RGBAHalf`). Features adaptive tile sizes (64/128/256 based on glyph complexity), shelf-based packing within 2048x2048 pages, reference counting with LRU eviction, automatic page recycling, and atlas shrinking.
- **SdfGenerator** (`Runtime/FontCore/SdfGenerator.cs`): Burst-compiled `IJobParallelFor` that generates single-channel SDF tiles using contour-seeded vector propagation (8SSEDT). Operates on raw quadratic Bezier curves — no bitmap rasterization.
- **MsdfGenerator** (`Runtime/FontCore/MsdfGenerator.cs`): Burst-compiled `IJobParallelFor` that generates multi-channel SDF tiles in `RGBAHalf` format. Three per-channel seed+propagate passes with tangent carry for pseudo-distance encoding, plus a fourth channel-agnostic error correction pass.
- **SdfCore** (`Runtime/FontCore/SdfCore.cs`): Shared types and reference implementations of SDF/MSDF algorithms — `GlyphTask` struct (used by both generators), tile transforms, Y-monotone splitting, winding number computation, 8SSEDT propagation (with and without tangent), Newton refinement, and quadratic solver. Both `SdfJob` and `MsdfJob` inline their own copies of the algorithms for optimal Burst codegen.
- **GlyphCurveCache** (`Runtime/FontCore/GlyphCurveCache.cs`): Per-font lazy extraction of glyph outlines as quadratic Bezier segments via FreeType `OutlineDecompose`. Normalizes curves to [0,1] glyph space, computes per-contour winding, runs edge coloring, and sorts segments by Y. Includes a thread-safe FreeType face pool for parallel extraction.
- **EdgeColoring** (`Runtime/FontCore/EdgeColoring.cs`): Port of msdfgen's `edgeColoringSimple` — assigns per-edge RGB channel masks for MSDF rendering. Detects corners via cross/dot product thresholds and cycles colors at corner vertices. Computes bisector vectors and corner flags for each segment.
- **RenderMode** enum on `UniText` component: `SDF` (single-channel) or `MSDF` (multi-channel) — controls which atlas mode the component uses.
- **SDF Detail Multiplier** on `UniTextFont`: Controls tile size classification — higher values force larger atlas tiles for fonts with thin strokes (e.g. calligraphic).
- **Glyph Overrides** on `UniTextFont`: Per-glyph tile size overrides (Auto/64/128/256) for fine-tuning quality on specific glyphs.

#### Font Family Architecture

- **FontFamily struct** on `UniTextFontStack`: `families[]` array replaces old flat `fonts` + `variants` lists. Each family has a `primary` font and optional `faces[]` (bold, italic, light, etc.) with a pre-computed `FontFaceLookup` for fast weight/style matching.
- **FontFaceLookup**: Sorted weight arrays, variable font slots (upright + italic), CSS §5.2 weight matching via BinarySearch. Pre-computed at initialization.
- **Variable font support**: `VariationModifier` with `<var>` tag for direct axis control (wght, wdth, ital, slnt, opsz). `UniTextFont.VariableAxes` exposes axis metadata. `IsVariable` property. Variable font axis enumeration via HarfBuzz (`hb_ot_var_get_axis_infos`) and variation setting via `hb_font_set_variations`.
- **Three-tier face resolution** in `ResolveFontFaces()`: (1) Variable font axes — if font has wght/ital/slnt, set axes directly; (2) Static font face — CSS §5.2 weight matching via `FontFaceLookup.FindFace()`; (3) Synthesis — fake bold/italic buffers remain non-zero for shader-based synthesis.
- **`<b>`/`<i>` semantic tags**: Automatically resolve to variable axes when available, fall back to static faces, then to synthesis. `<var>` tag provides direct axis control without fallback.
- **CSS font-weight scale for bold**: `BoldModifier` uses weight scale 100-900 encoded as a byte per codepoint. Smart default: `max(700, baseWeight + 300)`. Explicit parameter: `<b=500>` for CSS weight 500. Fake bold applied via SDF shader dilate (`UV1.y`) and per-glyph advance correction using FreeType's embolden ratio (em/24).
- **Variation run tracking**: `VariationRunInfo` struct and `variationMap` dictionary in TextProcessor track per-run axis values. `Shaper.Shape()` accepts `HB.hb_variation_t[]` parameter. FreeType coordinates set via `FT.SetVarDesignCoordinates()`.
- **FaceInfo auto-population** (editor): `familyName`, `styleName`, `weightClass`, and `isItalic` are automatically extracted from font data via FreeType on `OnEnable`/`OnValidate` and kept in sync. Fields are read-only in the inspector.
- **Native variable font API**: HarfBuzz axis enumeration/variation setting and FreeType Multiple Masters support (`FT.GetMMVar`, `FT.SetVarDesignCoordinates`) in `FT.cs` and `HB.cs`.

#### Word Segmentation for SE Asian Scripts

- **WordSegmentationProcessor** (`Runtime/Unicode/WordBreak/WordSegmentationProcessor.cs`): Post-processes UAX#14 line breaks — dispatches contiguous SA-class script runs (Thai, Lao, etc.) to registered word segmenters.
- **BestPathSegmenter** (`Runtime/Unicode/WordBreak/BestPathSegmenter.cs`): Dictionary-based best-path (maximal matching) DP algorithm — same approach as ICU Thai. Inserts `Optional` break opportunities at word boundaries.
- **DoubleArrayTrie** (`Runtime/Unicode/WordBreak/DoubleArrayTrie.cs`): Read-only compact double-array trie for fast dictionary lookup. Thread-safe after construction.
- **WordSegmentationDictionary** (`Runtime/Unicode/WordBreak/WordSegmentationDictionary.cs`): ScriptableObject holding compiled trie data for a specific script. Configured via `UniTextSettings.dictionaries[]`.
- **Dictionary Builder** tab in UniText Tools window: Builds dictionary assets from word list text files. Supports drag-and-drop, multi-file selection, target script selection, and automatic trie compilation.

#### Effect System (Outline, Shadow)

- **EffectModifier** (`Runtime/ModCore/EffectModifier.cs`): Abstract base class for modifiers that render an additional effect pass behind the face. Registers `EffectPass` (apply/revert callbacks) on the mesh generator. Provides `RecordEffectGlyph()` to store per-glyph UV and offset data, and `ApplyToMesh()`/`RevertFromMesh()` to write effect data to UV2 channel with vertex position offsets.
- **OutlineModifier** (`Runtime/ModCore/Modifiers/OutlineModifier.cs`): Outline effect via `<outline=dilate>`, `<outline=#color>`, or `<outline=dilate,#color>`. Supports fixed pixel size mode. Defaults: dilate=0.2, color=black.
- **ShadowModifier** (`Runtime/ModCore/Modifiers/ShadowModifier.cs`): Shadow/underlay effect via `<shadow=#color>`, `<shadow=dilate,#color>`, or `<shadow=dilate,#color,offsetX,offsetY,softness>`. Supports vertex shifts for offset shadows and fixed pixel size mode. Defaults: dilate=0, color=black 50% alpha.
- **EffectPacking** (`Runtime/Core/EffectPacking.cs`): Static utility for packing `Color32` into a single `float` via bit reinterpretation for shader unpacking.
- **UV2/UV3 buffers** on `UniTextMeshGenerator`: On-demand allocation of additional UV channels for effect layer data.
- **Multi-pass rendering** in `UniText.UpdateSubMeshes`: Effect passes rendered before the face pass using separate materials (Base shader). Each pass applies and reverts its mesh modifications via callbacks.

#### Material Management

- **UniTextMaterialCache** (`Runtime/Core/UniTextMaterialCache.cs`): Static cache that lazily creates and manages shared materials — SDF Face, SDF Base, MSDF Face, MSDF Base. MSDF variants use the `UNITEXT_MSDF` shader keyword. Subscribes to atlas texture changes and syncs `_MainTex` automatically.
- **Shader references on UniTextSettings**: `requiredShaders[]` array stores references to Base, Face, and Emoji shaders. `GetShader(int index)` provides runtime access. Settings provider auto-populates these on editor load.

#### Tag System Overhaul

- **TagRule** (`Runtime/ModCore/Rules/TagRule.cs`): Universal configurable tag parse rule that replaces all individual per-tag rule classes. A single sealed class with a serialized `tagName` field. Supports `defaultParameter` for fallback values and automatic parameter merging (tag-supplied values take priority, remaining fields filled from default).
- **MarkdownWrapRule** (`Runtime/ModCore/Rules/MarkdownWrapRule.cs`): Parse rule for Markdown-style symmetric wrap markers (`**`, `*`, `~~`, `++`). Configurable marker string, stack-based open/close matching, priority by marker length.
- **Simplified TagParseRule base**: Parameters are now always optional (no `HasParameter` virtual). Self-closing is purely syntax-driven (`<tag/>` or `<tag=value/>`). Removed `HasParameter`, `IsSelfClosing`, `InsertString` virtual properties.
- **DeprecatedTagRules** (`Runtime/ModCore/Rules/DeprecatedTagRules.cs`): All 16 tag parse rule classes (14 old + 2 new for outline/shadow) consolidated as hidden one-liner definitions marked with `[HideFromTypeSelector]` for backward-compatible deserialization.

#### Editor UX

- **Selector** (`Editor/Selector.cs`): Full-featured searchable popup selector with grouped mode (expandable group headers with submenu panels), flat search mode (multi-word tokenized, case-insensitive), keyboard navigation, description panels, theme-aware icons, auto-close on focus loss, and optional search field toggle.
- **Mod Register Presets**: The modifier list in the UniText inspector now opens a `Selector` with ~30 predefined presets (Bold, Italic, Outline, Shadow, Markdown variants, etc.) with icons and descriptions. Presets auto-configure both modifier and parse rule.
- **RangeRuleDataDrawer** (`Editor/RangeRuleDataDrawer.cs`): Custom property drawer for `RangeRule.Data` that generates structured UI for modifier parameters based on `ParameterFieldAttribute` metadata. Supports float, int, color, bool, string, enum, and unit (px/em/%) field types.
- **UniTextFontStackEditor** (`Editor/UniTextFontStackEditor.cs`): Custom inspector for `UniTextFontStack` with a Font Families section — each family displayed as a foldable group with primary font, faces list, family name mismatch warnings, weight/italic labels, add/remove buttons, and drag-and-drop zone.
- **Glyph Picker** in font editor: Type text to preview glyph rendering, select individual glyphs, and add tile size overrides directly from the preview grid.
- **Variable Axes Info** in font editor: Displays detected variable font axis metadata (tag, name, min/default/max) when a variable font is loaded.
- **UniTextObjectMenu** (`Editor/UniTextObjectMenu.cs`): `GameObject/UI/` menu items for creating UniText Text and Button objects. Supports prefab overrides via `UniTextSettings`. Creates Canvas/EventSystem if needed.
- **Atlas preview tabs**: Font editor preview split into SDF, MSDF, and Emoji tabs. Uses a `Hidden/UniText/AtlasPreview` shader to display raw distance field textures (grayscale for SDF, RGB for MSDF) from `Texture2DArray` slices.
- **Theme-aware editor icons**: `UniTextEditorResources` provides tinted icon caching for dark/light theme, with per-group and per-type icon mappings.
- **Text selection highlight**: `DefaultTextHighlighter` gains a `selectionGraphic` for programmatic text selection display via `SetSelection()`/`ClearSelection()`.

#### Metadata Attributes

- **ParameterFieldAttribute** (`Runtime/Attributes/ParameterFieldAttribute.cs`): Declares modifier parameter metadata (order, name, type, default) for auto-generating editor UI. Applied to all parameterized modifiers.
- **TypeDescriptionAttribute** (`Runtime/Attributes/TypeDescriptionAttribute.cs`): Human-readable description for types, shown in the Selector popup. Applied to all modifiers and parse rules.
- **HideFromTypeSelectorAttribute** (`Runtime/Attributes/TypeSelectorAttribute.cs`): Hides a type from the type selector dropdown while keeping it deserializable.

#### Virtual Glyph Injection

- **`virtualPositionedGlyphs` buffer** on `UniTextBuffers`: Separate buffer for glyphs injected by modifiers (ellipsis dots, list markers). Does not affect hit testing or selection.
- **`BeforeGenerateMesh` event** on `UniText`: Raised after glyph positioning but before mesh generation, allowing modifiers to inject virtual glyphs.
- `EllipsisModifier` and `ListModifier` now inject `PositionedGlyph` entries into the virtual buffer instead of drawing directly during mesh generation.

#### UniTextWorld (3D Text Rendering)

- **UniTextWorld** (`Runtime/Core/Component/UniTextWorld.cs`): World-space text rendering component. Provides the same text processing pipeline as `UniText` (Unicode, BiDi, shaping, line breaking, modifiers, emoji, font fallback, variable fonts) but renders via MeshRenderer + MeshFilter instead of CanvasRenderer. No Canvas required.
- **UniTextBase** (`Runtime/Core/Component/UniTextBase.cs`): Extracted shared base class from `UniText` — all text processing, modifier management, dirty flags, lifecycle, and parallel batch pipeline now live in `UniTextBase`. Both `UniText` (Canvas) and `UniTextWorld` (MeshRenderer) inherit from it.
- **UniTextBase_Parallel** (`Runtime/Core/Component/UniTextBase_Parallel.cs`): Extracted parallel batch processing pipeline (component collection, glyph batching, atlas rasterization, mesh generation, apply) from `UniText_Parallel` into a shared base partial class.
- **Per-instance owned sub-meshes**: Each effect pass and face segment renders via a dedicated child GameObject (`-_UTWSM_-`) with its own MeshFilter + MeshRenderer + per-instance Mesh (`HideFlags.HideAndDontSave`). Sorting order controls render layering (effects behind face).
- **Phased mesh upload**: Base vertex data (positions, UV0, UV1, UV3, colors, triangles) written once to all SDF sub-meshes; effect passes then overwrite only changed channels (UV2 + vertex shifts). Skips `Mesh.Clear()` when vertex count is unchanged between frames.
- **UniTextWorldEditor** (`Editor/UniTextWorldEditor.cs`): Custom inspector for `UniTextWorld` with sorting order and sorting layer controls.
- **UniTextBaseEditor** (`Editor/UniTextBaseEditor.cs`): Extracted shared editor base class from `UniTextEditor` for reuse by both `UniTextEditor` and `UniTextWorldEditor`.

#### SmallCaps and Lowercase Modifiers

- **SmallCapsModifier** (`Runtime/ModCore/Modifiers/SmallCapsModifier.cs`): Renders lowercase letters as small capitals. Two-tier approach: (1) Native — activates OpenType `smcp` feature via HarfBuzz for proper small cap glyphs; (2) Synthesis — converts to uppercase and scales down by 0.8x (fallback for fonts without `smcp`). Per-codepoint attribute byte: 0 = unchanged, 1 = native, 2 = synthesis. Synthesis adjusts both vertex positions and shaped glyph advances.
- **LowercaseModifier** (`Runtime/ModCore/Modifiers/LowercaseModifier.cs`): Transforms text to lowercase within marked ranges. Applied during modifier Apply phase before shaping.
- **`smcp` feature detection** in `Shaper`: `HasSmcpFeature()` test-shapes `'a'` with and without `smcp` feature, compares glyph IDs. Result cached per font ID in `smcpSupportCache`.
- **HarfBuzz feature support**: `hb_feature_t` struct and `Shape(font, buffer, features)` overload for passing OpenType features to shaping. `MakeTag()` utility for constructing OpenType tag values.
- **Shaper features parameter**: `Shaper.Shape()` now accepts optional `hb_feature_t[]` for per-run OpenType feature activation (used by SmallCaps for `smcp`).

#### Other

- **UI Creation Prefabs** on `UniTextSettings`: `textPrefab` and `buttonPrefab` fields for customizing `GameObject/UI/` menu item creation.
- **FreeType `OutlineDecompose`**: New native API that decomposes glyph outlines into quadratic Bezier segments in design units, replacing the old SDF bitmap rendering path.
- **FaceInfo extensions**: Added `weightClass` (CSS 100-900 from OS/2 `usWeightClass`) and `isItalic` (from FreeType `style_flags`) to the `FaceInfo` struct.
- **DefaultParameterAttribute** (`Runtime/Attributes/DefaultParameterAttribute.cs`): Declares default parameter values for modifiers, enabling parameter auto-fill in the editor.
- **ParameterFieldUtility** (`Editor/ParameterFieldUtility.cs`): Extracted shared parameter field drawing logic from `RangeRuleDataDrawer` for reuse by `DefaultParameterDrawer` and other editors.
- **Emoji atlas Texture2DArray**: `EmojiFont` now maintains a `Texture2DArray` synced from staging `Texture2D` pages, with incremental dirty-page sync.
- **ColorParsing** (`Runtime/ModCore/ColorParsing.cs`): Shared static utility for parsing hex (#RGB, #RRGGBB, #RRGGBBAA) and 21 named colors. Extracted from `ColorModifier` for reuse by OutlineModifier, ShadowModifier, and RangeRuleDataDrawer.

### Changed

#### UniTextWorld Rendering

- `UniText` component refactored: shared logic (text processing, modifier management, dirty flags, lifecycle, parallel pipeline) extracted to `UniTextBase`. `UniText` retains only Canvas-specific rendering (`CanvasRenderer`, stencil, `UpdateGeometry`).
- `UniText_Parallel` refactored: batch pipeline logic extracted to `UniTextBase_Parallel`. `UniText_Parallel` retains only Canvas-specific click handling.
- Mesh generator callbacks renamed to camelCase: `OnGlyph` → `onGlyph`, `OnAfterPage` → `onAfterPage`, `OnRebuildStart` → `onRebuildStart`, `OnRebuildEnd` → `onRebuildEnd`.
- Mesh generator: removed unused public fields (`currentShapedGlyphIndex`, `x`, `y`, `width`, `xScale`, `atlasSize`, `gradientScale`, `spreadRatio`, `rectWidth`, `hAlignment`, `currentFontId`). `SetHorizontalAlignment()` method removed.
- `UniTextFontProvider`: renamed `MainFont` → `PrimaryFont`, `MainFontId` → `PrinaryFontId`. Internal field names updated accordingly.
- `EmojiFont`: emoji atlas textures now use mipmaps (`Texture2D` and `Texture2DArray` created with `mipmap=true`). Filter mode changed to `Trilinear` with `mipMapBias = -0.5f`. Packing spacing increased from 1 to 4 pixels to prevent mipmap bleeding.
- All modifier base classes updated to use renamed `UniTextBase` references instead of `UniText`.

#### Rendering Pipeline

- Mesh generator rewritten from group-by-font-then-atlas iteration to single-pass loop over all positioned glyphs. SDF glyphs look up tiles in the shared `GlyphAtlas`; emoji glyphs processed separately in `GenerateEmojiSegment`.
- UV encoding changed: UV0.zw = `(tileIdx, glyphH)` for atlas tile lookup; UV1 = `(aspect, faceDilate)` as `Vector2` (was `Vector4`).
- Glyph metrics now use design units directly throughout the pipeline — removed `pointSize`-based `metricsConversion` factor.
- `UniTextRenderData` simplified to carry only mesh and font ID; materials assigned externally via `UniTextMaterialCache`.
- Multi-pass effect rendering in `UpdateSubMeshes`: effect passes render before the face pass, each with apply/revert callbacks modifying UV2 and vertex positions.
- Required canvas shader channels extended to include `TexCoord2` and `TexCoord3` for effect layers.
- Glyph reference counting: `UniText` component tracks `currentGlyphKeys` and calls `AddRef`/`Release` on the atlas, enabling accurate eviction.
- Atlas pre-allocation: estimated tile area per atlas mode calculated before rendering, enabling `GlyphAtlas.PreAllocate()`.
- Periodic atlas maintenance: page recycling every 60 frames, atlas shrinking every 300 frames.
- Mesh generator glyph lookup changed from `fontHash` (int) to `varHash48` (long) — supports variable font axis variation. `variationMap` from buffers used to resolve per-run variation hashes.

#### Font System

- `UniTextFont` no longer owns atlas textures — all atlas management delegated to `GlyphAtlas` singletons.
- Glyph preparation/rendering pipeline rewritten: `PrepareGlyphBatch` filters via `GlyphAtlas.TryGetEntry` and protects existing entries with `AddRef`; `RenderPreparedBatch` extracts curves via `GlyphCurveCache` (supports parallel extraction); `PackRenderedBatch` queues segments to `GlyphAtlas.EnsureGlyph`.
- `CreateFontAsset()` simplified — removed `samplingPointSize`, `spreadStrength`, `renderMode`, `atlasSize` parameters.
- `ClearDynamicData()` disposes curve cache and clears font entries from the shared atlas instead of destroying per-font textures.
- `OnDestroy()` now calls `Shaper.ClearCache()` to properly release HarfBuzz native data (was previously leaking).
- `FaceInfo.pointSize` removed; replaced by `weightClass` and `isItalic` fields.
- HarfBuzz memory: `Shaper.FontCacheEntry` now pins the managed `byte[]` via `GCHandle` instead of copying to unmanaged memory via `Marshal.AllocHGlobal`, eliminating the duplicate font data in memory.
- Glyph lookup key changed from `uint glyphIndex` to `long glyphKey` (48-bit variation hash + glyph index) via `GlyphAtlas.MakeKey(varHash48, glyphIndex)`. Enables the same font to cache different glyph shapes for different variable font axis values.
- `PrepareGlyphBatch` and `RenderPreparedBatch` now accept `varHash48` and `ftCoords` parameters for variable font rendering. FreeType design coordinates set before glyph extraction.

#### Font Provider

- Removed `Appearance` property and `GetMaterials()` method from `UniTextFontProvider`.
- Constructor no longer takes an `appearance` parameter.
- Constructor now calls `BuildResolvedFamilies()` to flatten the entire fallback chain into a `resolvedFamilies[]` array with `fontIdToFamilyIndex` dictionary for O(1) family lookup.
- `HasVariants`/`FindVariant()` replaced by `HasFaces` property, `GetFamilyIndex(int fontId)` and `GetFamilyLookup(ushort familyIndex)` for direct access to `FontFaceLookup`.

#### Parallel Pipeline

- Font batch key changed from `UniTextFont` reference to `(UniTextFont, RenderModee, varHash48)` struct — variable font runs with different axis values are batched separately.
- Glyph collection no longer filters already-atlased glyphs at collection time.
- `RasterizeGlyphBatches` extracted as a separate method with per-batch timing diagnostics.
- `DoGenerateMeshData` now clears virtual glyphs buffer, invokes `BeforeGenerateMesh`, and passes virtual glyphs alongside regular glyphs to `GenerateMeshDataOnly`.
- `PeriodicAtlasMaintenance()` extracted as a separate static method, called before component processing instead of after.

#### Modifier System

- `BaseLineModifier` refactored: line segment computation extracted into `ComputeLineSegments()`, executed once then rendered per page. No longer restricted to matching the current font. Event hook changed from `OnAfterGlyphsPerFont` to `OnAfterPage`.
- `LineRenderHelper` rewritten from 3-quad atlas-based rendering (12 vertices) to 1-quad tile-based rendering (4 vertices) using `GlyphAtlas.TryGetEntry` for underscore glyph lookup.
- `EllipsisModifier` changed from immediate mesh drawing (`GlyphRenderHelper.DrawString`) to virtual glyph injection into `virtualPositionedGlyphs`. Event hook changed from `OnAfterGlyphsPerFont` to `BeforeGenerateMesh`.
- `ListModifier` changed from immediate mesh drawing to virtual glyph injection, same pattern as `EllipsisModifier`. Parameter separator changed from `:` to `,`.
- `LineHeightModifier` parameter format changed from `s:value` to `s,value` (comma-separated).
- `ColorModifier` color parsing logic extracted to shared `ColorParsing` utility class.
- `ItalicModifier` now skips vertex shear when the resolved font is already natively italic (`FaceInfo.isItalic`).
- `BoldModifier` `ParameterField` format changed from `"int"` to `"int(100,900)"` for range-constrained editor UI.

#### Editor

- `UniTextFontToolsWindow` renamed to `UniTextToolsWindow`; menu item changed to `Tools/UniText Tools`. File list refactored into reusable `DrawFileList()` method.
- Font editor: removed Atlas Settings section (point size, atlas size, spread, render mode). Replaced with Settings section (font scale, SDF detail multiplier). Atlas preview changed from per-font `Texture2D` to shared `Texture2DArray` slices.
- Type selector dropdown replaced by `Selector` popup with icons, descriptions, and group navigation.
- Editor resource path changed from `Icons/{name}` to `UniText/Icons/{name}`.
- Settings provider no longer draws `defaultAppearance`; now draws UI Creation Prefabs and Word Segmentation sections.
- `EmojiFont` material shader changed from `UI/Default` to `UniText/Emoji` (via `UniTextSettings.GetShader`).
- `SearchableSelector` renamed to `Selector` (file and class). Added `showSearch` parameter to `Show()` for hiding the search field.
- Font editor: added Apply/Revert buttons for rebuild-required properties (`sdfDetailMultiplier`, `glyphOverrides`). Changes are staged as pending until explicitly applied.
- `RangeRuleDataDrawer`: shared parameter field drawing logic extracted into `ParameterFieldUtility` for reuse.

### Removed

- **UniTextAppearance** (`Runtime/FontCore/UniTextAppearance.cs`): Deleted. ScriptableObject that mapped fonts to rendering materials with per-frame property delta caching. Material management replaced by `UniTextMaterialCache`.
- **SDF rendering classes from FreeTypeParallel** (`Runtime/FontCore/FreeTypeParallel.cs`): `SdfRenderedGlyph` struct and `SdfGlyphRenderer` class removed. `FreeTypeFacePool` rewritten — SDF bitmap rendering via `FT.RenderSdfGlyph()` removed, class retained for color bitmap/emoji rendering only. SDF generation replaced by curve-based `GlyphCurveCache` + Burst SDF/MSDF jobs.
- **GlyphRenderHelper** (`Runtime/ModCore/Modifiers/GlyphRenderHelper.cs`): Deleted. Immediate glyph mesh generation utility (`DrawGlyph`, `DrawString`, `MeasureString`). Replaced by virtual glyph injection pattern.
- **UniTextRenderMode enum** (`Runtime/FontCore/FontTypes.cs`): Removed (had values: SDF, Smooth, Mono). Replaced by `UniText.RenderModee` enum (SDF, MSDF) on the component.
- **AtlasMode enum** (`Runtime/FontCore/GlyphAtlas.cs`): Removed. `GlyphAtlas.GetInstance()` now takes `UniText.RenderModee` directly.
- **Per-font atlas textures**: `atlasTextures`, `atlasSize`, `spreadStrength`, `atlasRenderMode`, `usedGlyphRects`, `freeGlyphRects`, and shelf packing state removed from `UniTextFont`.
- **FreeType SDF native API**: `ut_ft_set_sdf_spread`, `ut_ft_render_sdf_glyph`, `ut_ft_free_sdf_buffer` P/Invoke declarations and wrappers removed from `FT.cs`.
- **Shader GUIs**: `UniText_BitmapShaderGUI.cs` and `UniText_SDFShaderGUI.cs` deleted (old custom ShaderGUI for bitmap and SDF shader inspectors).
- **Individual tag parse rule files** (14 files): `BoldParseRule.cs`, `ItalicParseRule.cs`, `ColorParseRule.cs`, `SizeParseRule.cs`, `UnderlineParseRule.cs`, `StrikethroughParseRule.cs`, `CSpaceParseRule.cs`, `LineSpacingParseRule.cs`, `LineHeightParseRule.cs`, `GradientParseRule.cs`, `EllipsisTagRule.cs`, `ObjParseRule.cs`, `Link/LinkTagParseRule.cs`, `UppercaseParseRule.cs`. All consolidated into `TagRule` with backward-compatible stubs in `DeprecatedTagRules.cs`.
- **GeneratedMeshSegment struct**: Removed from `UniTextMeshGenerator`. Replaced by `EffectPass` struct for multi-pass rendering.
- **`defaultAppearance`** from `UniTextSettings` and its backup system.
- **`GlyphsByFont`** grouping from `SharedPipelineComponents` (no longer needed with single-pass mesh generation).
- **`sourceFontFilePath`** from `UniTextFont`.
- **`fonts` and `variants`** from `UniTextFontStack`: Flat `StyledList<UniTextFont>` fonts list and `UniTextFont[]` variants array replaced by `FontFamily[]` families.
- **`FindClosestVariant()`** from `UniTextFontStack`: Replaced by `FontFaceLookup.FindFace()` with CSS §5.2 directional weight matching.
- **`CurrentAtlasMode`** property from `UniText`: Removed. `GlyphAtlas.GetInstance()` now takes `RenderMode` directly.

#### Zstd Font Compression

- **Zstd-compressed font data**: Font bytes stored in `UniTextFont` assets are now compressed with Zstandard (level 22) at import time. Decompression is lazy (on first `FontData` access) with zero per-frame cost. Benchmarks: **~600 MB/s on desktop, ~175 MB/s on low-end Android**. Typical Latin font (600 KB) decompresses in <1 ms. Build size reduction: **~2.7x for Latin/Arabic fonts, ~1.3x for CJK fonts**.
- **Zstd native integration**: Decompression (`ut_zstd_decompress`, `ut_zstd_get_frame_content_size`) built into the runtime `unitext_native` library across all platforms (Windows, Linux, macOS, Android, iOS, tvOS, WebGL). Runtime library built with `-DZSTD_BUILD_COMPRESSION=OFF` for minimal size (~80 KB).
- **Editor-only compression**: `ut_zstd_compress` and `ut_zstd_compress_bound` live in `unitext_native_editor` (desktop only). `Zstd.Compress()` is available only under `#if UNITY_EDITOR`.
- **Automatic migration**: `OnValidate` detects uncompressed font data via Zstd magic bytes (`0x28B52FFD`) and compresses in-place. No manual migration step needed.
- **Memory optimization**: In runtime builds, compressed `fontData` is freed after decompression to avoid keeping both copies in memory.
- **Burst dependency**: Added `com.unity.burst` >= 1.6.0 to package dependencies.

### Fixed

- **HarfBuzz memory leak on font destroy**: `UniTextFont.OnDestroy()` now calls `Shaper.ClearCache()` to release HarfBuzz native data (unmanaged font copy, hb_blob, hb_face, hb_font). Previously, these resources leaked in the static `fontCache` until domain reload.
- **Duplicate font data in memory**: HarfBuzz `FontCacheEntry` now pins the managed `byte[]` via `GCHandle` instead of allocating a separate unmanaged copy, halving per-font memory overhead.
- **FontSize minimum too restrictive**: `fontSize`, `minFontSize`, `maxFontSize` setters clamped to `1f` minimum, preventing small text in world-space. Changed minimum to `0.01f`.
- **UniTextSettings resilience**: Fixed settings loss on package reinstallation.
- **Unity 2021/2022 compatibility**: Fixed compiler errors for older Unity versions.
