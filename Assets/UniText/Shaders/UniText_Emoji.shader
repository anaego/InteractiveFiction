// Emoji rendering — bitmap glyphs from a Texture2DArray atlas.
// Page layer index passed via TEXCOORD0.z from the mesh generator.

Shader "UniText/Emoji" {

Properties {
	[HideInInspector] _MainTex ("Emoji Atlas", 2DArray) = "" {}

	_ClipRect			("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
	_MaskSoftnessX		("Mask SoftnessX", float) = 0
	_MaskSoftnessY		("Mask SoftnessY", float) = 0

	_StencilComp		("Stencil Comparison", Float) = 8
	_Stencil			("Stencil ID", Float) = 0
	_StencilOp			("Stencil Operation", Float) = 0
	_StencilWriteMask	("Stencil Write Mask", Float) = 255
	_StencilReadMask	("Stencil Read Mask", Float) = 255

	_ColorMask			("Color Mask", Float) = 15
}

SubShader {
	Tags
	{
		"Queue"="Transparent"
		"IgnoreProjector"="True"
		"RenderType"="Transparent"
	}

	Stencil
	{
		Ref [_Stencil]
		Comp [_StencilComp]
		Pass [_StencilOp]
		ReadMask [_StencilReadMask]
		WriteMask [_StencilWriteMask]
	}

	Cull Off
	ZWrite Off
	Lighting Off
	Fog { Mode Off }
	ZTest [unity_GUIZTestMode]
	Blend One OneMinusSrcAlpha
	ColorMask [_ColorMask]

	Pass {
		Name "EMOJI"
		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#pragma target 3.5

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP

		#include "UnityCG.cginc"
		#include "UnityUI.cginc"

		UNITY_DECLARE_TEX2DARRAY(_MainTex);
		float4 _ClipRect;
		float _MaskSoftnessX;
		float _MaskSoftnessY;
		float _UIMaskSoftnessX;
		float _UIMaskSoftnessY;

		struct appdata
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			float4 vertex   : POSITION;
			float4 color    : COLOR;
			float4 texcoord : TEXCOORD0;
		};

		struct v2f
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4 vertex    : SV_POSITION;
			fixed4 color     : COLOR;
			float3 atlasUV   : TEXCOORD0;
			half4  mask      : TEXCOORD1;
		};

		v2f vert(appdata v)
		{
			v2f o;

			UNITY_INITIALIZE_OUTPUT(v2f, o);
			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_TRANSFER_INSTANCE_ID(v, o);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

			float4 vPosition = UnityObjectToClipPos(v.vertex);
			o.vertex = vPosition;
			o.color = v.color;
			o.color.rgb *= o.color.a;
			o.atlasUV = v.texcoord.xyz;

			float2 pixelSize = vPosition.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
			half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
			float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
			o.mask = half4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + abs(pixelSize.xy)));

			return o;
		}

		fixed4 frag(v2f i) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(i);

			half4 col = UNITY_SAMPLE_TEX2DARRAY(_MainTex, i.atlasUV);
			col *= i.color;

			#ifdef UNITY_UI_CLIP_RECT
			half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
			col *= m.x * m.y;
			#endif

			#ifdef UNITY_UI_ALPHACLIP
			clip(col.a - 0.001);
			#endif

			return col;
		}
		ENDCG
	}
}
}
