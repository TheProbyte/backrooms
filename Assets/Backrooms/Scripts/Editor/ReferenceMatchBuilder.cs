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
            BackroomsTextureGenerator.GenerateReferenceMatch();
            var assets = AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");

            var room = new GameObject("ReferenceRoom").AddComponent<ReferenceRoom>();
            room.panel = assets.lensOn;
            if (baseline)
            {
                room.wall = room.olive = assets.wallpaper; room.carpet = assets.carpet; room.trim = assets.baseboard; room.ceilingTile = assets.ceilingTile; room.recess = assets.baseboard;
                room.trimHeight = 0.1f; room.trimDepth = 0.014f;
            }
            else
            {
                Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}/{n}.png");
                room.wall = Lit("M_Ref_Wall", new Color(0.86f, 0.80f, 0.52f), T("RefWall_Albedo"), 0.1f);
                room.olive = Lit("M_Ref_WallOlive", new Color(0.70f, 0.70f, 0.12f), T("RefWall_Albedo"), 0.1f);
                room.carpet = Lit("M_Ref_Carpet", new Color(0.58f, 0.52f, 0.32f), T("RefCarpet_Albedo"), 0.05f);
                room.trim = Lit("M_Ref_Trim", new Color(0.88f, 0.84f, 0.66f), null, 0.15f);
                room.ceilingTile = Lit("M_Ref_Ceiling", new Color(0.80f, 0.76f, 0.50f), assets.ceilingTile ? assets.ceilingTile.GetTexture("_BaseMap") as Texture2D : null, 0.05f);
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

        public static Material Lit(string name, Color c, Texture2D tex, float smooth)
        {
            string path = $"{MatFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetShaderPassEnabled("MotionVectors", false);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
