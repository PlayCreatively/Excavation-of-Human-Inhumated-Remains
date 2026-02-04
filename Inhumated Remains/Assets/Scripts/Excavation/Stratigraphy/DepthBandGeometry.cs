using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Horizontal layer defined by top and bottom Y coordinates.
    /// Typical for sedimentary deposits like topsoil, subsoil, etc.
    /// </summary>
    [System.Serializable]
    public class DepthBandGeometry : LayerGeometryData
    {
        [Tooltip("Top surface Y coordinate (world space)")]
        public float topY = 0f;

        [Tooltip("Bottom surface Y coordinate (world space)")]
        public float bottomY = -0.3f;

        public override float SDF(Vector3 worldPos)
        {
            // Distance from top (positive when below top)
            float dTop = topY - worldPos.y;

            // Distance from bottom (positive when above bottom)
            float dBot = worldPos.y - bottomY;

            // Inside when both are positive (below top AND above bottom)
            // Return negative SDF when inside
            return Mathf.Max(-dTop, -dBot);
        }
    }
}
