using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Excavation.Rendering
{
    /// <summary>
    /// HDRP Custom Pass for rendering the excavation volume with raymarching.
    /// This is necessary because standard rendering doesn't work with procedural raymarched geometry.
    /// </summary>
    [Serializable]
    public class ExcavationCustomPass : CustomPass
    {
        [SerializeField]
        bool renderInEditMode = true;
        [Header("Rendering")]
        public Material raymarchMaterial;
        public Transform volumeTransform;
        Mesh proxyMesh;

        [Header("References")]
        public Core.ExcavationManager excavationManager;
        public Stratigraphy.StratigraphyEvaluator stratigraphy;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            // Setup called once
            proxyMesh = GenerateProxyMesh();
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (raymarchMaterial == null || proxyMesh == null || excavationManager == null)
            {
                Debug.LogWarning("[ExcavationCustomPass] Missing references, skipping rendering.");
                return;
            }

            if (excavationManager.CarveVolume == null && (Application.isPlaying || !renderInEditMode))
            {
                if (Application.isPlaying)
                    Debug.LogWarning("[ExcavationCustomPass] CarveVolume not ready, skipping rendering.");
                return;
            }

            // Update material properties
            UpdateMaterialProperties();

            // Draw the proxy mesh with the raymarch material
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer);

            // Set up the rendering
            ctx.cmd.DrawMesh(
                proxyMesh,
                volumeTransform != null ? volumeTransform.localToWorldMatrix : Matrix4x4.identity,
                raymarchMaterial,
                0, // submesh index
                0  // pass index
            );
        }

        /// <summary>
        /// Generate a cube mesh that encompasses the excavation volume.
        /// This is what we render with the raymarching shader.
        /// </summary>
        private Mesh GenerateProxyMesh()
        {
            var settings = excavationManager.Settings;

            // Create a cube mesh with the volume's dimensions
            Mesh mesh = new()
            {
                name = "Excavation Proxy"
            };

            // Simple cube vertices (local space, centered at origin)
            Vector3 halfSize = settings.worldSize * 0.5f;
            Vector3[] vertices = new Vector3[]
            {
                // Front face
                new (-halfSize.x, -halfSize.y, -halfSize.z),
                new ( halfSize.x, -halfSize.y, -halfSize.z),
                new ( halfSize.x,  halfSize.y, -halfSize.z),
                new (-halfSize.x,  halfSize.y, -halfSize.z),
                
                // Back face
                new (-halfSize.x, -halfSize.y,  halfSize.z),
                new ( halfSize.x, -halfSize.y,  halfSize.z),
                new ( halfSize.x,  halfSize.y,  halfSize.z),
                new (-halfSize.x,  halfSize.y,  halfSize.z),
            };

            int[] triangles = new int[]
            {
                // Front
                0, 2, 1, 0, 3, 2,
                // Back
                5, 6, 4, 4, 6, 7,
                // Left
                4, 7, 0, 0, 7, 3,
                // Right
                1, 2, 5, 5, 2, 6,
                // Top
                3, 6, 2, 3, 7, 6,
                // Bottom
                4, 0, 1, 4, 1, 5
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }


        protected override void Cleanup()
        {
            // Cleanup resources if needed
        }

        private void UpdateMaterialProperties()
        {
            if (raymarchMaterial == null || excavationManager == null)
                return;

            var settings = excavationManager.Settings;

            // Volume data
            raymarchMaterial.SetTexture("_CarveVolume", excavationManager.CarveVolume);
            raymarchMaterial.SetVector("_VolumeOrigin", settings.worldOrigin);
            raymarchMaterial.SetVector("_VolumeSize", settings.worldSize);
            raymarchMaterial.SetFloat("_VoxelSize", settings.voxelSize);

            // Raymarching parameters
            raymarchMaterial.SetInt("_MaxSteps", settings.maxRaymarchSteps);
            raymarchMaterial.SetFloat("_MaxDistance", settings.maxRaymarchDistance);
            raymarchMaterial.SetFloat("_SurfaceThreshold", settings.surfaceThreshold);
            raymarchMaterial.SetFloat("_TextureTiling", settings.textureTiling);
            raymarchMaterial.SetFloat("_TextureSharpness", settings.textureSharpness);

            // Base terrain Y from stratigraphy
            if (stratigraphy != null)
            {
                raymarchMaterial.SetFloat("_BaseTerrainY", stratigraphy.BaseTerrainY);
            }
            else
            {
                raymarchMaterial.SetFloat("_BaseTerrainY", 0f);
            }

            // Camera position
            if (Camera.main != null)
            {
                raymarchMaterial.SetVector("_CameraPosition", Camera.main.transform.position);
            }

            // Lighting: Find main directional light
            Light mainLight = RenderSettings.sun;
            if (mainLight == null)
            {
                // Fallback: try finding first directional light
                var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var light in lights)
                {
                    if (light.type == LightType.Directional)
                    {
                        mainLight = light;
                        break;
                    }
                }
            }

            if (mainLight != null)
            {
                raymarchMaterial.SetVector("_MainLightDirection", mainLight.transform.forward);
                raymarchMaterial.SetColor("_MainLightColor", mainLight.color * mainLight.intensity);
            }
            else
            {
                raymarchMaterial.SetVector("_MainLightDirection", Vector3.down);
                raymarchMaterial.SetColor("_MainLightColor", Color.white);
            }

            // Layer data
            if (stratigraphy != null && stratigraphy.Layers != null)
            {
                int layerCount = Mathf.Min(stratigraphy.Layers.Count, 8);
                raymarchMaterial.SetInt("_LayerCount", layerCount);
                raymarchMaterial.SetInt("_MaxMipLevel", settings.GetMaxMipLevel());

                Color[] layerColors = new Color[8];
                Vector4[] layerParams = new Vector4[8];
                Vector4[] layerParams2 = new Vector4[8]; // Secondary geometry parameters
                float[] geometryTypes = new float[8];        // Geometry type IDs (as floats for shader)

                for (int i = 0; i < 8; i++)
                {
                    if (i < layerCount && stratigraphy.Layers[i] != null)
                    {
                        var layer = stratigraphy.Layers[i];
                        layerColors[i] = layer.baseColour;

                        // Pass textures
                        if (layer.albedoTexture != null)
                            raymarchMaterial.SetTexture($"_LayerAlbedo{i}", layer.albedoTexture);
                        if (layer.normalMap != null)
                            raymarchMaterial.SetTexture($"_LayerNormal{i}", layer.normalMap);

                        // Use polymorphic geometry parameter packing
                        var geometry = layer.geometryData;
                        if (geometry != null)
                        {
                            geometryTypes[i] = (int)geometry.GeometryType;
                            layerParams[i] = geometry.GetPackedParams();
                            layerParams2[i] = geometry.GetPackedParams2();
                        }
                    }
                    else
                    {
                        layerColors[i] = Color.black;
                        layerParams[i] = Vector4.zero;
                        layerParams2[i] = Vector4.zero;
                        geometryTypes[i] = 0f;
                    }
                }

                raymarchMaterial.SetColorArray("_LayerColors", layerColors);
                raymarchMaterial.SetVectorArray("_LayerParams", layerParams);
                raymarchMaterial.SetVectorArray("_LayerParams2", layerParams2);
                // Material doesn't expose SetIntArray on all platforms/versions, use float array instead
                raymarchMaterial.SetFloatArray("_GeometryTypes", geometryTypes);
            }
            else
            {
                raymarchMaterial.SetInt("_LayerCount", 0);
            }
        }
    }
}
