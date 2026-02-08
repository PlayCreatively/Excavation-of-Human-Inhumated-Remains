Shader "Excavation/ExcavationRaymarch"
{
    Properties
    {
        // Volume Data
        _CarveVolume("Carve Volume", 3D) = "white" {}
        _VolumeMin("Volume Min", Vector) = (0, 0, 0, 0)
        _VolumeSize("Volume Size", Vector) = (10, 5, 10, 0)
        _VoxelSize("Voxel Size", Float) = 0.05
        _MaxMipLevel("Max MIP Level", Int) = 4
        
        // Raymarching Parameters
        _MaxSteps("Max Steps", Int) = 128
        _MaxDistance("Max Distance", Float) = 50.0
        _SurfaceThreshold("Surface Threshold", Float) = 0.001
        
        // Stratigraphy
        _LayerCount("Layer Count", Int) = 0
        _FillCount("Fill Count", Int) = 0
        
        // Texture Settings
        _TextureTiling("Texture Tiling", Float) = 1.0
        _TextureSharpness("Texture Sharpness", Float) = 8.0

        // Lighting
        _AmbientIntensity("Ambient Intensity", Range(0, 1)) = 0.15
        _DiffuseIntensity("Diffuse Intensity", Range(0, 2)) = 1.0

        // Normals
        _NormalEpsilon("Normal Epsilon", Range(0.001, 1)) = 0.05

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
            Cull Front
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "SDFCommon.hlsl"
            
            // Volume texture
            Texture3D<float> _CarveVolume;
            
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
            
            SamplerState sampler_linear_repeat
            {
                Filter = MIN_MAG_MIP_LINEAR;
                AddressU = Wrap;
                AddressV = Wrap;
                AddressW = Wrap;
            };
            
            // Shader properties
            float3 _VolumeMin;
            float3 _VolumeSize;
            float _VoxelSize;
            int _MaxMipLevel;
            int _MaxSteps;
            float _MaxDistance;
            float _SurfaceThreshold;
            int _LayerCount;
            int _FillCount;
            
            // Texture Settings
            float _TextureTiling;
            float _TextureSharpness;

            // Lighting
            float _AmbientIntensity;
            float _DiffuseIntensity;

            // Layer data (packed in evaluation order: fills first, then bands)
            float4 _LayerColors[8];
            float4 _LayerParams[8];
            float4 _LayerParams2[8];
            int _GeometryTypes[8];
            
            // Layer textures
            Texture2D _LayerAlbedo0; Texture2D _LayerNormal0;
            Texture2D _LayerAlbedo1; Texture2D _LayerNormal1;
            Texture2D _LayerAlbedo2; Texture2D _LayerNormal2;
            Texture2D _LayerAlbedo3; Texture2D _LayerNormal3;
            Texture2D _LayerAlbedo4; Texture2D _LayerNormal4;
            Texture2D _LayerAlbedo5; Texture2D _LayerNormal5;
            Texture2D _LayerAlbedo6; Texture2D _LayerNormal6;
            Texture2D _LayerAlbedo7; Texture2D _LayerNormal7;
            
            // Normal calculation
            float _NormalEpsilon;

            // Shadow parameters
            float _EnableSelfShadows;
            int _ShadowSteps;
            float _ShadowDistance;
            float _ShadowSoftness;
            
            // Light direction
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
            };

            // ================================================================
            // Scene SDF — pure texture sample
            // Volume stores scene SDF directly: negative = solid, positive = air.
            // No analytical base terrain, no CSG negation.
            // ================================================================

            float EvaluateSceneSDF(float3 cameraRelativePos, int mipLevel)
            {
                float3 absoluteWorldPos = cameraRelativePos + _WorldSpaceCameraPos;
                float3 uvw = WorldToUVW(absoluteWorldPos, _VolumeMin, _VolumeSize);

                // Outside volume = air
                if (any(uvw < 0.0) || any(uvw > 1.0))
                    return 9999.0;

                if (mipLevel == 0)
                    return _CarveVolume.SampleLevel(sampler_linear_clamp, uvw, 0).r;
                else
                    return _CarveVolume.SampleLevel(sampler_point_clamp, uvw, mipLevel).r;
            }

            // ================================================================
            // Ray-box intersection
            // ================================================================

            bool RayAABBIntersect(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax,
                                  out float tEnter, out float tExit)
            {
                float3 invDir = 1.0 / (abs(rayDir) > 1e-8 ? rayDir : (sign(rayDir) * 1e-8));
                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);
                tEnter = max(tMin.x, max(tMin.y, tMin.z));
                tExit  = min(tMax.x, min(tMax.y, tMax.z));
                return (tExit >= tEnter);
            }

            // ================================================================
            // Hierarchical raymarching
            // Conservative mips are now directly valid for the scene SDF.
            // No CSG inversion — standard safety threshold of 1.5x voxel size.
            // ================================================================

            bool RaymarchSegment(float3 origin, float3 dir, float tStart, float tEnd,
                                 out float3 hitPoint, out float totalDist)
            {
                float t = max(tStart, 0.0);
                int mip = _MaxMipLevel;

                for (int i = 0; i < _MaxSteps; i++)
                {
                    if (t > tEnd || t > _MaxDistance)
                        break;

                    float3 p = origin + dir * t;
                    float d = EvaluateSceneSDF(p, mip);

                    float voxelSize = GetVoxelSize(_VoxelSize, mip);

                    // Refine to finer mip when distance is within safety margin
                    if (d < voxelSize * 1.5 && mip > 0)
                    {
                        mip--;
                        continue;
                    }

                    // Hit
                    if (d < _SurfaceThreshold)
                    {
                        hitPoint = p;
                        totalDist = t;
                        return true;
                    }

                    t += d;
                }

                hitPoint = origin + dir * t;
                totalDist = t;
                return false;
            }

            // ================================================================
            // Material evaluation
            // Fills: youngest→oldest (forward: 0 to _FillCount-1)
            // Bands: oldest→youngest (reverse: _LayerCount-1 down to _FillCount)
            // Both stored youngest-first; bands iterated backward so oldest wins.
            // ================================================================

            void SampleLayerTexture(int index, float3 pos, float3 normal,
                                    out float3 albedo, out float3 normalTan)
            {
                float scale = _TextureTiling;
                float sharpness = _TextureSharpness;
                float mip = 0;
                
                float3 col = float3(1,1,1);
                float3 nrm = float3(0.5, 0.5, 1.0);

                if      (index == 0) { col = TriplanarSampleLevel(_LayerAlbedo0, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal0, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 1) { col = TriplanarSampleLevel(_LayerAlbedo1, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal1, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 2) { col = TriplanarSampleLevel(_LayerAlbedo2, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal2, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 3) { col = TriplanarSampleLevel(_LayerAlbedo3, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal3, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 4) { col = TriplanarSampleLevel(_LayerAlbedo4, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal4, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 5) { col = TriplanarSampleLevel(_LayerAlbedo5, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal5, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 6) { col = TriplanarSampleLevel(_LayerAlbedo6, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal6, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 7) { col = TriplanarSampleLevel(_LayerAlbedo7, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal7, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                
                albedo = col;
                normalTan = nrm * 2.0 - 1.0;
            }
            
            float3 PerturbNormal(float3 worldNormal, float3 tangentNormal)
            {
                float3 N = normalize(worldNormal);
                float3 ref_vec = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 T = normalize(cross(ref_vec, N));
                float3 B = cross(N, T);
                float3x3 TBN = float3x3(T, B, N);
                return normalize(mul(tangentNormal, TBN));
            }

            void GetMaterialData(float3 worldPos, float3 normal, out float3 color, out float3 finalNormal)
            {
                int layerIndex = -1;

                // 1. Check fills: youngest→oldest (indices 0 to _FillCount-1)
                for (int i = 0; i < _FillCount; i++)
                {
                    float sdf = EvaluateLayerSDF(worldPos, _GeometryTypes[i], _LayerParams[i], _LayerParams2[i]);
                    if (sdf < 0.0)
                    {
                        layerIndex = i;
                        break;
                    }
                }

                // 2. If no fill found, check bands: oldest→youngest (reverse)
                //    Bands are packed youngest-first at indices _FillCount.._LayerCount-1,
                //    so iterate backward to check oldest first.
                if (layerIndex < 0)
                {
                    for (int i = _LayerCount - 1; i >= _FillCount; i--)
                    {
                        float sdf = EvaluateLayerSDF(worldPos, _GeometryTypes[i], _LayerParams[i], _LayerParams2[i]);
                        if (sdf < 0.0)
                        {
                            layerIndex = i;
                            break;
                        }
                    }
                }
                
                if (layerIndex >= 0)
                {
                    float3 texColor;
                    float3 texNormal;
                    SampleLayerTexture(layerIndex, worldPos, normal, texColor, texNormal);
                    
                    color = _LayerColors[layerIndex].rgb * texColor;
                    finalNormal = PerturbNormal(normal, texNormal);
                }
                else
                {
                    color = float3(1, 0, 1); // Magenta fallback
                    finalNormal = normal;
                }
            }

            // ================================================================
            // Soft shadows
            // ================================================================

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

            // ================================================================
            // Vertex / Fragment
            // ================================================================
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(posWS);
                output.positionWS = posWS;
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

                float3 rayOrigin = float3(0, 0, 0); // Camera-relative
                float3 rayDir = normalize(input.positionWS);

                // Volume AABB in camera-relative space
                float3 boxMin = _VolumeMin - _WorldSpaceCameraPos;
                float3 boxMax = (_VolumeMin + _VolumeSize) - _WorldSpaceCameraPos;

                float tEnter, tExit;
                if (!RayAABBIntersect(rayOrigin, rayDir, boxMin, boxMax, tEnter, tExit))
                    discard;

                tEnter = max(tEnter, 0.0) + 1e-4;

                float3 hitPoint;
                float totalDist;
                bool hit = RaymarchSegment(rayOrigin, rayDir, tEnter, tExit, hitPoint, totalDist);

                if (!hit) discard;

                // Absolute world position for material lookups
                float3 hitPointAbsolute = hitPoint + _WorldSpaceCameraPos;

                // Normal via central differences on the scene SDF
                float3 eps = float3(_NormalEpsilon, 0.0, 0.0);
                float3 normal = normalize(float3(
                    EvaluateSceneSDF(hitPoint + eps.xyy, 0) - EvaluateSceneSDF(hitPoint - eps.xyy, 0),
                    EvaluateSceneSDF(hitPoint + eps.yxy, 0) - EvaluateSceneSDF(hitPoint - eps.yxy, 0),
                    EvaluateSceneSDF(hitPoint + eps.yyx, 0) - EvaluateSceneSDF(hitPoint - eps.yyx, 0)
                ));

                // Material
                float3 materialColor;
                float3 finalNormal;
                GetMaterialData(hitPointAbsolute, normal, materialColor, finalNormal);

                // Lighting
                float3 lightDir = normalize(_MainLightDirection.xyz);
                if (length(_MainLightDirection) < 0.01)
                    lightDir = normalize(float3(1, 1, 1));
                
                float ndotl = max(0.0, dot(finalNormal, lightDir));
                float shadow = ComputeSoftShadow(hitPoint, finalNormal, lightDir);

                float3 ambient = materialColor * _AmbientIntensity * _DiffuseIntensity;
                float3 diffuse = materialColor * ndotl * shadow * _MainLightColor.rgb * _DiffuseIntensity;
                output.color = float4(ambient + diffuse, 1.0);

                // Depth
                float4 clipPos = TransformWorldToHClip(hitPoint);
                output.depth = clipPos.z / clipPos.w;

                return output;
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/InternalErrorShader"
}
