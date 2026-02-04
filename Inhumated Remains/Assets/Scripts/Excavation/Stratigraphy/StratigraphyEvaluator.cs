using UnityEngine;
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

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool debugSphereTrace = false;
        public Vector3 debugPosition = Vector3.zero;

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
