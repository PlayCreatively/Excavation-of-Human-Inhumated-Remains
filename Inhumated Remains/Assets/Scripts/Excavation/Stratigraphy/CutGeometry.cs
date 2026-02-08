using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Vertical cylindrical cut feature (pit, posthole, ditch).
    /// Represents archaeological fill contexts — material deposited inside a feature.
    /// The shape defines where the fill material exists.
    /// </summary>
    [System.Serializable]
    public class CutGeometry : LayerGeometryData
    {
        [Tooltip("Center point of the feature (world space XYZ)")]
        public Vector3 centre = Vector3.zero;

        [Tooltip("Radius of the cylindrical feature")]
        [Min(0.01f)]
        public float radius = 1f;

        [Tooltip("Depth of the feature from the centre Y coordinate")]
        [Min(0.01f)]
        public float depth = 1f;

        public override LayerGeometryType GeometryType => LayerGeometryType.Cut;
        public override LayerCategory Category => LayerCategory.Fill;

        public override Vector4 GetPackedParams()
        {
            // Cut: params(centreX, centreY, centreZ, radius)
            return new Vector4(centre.x, centre.y, centre.z, radius);
        }

        public override Vector4 GetPackedParams2()
        {
            // Cut: params2(depth, -, -, -)
            return new Vector4(depth, 0f, 0f, 0f);
        }

        public override float SDF(Vector3 worldPos)
        {
            Vector2 horizontalOffset = new Vector2(
                worldPos.x - centre.x,
                worldPos.z - centre.z
            );
            float horizontalDist = horizontalOffset.magnitude - radius;

            float topDist = centre.y - worldPos.y;
            float bottomDist = worldPos.y - (centre.y - depth);
            float verticalDist = Mathf.Max(-topDist, -bottomDist);

            return Mathf.Max(horizontalDist, verticalDist);
        }
    }
}
