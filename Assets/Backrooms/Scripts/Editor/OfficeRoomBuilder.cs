using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Builds the first-milestone scene: one modular office room with a suspended tile ceiling,
    /// half-working fluorescent troffers, camcorder player and post-processing.
    /// Menu: Backrooms/Build Office Room Scene. Re-running rebuilds the scene from scratch.
    /// </summary>
    public static class OfficeRoomBuilder
    {
        const string Root = "Assets/Backrooms";
        const string ScenePath = Root + "/Scenes/OfficeRoom_Prototype.unity";
        const string MatFolder = Root + "/Materials";
        const string MeshFolder = Root + "/Meshes";
        const string SettingsFolder = Root + "/Settings";

        // Room layout (metres). Interior spans X [-3.6, 3.6], Z [-3.0, 3.0]; 0.6 m tile grid.
        static readonly Vector2 Interior = new Vector2(7.2f, 6.0f);
        const float CeilingY = 2.7f;
        const float WallThickness = 0.15f;
        const float BaseboardHeight = 0.1f, BaseboardDepth = 0.014f;
        const float TBarWidth = 0.024f, TBarDrop = 0.012f;

        // Troffers: 1.2 x 0.6 m, long axis along X. "on" = working tube.
        static readonly (Vector2 pos, bool on)[] Fixtures =
        {
            (new Vector2(-2.4f, -1.5f), true),  (new Vector2(-2.4f, 1.5f), true),
            (new Vector2( 0.0f, -1.5f), true),  (new Vector2( 0.0f, 1.5f), false),
            (new Vector2( 2.4f, -1.5f), false), (new Vector2( 2.4f, 1.5f), false),
        };

        static Color FluorescentColor => new Color(1.0f, 0.965f, 0.86f);

        [MenuItem("Backrooms/Build Office Room Scene")]
        public static void Build()
        {
            foreach (var f in new[] { Root + "/Scenes", MatFolder, MeshFolder, SettingsFolder }) Directory.CreateDirectory(f);
            BackroomsTextureGenerator.GenerateAll();

            var mats = CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureEnvironment();
            var room = BuildRoom(mats);
            var volume = BuildPostProcessing();
            BuildPlayer(volume);
            BuildAudio(room.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Office room scene built: " + ScenePath + " (baking bounce lighting...)");
            Bake();
        }

        [MenuItem("Backrooms/Bake Office Room Lighting")]
        public static void Bake()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            if (!Lightmapping.BakeAsync()) Debug.LogWarning("[Backrooms] Lightmap bake could not start.");
        }

        static void OnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[Backrooms] Lighting baked and scene saved.");
        }

        // ---------------- Materials ----------------
        class Mats { public Material carpet, wall, ceiling, tbar, baseboard, lensOn, lensOff, housing; }

        static Mats CreateMaterials()
        {
            string T(string n) => BackroomsTextureGenerator.Folder + "/" + n + ".png";
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(T(n));

            var m = new Mats();
            m.carpet = Lit("M_Carpet", Color.white, Tex("Carpet_Albedo"), Tex("Carpet_Normal"), 0.6f, 0.08f);
            m.carpet.SetTexture("_DetailAlbedoMap", Tex("CarpetDetail_Albedo"));
            m.carpet.SetTexture("_DetailNormalMap", Tex("CarpetDetail_Normal"));
            float detailTiling = BackroomsTextureGenerator.CarpetBaseMeters / BackroomsTextureGenerator.CarpetDetailMeters;
            m.carpet.SetTextureScale("_DetailAlbedoMap", Vector2.one * detailTiling);
            m.carpet.SetFloat("_DetailAlbedoMapScale", 1f);
            m.carpet.SetFloat("_DetailNormalMapScale", 0.8f);
            m.carpet.EnableKeyword("_DETAIL_MULX2");

            m.wall = Lit("M_Wallpaper", Color.white, Tex("Wallpaper_Albedo"), Tex("Wallpaper_Normal"), 0.35f, 0.18f);
            m.ceiling = Lit("M_CeilingTile", Color.white, Tex("CeilingTile_Albedo"), Tex("CeilingTile_Normal"), 0.7f, 0.04f);
            m.tbar = Lit("M_CeilingGrid", new Color(0.82f, 0.81f, 0.77f), null, null, 0f, 0.32f);
            m.baseboard = Lit("M_Baseboard", new Color(0.17f, 0.145f, 0.12f), null, null, 0f, 0.28f);
            m.housing = Lit("M_FixtureHousing", new Color(0.80f, 0.80f, 0.77f), null, null, 0f, 0.38f);
            m.lensOff = Lit("M_FixtureLens_Off", new Color(0.62f, 0.63f, 0.61f), null, null, 0f, 0.45f);

            m.lensOn = Lit("M_FixtureLens_On", new Color(0.85f, 0.85f, 0.82f), null, null, 0f, 0.45f);
            m.lensOn.EnableKeyword("_EMISSION");
            m.lensOn.SetColor("_EmissionColor", FluorescentColor * 2.4f);
            m.lensOn.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(m.lensOn);
            return m;
        }

        static Material Lit(string name, Color color, Texture2D albedo, Texture2D normal, float normalScale, float smoothness)
        {
            string path = $"{MatFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_EnvironmentReflections", 1f);
            if (normal)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", normalScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------- Environment ----------------
        static void ConfigureEnvironment()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.010f, 0.010f, 0.008f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = false;

            string lsPath = SettingsFolder + "/OfficeRoom_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls) { ls = new LightingSettings { name = "OfficeRoom_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 24f;
            ls.lightmapPadding = 4;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.maxBounces = 3;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            ls.indirectScale = 1f;
            ls.albedoBoost = 1f;
            Lightmapping.lightingSettings = ls;
        }

        // ---------------- Room ----------------
        static GameObject BuildRoom(Mats m)
        {
            var room = new GameObject("OfficeRoom_A");
            var module = room.AddComponent<RoomModule>();
            module.interiorSize = Interior;
            module.ceilingHeight = CeilingY;
            module.wallThickness = WallThickness;

            float hx = Interior.x * 0.5f, hz = Interior.y * 0.5f;

            // Floor: world-space UVs in carpet repeats.
            var floor = Child(room, "Floor");
            var floorMesh = SaveMesh("Floor_7.2x6", PlaneMesh(Interior, BackroomsTextureGenerator.CarpetBaseMeters, true));
            Render(floor, floorMesh, m.carpet, true);
            var floorCol = floor.AddComponent<BoxCollider>();
            floorCol.center = new Vector3(0f, -0.25f, 0f);
            floorCol.size = new Vector3(Interior.x + 1f, 0.5f, Interior.y + 1f);

            // Ceiling tiles (render-only plane facing down) and its collider.
            var ceil = Child(room, "CeilingTiles");
            ceil.transform.localPosition = new Vector3(0f, CeilingY, 0f);
            var ceilMesh = SaveMesh("Ceiling_7.2x6", PlaneMesh(Interior, BackroomsTextureGenerator.CeilingMeters, false));
            Render(ceil, ceilMesh, m.ceiling, true);
            var ceilCol = ceil.AddComponent<BoxCollider>();
            ceilCol.center = new Vector3(0f, 0.25f, 0f);
            ceilCol.size = new Vector3(Interior.x + 1f, 0.5f, Interior.y + 1f);

            BuildCeilingGrid(room, m);
            BuildWalls(room, m, hx, hz);
            BuildFixtures(room, m);
            BuildReflectionProbe(room);
            return room;
        }

        static void BuildWalls(GameObject room, Mats m, float hx, float hz)
        {
            var walls = Child(room, "Walls");
            var segMesh = SaveMesh("WallSegment_1.2x2.7", WallQuad(RoomModule.WallSegmentWidth, CeilingY));
            var baseMesh = SaveMesh("Baseboard_1.2", BoxMesh(new Vector3(RoomModule.WallSegmentWidth, BaseboardHeight, BaseboardDepth), 1f));

            // Each wall: inward-facing direction and run length.
            var sides = new (string name, Vector3 origin, float yaw, float length)[]
            {
                ("North", new Vector3(0f, 0f,  hz), 180f, Interior.x),
                ("South", new Vector3(0f, 0f, -hz),   0f, Interior.x),
                ("East",  new Vector3( hx, 0f, 0f), -90f, Interior.y),
                ("West",  new Vector3(-hx, 0f, 0f),  90f, Interior.y),
            };
            var rng = new System.Random(1979);
            foreach (var s in sides)
            {
                var wall = Child(walls, "Wall_" + s.name);
                wall.transform.localPosition = s.origin;
                wall.transform.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
                int count = Mathf.RoundToInt(s.length / RoomModule.WallSegmentWidth);
                for (int i = 0; i < count; i++)
                {
                    float x = -s.length * 0.5f + (i + 0.5f) * RoomModule.WallSegmentWidth;
                    var seg = Child(wall, $"WallSegment_{i:00}");
                    seg.transform.localPosition = new Vector3(x, 0f, 0f);
                    var r = Render(seg, segMesh, m.wall, true);
                    // Slight per-segment tint so the 1.2 m repeat never reads as identical.
                    var mpb = new MaterialPropertyBlock();
                    float k = 1f + ((float)rng.NextDouble() - 0.5f) * 0.015f;
                    float warm = ((float)rng.NextDouble() - 0.5f) * 0.01f;
                    mpb.SetColor("_BaseColor", new Color(k + warm, k, k - warm, 1f));
                    r.SetPropertyBlock(mpb);
                    var col = seg.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, CeilingY * 0.5f, -WallThickness * 0.5f);
                    col.size = new Vector3(RoomModule.WallSegmentWidth, CeilingY + 0.4f, WallThickness);

                    var bb = Child(seg, "Baseboard");
                    bb.transform.localPosition = new Vector3(0f, BaseboardHeight * 0.5f, BaseboardDepth * 0.5f);
                    Render(bb, baseMesh, m.baseboard, true);
                }
                // Corner blocks: walls are 1.2 m modules that butt at corners, so no gaps to seal.
            }
        }

        static void BuildCeilingGrid(GameObject room, Mats m)
        {
            var grid = Child(room, "CeilingGrid");
            grid.transform.localPosition = new Vector3(0f, CeilingY - TBarDrop * 0.5f, 0f);
            var mainX = SaveMesh("TBar_X", BoxMesh(new Vector3(Interior.x, TBarDrop, TBarWidth), 1f));
            float g = RoomModule.GridUnit;
            for (float z = -Interior.y * 0.5f; z <= Interior.y * 0.5f + 0.001f; z += g)
            {
                var bar = Child(grid, $"TBarX_{z:0.0}");
                bar.transform.localPosition = new Vector3(0f, 0f, z);
                Render(bar, mainX, m.tbar, true, ShadowCastingMode.Off);
            }
            // Cross tees: one 0.6 m piece per cell between main runners, skipped where a troffer sits.
            var tee = SaveMesh("TBar_Cross_0.6", BoxMesh(new Vector3(TBarWidth, TBarDrop, g), 1f));
            int nz = Mathf.RoundToInt(Interior.y / g);
            for (float x = -Interior.x * 0.5f; x <= Interior.x * 0.5f + 0.001f; x += g)
            for (int iz = 0; iz < nz; iz++)
            {
                float z = -Interior.y * 0.5f + (iz + 0.5f) * g;
                bool covered = false;
                foreach (var (pos, _) in Fixtures)
                    if (Mathf.Abs(x - pos.x) < 0.59f && Mathf.Abs(z - pos.y) < 0.29f) covered = true;
                if (covered) continue;
                var bar = Child(grid, $"TBarZ_{x:0.0}_{iz}");
                bar.transform.localPosition = new Vector3(x, 0f, z);
                Render(bar, tee, m.tbar, true, ShadowCastingMode.Off);
            }
        }

        static void BuildFixtures(GameObject room, Mats m)
        {
            var parent = Child(room, "Fixtures");
            var lensMesh = SaveMesh("TrofferLens_1.2x0.6", BoxMesh(new Vector3(1.17f, 0.006f, 0.57f), 1f));
            var rimLong = SaveMesh("TrofferRim_Long", BoxMesh(new Vector3(1.2f, 0.016f, 0.018f), 1f));
            var rimShort = SaveMesh("TrofferRim_Short", BoxMesh(new Vector3(0.018f, 0.016f, 0.6f), 1f));
            int idx = 0;
            foreach (var (pos, on) in Fixtures)
            {
                var fx = Child(parent, $"Troffer_{idx++:00}_{(on ? "On" : "Off")}");
                fx.transform.localPosition = new Vector3(pos.x, CeilingY, pos.y);

                var lens = Child(fx, "Lens");
                lens.transform.localPosition = new Vector3(0f, -0.004f, 0f);
                Render(lens, lensMesh, on ? m.lensOn : m.lensOff, true, ShadowCastingMode.Off);

                foreach (var (mesh, off) in new[] {
                    (rimLong, new Vector3(0f, -0.009f, 0.291f)), (rimLong, new Vector3(0f, -0.009f, -0.291f)),
                    (rimShort, new Vector3(0.591f, -0.009f, 0f)), (rimShort, new Vector3(-0.591f, -0.009f, 0f)) })
                {
                    var rim = Child(fx, "Rim");
                    rim.transform.localPosition = off;
                    Render(rim, mesh, m.housing, true, ShadowCastingMode.Off);
                }

                if (!on) continue;
                var lightGo = Child(fx, "TubeLight");
                lightGo.transform.localPosition = new Vector3(0f, -0.06f, 0f);
                lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = FluorescentColor;
                light.intensity = 9f;
                light.range = 5.5f;
                light.spotAngle = 140f;
                light.innerSpotAngle = 55f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.92f;
                light.shadowBias = 0.02f;
                light.shadowNormalBias = 0.3f;
                light.shadowNearPlane = 0.1f;
                light.lightmapBakeType = LightmapBakeType.Mixed;
                light.bounceIntensity = 0.9f;
                light.shapeRadius = 0.35f; // soft baked shadows from a 1.2 m tube array
                var add = light.GetUniversalAdditionalLightData();
                add.softShadowQuality = SoftShadowQuality.High;

                var hum = Child(fx, "Hum");
                hum.transform.localPosition = new Vector3(0f, -0.05f, 0f);
                var src = hum.AddComponent<AudioSource>();
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 0.9f;
                src.maxDistance = 12f;
                src.dopplerLevel = 0f;
                src.playOnAwake = false;
                var amb = hum.AddComponent<ProceduralAmbience>();
                amb.kind = ProceduralAmbience.Kind.FluorescentHum;
                amb.volume = 0.015f;
                amb.seed = idx * 17;
            }
        }

        static void BuildReflectionProbe(GameObject room)
        {
            var go = Child(room, "ReflectionProbe");
            go.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Baked;
            p.boxProjection = true;
            p.size = new Vector3(Interior.x, CeilingY, Interior.y);
            p.center = Vector3.zero;
            p.resolution = 256;
            p.clearFlags = ReflectionProbeClearFlags.SolidColor;
            p.backgroundColor = Color.black;
        }

        // ---------------- Post processing ----------------
        static Volume BuildPostProcessing()
        {
            string path = SettingsFolder + "/Camcorder_PostProfile.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);

            var color = Add<ColorAdjustments>();
            color.postExposure.Override(0f);
            color.contrast.Override(18f);
            color.saturation.Override(0f);
            color.colorFilter.Override(new Color(1f, 0.99f, 0.93f));

            var wb = Add<WhiteBalance>();
            wb.temperature.Override(9f);
            wb.tint.Override(-5f);   // faint green cast typical of fluorescent tubes on video

            var lift = Add<LiftGammaGain>();
            lift.lift.Override(new Vector4(1f, 1f, 1f, 0.004f)); // camcorder blacks never quite reach black

            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.32f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(true);

            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.28f);
            grain.response.Override(0.75f);

            var vig = Add<Vignette>();
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);

            var ca = Add<ChromaticAberration>();
            ca.intensity.Override(0.07f);

            var lens = Add<LensDistortion>();
            lens.intensity.Override(-0.07f);
            lens.scale.Override(1.015f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Camcorder_PostVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
            return vol;
        }

        // ---------------- Player ----------------
        static void BuildPlayer(Volume volume)
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(-2.6f, 0.02f, 0.2f);
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // facing the dark side
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.25f;
            cc.skinWidth = 0.03f;
            cc.slopeLimit = 40f;
            var ctrl = player.AddComponent<CamcorderPlayerController>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.58f, 0f);
            ctrl.cameraPitchPivot = pivot.transform;

            var camGo = new GameObject("Camcorder");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            camGo.AddComponent<AudioListener>();

            var shake = camGo.AddComponent<HandheldCameraShake>();
            shake.player = ctrl;
            var exposure = camGo.AddComponent<CamcorderAutoExposure>();
            exposure.volume = volume;
        }

        // ---------------- Audio ----------------
        static void BuildAudio(Transform room)
        {
            var tone = new GameObject("RoomTone");
            tone.transform.SetParent(room, false);
            tone.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var src = tone.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            var amb = tone.AddComponent<ProceduralAmbience>();
            amb.kind = ProceduralAmbience.Kind.RoomTone;
            amb.volume = 0.03f;
            amb.seed = 7;

            var reverb = tone.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = AudioReverbPreset.Room;
            reverb.minDistance = 6f;
            reverb.maxDistance = 9f;
        }

        // ---------------- Helpers ----------------
        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static MeshRenderer Render(GameObject go, Mesh mesh, Material mat, bool isStatic, ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows;
            r.receiveGI = ReceiveGI.Lightmaps;
            if (isStatic)
                GameObjectUtility.SetStaticEditorFlags(go,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return r;
        }

        static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name;
            Unwrapping.GenerateSecondaryUVSet(mesh);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Horizontal plane centred on the origin. UVs are world metres / uvMeters.</summary>
        static Mesh PlaneMesh(Vector2 size, float uvMeters, bool faceUp)
        {
            // Subdivide on the 0.6 m grid so lighting interpolation and lightmap texels stay even.
            int nx = Mathf.RoundToInt(size.x / RoomModule.GridUnit), nz = Mathf.RoundToInt(size.y / RoomModule.GridUnit);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                var p = new Vector3(-size.x * 0.5f + x * RoomModule.GridUnit, 0f, -size.y * 0.5f + z * RoomModule.GridUnit);
                v.Add(p);
                uv.Add(new Vector2(p.x / uvMeters, p.z / uvMeters));
            }
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * (nx + 1) + x;
                if (faceUp) tri.AddRange(new[] { i, i + nx + 1, i + 1, i + 1, i + nx + 1, i + nx + 2 });
                else tri.AddRange(new[] { i, i + 1, i + nx + 1, i + 1, i + nx + 2, i + nx + 1 });
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            return m;
        }

        /// <summary>Wall face spanning x [-w/2, w/2], y [0, h], facing +Z. UV covers the full texture.</summary>
        static Mesh WallQuad(float w, float h)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w * 0.5f, 0, 0), new Vector3(w * 0.5f, 0, 0), new Vector3(-w * 0.5f, h, 0), new Vector3(w * 0.5f, h, 0) };
            m.uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            m.RecalculateNormals();
            return m;
        }

        /// <summary>Axis-aligned box centred on the origin, per-face UVs in metres / uvMeters.</summary>
        static Mesh BoxMesh(Vector3 size, float uvMeters)
        {
            var h = size * 0.5f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Face(Vector3 normal, Vector3 up)
            {
                Vector3 right = Vector3.Cross(normal, up);
                Vector3 c = Vector3.Scale(normal, h);
                Vector3 r = Vector3.Scale(right, h), u = Vector3.Scale(up, h);
                int s = v.Count;
                v.Add(c - r - u); v.Add(c + r - u); v.Add(c - r + u); v.Add(c + r + u);
                float rw = r.magnitude * 2f / uvMeters, uh = u.magnitude * 2f / uvMeters;
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(rw, 0)); uv.Add(new Vector2(0, uh)); uv.Add(new Vector2(rw, uh));
                for (int i = 0; i < 4; i++) n.Add(normal);
                t.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3 });
            }
            Face(Vector3.forward, Vector3.up); Face(Vector3.back, Vector3.up);
            Face(Vector3.right, Vector3.up); Face(Vector3.left, Vector3.up);
            Face(Vector3.up, Vector3.forward); Face(Vector3.down, Vector3.back);
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            return m;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
