// Template for a custom UniText material shader.
// Copy this file (or use "Create > UniText > Custom Material Shader"), rename the Shader "..." line,
// and extend the vertex/fragment functions with your own effect logic.
//
// The MaterialModifier will assign this shader's material to a text range, set _MainTex to the
// current glyph atlas, and write per-glyph data you configure into TEXCOORD2 and TEXCOORD3.

Shader "UniText/Custom/Example"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2DArray) = "" {}

        // --- UniText keywords. In the default (cloneMaterial = true) workflow the batcher drives
        // these automatically; they stay here as checkboxes in the Material Inspector so users in
        // cloneMaterial = false mode can pick the correct atlas format manually. ---
        [Toggle(UNITEXT_MSDF)]  _UniText_IsMsdf  ("UniText: MSDF Mode",  Float) = 0
        [Toggle(UNITEXT_EMOJI)] _UniText_IsEmoji ("UniText: Emoji Mode", Float) = 0

        _Tint           ("Tint",              Color)  = (1, 1, 1, 1)

        // --- Required by UniText / UGUI. Leave these as-is. ---
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
            Name "UNITEXT_CUSTOM_EXAMPLE"
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../UniText_Custom.cginc"

            fixed4 _Tint;

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
                float4 userA     : TEXCOORD5;
                float4 userB     : TEXCOORD6;
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

                fixed4 vertColor = UniTextGammaToLinearIfNeeded(v.color);

                o.vertex    = clipPos;
                o.atlasUV   = UniTextComputeAtlasUV(v.texcoord0, v.texcoord1);
                o.glyphUV   = v.texcoord0.xy;
                o.mask      = UniTextComputeMask(v.vertex, pixelSize);
                o.color     = vertColor * _Tint;
                o.glyphMeta = float2(v.texcoord0.w, v.texcoord1.y);
                o.userA     = v.texcoord2;
                o.userB     = v.texcoord3;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                #ifdef UNITEXT_EMOJI
                    // Emoji atlas: just tint the RGBA sample.
                    half4 col = UniTextSampleAtlas(i.atlasUV) * i.color;
                #else
                    // SDF/MSDF atlas: signed-distance → alpha.
                    float signedDist = UniTextSampleSDF(i.atlasUV);
                    float alpha = UniTextSDFAlpha(signedDist, i.glyphMeta.y, i.glyphMeta.x, i.glyphUV);
                    half4 col = i.color * alpha;
                    col.rgb *= col.a; // premultiplied
                #endif

                return UniTextApplyClipping(col, i.mask);
            }
            ENDCG
        }
    }
}
