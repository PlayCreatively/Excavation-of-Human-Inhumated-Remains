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

            if (raymarchMaterial != null)
                meshRenderer.material = raymarchMaterial;

            GenerateProxyMesh();
        }

        private void GenerateProxyMesh()
        {
            var settings = excavationManager.Settings;

            transform.position = settings.worldOrigin + settings.worldSize * 0.5f;

            Mesh mesh = new Mesh();
            mesh.name = "Excavation Proxy";

            Vector3 halfSize = settings.worldSize * 0.5f;
            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3( halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3( halfSize.x,  halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x,  halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x, -halfSize.y,  halfSize.z),
                new Vector3( halfSize.x, -halfSize.y,  halfSize.z),
                new Vector3( halfSize.x,  halfSize.y,  halfSize.z),
                new Vector3(-halfSize.x,  halfSize.y,  halfSize.z),
            };

            int[] triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                5, 6, 4, 4, 6, 7,
                4, 7, 0, 0, 7, 3,
                1, 2, 5, 5, 2, 6,
                3, 6, 2, 3, 7, 6,
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
            mat.SetInt("_MaxMipLevel", settings.GetMaxMipLevel());

            // Layer data (packed in evaluation order: fills first, then bands)
            if (stratigraphy != null && stratigraphy.Layers != null)
            {
                stratigraphy.GetGPULayerData(
                    out Color[] colors, out Vector4[] layerParams, out Vector4[] layerParams2,
                    out float[] geometryTypes, out int fillCount, out int totalCount);

                mat.SetInt("_LayerCount", totalCount);
                mat.SetInt("_FillCount", fillCount);
                mat.SetColorArray("_LayerColors", colors);
                mat.SetVectorArray("_LayerParams", layerParams);
                mat.SetVectorArray("_LayerParams2", layerParams2);
                mat.SetFloatArray("_GeometryTypes", geometryTypes);

                // Set layer textures in evaluation order
                stratigraphy.SetLayerTextures(mat);
            }
        }
    }
}
