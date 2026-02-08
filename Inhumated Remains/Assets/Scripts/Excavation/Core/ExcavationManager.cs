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

        // Serialization state
        private bool serializationInProgress = false;

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
            BakeLayers();
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

        #region Serialization (Save/Load full texture)

        // File format: [12-byte header: int32 resX, resY, resZ] + [gzip compressed float data]
        private const int HEADER_SIZE = 12;

        public void SerializeVolumeAsync(Action<byte[]> callback)
        {
            if (carveVolume == null) { callback?.Invoke(null); return; }
            if (serializationInProgress) { callback?.Invoke(null); return; }

            serializationInProgress = true;

            AsyncGPUReadback.Request(carveVolume, 0, (request) =>
            {
                serializationInProgress = false;

                if (request.hasError)
                {
                    Debug.LogError("[ExcavationManager] GPU readback failed");
                    callback?.Invoke(null);
                    return;
                }

                try
                {
                    var data = request.GetData<float>();
                    byte[] rawData = new byte[data.Length * sizeof(float)];
                    float[] floatArray = data.ToArray();
                    System.Buffer.BlockCopy(floatArray, 0, rawData, 0, rawData.Length);

                    using var outputStream = new MemoryStream();

                    // Write resolution header (uncompressed)
                    var bw = new BinaryWriter(outputStream);
                    bw.Write(resolution.x);
                    bw.Write(resolution.y);
                    bw.Write(resolution.z);

                    // Write gzip-compressed voxel data
                    using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true))
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

        public void LoadVolume(byte[] fileData)
        {
            if (fileData == null || carveVolume == null)
            {
                Debug.LogError("[ExcavationManager] Cannot load: " +
                    (fileData == null ? "file data is null" : "volume texture not initialized (are you in Play mode?)"));
                return;
            }

            try
            {
                using var inputStream = new MemoryStream(fileData);

                // Detect format: gzip magic bytes (0x1F 0x8B) = legacy, otherwise new header
                bool isLegacy = fileData.Length >= 2 && fileData[0] == 0x1F && fileData[1] == 0x8B;

                int loadResX = resolution.x;
                int loadResY = resolution.y;
                int loadResZ = resolution.z;

                if (!isLegacy)
                {
                    var br = new BinaryReader(inputStream);
                    int savedResX = br.ReadInt32();
                    int savedResY = br.ReadInt32();
                    int savedResZ = br.ReadInt32();

                    if (savedResX != resolution.x || savedResY != resolution.y || savedResZ != resolution.z)
                    {
                        Debug.LogError($"[ExcavationManager] Resolution mismatch: " +
                            $"file has {savedResX}x{savedResY}x{savedResZ}, " +
                            $"current volume is {resolution.x}x{resolution.y}x{resolution.z}. " +
                            $"Adjust volume settings to match or re-save.");
                        return;
                    }

                    loadResX = savedResX;
                    loadResY = savedResY;
                    loadResZ = savedResZ;
                }
                else
                {
                    Debug.LogWarning("[ExcavationManager] Detected legacy save format (no header). " +
                        "Will attempt to load assuming current resolution. Re-save to upgrade.");
                }

                // Decompress voxel data
                using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                gzipStream.CopyTo(decompressed);
                byte[] rawData = decompressed.ToArray();

                int expectedSize = loadResX * loadResY * loadResZ * sizeof(float);
                if (rawData.Length != expectedSize)
                {
                    // Try to infer resolution from data size for legacy files
                    int voxelCount = rawData.Length / sizeof(float);
                    Debug.LogError($"[ExcavationManager] Data size mismatch: " +
                        $"expected {expectedSize} bytes ({loadResX}x{loadResY}x{loadResZ} = {loadResX * loadResY * loadResZ} voxels), " +
                        $"got {rawData.Length} bytes ({voxelCount} voxels). " +
                        (isLegacy ? "Legacy file was likely saved with different volume settings. " : "") +
                        "Delete old save and re-save with current settings.");
                    return;
                }

                float[] floatData = new float[loadResX * loadResY * loadResZ];
                System.Buffer.BlockCopy(rawData, 0, floatData, 0, rawData.Length);

                Texture3D temp = new Texture3D(loadResX, loadResY, loadResZ, TextureFormat.RFloat, false);
                temp.SetPixelData(floatData, 0);
                temp.Apply(false, false);

                Graphics.CopyTexture(temp, carveVolume);
                Destroy(temp);

                RegenerateMips();

                Debug.Log($"[ExcavationManager] Volume loaded ({loadResX}x{loadResY}x{loadResZ}, {fileData.Length} bytes on disk)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcavationManager] Failed to load volume: {e.Message}\n{e.StackTrace}");
            }
        }

        public void SaveExcavation(string filePath, Action<bool> callback = null)
        {
            SerializeVolumeAsync((data) =>
            {
                if (data == null) { callback?.Invoke(false); return; }
                try
                {
                    File.WriteAllBytes(filePath, data);
                    Debug.Log($"[ExcavationManager] Saved to: {filePath}");
                    callback?.Invoke(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ExcavationManager] Save failed: {e.Message}");
                    callback?.Invoke(false);
                }
            });
        }

        public bool LoadExcavation(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                byte[] data = File.ReadAllBytes(filePath);
                LoadVolume(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcavationManager] Load failed: {e.Message}");
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
