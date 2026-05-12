// Lit SDF text rendering for UniTextWorld (world-space).
// Two SubShaders below — Unity picks the URP one in URP projects (RenderPipeline tag),
// otherwise falls through to the built-in one.
//
// Built-in path (second SubShader):
//   Ambient (SH9) + main directional light + up to 4 nearest non-important point/spot
//   lights (vertex-evaluated through Shade4PointLights, batching survives, no ForwardAdd).
//   Casts shadows via a dedicated ShadowCaster pass with SDF alpha cutoff.
//   Receive shadows is NOT supported on transparent in built-in — this is an
//   architectural limit of the pipeline, not a shader gap.
//
// URP path (first SubShader):
//   Ambient (SH probes) + main directional light WITH receive shadows (transparent works
//   in URP) + per-pixel additional lights with shadows + cluster/Forward+ when available.
//   Casts shadows via a URP-style ShadowCaster pass.
//   Compatible with URP 12 (Unity 2021.3 LTS) through URP 17 (Unity 6) — version-specific
//   keywords (_CLUSTER_LIGHT_LOOP, _LIGHT_LAYERS, EVALUATE_SH_*) are silently ignored on
//   URP versions that don't recognize them.
//
// VFACE flips the geometric normal for back-facing fragments — physically correct
// two-sided shading. World-space only — no stencil/clip.

