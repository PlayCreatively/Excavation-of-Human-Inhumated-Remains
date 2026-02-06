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
    /// Geometry type IDs for GPU evaluation.
    /// MUST match constants in SDFGeometry.hlsl!
    /// </summary>
    public enum LayerGeometryType
    {
        DepthBand = 0,
        NoisyDepthBand = 1,
        Cut = 2,
        Ellipsoid = 3
    }

    // TODO: Fill Geometry
    // A Fill geometry could reference an existing Cut geometry and override its material.
    // This would allow modeling pit fills, ditch fills, etc. that share the same shape
    // as their cut but have different material properties.
    // Implementation would need to:
    // 1. Add FillGeometry class that references a CutGeometry
    // 2. In layer ordering, ensure fill comes before cut (younger)
    // 3. Fill's SDF would delegate to the referenced cut's SDF

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
        /// Get the geometry type ID for GPU evaluation.
        /// </summary>
        public abstract LayerGeometryType GeometryType { get; }

        /// <summary>
        /// Pack primary parameters for GPU (float4).
        /// Layout depends on geometry type - see SDFGeometry.hlsl for packing spec.
        /// </summary>
        public abstract Vector4 GetPackedParams();

        /// <summary>
        /// Pack secondary parameters for GPU (float4).
        /// Layout depends on geometry type - see SDFGeometry.hlsl for packing spec.
        /// </summary>
        public abstract Vector4 GetPackedParams2();

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
