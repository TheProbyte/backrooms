using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// The crawl holes' cameras render with Portal_Renderer: the PC renderer's settings without its camcorder features (no
    /// camcorder pass, no frame hold), so the view through a hole is processed once, by the player's camera. It keeps a copy
    /// of the PC renderer's SSAO, so the room seen through a hole has the same ambient occlusion it has once you are through
    /// (GraphicsController switches both with the AO option).
    /// </summary>
    public static class PortalRendererSetup
    {
        const string Source = "Assets/Settings/PC_Renderer.asset", Target = "Assets/Settings/Portal_Renderer.asset";

        [MenuItem("Backrooms/Set Up Crawl Hole Renderer")]
        public static void Setup()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Target);
            if (!data)
            {
                if (!AssetDatabase.CopyAsset(Source, Target)) { Debug.LogError("[Backrooms] Could not copy " + Source); return; }
                data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Target);
            }
            // Strip every renderer feature (they were copied along as sub-assets), then add back a copy of the SSAO.
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(Target))
                if (sub is ScriptableRendererFeature f) Object.DestroyImmediate(f, true);
            data.rendererFeatures.Clear();
            var source = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Source);
            foreach (var f in source.rendererFeatures)
            {
                if (!f || !f.GetType().Name.Contains("AmbientOcclusion")) continue;
                var copy = Object.Instantiate(f);
                copy.name = f.name;
                AssetDatabase.AddObjectToAsset(copy, data);
                data.rendererFeatures.Add(copy);
            }
            var so = new SerializedObject(data);
            so.Update();
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null)
            {
                map.arraySize = data.rendererFeatures.Count;
                for (int i = 0; i < data.rendererFeatures.Count; i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i], out string _, out long id);
                    map.GetArrayElementAtIndex(i).longValue = id;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var aso = new SerializedObject(asset);
                var list = aso.FindProperty("m_RendererDataList");
                bool found = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) found = true;
                if (found) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
                aso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Portal_Renderer ready.");
        }
    }
}
