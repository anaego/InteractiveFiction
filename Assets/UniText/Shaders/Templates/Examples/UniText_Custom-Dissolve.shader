// Dissolve — threshold a noise texture against per-text progress, with a glowing edge band.
// Drives the classic "burn away" reveal/hide effect.
//
// Per-text dynamic parameters (read from TEXCOORD2):
//   UV2.x = progress (0 = fully hidden, 1 = fully visible)
//   UV2.y = scroll offset (added to noise scroll phase, unique per text)
//   UV2.z, UV2.w = free
//
// Animate via `MaterialModifier.ConstantUv2 = new Vector4(progress, 0, 0, 0)` from script.
// Material-wide parameters (shared across all texts using this material) stay in Properties:
// _NoiseTex, _NoiseScale, _NoiseScroll, _NoiseSpace, _EdgeWidth, _EdgeSoftness, _EdgeColor.

Shader "UniText/Custom/Dissolve"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2DArray) = "" {}

        [Toggle(UNITEXT_MSDF)]  _UniText_IsMsdf  ("UniText: MSDF Mode",  Float) = 0
        [Toggle(UNITEXT_EMOJI)] _UniText_IsEmoji ("UniText: Emoji Mode", Float) = 0

        _NoiseTex       ("Noise",          2D)      = "white" {}

        _NoiseScale     ("Noise Scale",     Float) = 1.0
        _NoiseScroll    ("Noise Scroll (xy = speed, zw = static offset)", Vector) = (0, 0, 0, 0)

        _EdgeWidth      ("Edge Width",      Range(0, 0.3)) = 0.06
        _EdgeSoftness   ("Edge Softness",   Range(0, 0.2)) = 0.015
        [HDR] _EdgeColor ("Edge Color",     Color) = (2, 0.7, 0.1, 1)

        // Per-text parameters (read from sub-mesh UV2 — set via MaterialModifier inspector or ConstantUv2).
        [HideInInspector] _UniTextInstUv2X ("Progress",            Range(0,1)) = 1
        [HideInInspector] _UniTextInstUv2Y ("Noise Scroll Offset", Float)      = 0

        _ClipRect       ("Clip Rect",       Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX  ("Mask SoftnessX",  Float) = 0
        _MaskSoftnessY  ("Mask SoftnessY",  Float) = 0

        _StencilComp    ("Stencil Comparison", Float) = 8
        _Stencil        ("Stencil ID",         Float) = 0
        _StencilOp      ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255

        _CullMode       ("Cull Mode",  Float) = 0
        _ColorMask      ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "IgnoreProjector"= "True"
            "RenderType"     = "Transparent"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull   [_CullMode]
        ZWrite Off
        Lighting Off
        Fog { Mode Off }
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UNITEXT_CUSTOM_DISSOLVE"
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../../UniText_Custom.cginc"

            sampler2D _NoiseTex;

            float  _NoiseScale;
            float4 _NoiseScroll;

            float  _EdgeWidth;
            float  _EdgeSoftness;
            half4  _EdgeColor;

            struct v2f
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex    : SV_POSITION;
                float3 atlasUV   : TEXCOORD0;
                float2 glyphUV   : TEXCOORD1;
                half4  mask      : TEXCOORD2;
                fixed4 color     : TEXCOORD3;
                float2 glyphMeta : TEXCOORD4; // x = glyphH, y = faceDilate
                float2 noiseUV   : TEXCOORD5;
                float  progress  : TEXCOORD6; // per-text, from UV2.x
            };

            v2f vert(unitext_appdata v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 clipPos = UnityObjectToClipPos(v.vertex);
                float2 pixelSize = clipPos.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                o.vertex    = clipPos;
                o.atlasUV   = UniTextComputeAtlasUV(v.texcoord0, v.texcoord1);
                o.glyphUV   = v.texcoord0.xy;
                o.mask      = UniTextComputeMask(v.vertex, pixelSize);
                o.color     = UniTextGammaToLinearIfNeeded(v.color);
                o.glyphMeta = float2(v.texcoord0.w, v.texcoord1.y);

                // Size-invariant noise UV:
                //   * glyph-local UV gives consistent noise density (independent of rendered text size).
                //   * A hash of the encoded atlas tile ID — shared across all 4 vertices of the glyph
                //     quad — adds a per-glyph-type offset so different letters dissolve with different
                //     patterns. Repeated occurrences of the same glyph share a pattern (cheap tradeoff;
                //     upgrade path: write cluster index into UV3 via MaterialModifier for true uniqueness).
                //   * Scroll (time-driven + static) and per-text offset (UV2.y) ride on top.
                float tileId = v.texcoord0.z;
                float2 tileHash = frac(float2(
                    sin(tileId * 0.012345) * 43758.5,
                    sin(tileId * 0.098765) * 22578.1));

                o.noiseUV = v.texcoord0.xy * _NoiseScale
                          + tileHash
                          + _NoiseScroll.xy * _Time.y
                          + _NoiseScroll.zw
                          + v.texcoord2.y;

                o.progress = v.texcoord2.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float signedDist = UniTextSampleSDF(i.atlasUV);
                float sdfAlpha = UniTextSDFAlpha(signedDist, i.glyphMeta.y, i.glyphMeta.x, i.glyphUV);

                float n = tex2D(_NoiseTex, i.noiseUV).r;

                // threshold falls from 1 → 0 as progress goes 0 → 1 (progress from UV2.x, per text)
                float threshold = 1.0 - saturate(i.progress);
                float soft = max(_EdgeSoftness, 1e-4);

                // Two smooth thresholds: 'edgeLow' rises past threshold, 'edgeHigh' rises past (threshold + edgeWidth).
                float edgeLow  = smoothstep(threshold - soft, threshold + soft, n);
                float edgeHigh = smoothstep(threshold + _EdgeWidth - soft, threshold + _EdgeWidth + soft, n);

                float faceMask = edgeHigh;
                float edgeMask = edgeLow - edgeHigh;

                float faceA = sdfAlpha * i.color.a * faceMask;
                float edgeA = sdfAlpha * _EdgeColor.a * edgeMask;

                // Premultiplied composite of face + edge (both SDF-clipped, both monotone inside glyph).
                half4 col;
                col.rgb = i.color.rgb * faceA + _EdgeColor.rgb * edgeA;
                col.a   = faceA + edgeA;

                return UniTextApplyClipping(col, i.mask);
            }
            ENDCG
        }
    }
}
