using UnityEngine;

namespace Excavation.Core
{
    /// <summary>
    /// Static utility functions for SDF operations.
    /// These operations are replicated in HLSL shaders.
    /// </summary>
    public static class SDFUtility
    {
        /// <summary>
        /// Union of two SDFs (combines volumes).
        /// Returns the minimum, creating a shape that encompasses both.
        /// </summary>
        public static float Union(float a, float b)
        {
            return Mathf.Min(a, b);
        }

        /// <summary>
        /// Subtraction (removes b from a).
        /// Used for boolean subtraction operations.
        /// </summary>
        public static float Subtract(float a, float b)
        {
            return Mathf.Max(a, -b);
        }

        /// <summary>
        /// Intersection of two SDFs (only where both overlap).
        /// </summary>
        public static float Intersect(float a, float b)
        {
            return Mathf.Max(a, b);
        }

        /// <summary>
        /// Smooth minimum for soft blending between SDFs.
        /// </summary>
        /// <param name="a">First SDF value</param>
        /// <param name="b">Second SDF value</param>
        /// <param name="k">Smoothing factor (higher = smoother transition)</param>
        public static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>
        /// Compute gradient of an SDF for normal calculation.
        /// Uses central differences.
        /// </summary>
        public static Vector3 ComputeGradient(System.Func<Vector3, float> sdfFunc, Vector3 p, float epsilon = 0.001f)
        {
            Vector3 gradient = new Vector3(
                sdfFunc(p + Vector3.right * epsilon) - sdfFunc(p - Vector3.right * epsilon),
                sdfFunc(p + Vector3.up * epsilon) - sdfFunc(p - Vector3.up * epsilon),
                sdfFunc(p + Vector3.forward * epsilon) - sdfFunc(p - Vector3.forward * epsilon)
            );

            return gradient.normalized;
        }
    }
}
