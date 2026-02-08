using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Horizontal layer defined by thickness (depth).
    /// Actual Y positions are computed by StratigraphyEvaluator by stacking layers.
    /// Typical for sedimentary deposits like topsoil, subsoil, etc.
    /// </summary>
    [System.Serializable]
    public class DepthBandGeometry : LayerGeometryData
    {
        [Tooltip("Thickness of this layer in meters")]
        [Min(0.01f)]
        public float depth = 0.3f;

        // Computed by StratigraphyEvaluator during initialization.
        // These are set externally and used for SDF evaluation and GPU packing.
        [HideInInspector] public float computedTopY;
        [HideInInspector] public float computedBottomY;

        public override LayerGeometryType GeometryType => LayerGeometryType.DepthBand;
        public override LayerCategory Category => LayerCategory.Band;

        public override Vector4 GetPackedParams()
        {
            // DepthBand: params(topY, bottomY, -, -)
            return new Vector4(computedTopY, computedBottomY, 0f, 0f);
        }

        public override Vector4 GetPackedParams2()
        {
            return Vector4.zero;
        }

        public override float SDF(Vector3 worldPos)
        {
            float dTop = computedTopY - worldPos.y;
            float dBot = worldPos.y - computedBottomY;
            return Mathf.Max(-dTop, -dBot);
        }
    }
}
