using System.Collections.Generic;
using System.IO;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Builds the infinite office-biome scene: world streamer, pooled lights, camcorder player with the
    /// F5 visual menu, OSD and the full-screen video pass. Menu: Backrooms/Build Infinite Offices Scene.
    /// </summary>
    public static class InfiniteWorldBuilder
    {
        const string Root = "Assets/Backrooms";
        const string ScenePath = Root + "/Scenes/Backrooms_Infinite.unity";
        const string MatFolder = Root + "/Materials";
        const string SettingsFolder = Root + "/Settings";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        [MenuItem("Backrooms/Build Infinite Offices Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            if (!File.Exists(BackroomsTextureGenerator.Folder + "/Wallpaper_Albedo.png")) BackroomsTextureGenerator.GenerateAll();
            else BackroomsTextureGenerator.GenerateProps();

            var assets = CreateBiomeAssets();
            EnsureVideoPass();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureEnvironment();
            var volume = BuildVolume();
            var (player, cam) = BuildPlayer(volume);

            var world = new GameObject("World");
            var lights = world.AddComponent<FixtureLightManager>();
            lights.viewer = cam.transform;
            var streamer = world.AddComponent<WorldStreamer>();
            streamer.assets = assets;
            streamer.player = player.transform;
            streamer.lightManager = lights;

            var tone = new GameObject("RoomTone");
            var src = tone.AddComponent<AudioSource>();
            src.spatialBlend = 0f; src.playOnAwake = false;
            var amb = tone.AddComponent<ProceduralAmbience>();
            amb.kind = ProceduralAmbience.Kind.RoomTone; amb.volume = 0.03f; amb.seed = 7;

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Infinite offices scene built: " + ScenePath);
        }

        // ---------------- Materials ----------------

        static OfficeBiomeAssets CreateBiomeAssets()
        {
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(BackroomsTextureGenerator.Folder + "/" + n + ".png");
            Material Existing(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{n}.mat");


            var a = LoadOrCreate<OfficeBiomeAssets>(SettingsFolder + "/OfficeBiome_Assets.asset");
            a.carpet = Existing("M_Carpet");
            a.wallpaper = Existing("M_Wallpaper");
            a.ceilingTile = Existing("M_CeilingTile");
            a.ceilingGrid = Existing("M_CeilingGrid");
            a.baseboard = Existing("M_Baseboard");
            a.lensOn = Existing("M_FixtureLens_On");
            a.lensOff = Existing("M_FixtureLens_Off");
            a.fixtureHousing = Existing("M_FixtureHousing");
            // Procedural chunks have no lightmaps: tiles and walls are textured in world space, so 1:1 tiling.
            foreach (var m in new[] { a.carpet, a.wallpaper, a.ceilingTile })
            {
                m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            var plate = Lit("M_WallPlate", Color.white, Tex("WallPlate_Atlas"), null, 0.42f);
            a.wallPlate = plate;
            a.halfWallCap = Lit("M_HalfWallCap", new Color(0.36f, 0.30f, 0.23f), Tex("Wood_Albedo"), null, 0.3f);

            var fabricTex = Tex("Fabric_Albedo"); var fabricN = Tex("Fabric_Normal");
            a.sofaFabrics = new[]
            {
                Lit("M_Fabric_Mustard", new Color(0.62f, 0.50f, 0.30f), fabricTex, fabricN, 0.08f, 0.6f),
                Lit("M_Fabric_Teal", new Color(0.30f, 0.42f, 0.40f), fabricTex, fabricN, 0.08f, 0.6f),
                Lit("M_Fabric_Rose", new Color(0.55f, 0.36f, 0.34f), fabricTex, fabricN, 0.08f, 0.6f),
            };
            a.chairFabric = Lit("M_Fabric_Charcoal", new Color(0.24f, 0.24f, 0.25f), fabricTex, fabricN, 0.1f, 0.6f);
            a.wood = Lit("M_WoodDark", new Color(0.45f, 0.36f, 0.28f), Tex("Wood_Albedo"), null, 0.35f);
            a.laminate = Lit("M_TableLaminate", new Color(1f, 0.96f, 0.9f), Tex("Wood_Albedo"), null, 0.48f);
            a.lampMetal = Lit("M_LampMetal", new Color(0.22f, 0.2f, 0.18f), null, null, 0.55f);
            a.lampMetal.SetFloat("_Metallic", 0.6f);
            a.lampShadeOff = Lit("M_LampShade_Off", new Color(0.82f, 0.76f, 0.62f), fabricTex, null, 0.1f);
            a.lampShadeOn = Lit("M_LampShade_On", new Color(0.9f, 0.82f, 0.66f), fabricTex, null, 0.1f);
            a.lampShadeOn.EnableKeyword("_EMISSION");
            a.lampShadeOn.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.42f) * 1.6f);
            a.lampShadeOn.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            return a;
        }

        static Material Lit(string name, Color color, Texture2D albedo, Texture2D normal, float smoothness, float normalScale = 1f)
        {
            string path = $"{MatFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            if (normal) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", normalScale); mat.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ---------------- Video pass ----------------

        /// <summary>Adds the camcorder full-screen pass to the PC renderer (after post-processing).</summary>
        static void EnsureVideoPass()
        {
            var shader = Shader.Find("Backrooms/CamcorderVideo");
            if (!shader) { Debug.LogError("[Backrooms] CamcorderVideo shader missing."); return; }
            string matPath = MatFolder + "/M_CamcorderVideo.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!data) { Debug.LogError("[Backrooms] PC_Renderer not found."); return; }
            // The pass reads the camera colour; guarantee an intermediate texture even if SSAO is turned off.
            var rso = new SerializedObject(data);
            var mode = rso.FindProperty("m_IntermediateTextureMode");
            if (mode != null && mode.intValue != 1) { mode.intValue = 1; rso.ApplyModifiedPropertiesWithoutUndo(); }
            foreach (var f in data.rendererFeatures)
                if (f is FullScreenPassRendererFeature fs && fs.name == "CamcorderVideo") { fs.passMaterial = mat; EditorUtility.SetDirty(data); return; }

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "CamcorderVideo";
            feature.passMaterial = mat;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.Color;
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            var so = new SerializedObject(data);
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId))
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        // ---------------- Scene ----------------

        static void ConfigureEnvironment()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.016f, 0.015f, 0.012f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.3f;
            // Darkness swallows anything far away, so the outer ring of generation is never visible.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.008f, 0.007f, 0.005f);
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 34f;
            string lsPath = SettingsFolder + "/Infinite_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls) { ls = new LightingSettings { name = "Infinite_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = false; ls.realtimeGI = false;
            Lightmapping.lightingSettings = ls;

            // Enough shadow atlas for the pooled near lights (spots at medium tier, a couple of lamp cubes).
            var urp = UniversalRenderPipeline.asset;
            if (urp)
            {
                var so = new SerializedObject(urp);
                var res = so.FindProperty("m_AdditionalLightsShadowmapResolution");
                if (res != null && res.intValue < 4096) { res.intValue = 4096; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        static Volume BuildVolume()
        {
            string path = SettingsFolder + "/Infinite_PostProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path)) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            void Add<T>() where T : VolumeComponent { var c = profile.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, profile); }
            Add<Tonemapping>(); Add<ColorAdjustments>(); Add<WhiteBalance>(); Add<LiftGammaGain>(); Add<Bloom>(); Add<FilmGrain>();
            Add<Vignette>(); Add<ChromaticAberration>(); Add<LensDistortion>(); Add<DepthOfField>(); Add<MotionBlur>();
            // Neutral defaults; CamcorderRig drives every value from the user's settings at runtime.
            profile.TryGet(out Tonemapping tone); tone.mode.Override(TonemappingMode.Neutral);
            profile.TryGet(out ColorAdjustments ca); ca.postExposure.Override(0f); ca.contrast.Override(18f);
            profile.TryGet(out Bloom bloom); bloom.intensity.Override(0.32f); bloom.highQualityFiltering.Override(true);
            profile.TryGet(out DepthOfField dof); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(4f); dof.aperture.Override(4f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Camcorder_PostVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 10f; vol.sharedProfile = profile;
            return vol;
        }

        static (GameObject player, Camera cam) BuildPlayer(Volume volume)
        {
            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.25f; cc.skinWidth = 0.03f; cc.slopeLimit = 40f;
            var ctrl = player.AddComponent<CamcorderPlayerController>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.58f, 0f);
            ctrl.cameraPitchPivot = pivot.transform;

            var camGo = new GameObject("Camcorder") { tag = "MainCamera" };
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 45f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<AudioListener>();
            var shake = camGo.AddComponent<HandheldCameraShake>();
            shake.player = ctrl;
            var exposure = camGo.AddComponent<CamcorderAutoExposure>();
            exposure.volume = volume;

            var ui = new GameObject("CamcorderUI");
            var osd = ui.AddComponent<CamcorderOSD>();
            var rig = camGo.AddComponent<CamcorderRig>();
            rig.volume = volume; rig.exposure = exposure; rig.shake = shake; rig.osd = osd;
            var menu = ui.AddComponent<CamcorderVisualMenu>();
            menu.rig = rig; menu.player = ctrl;
            return (player, cam);
        }
    }
}
