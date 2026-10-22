using Backrooms.Generation;
using UnityEditor;

namespace Backrooms.EditorTools
{
    /// <summary>Binds the light grid's stand-in globals in the editor so Backrooms/Lit draws in edit mode.</summary>
    [InitializeOnLoad]
    static class LightGridEditorInit
    {
        static LightGridEditorInit() => EditorApplication.delayCall += LightGridGlobals.BindFallbacks;
    }
}
