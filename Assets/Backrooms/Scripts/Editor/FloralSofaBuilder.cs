using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// The floral sofa (round 15, owner's reference: a cream three-seater with rolled arms and floral cushions), from
    /// Tools/Blender/build_floral_sofa.py: sets the import of its model and textures, makes its two fabrics and its prefab in
    /// Resources/Furniture (spawned from the developer menu; saved moved ones are found again by name, WorldSaver.AssetKeys).
    /// Backrooms > Build floral sofa.
    /// </summary>
    public static class FloralSofaBuilder
    {
        const string Model = "Assets/Backrooms/Models/Sofa_Floral.fbx";
        const string TexFolder = "Assets/Backrooms/Textures/FloralSofa";
        const string MatFolder = "Assets/Backrooms/Materials/FloralSofa";
        const string PrefabPath = "Assets/Backrooms/Resources/Furniture/Sofa_Floral.prefab";

        [MenuItem("Backrooms/Build floral sofa")]
        public static void Build()
        {
            Texture(TexFolder + "/FloralPattern.png", false, 2048);
            Texture(TexFolder + "/CreamWeave_Albedo.png", false, 512);
            Texture(TexFolder + "/CreamWeave_Normal.png", true, 512);
            if (AssetImporter.GetAtPath(Model) is ModelImporter mi)
            {
                // Readable: the hand is placed on the model's own surface (MovableProp.PlaceHand).
                mi.isReadable = true;
                mi.importAnimation = false;
                mi.importCameras = false; mi.importLights = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.SaveAndReimport();
            }
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Backrooms/Materials", "FloralSofa");
            if (!AssetDatabase.IsValidFolder("Assets/Backrooms/Resources/Furniture")) AssetDatabase.CreateFolder("Assets/Backrooms/Resources", "Furniture");
            var floral = Lit("FloralSofa_Floral", Load<Texture2D>(TexFolder + "/FloralPattern.png"), null, 0.1f, 0f);
            var cream = Lit("FloralSofa_Cream", Load<Texture2D>(TexFolder + "/CreamWeave_Albedo.png"), Load<Texture2D>(TexFolder + "/CreamWeave_Normal.png"), 0.08f, 0.7f);
            var wood = Load<Material>("Assets/Backrooms/Materials/M_WoodDark.mat");

            var model = Load<GameObject>(Model);
            if (!model) { Debug.LogError("[Backrooms] Sofa_Floral.fbx missing: run Tools/Blender/build_floral_sofa.py first."); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Sofa_Floral";
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name : "";
                    mats[i] = n.StartsWith("Floral") ? floral : n.StartsWith("Wood") ? wood : cream;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Floral sofa built: " + PrefabPath);
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        static void Texture(string path, bool normal, int max)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogError("[Backrooms] Missing " + path); return; }
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.maxTextureSize = max;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Material Lit(string name, Texture2D albedo, Texture2D normal, float smoothness, float normalScale)
        {
            string path = $"{MatFolder}/{name}.mat";
            var mat = Load<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            if (normal) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", normalScale); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetShaderPassEnabled("MotionVectors", false);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
