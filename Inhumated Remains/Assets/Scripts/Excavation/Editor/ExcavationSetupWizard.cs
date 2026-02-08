using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Excavation.Editor
{
    /// <summary>
    /// Scene setup wizard to quickly create an excavation system in the current scene.
    /// </summary>
    public class ExcavationSetupWizard : EditorWindow
    {
        private Core.ExcavationVolumeSettings volumeSettings;
        [SerializeField] private Stratigraphy.MaterialLayer[] layers = new Stratigraphy.MaterialLayer[0];
        private Tools.DigBrushPreset digBrush;
        private bool createExampleAssets = true;

        [MenuItem("Tools/Excavation/Setup Wizard")]
        public static void ShowWindow()
        {
            var window = GetWindow<ExcavationSetupWizard>("Excavation Setup");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Excavation System Setup Wizard", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "This wizard will create and configure all necessary GameObjects for the excavation system in your current scene.",
                MessageType.Info
            );

            EditorGUILayout.Space();

            // Asset references
            EditorGUILayout.LabelField("Asset References", EditorStyles.boldLabel);
            
            volumeSettings = (Core.ExcavationVolumeSettings)EditorGUILayout.ObjectField(
                "Volume Settings",
                volumeSettings,
                typeof(Core.ExcavationVolumeSettings),
                false
            );

            EditorGUILayout.LabelField("Material Layers (youngest to oldest):");
            SerializedObject so = new SerializedObject(this);
            SerializedProperty layersProp = so.FindProperty("layers");
            EditorGUILayout.PropertyField(layersProp, true);
            so.ApplyModifiedProperties();

            digBrush = (Tools.DigBrushPreset)EditorGUILayout.ObjectField(
                "Dig Brush",
                digBrush,
                typeof(Tools.DigBrushPreset),
                false
            );

            EditorGUILayout.Space();

            // Quick creation option
            createExampleAssets = EditorGUILayout.Toggle("Create Example Assets", createExampleAssets);

            if (createExampleAssets)
            {
                EditorGUILayout.HelpBox(
                    "Will create example volume settings, layers, and brush if not assigned above.",
                    MessageType.Info
                );
            }

            EditorGUILayout.Space();

            // Setup button
            EditorGUI.BeginDisabledGroup(!CanSetup());
            if (GUILayout.Button("Create Excavation System", GUILayout.Height(40)))
            {
                SetupScene();
            }
            EditorGUI.EndDisabledGroup();

            if (!CanSetup())
            {
                EditorGUILayout.HelpBox(
                    "Please assign Volume Settings, at least one Material Layer, and a Dig Brush. Or enable 'Create Example Assets' to auto-generate.",
                    MessageType.Warning
                );
            }
        }

        bool CanSetup()
        {
            if (createExampleAssets)
                return true;

            return volumeSettings != null && layers != null && layers.Length > 0 && digBrush != null;
        }

        void SetupScene()
        {
            // Create example assets if needed
            if (createExampleAssets)
            {
                CreateExampleAssets();
            }

            // Create GameObjects
            CreateExcavationManager();
            CreateStratigraphyEvaluator();
            CreateExcavationRenderer();
            AddDigToolToCamera();

            EditorUtility.DisplayDialog(
                "Success!",
                "Excavation system has been set up in the scene. Enter Play Mode and start digging!",
                "OK"
            );

            Debug.Log("[ExcavationSetupWizard] Scene setup complete!");
        }

        void CreateExampleAssets()
        {
            string assetPath = "Assets/ExampleContent/Excavation/";
            
            if (!AssetDatabase.IsValidFolder("Assets/ExampleContent"))
                AssetDatabase.CreateFolder("Assets", "ExampleContent");
            if (!AssetDatabase.IsValidFolder(assetPath.TrimEnd('/')))
                AssetDatabase.CreateFolder("Assets/ExampleContent", "Excavation");

            // Create volume settings
            if (volumeSettings == null)
            {
                volumeSettings = ScriptableObject.CreateInstance<Core.ExcavationVolumeSettings>();
                volumeSettings.worldOrigin = Vector3.zero;
                volumeSettings.worldSize = new Vector3(5, 3, 5);
                volumeSettings.voxelSize = 0.1f;
                AssetDatabase.CreateAsset(volumeSettings, assetPath + "TestVolumeSettings.asset");
            }

            // Create topsoil layer
            if (layers == null || layers.Length == 0)
            {
                var topsoil = ScriptableObject.CreateInstance<Stratigraphy.MaterialLayer>();
                topsoil.layerName = "Topsoil";
                topsoil.baseColour = new Color(0.4f, 0.3f, 0.2f);
                topsoil.hardness = 4f;
                topsoil.geometryData = new Stratigraphy.DepthBandGeometry
                {
                    depth = 0.8f,
                };
                AssetDatabase.CreateAsset(topsoil, assetPath + "Topsoil.asset");

                var subsoil = ScriptableObject.CreateInstance<Stratigraphy.MaterialLayer>();
                subsoil.layerName = "Subsoil";
                subsoil.baseColour = new Color(0.6f, 0.4f, 0.2f);
                subsoil.hardness = 7f;
                subsoil.geometryData = new Stratigraphy.DepthBandGeometry
                {
                    depth = 0.8f,
                };
                AssetDatabase.CreateAsset(subsoil, assetPath + "Subsoil.asset");

                layers = new Stratigraphy.MaterialLayer[] { topsoil, subsoil };
            }

            // Create dig brush
            if (digBrush == null)
            {
                digBrush = ScriptableObject.CreateInstance<Tools.DigBrushPreset>();
                digBrush.radius = 0.05f;
                digBrush.digSpeed = 1f;
                AssetDatabase.CreateAsset(digBrush, assetPath + "Trowel.asset");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        void CreateExcavationManager()
        {
            var go = new GameObject("ExcavationManager");
            var manager = go.AddComponent<Core.ExcavationManager>();

            // Use reflection to set private fields (or make them public temporarily)
            var settingsField = typeof(Core.ExcavationManager).GetField("settings", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            settingsField?.SetValue(manager, volumeSettings);

            // Load compute shaders
            var carveShader = Resources.Load<ComputeShader>("Shaders/CarveVolume");
            var mipShader = Resources.Load<ComputeShader>("Shaders/GenerateMips");

            var carveField = typeof(Core.ExcavationManager).GetField("carveShader",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            carveField?.SetValue(manager, carveShader);

            var mipField = typeof(Core.ExcavationManager).GetField("mipGenShader",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mipField?.SetValue(manager, mipShader);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        void CreateStratigraphyEvaluator()
        {
            var go = new GameObject("StratigraphyEvaluator");
            var evaluator = go.AddComponent<Stratigraphy.StratigraphyEvaluator>();

            var layersField = typeof(Stratigraphy.StratigraphyEvaluator).GetField("layers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            layersField?.SetValue(evaluator, new System.Collections.Generic.List<Stratigraphy.MaterialLayer>(layers));

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        void CreateExcavationRenderer()
        {
            var go = new GameObject("ExcavationRenderer");
            var renderer = go.AddComponent<Rendering.ExcavationRenderer>();

            // Find the manager and evaluator
            var manager = Object.FindFirstObjectByType<Core.ExcavationManager>();
            var evaluator = Object.FindFirstObjectByType<Stratigraphy.StratigraphyEvaluator>();

            var managerField = typeof(Rendering.ExcavationRenderer).GetField("excavationManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            managerField?.SetValue(renderer, manager);

            var evalField = typeof(Rendering.ExcavationRenderer).GetField("stratigraphy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            evalField?.SetValue(renderer, evaluator);

            // Create material
            var shader = Shader.Find("Excavation/ExcavationRaymarch");
            if (shader != null)
            {
                var material = new Material(shader);
                AssetDatabase.CreateAsset(material, "Assets/ExampleContent/Excavation/ExcavationMaterial.mat");
                
                var matField = typeof(Rendering.ExcavationRenderer).GetField("raymarchMaterial",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                matField?.SetValue(renderer, material);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        void AddDigToolToCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[ExcavationSetupWizard] No main camera found. Dig tool not added.");
                return;
            }

            var tool = camera.gameObject.AddComponent<Tools.DigTool>();

            // Create tool tip
            var tipGO = new GameObject("ToolTip");
            tipGO.transform.SetParent(camera.transform);
            tipGO.transform.localPosition = new Vector3(0, 0, 1);

            // Assign references
            var manager = Object.FindFirstObjectByType<Core.ExcavationManager>();
            var evaluator = Object.FindFirstObjectByType<Stratigraphy.StratigraphyEvaluator>();

            var managerField = typeof(Tools.DigTool).GetField("excavationManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            managerField?.SetValue(tool, manager);

            var evalField = typeof(Tools.DigTool).GetField("stratigraphy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            evalField?.SetValue(tool, evaluator);

            var brushField = typeof(Tools.DigTool).GetField("currentBrush",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            brushField?.SetValue(tool, digBrush);

            var tipField = typeof(Tools.DigTool).GetField("toolTip",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tipField?.SetValue(tool, tipGO.transform);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
