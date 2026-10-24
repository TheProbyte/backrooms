using System.Collections.Generic;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Backrooms.EditorTools
{
    /// <summary>Creates the Build Mode scene (a camera and the builder: everything else is made at run time) and lists the game's scenes in the build settings.</summary>
    public static class BuildModeSceneBuilder
    {
        const string ScenePath = "Assets/Backrooms/Scenes/BuildMode.unity";

        [MenuItem("Backrooms/Build Build-Mode Scene")]
        public static void Build()
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.29f, 0.22f);
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.fog = false;

            var camGo = new GameObject("BuilderCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.012f, 0.012f, 0.016f);
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            camGo.transform.position = new Vector3(5f, 9f, -4f);
            camGo.transform.rotation = Quaternion.Euler(52f, 25f, 0f);

            var app = new GameObject("BuilderApp").AddComponent<BuilderApp>();
            app.assets = AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            if (!app.assets) Debug.LogError("[Backrooms] OfficeBiome_Assets not found: assign it on BuilderApp.");

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildSettings();
            Debug.Log("[Backrooms] Build Mode scene created: " + ScenePath);
        }

        /// <summary>The procedural world first (it is what starts), then the reference room and build mode.</summary>
        public static void EnsureBuildSettings()
        {
            var wanted = new[]
            {
                "Assets/Backrooms/Scenes/Backrooms_Infinite.unity",
                "Assets/Backrooms/Scenes/ReferenceMatch.unity",
                ScenePath,
            };
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var path in wanted)
            {
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) continue;
                int i = list.FindIndex(s => s.path == path);
                if (i < 0) list.Add(new EditorBuildSettingsScene(path, true));
                else if (!list[i].enabled) list[i] = new EditorBuildSettingsScene(path, true);
            }
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
