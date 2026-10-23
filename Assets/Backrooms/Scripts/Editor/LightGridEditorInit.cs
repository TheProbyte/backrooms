using Backrooms.Generation;
using UnityEditor;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Binds the light grid's stand-in globals in the editor so Backrooms/Lit draws in edit mode. Never while
    /// playing: after a domain reload the delayed call could land on a running game and replace the live grid's
    /// buffers with empty ones (the shader then sees no walls). The running grid also re-binds every frame.
    /// </summary>
    [InitializeOnLoad]
    static class LightGridEditorInit
    {
        static LightGridEditorInit()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) LightGridGlobals.BindFallbacks();
            };
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredEditMode) LightGridGlobals.BindFallbacks();
            };
        }
    }
}
