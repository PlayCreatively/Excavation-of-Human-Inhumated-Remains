using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using System.IO.Compression;

namespace Excavation.Core
{
    /// <summary>
    /// Manages the 3D carve volume texture and handles digging operations.
    /// Central coordinator for the excavation system.
    /// </summary>
    public class ExcavationManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private ExcavationVolumeSettings settings;

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader carveShader;
        [SerializeField] private ComputeShader mipGenShader;

        // The 3D SDF texture
        private RenderTexture carveVolume;
        
        // Cached shader kernel IDs
        private int carveKernel;
        private int mipGenKernel;

        // Texture resolution
        private Vector3Int resolution;

        public RenderTexture CarveVolume => carveVolume;
        public ExcavationVolumeSettings Settings => settings;

        void Start()
        {
            if (settings == null)
            {
                Debug.LogError("[ExcavationManager] No settings assigned!");
                enabled = false;
                return;
            }

            InitializeVolume();
        }

        /// <summary>
        /// Create and initialize the 3D carve volume texture.
        /// </summary>
        private void InitializeVolume()
        {
            resolution = settings.GetTextureResolution();

            Debug.Log($"[ExcavationManager] Initializing volume: {resolution.x}x{resolution.y}x{resolution.z} " +
                      $"({resolution.x * resolution.y * resolution.z:N0} voxels)");

            // Create 3D render texture
            carveVolume = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.RFloat)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution.z,
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            carveVolume.Create();

            // Initialize all voxels to large positive value (no excavation)
            ClearVolume();

            // Cache kernel IDs
            if (carveShader != null)
                carveKernel = carveShader.FindKernel("CSCarve");

