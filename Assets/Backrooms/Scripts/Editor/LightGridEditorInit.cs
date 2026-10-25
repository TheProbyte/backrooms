using Backrooms.Generation;
using UnityEditor;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Owns fallback resources across editor transitions. A live grid always retains its real bindings;
    /// scenes without a running grid can rebind stand-ins after a reload, including build mode.
    /// </summary>
    [InitializeOnLoad]
    static class LightGridEditorInit
    {
        static LightGridEditorInit()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
                var grid = UnityEngine.Object.FindAnyObjectByType<LightGrid>();
                if (!grid || !grid.Ready) LightGridGlobals.ResetAndBindFallbacks();
            };
            AssemblyReloadEvents.beforeAssemblyReload += LightGridGlobals.ReleaseFallbacks;
            EditorApplication.quitting += LightGridGlobals.ReleaseFallbacks;
            EditorApplication.playModeStateChanged += s =>
            {
                // ExitingPlayMode can still render frames: keep valid bindings until assembly teardown.
                if (s == PlayModeStateChange.EnteredEditMode)
                {
                    LightGridGlobals.ReleaseFallbacks();
                    LightGridGlobals.ResetAndBindFallbacks();
                }
            };
        }
    }
}
