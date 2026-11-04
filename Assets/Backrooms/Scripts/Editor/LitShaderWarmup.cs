using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Compiles Backrooms/Lit's variants in Edit Mode, in the background, whenever the shader or its includes change, and
    /// holds Play until they are ready.
    ///
    /// Why: one variant of the lit shader takes 10-15 s to compile. Cameras that render into textures (the auto-exposure
    /// probe, the crawl holes' portal views, the indoor reflection probes) compile shaders synchronously, so in Play Mode a
    /// changed shader stalls the editor mid-frame for minutes; on Direct3D 12 a long stall there made the editor lose its
    /// graphics device and crash (2026-10-26). In Edit Mode the variants compile in parallel on the shader compiler processes
    /// (a minute or two), and Play Mode then finds them in the shader cache.
    ///
    /// The variants are the ones a play session used, recorded in Shaders/BackroomsLitVariants.shadervariants (Project
    /// Settings > Graphics > Shader Loading > Save to asset, with that path, records what the current session used).
    /// Backrooms > Compile Lit Shader Variants runs it by hand.
    /// </summary>
    [InitializeOnLoad]
    static class LitShaderWarmup
    {
        const string CollectionPath = "Assets/Backrooms/Shaders/BackroomsLitVariants.shadervariants";
        const string HashKey = "Backrooms.LitWarmupHash";

        static bool s_warming, s_playWhenReady;
        static string s_hash;
        static readonly List<Material> s_materials = new List<Material>();
        static System.Diagnostics.Stopwatch s_watch;

        static LitShaderWarmup()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            // After a script or shader change (a domain reload follows a shader import): start in the background if needed.
            EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode && NeedsWarm(out _)) Start(); };
        }

        [MenuItem("Backrooms/Compile Lit Shader Variants")]
        static void FromMenu() { EditorPrefs.DeleteKey(HashKey); Start(); }

        /// <summary>A shader-only change does not reload scripts: start when the lit shader's files are imported.</summary>
        class ShaderChanges : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (var path in imported)
                    if (path.StartsWith("Assets/Backrooms/Shaders/") && (path.EndsWith(".shader") || path.EndsWith(".hlsl")))
                    {
                        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode && NeedsWarm(out _)) Start(); };
                        return;
                    }
            }
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            if (!s_warming && !NeedsWarm(out _)) return;
            // Not yet: Play starts by itself once the lighting shaders are compiled.
            EditorApplication.isPlaying = false;
            s_playWhenReady = true;
            if (!s_warming) Start();
            Debug.Log("[Backrooms] Compiling the lighting shaders before Play (once after they change); Play starts when they are ready.");
        }

        static bool NeedsWarm(out string hash)
        {
            hash = null;
            var lit = Shader.Find("Backrooms/Lit");
            if (!lit || !AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath)) return false;
            hash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(lit)) + "|" +
                   AssetDatabase.GetAssetDependencyHash(CollectionPath) + "|" + SystemInfo.graphicsDeviceType;
            return EditorPrefs.GetString(HashKey, "") != hash;
        }

        static void Start()
        {
            if (s_warming) return;
            NeedsWarm(out s_hash);
            if (s_hash == null) return;
            Debug.Log("[Backrooms] Compiling the lighting shaders' variants in the background (the shader changed).");
            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
            var lit = Shader.Find("Backrooms/Lit");
            if (!collection || !lit) return;
            s_warming = true;
            s_watch = System.Diagnostics.Stopwatch.StartNew();
            // Queue every recorded variant of the lit shader (and the passes it is drawn with) at once: the compiler
            // processes work through them in parallel.
            var lightMode = new ShaderTagId("LightMode");
            var entries = new SerializedObject(collection).FindProperty("m_Shaders");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var shader = entry.FindPropertyRelative("first").objectReferenceValue as Shader;
                if (!shader || !shader.name.StartsWith("Backrooms/")) continue;
                var variants = entry.FindPropertyRelative("second.variants");
                for (int v = 0; v < variants.arraySize; v++)
                {
                    var variant = variants.GetArrayElementAtIndex(v);
                    var keywords = variant.FindPropertyRelative("keywords").stringValue.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                    bool shadow = variant.FindPropertyRelative("passType").intValue == (int)PassType.ShadowCaster;
                    bool forward = System.Array.Exists(keywords, k => k.StartsWith("FOG_") || k == "_ADDITIONAL_LIGHT_SHADOWS" || k == "_SCREEN_SPACE_OCCLUSION" || k.StartsWith("_REFLECTION_PROBE"));
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, shaderKeywords = keywords };
                    s_materials.Add(material);
                    for (int p = 0; p < shader.passCount; p++)
                    {
                        string mode = shader.FindPassTagValue(p, lightMode).name;
                        bool wanted = shadow ? mode == "ShadowCaster" : forward ? mode == "UniversalForward" : mode == "DepthNormals" || mode == "DepthOnly";
                        if (wanted) ShaderUtil.CompilePass(material, p, false);
                    }
                }
            }
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            float progress = Mathf.Repeat((float)s_watch.Elapsed.TotalSeconds / 60f, 1f);
            if (s_playWhenReady && EditorUtility.DisplayCancelableProgressBar("Still Recording",
                    "Compiling the lighting shaders before Play (once after they change)...", progress))
                s_playWhenReady = false;                         // cancelled: stay in Edit Mode, keep compiling
            if (ShaderUtil.anythingCompiling) return;
            EditorApplication.update -= Poll;
            EditorUtility.ClearProgressBar();
            // Anything the queue missed is compiled here (one at a time; normally nothing is left).
            var queued = s_watch.Elapsed.TotalSeconds;
            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
            if (collection) collection.WarmUp();
            var rest = s_watch.Elapsed.TotalSeconds - queued;
            foreach (var m in s_materials) if (m) Object.DestroyImmediate(m);
            s_materials.Clear();
            s_warming = false;
            if (s_hash != null) EditorPrefs.SetString(HashKey, s_hash);
            Debug.Log($"[Backrooms] Lighting shaders compiled ({queued:0} s in parallel, {rest:0} s for what was left).");
            if (s_playWhenReady) { s_playWhenReady = false; EditorApplication.isPlaying = true; }
        }
    }
}
