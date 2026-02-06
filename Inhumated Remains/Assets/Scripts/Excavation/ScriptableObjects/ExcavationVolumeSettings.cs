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
        [Tooltip("Texture format for the SDF volume")]
        public TextureFormat textureFormat = TextureFormat.R16_SFloat;

        [Tooltip("Maximum MIP level for hierarchical raymarching (auto-calculated from resolution)")]
        public int maxMipLevel = 4;

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

        [Tooltip("Scale factor for triplanar texture mapping")]
        [Range(0.1f, 10f)]
        public float textureScale = 1.0f;

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
        }

        /// <summary>
        /// Get the texture format enum.
        /// </summary>
        public enum TextureFormat
        {
            R16_SFloat,  // 16-bit float (recommended)
            R8_SNorm     // 8-bit signed normalized (-1 to +1 range)
        }
    }
}
