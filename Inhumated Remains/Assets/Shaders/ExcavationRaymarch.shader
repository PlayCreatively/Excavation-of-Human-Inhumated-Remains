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
        
        // Texture Settings
        _TextureScale("Texture Scale", Float) = 1.0
        _TextureSharpness("Texture Sharpness", Float) = 8.0

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
            Cull Front  // Render back faces for proper ray entry from inside volume
            
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
            
            // Texture Sampler
            SamplerState sampler_linear_repeat
            {
                Filter = MIN_MAG_MIP_LINEAR;
                AddressU = Wrap;
                AddressV = Wrap;
                AddressW = Wrap;
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
            
            // Texture Settings
            float _TextureScale;
            float _TextureSharpness;

            // Layer data
            float4 _LayerColors[8];
            float4 _LayerParams[8];  // Geometry parameters per layer
            
            // Textures (up to 8 layers)
            Texture2D _LayerAlbedo0; Texture2D _LayerNormal0;
            Texture2D _LayerAlbedo1; Texture2D _LayerNormal1;
            Texture2D _LayerAlbedo2; Texture2D _LayerNormal2;
            Texture2D _LayerAlbedo3; Texture2D _LayerNormal3;
            Texture2D _LayerAlbedo4; Texture2D _LayerNormal4;
            Texture2D _LayerAlbedo5; Texture2D _LayerNormal5;
            Texture2D _LayerAlbedo6; Texture2D _LayerNormal6;
            Texture2D _LayerAlbedo7; Texture2D _LayerNormal7;
            
            // Shadow parameters
            float _EnableSelfShadows;
            int _ShadowSteps;
            float _ShadowDistance;
            float _ShadowSoftness;
            
            // Light direction (passed from script)
            float3 _MainLightDirection;
            float4 _MainLightColor;
            
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
                return worldPos.y - _BaseTerrainY;
            }
            
            // Evaluate the complete scene SDF
            float EvaluateSceneSDF(float3 worldPos, int mipLevel)
            {
                float baseSDF = EvaluateBaseTerrain(worldPos);
                float3 uvw = WorldToUVW(worldPos, _VolumeOrigin, _VolumeSize);
                
                if (any(uvw < 0.0) || any(uvw > 1.0))
                {
                    return baseSDF;
                }
                
                float carveSDF = _CarveVolume.SampleLevel(sampler_point_clamp, uvw, mipLevel).r;
                return max(baseSDF, -carveSDF);
            }
            
            // Helper to sample correct texture based on index
            void SampleLayerTexture(int index, float3 pos, float3 normal, out float3 albedo, out float3 normalTan)
            {
                // Explicit branching
                float mip = 0; 
                float scale = _TextureScale;
                float sharpness = _TextureSharpness;
                
                float3 col = float3(1,1,1);
                float3 nrm = float3(0,0,1); // Default normal
                
                // Override scale if provided in params?
                if (index >= 0 && index < 8) 
                {
                     // Use global scale for now, ignore per-layer scale unless needed
                }

                // Manual unroll
                if (index == 0) col = TriplanarSampleLevel(_LayerAlbedo0, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 1) col = TriplanarSampleLevel(_LayerAlbedo1, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 2) col = TriplanarSampleLevel(_LayerAlbedo2, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 3) col = TriplanarSampleLevel(_LayerAlbedo3, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 4) col = TriplanarSampleLevel(_LayerAlbedo4, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 5) col = TriplanarSampleLevel(_LayerAlbedo5, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 6) col = TriplanarSampleLevel(_LayerAlbedo6, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                else if (index == 7) col = TriplanarSampleLevel(_LayerAlbedo7, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb;
                
                albedo = col;
                normalTan = float3(0,0,1); 
            }
            
            // Get material layer at world position
            void GetMaterialData(float3 worldPos, float3 normal, out float3 color, out float3 finalNormal)
            {
                int layerIndex = -1;
                
                // Use default params if loop fails
                float defaultScale = _TextureScale;

                for (int i = 0; i < _LayerCount; i++)
                {
                    float topY = _LayerParams[i].x;
                    float bottomY = _LayerParams[i].y;
                    
                    float sdf = DepthBandSDF(worldPos, topY, bottomY);
                    
                    if (sdf < 0.0)
                    {
                        layerIndex = i;
                        break;
                    }
                }
                
                if (layerIndex >= 0)
                {
                    float3 texColor;
                    float3 texNormal;
                    SampleLayerTexture(layerIndex, worldPos, normal, texColor, texNormal);
                    
                    color = _LayerColors[layerIndex].rgb * texColor;
                    finalNormal = normal; // TODO: perturb normal
                }
                else
                {
                    // Default fallback
                    color = float3(1, 0.0, 1);
                    finalNormal = normal;
                }
            }
            
            // Hierarchical raymarching
            bool Raymarch(float3 origin, float3 dir, out float3 hitPoint, out float totalDist)
            {
                float t = 0.0;
                int mip = 4;
                
                for (int i = 0; i < _MaxSteps; i++)
                {
                    float3 p = origin + dir * t;
                    float d = EvaluateSceneSDF(p, mip);
                    
                    float voxelSize = GetVoxelSize(_VoxelSize, mip);
                    if (d < voxelSize * 1.5 && mip > 0)
                    {
                        mip--;
                        continue;
                    }
                    
                    if (d < _SurfaceThreshold)
                    {
                        hitPoint = p;
                        totalDist = t;
                        return true;
                    }
                    
                    if (t > _MaxDistance)
                        break;
                    
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
                if (_EnableSelfShadows < 0.5) return 1.0;
                
                float3 rayOrigin = hitPoint + normal * 0.01;
                float t = 0.01;
                float shadow = 1.0;
                
                for (int i = 0; i < _ShadowSteps; i++)
                {
                    float3 p = rayOrigin + lightDir * t;
                    float d = EvaluateSceneSDF(p, 0);
                    
                    if (d < 0.001) return 0.0;
                    
                    shadow = min(shadow, _ShadowSoftness * d / t);
                    t += d;
                    
                    if (t > _ShadowDistance) break;
                }
                
                return saturate(shadow);
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
                
                float3 hitPoint;
                float totalDist;
                bool hit = Raymarch(input.rayOrigin, input.rayDir, hitPoint, totalDist);
                
                if (!hit) discard;
                
                // Calculate normal
                float3 normal = CalculateNormal(hitPoint, 0.001, _CarveVolume, sampler_linear_clamp,
                                                _VolumeOrigin, _VolumeSize);
                
                // Get material data (triPlanar)
                float3 albedo;
                float3 perturbedNormal;
                GetMaterialData(hitPoint, normal, albedo, perturbedNormal);
                
                float3 sceneNormal = normal; // Use perturbed here if implemented
                
                // Lighting
                float3 lightDir = normalize(-_MainLightDirection); 
                float ndotl = max(0.0, dot(sceneNormal, lightDir));
                
                float shadow = ComputeSoftShadow(hitPoint, sceneNormal, lightDir);
                
                float3 ambient = albedo * 0.15; 
                float3 diffuse = albedo * _MainLightColor.rgb * ndotl * shadow;
                
                output.color = float4(ambient + diffuse, 1.0);
                
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
