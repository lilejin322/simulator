#ifndef SIMULATOR_PROCEDURAL_LINE_INCLUDED
#define SIMULATOR_PROCEDURAL_LINE_INCLUDED

// Expand one line into two triangles in the vertex stage (also supported by Metal).
float4 LineVertex(float4 v0, float4 v1, uint vertexID, float width)
{
    if (v0.w <= 0.00001 || v1.w <= 0.00001)
        return float4(2, 2, 2, 1);
    float2 delta = v1.xy / v1.w - v0.xy / v0.w;
    float2 direction = delta * rsqrt(max(dot(delta, delta), 0.00000001));
    float2 size = width / _ScreenParams.xy;
    float2 normal = float2(-direction.y, direction.x) * size;
    direction *= size * 0.5;
    const uint corners[6] = { 0, 1, 2, 2, 1, 3 };
    uint corner = corners[vertexID];
    float4 position = corner < 2 ? v0 : v1;
    float side = (corner & 1) == 0 ? 1 : -1;
    float end = corner < 2 ? -1 : 1;
    position.xy += (end * direction + side * normal) * position.w;
    return position;
}
#endif
