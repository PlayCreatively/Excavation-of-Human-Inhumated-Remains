using UnityEngine;

namespace Excavation.Core
{
    /// <summary>
    /// Represents a single brush stroke operation for carving.
    /// Passed to compute shaders for volume modification.
    /// </summary>
    [System.Serializable]
    public struct BrushStroke
    {
        public Vector3 worldPosition;
        public float radius;
        public float intensity;
        public float deltaTime;

        public BrushStroke(Vector3 position, float radius, float intensity, float deltaTime)
        {
            this.worldPosition = position;
            this.radius = radius;
            this.intensity = intensity;
            this.deltaTime = deltaTime;
        }
    }
}
