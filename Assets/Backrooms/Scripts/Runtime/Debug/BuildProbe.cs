using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks a build's picture without anyone at the keyboard: run the game with <c>-probe [folder]</c>. It opens World 3
    /// (slot 2, seed 553893173), waits for the world to load, then for each camcorder preset waits for exposure to settle,
    /// saves a screenshot and logs the exposure numbers, then erases World 3 and quits. Nothing is saved to settings.
    /// In the editor, add it to an object in Play mode (it skips the slot handling when a world is already running).
    /// </summary>
    public class BuildProbe : MonoBehaviour
    {
        const int Slot = 2;
        const int Seed = 553893173;
        public string folder;
        public int[] presets = { 0, 4, 5, 7 };
        bool _ownSlot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-probe");
            if (i < 0 || FindAnyObjectByType<BuildProbe>()) return;
            var p = new GameObject("BuildProbe").AddComponent<BuildProbe>();
            p.folder = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1]
                : Path.Combine(Application.persistentDataPath, "probe");
            p._ownSlot = true;
            DontDestroyOnLoad(p.gameObject);
        }

        IEnumerator Start()
        {
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Application.persistentDataPath, "probe");
            Directory.CreateDirectory(folder);
            var log = new StringBuilder();
            log.AppendLine($"probe {System.DateTime.Now:yyyy-MM-dd HH:mm}  editor={Application.isEditor}  {Screen.width}x{Screen.height}  {SystemInfo.graphicsDeviceType}");

            log.AppendLine($"shipped settings: file={(bool)Resources.Load<TextAsset>(ShippedSettings.ResourceName)} seeded={ShippedSettings.Seeded}  screen={ScreenSettings.CurrentMode} {ScreenSettings.EffectiveSize}");
            if (_ownSlot)
            {
                Saves.SaveSlots.Erase(Slot);
                var d = Saves.SaveSlots.Create(Slot);
                d.seed = Seed;
                Saves.SaveSlots.Write(d, null);
                yield return new WaitForSeconds(1f);
                Saves.GameSession.Play(Slot);
            }

            WorldStreamer world = null;
            float t0 = Time.realtimeSinceStartup;
            while ((!world || world.QueuedChunks > 0 || Time.realtimeSinceStartup - t0 < 8f) && Time.realtimeSinceStartup - t0 < 120f)
            {
                world = WorldStreamer.Current;
                yield return null;
            }
            var rig = FindAnyObjectByType<CamcorderRig>();
            var ae = FindAnyObjectByType<CamcorderAutoExposure>();
            log.AppendLine($"world={(bool)world}  rig={(bool)rig}  exposure={(bool)ae}");
            var original = rig ? rig.Settings : null;

            foreach (int p in presets)
            {
                if (rig) rig.SetSettings(CamcorderSettings.Preset(p));
                yield return new WaitForSeconds(6f);
                string name = p + "_" + CamcorderSettings.PresetNames[p].Replace(" ", "").Replace("'", "");
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
                yield return null;
                if (ae) log.AppendLine($"{name}: exposure={ae.Exposure:F3} gainEV={ae.GainEV:F2} metered={ae.MeteredLevel:F3} lum={ae.MeasuredLuminance:F3} wb={ae.WhiteBalance} samples={ae.SampleCount}");
                yield return new WaitForSeconds(0.5f);
            }
            // The default camcorder again with the 3D view drawn at full size (not matched to the recording's lines).
            var gc = GraphicsController.Instance;
            if (rig && gc)
            {
                bool match = gc.Options.matchRecording;
                gc.Options.matchRecording = false;
                rig.SetSettings(CamcorderSettings.Preset(0));
                yield return new WaitForSeconds(6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "0_Default_fullsize.png"));
                yield return null;
                if (ae) log.AppendLine($"0_Default_fullsize: exposure={ae.Exposure:F3} gainEV={ae.GainEV:F2} metered={ae.MeteredLevel:F3} lum={ae.MeasuredLuminance:F3} wb={ae.WhiteBalance}");
                gc.Options.matchRecording = match;
                yield return new WaitForSeconds(0.5f);
            }
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) log.AppendLine($"renderScale={urp.renderScale} upscale={urp.upscalingFilter} hdr={urp.supportsHDR}");
            if (gc) log.AppendLine("graphics " + JsonUtility.ToJson(gc.Options));
            if (rig && original != null) rig.SetSettings(original);

            File.WriteAllText(Path.Combine(folder, "probe.txt"), log.ToString());
            Debug.Log("[Backrooms] Probe written to " + folder + "\n" + log);
            if (_ownSlot)
            {
                Saves.GameSession.Leave();
                Saves.SaveSlots.Erase(Slot);
                Application.Quit();
            }
            else Destroy(gameObject);
        }
    }
}
