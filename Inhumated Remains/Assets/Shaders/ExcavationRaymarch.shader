Shader "Excavation/ExcavationRaymarch"
{
    Properties
    {
        // Volume Data
        _CarveVolume("Carve Volume", 3D) = "white" {}
        _VolumeOrigin("Volume Origin", Vector) = (0, 0, 0, 0)
        _VolumeSize("Volume Size", Vector) = (10, 5, 10, 0)
        _VoxelSize("Voxel Size", Float) = 0.05
        _MaxMipLevel("Max MIP Level", Int) = 4
        
        // Raymarching Parameters
        _MaxSteps("Max Steps", Int) = 128
        _MaxDistance("Max Distance", Float) = 50.0
        _SurfaceThreshold("Surface Threshold", Float) = 0.001
        
        // Rendering
        _BaseTerrainY("Base Terrain Y", Float) = 0.0
        
        // Stratigraphy (up to 8 layers)
        _LayerCount("Layer Count", Int) = 0
        
        // Texture Settings (renamed from TextureScale for clarity - higher = smaller tiles)
        _TextureTiling("Texture Tiling", Float) = 1.0
        _TextureSharpness("Texture Sharpness", Float) = 8.0

        // Lighting
        _AmbientIntensity("Ambient Intensity", Range(0, 1)) = 0.15

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
            int _MaxMipLevel;
            int _MaxSteps;
            float _MaxDistance;
            float _SurfaceThreshold;
            float _BaseTerrainY;
            int _LayerCount;
            
            // Texture Settings
            float _TextureTiling;
            float _TextureSharpness;

            // Lighting
            float _AmbientIntensity;

            // Layer data (extended for all geometry types)
            float4 _LayerColors[8];
            float4 _LayerParams[8];   // Primary geometry parameters
            float4 _LayerParams2[8];  // Secondary geometry parameters
            int _GeometryTypes[8];    // Geometry type IDs
            
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

            // Convert SV_POSITION (pixel coords) -> normalized screen UV (0..1)
            float2 GetNormalizedScreenUV(float4 positionCS)
            {
                // In fragment stage, SV_POSITION.xy is in pixel coordinates in Unity.
                // _ScreenSize.zw = (1/width, 1/height)
                return positionCS.xy * _ScreenSize.zw;
            }

            // Unproject a point from NDC into world space using inverse view-projection.
            // ndc.xy in [-1,1]. ndc.z in [0,1] (Unity/D3D style in modern pipelines).
            float3 UnprojectNDCToWorld(float3 ndc)
            {
                float4 clip = float4(ndc.xy, ndc.z, 1.0);
                float4 worldH = mul(UNITY_MATRIX_I_VP, clip);
                return worldH.xyz / max(worldH.w, 1e-6);
            }

            // Build a world-space view ray for this pixel.
            // Returns: rayOrigin = camera position, rayDir = normalized direction.
            // Uses near/far unprojection so it works whether camera is inside/outside the proxy mesh.
            void BuildWorldSpaceViewRay(float4 positionCS, out float3 rayOrigin, out float3 rayDir)
            {
                float2 uv = GetNormalizedScreenUV(positionCS);

                // NDC x/y in [-1,1]
                float2 ndcXY = uv * 2.0 - 1.0;

                // Unproject near and far points
                // UNITY_MATRIX_I_VP gives CAMERA-RELATIVE world positions in HDRP
                // (camera is at origin in this space)
                float3 worldNear = UnprojectNDCToWorld(float3(ndcXY, 0.0));
                float3 worldFar  = UnprojectNDCToWorld(float3(ndcXY, 1.0));

                // In camera-relative rendering, camera is at origin
                rayOrigin = float3(0, 0, 0);
                rayDir    = normalize(worldFar - worldNear);
            }

            // Ray-box intersection for an axis-aligned bounding box.
            // Returns true if intersects, and outputs the parametric interval [tEnter, tExit].
            bool RayAABBIntersect(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax, out float tEnter, out float tExit)
            {
                // Avoid division by zero: reciprocal with big number for near-zero components
                float3 invDir = 1.0 / (abs(rayDir) > 1e-8 ? rayDir : (sign(rayDir) * 1e-8));

                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;

                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);

                tEnter = max(tMin.x, max(tMin.y, tMin.z));
                tExit  = min(tMax.x, min(tMax.y, tMax.z));

                // Intersection exists if exit is after enter
                return (tExit >= tEnter);
            }


            // Evaluate base terrain SDF (flat ground for now)
            // worldPos is camera-relative; convert to absolute Y for comparison
            float EvaluateBaseTerrain(float3 worldPos)
            {
                return (worldPos.y + _WorldSpaceCameraPos.y) - _BaseTerrainY;
            }
            
            // Evaluate the complete scene SDF
            // worldPos is in camera-relative space
            // Uses linear sampling at MIP 0 for visual smoothness, point for higher MIPs
            float EvaluateSceneSDF(float3 worldPos, int mipLevel)
            {
                float baseSDF = EvaluateBaseTerrain(worldPos);
                
                // Convert camera-relative position to absolute world position for UVW lookup
                float3 absoluteWorldPos = worldPos + _WorldSpaceCameraPos;
                float3 uvw = WorldToUVW(absoluteWorldPos, _VolumeOrigin, _VolumeSize);

                // Outside volume: just return terrain
                if (any(uvw < 0.0) || any(uvw > 1.0))
                {
                    return baseSDF;
                }

                // Sample carve volume (stored as positive = carved away)
                float carveSDF = _CarveVolume.SampleLevel(sampler_point_clamp, uvw, mipLevel).r;
                
                // Combine: terrain minus carved regions
                return max(baseSDF, -carveSDF);
            }
            
            // Helper to sample correct texture based on index
            void SampleLayerTexture(int index, float3 pos, float3 normal, out float3 albedo, out float3 normalTan)
            {
                float mip = 0; 
                float scale = _TextureTiling;
                float sharpness = _TextureSharpness;
                
                float3 col = float3(1,1,1);
                float3 nrm = float3(0.5, 0.5, 1.0); // Default normal (unpacked neutral)

                // Sample albedo - manual unroll for texture array workaround
                if (index == 0) { col = TriplanarSampleLevel(_LayerAlbedo0, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal0, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 1) { col = TriplanarSampleLevel(_LayerAlbedo1, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal1, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 2) { col = TriplanarSampleLevel(_LayerAlbedo2, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal2, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 3) { col = TriplanarSampleLevel(_LayerAlbedo3, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal3, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 4) { col = TriplanarSampleLevel(_LayerAlbedo4, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal4, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 5) { col = TriplanarSampleLevel(_LayerAlbedo5, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal5, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 6) { col = TriplanarSampleLevel(_LayerAlbedo6, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal6, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                else if (index == 7) { col = TriplanarSampleLevel(_LayerAlbedo7, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; nrm = TriplanarSampleLevel(_LayerNormal7, sampler_linear_repeat, pos, normal, scale, sharpness, mip).rgb; }
                
                albedo = col;
                // Unpack normal from 0-1 to -1 to 1 range
                normalTan = nrm * 2.0 - 1.0;
            }
            
            // Perturb world normal using tangent-space normal map
            float3 PerturbNormal(float3 worldNormal, float3 worldPos, float3 tangentNormal)
            {
                // Build TBN matrix from world normal
                // For procedural geometry, we derive tangent/bitangent from world axes
                float3 N = normalize(worldNormal);
                
                // Choose a reference vector that's not parallel to N
                float3 ref = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 T = normalize(cross(ref, N));
                float3 B = cross(N, T);
                
                // Transform tangent normal to world space
                float3x3 TBN = float3x3(T, B, N);
                return normalize(mul(tangentNormal, TBN));
            }
            
            // Get material layer at world position
            void GetMaterialData(float3 worldPos, float3 normal, out float3 color, out float3 finalNormal)
            {
                int layerIndex = -1;

                for (int i = 0; i < _LayerCount; i++)
                {
                    // Use shared EvaluateLayerSDF with geometry type dispatch
                    int geomType = _GeometryTypes[i];
                    float4 params1 = _LayerParams[i];
                    float4 params2 = _LayerParams2[i];
                    
                    float sdf = EvaluateLayerSDF(worldPos, geomType, params1, params2);
                    
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
                    // Apply normal map perturbation
                    finalNormal = PerturbNormal(normal, worldPos, texNormal);
                }
                else
                {
                    // fallback
                    color = float3(1, 0.0, 1); // Magenta for debugging
                    finalNormal = normal;
                }
            }
            
            bool RaymarchSegment(float3 origin, float3 dir, float tStart, float tEnd,
                     out float3 hitPoint, out float totalDist)
            {
                // Clamp start to non-negative so we don't march behind camera
                float t = max(tStart, 0.0);
                int mip = _MaxMipLevel;

                for (int i = 0; i < _MaxSteps; i++)
                {
                    if (t > tEnd || t > _MaxDistance)
                        break;

                    float3 p = origin + dir * t;
                    float d = EvaluateSceneSDF(p, mip);

                    float voxelSize = GetVoxelSize(_VoxelSize, mip);

                    // Hierarchical refinement: if we're close to a surface at this mip, drop mip
                    if (d < voxelSize * 1.5 && mip > 0)
                    {
                        mip--;
                        continue;
                    }

                    // Hit condition
                    if (d < _SurfaceThreshold)
                    {
                        hitPoint = p;
                        totalDist = t;
                        return true;
                    }

                    // Step forward
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
                // TransformObjectToWorld returns CAMERA-RELATIVE position in HDRP
                // (camera is at origin in this coordinate space)
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

                // In HDRP camera-relative rendering, camera is at (0,0,0)
                float3 rayOrigin = float3(0, 0, 0);
                float3 rayDir = normalize(input.positionWS);

                // Volume AABB in camera-relative space
                float3 boxMin = (_VolumeOrigin - 0.5 * _VolumeSize) - _WorldSpaceCameraPos;
                float3 boxMax = (_VolumeOrigin + 0.5 * _VolumeSize) - _WorldSpaceCameraPos;

                float tEnter, tExit;
                if (!RayAABBIntersect(rayOrigin, rayDir, boxMin, boxMax, tEnter, tExit))
                    discard;

                tEnter = max(tEnter, 0.0) + 1e-4;

                // Raymarch the terrain + carve volume SDF
                float3 hitPoint;
                float totalDist;
                bool hit = RaymarchSegment(rayOrigin, rayDir, tEnter, tExit, hitPoint, totalDist);

                if (!hit) discard;

                // Convert hit point to absolute world space for material lookups
                float3 hitPointAbsolute = hitPoint + _WorldSpaceCameraPos;

                // Calculate normal via finite differences on the SDF
                float3 eps = float3(0.001, 0.0, 0.0);
                float3 normal = normalize(float3(
                    EvaluateSceneSDF(hitPoint + eps.xyy, 0) - EvaluateSceneSDF(hitPoint - eps.xyy, 0),
                    EvaluateSceneSDF(hitPoint + eps.yxy, 0) - EvaluateSceneSDF(hitPoint - eps.yxy, 0),
                    EvaluateSceneSDF(hitPoint + eps.yyx, 0) - EvaluateSceneSDF(hitPoint - eps.yyx, 0)
                ));

                // Get material data (uses absolute world position for layer SDFs and texturing)
                float3 materialColor;
                float3 finalNormal;
                GetMaterialData(hitPointAbsolute, normal, materialColor, finalNormal);

                // Lighting: use main light direction or fallback
                float3 lightDir = normalize(_MainLightDirection.xyz);
                if (length(_MainLightDirection) < 0.01)
                    lightDir = normalize(float3(1, 1, 1));
                
                float ndotl = max(0.0, dot(finalNormal, lightDir));

                // Compute shadows if enabled
                float shadow = ComputeSoftShadow(hitPoint, finalNormal, lightDir);

                // Final color with ambient and diffuse lighting
                float3 ambient = materialColor * _AmbientIntensity;
                float3 diffuse = materialColor * ndotl * shadow * _MainLightColor.rgb;
                output.color = float4(ambient + diffuse, 1.0);

                // hitPoint is camera-relative, TransformWorldToHClip expects this in HDRP
                float4 clipPos = TransformWorldToHClip(hitPoint);
                output.depth = clipPos.z / clipPos.w;

                return output;
            }

            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/InternalErrorShader"
}
