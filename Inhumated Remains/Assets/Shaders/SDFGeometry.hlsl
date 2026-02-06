// SDFGeometry.hlsl - Shared SDF geometry functions
// This file is the single source of truth for all layer geometry SDFs.
// Included by: Raymarch shaders (rendering), SDFQuery.compute (CPU queries)

#ifndef EXCAVATION_SDF_GEOMETRY
#define EXCAVATION_SDF_GEOMETRY

// ============================================================================
// GEOMETRY TYPE IDs
// Must match LayerGeometryType enum in C#
// ============================================================================

#define GEOMETRY_TYPE_DEPTH_BAND        0
#define GEOMETRY_TYPE_NOISY_DEPTH_BAND  1
#define GEOMETRY_TYPE_CUT               2
#define GEOMETRY_TYPE_ELLIPSOID         3

// ============================================================================
// NOISE FUNCTIONS (for NoisyDepthBandGeometry)
// Using a simple gradient noise that approximates Perlin noise behavior.
// Note: This may produce slightly different results than Unity's Mathf.PerlinNoise.
// ============================================================================

float2 GradientNoiseDir(float2 p)
{
    p = fmod(p, 289.0);
    float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
    x = fmod((34.0 * x + 1.0) * x, 289.0);
    x = frac(x / 41.0) * 2.0 - 1.0;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

float GradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    float d00 = dot(GradientNoiseDir(ip), fp);
    float d01 = dot(GradientNoiseDir(ip + float2(0, 1)), fp - float2(0, 1));
    float d10 = dot(GradientNoiseDir(ip + float2(1, 0)), fp - float2(1, 0));
    float d11 = dot(GradientNoiseDir(ip + float2(1, 1)), fp - float2(1, 1));
    fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0); // Smoothstep
    return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x) + 0.5; // Remap to 0-1
}

// ============================================================================
// SDF PRIMITIVES
// ============================================================================

/// <summary>
/// Horizontal layer defined by top and bottom Y coordinates.
/// Params: x = topY, y = bottomY
/// </summary>
float DepthBandSDF(float3 worldPos, float topY, float bottomY)
{
    float dTop = topY - worldPos.y;      // Positive below top
    float dBot = worldPos.y - bottomY;   // Positive above bottom
    return max(-dTop, -dBot);            // Inside when both positive
}

/// <summary>
/// Horizontal layer with noise-based undulation.
/// Params: x = baseTopY, y = baseBottomY, z = amplitude, w = frequency
/// Params2: x = noiseOffsetX, y = noiseOffsetZ
/// </summary>
float NoisyDepthBandSDF(float3 worldPos, float baseTopY, float baseBottomY, 
                        float amplitude, float frequency, float2 noiseOffset)
{
    // Sample noise at this XZ position
    float2 noiseCoord = float2(worldPos.x, worldPos.z) * frequency + noiseOffset;
    float noiseValue = GradientNoise(noiseCoord);
    
    // Apply amplitude (remap from 0-1 to -amplitude to +amplitude)
    float offset = (noiseValue - 0.5) * 2.0 * amplitude;
    
    // Offset both surfaces
    float topY = baseTopY + offset;
    float bottomY = baseBottomY + offset;
    
    return DepthBandSDF(worldPos, topY, bottomY);
}

/// <summary>
/// Vertical cylindrical cut (pit, posthole, ditch).
/// Params: xyz = centre, w = radius
/// Params2: x = depth
/// </summary>
float CutSDF(float3 worldPos, float3 centre, float radius, float depth)
{
    // Horizontal distance from center (XZ plane)
    float2 horizontalOffset = float2(worldPos.x - centre.x, worldPos.z - centre.z);
    float horizontalDist = length(horizontalOffset) - radius;
    
    // Vertical containment
    float topDist = centre.y - worldPos.y;           // Positive below top
    float bottomDist = worldPos.y - (centre.y - depth); // Positive above bottom
    float verticalDist = max(-topDist, -bottomDist);
    
    // Inside when both horizontal AND vertical are negative
    return max(horizontalDist, verticalDist);
}

