// URP-flavored counterpart of UniText_SDF-Common.cginc.
// Same SDF / MSDF atlas math, but uses URP/Core HLSL API (TEXTURE2D_ARRAY, SAMPLE_TEXTURE2D_ARRAY)
// instead of legacy UNITY_* macros. Encoding (signed distance in EM-space [-0.5, 0.5]
// mapped to R16F [0, 1], shelf layout, page stride) is identical to the built-in path —
// CPU pipeline emits the same atlas for both renderers.

#ifndef UNITEXT_SDF_COMMON_URP_INCLUDED
#define UNITEXT_SDF_COMMON_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define SDF_PAGE_SIZE 2048.0
#define SDF_PAD       0.5
#define DILATE_SCALE  SDF_PAD
#define PAGE_STRIDE   16384
#define GRID_UNIT     64
#define COL_SLOTS     32

TEXTURE2D_ARRAY(_MainTex);
SAMPLER(sampler_MainTex);

struct sdf_vertex_t
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;     // per-quad world-space normal written by UniTextWorldBatcher
    float4 color      : COLOR;
    float4 texcoord0  : TEXCOORD0;  // xy = glyph UV, z = encodedTile, w = glyphH
    float4 texcoord1  : TEXCOORD1;  // x = aspect, y = faceDilate
    float4 texcoord2  : TEXCOORD2;  // x = effectDilate, y = packedRG, z = packedBA, w = effectSoft
    float4 texcoord3  : TEXCOORD3;
};

half4 UnpackColor(float rg, float ba)
{
    float r = floor(rg / 256.0);
    float g = rg - r * 256.0;
    float b = floor(ba / 256.0);
    float a = ba - b * 256.0;
    return half4(r, g, b, a) / 255.0;
}

void ComputeSDFTransform(float fullEncoded, float aspect, float glyphH,
                         out float2 sdfScale, out float2 sdfOffset, out float pageLayer)
{
    uint enc  = (uint)(fullEncoded + 0.5);
    uint page = enc >> 14u;          // PAGE_STRIDE = 2^14
    enc &= 16383u;
    pageLayer = (float)page;

    uint sizeClass = enc >> 12u;     // 4096 = 2^12
    uint rem       = enc & 4095u;
    uint shelfRow  = rem >> 5u;      // COL_SLOTS = 2^5
    uint tileCol   = rem & 31u;

    float tileSize = (float)GRID_UNIT * (float)(1 << sizeClass);
    float padGlyph = SDF_PAD / max(glyphH, 1e-6);

    float maxDim      = max(aspect, 1.0);
    float totalExtent = maxDim + 2.0 * padGlyph;

    float invPage    = 1.0 / SDF_PAGE_SIZE;
    float2 tileOrigin = float2(tileCol * tileSize, shelfRow * (float)GRID_UNIT) * invPage;
    float  s          = tileSize * invPage / totalExtent;

    float2 glyphOffset = float2(
        (maxDim - aspect) * 0.5 + padGlyph,
        (maxDim - 1.0) * 0.5 + padGlyph
    );

    sdfScale  = float2(s, s);
    sdfOffset = tileOrigin + glyphOffset * s;
}

float median3(float3 v)
{
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

#ifdef UNITEXT_MSDF
    #define SAMPLE_SDF(uv) (median3(SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, (uv).xy, (uv).z).rgb) - 0.5)
#else
    #define SAMPLE_SDF(uv) (SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, (uv).xy, (uv).z).r - 0.5)
#endif

#endif // UNITEXT_SDF_COMMON_URP_INCLUDED
