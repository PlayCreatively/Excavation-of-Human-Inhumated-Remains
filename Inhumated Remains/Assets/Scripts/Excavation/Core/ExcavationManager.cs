using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.IO;
using System.IO.Compression;

namespace Excavation.Core
{
    /// <summary>
    /// Manages the 3D volume texture and handles digging operations.
    /// The volume stores the scene SDF directly: negative = solid, positive = air.
    /// At initialization, layers are baked into the volume, then carving modifies it at runtime.
    /// </summary>
    public class ExcavationManager : MonoBehaviour
    {
        [SerializeField] private ExcavationVolumeSettings settings;

        [Header("Stratigraphy")]
        [SerializeField] private Stratigraphy.StratigraphyEvaluator stratigraphy;

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader carveShader;
        [SerializeField] private ComputeShader mipGenShader;

        // The 3D SDF texture (scene SDF: negative = solid, positive = air)
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
            BakeLayers();

            // Clear dirty flag from any OnValidate calls during scene load
            if (stratigraphy != null)
                stratigraphy.ClearDirty();
        }

        void Update()
        {
            // Auto-rebake when stratigraphy parameters change at runtime
            if (stratigraphy != null && stratigraphy.IsDirty && carveVolume != null)
            {
                Debug.Log("[ExcavationManager] Stratigraphy changed, re-baking...");
                RebakeLayers();
                stratigraphy.ClearDirty();
            }
        }

        /// <summary>
        /// Create and initialize the 3D volume texture.
        /// Initialized to large positive value (everything is air).
        /// </summary>
        private void InitializeVolume()
        {
            resolution = settings.GetTextureResolution();

            Debug.Log($"[ExcavationManager] Initializing volume: {resolution.x}x{resolution.y}x{resolution.z} " +
                      $"({resolution.x * resolution.y * resolution.z:N0} voxels)");

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

            // Initialize all voxels to large positive value (all air, no solid)
            ClearVolume();

            if (carveShader != null)
                carveKernel = carveShader.FindKernel("CSCarve");

            if (mipGenShader != null)
                mipGenKernel = mipGenShader.FindKernel("CSGenerateMip");
        }

        /// <summary>
        /// Clear the volume (reset to all-air state).
        /// </summary>
        public void ClearVolume()
        {
            if (carveVolume == null) return;

            ComputeShader initShader = Resources.Load<ComputeShader>("Shaders/InitializeVolume");
            if (initShader != null)
            {
                int kernel = initShader.FindKernel("CSInitialize");
                initShader.SetTexture(kernel, "Result", carveVolume);
                initShader.SetFloat("InitValue", 9999.0f); // All air
                
                int groupsX = Mathf.Max(1, Mathf.CeilToInt(resolution.x / 8f));
                int groupsY = Mathf.Max(1, Mathf.CeilToInt(resolution.y / 8f));
                int groupsZ = Mathf.Max(1, Mathf.CeilToInt(resolution.z / 8f));
                initShader.Dispatch(kernel, groupsX, groupsY, groupsZ);
            }
            else
            {
                Debug.LogWarning("[ExcavationManager] Initialize compute shader not found.");
            }
        }

        /// <summary>
        /// Bake all stratigraphic layers into the volume texture, then generate mips.
        /// </summary>
        private void BakeLayers()
        {
            if (stratigraphy == null)
            {
                Debug.LogWarning("[ExcavationManager] No StratigraphyEvaluator assigned, skipping bake.");
                return;
            }

            stratigraphy.InitializeLayers();
            stratigraphy.BakeToVolume(carveVolume, settings);
            RegenerateMips();

            Debug.Log("[ExcavationManager] Layer bake complete.");
        }

        /// <summary>
        /// Re-bake layers from scratch (clears volume first). Destroys any carving.
        /// </summary>
        public void RebakeLayers()
        {
            ClearVolume();
            BakeLayers();
        }

