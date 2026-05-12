// UniText_Custom.cginc — prelude for user-authored UniText material shaders.
//
// Contract (enforced automatically when you include this file):
//   * _MainTex is declared as 2DArray — UniText sets it to the current glyph atlas at runtime.
//   * MSDF and emoji atlas modes are handled by UniTextSampleAtlas / UniTextSampleSDF —
//     you do NOT need to add multi_compile for UNITEXT_MSDF or UNITEXT_EMOJI yourself.
//   * Standard Canvas UI clip/mask helpers are provided (ComputeMask, ApplyClipping).
//
// Required Properties block in your .shader:
//   [HideInInspector] _MainTex ("Font Atlas", 2DArray) = "" {}   // bound at runtime — must be hidden
//   _ClipRect     ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
//   _MaskSoftnessX("Mask SoftnessX", float) = 0
//   _MaskSoftnessY("Mask SoftnessY", float) = 0
//   _Stencil*     (stencil properties for UI masks)
//   _ColorMask    ("Color Mask", Float) = 15
//
// Required SubShader Tags:
//   "Queue"="Transparent", "IgnoreProjector"="True", "RenderType"="Transparent"
//
// Required Pass pragmas:
//   #pragma multi_compile __ UNITY_UI_CLIP_RECT
//   #pragma multi_compile __ UNITY_UI_ALPHACLIP
//
// Optional Properties — keyword control in cloneMaterial=false mode (see MaterialModifier):
//   [Toggle(UNITEXT_MSDF)]  _UniText_IsMsdf  ("UniText: MSDF Mode",  Float) = 0
//   [Toggle(UNITEXT_EMOJI)] _UniText_IsEmoji ("UniText: Emoji Mode", Float) = 0
// In the default cloneMaterial=true workflow UniTextCustomMaterialCache drives these keywords
// automatically on a cached runtime clone — the Toggle UI in Inspector is cosmetic (state stays
// in the source asset). In cloneMaterial=false mode the batcher does NOT touch keywords; set
// them yourself via these checkboxes or via Material.EnableKeyword / DisableKeyword in code.
// Example shaders (UniText_Custom-Example.shader etc.) declare these toggles for you.
//
// Optional per-vertex custom data:
//   TEXCOORD2 (float4) and TEXCOORD3 (float4) are free and are populated by MaterialModifier
//   (constant-from-inspector / delegate / subclass override). Use them in your vert/frag freely.

#ifndef UNITEXT_CUSTOM_INCLUDED
#define UNITEXT_CUSTOM_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"

// Atlas mode keywords.
// In cloneMaterial=true mode MaterialModifier drives these via UniTextCustomMaterialCache on a
// cached runtime clone. In cloneMaterial=false mode the source material's own keyword state is
// used — either through [Toggle(UNITEXT_MSDF)] / [Toggle(UNITEXT_EMOJI)] Properties (see header)
// or Material.EnableKeyword / DisableKeyword from code. This include registers multi_compile
// either way; you don't add it yourself.
#pragma multi_compile __ UNITEXT_MSDF
#pragma multi_compile __ UNITEXT_EMOJI

UNITY_DECLARE_TEX2DARRAY(_MainTex);

float4 _ClipRect;
float _MaskSoftnessX;
float _MaskSoftnessY;
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;
int _UIVertexColorAlwaysGammaSpace;

// ============================================================================
// Vertex input layout written by UniText mesh generator.
// Matches EffectModifier / base SDF contract exactly.
//
// IMPORTANT — v.vertex.xy is NOT a size/position-invariant coord.
//   * Canvas path:  v.vertex.xy is in RectTransform-local UI space (starts near 0,0).
//   * World path:   UniTextWorldBatcher combines many UniTextWorld components into one mesh
//                   and pre-transforms their vertices into the batcher's local space, so
//                   v.vertex.xy ends up shifted by each component's world position.
// If you need a per-glyph identifier stable between Canvas/World and independent of text
// size/position, use `v.texcoord0.z` (atlas tile id — same for every quad of the same glyph)
// or `v.texcoord0.xy` (glyph-local UV). See Rainbow/Dissolve/Hologram examples.
// ============================================================================
struct unitext_appdata
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 vertex    : POSITION;
    float3 normal    : NORMAL;
    fixed4 color     : COLOR;
    float4 texcoord0 : TEXCOORD0; // xy = glyph UV, z = encodedTile, w = glyphH (em-space height)
    float4 texcoord1 : TEXCOORD1; // x = aspect (glyphW/glyphH), y = faceDilate, z = cluster index (integer), w = intra-glyph X fraction (0..1)
    float4 texcoord2 : TEXCOORD2; // user channel A (MaterialModifier constant / delegate / override)
    float4 texcoord3 : TEXCOORD3; // user channel B
};

