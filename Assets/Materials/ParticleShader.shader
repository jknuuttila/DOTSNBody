Shader "Unlit/ParticleShader"
{
	Properties
	{
		_FalseColor("False color", 2D) = "defaulttexture" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
			Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                float4 color : COLOR;
				float4 pos : SV_Position;
            };

			UNITY_DECLARE_TEX2D(_FalseColor);
			StructuredBuffer<float4> _vertexPositions;

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
				float4 vertex = _vertexPositions[vid];
				float4 worldPos = float4(vertex.x, 0, vertex.y, 1);

				o.pos = mul(UNITY_MATRIX_VP, worldPos);

				float v = vertex.z;
				float m = 3e3 * vertex.w + 1;
				float E = m * v * v;

				E *= 0.5;

				float intensity = max(0.1, log10(E));

				o.color = float4(E, intensity, 0, 0);
                return o;
            }

			float4 frag (v2f i) : SV_Target
			{
				float E = i.color.x;
				float intensity = i.color.y;
				float3 falseColor = UNITY_SAMPLE_TEX2D(_FalseColor, float2(0.5, intensity)).rgb;
				return float4(E * falseColor, 1);
            }
            ENDCG
        }
    }
}
