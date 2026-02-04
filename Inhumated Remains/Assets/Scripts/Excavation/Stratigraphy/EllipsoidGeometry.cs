using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Ellipsoidal geometry for burial mounds, tumuli, or rounded deposits.
    /// </summary>
    [System.Serializable]
    public class EllipsoidGeometry : LayerGeometryData
    {
        [Tooltip("Center point of the ellipsoid (world space)")]
        public Vector3 centre = Vector3.zero;

        [Tooltip("Radius along each axis (X, Y, Z)")]
        public Vector3 radii = Vector3.one;

        public override float SDF(Vector3 worldPos)
        {
            // Offset from center
            Vector3 offset = worldPos - centre;

            // Normalize by radii (creates unit sphere in normalized space)
            Vector3 normalized = new Vector3(
                offset.x / Mathf.Max(radii.x, 0.001f),
                offset.y / Mathf.Max(radii.y, 0.001f),
                offset.z / Mathf.Max(radii.z, 0.001f)
            );

            // Distance from center in normalized space
            float normalizedDist = normalized.magnitude;

            // Scale back to world space
            // This is an approximation - exact ellipsoid SDF is more complex
            float avgRadius = (radii.x + radii.y + radii.z) / 3f;
            float sdf = (normalizedDist - 1f) * avgRadius;

            return sdf;
        }
    }
}
