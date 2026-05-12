// Lit emoji rendering for UniTextWorld (world-space).
// Two SubShaders below — Unity picks the URP one in URP projects (RenderPipeline tag),
// otherwise falls through to the built-in one.
//
// Built-in path (second SubShader):
//   Ambient (SH9) + main directional + up to 4 nearest non-important point/spot lights
//   (vertex-evaluated through Shade4PointLights). Casts shadows via ShadowCaster pass
//   with bitmap alpha cutoff. Receive shadows is NOT supported on transparent in
//   built-in — architectural pipeline limit.
//
// URP path (first SubShader):
//   Ambient (SH probes) + main directional WITH receive shadows + per-pixel additional
//   lights with shadows + cluster/Forward+ when available. Casts shadows via URP
//   ShadowCaster. Compatible with URP 12 (Unity 2021.3 LTS) through URP 17 (Unity 6).
//
// World text only. Not intended for Canvas UI (no stencil/clip properties).

Shader "UniText/Lit/Emoji" {

Properties {
    [HideInInspector] _MainTex ("Emoji Atlas", 2DArray) = "" {}

    _LightInfluence   ("Light Influence (0 = unlit, 1 = fully lit)", Range(0, 1)) = 1
    _AmbientStrength  ("Ambient Strength", Range(0, 2)) = 1
    _DirectStrength   ("Directional Light Strength", Range(0, 2)) = 1

    _ShadowCutoff     ("Shadow Caster Cutoff", Range(0, 1)) = 0.5

    _CullMode         ("Cull Mode", Float) = 0
    _ColorMask        ("Color Mask", Float) = 15
}

// =================================================================================
// URP SubShader — picked by URP renderer via RenderPipeline tag.
// =================================================================================
SubShader {
    Tags
    {
        "Queue"          = "Transparent"
        "IgnoreProjector"= "True"
        "RenderType"     = "Transparent"
        "RenderPipeline" = "UniversalPipeline"
    }

    Cull      [_CullMode]
    ZWrite    Off
    ZTest     LEqual
    Blend     One OneMinusSrcAlpha
    ColorMask [_ColorMask]

    Pass {
        Name "UniversalForward"
        Tags { "LightMode" = "UniversalForward" }

        HLSLPROGRAM
        #pragma vertex   vert
        #pragma fragment frag
        #pragma target   3.5

        // URP main-light shadows (3-way: off / cascade / screen-space).
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

        // Additional lights — pixel or vertex.
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

        // Soft shadow filter quality.
        #pragma multi_compile_fragment _ _SHADOWS_SOFT

        // Light cookies.
        #pragma multi_compile_fragment _ _LIGHT_COOKIES

        // Light layers / Rendering Layer Mask (URP 14+; ignored on URP 12).
        #pragma multi_compile _ _LIGHT_LAYERS

        // Forward+ cluster light loop (URP 14+; ignored on URP 12).
        #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

        // SH probe evaluation mode (URP 14+).
        #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX

        #pragma multi_compile_fog
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        // LIGHT_LOOP_BEGIN / LIGHT_LOOP_END exist in URP 14+ (and switch to cluster
        // iteration when _CLUSTER_LIGHT_LOOP is active). On URP 12 the macros do not
        // exist — fall back to a plain index loop so the shader compiles unchanged.
        #ifndef LIGHT_LOOP_BEGIN
            #define LIGHT_LOOP_BEGIN(count) for (uint lightIndex = 0u; lightIndex < count; ++lightIndex) {
            #define LIGHT_LOOP_END          }
        #endif

        TEXTURE2D_ARRAY(_MainTex);
        SAMPLER(sampler_MainTex);

        half _LightInfluence;
        half _AmbientStrength;
        half _DirectStrength;

        struct Attributes
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            float4 texcoord   : TEXCOORD0;
        };

        struct Varyings
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
            float4 positionCS  : SV_POSITION;
            half4  color       : COLOR;
            float3 atlasUV     : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float3 positionWS  : TEXCOORD2;
            float  fogCoord    : TEXCOORD3;
        };

        half3 ApplyLight(Light light, half3 n)
        {
            half NdotL = saturate(dot(n, light.direction));
            return light.color * (NdotL * light.distanceAttenuation * light.shadowAttenuation);
        }

        Varyings vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
            float4 positionCS = TransformWorldToHClip(positionWS);

            o.positionCS  = positionCS;
            o.color       = v.color;
            o.color.rgb  *= o.color.a;
            o.atlasUV     = v.texcoord.xyz;
            // Batcher writes per-quad world-space normal directly into NORMAL.
            o.worldNormal = v.normalOS;
            o.positionWS  = positionWS;
            o.fogCoord    = ComputeFogFactor(positionCS.z);
            return o;
        }

        half4 frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);

            half4 col = SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, i.atlasUV.xy, i.atlasUV.z);

            // Two-sided lighting via face flip.
            half3 n = normalize(i.worldNormal) * (IS_FRONT_VFACE(facing, 1.0, -1.0));

            float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            half4  shadowMask  = half4(1, 1, 1, 1);
            Light  mainLight   = GetMainLight(shadowCoord, i.positionWS, shadowMask);

            half3 ambient = SampleSHVertex(n) * _AmbientStrength;
            half3 lit     = ambient + ApplyLight(mainLight, n) * _DirectStrength;

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                    lit += ApplyLight(addLight, n);
                LIGHT_LOOP_END
            #endif

            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lit += VertexLighting(i.positionWS, n);
            #endif

            col.rgb = lerp(col.rgb, col.rgb * lit, _LightInfluence);
            col    *= i.color;

            col.rgb = MixFog(col.rgb, i.fogCoord);

            return col;
        }
        ENDHLSL
    }

    Pass {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }

        ZWrite On
        ZTest LEqual
        ColorMask 0
        Cull [_CullMode]

        HLSLPROGRAM
        #pragma vertex   ShadowVert
        #pragma fragment ShadowFrag
        #pragma target   3.5

        #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        TEXTURE2D_ARRAY(_MainTex);
        SAMPLER(sampler_MainTex);
        half _ShadowCutoff;

        float3 _LightDirection;
        float3 _LightPosition;

        struct Attributes
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            float4 texcoord   : TEXCOORD0;
        };

        struct ShadowVaryings
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 positionCS : SV_POSITION;
            float3 atlasUV    : TEXCOORD0;
            half   tint       : TEXCOORD1;
        };

        float4 GetShadowPositionHClip(Attributes v)
        {
            float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
            float3 normalWS   = v.normalOS;  // batcher writes world-space normal

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

            float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            positionCS = ApplyShadowClamping(positionCS);
            return positionCS;
        }

        ShadowVaryings ShadowVert(Attributes v)
        {
            ShadowVaryings o = (ShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);

            o.positionCS = GetShadowPositionHClip(v);
            o.atlasUV    = v.texcoord.xyz;
            o.tint       = v.color.a;
            return o;
        }

        half4 ShadowFrag(ShadowVaryings i) : SV_Target
        {
            half4 col = SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, i.atlasUV.xy, i.atlasUV.z);
            clip(col.a * i.tint - _ShadowCutoff);
            return 0;
        }
        ENDHLSL
    }
}

