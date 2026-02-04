using UnityEngine;

namespace Excavation.Stratigraphy
{
    /// <summary>
    /// Defines a single stratigraphic layer with its visual properties and geometry.
    /// </summary>
    [CreateAssetMenu(fileName = "New Material Layer", menuName = "Excavation/Material Layer")]
    public class MaterialLayer : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Archaeological context name (e.g., 'Topsoil', 'Pit Fill 001')")]
        public string layerName = "Unnamed Layer";

        [Header("Visual Properties")]
        [Tooltip("Base color for this material layer")]
        public Color baseColour = Color.gray;

        [Tooltip("Albedo texture for triplanar mapping")]
        public Texture2D albedoTexture;

        [Tooltip("Normal map for surface detail")]
        public Texture2D normalMap;

        [Header("Material Properties")]
        [Tooltip("How difficult this material is to dig (affects dig speed)")]
        [Range(0f, 10f)]
        public float hardness = 5f;

        [Header("Geometry")]
        [Tooltip("Defines the 3D shape and position of this layer")]
        [SerializeReference]
        public LayerGeometryData geometryData;

        /// <summary>
        /// Check if a world position is inside this layer.
        /// </summary>
        public bool Contains(Vector3 worldPos)
        {
            if (geometryData == null)
                return false;
            return geometryData.Contains(worldPos);
        }

        /// <summary>
        /// Evaluate the signed distance to this layer's boundary.
        /// </summary>
        public float SDF(Vector3 worldPos)
        {
            if (geometryData == null)
                return float.MaxValue;
            return geometryData.SDF(worldPos);
        }
    }
}
