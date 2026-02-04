using UnityEngine;
using Excavation.Stratigraphy;

namespace Excavation.Rendering
{
    /// <summary>
    /// Renders the excavated terrain using raymarching.
    /// Manages the proxy mesh and material property updates.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ExcavationRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Core.ExcavationManager excavationManager;
        [SerializeField] private StratigraphyEvaluator stratigraphy;

        [Header("Rendering")]
        [SerializeField] private Material raymarchMaterial;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        void Start()
        {
            if (excavationManager == null)
            {
                Debug.LogError("[ExcavationRenderer] ExcavationManager not assigned!");
                enabled = false;
                return;
            }

            if (stratigraphy == null)
            {
                Debug.LogError("[ExcavationRenderer] StratigraphyEvaluator not assigned!");
                enabled = false;
                return;
            }

            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            // Set material
            if (raymarchMaterial != null)
            {
                meshRenderer.material = raymarchMaterial;
            }

            // Generate bounding box proxy mesh
            GenerateProxyMesh();
        }

        /// <summary>
        /// Generate a cube mesh that encompasses the excavation volume.
        /// This is what we render with the raymarching shader.
        /// </summary>
        private void GenerateProxyMesh()
        {
            var settings = excavationManager.Settings;
            
            // Position the renderer at the volume's center
            transform.position = settings.worldOrigin + settings.worldSize * 0.5f;
            
            // Create a cube mesh with the volume's dimensions
            Mesh mesh = new Mesh();
            mesh.name = "Excavation Proxy";

            // Simple cube vertices (local space, centered at origin)
            Vector3 halfSize = settings.worldSize * 0.5f;
            Vector3[] vertices = new Vector3[]
            {
                // Front face
                new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3( halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3( halfSize.x,  halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x,  halfSize.y, -halfSize.z),
                
                // Back face
                new Vector3(-halfSize.x, -halfSize.y,  halfSize.z),
                new Vector3( halfSize.x, -halfSize.y,  halfSize.z),
                new Vector3( halfSize.x,  halfSize.y,  halfSize.z),
                new Vector3(-halfSize.x,  halfSize.y,  halfSize.z),
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

            meshFilter.mesh = mesh;
        }

        void LateUpdate()
        {
            if (meshRenderer.sharedMaterial == null || excavationManager.CarveVolume == null)
                return;

            UpdateMaterialProperties();
        }

        /// <summary>
        /// Update material properties for the raymarching shader.
        /// </summary>
        private void UpdateMaterialProperties()
        {
            Material mat = meshRenderer.material;
            var settings = excavationManager.Settings;

            // Volume data
            mat.SetTexture("_CarveVolume", excavationManager.CarveVolume);
            mat.SetVector("_VolumeOrigin", settings.worldOrigin);
            mat.SetVector("_VolumeSize", settings.worldSize);
            mat.SetFloat("_VoxelSize", settings.voxelSize);

            // Raymarching parameters
            mat.SetInt("_MaxSteps", settings.maxRaymarchSteps);
            mat.SetFloat("_MaxDistance", settings.maxRaymarchDistance);
            mat.SetFloat("_SurfaceThreshold", settings.surfaceThreshold);

            // Camera position for raymarching
            if (Camera.main != null)
            {
                mat.SetVector("_CameraPosition", Camera.main.transform.position);
            }

            // Layer data (simplified - just pass count and first few layers)
            if (stratigraphy != null && stratigraphy.Layers != null)
            {
                mat.SetInt("_LayerCount", Mathf.Min(stratigraphy.Layers.Count, 8)); // Max 8 layers for shader

                // Pass layer colors and properties
                Color[] layerColors = new Color[8];
                Vector4[] layerParams = new Vector4[8]; // Store geometry parameters

                for (int i = 0; i < Mathf.Min(stratigraphy.Layers.Count, 8); i++)
                {
                    if (stratigraphy.Layers[i] != null)
                    {
                        layerColors[i] = stratigraphy.Layers[i].baseColour;
                        
                        // Pack layer geometry info (type-specific)
                        if (stratigraphy.Layers[i].geometryData is DepthBandGeometry depth)
                        {
                            layerParams[i] = new Vector4(depth.topY, depth.bottomY, 0, 0); // Type 0 = DepthBand
                        }
                    }
                }

                mat.SetColorArray("_LayerColors", layerColors);
                mat.SetVectorArray("_LayerParams", layerParams);
            }
        }
    }
}
