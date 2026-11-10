using UnityEditor;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// The Blender scripts' models (Tools/Blender, exported into Resources/Overflow/Fittings): imported readable, with no cameras,
    /// lights or animation. The chunk builder copies their meshes on worker threads (FittingModels), so an unreadable one
    /// is silently left out of the world. (2026-11-09: it happened a third time, to the seam rooms' new staff door, after
    /// the tall shelving and pallet racking.)
    /// </summary>
    public class OverflowFittingImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Overflow/Fittings/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.isReadable = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
        }
    }
}
