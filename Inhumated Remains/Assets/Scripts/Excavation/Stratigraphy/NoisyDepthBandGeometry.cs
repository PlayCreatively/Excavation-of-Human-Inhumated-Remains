using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Horizontal layer with undulating surfaces created using Perlin noise.
    /// Good for natural deposits with irregular boundaries.
    /// </summary>
    [System.Serializable]
    public class NoisyDepthBandGeometry : LayerGeometryData
    {
        [Tooltip("Base top surface Y coordinate (before noise offset)")]
        public float baseTopY = 0f;

        [Tooltip("Base bottom surface Y coordinate (before noise offset)")]
        public float baseBottomY = -0.3f;

        [Tooltip("Maximum noise displacement in meters")]
        [Range(0f, 1f)]
        public float noiseAmplitude = 0.05f;

        [Tooltip("Noise frequency (higher = more variation)")]
        [Range(0.1f, 10f)]
        public float noiseFrequency = 1f;

        [Tooltip("Noise offset for variation between layers")]
        public Vector2 noiseOffset = Vector2.zero;

        public override float SDF(Vector3 worldPos)
        {
            // Sample Perlin noise for this XZ position
            float noiseValue = Mathf.PerlinNoise(
                worldPos.x * noiseFrequency + noiseOffset.x,
                worldPos.z * noiseFrequency + noiseOffset.y
            );

            // Apply amplitude (noise is 0-1, remap to -amplitude to +amplitude)
            float offset = (noiseValue - 0.5f) * 2f * noiseAmplitude;

            // Offset both surfaces
            float topY = baseTopY + offset;
            float bottomY = baseBottomY + offset;

            // Calculate SDF
            float dTop = topY - worldPos.y;
            float dBot = worldPos.y - bottomY;

            return Mathf.Max(-dTop, -dBot);
        }
    }
}
