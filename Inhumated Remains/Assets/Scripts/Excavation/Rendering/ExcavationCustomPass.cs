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
        [Header("Rendering")]
        public Material raymarchMaterial;
        public MeshFilter proxyMeshFilter;
        public Transform volumeTransform;

        [Header("References")]
        public Core.ExcavationManager excavationManager;
        public Stratigraphy.StratigraphyEvaluator stratigraphy;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            // Setup called once
        }

        protected override void Execute(CustomPassContext ctx)
        {
            Mesh proxyMesh = proxyMeshFilter != null ? proxyMeshFilter.sharedMesh : null;

            if (raymarchMaterial == null || proxyMesh == null || excavationManager == null)
                return;

            if (excavationManager.CarveVolume == null)
                return;

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
            raymarchMaterial.SetFloat("_TextureScale", settings.textureScale);
            raymarchMaterial.SetFloat("_TextureSharpness", settings.textureSharpness);

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

                Color[] layerColors = new Color[8];
                Vector4[] layerParams = new Vector4[8];

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
                        
                        float topY = 0;
                        float bottomY = 0;

                        if (layer.geometryData is Stratigraphy.DepthBandGeometry depth)
                        {
                            topY = depth.topY;
                            bottomY = depth.bottomY;
                        }
                        else if (layer.geometryData is Stratigraphy.NoisyDepthBandGeometry noisy)
                        {
                            topY = noisy.baseTopY;
                            bottomY = noisy.baseBottomY;
                        }
                        else
                        {
                             // Fallback for unknown geometry - maybe use a defaulted infinite band?
                             // But defaulting to 0,0 is safe (invisible)
                        }
                        
                        layerParams[i] = new Vector4(topY, bottomY, 1.0f, 0); // z=scale could be added here
                    }
                    else
                    {
                        layerColors[i] = Color.black; 
                        layerParams[i] = Vector4.zero;
                    }
                }

                raymarchMaterial.SetColorArray("_LayerColors", layerColors);
                raymarchMaterial.SetVectorArray("_LayerParams", layerParams);
            }
            else
            {
                raymarchMaterial.SetInt("_LayerCount", 0);
            }
        }
    }
}
