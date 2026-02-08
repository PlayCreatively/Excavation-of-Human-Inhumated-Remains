using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Ellipsoidal geometry for burial mounds, tumuli, or rounded deposits/fills.
    /// 
    /// NOTE: Uses an average radius approximation for the SDF.
    /// Works well for nearly-spherical ellipsoids but may produce visual artifacts
    /// for highly elongated shapes. For such cases, consider multiple overlapping spheres.
    /// </summary>
    [System.Serializable]
    public class EllipsoidGeometry : LayerGeometryData
    {
        [Tooltip("Center point of the ellipsoid (world space XYZ)")]
        public Vector3 centre = Vector3.zero;

        [Tooltip("Radius along each axis (X, Y, Z)")]
        public Vector3 radii = Vector3.one;

        public override LayerGeometryType GeometryType => LayerGeometryType.Ellipsoid;
        public override LayerCategory Category => LayerCategory.Fill;

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
            Vector3 offset = worldPos - centre;

            Vector3 normalized = new Vector3(
                offset.x / Mathf.Max(radii.x, 0.001f),
                offset.y / Mathf.Max(radii.y, 0.001f),
                offset.z / Mathf.Max(radii.z, 0.001f)
            );

            float normalizedDist = normalized.magnitude;
            float avgRadius = (radii.x + radii.y + radii.z) / 3f;

            return (normalizedDist - 1f) * avgRadius;
        }
    }
}
