using UnityEngine;
using UnityEditor;

namespace Excavation.Editor
{
    /// <summary>
    /// Custom editor for MaterialLayer.
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

            DrawPropertiesExcluding(serializedObject, "m_Script");

            var layer = (Stratigraphy.MaterialLayer)target;

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

            EditorGUILayout.Space();

            // Geometry
            EditorGUILayout.LabelField("Geometry Definition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(geometryDataProp);

            if (geometryDataProp.managedReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No geometry defined. Choose a geometry type below.", MessageType.Warning);
                
                EditorGUILayout.LabelField("Bands (horizontal deposits):", EditorStyles.miniLabel);
                if (GUILayout.Button("Add Depth Band"))
                    geometryDataProp.managedReferenceValue = new Stratigraphy.DepthBandGeometry();
                if (GUILayout.Button("Add Noisy Depth Band"))
                    geometryDataProp.managedReferenceValue = new Stratigraphy.NoisyDepthBandGeometry();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Fills (discrete features):", EditorStyles.miniLabel);
                if (GUILayout.Button("Add Cut Fill (Pit/Posthole)"))
                    geometryDataProp.managedReferenceValue = new Stratigraphy.CutGeometry();
                if (GUILayout.Button("Add Ellipsoid Fill (Mound)"))
                    geometryDataProp.managedReferenceValue = new Stratigraphy.EllipsoidGeometry();
            }
            else
            {
                var geom = geometryDataProp.managedReferenceValue as Stratigraphy.LayerGeometryData;
                if (geom != null)
                {
                    string typeName = geom.GetType().Name;
                    string category = geom.Category.ToString();
                    EditorGUILayout.HelpBox($"{typeName} ({category})", MessageType.Info);

                    // Show computed Y values for depth bands (read-only)
                    if (geom is Stratigraphy.DepthBandGeometry db)
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.FloatField("Computed Top Y", db.computedTopY);
                        EditorGUILayout.FloatField("Computed Bottom Y", db.computedBottomY);
                        EditorGUI.EndDisabledGroup();
                    }
                    else if (geom is Stratigraphy.NoisyDepthBandGeometry ndb)
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.FloatField("Computed Base Top Y", ndb.computedBaseTopY);
                        EditorGUILayout.FloatField("Computed Base Bottom Y", ndb.computedBaseBottomY);
                        EditorGUI.EndDisabledGroup();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            // Preview
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            
            Rect colorRect = EditorGUILayout.GetControlRect(false, 30);
            EditorGUI.DrawRect(colorRect, layer.baseColour);
        }
    }
}
