using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Vertical cylindrical cut feature (pit, posthole, ditch).
    /// Represents archaeological cut contexts.
    /// </summary>
    [System.Serializable]
    public class CutGeometry : LayerGeometryData
    {
        [Tooltip("Center point of the cut (world space)")]
        public Vector3 centre = Vector3.zero;

        [Tooltip("Radius of the cylindrical cut")]
        [Range(0.1f, 10f)]
        public float radius = 1f;

        [Tooltip("Depth of the cut from the centre Y coordinate")]
        [Range(0.1f, 5f)]
        public float depth = 1f;

        public override float SDF(Vector3 worldPos)
        {
            // Horizontal distance from center (XZ plane)
            Vector2 horizontalOffset = new Vector2(
                worldPos.x - centre.x,
                worldPos.z - centre.z
            );
            float horizontalDist = horizontalOffset.magnitude - radius;

            // Vertical containment (between top and bottom)
            float topDist = centre.y - worldPos.y;           // Positive when below top
            float bottomDist = worldPos.y - (centre.y - depth); // Positive when above bottom

            float verticalDist = Mathf.Max(-topDist, -bottomDist);

            // Combine: inside when both horizontal AND vertical are negative
            return Mathf.Max(horizontalDist, verticalDist);
        }
    }
}
