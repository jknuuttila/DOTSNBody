Shader "Unlit/ParticleShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                float4 color : COLOR;
				float4 pos : SV_Position;
            };

			StructuredBuffer<float4> _vertexPositions;

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
				float4 vertex = _vertexPositions[vid];
				float4 worldPos = float4(vertex.x, 0, vertex.y, 1);

				o.pos = mul(UNITY_MATRIX_VP, worldPos);

				float v = vertex.z;
				float m = vertex.w + 1;
				float E = m * v * v;
				E *= 1;

				o.color = float4(E, E, E, 1);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
