/**
 * Copyright (c) 2019 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

Shader "Simulator/WireframeBox"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" }

        Pass
        {
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma target 4.5
            #pragma fragment Frag

            #include "UnityCG.cginc"

            struct Box
            {
                float4x4 transform;
                float3 position;
                float3 size;
                float3 color;
            };

            StructuredBuffer<Box> _Boxes;

            float _LineWidth;

            struct g2f
            {
                float4 position : SV_POSITION;
                float3 color : COLOR;
            };

            #include "ProceduralLine.hlsl"

            g2f Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Box box = _Boxes[instanceID];
                const float3 corners[8] = {
                    float3(-1, 1, 1), float3(1, 1, 1), float3(1, -1, 1), float3(-1, -1, 1),
                    float3(-1, 1, -1), float3(1, 1, -1), float3(1, -1, -1), float3(-1, -1, -1)
                };
                const uint2 edges[12] = {
                    uint2(0,1), uint2(1,2), uint2(2,3), uint2(3,0),
                    uint2(4,5), uint2(5,6), uint2(6,7), uint2(7,4),
                    uint2(4,0), uint2(5,1), uint2(6,2), uint2(7,3)
                };
                uint2 edge = edges[vertexID / 6];
                float3 start = box.position + box.size * 0.5 * corners[edge.x];
                float3 end = box.position + box.size * 0.5 * corners[edge.y];
                float4 v0 = UnityWorldToClipPos(mul(box.transform, float4(start, 1)));
                float4 v1 = UnityWorldToClipPos(mul(box.transform, float4(end, 1)));
                g2f output;
                output.position = LineVertex(v0, v1, vertexID % 6, _LineWidth);
                output.color = box.color;
                return output;
            }

            float4 Frag(g2f input) : SV_Target
            {
                return float4(input.color, 1.0f);
            }

            ENDCG
        }
    }
}
