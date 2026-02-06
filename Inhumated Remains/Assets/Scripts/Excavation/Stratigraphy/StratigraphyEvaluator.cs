using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using Excavation.Core;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Evaluates stratigraphic layers to determine material at any world position.
    /// Implements the Harris Matrix ordering principle.
    /// </summary>
    public class StratigraphyEvaluator : MonoBehaviour
    {
        [Header("Layer Configuration")]
        [Tooltip("Stratigraphic layers ordered from youngest (top) to oldest (bottom)")]
        [SerializeField] private List<MaterialLayer> layers = new List<MaterialLayer>();

        [Tooltip("Default substrate material (when no layers match)")]
        [SerializeField] private MaterialLayer defaultSubstrate;

        [Header("Base Terrain")]
        [Tooltip("Base terrain Y level (flat ground)")]
        [SerializeField] private float baseTerrainY = 0f;

        /// <summary>
        /// Public accessor for the base terrain Y level.
        /// </summary>
        public float BaseTerrainY => baseTerrainY;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool debugSphereTrace = false;
        public Vector3 debugPosition = Vector3.zero;

        [Header("GPU Query")]
        [Tooltip("Compute shader for GPU SDF queries (optional, improves performance)")]
        [SerializeField] private ComputeShader sdfQueryShader;

        // GPU Query state
        private ComputeBuffer queryPositionsBuffer;
        private ComputeBuffer queryResultsBuffer;
        private float[] pendingQueryResults;
        private bool queryInFlight = false;
        private Action<float[]> pendingCallback;

        public List<MaterialLayer> Layers => layers;

        /// <summary>
        /// Get the material layer at a given world position.
        /// Evaluates layers from youngest (top) to oldest (bottom).
        /// </summary>
        public MaterialLayer GetMaterialAt(Vector3 worldPos)
        {
            // Iterate from youngest to oldest (Harris Matrix principle)
            foreach (var layer in layers)
            {
                if (layer != null && layer.Contains(worldPos))
                {
                    return layer;
                }
            }

            // Fall back to default substrate
            return defaultSubstrate;
        }

        /// <summary>
        /// Evaluate the base terrain SDF (before carving).
        /// Combines all layer geometries based on their operations.
        /// </summary>
        public float GetBaseTerrainSDF(Vector3 worldPos)
        {
            // Start with flat ground
            float baseSDF = worldPos.y - baseTerrainY;

            // Apply each layer's geometry operation
            foreach (var layer in layers)
            {
                if (layer == null || layer.geometryData == null)
                    continue;

                float layerSDF = layer.SDF(worldPos);

                switch (layer.geometryData.operation)
                {
                    case LayerOperation.Union:
                        // Add material (burial mounds)
                        baseSDF = Core.SDFUtility.Union(baseSDF, layerSDF);
                        break;

                    case LayerOperation.Subtract:
                        // Remove material (modern trenches cutting through everything)
                        baseSDF = Core.SDFUtility.Subtract(baseSDF, layerSDF);
                        break;

                    case LayerOperation.Inside:
                        // Default: layer only exists within existing terrain
                        // No modification to base geometry
                        break;
                }
            }

            return baseSDF;
        }

        /// <summary>
        /// Evaluate the complete scene SDF (base terrain + carve mask).
        /// This is the main function used for raymarching and collision.
        /// </summary>
        public float GetSceneSDF(Vector3 worldPos, ExcavationManager excavationManager)
        {
            float baseSDF = GetBaseTerrainSDF(worldPos);

            // Sample carve volume if available
            if (excavationManager != null && excavationManager.CarveVolume != null)
            {
                float carveSDF = SampleCarveVolume(worldPos, excavationManager);

                // Boolean subtraction: max(base, -carve)
                // If carve is negative (inside hole), -carve becomes positive, making the final SDF positive (air)
                return Mathf.Max(baseSDF, -carveSDF);
            }

            return baseSDF;
        }

        /// <summary>
        /// Sample the carve volume texture at a world position.
        /// </summary>
        private float SampleCarveVolume(Vector3 worldPos, ExcavationManager excavationManager)
        {
            var settings = excavationManager.Settings;

            // Convert world position to texture UVW coordinates
            Vector3 localPos = worldPos - settings.worldOrigin;
            Vector3 uvw = new Vector3(
                localPos.x / settings.worldSize.x,
                localPos.y / settings.worldSize.y,
                localPos.z / settings.worldSize.z
            );

            // Check bounds
            if (uvw.x < 0 || uvw.x > 1 || uvw.y < 0 || uvw.y > 1 || uvw.z < 0 || uvw.z > 1)
            {
                return 9999f; // Outside volume = far from any excavation
            }

            // Note: Reading from RenderTexture in C# requires creating a temporary Texture3D
            // For performance, this should primarily be done in shaders
            // This is mainly for CPU-side collision detection

            // For now, return a placeholder
            // In production, you'd read the texture or use a compute shader for queries
            return 9999f;
        }

        /// <summary>
        /// Perform sphere tracing to find the surface from a ray origin in a direction.
        /// </summary>
        public Core.SurfaceHit SphereTrace(Vector3 origin, Vector3 direction, float maxDistance, ExcavationManager excavationManager)
        {
            float t = 0f;
            const int maxSteps = 64;
            const float threshold = 0.001f;

            for (int i = 0; i < maxSteps; i++)
            {
                Vector3 p = origin + direction * t;
                float d = GetSceneSDF(p, excavationManager);

                if (d < threshold)
                {
                    // Hit! Calculate normal and material
                    Vector3 normal = Core.SDFUtility.ComputeGradient(
                        pos => GetSceneSDF(pos, excavationManager),
                        p
                    );

                    MaterialLayer material = GetMaterialAt(p);

                    return Core.SurfaceHit.Hit(p, normal, material, t);
                }

                if (t > maxDistance)
                {
                    break; // Too far
                }

                t += d;
            }

            return Core.SurfaceHit.Miss();
        }

        #region GPU Query System

        /// <summary>
        /// Submit a batch of positions to query SDF values on the GPU.
        /// Results will be returned asynchronously via the callback.
        /// This is much faster than CPU sampling for large numbers of queries.
        /// </summary>
        /// <param name="positions">World positions to query</param>
        /// <param name="excavationManager">Reference to the excavation manager</param>
        /// <param name="callback">Callback with SDF values for each position</param>
        public void QuerySDFBatchAsync(Vector3[] positions, ExcavationManager excavationManager, Action<float[]> callback)
        {
            if (sdfQueryShader == null || excavationManager == null || excavationManager.CarveVolume == null)
            {
                // Fallback to CPU queries
                float[] results = new float[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    results[i] = GetSceneSDF(positions[i], excavationManager);
                }
                callback?.Invoke(results);
                return;
            }

            if (queryInFlight)
            {
                Debug.LogWarning("[StratigraphyEvaluator] GPU query already in flight, discarding request");
                return;
            }

            int count = positions.Length;

            // Create or resize buffers
            if (queryPositionsBuffer == null || queryPositionsBuffer.count != count)
            {
                queryPositionsBuffer?.Release();
                queryResultsBuffer?.Release();
                queryPositionsBuffer = new ComputeBuffer(count, sizeof(float) * 4);
                queryResultsBuffer = new ComputeBuffer(count, sizeof(float));
            }

            // Upload positions (convert to Vector4 for GPU)
            Vector4[] positionsV4 = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                positionsV4[i] = new Vector4(positions[i].x, positions[i].y, positions[i].z, 0);
            }
            queryPositionsBuffer.SetData(positionsV4);

            // Set up compute shader
            var settings = excavationManager.Settings;
            int kernel = sdfQueryShader.FindKernel("CSQuerySDF");

            sdfQueryShader.SetBuffer(kernel, "_QueryPositions", queryPositionsBuffer);
            sdfQueryShader.SetBuffer(kernel, "_QueryResults", queryResultsBuffer);
            sdfQueryShader.SetTexture(kernel, "_CarveVolume", excavationManager.CarveVolume);
            sdfQueryShader.SetVector("_VolumeOrigin", settings.worldOrigin);
            sdfQueryShader.SetVector("_VolumeSize", settings.worldSize);
            sdfQueryShader.SetFloat("_BaseTerrainY", baseTerrainY);
            sdfQueryShader.SetInt("_QueryCount", count);

            // Dispatch
            int threadGroups = Mathf.CeilToInt(count / 64.0f);
            sdfQueryShader.Dispatch(kernel, threadGroups, 1, 1);

            // Request async readback
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

                // Copy results
                var data = request.GetData<float>();
                data.CopyTo(pendingQueryResults);

                pendingCallback?.Invoke(pendingQueryResults);
                pendingCallback = null;
            });
        }

        /// <summary>
        /// Check if a GPU query is currently in flight.
        /// </summary>
        public bool IsQueryInFlight => queryInFlight;

        private void OnDestroy()
        {
            queryPositionsBuffer?.Release();
            queryResultsBuffer?.Release();
        }

        #endregion

        #region Player Grounding

        /// <summary>
        /// Ground a player/character to the excavated terrain surface.
        /// Traces downward from the given position to find the surface.
        /// TODO: Implement full grounding logic with slope handling and collision response.
        /// </summary>
        /// <param name="position">Starting position (typically player feet)</param>
        /// <param name="excavationManager">Reference to the excavation manager</param>
        /// <param name="maxTraceDistance">Maximum distance to trace downward</param>
        /// <returns>Grounded position on the surface, or original position if no surface found</returns>
        public Vector3 GroundPlayer(Vector3 position, ExcavationManager excavationManager, float maxTraceDistance = 10f)
        {
            // TODO: Implement proper grounding with:
            // - GPU-accelerated SDF queries for performance
            // - Slope angle limits
            // - Step-up/step-down thresholds
            // - Integration with character controller

            // For now, use simple sphere trace downward
            var hit = SphereTrace(position, Vector3.down, maxTraceDistance, excavationManager);

            if (hit.isHit)
            {
                return hit.position;
            }

            return position;
        }

        #endregion

        void OnDrawGizmos()
        {
            if (!drawGizmos || layers == null) return;

            // Draw layer boundaries as wireframe boxes (approximate)
            foreach (var layer in layers)
            {
                if (layer == null || layer.geometryData == null)
                    continue;

                Gizmos.color = layer.baseColour;

                // Draw different gizmos based on geometry type
                if (layer.geometryData is DepthBandGeometry depth)
                {
                    Vector3 center = new Vector3(0, (depth.topY + depth.bottomY) * 0.5f, 0);
                    Vector3 size = new Vector3(10, depth.topY - depth.bottomY, 10);
                    Gizmos.DrawWireCube(center, size);
                }
                else if (layer.geometryData is CutGeometry cut)
                {
                    Gizmos.DrawWireSphere(cut.centre, cut.radius);
                }
                else if (layer.geometryData is EllipsoidGeometry ellipsoid)
                {
                    Gizmos.matrix = Matrix4x4.TRS(ellipsoid.centre, Quaternion.identity, ellipsoid.radii);
                    Gizmos.DrawWireSphere(Vector3.zero, 1f);
                    Gizmos.matrix = Matrix4x4.identity;
                }
            }

            // Debug sphere trace
            if (debugSphereTrace)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(debugPosition, 0.1f);
            }
        }
    }
}
