using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Builds the Reference Match scene: a copy of the infinite scene's player, camera, post volume and UI, with the
    /// procedural world replaced by the hand-built <see cref="ReferenceRoom"/> (see Docs/ReferenceMatch.md).
    /// The scene stores only the room's parameters and materials; the geometry is built when the scene starts.
    /// </summary>
    public static class ReferenceMatchBuilder
    {
        const string ScenePath = "Assets/Backrooms/Scenes/ReferenceMatch.unity";
        const string SourceScene = "Assets/Backrooms/Scenes/Backrooms_Infinite.unity";
        const string MatFolder = "Assets/Backrooms/Materials/ReferenceMatch";
        const string TexFolder = "Assets/Backrooms/Textures";

        [MenuItem("Backrooms/Build Reference Match Scene")]
        public static void BuildFinal() => Build(false);

        [MenuItem("Backrooms/Build Reference Match Scene (baseline: game materials)")]
        public static void BuildBaseline() => Build(true);

        public static void Build(bool baseline)
        {
            EditorSceneManager.SaveOpenScenes();
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) AssetDatabase.CopyAsset(SourceScene, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var n in new[] { "World", "ReferenceRoom", "ReferenceMatch" })
            {
                var go = GameObject.Find(n);
                if (go) Object.DestroyImmediate(go);
            }
            System.IO.Directory.CreateDirectory(MatFolder);
            InfiniteWorldBuilder.EnsureFrameHold();
            BackroomsTextureGenerator.GenerateReferenceMatch();
            var assets = AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");

            var room = new GameObject("ReferenceRoom").AddComponent<ReferenceRoom>();
            room.panel = assets.lensOn;
            if (baseline)
            {
                room.wall = room.olive = assets.wallpaper; room.carpet = assets.carpet; room.trim = assets.baseboard; room.ceilingTile = assets.ceilingTile; room.recess = assets.baseboard;
                room.baseboard = true; room.trimHeight = 0.1f; room.trimDepth = 0.014f;
            }
            else
            {
                Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}/{n}.png");
                // Walls: the game's own wallpaper (albedo, normal and detail maps) tinted to the reference's pale cream.
                // The tint is slightly above 1 because the wallpaper texture itself is darker and yellower.
                room.wall = Tinted("M_Ref_Wall", assets.wallpaper, WallTint);
                room.olive = Tinted("M_Ref_WallOlive", assets.wallpaper, OliveTint);
                room.carpet = Lit("M_Ref_Carpet", CarpetTint, T("RefCarpet_Albedo"), 0.05f, T("RefCarpet_Normal"), 0.6f);
                room.trim = null;                                        // no baseboard (ReferenceRoom.baseboard is off)
                room.ceilingTile = Lit("M_Ref_Ceiling", CeilingTint, T("RefCeiling_Albedo"), 0.05f, T("RefCeiling_Normal"), 1f);
                room.recess = Lit("M_Ref_Recess", new Color(0.35f, 0.31f, 0.12f), null, 0.05f);
            }

            var rm = new GameObject("ReferenceMatch").AddComponent<ReferenceMatch>();
            rm.presetIndex = baseline ? 0 : 7;
            rm.position = new Vector3(0.4f, 0f, 0f);
            RenderSettings.fog = false;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Reference Match scene built (" + (baseline ? "baseline" : "tuned") + ").");
        }

        // Carpet colour on top of RefCarpet_Albedo (whose mean is about 0.72): the tuned tan of the first match.
        public static readonly Color CarpetTint = new Color(0.80f, 0.71f, 0.44f);
        public static readonly Color CeilingTint = new Color(0.62f, 0.62f, 0.34f);
        public static readonly Color WallTint = new Color(1.075f, 1.04f, 1.10f);
        public static readonly Color OliveTint = new Color(0.877f, 0.913f, 0.298f);

        /// <summary>A copy of a game material (all its maps and settings) with a different base colour.</summary>
        public static Material Tinted(string name, Material source, Color c)
        {
            string path = $"{MatFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
            m.shader = source.shader;
            m.CopyPropertiesFromMaterial(source);
            m.shaderKeywords = source.shaderKeywords;
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetShaderPassEnabled("MotionVectors", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Lit(string name, Color c, Texture2D tex, float smooth, Texture2D normal = null, float normalScale = 1f)
        {
            string path = $"{MatFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", normalScale);
            if (normal) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetShaderPassEnabled("MotionVectors", false);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
