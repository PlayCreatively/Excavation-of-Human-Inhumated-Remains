using UnityEngine;
using UnityEditor;

namespace Excavation.Editor
{
    /// <summary>
    /// Custom editor for StratigraphyEvaluator.
    /// </summary>
    [CustomEditor(typeof(Stratigraphy.StratigraphyEvaluator))]
    public class StratigraphyEvaluatorEditor : UnityEditor.Editor
    {
        private SerializedProperty layersProp;
        private SerializedProperty defaultSubstrateProp;
        private SerializedProperty surfaceYProp;
        private SerializedProperty bakeLayerShaderProp;

        void OnEnable()
        {
            layersProp = serializedObject.FindProperty("layers");
            defaultSubstrateProp = serializedObject.FindProperty("defaultSubstrate");
            surfaceYProp = serializedObject.FindProperty("surfaceY");
            bakeLayerShaderProp = serializedObject.FindProperty("bakeLayerShader");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var evaluator = (Stratigraphy.StratigraphyEvaluator)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stratigraphy Evaluator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Layer order: youngest to oldest (top to bottom)\n\n" +
                "  • Fills at top (youngest first)\n" +
                "  • Bands at bottom (youngest first)\n\n" +
                "Bands stack downward from Surface Y. Their depth determines thickness.\n" +
                "All layers are baked into the volume at startup.",
                MessageType.Info);
            EditorGUILayout.Space();

            // Surface Y
            EditorGUILayout.LabelField("Terrain Surface", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(surfaceYProp, new GUIContent("Surface Y", "Top of the band stack"));

            EditorGUILayout.Space();

            // Layers
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(layersProp);

            // Layer stack preview
            if (layersProp.arraySize > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Layer Stack Preview:", EditorStyles.boldLabel);
                
                bool inFills = true;
                for (int i = 0; i < layersProp.arraySize; i++)
                {
                    var layerProp = layersProp.GetArrayElementAtIndex(i);
                    var layer = layerProp.objectReferenceValue as Stratigraphy.MaterialLayer;
                    
                    if (layer != null)
                    {
                        // Detect transition from fills to bands
                        if (inFills && layer.geometryData != null &&
                            layer.geometryData.Category == Stratigraphy.LayerCategory.Band)
                        {
                            inFills = false;
                            EditorGUILayout.LabelField("── Bands (youngest → oldest) ──",
                                EditorStyles.centeredGreyMiniLabel);
                        }
                        else if (i == 0 && layer.geometryData != null &&
                                 layer.geometryData.Category == Stratigraphy.LayerCategory.Fill)
                        {
                            EditorGUILayout.LabelField("── Fills (youngest → oldest) ──",
                                EditorStyles.centeredGreyMiniLabel);
                        }

                        EditorGUILayout.BeginHorizontal();
                        
                        Rect colorRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(20));
                        EditorGUI.DrawRect(colorRect, layer.baseColour);
                        
                        string category = layer.geometryData != null ? $"[{layer.geometryData.Category}]" : "";
                        EditorGUILayout.LabelField($"{i + 1}. {layer.layerName} {category}");
                        
                        EditorGUILayout.EndHorizontal();

                        // Show geometry info
                        EditorGUI.indentLevel++;
                        if (layer.geometryData != null)
                        {
                            string geomType = layer.geometryData.GetType().Name;
                            
                            if (layer.geometryData is Stratigraphy.DepthBandGeometry db)
                                EditorGUILayout.LabelField($"{geomType}: depth={db.depth}m  (Y: {db.computedTopY:F2} → {db.computedBottomY:F2})");
                            else if (layer.geometryData is Stratigraphy.NoisyDepthBandGeometry ndb)
                                EditorGUILayout.LabelField($"{geomType}: depth={ndb.depth}m  noise±{ndb.noiseAmplitude:F2}m  (Y: {ndb.computedBaseTopY:F2} → {ndb.computedBaseBottomY:F2})");
                            else if (layer.geometryData is Stratigraphy.CutGeometry cut)
                                EditorGUILayout.LabelField($"{geomType}: centre={cut.centre}, r={cut.radius}m, depth={cut.depth}m");
                            else if (layer.geometryData is Stratigraphy.EllipsoidGeometry ell)
                                EditorGUILayout.LabelField($"{geomType}: top={ell.centre.y + ell.radii.y}, centre={ell.centre}, bottom={ell.centre.y - ell.radii.y}, radii={ell.radii}");
                        }
                        EditorGUI.indentLevel--;
                        
                        EditorGUILayout.Space(3);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(defaultSubstrateProp);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baking", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(bakeLayerShaderProp);

            // Utility buttons
            EditorGUILayout.Space();
            if (GUILayout.Button("Compute Band Positions (Preview)"))
            {
                evaluator.InitializeLayers();
                EditorUtility.SetDirty(target);
            }

            serializedObject.ApplyModifiedProperties();
        }

		void OnSceneGUI()
		{
            Handles.color = Color.white;            
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                var layerProp = layersProp.GetArrayElementAtIndex(i);
                var layer = layerProp.objectReferenceValue as Stratigraphy.MaterialLayer;

                if (layer == null || layer.geometryData == null) continue;

                if (layer.geometryData is Stratigraphy.DepthBandGeometry db)
                {
                    float topY = db.computedTopY;
                    float bottomY = db.computedBottomY;
                    Vector3 center = new Vector3(0, (topY + bottomY) / 2f, 0);
                    Vector3 size = new Vector3(10, topY - bottomY, 10);
                    Handles.DrawWireCube(center, size);
                }
                else if (layer.geometryData is Stratigraphy.NoisyDepthBandGeometry ndb)
                {
                    float topY = ndb.computedBaseTopY;
                    float bottomY = ndb.computedBaseBottomY;
                    Vector3 center = new Vector3(0, (topY + bottomY) / 2f, 0);
                    Vector3 size = new Vector3(10, topY - bottomY, 10);
                    Handles.DrawWireCube(center, size);
                }
                else if (layer.geometryData is Stratigraphy.CutGeometry cut)
                {
                    Vector3 center = cut.centre;
                    Vector3 size = new Vector3(cut.radius * 2, cut.depth, cut.radius * 2);
                    Handles.DrawWireCube(center - new Vector3(0, cut.depth / 2f, 0), size);
                }
                else if (layer.geometryData is Stratigraphy.EllipsoidGeometry ell)
                {
                    Handles.DrawWireDisc(new Vector3(ell.centre.x, ell.centre.y + ell.radii.y, ell.centre.z), Vector3.up, ell.radii.x);
                    Handles.DrawWireDisc(new Vector3(ell.centre.x, ell.centre.y - ell.radii.y, ell.centre.z), Vector3.up, ell.radii.x);
                    
                    // Approximate vertical profile with a line
                    Handles.DrawLine(
                        new Vector3(ell.centre.x, ell.centre.y + ell.radii.y, ell.centre.z),
                        new Vector3(ell.centre.x, ell.centre.y - ell.radii.y, ell.centre.z));
                }
            }
		}
	}
}
