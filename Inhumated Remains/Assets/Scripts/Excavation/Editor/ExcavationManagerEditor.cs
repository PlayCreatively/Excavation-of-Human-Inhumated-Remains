using UnityEngine;
using UnityEditor;

namespace Excavation.Editor
{
    /// <summary>
    /// Custom editor for ExcavationManager with utility functions.
    /// </summary>
    [CustomEditor(typeof(Core.ExcavationManager))]
    public class ExcavationManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty settingsProp;
        private SerializedProperty carveShaderProp;
        private SerializedProperty mipGenShaderProp;

        void OnEnable()
        {
            settingsProp = serializedObject.FindProperty("settings");
            carveShaderProp = serializedObject.FindProperty("carveShader");
            mipGenShaderProp = serializedObject.FindProperty("mipGenShader");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var manager = (Core.ExcavationManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Excavation Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Configuration
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp);

            // Display volume info if settings assigned
            if (settingsProp.objectReferenceValue != null)
            {
                var settings = settingsProp.objectReferenceValue as Core.ExcavationVolumeSettings;
                if (settings != null)
                {
                    var resolution = settings.GetTextureResolution();
                    int totalVoxels = resolution.x * resolution.y * resolution.z;
                    float memorySizeMB = (totalVoxels * 2f) / (1024f * 1024f); // R16 = 2 bytes per voxel

                    EditorGUILayout.HelpBox(
                        $"Volume: {settings.worldSize.x}×{settings.worldSize.y}×{settings.worldSize.z}m\n" +
                        $"Resolution: {resolution.x}×{resolution.y}×{resolution.z}\n" +
                        $"Total Voxels: {totalVoxels:N0}\n" +
                        $"Memory (approx): {memorySizeMB:F2} MB",
                        MessageType.Info
                    );
                }
            }

            EditorGUILayout.Space();

            // Compute Shaders
            EditorGUILayout.LabelField("Compute Shaders", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(carveShaderProp);
            EditorGUILayout.PropertyField(mipGenShaderProp);

            if (carveShaderProp.objectReferenceValue == null || mipGenShaderProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Compute shaders not assigned! Load from Resources/Shaders/", MessageType.Warning);
                
                if (GUILayout.Button("Auto-Load Shaders from Resources"))
                {
                    if (carveShaderProp.objectReferenceValue == null)
                    {
                        var carve = Resources.Load<ComputeShader>("Shaders/CarveVolume");
                        if (carve != null)
                        {
                            carveShaderProp.objectReferenceValue = carve;
                            Debug.Log("Loaded CarveVolume shader");
                        }
                    }
                    
                    if (mipGenShaderProp.objectReferenceValue == null)
                    {
                        var mipGen = Resources.Load<ComputeShader>("Shaders/GenerateMips");
                        if (mipGen != null)
                        {
                            mipGenShaderProp.objectReferenceValue = mipGen;
                            Debug.Log("Loaded GenerateMips shader");
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            // Runtime controls
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

                // Volume status
                if (manager.CarveVolume != null)
                {
                    EditorGUILayout.HelpBox(
                        $"Volume Active: {manager.CarveVolume.width}×{manager.CarveVolume.height}×{manager.CarveVolume.volumeDepth}\n" +
                        $"MIP Levels: {manager.CarveVolume.mipmapCount}",
                        MessageType.Info
                    );
                }
                else
                {
                    EditorGUILayout.HelpBox("Volume not initialized", MessageType.Warning);
                }

                // Clear button
                EditorGUI.BeginDisabledGroup(manager.CarveVolume == null);
                if (GUILayout.Button("Clear Volume (Reset All Excavations)"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Clear Excavation Volume",
                        "This will reset all excavations to pristine state. This cannot be undone!",
                        "Clear",
                        "Cancel"))
                    {
                        manager.ClearVolume();
                        Debug.Log("[ExcavationManager] Volume cleared");
                    }
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space();

                // Save/Load
                EditorGUILayout.LabelField("Serialization", EditorStyles.boldLabel);
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Volume to File"))
                {
                    string path = EditorUtility.SaveFilePanel(
                        "Save Excavation Volume",
                        Application.dataPath,
                        "excavation.dat",
                        "dat"
                    );
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        byte[] data = manager.SerializeVolume();
                        System.IO.File.WriteAllBytes(path, data);
                        Debug.Log($"[ExcavationManager] Volume saved to {path} ({data.Length / 1024f:F2} KB)");
                    }
                }
                
                if (GUILayout.Button("Load Volume from File"))
                {
                    string path = EditorUtility.OpenFilePanel(
                        "Load Excavation Volume",
                        Application.dataPath,
                        "dat"
                    );
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        byte[] data = System.IO.File.ReadAllBytes(path);
                        manager.LoadVolume(data);
                        Debug.Log($"[ExcavationManager] Volume loaded from {path}");
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Enter Play Mode to access runtime controls", MessageType.Info);
            }
        }
    }
}
