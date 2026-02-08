using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Category of layer geometry, determines material evaluation order.
    /// </summary>
    public enum LayerCategory
    {
        Band,   // Horizontal deposit (DepthBand, NoisyDepthBand) — evaluated oldest→youngest
        Fill    // Discrete feature (Cut, Ellipsoid) — evaluated youngest→oldest
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

    /// <summary>
    /// Abstract base class for layer geometry definitions.
    /// Uses signed distance fields (SDF) to define layer boundaries.
    /// 
    /// Convention: Negative = inside the layer (solid), Positive = outside.
    /// All layers add solid geometry when baked into the volume texture.
    /// </summary>
    [System.Serializable]
    public abstract class LayerGeometryData
    {
        /// <summary>
        /// Get the geometry type ID for GPU evaluation.
        /// </summary>
        public abstract LayerGeometryType GeometryType { get; }

        /// <summary>
        /// Get the layer category (Band or Fill) for material evaluation ordering.
        /// </summary>
        public abstract LayerCategory Category { get; }

        /// <summary>
        /// Pack primary parameters for GPU (float4).
        /// Layout depends on geometry type — see SDFGeometry.hlsl for packing spec.
        /// </summary>
        public abstract Vector4 GetPackedParams();

        /// <summary>
        /// Pack secondary parameters for GPU (float4).
        /// Layout depends on geometry type — see SDFGeometry.hlsl for packing spec.
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