            if (mipGenShader != null)
                mipGenKernel = mipGenShader.FindKernel("CSGenerateMip");
        }

        /// <summary>
        /// Clear the volume (reset to unexcavated state).
        /// </summary>
        public void ClearVolume()
        {
            if (carveVolume == null) return;

            // Use a simple compute shader to fill with 9999.0
            // For now, we'll use Graphics.Blit with a material (simpler for initialization)
            // In production, use a dedicated compute shader for better performance
            
            ComputeShader initShader = Resources.Load<ComputeShader>("Shaders/InitializeVolume");
            if (initShader != null)
            {
                int kernel = initShader.FindKernel("CSInitialize");
                initShader.SetTexture(kernel, "Result", carveVolume);
                initShader.SetFloat("InitValue", 9999.0f);
                
                int threadGroups = Mathf.CeilToInt(resolution.x / 8f);
                initShader.Dispatch(kernel, threadGroups, threadGroups, threadGroups);
            }
            else
            {
                Debug.LogWarning("[ExcavationManager] Initialize compute shader not found. Volume may not be properly cleared.");
            }
        }

        /// <summary>
        /// Apply a brush stroke to carve the volume.
        /// </summary>
        public void ApplyBrushStroke(BrushStroke stroke)
        {
            if (carveVolume == null || carveShader == null)
            {
                Debug.LogWarning("[ExcavationManager] Cannot apply brush stroke - volume or shader not initialized.");
                return;
            }

            // Set shader parameters
            carveShader.SetTexture(carveKernel, "CarveVolume", carveVolume, 0); // Mip 0
            carveShader.SetVector("BrushPosition", stroke.worldPosition);
            carveShader.SetFloat("BrushRadius", stroke.radius);
            carveShader.SetFloat("DigSpeed", stroke.intensity);
            carveShader.SetFloat("DeltaTime", stroke.deltaTime);
            carveShader.SetVector("VolumeOrigin", settings.worldOrigin);
            carveShader.SetFloat("VoxelSize", settings.voxelSize);
            carveShader.SetVector("VolumeSize", settings.worldSize);

            // Calculate affected region (for optimization, only process nearby voxels)
            Vector3 brushMin = stroke.worldPosition - Vector3.one * (stroke.radius + settings.voxelSize);
            Vector3 brushMax = stroke.worldPosition + Vector3.one * (stroke.radius + settings.voxelSize);

            Vector3Int minVoxel = WorldToVoxel(brushMin);
            Vector3Int maxVoxel = WorldToVoxel(brushMax);

            // Clamp to volume bounds
            minVoxel = Vector3Int.Max(minVoxel, Vector3Int.zero);
            maxVoxel = Vector3Int.Min(maxVoxel, resolution - Vector3Int.one);

            Vector3Int regionSize = maxVoxel - minVoxel;

            carveShader.SetInts("MinVoxel", minVoxel.x, minVoxel.y, minVoxel.z);

            // Dispatch compute shader (8x8x8 thread groups)
            int groupsX = Mathf.CeilToInt(regionSize.x / 8f);
            int groupsY = Mathf.CeilToInt(regionSize.y / 8f);
            int groupsZ = Mathf.CeilToInt(regionSize.z / 8f);

            carveShader.Dispatch(carveKernel, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), Mathf.Max(1, groupsZ));

            // Regenerate MIP maps
            RegenerateMips();
        }

        /// <summary>
        /// Generate conservative MIP maps for hierarchical raymarching.
        /// </summary>
        private void RegenerateMips()
        {
            if (mipGenShader == null)
            {
                Debug.LogWarning("[ExcavationManager] MIP generation shader not assigned.");
                return;
            }

            // Generate each MIP level from the previous one
            int mipLevels = carveVolume.mipmapCount;

            for (int mip = 1; mip < mipLevels; mip++)
            {
                mipGenShader.SetTexture(mipGenKernel, "SourceMip", carveVolume, mip - 1);
                mipGenShader.SetTexture(mipGenKernel, "DestMip", carveVolume, mip);
                
                float parentVoxelSize = settings.voxelSize * Mathf.Pow(2, mip);
                mipGenShader.SetFloat("ParentVoxelSize", parentVoxelSize);

                int mipRes = Mathf.Max(1, resolution.x >> mip);
                int groups = Mathf.CeilToInt(mipRes / 8f);
                
                mipGenShader.Dispatch(mipGenKernel, groups, groups, groups);
            }
        }

        /// <summary>
        /// Convert world position to voxel coordinates.
        /// </summary>
        public Vector3Int WorldToVoxel(Vector3 worldPos)
        {
            Vector3 local = worldPos - settings.worldOrigin;
            return new Vector3Int(
                Mathf.FloorToInt(local.x / settings.voxelSize),
                Mathf.FloorToInt(local.y / settings.voxelSize),
                Mathf.FloorToInt(local.z / settings.voxelSize)
            );
        }

        /// <summary>
        /// Convert voxel coordinates to world position (voxel center).
        /// </summary>
        public Vector3 VoxelToWorld(Vector3Int voxel)
        {
            return settings.worldOrigin + new Vector3(
                (voxel.x + 0.5f) * settings.voxelSize,
                (voxel.y + 0.5f) * settings.voxelSize,
                (voxel.z + 0.5f) * settings.voxelSize
            );
        }

        /// <summary>
        /// Serialize the carve volume to compressed byte array.
        /// </summary>
        public byte[] SerializeVolume()
        {
            if (carveVolume == null) return null;

            // Read texture data from GPU
            RenderTexture.active = carveVolume;
            Texture2D temp2D = new Texture2D(resolution.x, resolution.y, TextureFormat.RFloat, false, true);
            float[] volumeData = new float[resolution.x * resolution.y * resolution.z];

            for (int z = 0; z < resolution.z; z++)
            {
                // Copy each slice to a temporary 2D texture
                Graphics.CopyTexture(carveVolume, 0, z, temp2D, 0, 0);
                temp2D.Apply();
                var sliceData = temp2D.GetRawTextureData<float>();
                System.Buffer.BlockCopy(sliceData.ToArray(), 0, volumeData, (z * resolution.x * resolution.y) * sizeof(float), resolution.x * resolution.y * sizeof(float));
            }
            RenderTexture.active = null;
            Object.Destroy(temp2D);

            // Convert float array to byte array
            byte[] rawData = new byte[volumeData.Length * sizeof(float)];
            System.Buffer.BlockCopy(volumeData, 0, rawData, 0, rawData.Length);

            // Compress using GZip
            using (var outputStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
                {
                    gzipStream.Write(rawData, 0, rawData.Length);
                }
                return outputStream.ToArray();
            }
        }

        /// <summary>
        /// Load volume from compressed byte array.
        /// </summary>
        public void LoadVolume(byte[] compressedData)
        {
            if (compressedData == null || carveVolume == null) return;

            // Decompress
            using (var inputStream = new MemoryStream(compressedData))
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                gzipStream.CopyTo(outputStream);
                byte[] rawData = outputStream.ToArray();

                // Apply to texture
                Texture3D temp = new Texture3D(resolution.x, resolution.y, resolution.z, TextureFormat.RFloat, false);
                // Set pixels manually since LoadRawTextureData is not available for Texture3D
                Color[] colors = new Color[resolution.x * resolution.y * resolution.z];
                System.Buffer.BlockCopy(rawData, 0, colors, 0, rawData.Length);
                temp.SetPixels(colors);
                temp.Apply();

                // Copy to RenderTexture
                Graphics.CopyTexture(temp, carveVolume);
                Destroy(temp);

                // Regenerate MIPs
                RegenerateMips();
            }
        }

        void OnDestroy()
        {
            if (carveVolume != null)
            {
                carveVolume.Release();
                Destroy(carveVolume);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (settings == null) return;

            // Draw volume bounds
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                settings.worldOrigin + settings.worldSize * 0.5f,
                settings.worldSize
            );
        }
    }
}
