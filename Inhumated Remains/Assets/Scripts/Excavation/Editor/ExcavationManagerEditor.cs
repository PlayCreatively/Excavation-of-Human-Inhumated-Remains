using UnityEngine;
using UnityEditor;

namespace Excavation.Editor
{
    [CustomEditor(typeof(Core.ExcavationManager))]
    public class ExcavationManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty settingsProp;
        private SerializedProperty stratigraphyProp;
        private SerializedProperty carveShaderProp;
        private SerializedProperty mipGenShaderProp;

        void OnEnable()
        {
            settingsProp = serializedObject.FindProperty("settings");
            stratigraphyProp = serializedObject.FindProperty("stratigraphy");
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

            if (settingsProp.objectReferenceValue != null)
            {
                var settings = settingsProp.objectReferenceValue as Core.ExcavationVolumeSettings;
                if (settings != null)
                {
                    var resolution = settings.GetTextureResolution();
                    int totalVoxels = resolution.x * resolution.y * resolution.z;
                    float memorySizeMB = (totalVoxels * 4f) / (1024f * 1024f); // RFloat = 4 bytes

                    EditorGUILayout.HelpBox(
                        $"Volume: {settings.worldSize.x}×{settings.worldSize.y}×{settings.worldSize.z}m\n" +
                        $"Resolution: {resolution.x}×{resolution.y}×{resolution.z}\n" +
                        $"Voxels: {totalVoxels:N0} | Memory: ~{memorySizeMB:F1} MB",
                        MessageType.Info);
                }
            }

            EditorGUILayout.Space();

            // Stratigraphy
            EditorGUILayout.LabelField("Stratigraphy", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(stratigraphyProp);

            EditorGUILayout.Space();

            // Compute Shaders
            EditorGUILayout.LabelField("Compute Shaders", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(carveShaderProp);
            EditorGUILayout.PropertyField(mipGenShaderProp);

            if (carveShaderProp.objectReferenceValue == null || mipGenShaderProp.objectReferenceValue == null)
            {
                if (GUILayout.Button("Auto-Load Shaders from Resources"))
                {
                    if (carveShaderProp.objectReferenceValue == null)
                    {
                        var carve = Resources.Load<ComputeShader>("Shaders/CarveVolume");
                        if (carve != null) carveShaderProp.objectReferenceValue = carve;
                    }
                    if (mipGenShaderProp.objectReferenceValue == null)
                    {
                        var mipGen = Resources.Load<ComputeShader>("Shaders/GenerateMips");
                        if (mipGen != null) mipGenShaderProp.objectReferenceValue = mipGen;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            // Runtime controls
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

                if (manager.CarveVolume != null)
                {
                    EditorGUILayout.HelpBox(
                        $"Volume: {manager.CarveVolume.width}×{manager.CarveVolume.height}×{manager.CarveVolume.volumeDepth}\n" +
                        $"MIP Levels: {manager.CarveVolume.mipmapCount}",
                        MessageType.Info);
                }

                EditorGUI.BeginDisabledGroup(manager.CarveVolume == null);

                if (GUILayout.Button("Rebake Layers (Destroys Carving!)"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Rebake Layers",
                        "This will clear all carving and re-bake layers from scratch.",
                        "Rebake", "Cancel"))
                    {
                        manager.RebakeLayers();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Save / Load", EditorStyles.boldLabel);
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save"))
                {
                    string path = EditorUtility.SaveFilePanel("Save Volume", Application.dataPath, "excavation.dat", "dat");
                    if (!string.IsNullOrEmpty(path))
                    {
                        manager.SaveExcavation(path, (ok) =>
                        {
                            if (ok) Debug.Log($"Saved to {path}");
                        });
                    }
                }
                if (GUILayout.Button("Load"))
                {
                    string path = EditorUtility.OpenFilePanel("Load Volume", Application.dataPath, "dat");
                    if (!string.IsNullOrEmpty(path))
                        manager.LoadExcavation(path);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Enter Play Mode for runtime controls (rebake, save/load).", MessageType.Info);
            }
        }
    }
}
