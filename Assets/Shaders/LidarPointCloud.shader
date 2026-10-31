/**
 * Copyright (c) 2019 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

Shader "Simulator/LidarPointCloud"
{
    Properties
    {
        _Color("Color", Color) = (1, 0, 0, 1)
        _Size("Size (px)", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" }

        Pass
        {
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma target 4.5
            #pragma fragment Frag

            #include "UnityCG.cginc"

            CBUFFER_START(UnityPerMaterial)
                float4x4 _LocalToWorld;
                float4 _Color;
                float _Size;
            CBUFFER_END

            StructuredBuffer<float4> _PointCloud;

            struct g2f
            {
                float4 position : SV_POSITION;
            };

            g2f Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                float3 pos = _PointCloud[instanceID].xyz;
                g2f output;
                if (dot(pos, pos) == 0)
                {
                    output.position = float4(2, 2, 2, 1);
                    return output;
                }
                float4 world = mul(_LocalToWorld, float4(pos, 1));
                float4 clip = UnityWorldToClipPos(world.xyz);
                const float2 offsets[6] = {
                    float2(-1,-1), float2(1,-1), float2(-1,1),
                    float2(-1,1), float2(1,-1), float2(1,1)
                };
                output.position = clip;
                output.position.xy += _Size * clip.w / _ScreenParams.xy * offsets[vertexID];
                output.position.z = 1.1 * clip.z;
                return output;
            }

            float4 Frag(g2f input) : SV_Target
            {
                return float4(_Color.rgb, 1.0f);
            }

            ENDHLSL
        }
    }
}