        /// <summary>
        /// Apply a brush stroke to carve the volume.
        /// Carving = CSG subtraction of brush from solid terrain.
        /// Scene SDF convention: max(existing, -brushSDF) where brushSDF is negative inside brush.
        /// </summary>
        public void ApplyBrushStroke(BrushStroke stroke)
        {
            if (carveVolume == null || carveShader == null)
            {
                Debug.LogWarning("[ExcavationManager] Cannot apply brush stroke.");
                return;
            }

            carveShader.SetTexture(carveKernel, "Volume", carveVolume, 0);
            carveShader.SetVector("BrushPosition", stroke.worldPosition);
            carveShader.SetFloat("BrushRadius", stroke.radius);
            carveShader.SetFloat("DigSpeed", stroke.intensity);
            carveShader.SetFloat("DeltaTime", stroke.deltaTime);
            carveShader.SetVector("VolumeMin", settings.VolumeMin);
            carveShader.SetFloat("VoxelSize", settings.voxelSize);
            carveShader.SetVector("VolumeSize", settings.worldSize);

            Vector3 brushMin = stroke.worldPosition - Vector3.one * (stroke.radius + settings.voxelSize);
            Vector3 brushMax = stroke.worldPosition + Vector3.one * (stroke.radius + settings.voxelSize);

            Vector3Int minVoxel = WorldToVoxel(brushMin);
            Vector3Int maxVoxel = WorldToVoxel(brushMax);

            minVoxel = Vector3Int.Max(minVoxel, Vector3Int.zero);
            maxVoxel = Vector3Int.Min(maxVoxel, resolution - Vector3Int.one);

            Vector3Int regionSize = maxVoxel - minVoxel;

            carveShader.SetInts("MinVoxel", minVoxel.x, minVoxel.y, minVoxel.z);

            int groupsX = Mathf.Max(1, Mathf.CeilToInt(regionSize.x / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(regionSize.y / 8f));
            int groupsZ = Mathf.Max(1, Mathf.CeilToInt(regionSize.z / 8f));

            carveShader.Dispatch(carveKernel, groupsX, groupsY, groupsZ);

            RegenerateMips();
        }

        /// <summary>
        /// Generate conservative MIP maps for hierarchical raymarching.
        /// </summary>
        private void RegenerateMips()
        {
            if (mipGenShader == null) return;

            int mipLevels = carveVolume.mipmapCount;

            for (int mip = 1; mip < mipLevels; mip++)
            {
                mipGenShader.SetTexture(mipGenKernel, "SourceMip", carveVolume, mip - 1);
                mipGenShader.SetTexture(mipGenKernel, "DestMip", carveVolume, mip);

                float parentVoxelSize = settings.voxelSize * Mathf.Pow(2, mip);
                mipGenShader.SetFloat("ParentVoxelSize", parentVoxelSize);

                int mipResX = Mathf.Max(1, resolution.x >> mip);
                int mipResY = Mathf.Max(1, resolution.y >> mip);
                int mipResZ = Mathf.Max(1, resolution.z >> mip);

                int groupsX = Mathf.Max(1, Mathf.CeilToInt(mipResX / 8f));
                int groupsY = Mathf.Max(1, Mathf.CeilToInt(mipResY / 8f));
                int groupsZ = Mathf.Max(1, Mathf.CeilToInt(mipResZ / 8f));

                mipGenShader.Dispatch(mipGenKernel, groupsX, groupsY, groupsZ);
            }
        }

        public Vector3Int WorldToVoxel(Vector3 worldPos)
        {
            Vector3 local = worldPos - settings.VolumeMin;
            return new Vector3Int(
                Mathf.FloorToInt(local.x / settings.voxelSize),
                Mathf.FloorToInt(local.y / settings.voxelSize),
                Mathf.FloorToInt(local.z / settings.voxelSize)
            );
        }

        public Vector3 VoxelToWorld(Vector3Int voxel)
        {
            return settings.VolumeMin + new Vector3(
                (voxel.x + 0.5f) * settings.voxelSize,
                (voxel.y + 0.5f) * settings.voxelSize,
                (voxel.z + 0.5f) * settings.voxelSize
            );
        }

        #region Serialization

        // File format: [int32 resX][int32 resY][int32 resZ][gzip compressed float[] data]
        // Voxel layout: x + y * resX + z * resX * resY (matches compute shader indexing)

        /// <summary>
        /// Read the volume from GPU into a float array via compute shader + buffer.
        /// AsyncGPUReadback on 3D RenderTextures only returns one slice in many
        /// Unity versions, so we use a compute shader to copy texture → buffer first.
        /// </summary>
        private float[] ReadVolumeFromGPU()
        {
            var downloadShader = Resources.Load<ComputeShader>("Shaders/DownloadVolume");
            if (downloadShader == null)
            {
                Debug.LogError("[ExcavationManager] DownloadVolume compute shader not found.");
                return null;
            }

            int voxelCount = resolution.x * resolution.y * resolution.z;
            var buffer = new ComputeBuffer(voxelCount, sizeof(float));

            int kernel = downloadShader.FindKernel("CSDownload");
            downloadShader.SetTexture(kernel, "Volume", carveVolume);
            downloadShader.SetBuffer(kernel, "DestData", buffer);
            downloadShader.SetInts("Resolution", resolution.x, resolution.y, resolution.z);

            int groupsX = Mathf.Max(1, Mathf.CeilToInt(resolution.x / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(resolution.y / 8f));
            int groupsZ = Mathf.Max(1, Mathf.CeilToInt(resolution.z / 8f));
            downloadShader.Dispatch(kernel, groupsX, groupsY, groupsZ);

            float[] result = new float[voxelCount];
            buffer.GetData(result);
            buffer.Release();

            return result;
        }

        /// <summary>
        /// Write a float array into the GPU volume via compute buffer + compute shader.
        /// This is reliable for 3D textures unlike Graphics.CopyTexture.
        /// </summary>
        private void WriteVolumeToGPU(float[] floatData)
        {
            var uploadShader = Resources.Load<ComputeShader>("Shaders/UploadVolume");
            if (uploadShader == null)
            {
                Debug.LogError("[ExcavationManager] UploadVolume compute shader not found.");
                return;
            }

            int voxelCount = resolution.x * resolution.y * resolution.z;
            var buffer = new ComputeBuffer(voxelCount, sizeof(float));
            buffer.SetData(floatData);

            int kernel = uploadShader.FindKernel("CSUpload");
            uploadShader.SetTexture(kernel, "Volume", carveVolume);
            uploadShader.SetBuffer(kernel, "SourceData", buffer);
            uploadShader.SetInts("Resolution", resolution.x, resolution.y, resolution.z);

            int groupsX = Mathf.Max(1, Mathf.CeilToInt(resolution.x / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(resolution.y / 8f));
            int groupsZ = Mathf.Max(1, Mathf.CeilToInt(resolution.z / 8f));
            uploadShader.Dispatch(kernel, groupsX, groupsY, groupsZ);

            buffer.Release();
        }

        /// <summary>
        /// Save the volume to a file.
        /// </summary>
        public void SaveExcavation(string filePath)
        {
            if (carveVolume == null)
            {
                Debug.LogError("[ExcavationManager] Cannot save: volume not initialized.");
                return;
            }

            try
            {
                float[] floatData = ReadVolumeFromGPU();
                if (floatData == null) return;

                byte[] rawData = new byte[floatData.Length * sizeof(float)];
                System.Buffer.BlockCopy(floatData, 0, rawData, 0, rawData.Length);

                using var stream = new MemoryStream();
                var bw = new BinaryWriter(stream);
                bw.Write(resolution.x);
                bw.Write(resolution.y);
                bw.Write(resolution.z);

                using (var gz = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true))
                    gz.Write(rawData, 0, rawData.Length);

                File.WriteAllBytes(filePath, stream.ToArray());
                Debug.Log($"[ExcavationManager] Saved {resolution.x}x{resolution.y}x{resolution.z} " +
                    $"to: {filePath} ({stream.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcavationManager] Save failed: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Load the volume from a file.
        /// </summary>
        public bool LoadExcavation(string filePath)
        {
            if (carveVolume == null)
            {
                Debug.LogError("[ExcavationManager] Cannot load: volume not initialized.");
                return false;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[ExcavationManager] File not found: {filePath}");
                return false;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                using var stream = new MemoryStream(fileData);
                var br = new BinaryReader(stream);

                int savedX = br.ReadInt32();
                int savedY = br.ReadInt32();
                int savedZ = br.ReadInt32();

                if (savedX != resolution.x || savedY != resolution.y || savedZ != resolution.z)
                {
                    Debug.LogError($"[ExcavationManager] Resolution mismatch: " +
                        $"file={savedX}x{savedY}x{savedZ}, volume={resolution.x}x{resolution.y}x{resolution.z}. " +
                        $"Delete old save and re-save.");
                    return false;
                }

                using var gz = new GZipStream(stream, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                gz.CopyTo(decompressed);
                byte[] rawData = decompressed.ToArray();

                int expectedBytes = savedX * savedY * savedZ * sizeof(float);
                if (rawData.Length != expectedBytes)
                {
                    Debug.LogError($"[ExcavationManager] Data size mismatch: " +
                        $"expected {expectedBytes} bytes, got {rawData.Length}.");
                    return false;
                }

                float[] floatData = new float[savedX * savedY * savedZ];
                System.Buffer.BlockCopy(rawData, 0, floatData, 0, rawData.Length);

                WriteVolumeToGPU(floatData);
                RegenerateMips();

                Debug.Log($"[ExcavationManager] Loaded {savedX}x{savedY}x{savedZ} from: {filePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcavationManager] Load failed: {e.Message}\n{e.StackTrace}");
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
        }

        void OnDrawGizmosSelected()
        {
            if (settings == null) return;

            // Volume bounds
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(settings.VolumeCenter, settings.worldSize);

            // Surface origin (top-center of volume)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(settings.worldOrigin, 0.05f);

            // Top face outline
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Vector3 o = settings.worldOrigin;
            Vector3 hs = settings.worldSize * 0.5f;
            Gizmos.DrawLine(o + new Vector3(-hs.x, 0, -hs.z), o + new Vector3( hs.x, 0, -hs.z));
            Gizmos.DrawLine(o + new Vector3( hs.x, 0, -hs.z), o + new Vector3( hs.x, 0,  hs.z));
            Gizmos.DrawLine(o + new Vector3( hs.x, 0,  hs.z), o + new Vector3(-hs.x, 0,  hs.z));
            Gizmos.DrawLine(o + new Vector3(-hs.x, 0,  hs.z), o + new Vector3(-hs.x, 0, -hs.z));
        }
    }
}
