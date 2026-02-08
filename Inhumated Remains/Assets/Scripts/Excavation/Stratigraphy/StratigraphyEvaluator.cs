using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using Excavation.Core;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Evaluates stratigraphic layers and manages baking into the volume texture.
    /// 
    /// Layer list conventions:
    ///   - All layers ordered youngest to oldest (top to bottom in inspector).
    ///   - Fills (Cut, Ellipsoid) go at the TOP.
    ///   - Bands (DepthBand, NoisyDepthBand) go at the BOTTOM.
    ///   - The user is responsible for maintaining this order.
    /// 
    /// Baking: All layers union solid geometry via min(). Order is irrelevant.
    /// 
    /// Material evaluation order:
    ///   1. Fills: youngest→oldest (forward, first match wins)
    ///   2. Bands: oldest→youngest (reverse, first match wins)
    /// </summary>
    public class StratigraphyEvaluator : MonoBehaviour
    {
        [Tooltip("Stratigraphic layers. All youngest to oldest. Fills at top, bands at bottom.")]
        [SerializeField] private List<MaterialLayer> layers = new List<MaterialLayer>();

        [Tooltip("Default substrate material (when no layers match)")]
        [SerializeField] private MaterialLayer defaultSubstrate;

        [Tooltip("Surface Y level — top of the band stack")]
        [SerializeField] private float surfaceY = 0f;

        [Header("Baking")]
        [Tooltip("Compute shader for baking layers into the volume")]
        [SerializeField] private ComputeShader bakeLayerShader;

        [Header("GPU Query")]
        [Tooltip("Compute shader for GPU SDF queries (optional)")]
        [SerializeField] private ComputeShader sdfQueryShader;

        // GPU Query state
        private ComputeBuffer queryPositionsBuffer;
        private ComputeBuffer queryResultsBuffer;
        private float[] pendingQueryResults;
        private bool queryInFlight = false;
        private Action<float[]> pendingCallback;

        // Cached separated lists for material evaluation
        private List<MaterialLayer> fills = new List<MaterialLayer>();
        private List<MaterialLayer> bands = new List<MaterialLayer>();

        public List<MaterialLayer> Layers => layers;
        public float SurfaceY => surfaceY;

        /// <summary>
        /// Initialize layer data: compute band Y positions and cache fill/band lists.
        /// Must be called before baking or material evaluation.
        /// </summary>
        public void InitializeLayers()
        {
            ComputeBandPositions();
            CacheFillsAndBands();
        }

        /// <summary>
        /// Compute Y positions for all depth bands by stacking from the surface downward.
        /// Bands are stored youngest-first, so index 0 = topmost band (just below surface).
        /// </summary>
        private void ComputeBandPositions()
        {
            float currentTopY = surfaceY;

            foreach (var layer in layers)
            {
                if (layer == null || layer.geometryData == null ||
                    layer.geometryData.Category != LayerCategory.Band)
                    continue;

                var geom = layer.geometryData;

                if (geom is DepthBandGeometry db)
                {
                    db.computedTopY = currentTopY;
                    db.computedBottomY = currentTopY - db.depth;
                    currentTopY = db.computedBottomY;
                }
                else if (geom is NoisyDepthBandGeometry ndb)
                {
                    ndb.computedBaseTopY = currentTopY;
                    ndb.computedBaseBottomY = currentTopY - ndb.depth;
                    currentTopY = ndb.computedBaseBottomY;
                }
            }
        }

        /// <summary>
        /// Cache fills and bands separately. Both stored youngest-first (matching list order).
        /// </summary>
        private void CacheFillsAndBands()
        {
            fills.Clear();
            bands.Clear();

            foreach (var layer in layers)
            {
                if (layer == null || layer.geometryData == null)
                    continue;

                if (layer.geometryData.Category == LayerCategory.Fill)
                    fills.Add(layer);
                else
                    bands.Add(layer);
            }
        }

        /// <summary>
        /// Get the material layer at a given world position.
        /// Fills: youngest→oldest (forward). Bands: oldest→youngest (reverse).
        /// </summary>
        public MaterialLayer GetMaterialAt(Vector3 worldPos)
        {
            // 1. Check fills: youngest→oldest (forward)
            foreach (var fill in fills)
            {
                if (fill.Contains(worldPos))
                    return fill;
            }

            // 2. Check bands: oldest→youngest (reverse, since bands are stored youngest-first)
            for (int i = bands.Count - 1; i >= 0; i--)
            {
                if (bands[i].Contains(worldPos))
                    return bands[i];
            }

            return defaultSubstrate;
        }

        /// <summary>
        /// Bake all layers into the volume texture.
        /// Processes bottom to top (oldest→youngest): iterates the list in reverse
        /// for fills (youngest first in list), and forward for bands (oldest first in list).
        /// 
        /// In practice, since all layers just union solid geometry, the order only matters
        /// for overlapping regions and the user manages this.
        /// </summary>
        public void BakeToVolume(RenderTexture volume, ExcavationVolumeSettings settings)
        {
            if (bakeLayerShader == null)
            {
                Debug.LogError("[StratigraphyEvaluator] BakeLayer compute shader not assigned!");
                return;
            }

            int kernel = bakeLayerShader.FindKernel("CSBakeLayer");

            // Set volume-wide parameters
            bakeLayerShader.SetVector("VolumeMin", settings.VolumeMin);
            bakeLayerShader.SetVector("VolumeSize", settings.worldSize);
            bakeLayerShader.SetFloat("VoxelSize", settings.voxelSize);

            Vector3Int resolution = settings.GetTextureResolution();

            int groupsX = Mathf.Max(1, Mathf.CeilToInt(resolution.x / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(resolution.y / 8f));
            int groupsZ = Mathf.Max(1, Mathf.CeilToInt(resolution.z / 8f));

            // Bake all layers into the volume. Since all layers union via min(),
            // order is irrelevant — we iterate in natural list order.
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null || layer.geometryData == null)
                    continue;

                var geom = layer.geometryData;

                bakeLayerShader.SetTexture(kernel, "Volume", volume, 0);
                bakeLayerShader.SetInt("GeometryType", (int)geom.GeometryType);
                bakeLayerShader.SetVector("LayerParams", geom.GetPackedParams());
                bakeLayerShader.SetVector("LayerParams2", geom.GetPackedParams2());

                bakeLayerShader.Dispatch(kernel, groupsX, groupsY, groupsZ);
            }

            Debug.Log($"[StratigraphyEvaluator] Baked {layers.Count} layers into volume");
        }

        /// <summary>
        /// Get GPU-ready layer data for the material evaluation shader.
        /// Returns layers packed in evaluation order: fills first, then bands.
        /// </summary>
        public void GetGPULayerData(
            out Color[] colors, out Vector4[] layerParams, out Vector4[] layerParams2,
            out float[] geometryTypes, out int fillCount, out int totalCount)
        {
            colors = new Color[8];
            layerParams = new Vector4[8];
            layerParams2 = new Vector4[8];
            geometryTypes = new float[8];

            int idx = 0;

            // Fills first (youngest→oldest, as in list)
            foreach (var fill in fills)
            {
                if (idx >= 8) break;
                PackLayer(fill, idx, colors, layerParams, layerParams2, geometryTypes);
                idx++;
            }

            fillCount = idx;

            // Then bands (youngest→oldest, as in list — shader iterates in reverse)
            foreach (var band in bands)
            {
                if (idx >= 8) break;
                PackLayer(band, idx, colors, layerParams, layerParams2, geometryTypes);
                idx++;
            }

            totalCount = idx;
        }

        private void PackLayer(MaterialLayer layer, int idx,
            Color[] colors, Vector4[] lp, Vector4[] lp2, float[] gt)
        {
            colors[idx] = layer.baseColour;
            var geom = layer.geometryData;
            if (geom != null)
            {
                gt[idx] = (int)geom.GeometryType;
                lp[idx] = geom.GetPackedParams();
                lp2[idx] = geom.GetPackedParams2();
            }
        }

        /// <summary>
        /// Set texture references for layers on the material.
        /// Packs in evaluation order (fills first, then bands).
        /// </summary>
        public void SetLayerTextures(Material mat)
        {
            var ordered = new List<MaterialLayer>();
            ordered.AddRange(fills);
            ordered.AddRange(bands);

            string[] albedoNames = {
                "_LayerAlbedo0", "_LayerAlbedo1", "_LayerAlbedo2", "_LayerAlbedo3",
                "_LayerAlbedo4", "_LayerAlbedo5", "_LayerAlbedo6", "_LayerAlbedo7"
            };
            string[] normalNames = {
                "_LayerNormal0", "_LayerNormal1", "_LayerNormal2", "_LayerNormal3",
                "_LayerNormal4", "_LayerNormal5", "_LayerNormal6", "_LayerNormal7"
            };

            for (int i = 0; i < Mathf.Min(ordered.Count, 8); i++)
            {
                if (ordered[i].albedoTexture != null)
                    mat.SetTexture(albedoNames[i], ordered[i].albedoTexture);
                if (ordered[i].normalMap != null)
                    mat.SetTexture(normalNames[i], ordered[i].normalMap);
            }
        }

        #region CPU Sphere Trace

        /// <summary>
        /// CPU-side sphere trace against the analytical layer SDFs.
        /// Evaluates the pre-carve terrain surface (does not account for carving).
        /// For carved-surface detection, use the GPU query system instead.
        /// </summary>
        /// <param name="origin">Ray origin in world space</param>
        /// <param name="direction">Ray direction (will be normalized)</param>
        /// <param name="maxDistance">Maximum trace distance</param>
        /// <param name="manager">ExcavationManager (unused for CPU trace, kept for API compat)</param>
        /// <returns>SurfaceHit result</returns>
        public SurfaceHit SphereTrace(Vector3 origin, Vector3 direction, float maxDistance, ExcavationManager manager)
        {
            Vector3 dir = direction.normalized;
            float t = 0f;
            const int maxSteps = 64;
            const float threshold = 0.001f;

            for (int i = 0; i < maxSteps; i++)
            {
                if (t > maxDistance)
                    break;

                Vector3 p = origin + dir * t;
                float d = EvaluateSceneCPU(p);

                if (d < threshold)
                {
                    // Compute normal via central differences
                    const float eps = 0.01f;
                    Vector3 normal = new Vector3(
                        EvaluateSceneCPU(p + Vector3.right * eps) - EvaluateSceneCPU(p - Vector3.right * eps),
                        EvaluateSceneCPU(p + Vector3.up * eps) - EvaluateSceneCPU(p - Vector3.up * eps),
                        EvaluateSceneCPU(p + Vector3.forward * eps) - EvaluateSceneCPU(p - Vector3.forward * eps)
                    ).normalized;

                    MaterialLayer mat = GetMaterialAt(p);
                    return SurfaceHit.Hit(p, normal, mat, t);
                }

                t += Mathf.Max(d, threshold);
            }

            return SurfaceHit.Miss();
        }

        /// <summary>
        /// Evaluate the scene SDF on CPU using analytical layer SDFs.
        /// Replicates the bake logic: start with air (+9999), union all layers.
        /// Does NOT include carving — this is the pre-carve terrain.
        /// </summary>
        private float EvaluateSceneCPU(Vector3 worldPos)
        {
            float sdf = 9999f;

            foreach (var layer in layers)
            {
                if (layer == null || layer.geometryData == null)
                    continue;

                float layerSDF = layer.geometryData.SDF(worldPos);
                sdf = Mathf.Min(sdf, layerSDF);
            }

            return sdf;
        }

        #endregion

        #region GPU Query System

        /// <summary>
        /// Submit a batch of positions to query SDF values on the GPU.
        /// </summary>
        public void QuerySDFBatchAsync(Vector3[] positions, ExcavationManager excavationManager, Action<float[]> callback)
        {
            if (sdfQueryShader == null || excavationManager == null || excavationManager.CarveVolume == null)
            {
                callback?.Invoke(null);
                return;
            }

            if (queryInFlight)
            {
                Debug.LogWarning("[StratigraphyEvaluator] GPU query already in flight");
                return;
            }

            int count = positions.Length;

            if (queryPositionsBuffer == null || queryPositionsBuffer.count != count)
            {
                queryPositionsBuffer?.Release();
                queryResultsBuffer?.Release();
                queryPositionsBuffer = new ComputeBuffer(count, sizeof(float) * 4);
                queryResultsBuffer = new ComputeBuffer(count, sizeof(float));
            }

            Vector4[] positionsV4 = new Vector4[count];
            for (int i = 0; i < count; i++)
                positionsV4[i] = new Vector4(positions[i].x, positions[i].y, positions[i].z, 0);
            queryPositionsBuffer.SetData(positionsV4);

            var settings = excavationManager.Settings;
            int kernel = sdfQueryShader.FindKernel("CSQuerySDF");

            sdfQueryShader.SetBuffer(kernel, "_QueryPositions", queryPositionsBuffer);
            sdfQueryShader.SetBuffer(kernel, "_QueryResults", queryResultsBuffer);
            sdfQueryShader.SetTexture(kernel, "_CarveVolume", excavationManager.CarveVolume);
            sdfQueryShader.SetVector("_VolumeMin", settings.VolumeMin);
            sdfQueryShader.SetVector("_VolumeSize", settings.worldSize);
            sdfQueryShader.SetInt("_QueryCount", count);

            int threadGroups = Mathf.CeilToInt(count / 64.0f);
            sdfQueryShader.Dispatch(kernel, threadGroups, 1, 1);

            queryInFlight = true;
            pendingCallback = callback;
            pendingQueryResults = new float[count];

            AsyncGPUReadback.Request(queryResultsBuffer, (request) =>
            {
                queryInFlight = false;

                if (request.hasError)
                {
                    Debug.LogError("[StratigraphyEvaluator] GPU query readback failed");
                    pendingCallback?.Invoke(null);
                    return;
                }

                var data = request.GetData<float>();
                data.CopyTo(pendingQueryResults);
                pendingCallback?.Invoke(pendingQueryResults);
                pendingCallback = null;
            });
        }

        public bool IsQueryInFlight => queryInFlight;

        private void OnDestroy()
        {
            queryPositionsBuffer?.Release();
            queryResultsBuffer?.Release();
        }

        #endregion
    }
}
