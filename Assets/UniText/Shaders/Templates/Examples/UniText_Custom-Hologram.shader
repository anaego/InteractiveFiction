// Hologram — iridescent vertical hue shift, horizontal scanlines, subtle noise flicker
// and soft edge glow. Self-animating via _Time.
//
// Per-text dynamic parameters (read from TEXCOORD2):
//   UV2.x = hue phase offset  (0 = material default; shift to desynchronize multiple hologram texts)
//   UV2.y = flicker phase offset
//   UV2.z = scan phase offset
//   UV2.w = intensity override (0 = use material default, otherwise multiplier on final alpha)
//
// Animate via `MaterialModifier.ConstantUv2 = ...` from script. Material-wide constants
// (hue scale/speed, saturation, scan freq, flicker scale, edge color, tint) stay in Properties.

Shader "UniText/Custom/Hologram"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2DArray) = "" {}

        [Toggle(UNITEXT_MSDF)]  _UniText_IsMsdf  ("UniText: MSDF Mode",  Float) = 0
        [Toggle(UNITEXT_EMOJI)] _UniText_IsEmoji ("UniText: Emoji Mode", Float) = 0

        _NoiseTex       ("Noise",         2D)      = "white" {}

        [HDR] _Tint     ("Tint",          Color)   = (1, 1, 1, 1)
        _HueScale       ("Hue Scale",     Float)   = 0.003
        _HueSpeed       ("Hue Speed",     Float)   = 0.1
        _Saturation     ("Saturation",    Range(0,1)) = 0.85
        _Brightness     ("Brightness",    Range(0,3)) = 1.1

        _ScanFreq       ("Scanline Freq",  Float) = 45
        _ScanSpeed      ("Scanline Speed", Float) = -3
        _ScanContrast   ("Scanline Contrast", Range(0,1)) = 0.45

        _FlickerScale   ("Flicker Noise Scale", Float) = 2
        _FlickerSpeed   ("Flicker Speed",       Float) = 0.7
        _FlickerAmount  ("Flicker Amount",      Range(0,1)) = 0.25

        [HDR] _EdgeColor ("Edge Glow Color", Color) = (0.6, 0.9, 1.4, 1)
        _EdgeWidth      ("Edge Glow Width", Range(0, 0.3)) = 0.08

        // Mesh-generation hint read by MaterialModifier: how many em-units of padding each glyph
        // quad needs so the edge glow isn't clipped by the default glyph bounds.
        [HideInInspector] _UniTextMeshPadding ("Quad Padding (em)", Float) = 0.12

        // Per-text parameters (read from sub-mesh UV2 — set via MaterialModifier inspector or ConstantUv2).
        [HideInInspector] _UniTextInstUv2X ("Hue Phase Offset",     Range(0,1)) = 0
        [HideInInspector] _UniTextInstUv2Y ("Flicker Phase Offset", Float)      = 0
        [HideInInspector] _UniTextInstUv2Z ("Scan Phase Offset",    Float)      = 0
        [HideInInspector] _UniTextInstUv2W ("Intensity (0 = off)",  Range(0,2)) = 1

        _ClipRect       ("Clip Rect",     Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX  ("Mask SoftnessX", Float) = 0
        _MaskSoftnessY  ("Mask SoftnessY", Float) = 0

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
            Name "UNITEXT_CUSTOM_HOLOGRAM"
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../../UniText_Custom.cginc"

            sampler2D _NoiseTex;

            half4 _Tint;
            float _HueScale, _HueSpeed, _Saturation, _Brightness;
            float _ScanFreq, _ScanSpeed, _ScanContrast;
            float _FlickerScale, _FlickerSpeed, _FlickerAmount;
            half4 _EdgeColor;
            float _EdgeWidth;

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
                float2 tileHash  : TEXCOORD5; // per-letter random seed (shared across all 4 verts of the glyph)
                float4 instance  : TEXCOORD6; // per-text: x=huePhase, y=flickerPhase, z=scanPhase, w=intensity
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
                o.instance  = v.texcoord2;

                // Per-letter hash seeded from the atlas tile id — identical for all 4 quad verts of the
                // glyph. Drives hue phase offset and flicker noise offset so each letter looks unique
                // while staying size-invariant (hue range, scanline density and flicker detail don't
                // depend on rendered text size — only on glyph-local UV).
                float tileId = v.texcoord0.z;
                o.tileHash = frac(float2(
                    sin(tileId * 0.012345) * 43758.5,
                    sin(tileId * 0.098765) * 22578.1));

                return o;
            }

            // IQ cosine-palette rainbow — smooth, cheap, no branching.
            half3 Rainbow(float t)
            {
                const half3 phase = half3(0.0, 0.333, 0.667);
                return lerp(half3(1,1,1), 0.5 + 0.5 * cos(6.28318 * (t + phase)), _Saturation) * _Brightness;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float signedDist = UniTextSampleSDF(i.atlasUV);

                // Face SDF alpha + soft edge band for rim glow.
                float2 dUV = fwidth(i.glyphUV);
                float aaWidth = max(dUV.x, dUV.y) * i.glyphMeta.x;
                float faceDist = signedDist - i.glyphMeta.y * UNITEXT_DILATE_SCALE;

                float faceA = saturate(-faceDist / aaWidth + 0.5);

                // Edge glow: band near the outside of the glyph boundary.
                float edgeA = saturate(1.0 - abs(faceDist) / (_EdgeWidth + aaWidth));
                edgeA = edgeA * edgeA;
                edgeA *= 1.0 - faceA; // only on the outer side of the boundary

                // Iridescent hue: per-letter base (from tile hash) + soft gradient within the glyph
                // along Y + time + per-text phase. Size-invariant.
                float hue = i.tileHash.x + i.glyphUV.y * _HueScale + _Time.y * _HueSpeed + i.instance.x;
                half3 iridescent = Rainbow(hue) * _Tint.rgb;

                // Horizontal scanlines riding on glyph-local Y (text-space, consistent pitch per letter).
                float scan = sin(i.glyphUV.y * _ScanFreq + _Time.y * _ScanSpeed + i.instance.z) * 0.5 + 0.5;
                float scanMul = lerp(1.0 - _ScanContrast, 1.0, scan);

                // Flicker noise: glyph-local UV + tile hash (same approach as Dissolve — density stays
                // consistent per letter regardless of rendered text size).
                float2 flickerUV = i.glyphUV * _FlickerScale + i.tileHash + _Time.y * _FlickerSpeed + i.instance.y;
                float flickerN = tex2D(_NoiseTex, flickerUV).r;
                float flickerMul = lerp(1.0 - _FlickerAmount, 1.0, flickerN);

                // Optional per-text intensity override (0 = use material default, else multiply final alpha).
                float instIntensity = (i.instance.w > 0.0) ? i.instance.w : 1.0;

                half3 faceRgb = iridescent * scanMul * flickerMul;
                float faceAlpha = faceA * i.color.a * _Tint.a * flickerMul * instIntensity;

                half3 edgeRgb = _EdgeColor.rgb;
                float edgeAlpha = edgeA * _EdgeColor.a * i.color.a * flickerMul * instIntensity;

                // Premultiplied additive composite (face + edge glow).
                half4 col;
                col.rgb = faceRgb * faceAlpha + edgeRgb * edgeAlpha;
                col.a   = faceAlpha + edgeAlpha;

                return UniTextApplyClipping(col, i.mask);
            }
            ENDCG
        }
    }
}
