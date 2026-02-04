using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Excavation.Editor
{
    /// <summary>
    /// Custom editor for StratigraphyEvaluator with Harris Matrix visualization.
    /// </summary>
    [CustomEditor(typeof(Stratigraphy.StratigraphyEvaluator))]
    public class StratigraphyEvaluatorEditor : UnityEditor.Editor
    {
        private SerializedProperty layersProp;
        private SerializedProperty defaultSubstrateProp;
        private SerializedProperty baseTerrainYProp;
        private SerializedProperty drawGizmosProp;
        private SerializedProperty debugSphereTraceProp;
        private SerializedProperty debugPositionProp;

        void OnEnable()
        {
            layersProp = serializedObject.FindProperty("layers");
            defaultSubstrateProp = serializedObject.FindProperty("defaultSubstrate");
            baseTerrainYProp = serializedObject.FindProperty("baseTerrainY");
            drawGizmosProp = serializedObject.FindProperty("drawGizmos");
            debugSphereTraceProp = serializedObject.FindProperty("debugSphereTrace");
            debugPositionProp = serializedObject.FindProperty("debugPosition");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var evaluator = (Stratigraphy.StratigraphyEvaluator)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stratigraphy Evaluator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Layers are ordered from YOUNGEST (top) to OLDEST (bottom) following the Harris Matrix principle.", MessageType.Info);
            EditorGUILayout.Space();

            // Layer Configuration
            EditorGUILayout.LabelField("Layer Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(layersProp);

            // Visual layer stack preview
            if (layersProp.arraySize > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Layer Stack Preview (Youngest → Oldest):", EditorStyles.boldLabel);
                
                for (int i = 0; i < layersProp.arraySize; i++)
                {
                    var layerProp = layersProp.GetArrayElementAtIndex(i);
                    var layer = layerProp.objectReferenceValue as Stratigraphy.MaterialLayer;
                    
                    if (layer != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        
                        // Color indicator
                        Rect colorRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(20));
                        EditorGUI.DrawRect(colorRect, layer.baseColour);
                        
                        // Layer info
                        EditorGUILayout.LabelField($"{i + 1}. {layer.layerName}", EditorStyles.boldLabel);
                        
                        EditorGUILayout.EndHorizontal();
                        
                        // Additional info
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Hardness: {layer.hardness}/10");
                        if (layer.geometryData != null)
                        {
                            EditorGUILayout.LabelField($"Geometry: {layer.geometryData.GetType().Name} ({layer.geometryData.operation})");
                        }
                        EditorGUI.indentLevel--;
                        
                        EditorGUILayout.Space(5);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No layers assigned. Add layers to define stratigraphy.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(defaultSubstrateProp);

            EditorGUILayout.Space();

            // Base Terrain
            EditorGUILayout.LabelField("Base Terrain", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(baseTerrainYProp);

            EditorGUILayout.Space();

            // Debug
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(drawGizmosProp);
            EditorGUILayout.PropertyField(debugSphereTraceProp);
            
            if (debugSphereTraceProp.boolValue)
            {
                EditorGUILayout.PropertyField(debugPositionProp);
                
                // Test SDF button
                if (GUILayout.Button("Test SDF at Debug Position"))
                {
                    if (Application.isPlaying)
                    {
                        float sdf = evaluator.GetSceneSDF(evaluator.debugPosition, null);
                        var material = evaluator.GetMaterialAt(evaluator.debugPosition);
                        
                        Debug.Log($"[Stratigraphy Test] Position: {evaluator.debugPosition}\n" +
                                  $"SDF Value: {sdf}\n" +
                                  $"Material: {(material != null ? material.layerName : "None")}");
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Enter Play Mode to test SDF evaluation.", MessageType.Info);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
