#pragma once

half3 ApplyVignette(half3 input, float2 uv, float2 center, float intensity, float roundness, float smoothness, half3 color)
{
    // center = UnityStereoTransformScreenSpaceTex(center);
    float2 dist = abs(uv - center) * intensity;

    dist.x *= roundness;
    float vfactor = pow(saturate(1.0 - dot(dist, dist)), smoothness);
    return input * lerp(color, (1.0).xxx, vfactor);
}



float GetFogAmount(float distance)
{
    // factor = (end-z)/(end-start) = z * (-1/(end-start)) + (end/(end-start))
    return saturate(distance/10);
}