// ============================================================================
// Atlas UV transform.
//
// In SDF/MSDF mode the mesh generator writes `encodedTile` and `glyphH` into UV0.zw, so the
// "atlas-local UV" has to be transformed by UniTextComputeAtlasUV before sampling.
//
// In emoji mode the mesh generator writes normalized atlas UV directly into UV0.xy and the
// page layer into UV0.z. UniTextComputeAtlasUV branches on UNITEXT_EMOJI so you can call it
// uniformly in either mode.
// ============================================================================

#define UNITEXT_SDF_PAGE_SIZE 2048.0
#define UNITEXT_SDF_PAD 0.5
#define UNITEXT_GRID_UNIT 64
#define UNITEXT_COL_SLOTS 32
#define UNITEXT_DILATE_SCALE UNITEXT_SDF_PAD

float3 UniTextComputeAtlasUV(float4 texcoord0, float4 texcoord1)
{
#ifdef UNITEXT_EMOJI
    return texcoord0.xyz;
#else
    uint enc = (uint)(texcoord0.z + 0.5);
    uint page = enc >> 14u;
    enc &= 16383u;

    uint sizeClass = enc >> 12u;
    uint rem = enc & 4095u;
    uint shelfRow = rem >> 5u;
    uint tileCol = rem & 31u;

    float glyphH = texcoord0.w;
    float aspect = texcoord1.x;

    float tileSize = (float)UNITEXT_GRID_UNIT * (float)(1 << sizeClass);
    float padGlyph = UNITEXT_SDF_PAD / max(glyphH, 1e-6);

    float maxDim = max(aspect, 1.0);
    float totalExtent = maxDim + 2.0 * padGlyph;

    float invPage = 1.0 / UNITEXT_SDF_PAGE_SIZE;
    float2 tileOrigin = float2(tileCol * tileSize, shelfRow * (float)UNITEXT_GRID_UNIT) * invPage;
    float s = tileSize * invPage / totalExtent;

    float2 glyphOffset = float2(
        (maxDim - aspect) * 0.5 + padGlyph,
        (maxDim - 1.0)    * 0.5 + padGlyph
    );

    float2 uv = texcoord0.xy * float2(s, s) + tileOrigin + glyphOffset * s;
    return float3(uv, (float)page);
#endif
}

// ============================================================================
// Atlas sampling.
//   UniTextSampleAtlas  — raw RGBA sample. In emoji mode returns the color tile.
//                          In SDF mode returns the distance field as-is (R in SDF, RGB in MSDF).
//   UniTextSampleSDF    — returns a signed distance in [-0.5, +0.5]. Valid only outside UNITEXT_EMOJI
//                          (emoji atlas stores RGBA, not a distance field).
// ============================================================================

half4 UniTextSampleAtlas(float3 atlasUV)
{
    return UNITY_SAMPLE_TEX2DARRAY(_MainTex, atlasUV);
}

float UniTextMedian3(float3 v)
{
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

float UniTextSampleSDF(float3 atlasUV)
{
#ifdef UNITEXT_EMOJI
    // Emoji atlas is RGBA, no signed distance — return 0 as a safe fallback.
    return 0.0;
#elif defined(UNITEXT_MSDF)
    return UniTextMedian3(UNITY_SAMPLE_TEX2DARRAY(_MainTex, atlasUV).rgb) - 0.5;
#else
    return UNITY_SAMPLE_TEX2DARRAY(_MainTex, atlasUV).r - 0.5;
#endif
}

// ============================================================================
// Gamma, clipping, masking. Matches base UniText behaviour — call these from your vert/frag.
// ============================================================================

fixed4 UniTextGammaToLinearIfNeeded(fixed4 color)
{
    if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
        color.rgb = UIGammaToLinear(color.rgb);
    return color;
}

half4 UniTextComputeMask(float4 vert, float2 pixelSize)
{
    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX),
                               max(_UIMaskSoftnessY, _MaskSoftnessY));
    return half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw,
                 0.25 / (0.25 * maskSoftness + abs(pixelSize)));
}

half4 UniTextApplyClipping(half4 color, half4 mask)
{
    #if UNITY_UI_CLIP_RECT
    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(mask.xy)) * mask.zw);
    color *= m.x * m.y;
    #endif

    #if UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
    #endif

    return color;
}

// ============================================================================
// Convenience: SDF face alpha with screen-space AA. Use when you want the standard
// signed-distance-to-coverage conversion in your custom shader.
// ============================================================================
float UniTextSDFAlpha(float signedDist, float faceDilate, float glyphH, float2 glyphUV)
{
    float2 dUV = fwidth(glyphUV);
    float aaWidth = max(dUV.x, dUV.y) * glyphH;
    float faceDist = signedDist - faceDilate * UNITEXT_DILATE_SCALE;
    return saturate(-faceDist / aaWidth + 0.5);
}

#endif // UNITEXT_CUSTOM_INCLUDED
