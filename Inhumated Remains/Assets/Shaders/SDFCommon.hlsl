// Common SDF utility functions
// Shared between C# and HLSL

#ifndef EXCAVATION_SDF_COMMON
#define EXCAVATION_SDF_COMMON

// Include shared geometry SDF functions (single source of truth)
#include "SDFGeometry.hlsl"

// SDF Combination Operations

float SDFUnion(float a, float b)
{
    return min(a, b);
}

float SDFSubtract(float a, float b)
{
    return max(a, -b);
}

float SDFIntersect(float a, float b)
{
    return max(a, b);
}

float SDFSmoothMin(float a, float b, float k)
{
    float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// Note: DepthBandSDF is now defined in SDFGeometry.hlsl

float SphereSDF(float3 worldPos, float3 center, float radius)
{
    return length(worldPos - center) - radius;
}

// Get voxel size for a given MIP level
float GetVoxelSize(float baseVoxelSize, int mipLevel)
{
    return baseVoxelSize * pow(2.0, mipLevel);
}

// Convert world position to texture UVW coordinates
float3 WorldToUVW(float3 worldPos, float3 volumeOrigin, float3 volumeSize)
{
    float3 local = worldPos - volumeOrigin;
    return local / volumeSize;
}

// Triplanar texture sampling
float4 TriplanarSample(Texture2D tex, SamplerState samp, float3 worldPos, float3 normal, float scale, float sharpness)
{
    // Sample from each axis
    float4 xProj = tex.Sample(samp, worldPos.yz * scale);
    float4 yProj = tex.Sample(samp, worldPos.xz * scale);
    float4 zProj = tex.Sample(samp, worldPos.xy * scale);
    
    // Blend weights based on normal
    float3 blend = pow(abs(normal), sharpness);
    blend /= (blend.x + blend.y + blend.z);
    
    return xProj * blend.x + yProj * blend.y + zProj * blend.z;
}

float4 TriplanarSampleLevel(Texture2D tex, SamplerState samp, float3 worldPos, float3 normal, float scale, float sharpness, float mipLevel)
{
    // Sample from each axis
    float4 xProj = tex.SampleLevel(samp, worldPos.yz * scale, mipLevel);
    float4 yProj = tex.SampleLevel(samp, worldPos.xz * scale, mipLevel);
    float4 zProj = tex.SampleLevel(samp, worldPos.xy * scale, mipLevel);
    
    // Blend weights based on normal
    float3 blend = pow(abs(normal), sharpness);
    blend /= (blend.x + blend.y + blend.z);
    
    return xProj * blend.x + yProj * blend.y + zProj * blend.z;
}

// Calculate normal from SDF gradient using central differences
float3 CalculateNormal(float3 p, float epsilon, Texture3D<float> volumeTex, SamplerState samp,
                       float3 volumeOrigin, float3 volumeSize)
{
    float3 uvw = WorldToUVW(p, volumeOrigin, volumeSize);
    
    float2 e = float2(epsilon, 0.0);
    
    float3 n = float3(
        volumeTex.SampleLevel(samp, uvw + e.xyy, 0).r - volumeTex.SampleLevel(samp, uvw - e.xyy, 0).r,
        volumeTex.SampleLevel(samp, uvw + e.yxy, 0).r - volumeTex.SampleLevel(samp, uvw - e.yxy, 0).r,
        volumeTex.SampleLevel(samp, uvw + e.yyx, 0).r - volumeTex.SampleLevel(samp, uvw - e.yyx, 0).r
    );
    
    return normalize(n);
}

#endif // EXCAVATION_SDF_COMMON
