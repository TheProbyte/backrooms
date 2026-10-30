using System;
using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// A build starts from the settings tuned in the editor: world generation, sounds, graphics, light look and camcorder.
    /// They live in the editor's own PlayerPrefs, which a build cannot see, so the build copies them into
    /// Resources/ShippedSettings.json (EditorTools.ShipEditorSettings, only for the length of the build). When the game
    /// starts, each one the player has not saved yet is taken from there; anything the player changes is theirs.
    /// </summary>
    public static class ShippedSettings
    {
        public const string ResourceName = "ShippedSettings";

        /// <summary>The settings a build takes from the editor (not the window, the tension strength or backups).</summary>
        public static readonly string[] Keys =
        {
            "Backrooms.WorldGen.v2", "Backrooms.SoundProfile.v1", "Backrooms.AudioDev.v1", "Backrooms.Graphics.v1", "Backrooms.MyLook.v1",
            "Backrooms.CamcorderSettings.v1", "Backrooms.CamcorderSettings.UserDefault", "Backrooms.CamcorderSettings.Preset",
            "Backrooms.CamcorderSettings.RefMatchHighlight",
        };

        [Serializable] public class Entry { public string key; public int type; public string s; public int i; public float f; }   // type 0 string, 1 int, 2 float
        [Serializable] public class File { public Entry[] entries; }

        /// <summary>How many settings were taken from the build's copy this run (for checks).</summary>
        public static int Seeded { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Seed()
        {
            Seeded = 0;
            if (Application.isEditor) return;
            var ta = Resources.Load<TextAsset>(ResourceName);
            if (!ta) return;
            try
            {
                var file = JsonUtility.FromJson<File>(ta.text);
                if (file?.entries == null) return;
                foreach (var e in file.entries)
                {
                    if (string.IsNullOrEmpty(e.key) || PlayerPrefs.HasKey(e.key)) continue;
                    if (e.type == 1) PlayerPrefs.SetInt(e.key, e.i);
                    else if (e.type == 2) PlayerPrefs.SetFloat(e.key, e.f);
                    else PlayerPrefs.SetString(e.key, e.s ?? "");
                    Seeded++;
                }
                if (Seeded > 0) PlayerPrefs.Save();
            }
            catch (Exception ex) { Debug.LogWarning("[Backrooms] Could not read the shipped settings: " + ex.Message); }
        }
    }
}
