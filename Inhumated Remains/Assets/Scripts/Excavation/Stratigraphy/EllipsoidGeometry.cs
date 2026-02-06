using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Ellipsoidal geometry for burial mounds, tumuli, or rounded deposits.
    /// 
    /// NOTE: This uses an average radius approximation for the SDF calculation.
    /// A true ellipsoid SDF requires iterative solving which is expensive.
    /// The approximation works well for nearly-spherical ellipsoids but may
    /// produce visual artifacts for highly elongated shapes (e.g., radii of 1, 0.1, 1).
    /// For such cases, consider using multiple overlapping spheres instead.
    /// </summary>
    [System.Serializable]
    public class EllipsoidGeometry : LayerGeometryData
    {
        [Tooltip("Center point of the ellipsoid (world space)")]
        public Vector3 centre = Vector3.zero;

        [Tooltip("Radius along each axis (X, Y, Z)")]
        public Vector3 radii = Vector3.one;

        public override LayerGeometryType GeometryType => LayerGeometryType.Ellipsoid;

        public override Vector4 GetPackedParams()
        {
            // Ellipsoid: params(centreX, centreY, centreZ, -)
            return new Vector4(centre.x, centre.y, centre.z, 0f);
        }

        public override Vector4 GetPackedParams2()
        {
            // Ellipsoid: params2(radiusX, radiusY, radiusZ, -)
            return new Vector4(radii.x, radii.y, radii.z, 0f);
        }

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

            // Scale back to world space using average radius
            // This is an approximation - exact ellipsoid SDF requires iterative solving
            float avgRadius = (radii.x + radii.y + radii.z) / 3f;
            float sdf = (normalizedDist - 1f) * avgRadius;

            return sdf;
        }
    }
}
