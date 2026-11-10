using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// <c>-ui-shots [folder]</c> (Docs/UiPlan.md U3, 2026-11-10): a picture of every screen, taken by the game. It opens
    /// each screen and state in turn (the title menu and its pages, settings, YOUR LOOK; in a world of its own: the hub
    /// with the side panels, a notice, a pay slip, the pause menu, F5, the L menu) at five window shapes, saves a PNG of
    /// each in <c>folder/shape/</c>, writes <c>index.html</c> to flip through them side by side, and quits.
    /// <para>Run it on a build (the editor's game view cannot change size): <c>StillRecording.exe -ui-shots D:\shots -dev</c>
    /// (<c>-dev</c> for the L menu). Saves go to a folder of their own; the player's are never touched.</para>
    /// </summary>
    public class UiShots : MonoBehaviour
    {
        struct Shape { public string name; public int w, h; public bool bars; }

        static readonly Shape[] Shapes =
        {
            new Shape { name = "16x9", w = 1920, h = 1080, bars = true },
            new Shape { name = "16x10-deck", w = 1280, h = 800, bars = true },
            new Shape { name = "21x9", w = 1920, h = 822, bars = true },
            new Shape { name = "32x9", w = 1920, h = 540, bars = true },
            new Shape { name = "16x9-no-bars", w = 1920, h = 1080, bars = false },
        };

        string _dir;
        readonly List<string> _states = new List<string>();
        // The player's own camcorder settings, put back before quitting (the run shows the viewfinder marks and changes
        // the 4:3 bars; nothing is saved).
        bool _hadSettings, _osd0, _bars0;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-ui-shots");
            if (i < 0) return;
            var s = new GameObject("UiShots").AddComponent<UiShots>();
            s._dir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "ui-shots");
            DontDestroyOnLoad(s.gameObject);
            Saves.SaveSlots.FolderOverride = Path.Combine(Application.temporaryCachePath, "UiShotsSaves");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_dir);
            float t0 = Time.realtimeSinceStartup;
            yield return Frames(30);

            // ---- The title menu and its pages ----
            var mm = FindAnyObjectByType<Menu.MainMenu>();
            if (mm)
            {
                var page = typeof(Menu.MainMenu).GetNestedType("Page", Any);
                var show = typeof(Menu.MainMenu).GetMethod("Show", Any, null, new[] { page }, null);
                foreach (var (p, name) in new[] { ("Top", "01-title"), ("Saves", "02-saves"), ("Multi", "03-multiplayer") })
                {
                    show?.Invoke(mm, new[] { Enum.Parse(page, p) });
                    yield return AllShapes(name);
                }
                show?.Invoke(mm, new[] { Enum.Parse(page, "Top") });

                typeof(Menu.MainMenu).GetMethod("OpenSettings", Any)?.Invoke(mm, null);
                yield return Frames(5);
                // Every settings tab (2026-11-10: the pages are tabs now).
                for (int p = 0; p < Menu.SettingsMenu.PageCount; p++)
                {
                    Menu.SettingsMenu.ShowPageForShots(p);
                    yield return AllShapes($"04-settings-{p}-{Menu.SettingsMenu.PageName(p).ToLowerInvariant()}");
                }
                // TEXT SIZE at LARGER: the same page, everything bigger (the player's own size put back after).
                int size0 = GameSettings.TextSize;
                GameSettings.TextSize = 2;
                Menu.SettingsMenu.ShowPageForShots(3);
                yield return Frames(5);
                yield return AllShapes("04-settings-text-larger");
                GameSettings.TextSize = size0;
                Menu.SettingsMenu.CloseForShots();
                yield return Frames(5);
                typeof(Menu.MainMenu).GetMethod("OpenLook", Any)?.Invoke(mm, null);
                yield return Frames(20);
                yield return AllShapes("09-your-look");
                var look = FindAnyObjectByType<Menu.LookMenu>();
                if (look) typeof(Menu.LookMenu).GetMethod("Close", Any)?.Invoke(look, null);
                yield return Frames(5);
            }

            // ---- A world of its own: the hub ----
            Saves.SaveSlots.Erase(0);
            yield return null;
            Saves.GameSession.Play(0);
            float until = Time.realtimeSinceStartup + 40f;
            while ((Saves.GameSession.InMenu || FindAnyObjectByType<Menu.MainMenu>() || Menu.TapeTransition.Running) && Time.realtimeSinceStartup < until) yield return null;
            CamcorderPlayerController player = null;
            while (!player && Time.realtimeSinceStartup < until) { player = FindAnyObjectByType<CamcorderPlayerController>(); yield return null; }
            var wake = FindAnyObjectByType<Interaction.HubWake>();
            if (wake) wake.GetUp();
            yield return new WaitForSecondsRealtime(3f);
            yield return AllShapes("10-hub");

            // Batches in every state for the side panels' cards (this run's own throwaway world: its save is never kept):
            // the first lot stored with time left, one due back, one late.
            var data = Saves.GameSession.Data;
            if (data != null && data.batches.Count > 0)
            {
                var first = data.batches[0];
                first.state = 2; first.storeFor = 240f; first.left = 150f;
                data.batches.Add(new Saves.BatchData { id = 9001, client = 1, count = 6, state = 3, window = 180f, since = 60f });
                data.batches.Add(new Saves.BatchData { id = 9002, client = 2, count = 3, state = 3, window = 180f, since = 200f });
                yield return new WaitForSecondsRealtime(0.6f);
                yield return AllShapes("10b-batches");
                int size1 = GameSettings.TextSize;
                GameSettings.TextSize = 2;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return AllShapes("10c-batches-text-larger");
                GameSettings.TextSize = size1;
            }

            Camcorder.CamcorderOSD.Title("TALLMORE STORED - +$40 - DUE BACK IN 3:00");
            Camcorder.CamcorderOSD.Notice("ORDERED - SHELVING KIT - WAITING BY THE LIFT");
            Camcorder.CamcorderOSD.Notice("PLUGGED IN - LOAD 2 OF 4");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return AllShapes("11-notice", 0.2f);

            Camcorder.CamcorderOSD.ShowSlip("TALLMORE X5\tRETURNED\nSTORAGE, 3 WEEKS\t$40\nBACK ON TIME\t+$10\n--\nTODAY\t$50\nIN HAND\t$110", 60f);
            yield return new WaitForSecondsRealtime(1f);
            yield return AllShapes("12-pay-slip");
            Camcorder.CamcorderOSD.ShowSlip("", 0.01f);
            yield return new WaitForSecondsRealtime(0.5f);

            // The hub's three boards from where they are read, 3 m (U14, U15).
            foreach (var (screen, name) in new[] { ("ContractsBoardScreen", "16a-work-board"), ("SuppliesBoardScreen", "16b-supply-counter"), ("ImprovementsBoardScreen", "16c-improvements") })
            {
                var go = GameObject.Find(screen);
                var me = FindAnyObjectByType<CamcorderPlayerController>();
                if (!go || !me) continue;
                var t = go.transform;
                // A quad shows its face to -forward: stand 3 m out on that side, eyes level with its middle.
                Vector3 at = t.position - t.forward * 3f;
                float eye = 1.6f;
                at.y = t.position.y - eye + 0.05f;
                if (Physics.Raycast(new Vector3(at.x, t.position.y, at.z), Vector3.down, out var floor, 4f)) at.y = floor.point.y;
                var look = t.position - (at + Vector3.up * eye);
                float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                me.TeleportSafe(at, Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y, pitch);
                yield return new WaitForSecondsRealtime(1f);
                yield return AllShapes(name);
            }

            Interaction.PropGrabber.ForceHint = "CLICK PUT DOWN · HOLD THROW · HOLD ON A BOX TAKE IT TOO · R TURN";
            yield return Frames(3);
            yield return AllShapes("12b-prompts");
            Interaction.PropGrabber.ForceHint = "HOLD G TO PACK IT BACK INTO ITS CRATE";
            yield return Frames(3);
            yield return AllShapes("12c-prompt-hold");
            Interaction.PropGrabber.ForceHint = null;

            var pause = CustomRooms.PauseMenu.Instance;
            if (pause)
            {
                pause.Open();
                // Mid-slide (UI pass 5): three frames in, before its 120 ms are up.
                for (int f = 0; f < 3; f++) yield return null;
                yield return new WaitForEndOfFrame();
                var mid = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_dir, "13-pause-sliding.png"), mid.EncodeToPNG());
                Destroy(mid);
                yield return AllShapes("13-pause");
                pause.Close();
                yield return Frames(5);
            }

            var f5 = FindAnyObjectByType<Camcorder.CamcorderVisualMenu>();
            if (f5)
            {
                // The player's F5 (2026-11-10, U13), then the developer's bench.
                Camcorder.CamcorderPlayerMenu.SetOpenForShots(f5, true);
                yield return Frames(10);
                yield return AllShapes("14a-f5-player");
                Camcorder.CamcorderPlayerMenu.SetOpenForShots(f5, false);
                // (Closing it saves the camcorder settings: the player's own values first.)
                yield return Frames(5);
                f5.SetOpen(true);
                yield return Frames(10);
                yield return AllShapes("14-f5");
                // Closing F5 saves the camcorder settings: the player's own values first.
                var rigF5 = FindAnyObjectByType<Camcorder.CamcorderRig>();
                if (_hadSettings && rigF5 && rigF5.Settings != null) { rigF5.Settings.showOSD = _osd0; rigF5.Settings.aspect43 = _bars0; }
                f5.SetOpen(false);
                yield return Frames(5);
            }

            if (DevTools.On && Interaction.ItemsMenu.Instance)
            {
                Interaction.ItemsMenu.Instance.SetOpen(true);
                yield return Frames(10);
                yield return AllShapes("15-l-menu");
                Interaction.ItemsMenu.Instance.SetOpen(false);
                yield return Frames(5);
            }

            var rigEnd = FindAnyObjectByType<Camcorder.CamcorderRig>();
            if (_hadSettings && rigEnd && rigEnd.Settings != null) { rigEnd.Settings.showOSD = _osd0; rigEnd.Settings.aspect43 = _bars0; }
            WriteIndex(Time.realtimeSinceStartup - t0);
            Debug.Log($"[UiShots] {_states.Count} screens x {Shapes.Length} shapes in {Time.realtimeSinceStartup - t0:F0} s: {_dir}");
            Application.Quit();
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// <summary>The current screen at every shape, back to the first shape after.</summary>
        IEnumerator AllShapes(string state, float settle = 0f)
        {
            _states.Add(state);
            foreach (var s in Shapes)
            {
                yield return SetShape(s);
                if (settle > 0f) yield return new WaitForSecondsRealtime(settle);
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var dir = Path.Combine(_dir, s.name);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, state + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            yield return SetShape(Shapes[0]);
        }

        IEnumerator SetShape(Shape s)
        {
            var rig = FindAnyObjectByType<Camcorder.CamcorderRig>();
            if (rig && rig.Settings != null)
            {
                if (!_hadSettings) { _hadSettings = true; _osd0 = rig.Settings.showOSD; _bars0 = rig.Settings.aspect43; }
                rig.Settings.aspect43 = s.bars;
                rig.Settings.showOSD = true;   // the marks are what is being looked at
            }
            if (!Application.isEditor && (Screen.width != s.w || Screen.height != s.h))
            {
                Screen.SetResolution(s.w, s.h, FullScreenMode.Windowed);
                float until = Time.realtimeSinceStartup + 3f;
                while ((Screen.width != s.w || Screen.height != s.h) && Time.realtimeSinceStartup < until) yield return null;
            }
            yield return Frames(6);   // the panels lay out again at the new size
            yield return new WaitForSecondsRealtime(0.15f);   // (and a menu that has just come in has finished sliding)
        }

        void WriteIndex(float seconds)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=utf-8><title>UI shots</title><style>body{background:#140d09;color:#f3e6c8;font:14px sans-serif;margin:16px}")
              .Append("h2{margin:24px 0 8px;color:#f0a21e}div{display:flex;gap:8px;overflow-x:auto}figure{margin:0}img{height:240px;display:block}figcaption{font-size:12px;color:#cdb48a}</style>")
              .Append($"<h1>UI shots</h1><p>{DateTime.Now:yyyy-MM-dd HH:mm} · {_states.Count} screens × {Shapes.Length} shapes · {seconds:F0} s · developer tools {(DevTools.On ? "on" : "off")}</p>");
            foreach (var st in _states)
            {
                sb.Append("<h2>").Append(st).Append("</h2><div>");
                foreach (var s in Shapes) sb.Append($"<figure><a href=\"{s.name}/{st}.png\"><img src=\"{s.name}/{st}.png\"></a><figcaption>{s.name}</figcaption></figure>");
                sb.Append("</div>");
            }
            File.WriteAllText(Path.Combine(_dir, "index.html"), sb.ToString());
        }
    }
}
