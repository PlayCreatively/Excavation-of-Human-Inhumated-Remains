using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Horizontal layer with undulating surfaces created using Perlin noise.
    /// Stores thickness (depth); actual Y positions are computed by stacking.
    /// Good for natural deposits with irregular boundaries.
    /// </summary>
    [System.Serializable]
    public class NoisyDepthBandGeometry : LayerGeometryData
    {
        [Tooltip("Thickness of this layer in meters")]
        [Min(0.01f)]
        public float depth = 0.3f;

        [Tooltip("Maximum noise displacement in meters")]
        [Range(0f, 1f)]
        public float noiseAmplitude = 0.05f;

        [Tooltip("Noise frequency (higher = more variation)")]
        [Range(0.1f, 10f)]
        public float noiseFrequency = 1f;

        [Tooltip("Noise offset for variation between layers")]
        public Vector2 noiseOffset = Vector2.zero;

        // Computed by StratigraphyEvaluator during initialization.
        [HideInInspector] public float computedBaseTopY;
        [HideInInspector] public float computedBaseBottomY;

        public override LayerGeometryType GeometryType => LayerGeometryType.NoisyDepthBand;
        public override LayerCategory Category => LayerCategory.Band;

        public override Vector4 GetPackedParams()
        {
            // NoisyDepthBand: params(baseTopY, baseBottomY, amplitude, frequency)
            return new Vector4(computedBaseTopY, computedBaseBottomY, noiseAmplitude, noiseFrequency);
        }

        public override Vector4 GetPackedParams2()
        {
            // NoisyDepthBand: params2(offsetX, offsetZ, -, -)
            return new Vector4(noiseOffset.x, noiseOffset.y, 0f, 0f);
        }

        public override float SDF(Vector3 worldPos)
        {
            float noiseValue = Mathf.PerlinNoise(
                worldPos.x * noiseFrequency + noiseOffset.x,
                worldPos.z * noiseFrequency + noiseOffset.y
            );

            float offset = (noiseValue - 0.5f) * 2f * noiseAmplitude;

            float topY = computedBaseTopY + offset;
            float bottomY = computedBaseBottomY + offset;

            float dTop = topY - worldPos.y;
            float dBot = worldPos.y - bottomY;

            return Mathf.Max(-dTop, -dBot);
        }
    }
}