/// <summary>
/// Ellipsoidal geometry for burial mounds, tumuli, or rounded deposits.
/// Params: xyz = centre
/// Params2: xyz = radii
/// Note: This uses an approximation with average radius for SDF scaling.
///       Exact ellipsoid SDF is more complex and computationally expensive.
/// </summary>
float EllipsoidSDF(float3 worldPos, float3 centre, float3 radii)
{
    // Offset from center
    float3 offset = worldPos - centre;
    
    // Normalize by radii (creates unit sphere in normalized space)
    float3 safeRadii = max(radii, float3(0.001, 0.001, 0.001));
    float3 normalized = offset / safeRadii;
    
    // Distance from center in normalized space
    float normalizedDist = length(normalized);
    
    // Scale back to world space using average radius
    // This is an approximation - exact ellipsoid SDF requires iterative solving
    float avgRadius = (radii.x + radii.y + radii.z) / 3.0;
    
    return (normalizedDist - 1.0) * avgRadius;
}

// ============================================================================
// LAYER EVALUATION
// Evaluates which layer a point is inside based on geometry parameters
// ============================================================================

/// <summary>
/// Evaluate layer SDF based on geometry type.
/// 
/// Parameter packing:
/// - layerParams.x, .y, .z, .w = primary parameters (type-dependent)
/// - layerParams2.x, .y, .z, .w = secondary parameters (type-dependent)
/// - geometryType = GEOMETRY_TYPE_* constant
/// 
/// DepthBand:        params(topY, bottomY, -, -)
/// NoisyDepthBand:   params(baseTopY, baseBottomY, amplitude, frequency), params2(offsetX, offsetZ, -, -)
/// Cut:              params(centreX, centreY, centreZ, radius), params2(depth, -, -, -)
/// Ellipsoid:        params(centreX, centreY, centreZ, -), params2(radiusX, radiusY, radiusZ, -)
/// </summary>
float EvaluateLayerSDF(float3 worldPos, int geometryType, float4 layerParams, float4 layerParams2)
{
    switch (geometryType)
    {
        case GEOMETRY_TYPE_DEPTH_BAND:
            return DepthBandSDF(worldPos, layerParams.x, layerParams.y);
            
        case GEOMETRY_TYPE_NOISY_DEPTH_BAND:
            return NoisyDepthBandSDF(worldPos, 
                layerParams.x,  // baseTopY
                layerParams.y,  // baseBottomY
                layerParams.z,  // amplitude
                layerParams.w,  // frequency
                layerParams2.xy // noiseOffset
            );
            
        case GEOMETRY_TYPE_CUT:
            return CutSDF(worldPos,
                layerParams.xyz, // centre
                layerParams.w,   // radius
                layerParams2.x   // depth
            );
            
        case GEOMETRY_TYPE_ELLIPSOID:
            return EllipsoidSDF(worldPos,
                layerParams.xyz,  // centre
                layerParams2.xyz  // radii
            );
            
        default:
            return 9999.0; // Outside any geometry
    }
}

/// <summary>
/// Find the first (youngest) layer that contains this point.
/// Returns layer index (0-7) or -1 if no layer contains the point.
/// </summary>
int FindContainingLayer(float3 worldPos, int layerCount, 
                        int geometryTypes[8], float4 layerParams[8], float4 layerParams2[8])
{
    for (int i = 0; i < layerCount && i < 8; i++)
    {
        float sdf = EvaluateLayerSDF(worldPos, geometryTypes[i], layerParams[i], layerParams2[i]);
        if (sdf < 0.0)
        {
            return i;
        }
    }
    return -1;
}

// ============================================================================
// TODO: Layer Blending / Transition Zones
// 
// For soft boundaries (bioturbation, gradual color change), implement:
// 
// float t = saturate(InverseLerp(transitionStart, transitionEnd, -layerSDF));
// float3 blended = lerp(layerAboveColor, layerColor, t);
// 
// This would require additional parameters per layer:
// - transitionThickness (how far the blend extends)
// - Whether to blend with layer above or use fixed transition zone
// ============================================================================

#endif // EXCAVATION_SDF_GEOMETRY
