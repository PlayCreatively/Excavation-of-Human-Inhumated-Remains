using UnityEngine;
using UnityEditor;

namespace Excavation.Editor
{
    /// <summary>
    /// Custom editor for MaterialLayer to improve Inspector workflow.
    /// </summary>
    [CustomEditor(typeof(Stratigraphy.MaterialLayer))]
    public class MaterialLayerEditor : UnityEditor.Editor
    {
        private SerializedProperty layerNameProp;
        private SerializedProperty baseColourProp;
        private SerializedProperty albedoTextureProp;
        private SerializedProperty normalMapProp;
        private SerializedProperty hardnessProp;
        private SerializedProperty geometryDataProp;

        void OnEnable()
        {
            layerNameProp = serializedObject.FindProperty("layerName");
            baseColourProp = serializedObject.FindProperty("baseColour");
            albedoTextureProp = serializedObject.FindProperty("albedoTexture");
            normalMapProp = serializedObject.FindProperty("normalMap");
            hardnessProp = serializedObject.FindProperty("hardness");
            geometryDataProp = serializedObject.FindProperty("geometryData");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var layer = (Stratigraphy.MaterialLayer)target;

            // Header
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Material Layer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Identification
            EditorGUILayout.LabelField("Identification", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(layerNameProp);

            EditorGUILayout.Space();

            // Visual Properties
            EditorGUILayout.LabelField("Visual Properties", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(baseColourProp);
            EditorGUILayout.PropertyField(albedoTextureProp);
            EditorGUILayout.PropertyField(normalMapProp);

            EditorGUILayout.Space();

            // Material Properties
            EditorGUILayout.LabelField("Material Properties", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(hardnessProp);
            EditorGUILayout.HelpBox("Hardness affects dig speed: 0 = very soft, 10 = very hard", MessageType.Info);

            EditorGUILayout.Space();

            // Geometry
            EditorGUILayout.LabelField("Geometry Definition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(geometryDataProp);

            // Geometry type selector
            if (geometryDataProp.managedReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No geometry defined. Click '+' above to add geometry.", MessageType.Warning);
                
                if (GUILayout.Button("Add Depth Band"))
                {
                    geometryDataProp.managedReferenceValue = new Stratigraphy.DepthBandGeometry();
                }
                if (GUILayout.Button("Add Noisy Depth Band"))
                {
                    geometryDataProp.managedReferenceValue = new Stratigraphy.NoisyDepthBandGeometry();
                }
                if (GUILayout.Button("Add Cut (Pit/Posthole)"))
                {
                    geometryDataProp.managedReferenceValue = new Stratigraphy.CutGeometry();
                }
                if (GUILayout.Button("Add Ellipsoid (Mound)"))
                {
                    geometryDataProp.managedReferenceValue = new Stratigraphy.EllipsoidGeometry();
                }
            }
            else
            {
                // Show geometry type info
                string geometryType = geometryDataProp.managedReferenceValue.GetType().Name;
                EditorGUILayout.HelpBox($"Geometry Type: {geometryType}", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            // Preview section
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            
            Rect colorRect = EditorGUILayout.GetControlRect(false, 30);
            EditorGUI.DrawRect(colorRect, layer.baseColour);
            EditorGUILayout.LabelField($"Color: {layer.baseColour}");

            if (layer.geometryData != null)
            {
                EditorGUILayout.LabelField($"Operation: {layer.geometryData.operation}");
            }
        }
    }
}
