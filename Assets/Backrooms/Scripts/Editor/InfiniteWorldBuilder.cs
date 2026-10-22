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

            // All building sound (fixture hum pool, beds, reverb) lives in one manager.
            var audio = world.AddComponent<Backrooms.Audio.BuildingAudio>();
            audio.streamer = streamer; audio.lights = lights; audio.listener = cam.transform;

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Infinite offices scene built: " + ScenePath);
        }

        // ---------------- Materials ----------------

        const string CC0 = "Assets/Backrooms/Textures/CC0";
        const bool UseScannedTextures = false;

        static Texture2D CC0Tex(string file, bool normal, bool linear)
        {
            string path = CC0 + "/" + file;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!imp) return null;
            bool dirty = false;
            if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (!normal && imp.sRGBTexture == linear) { imp.sRGBTexture = !linear; dirty = true; }
            if (imp.maxTextureSize != 2048) { imp.maxTextureSize = 2048; dirty = true; }
            if (imp.anisoLevel != 8) { imp.anisoLevel = 8; dirty = true; }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Scanned CC0 PBR sets (ambientCG, see Textures/CC0/LICENSES.md): colour, normal, and a mask with
        /// ambient occlusion and smoothness. The yellow striped wallpaper keeps its procedural pattern and
        /// gets the scanned paper's normal and smoothness for real surface texture.
        /// </summary>
        static void ApplyScannedTextures(OfficeBiomeAssets a)
        {
            void Pbr(Material m, string set, bool color, Color tint, float normalScale, float occlusion)
            {
                if (!m) return;
                var col = color ? CC0Tex(set + "_Color.jpg", false, false) : null;
                var nrm = CC0Tex(set + "_Normal.jpg", true, true);
                var mask = CC0Tex(set + "_Mask.png", false, true);
                if (color && col) { m.SetTexture("_BaseMap", col); m.SetColor("_BaseColor", tint); }
                if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
                if (mask)
                {
                    m.SetTexture("_MetallicGlossMap", mask); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 0f);
                    m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", occlusion); m.EnableKeyword("_OCCLUSIONMAP");
                }
                NoSkyReflections(m);
                EditorUtility.SetDirty(m);
            }
            // Carpet: worn beige loop pile, tinted toward the classic brownish office carpet (4 m repeat).
            Pbr(a.carpet, "Carpet", true, new Color(0.66f, 0.55f, 0.37f), 1f, 0.8f);
            // Ceiling tiles: the scan has 6 tiles per repeat; 0.667 tiling makes each tile 0.6 m on our grid.
            Pbr(a.ceilingTile, "CeilingTile", true, new Color(0.93f, 0.9f, 0.8f), 0.8f, 0.7f);
            if (a.ceilingTile) a.ceilingTile.SetTextureScale("_BaseMap", new Vector2(2.4f / 3.6f, 2.4f / 3.6f));
            // Painted plaster walls and tall ceilings keep their cream colour.
            Pbr(a.paintedWall, "Plaster", true, new Color(1f, 0.86f, 0.52f), 0.55f, 1f);
            Pbr(a.ceilingPlaster, "Plaster", true, new Color(0.95f, 0.88f, 0.7f), 0.45f, 1f);
            // Wallpaper: procedural stripes, scanned paper relief.
            Pbr(a.wallpaper, "WallpaperPaper", false, Color.white, 0.6f, 1f);
        }

        /// <summary>
        /// The owner's seamless 2K chevron-stripe wallpaper (Textures/Wallpaper_Chevron_*). One repeat is
        /// 0.6 m square: walls map U over 1.2 m and V over 2.7 m, hence 2 x 4.5. The normal map is derived
        /// from the colour for a faint embossed-paper relief. Falls back to the procedural stripes if missing.
        /// </summary>
        static void ApplyChevronWallpaper(Material m)
        {
            const string dir = "Assets/Backrooms/Textures/";
            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Wallpaper_Chevron_Albedo.png");
            if (!m || !col) return;
            var nrmPath = dir + "Wallpaper_Chevron_Normal.png";
            if (AssetImporter.GetAtPath(nrmPath) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath);
            m.SetTexture("_BaseMap", col);
            m.SetColor("_BaseColor", new Color(0.93f, 0.92f, 0.86f));
            m.SetTextureScale("_BaseMap", new Vector2(2f, 4.5f));
            if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 0.5f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(m);
        }

        /// <summary>Environment (sky) reflections are not occluded indoors, so shiny surfaces would glow in the dark.</summary>
        static void NoSkyReflections(Material m)
        {
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
        }

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
            ApplyChevronWallpaper(a.wallpaper);

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
            a.lampShadeOn.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; a.lampShadeOn.EnableKeyword("_EMISSION");

            // Painted plaster walls and tall plaster ceilings (warm cream, matte), and round dome fittings.
            var plaster = Tex("Plaster_Albedo"); var plasterN = Tex("Plaster_Normal");
            a.paintedWall = Lit("M_PaintedWall", new Color(0.86f, 0.79f, 0.6f), plaster, plasterN, 0.12f, 0.22f);
            a.ceilingPlaster = Lit("M_CeilingPlaster", new Color(0.84f, 0.81f, 0.72f), plaster, plasterN, 0.06f, 0.18f);
            // Unlit opal diffuser: pale, with a faint glow from room light scattering inside it, so a dead fitting never reads as a black hole.
            // Unpowered diffusers: no emission and no sky reflections, so they stay as dark as the ceiling.
            a.domeOff = Lit("M_DomeLens_Off", new Color(0.72f, 0.71f, 0.68f), null, null, 0.25f);
            a.domeOff.DisableKeyword("_EMISSION"); a.domeOff.SetColor("_EmissionColor", Color.black);
            a.domeOff.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            NoSkyReflections(a.domeOff);
            if (a.lensOff) NoSkyReflections(a.lensOff);
            a.domeOn = Lit("M_DomeLens_On", new Color(0.92f, 0.9f, 0.86f), null, null, 0.5f);
            a.domeOn.EnableKeyword("_EMISSION");
            a.domeOn.SetColor("_EmissionColor", new Color(1f, 0.93f, 0.8f) * 3.2f);
            a.domeOn.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; a.domeOn.EnableKeyword("_EMISSION");
            // Warehouse strips: bare cool-white tubes.
            a.stripOn = Lit("M_StripTube_On", new Color(0.95f, 0.97f, 1f), null, null, 0.4f);
            a.stripOn.EnableKeyword("_EMISSION");
            a.stripOn.SetColor("_EmissionColor", new Color(0.86f, 0.93f, 1f) * 4f);
            a.stripOn.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            a.stripOff = Lit("M_StripTube_Off", new Color(0.7f, 0.72f, 0.72f), null, null, 0.3f);
            NoSkyReflections(a.stripOff);
            // Indoors, nothing should pick up the (unoccluded) sky reflection.
            foreach (var m in new[] { a.paintedWall, a.ceilingPlaster, a.lampMetal, a.laminate, a.fixtureHousing })
                if (m) NoSkyReflections(m);
            // One plaster repeat = 2.4 m square. Walls map U over 1.2 m and V over 2.7 m; ceilings are in metres.
            a.paintedWall.SetTextureScale("_BaseMap", new Vector2(0.5f, 1.125f));
            a.ceilingPlaster.SetTextureScale("_BaseMap", new Vector2(1f / 2.4f, 1f / 2.4f));
            // Scanned CC0 textures are off (owner preferred the original look); set to true to try them again.
            if (UseScannedTextures) ApplyScannedTextures(a);
            a.cardboard = Lit("M_Cardboard", new Color(0.72f, 0.52f, 0.32f), Tex("Wood_Albedo"), null, 0.08f);
            CreatePropPrefabs(a);
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            return a;
        }

        // ---------------- Blender props ----------------

        const string ModelFolder = Root + "/Models";
        const string PrefabFolder = Root + "/Prefabs/Props";

        /// <summary>
        /// Wraps each Blender FBX in a prefab: material slots are remapped by name to project materials,
        /// one box collider from the bounds, static flags off (props are instanced into streamed chunks).
        /// </summary>
        static void CreatePropPrefabs(OfficeBiomeAssets a)
        {
            Directory.CreateDirectory(PrefabFolder);
            var plastic = Lit("M_PropPlastic", new Color(0.12f, 0.12f, 0.125f), null, null, 0.35f);
            var glass = Lit("M_PropGlass", new Color(0.95f, 0.92f, 0.85f), null, null, 0.85f);
            var clockFace = Lit("M_ClockFace", new Color(0.92f, 0.91f, 0.87f), null, null, 0.3f);

            GameObject Make(string fbx, string prefabName, Material fabric, Material laminate = null, bool collider = true)
            {
                string path = $"{ModelFolder}/{fbx}.fbx";
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (!imp) { Debug.LogWarning("[Backrooms] Missing prop model " + path); return null; }
                if (imp.globalScale != 1f || imp.isReadable || imp.importAnimation || !imp.useFileScale)
                {
                    imp.globalScale = 1f; imp.useFileScale = true; imp.isReadable = false;
                    imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
                    imp.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                go.name = prefabName;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] ? mats[i].name : "";
                        mats[i] = n.StartsWith("Fabric") ? fabric
                                : n.StartsWith("Wood") ? a.wood
                                : n.StartsWith("Metal") ? a.lampMetal
                                : n.StartsWith("Shade") ? a.lampShadeOff
                                : n.StartsWith("Laminate") ? (laminate ? laminate : a.laminate)
                                : n.StartsWith("Glass") ? glass
                                : plastic;
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                if (collider)
                {
                    var b = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = b.center - go.transform.position; bc.size = b.size;
                }
                string prefabPath = $"{PrefabFolder}/{prefabName}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);
                return prefab;
            }

            var fabrics = a.sofaFabrics;
            var sofas = new List<GameObject>();
            foreach (var model in new[] { "Sofa_Chesterfield", "Sofa_Modern" })
                for (int f = 0; f < fabrics.Length; f++) sofas.Add(Make(model, $"{model}_{f}", fabrics[f]));
            a.sofaPrefabs = sofas.ToArray();
            var chairs = new List<GameObject>();
            for (int f = 0; f < fabrics.Length; f++) chairs.Add(Make("Armchair", $"Armchair_{f}", fabrics[f]));
            a.armchairPrefabs = chairs.ToArray();
            a.floorLampPrefab = Make("FloorLamp", "FloorLamp", a.lampShadeOff);
            a.officeChairPrefab = Make("OfficeChair", "OfficeChair", a.chairFabric);
            a.tableModulePrefab = Make("ConferenceTable_Module", "ConferenceTable_Module", a.chairFabric, null, false);
            a.tableLegPrefab = Make("ConferenceTable_Leg", "ConferenceTable_Leg", a.chairFabric, null, false);
            a.wallClockPrefab = Make("WallClock", "WallClock", a.chairFabric, clockFace, false);
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

        /// <summary>
        /// A bare renderer (no SSAO, no camcorder pass) for the OSD camera, added to every URP asset's
        /// renderer list. Returns its index.
        /// </summary>
        static int EnsureUIRenderer()
        {
            const string path = "Assets/Settings/OSD_Renderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!data)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, path);
                AssetDatabase.SaveAssets();
            }
            int index = -1;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                int found = -1;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) found = i;
                if (found < 0)
                {
                    list.arraySize++;
                    found = list.arraySize - 1;
                    list.GetArrayElementAtIndex(found).objectReferenceValue = data;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(asset);
                }
                if (asset == UniversalRenderPipeline.asset) index = found;
                else if (index < 0) index = found;
            }
            AssetDatabase.SaveAssets();
            return index;
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
            // Listener filters run in component order: room reverb, then the camcorder recording chain.
            camGo.AddComponent<AudioReverbFilter>().reverbPreset = AudioReverbPreset.Off;
            camGo.AddComponent<Backrooms.Audio.CamcorderAudioChain>();
            player.AddComponent<Backrooms.Audio.PlayerAudio>();
            var shake = camGo.AddComponent<HandheldCameraShake>();
            shake.player = ctrl;
            var exposure = camGo.AddComponent<CamcorderAutoExposure>();
            exposure.volume = volume;

            var ui = new GameObject("CamcorderUI");
            var osd = ui.AddComponent<CamcorderOSD>();
            osd.uiRendererIndex = EnsureUIRenderer();
            var rig = camGo.AddComponent<CamcorderRig>();
            rig.volume = volume; rig.exposure = exposure; rig.shake = shake; rig.osd = osd;
            var menu = ui.AddComponent<CamcorderVisualMenu>();
            menu.rig = rig; menu.player = ctrl;
            return (player, cam);
        }
    }
}