Shader "UniText/Lit/SDF" {

Properties {
    [HideInInspector] _MainTex ("Font Atlas", 2DArray) = "" {}

    _LightInfluence     ("Light Influence (0 = unlit, 1 = fully lit)", Range(0, 1)) = 1
    _AmbientStrength    ("Ambient Strength", Range(0, 2)) = 1
    _DirectStrength     ("Directional Light Strength", Range(0, 2)) = 1

    _CullMode           ("Cull Mode", Float) = 0
    _ColorMask          ("Color Mask", Float) = 15
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

    Cull       [_CullMode]
    ZWrite     Off
    ZTest      LEqual
    Blend      One OneMinusSrcAlpha
    ColorMask  [_ColorMask]

    Pass {
        Name "UniversalForward"
        Tags { "LightMode" = "UniversalForward" }

        HLSLPROGRAM
        #pragma vertex   VertShader
        #pragma fragment PixShader
        #pragma target   3.5

        #pragma multi_compile __ UNITEXT_MSDF

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

        // Forward+ cluster light loop (URP 14+ as _FORWARD_PLUS, renamed to
        // _CLUSTER_LIGHT_LOOP in URP 17; ignored on URP 12).
        #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

        // SH probe evaluation mode (URP 14+).
        #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX

        #pragma multi_compile_fog
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "UniText_SDF-CommonURP.hlsl"

        // LIGHT_LOOP_BEGIN / LIGHT_LOOP_END exist in URP 14+ (and switch to cluster
        // iteration when _CLUSTER_LIGHT_LOOP is active). On URP 12 the macros do not
        // exist — fall back to a plain index loop so the shader compiles unchanged.
        #ifndef LIGHT_LOOP_BEGIN
            #define LIGHT_LOOP_BEGIN(count) for (uint lightIndex = 0u; lightIndex < count; ++lightIndex) {
            #define LIGHT_LOOP_END          }
        #endif

        half _LightInfluence;
        half _AmbientStrength;
        half _DirectStrength;

        struct Varyings
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
            float4 positionCS  : SV_POSITION;
            float3 atlasUV     : TEXCOORD0;
            float2 glyphUV     : TEXCOORD1;
            half4  params      : TEXCOORD2;  // x=faceDilate y=effectDilate z=softness w=glyphH
            half4  faceColor   : TEXCOORD3;
            half4  effectColor : TEXCOORD4;
            float3 worldNormal : TEXCOORD5;
            float3 positionWS  : TEXCOORD6;
            float  fogCoord    : TEXCOORD7;
        };

        // Adds one URP Light's Lambert contribution, attenuated by its distance and
        // shadow factors. URP's Light struct already encodes all of these — just multiply.
        half3 ApplyLight(Light light, half3 n)
        {
            half NdotL = saturate(dot(n, light.direction));
            return light.color * (NdotL * light.distanceAttenuation * light.shadowAttenuation);
        }

        Varyings VertShader(sdf_vertex_t input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float4 positionCS = TransformWorldToHClip(positionWS);

            half4 color = input.color;
            half  vertexAlpha = color.a;

            half4 faceColor = color;
            faceColor.rgb *= faceColor.a;

            half4 effectCol = UnpackColor(input.texcoord2.y, input.texcoord2.z);
            effectCol.a   *= vertexAlpha;
            effectCol.rgb *= effectCol.a;

            float glyphH       = input.texcoord0.w;
            float faceDilate   = input.texcoord1.y;
            float effectDilate = input.texcoord2.x;
            float effectSoft   = input.texcoord2.w;
            float2 sdfScale, sdfOffset;
            float  pageLayer;
            ComputeSDFTransform(input.texcoord0.z, input.texcoord1.x, glyphH, sdfScale, sdfOffset, pageLayer);

            output.positionCS  = positionCS;
            output.atlasUV     = float3(input.texcoord0.xy * sdfScale + sdfOffset, pageLayer);
            output.glyphUV     = input.texcoord0.xy;
            output.params      = half4(faceDilate, effectDilate, effectSoft, glyphH);
            output.faceColor   = faceColor;
            output.effectColor = effectCol;
            // Batcher writes per-quad world-space normal directly into NORMAL — pass through.
            output.worldNormal = input.normalOS;
            output.positionWS  = positionWS;
            output.fogCoord    = ComputeFogFactor(positionCS.z);
            return output;
        }

        half4 PixShader(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            float glyphH     = input.params.w;
            float faceDilate = input.params.x;

            float signedDist = SAMPLE_SDF(input.atlasUV);

            float2 dUV = fwidth(input.glyphUV);
            float  aaWidth = max(dUV.x, dUV.y) * glyphH;

            half4 result;

            if (input.effectColor.a < 0.001)
            {
                float faceDist = signedDist - faceDilate * DILATE_SCALE;
                float alpha    = saturate(-faceDist / aaWidth + 0.5);
                result = input.faceColor * alpha;
            }
            else
            {
                float effectDilate = input.params.y * DILATE_SCALE;
                float threshold    = -(faceDilate * DILATE_SCALE + effectDilate);
                float softEdge     = max(aaWidth, input.params.z);
                float alpha        = saturate((-signedDist - threshold) / softEdge);
                result = input.effectColor * alpha;
            }

            // Two-sided lighting: flip the geometric normal for back-facing fragments.
            half3 n = normalize(input.worldNormal) * (IS_FRONT_VFACE(facing, 1.0, -1.0));

            // Main light + cascaded shadows. TransformWorldToShadowCoord handles
            // screen-space and cascade modes internally based on multi-compile keyword.
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            half4  shadowMask  = half4(1, 1, 1, 1);
            Light  mainLight   = GetMainLight(shadowCoord, input.positionWS, shadowMask);

            // Ambient via SH probes — replaces ShadeSH9 in built-in.
            half3 ambient = SampleSHVertex(n) * _AmbientStrength;
            half3 lit     = ambient + ApplyLight(mainLight, n) * _DirectStrength;

            // Additional lights (pixel path). LIGHT_LOOP_BEGIN/END resolves to either a
            // simple loop (URP 12 / non-Forward+) or a cluster iterator (URP 14+ / Forward+).
            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    lit += ApplyLight(addLight, n);
                LIGHT_LOOP_END
            #endif

            // Vertex-path additional lights — URP evaluates these in vertex stage and
            // exposes the result through GetVertexLighting. We don't request a vertex
            // interpolator slot, so call it in fragment with positionWS as input (a few
            // extra ALU per fragment, but keeps the pixel-path Lit shader the same shape).
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lit += VertexLighting(input.positionWS, n);
            #endif

            result.rgb = lerp(result.rgb, result.rgb * lit, _LightInfluence);

            // Fog with premultiplied alpha: blend RGB toward fog colour multiplied by the
            // fragment's alpha so faded fragments still preserve their SDF shape.
            result.rgb = MixFog(result.rgb, input.fogCoord);

            return result;
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

        #pragma multi_compile __ UNITEXT_MSDF

        // Tells URP to use punctual-light normal bias when this caster is in a spot/point
        // shadow map; on directional cascades it stays defined to 0.
        #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "UniText_SDF-CommonURP.hlsl"

        float3 _LightDirection;
        float3 _LightPosition;

        struct ShadowVaryings
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 positionCS : SV_POSITION;
            float3 atlasUV    : TEXCOORD0;
            half3  params     : TEXCOORD1;  // x=faceDilate y=effectDilate z=effectAlpha
        };

        float4 GetShadowPositionHClip(sdf_vertex_t v)
        {
            float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
            // Batcher already writes a world-space face normal — use it directly.
            float3 normalWS   = v.normalOS;

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

            float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            positionCS = ApplyShadowClamping(positionCS);
            return positionCS;
        }

        ShadowVaryings ShadowVert(sdf_vertex_t v)
        {
            ShadowVaryings o = (ShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);

            o.positionCS = GetShadowPositionHClip(v);

            float glyphH       = v.texcoord0.w;
            float faceDilate   = v.texcoord1.y;
            float effectDilate = v.texcoord2.x;
            float2 sdfScale, sdfOffset;
            float  pageLayer;
            ComputeSDFTransform(v.texcoord0.z, v.texcoord1.x, glyphH, sdfScale, sdfOffset, pageLayer);

            half4 effectCol   = UnpackColor(v.texcoord2.y, v.texcoord2.z);
            half  effectAlpha = effectCol.a * v.color.a;

            o.atlasUV = float3(v.texcoord0.xy * sdfScale + sdfOffset, pageLayer);
            o.params  = half3(faceDilate, effectDilate, effectAlpha);
            return o;
        }

        half4 ShadowFrag(ShadowVaryings i) : SV_Target
        {
            float signedDist   = SAMPLE_SDF(i.atlasUV);
            half  faceDilate   = i.params.x;
            half  effectDilate = i.params.y;
            half  effectAlpha  = i.params.z;

            // Effect mode (outline/glow) inflates the silhouette by both face and effect
            // dilate, so the cast shadow matches the visible outline rather than just the
            // core glyph.
            float threshold = (effectAlpha < 0.001)
                ? faceDilate * DILATE_SCALE
                : (faceDilate + effectDilate) * DILATE_SCALE;

            clip(threshold - signedDist);
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

    Cull       [_CullMode]
    ZWrite     Off
    Lighting   Off
    ZTest      LEqual
    Blend      One OneMinusSrcAlpha
    ColorMask  [_ColorMask]

    Pass {
        Name "SDF_LIT_FORWARDBASE"
        Tags { "LightMode" = "ForwardBase" }

        CGPROGRAM
        #pragma vertex   VertShader
        #pragma fragment PixShader
        #pragma target   3.5

        #pragma multi_compile __ UNITEXT_MSDF
        #pragma multi_compile __ VERTEXLIGHT_ON
        #pragma multi_compile_fog

        #include "UnityCG.cginc"
        #include "UniText_SDF-Common.cginc"

        float4 _LightColor0;

        half _LightInfluence;
        half _AmbientStrength;
        half _DirectStrength;

        struct pixel_t
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
            float4 vertex      : SV_POSITION;
            float3 atlasUV     : TEXCOORD0;  // xy = atlas UV, z = page layer
            float2 glyphUV     : TEXCOORD1;  // for fwidth AA
            half4  params      : TEXCOORD2;  // x = faceDilate, y = effectDilate, z = softness, w = glyphH
            fixed4 faceColor   : TEXCOORD3;  // premultiplied vertex color (face mode, unlit)
            fixed4 effectColor : TEXCOORD4;  // premultiplied effect color; a == 0 → face mode
            float3 worldNormal : TEXCOORD5;  // per-quad world-space normal written by batcher
            half3  vertexLight : TEXCOORD6;  // 4 nearest non-important lights, per-vertex
            UNITY_FOG_COORDS(7)
        };

        pixel_t VertShader(sdf_vertex_t input)
        {
            pixel_t output;

            UNITY_INITIALIZE_OUTPUT(pixel_t, output);
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float4 vPosition = UnityObjectToClipPos(input.vertex);

            fixed4 color = GammaToLinearIfNeeded(input.color);
            half vertexAlpha = color.a;

            fixed4 faceColor = color;
            faceColor.rgb *= faceColor.a;

            float effectDilate = input.texcoord2.x;
            float effectSoft   = input.texcoord2.w;

            half4 effectCol = UnpackColor(input.texcoord2.y, input.texcoord2.z);
            effectCol.a   *= vertexAlpha;
            effectCol.rgb *= effectCol.a;

            float glyphH     = input.texcoord0.w;
            float faceDilate = input.texcoord1.y;
            float2 sdfScale, sdfOffset;
            float  pageLayer;
            ComputeSDFTransform(input.texcoord0.z, input.texcoord1.x, glyphH, sdfScale, sdfOffset, pageLayer);

            // UniTextWorldBatcher writes per-quad world-space face normal into NORMAL
            // (cross product of actual quad edges — survives per-glyph rotation from modifiers).
            output.worldNormal = input.normal;

            // Per-vertex evaluation of up to 4 nearest non-important point/spot lights.
            // Compiled out when no scene non-important lights affect the object — Unity
            // strips the variant and Shade4PointLights becomes dead code.
            #ifdef VERTEXLIGHT_ON
                float3 worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.vertexLight = Shade4PointLights(
                    unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                    unity_LightColor[0].rgb, unity_LightColor[1].rgb,
                    unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                    unity_4LightAtten0,
                    worldPos, input.normal);
            #else
                output.vertexLight = 0;
            #endif

            output.vertex      = vPosition;
            output.atlasUV     = float3(input.texcoord0.xy * sdfScale + sdfOffset, pageLayer);
            output.glyphUV     = input.texcoord0.xy;
            output.params      = half4(faceDilate, effectDilate, effectSoft, glyphH);
            output.faceColor   = faceColor;
            output.effectColor = effectCol;
            UNITY_TRANSFER_FOG(output, vPosition);

            return output;
        }

        half3 ComputeLighting(half3 n, half3 vertexLight)
        {
            half  NdotL   = max(0.0, dot(n, _WorldSpaceLightPos0.xyz));
            half3 ambient = ShadeSH9(half4(n, 1.0)) * _AmbientStrength;
            half3 direct  = _LightColor0.rgb * NdotL * _DirectStrength;
            return ambient + direct + vertexLight;
        }

        fixed4 PixShader(pixel_t input, float facing : VFACE) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            float glyphH     = input.params.w;
            float faceDilate = input.params.x;

            float signedDist = SAMPLE_SDF(input.atlasUV);

            float2 dUV = fwidth(input.glyphUV);
            float  aaWidth = max(dUV.x, dUV.y) * glyphH;

            half4 result;

            if (input.effectColor.a < 0.001)
            {
                float faceDist = signedDist - faceDilate * DILATE_SCALE;
                float alpha = saturate(-faceDist / aaWidth + 0.5);
                result = input.faceColor * alpha;
            }
            else
            {
                float effectDilate = input.params.y * DILATE_SCALE;
                float threshold = -(faceDilate * DILATE_SCALE + effectDilate);
                float softEdge = max(aaWidth, input.params.z);
                float alpha = saturate((-signedDist - threshold) / softEdge);
                result = input.effectColor * alpha;
            }

            half3 n = normalize(input.worldNormal) * sign(facing);
            half3 lit = ComputeLighting(n, input.vertexLight);
            result.rgb = lerp(result.rgb, result.rgb * lit, _LightInfluence);

            // Classic fog for premultiplied output: mix toward unity_FogColor premultiplied by alpha,
            // so distant fragments take the scene fog colour while preserving the glyph's alpha shape.
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                half  fogFactor = saturate(input.fogCoord);
                half3 fogRGB    = unity_FogColor.rgb * result.a;
                result.rgb = lerp(fogRGB, result.rgb, fogFactor);
            #endif

            return result;
        }
        ENDCG
    }

    // SDF-driven hard cutoff. No anti-aliasing in shadow maps — gives clean silhouette
    // shadows (TMP_SDF and Unity Standard cutout do the same).
    Pass {
        Name "SDF_LIT_SHADOWCASTER"
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
        #pragma multi_compile __ UNITEXT_MSDF

        #include "UnityCG.cginc"
        #include "UniText_SDF-Common.cginc"

        struct shadow_v2f
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            V2F_SHADOW_CASTER;
            float3 atlasUV : TEXCOORD1;          // V2F_SHADOW_CASTER claims TEXCOORD0 for SHADOWS_CUBE
            half3  params  : TEXCOORD2;          // x = faceDilate, y = effectDilate, z = effectAlpha
        };

        shadow_v2f VertShadow(sdf_vertex_t v)
        {
            shadow_v2f o;
            UNITY_INITIALIZE_OUTPUT(shadow_v2f, o);
            UNITY_SETUP_INSTANCE_ID(v);

            // Plain TRANSFER_SHADOW_CASTER (not _NORMALOFFSET): batcher writes world-space
            // normals, the offset variant would re-apply ObjectToWorld to them.
            TRANSFER_SHADOW_CASTER(o)

            float glyphH       = v.texcoord0.w;
            float faceDilate   = v.texcoord1.y;
            float effectDilate = v.texcoord2.x;
            float2 sdfScale, sdfOffset;
            float  pageLayer;
            ComputeSDFTransform(v.texcoord0.z, v.texcoord1.x, glyphH, sdfScale, sdfOffset, pageLayer);

            half4 effectCol  = UnpackColor(v.texcoord2.y, v.texcoord2.z);
            half  effectAlpha = effectCol.a * v.color.a;

            o.atlasUV = float3(v.texcoord0.xy * sdfScale + sdfOffset, pageLayer);
            o.params  = half3(faceDilate, effectDilate, effectAlpha);
            return o;
        }

        float4 FragShadow(shadow_v2f i) : SV_Target
        {
            float signedDist   = SAMPLE_SDF(i.atlasUV);
            half  faceDilate   = i.params.x;
            half  effectDilate = i.params.y;
            half  effectAlpha  = i.params.z;

            // Effect mode (outline/glow) inflates the silhouette by both face and effect dilate,
            // so the cast shadow matches the visible outline rather than just the core glyph.
            float threshold = (effectAlpha < 0.001)
                ? faceDilate * DILATE_SCALE
                : (faceDilate + effectDilate) * DILATE_SCALE;

            clip(threshold - signedDist);

            SHADOW_CASTER_FRAGMENT(i)
        }
        ENDCG
    }
}
Fallback "UniText/SDF"
}
