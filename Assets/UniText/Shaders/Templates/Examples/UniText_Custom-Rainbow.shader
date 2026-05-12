// Rainbow — smooth hue gradient stepping from letter to letter, slowly shimmering over time.
// No textures, no per-glyph data — plug it in, see colors.
//
// Per-text dynamic parameters (read from TEXCOORD2):
//   UV2.x = hue offset added on top of material _HueOffset (lets two texts sharing the same
//           material start at different colors — set via MaterialModifier.ConstantUv2).
//
// Implementation — hue is driven by `v.texcoord1.z + v.texcoord1.w`, which together form a
// fractional "position in line": .z is the integer cluster index (0, 1, 2, …) written by the
// mesh generator, .w is the intra-glyph X fraction (0 on the left edge of a letter, 1 on the
// right — interpolated by the GPU). Their sum gives a smoothly increasing value across AND within
// letters, so rainbow flows seamlessly. Both values live in a UV channel, bypassing the world
// batcher's vertex transform — Canvas and World render identically, stable at any size/zoom/pos.
// Set _HueScale ≈ 1/N for a full rainbow cycle over N letters.

Shader "UniText/Custom/Rainbow"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2DArray) = "" {}

        [Toggle(UNITEXT_MSDF)]  _UniText_IsMsdf  ("UniText: MSDF Mode",  Float) = 0
        [Toggle(UNITEXT_EMOJI)] _UniText_IsEmoji ("UniText: Emoji Mode", Float) = 0

        _HueScale       ("Hue Scale (per-glyph step)", Float) = 0.05
        _HueOffset      ("Hue Offset",        Float)   = 0
        _HueSpeed       ("Hue Speed",         Float)   = 0.15
        _Saturation     ("Saturation",        Range(0,1)) = 1
        _Brightness     ("Brightness",        Range(0,2)) = 1

        // Per-text parameters (read from sub-mesh UV2 — set via MaterialModifier inspector or ConstantUv2).
        [HideInInspector] _UniTextInstUv2X ("Hue Offset", Range(0,1)) = 0

        _ClipRect       ("Clip Rect",         Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX  ("Mask SoftnessX",    Float)  = 0
        _MaskSoftnessY  ("Mask SoftnessY",    Float)  = 0

        _StencilComp    ("Stencil Comparison", Float) = 8
        _Stencil        ("Stencil ID",         Float) = 0
        _StencilOp      ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255

        _CullMode       ("Cull Mode",          Float) = 0
        _ColorMask      ("Color Mask",         Float) = 15
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
            Name "UNITEXT_CUSTOM_RAINBOW"
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../../UniText_Custom.cginc"

            float _HueScale;
            float _HueOffset;
            float _HueSpeed;
            float _Saturation;
            float _Brightness;

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
                float2 huePhase  : TEXCOORD5; // x = fractional cluster (cluster + intra-glyph 0..1), y = per-text hue offset
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
                o.huePhase  = float2(v.texcoord1.z + v.texcoord1.w, v.texcoord2.x);
                return o;
            }

            // Iñigo Quilez cosine-palette rainbow — cheap, smooth, no branching.
            half3 Rainbow(float t)
            {
                const half3 phase = half3(0.0, 0.333, 0.667);
                return lerp(half3(1,1,1), 0.5 + 0.5 * cos(6.28318 * (t + phase)), _Saturation) * _Brightness;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float signedDist = UniTextSampleSDF(i.atlasUV);
                float alpha = UniTextSDFAlpha(signedDist, i.glyphMeta.y, i.glyphMeta.x, i.glyphUV);

                float hue = i.huePhase.x * _HueScale + _HueOffset + i.huePhase.y + _Time.y * _HueSpeed;
                half3 rgb = Rainbow(hue);

                half4 col;
                col.rgb = rgb * i.color.rgb;
                col.a   = i.color.a * alpha;
                col.rgb *= col.a;

                return UniTextApplyClipping(col, i.mask);
            }
            ENDCG
        }
    }
}
