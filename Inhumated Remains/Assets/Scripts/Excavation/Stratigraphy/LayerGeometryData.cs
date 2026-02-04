using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// How a layer's geometry interacts with the base terrain.
    /// </summary>
    public enum LayerOperation
    {
        Inside,   // AND — only exists within base terrain (default for deposits)
        Union,    // OR  — adds to base terrain (burial mounds, spoil heaps)
        Subtract  // XOR — cuts through everything (modern service trench, pipe)
    }

    /// <summary>
    /// Abstract base class for layer geometry definitions.
    /// Uses signed distance fields (SDF) to define layer boundaries.
    /// </summary>
    [System.Serializable]
    public abstract class LayerGeometryData
    {
        [Tooltip("How this layer interacts with the base terrain")]
        public LayerOperation operation = LayerOperation.Inside;

        /// <summary>
        /// Evaluate the signed distance to the layer boundary.
        /// Negative = inside the layer, Positive = outside the layer.
        /// </summary>
        public abstract float SDF(Vector3 worldPos);

        /// <summary>
        /// Check if a world position is inside this layer.
        /// </summary>
        public bool Contains(Vector3 worldPos)
        {
            return SDF(worldPos) < 0f;
        }
    }
}