// =================================================================================
// Built-in SubShader — picked when no SRP is active.
// =================================================================================
SubShader {
    Tags
    {
        "Queue"          = "Transparent"
        "IgnoreProjector"= "True"
        "RenderType"     = "Transparent"
    }

    Cull      [_CullMode]
    ZWrite    Off
    Lighting  Off
    ZTest     LEqual
    Blend     One OneMinusSrcAlpha
    ColorMask [_ColorMask]

    Pass {
        Name "EMOJI_LIT_FORWARDBASE"
        Tags { "LightMode" = "ForwardBase" }

        CGPROGRAM
        #pragma vertex   vert
        #pragma fragment frag
        #pragma target   3.5

        #pragma multi_compile __ VERTEXLIGHT_ON
        #pragma multi_compile_fog

        #include "UnityCG.cginc"

        UNITY_DECLARE_TEX2DARRAY(_MainTex);

        float4 _LightColor0;

        half _LightInfluence;
        half _AmbientStrength;
        half _DirectStrength;

        struct appdata
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 vertex   : POSITION;
            float3 normal   : NORMAL;
            float4 color    : COLOR;
            float4 texcoord : TEXCOORD0;
        };

        struct v2f
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
            float4 vertex      : SV_POSITION;
            fixed4 color       : COLOR;
            float3 atlasUV     : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;  // per-quad world-space normal written by batcher
            half3  vertexLight : TEXCOORD2;  // 4 nearest non-important lights, per-vertex
            UNITY_FOG_COORDS(3)
        };

        // Normal is per-quad world-space face normal (batcher writes it from cross product
        // of actual quad edges). VFACE flips sign for back-facing fragments — physically
        // correct two-sided lighting.
        half3 LightColor(half3 baseColor, half3 n, half3 vertexLight)
        {
            half  NdotL = max(0.0, dot(n, _WorldSpaceLightPos0.xyz));

            half3 ambient  = ShadeSH9(half4(n, 1.0)) * _AmbientStrength;
            half3 diffuse  = _LightColor0.rgb * NdotL * _DirectStrength;
            half3 lighting = ambient + diffuse + vertexLight;

            return lerp(baseColor, baseColor * lighting, _LightInfluence);
        }

        v2f vert(appdata v)
        {
            v2f o;

            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            float4 vPosition = UnityObjectToClipPos(v.vertex);
            o.vertex = vPosition;

            // Vertex colour tint, then premultiply. Lighting is applied in fragment to the
            // sampled bitmap itself so it affects per-texel colour, not just the vertex tint.
            o.color = v.color;
            o.color.rgb *= o.color.a;

            o.atlasUV     = v.texcoord.xyz;
            o.worldNormal = v.normal;

            // Per-vertex evaluation of up to 4 nearest non-important point/spot lights.
            // Compiled out when no scene non-important lights affect the object.
            #ifdef VERTEXLIGHT_ON
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.vertexLight = Shade4PointLights(
                    unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                    unity_LightColor[0].rgb, unity_LightColor[1].rgb,
                    unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                    unity_4LightAtten0,
                    worldPos, v.normal);
            #else
                o.vertexLight = 0;
            #endif

            UNITY_TRANSFER_FOG(o, vPosition);
            return o;
        }

        fixed4 frag(v2f i, float facing : VFACE) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);

            half4 col = UNITY_SAMPLE_TEX2DARRAY(_MainTex, i.atlasUV);
            half3 n = normalize(i.worldNormal) * sign(facing);
            col.rgb = LightColor(col.rgb, n, i.vertexLight);
            col *= i.color;

            // Classic fog for premultiplied output: mix toward unity_FogColor premultiplied by alpha.
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                half  fogFactor = saturate(i.fogCoord);
                half3 fogRGB    = unity_FogColor.rgb * col.a;
                col.rgb = lerp(fogRGB, col.rgb, fogFactor);
            #endif

            return col;
        }
        ENDCG
    }

    // Hard alpha cutoff against the bitmap. _ShadowCutoff lets the user trade silhouette
    // softness vs holes in the shadow (default 0.5 — standard cutout threshold).
    Pass {
        Name "EMOJI_LIT_SHADOWCASTER"
        Tags { "LightMode" = "ShadowCaster" }

        ZWrite On
        ZTest LEqual
        Cull [_CullMode]
        ColorMask 0

        CGPROGRAM
        #pragma vertex   VertShadow
        #pragma fragment FragShadow
        #pragma target   3.5

        #pragma multi_compile_shadowcaster

        #include "UnityCG.cginc"

        UNITY_DECLARE_TEX2DARRAY(_MainTex);
        half _ShadowCutoff;

        struct shadow_appdata
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 vertex   : POSITION;
            float3 normal   : NORMAL;
            float4 color    : COLOR;
            float4 texcoord : TEXCOORD0;
        };

        struct shadow_v2f
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            V2F_SHADOW_CASTER;
            float3 atlasUV : TEXCOORD1;        // V2F_SHADOW_CASTER claims TEXCOORD0 for SHADOWS_CUBE
            half   tint    : TEXCOORD2;        // vertex alpha modulates cutoff
        };

        shadow_v2f VertShadow(shadow_appdata v)
        {
            shadow_v2f o;
            UNITY_INITIALIZE_OUTPUT(shadow_v2f, o);
            UNITY_SETUP_INSTANCE_ID(v);

            // Plain TRANSFER_SHADOW_CASTER (not _NORMALOFFSET): batcher writes world-space
            // normals, the offset variant would re-apply ObjectToWorld to them.
            TRANSFER_SHADOW_CASTER(o)

            o.atlasUV = v.texcoord.xyz;
            o.tint    = v.color.a;
            return o;
        }

        float4 FragShadow(shadow_v2f i) : SV_Target
        {
            half4 col = UNITY_SAMPLE_TEX2DARRAY(_MainTex, i.atlasUV);
            clip(col.a * i.tint - _ShadowCutoff);

            SHADOW_CASTER_FRAGMENT(i)
        }
        ENDCG
    }
}
Fallback "UniText/Emoji"
}
