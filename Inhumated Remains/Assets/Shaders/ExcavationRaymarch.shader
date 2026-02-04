Shader "Excavation/ExcavationRaymarch"
{
    Properties
    {
        // Volume Data
        _CarveVolume("Carve Volume", 3D) = "white" {}
        _VolumeOrigin("Volume Origin", Vector) = (0, 0, 0, 0)
        _VolumeSize("Volume Size", Vector) = (10, 5, 10, 0)
        _VoxelSize("Voxel Size", Float) = 0.05
        
        // Raymarching Parameters
        _MaxSteps("Max Steps", Int) = 128
        _MaxDistance("Max Distance", Float) = 50.0
        _SurfaceThreshold("Surface Threshold", Float) = 0.001
        
        // Rendering
        _BaseTerrainY("Base Terrain Y", Float) = 0.0
        
        // Stratigraphy (up to 8 layers)
        _LayerCount("Layer Count", Int) = 0
        
        // Self-shadowing
        [Toggle] _EnableSelfShadows("Enable Self Shadows", Float) = 0
        _ShadowSteps("Shadow Steps", Int) = 32
        _ShadowDistance("Shadow Distance", Float) = 5.0
        _ShadowSoftness("Shadow Softness", Float) = 4.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "HDRenderPipeline"
        }
        
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }
            
            ZWrite On
            ZTest LEqual
            Cull Front  // Render back faces for proper ray entry
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "SDFCommon.hlsl"
            
            // Volume textures
            Texture3D<float> _CarveVolume;
            SamplerState sampler_CarveVolume;
            
            SamplerState sampler_point_clamp
            {
                Filter = MIN_MAG_MIP_POINT;
                AddressU = Clamp;
                AddressV = Clamp;
                AddressW = Clamp;
            };
            
            SamplerState sampler_linear_clamp
            {
                Filter = MIN_MAG_MIP_LINEAR;
                AddressU = Clamp;
                AddressV = Clamp;
                AddressW = Clamp;
            };
            
            // Shader properties
            float3 _VolumeOrigin;
            float3 _VolumeSize;
            float _VoxelSize;
            int _MaxSteps;
            float _MaxDistance;
            float _SurfaceThreshold;
            float _BaseTerrainY;
            int _LayerCount;
            
            // Layer data arrays
            float4 _LayerColors[8];
            float4 _LayerParams[8];  // Geometry parameters per layer
            
            // Shadow parameters
            float _EnableSelfShadows;
            int _ShadowSteps;
            float _ShadowDistance;
            float _ShadowSoftness;
            
            // Light direction (main directional light)
            static float3 _MainLightDirection = float3(0.5, -1.0, 0.5);
            static float4 _MainLightColor = float4(1, 1, 1, 1);
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 rayOrigin : TEXCOORD1;
                float3 rayDir : TEXCOORD2;
            };
            
            // Evaluate base terrain SDF (flat ground for now)
            float EvaluateBaseTerrain(float3 worldPos)
            {
                // Simple flat ground
                float baseSDF = worldPos.y - _BaseTerrainY;
                
                // TODO: Apply layer geometry operations here
                // For now, just flat ground
                
                return baseSDF;
            }
            
            // Evaluate the complete scene SDF
            float EvaluateSceneSDF(float3 worldPos, int mipLevel)
            {
                // Get base terrain
                float baseSDF = EvaluateBaseTerrain(worldPos);
                
                // Sample carve volume
                float3 uvw = WorldToUVW(worldPos, _VolumeOrigin, _VolumeSize);
                
                // Check bounds
                if (any(uvw < 0.0) || any(uvw > 1.0))
                {
                    return baseSDF; // Outside volume, just return base terrain
                }
                
                // Sample at current MIP level
                float carveSDF = _CarveVolume.SampleLevel(sampler_point_clamp, uvw, mipLevel).r;
                
                // Boolean subtraction: max(base, -carve)
                return max(baseSDF, -carveSDF);
            }
            
            // Get material layer at world position (simplified)
            float4 GetMaterialColor(float3 worldPos)
            {
                // Evaluate layers from top to bottom
                for (int i = 0; i < _LayerCount; i++)
                {
                    // For DepthBand layers (type 0)
                    float topY = _LayerParams[i].x;
                    float bottomY = _LayerParams[i].y;
                    
                    float sdf = DepthBandSDF(worldPos, topY, bottomY);
                    
                    if (sdf < 0.0)
                    {
                        return _LayerColors[i];
                    }
                }
                
                // Default: brown dirt
                return float4(0.55, 0.4, 0.25, 1.0);
            }
            
            // Hierarchical raymarching
            bool Raymarch(float3 origin, float3 dir, out float3 hitPoint, out float totalDist)
            {
                float t = 0.0;
                int mip = 4; // Start at coarse MIP level
                int maxMip = 4;
                
                for (int i = 0; i < _MaxSteps; i++)
                {
                    float3 p = origin + dir * t;
                    
                    // Sample at current MIP
                    float d = EvaluateSceneSDF(p, mip);
                    
                    // Safety brake: if distance is smaller than voxel size, drop to lower MIP
                    float voxelSize = GetVoxelSize(_VoxelSize, mip);
                    if (d < voxelSize * 1.5 && mip > 0)
                    {
                        mip--;
                        continue; // Retry with higher resolution
                    }
                    
                    // Surface hit
                    if (d < _SurfaceThreshold)
                    {
                        hitPoint = p;
                        totalDist = t;
                        return true;
                    }
                    
                    // Too far
                    if (t > _MaxDistance)
                    {
                        break;
                    }
                    
                    // March forward (with small nudge for coarse MIPs)
                    float nudge = (mip > 0) ? (voxelSize * 0.25) : 0.0;
                    t += d + nudge;
                }
                
                hitPoint = origin + dir * t;
                totalDist = t;
                return false;
            }
            
            // Compute soft shadows
            float ComputeSoftShadow(float3 hitPoint, float3 normal, float3 lightDir)
            {
                if (_EnableSelfShadows < 0.5)
                    return 1.0;
                
                float3 rayOrigin = hitPoint + normal * 0.01;
                float t = 0.0;
                float shadow = 1.0;
                
                for (int i = 0; i < _ShadowSteps; i++)
                {
                    float3 p = rayOrigin + lightDir * t;
                    float d = EvaluateSceneSDF(p, 0);
                    
                    if (d < 0.001)
                        return 0.0;
                    
                    shadow = min(shadow, _ShadowSoftness * d / t);
                    t += d;
                    
                    if (t > _ShadowDistance)
                        break;
                }
                
                return saturate(shadow);
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Transform to clip space
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                
                // Ray setup
                output.rayOrigin = _WorldSpaceCameraPos;
                output.rayDir = normalize(output.positionWS - output.rayOrigin);
                
                return output;
            }
            
            struct FragOutput
            {
                float4 color : SV_Target;
                float depth : SV_Depth;
            };
            
            FragOutput frag(Varyings input)
            {
                FragOutput output;
                
                // Perform raymarching
                float3 hitPoint;
                float totalDist;
                bool hit = Raymarch(input.rayOrigin, input.rayDir, hitPoint, totalDist);
                
                if (!hit)
                {
                    discard; // No surface hit
                }
                
                // Calculate normal
                float3 normal = CalculateNormal(hitPoint, 0.001, _CarveVolume, sampler_linear_clamp,
                                                _VolumeOrigin, _VolumeSize);
                
                // Get material color
                float4 albedo = GetMaterialColor(hitPoint);
                
                // Simple lighting (Lambert)
                float3 lightDir = normalize(-_MainLightDirection);
                float ndotl = max(0.0, dot(normal, lightDir));
                
                // Compute shadows
                float shadow = ComputeSoftShadow(hitPoint, normal, lightDir);
                
                // Final color
                float3 ambient = albedo.rgb * 0.2;
                float3 diffuse = albedo.rgb * ndotl * shadow;
                float3 finalColor = ambient + diffuse;
                
                output.color = float4(finalColor, 1.0);
                
                // Write depth
                float4 clipPos = TransformWorldToHClip(hitPoint);
                output.depth = clipPos.z / clipPos.w;
                
                return output;
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/InternalErrorShader"
}
