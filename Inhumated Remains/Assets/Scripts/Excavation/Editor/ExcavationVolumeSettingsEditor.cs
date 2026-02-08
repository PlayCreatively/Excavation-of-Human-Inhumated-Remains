using UnityEngine;
using UnityEditor;

namespace Excavation.Editor
{
    /// <summary>
    /// Custom editor for ExcavationManager with utility functions.
    /// </summary>
    [CustomEditor(typeof(Core.ExcavationVolumeSettings))]
    public class ExcavationVolumeSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var settings = (Core.ExcavationVolumeSettings)target;

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

            DrawPropertiesExcluding(serializedObject, "m_Script");
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
