#ifndef SIMULATOR_CUBEMAP_TEXEL_CENTER_INCLUDED
#define SIMULATOR_CUBEMAP_TEXEL_CENTER_INCLUDED

// Moves a cubemap sampling direction to the center of the texel it falls in. A direction on a texel boundary, like
// a LiDAR beam at exactly 0 degrees elevation on the center row of a face, otherwise reads whichever of the two
// texels the GPU's rounding picks, which differs between Metal and Vulkan. Directions inside a texel keep their
// texel; a direction within a thousandth of a texel of a boundary takes the upper texel, as Vulkan (the Linux
// player) reads it; Metal reads the lower one.
float3 CubemapTexelCenter(float3 direction, float faceSize)
{
    // Face selection and face coordinates as every graphics API defines cubemap sampling
    float3 magnitude = abs(direction);
    int face;
    float major;
    float2 st;
    if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
    {
        major = magnitude.x;
        face = direction.x > 0 ? 0 : 1;
        st = direction.x > 0 ? float2(-direction.z, -direction.y) : float2(direction.z, -direction.y);
    }
    else if (magnitude.y >= magnitude.z)
    {
        major = magnitude.y;
        face = direction.y > 0 ? 2 : 3;
        st = direction.y > 0 ? float2(direction.x, direction.z) : float2(direction.x, -direction.z);
    }
    else
    {
        major = magnitude.z;
        face = direction.z > 0 ? 4 : 5;
        st = direction.z > 0 ? float2(direction.x, -direction.y) : float2(-direction.x, -direction.y);
    }
    float2 uv = 0.5 * (st / major + 1.0);
    const float epsilon = 0.001;
    float2 texel = clamp(floor(uv * faceSize + epsilon), 0.0, faceSize - 1.0);
    float2 center = (texel + 0.5) / faceSize * 2.0 - 1.0;
    switch (face)
    {
        case 0: return float3(1.0, -center.y, -center.x);
        case 1: return float3(-1.0, -center.y, center.x);
        case 2: return float3(center.x, 1.0, center.y);
        case 3: return float3(center.x, -1.0, -center.y);
        case 4: return float3(center.x, -center.y, 1.0);
        default: return float3(-center.x, -center.y, -1.0);
    }
}

#endif
