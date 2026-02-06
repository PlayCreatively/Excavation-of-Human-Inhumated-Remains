using UnityEngine;

namespace Excavation.Core
{
    /// <summary>
    /// Configuration settings for the excavation volume.
    /// Defines the 3D texture resolution, size, and rendering parameters.
    /// </summary>
    [CreateAssetMenu(fileName = "New Volume Settings", menuName = "Excavation/Volume Settings")]
    public class ExcavationVolumeSettings : ScriptableObject
    {
        [Header("Volume Bounds")]
        [Tooltip("World-space origin point (bottom-left-front corner)")]
        public Vector3 worldOrigin = Vector3.zero;

        [Tooltip("World-space size of the excavation volume")]
        public Vector3 worldSize = new Vector3(10f, 5f, 10f);

        [Tooltip("Size of each voxel in meters (smaller = higher resolution)")]
        [Range(0.01f, 0.5f)]
        public float voxelSize = 0.05f; // 5cm default

        [Header("Texture Configuration")]
        // Note: Only R16_SFloat (16-bit float) is supported for the SDF volume.
        // R8_SNorm was considered but requires distance remapping and reduces precision.

        [Header("Rendering Parameters")]
        [Tooltip("Maximum raymarching steps before giving up")]
        [Range(32, 512)]
        public int maxRaymarchSteps = 128;

        [Tooltip("Maximum raymarching distance in meters")]
        [Range(1f, 100f)]
        public float maxRaymarchDistance = 50f;

        [Tooltip("Surface detection threshold (smaller = more precise)")]
        [Range(0.0001f, 0.01f)]
        public float surfaceThreshold = 0.001f;

        [Tooltip("Tiling factor for triplanar texture mapping (higher = smaller tiles)")]
        [Range(0.1f, 10f)]
        public float textureTiling = 1.0f;

        [Tooltip("Sharpness of triplanar texture blending (higher = sharper transitions)")]
        [Range(1f, 64f)]
        public float textureSharpness = 8.0f;

        /// <summary>
        /// Calculate the texture resolution based on world size and voxel size.
        /// </summary>
        public Vector3Int GetTextureResolution()
        {
            return new Vector3Int(
                Mathf.CeilToInt(worldSize.x / voxelSize),
                Mathf.CeilToInt(worldSize.y / voxelSize),
                Mathf.CeilToInt(worldSize.z / voxelSize)
            );
        }

        /// <summary>
        /// Validate settings and clamp to reasonable ranges.
        /// </summary>
        private void OnValidate()
        {
            // Ensure minimum volume size
            worldSize.x = Mathf.Max(worldSize.x, 0.1f);
            worldSize.y = Mathf.Max(worldSize.y, 0.1f);
            worldSize.z = Mathf.Max(worldSize.z, 0.1f);

            // Warn about extreme resolutions
            Vector3Int resolution = GetTextureResolution();
            int totalVoxels = resolution.x * resolution.y * resolution.z;

            if (totalVoxels > 8_000_000) // 200 cubed
            {
                Debug.LogWarning($"[ExcavationVolumeSettings] Very high resolution: {resolution} ({totalVoxels:N0} voxels). This may impact performance.");
            }

            // Auto-calculate max MIP level based on smallest dimension
            int minDim = Mathf.Min(resolution.x, Mathf.Min(resolution.y, resolution.z));
            int calculatedMaxMip = Mathf.FloorToInt(Mathf.Log(minDim, 2));
            if (calculatedMaxMip < 1) calculatedMaxMip = 1;
            // Store for runtime use (read via GetMaxMipLevel())
        }

        /// <summary>
        /// Calculate the maximum MIP level based on volume resolution.
        /// </summary>
        public int GetMaxMipLevel()
        {
            Vector3Int resolution = GetTextureResolution();
            int minDim = Mathf.Min(resolution.x, Mathf.Min(resolution.y, resolution.z));
            int maxMip = Mathf.FloorToInt(Mathf.Log(minDim, 2));
            return Mathf.Max(1, Mathf.Min(maxMip, 8)); // Clamp between 1 and 8
        }
    }
}
