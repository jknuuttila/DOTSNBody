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
				float4 vPos = float4(_vertexPositions[vid].xyz, 1);
				o.pos = mul(UNITY_MATRIX_VP, vPos);
				o.color = float4(1, 0, 0, 1);
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
