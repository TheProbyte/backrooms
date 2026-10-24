using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// The crawl holes' cameras render with Portal_Renderer: the PC renderer's settings without its renderer features (no
    /// camcorder pass, no frame hold), so the view through a hole is processed once, by the player's camera.
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
            // Strip every renderer feature (they were copied along as sub-assets).
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(Target))
                if (sub is ScriptableRendererFeature f) Object.DestroyImmediate(f, true);
            data.rendererFeatures.Clear();
            var so = new SerializedObject(data);
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null) map.arraySize = 0;
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
