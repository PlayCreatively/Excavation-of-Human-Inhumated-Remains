Shader "Excavation/ExcavationRaymarchUnlit"
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
        [HideInInspector] _LayerCount("Layer Count", Int) = 0
        
        // Textures (Manually unrolled for up to 8 layers)
        [NoScaleOffset] _LayerAlbedo0("Layer 0 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal0("Layer 0 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo1("Layer 1 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal1("Layer 1 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo2("Layer 2 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal2("Layer 2 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo3("Layer 3 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal3("Layer 3 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo4("Layer 4 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal4("Layer 4 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo5("Layer 5 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal5("Layer 5 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo6("Layer 6 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal6("Layer 6 Normal", 2D) = "bump" {}
        [NoScaleOffset] _LayerAlbedo7("Layer 7 Albedo", 2D) = "white" {}
        [NoScaleOffset] _LayerNormal7("Layer 7 Normal", 2D) = "bump" {}

        _TextureTiling("Texture Tiling", Float) = 1.0
        _TextureSharpness("Texture Sharpness", Float) = 8.0
        _MaxMipLevel("Max MIP Level", Int) = 4
        _AmbientIntensity("Ambient Intensity", Range(0, 1)) = 0.1
        
        // Self-shadowing
        [Toggle] _EnableSelfShadows("Enable Self Shadows", Float) = 0
        _ShadowSteps("Shadow Steps", Int) = 32
        _ShadowDistance("Shadow Distance", Float) = 5.0
        _ShadowSoftness("Shadow Softness", Float) = 4.0

        // Stencil settings to cooperate with HDRP
        [HideInInspector] _StencilRef("_StencilRef", Int) = 0
        [HideInInspector] _StencilComp("_StencilComp", Int) = 8
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        
        Pass
        {
            Name "ExcavationRaymarch"
            Tags { "LightMode" = "ForwardOnly" } 

            ZWrite On
            ZTest LEqual
            Cull Front
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            
            // HDRP Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            
            // Include SDF common functions
            #include "SDFCommon.hlsl"
            
            // Volume textures
            Texture3D<float> _CarveVolume;
            SamplerState sampler_CarveVolume;
            
            SamplerState sampler_point_clamp;
            SamplerState sampler_linear_clamp;
            SamplerState sampler_linear_repeat; // For triplanar texturing

            // Layer Textures
            Texture2D _LayerAlbedo0;
            Texture2D _LayerNormal0;
            Texture2D _LayerAlbedo1;
            Texture2D _LayerNormal1;
            Texture2D _LayerAlbedo2;
            Texture2D _LayerNormal2;
            Texture2D _LayerAlbedo3;
            Texture2D _LayerNormal3;
            Texture2D _LayerAlbedo4;
            Texture2D _LayerNormal4;
            Texture2D _LayerAlbedo5;
            Texture2D _LayerNormal5;
            Texture2D _LayerAlbedo6;
            Texture2D _LayerNormal6;
            Texture2D _LayerAlbedo7;
            Texture2D _LayerNormal7;
            
            // Shader properties
            float3 _VolumeOrigin;
            float3 _VolumeSize;
            float _VoxelSize;
            int _MaxSteps;
            float _MaxDistance;
            float _SurfaceThreshold;
            float _BaseTerrainY;
            int _LayerCount;

            float _TextureTiling;
            float _TextureSharpness;
            int _MaxMipLevel;
            float _AmbientIntensity;
            
            // Layer data arrays
            float4 _LayerColors[8];
            float4 _LayerParams[8];
            float4 _LayerParams2[8]; // Additional geometry params (noise, etc.)
            int _GeometryTypes[8];   // Geometry type per layer
            
            // Shadow parameters
            float _EnableSelfShadows;
            int _ShadowSteps;
            float _ShadowDistance;
            float _ShadowSoftness;
            
            // Light direction (main directional light)
            float3 _MainLightDirection;
            float4 _MainLightColor;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0; // Absolute world pos
                float3 rayOrigin : TEXCOORD1;
                float3 rayDir : TEXCOORD2;
            };
            
            // --- Helper Functions ---

            // Evaluate base terrain SDF (flat ground for now)
            float EvaluateBaseTerrain(float3 worldPos)
            {
                return worldPos.y - _BaseTerrainY;
            }
            
            // Evaluate the complete scene SDF (Base - Carve)
            float EvaluateSceneSDF(float3 worldPos, int mipLevel)
            {
                float baseSDF = EvaluateBaseTerrain(worldPos);
                
                // Transform to volume local UVW
                float3 uvw = WorldToUVW(worldPos, _VolumeOrigin, _VolumeSize);
                
                // If outside local volume bounds, assume we just have base terrain 
                if (any(uvw < 0.0) || any(uvw > 1.0))
                {
                    return baseSDF;
                }
                
                // Use linear sampling at MIP 0 for smooth surfaces, point sampling at higher MIPs for speed
                float carveSDF;
                if (mipLevel == 0)
                {
                    carveSDF = _CarveVolume.SampleLevel(sampler_linear_clamp, uvw, 0).r;
                }
                else
                {
                    carveSDF = _CarveVolume.SampleLevel(sampler_point_clamp, uvw, mipLevel).r;
                }
                
                // Subtract carve from base
                return max(baseSDF, -carveSDF);
            }
            
            // Calculate normal gradient from the FULL SCENE SDF
            float3 CalculateSceneNormal(float3 p)
            {
                float e = 0.005; // Epsilon
                
                // Compute gradient using central differences
                float dx = EvaluateSceneSDF(p + float3(e,0,0), 0) - EvaluateSceneSDF(p - float3(e,0,0), 0);
                float dy = EvaluateSceneSDF(p + float3(0,e,0), 0) - EvaluateSceneSDF(p - float3(0,e,0), 0);
                float dz = EvaluateSceneSDF(p + float3(0,0,e), 0) - EvaluateSceneSDF(p - float3(0,0,e), 0);
                
                return normalize(float3(dx, dy, dz));
            }

            // Texture Sampling Helper
            void SampleLayerTextures(int index, float3 pos, float3 normal, out float3 albedo)
            {
                // Default
                albedo = float3(1,1,1);
                float4 texColor = float4(1,1,1,1);

                // Manual switch because we can't index Texture objects dynamically easily without array resource
                if(index == 0) texColor = TriplanarSample(_LayerAlbedo0, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 1) texColor = TriplanarSample(_LayerAlbedo1, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 2) texColor = TriplanarSample(_LayerAlbedo2, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 3) texColor = TriplanarSample(_LayerAlbedo3, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 4) texColor = TriplanarSample(_LayerAlbedo4, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 5) texColor = TriplanarSample(_LayerAlbedo5, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 6) texColor = TriplanarSample(_LayerAlbedo6, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                else if(index == 7) texColor = TriplanarSample(_LayerAlbedo7, sampler_linear_repeat, pos, normal, _TextureTiling, _TextureSharpness);
                
                albedo = texColor.rgb;
            }
            
            // Get material layer properties at world position
            void GetSurfaceMaterial(float3 worldPos, float3 normal, out float3 albedo, out float3 emission)
            {
                // Default
                albedo = float3(0.5, 0.5, 0.5);
                emission = float3(0,0,0);
                
                // Find which layer we are in
                int activeLayerIndex = -1;
                
                for (int i = 0; i < _LayerCount; i++)
                {
                    // Use shared EvaluateLayerSDF with geometry type dispatch
                    int geomType = _GeometryTypes[i];
                    float4 params1 = _LayerParams[i];
                    float4 params2 = _LayerParams2[i];
                    
                    float sdf = EvaluateLayerSDF(worldPos, geomType, params1, params2);
                    
                    if (sdf < 0.0)
                    {
                        activeLayerIndex = i;
                        break;
                    }
                }
                
                if (activeLayerIndex != -1)
                {
                    float3 texAlbedo;
                    SampleLayerTextures(activeLayerIndex, worldPos, normal, texAlbedo);
                    
                    // Combine with layer base color
                    albedo = _LayerColors[activeLayerIndex].rgb * texAlbedo;
                }
                else
                {
                    // Fallback (bedrock?)
                    albedo = float3(0.2, 0.2, 0.2);
                }
            }

            // Hierarchical raymarching
            bool Raymarch(float3 origin, float3 dir, out float3 hitPoint, out float totalDist)
            {
                float t = 0.0;
                int mip = _MaxMipLevel;
                
                for (int i = 0; i < _MaxSteps; i++)
                {
                    float3 p = origin + dir * t;
                    float d = EvaluateSceneSDF(p, mip);
                    
                    float voxelSize = GetVoxelSize(_VoxelSize, mip);
                    
                    // Adaptive MIP descent
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
                    {
                        break;
                    }
                    
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
                
                float3 rayOrigin = hitPoint + normal * 0.1; // Increased bias
                float t = 0.0;
                float shadow = 1.0;
                
                for (int i = 0; i < _ShadowSteps; i++)
                {
                    float3 p = rayOrigin + lightDir * t;
                    float d = EvaluateSceneSDF(p, 0); 
                    
                    if (d < 0.001)
                        return 0.0;
                    
                    shadow = min(shadow, _ShadowSoftness * d / max(t, 0.01));
                    t += d;
                    
                    if (t > _ShadowDistance)
                        break;
                }
                
                return saturate(shadow);
            }
            
            v2f vert(appdata v)
            {
                v2f o;
                
                // HDRP Transforms
                float3 worldPos = TransformObjectToWorld(v.vertex.xyz);
                o.worldPos = worldPos;
                
                // Clip space
                o.pos = TransformWorldToHClip(worldPos);
                
                // Ray Origin is camera position
                o.rayOrigin = _WorldSpaceCameraPos; 
                
                o.rayDir = normalize(worldPos - o.rayOrigin);
                
                return o;
            }
            
            struct fragOutput
            {
                float4 color : SV_Target;
                float depth : SV_Depth;
            };
            
            fragOutput frag(v2f i)
            {
                fragOutput o;
                
                float3 hitPoint;
                float totalDist;
                
                bool hit = Raymarch(i.rayOrigin, i.rayDir, hitPoint, totalDist);
                
                if (!hit)
                {
                    discard;
                }
                
                float3 normal = CalculateSceneNormal(hitPoint);
                
                float3 albedo, emission;
                GetSurfaceMaterial(hitPoint, normal, albedo, emission);
                
                // Lighting
                float3 L = normalize(-_MainLightDirection);
                float ndotl = max(0.0, dot(normal, L));
                float shadow = ComputeSoftShadow(hitPoint, normal, L);
                
                float3 ambient = albedo * _AmbientIntensity;
                float3 diffuse = albedo * _MainLightColor.rgb * ndotl * shadow;
                
                o.color = float4(ambient + diffuse + emission, 1.0);
                
                float4 clipPos = TransformWorldToHClip(hitPoint);
                o.depth = clipPos.z / clipPos.w;
                
                return o;
            }
            
            ENDHLSL
        }
    }
    
    FallBack Off
}
