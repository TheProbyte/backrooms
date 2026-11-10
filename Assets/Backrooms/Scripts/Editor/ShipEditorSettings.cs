using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Copies the settings tuned in the editor (the editor's own PlayerPrefs) into Resources/ShippedSettings.json for the
    /// length of a build, so the build starts from them (Backrooms.ShippedSettings). The file is removed again afterwards.
    /// </summary>
    class ShipEditorSettings : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string Path = "Assets/Backrooms/Resources/" + ShippedSettings.ResourceName + ".json";
        public int callbackOrder => -900;

        public void OnPreprocessBuild(BuildReport report)
        {
            var list = new List<ShippedSettings.Entry>();
            const string none = "\u0001none";
            foreach (var k in ShippedSettings.Keys)
            {
                if (!PlayerPrefs.HasKey(k)) continue;
                // PlayerPrefs does not say a key's type: each getter returns the default for a key of another type.
                string s = PlayerPrefs.GetString(k, none);
                if (s != none) { list.Add(new ShippedSettings.Entry { key = k, type = 0, s = s }); continue; }
                int i = PlayerPrefs.GetInt(k, int.MinValue);
                if (i != int.MinValue) { list.Add(new ShippedSettings.Entry { key = k, type = 1, i = i }); continue; }
                float f = PlayerPrefs.GetFloat(k, float.NaN);
                if (!float.IsNaN(f)) list.Add(new ShippedSettings.Entry { key = k, type = 2, f = f });
            }
            File.WriteAllText(Path, JsonUtility.ToJson(new ShippedSettings.File { entries = list.ToArray() }));
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Backrooms] Build starts from {list.Count} editor settings ({Path}).");
            EditorApplication.delayCall += Remove;   // also when the build fails (no post-build step then)
        }

        public void OnPostprocessBuild(BuildReport report) => Remove();

        static void Remove()
        {
            if (File.Exists(Path)) AssetDatabase.DeleteAsset(Path);
        }
    }
}
