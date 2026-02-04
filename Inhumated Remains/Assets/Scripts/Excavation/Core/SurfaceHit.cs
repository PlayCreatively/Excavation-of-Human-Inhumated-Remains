using UnityEngine;
using Excavation.Stratigraphy;

namespace Excavation.Core
{
    /// <summary>
    /// Result of a sphere-trace/raymarch operation against the SDF.
    /// </summary>
    [System.Serializable]
    public struct SurfaceHit
    {
        public bool isHit;
        public Vector3 position;
        public Vector3 normal;
        public MaterialLayer material;
        public float distance;

        public static SurfaceHit Miss()
        {
            return new SurfaceHit
            {
                isHit = false,
                position = Vector3.zero,
                normal = Vector3.up,
                material = null,
                distance = float.MaxValue
            };
        }

        public static SurfaceHit Hit(Vector3 position, Vector3 normal, MaterialLayer material, float distance)
        {
            return new SurfaceHit
            {
                isHit = true,
                position = position,
                normal = normal,
                material = material,
                distance = distance
            };
        }
    }
}
