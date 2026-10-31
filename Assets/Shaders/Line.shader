/**
 * Copyright (c) 2020 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

Shader "Simulator/Line"
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

            struct Line
            {
                float4x4 transform;
                float3 start;
                float3 end;
                float3 color;
            };

            StructuredBuffer<Line> _Lines;

            float _LineWidth;

            struct g2f
            {
                float4 position : SV_POSITION;
                float3 color : COLOR;
            };

            #include "ProceduralLine.hlsl"

            g2f Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Line segment = _Lines[instanceID];
                float4 start = UnityWorldToClipPos(mul(segment.transform, float4(segment.start, 1)));
                float4 end = UnityWorldToClipPos(mul(segment.transform, float4(segment.end, 1)));
                g2f output;
                output.position = LineVertex(start, end, vertexID, _LineWidth);
                output.color = segment.color;
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
