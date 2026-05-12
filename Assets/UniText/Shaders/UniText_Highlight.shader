// Flat-colored transparent quads for UniText range highlights in world space.
// Color is taken from per-vertex COLOR — one shared material serves every highlight
// instance with no per-instance material allocation.

Shader "UniText/Highlight"
{
	SubShader
	{
		Tags
		{
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"PreviewType"="Plane"
		}

		Cull Off
		ZWrite Off
		Lighting Off
		Fog { Mode Off }
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			Name "HIGHLIGHT"
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#include "UnityCG.cginc"

			struct appdata
			{
				UNITY_VERTEX_INPUT_INSTANCE_ID
				float4 vertex : POSITION;
				fixed4 color  : COLOR;
			};

			struct v2f
			{
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
				float4 vertex : SV_POSITION;
				fixed4 color  : COLOR;
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.vertex = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				return i.color;
			}
			ENDCG
		}
	}
}
