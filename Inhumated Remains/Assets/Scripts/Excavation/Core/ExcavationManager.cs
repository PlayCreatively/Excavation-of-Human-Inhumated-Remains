using UnityEngine;
using UnityEngine.Rendering;
using System;
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
        [SerializeField] private ExcavationVolumeSettings settings;

        [SerializeField] private ComputeShader carveShader;
        [SerializeField] private ComputeShader mipGenShader;

        // The 3D SDF texture
        private RenderTexture carveVolume;

        // Cached shader kernel IDs
        private int carveKernel;
        private int mipGenKernel;

        // Texture resolution
        private Vector3Int resolution;

        // Serialization state
        private bool serializationInProgress = false;
        private ComputeBuffer serializationBuffer;

        public RenderTexture CarveVolume => carveVolume;
        public ExcavationVolumeSettings Settings => settings;
        public bool IsSerializationInProgress => serializationInProgress;

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

                // Calculate separate dispatch counts for non-cubic volumes
                int mipResX = Mathf.Max(1, resolution.x >> mip);
                int mipResY = Mathf.Max(1, resolution.y >> mip);
                int mipResZ = Mathf.Max(1, resolution.z >> mip);

                int groupsX = Mathf.Max(1, Mathf.CeilToInt(mipResX / 8f));
                int groupsY = Mathf.Max(1, Mathf.CeilToInt(mipResY / 8f));
                int groupsZ = Mathf.Max(1, Mathf.CeilToInt(mipResZ / 8f));

                mipGenShader.Dispatch(mipGenKernel, groupsX, groupsY, groupsZ);
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
        /// Serialize the carve volume to compressed byte array asynchronously.
        /// Uses AsyncGPUReadback to avoid stalling the GPU pipeline.
        /// </summary>
        /// <param name="callback">Called with compressed data when complete, or null on failure</param>
        public void SerializeVolumeAsync(Action<byte[]> callback)
        {
            if (carveVolume == null)
            {
                callback?.Invoke(null);
                return;
            }

            if (serializationInProgress)
            {
                Debug.LogWarning("[ExcavationManager] Serialization already in progress");
                callback?.Invoke(null);
                return;
            }

            serializationInProgress = true;

            // Request async readback of MIP 0
            // Note: AsyncGPUReadback.Request for 3D textures reads the full volume
            // Use the overload that accepts (Texture, int, Action<AsyncGPUReadbackRequest>)
            AsyncGPUReadback.Request(carveVolume, 0, (request) =>
            {
                serializationInProgress = false;

                if (request.hasError)
                {
                    Debug.LogError("[ExcavationManager] GPU readback failed during serialization");
                    callback?.Invoke(null);
                    return;
                }

                try
                {
                    // Get the raw float data
                    var data = request.GetData<float>();
                    byte[] rawData = new byte[data.Length * sizeof(float)];

                    // Copy to byte array (safe managed copy)
                    {
                        float[] floatArray = data.ToArray();
                        Buffer.BlockCopy(floatArray, 0, rawData, 0, rawData.Length);
                    }

                    // Compress using GZip
                    using var outputStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
                    {
                        gzipStream.Write(rawData, 0, rawData.Length);
                    }
                    callback?.Invoke(outputStream.ToArray());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ExcavationManager] Serialization failed: {e.Message}");
                    callback?.Invoke(null);
                }
            });
        }

        /// <summary>
        /// Synchronous serialize - blocks until complete.
        /// Prefer SerializeVolumeAsync for better performance.
        /// </summary>
        [Obsolete("Use SerializeVolumeAsync for non-blocking serialization")]
        public byte[] SerializeVolume()
        {
            if (carveVolume == null) return null;

            // Force sync readback - not recommended for production
            byte[] result = null;
            bool done = false;

            SerializeVolumeAsync((data) =>
            {
                result = data;
                done = true;
            });

            // Spin wait - bad for performance but maintains API compatibility
            int timeout = 10000; // 10 second timeout
            while (!done && timeout > 0)
            {
                System.Threading.Thread.Sleep(1);
                timeout--;
            }

            return result;
        }

        /// <summary>
        /// Load volume from compressed byte array.
        /// Creates a Texture3D on CPU and copies to RenderTexture.
        /// </summary>
        public void LoadVolume(byte[] compressedData)
        {
            if (compressedData == null || carveVolume == null) return;

            try
            {
                // Decompress
                using (var inputStream = new MemoryStream(compressedData))
                using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                using (var outputStream = new MemoryStream())
                {
                    gzipStream.CopyTo(outputStream);
                    byte[] rawData = outputStream.ToArray();

                    // Validate data size
                    int expectedSize = resolution.x * resolution.y * resolution.z * sizeof(float);
                    if (rawData.Length != expectedSize)
                    {
                        Debug.LogError($"[ExcavationManager] Volume data size mismatch. Expected {expectedSize}, got {rawData.Length}");
                        return;
                    }

                    // Convert bytes to float array
                    float[] floatData = new float[resolution.x * resolution.y * resolution.z];
                    System.Buffer.BlockCopy(rawData, 0, floatData, 0, rawData.Length);

                    // Create Texture3D with the data
                    Texture3D temp = new Texture3D(resolution.x, resolution.y, resolution.z, TextureFormat.RFloat, false);
                    temp.SetPixelData(floatData, 0);
                    temp.Apply(false, false);

                    // Copy to RenderTexture (this works because both use RFloat format)
                    Graphics.CopyTexture(temp, carveVolume);
                    Destroy(temp);

                    // Regenerate MIPs
                    RegenerateMips();

                    Debug.Log("[ExcavationManager] Volume loaded successfully");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcavationManager] Failed to load volume: {e.Message}");
            }
        }

        #region Public Save/Load API

        /// <summary>
        /// Save the excavation volume to a file asynchronously.
        /// </summary>
        /// <param name="filePath">Full path to save file</param>
        /// <param name="callback">Called with success status when complete</param>
        public void SaveExcavation(string filePath, Action<bool> callback = null)
        {
            SerializeVolumeAsync((data) =>
            {
                if (data == null)
                {
                    Debug.LogError("[ExcavationManager] Failed to serialize volume for save");
                    callback?.Invoke(false);
                    return;
                }

                try
                {
                    File.WriteAllBytes(filePath, data);
                    Debug.Log($"[ExcavationManager] Excavation saved to: {filePath}");
                    callback?.Invoke(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ExcavationManager] Failed to save excavation: {e.Message}");
                    callback?.Invoke(false);
                }
            });
        }

        /// <summary>
        /// Load excavation volume from a file.
        /// </summary>
        /// <param name="filePath">Full path to load file</param>
        /// <returns>True if load was successful</returns>
        public bool LoadExcavation(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"[ExcavationManager] File not found: {filePath}");
                    return false;
                }

                byte[] data = File.ReadAllBytes(filePath);
                LoadVolume(data);
                Debug.Log($"[ExcavationManager] Excavation loaded from: {filePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcavationManager] Failed to load excavation: {e.Message}");
                return false;
            }
        }

        #endregion

        void OnDestroy()
        {
            if (carveVolume != null)
            {
                carveVolume.Release();
                Destroy(carveVolume);
            }

            serializationBuffer?.Release();
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
