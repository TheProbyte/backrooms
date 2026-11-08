using System.Collections;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Co-op on one machine without anyone at the keyboard (2026-11-07, testing voice chat and looks): a build started with
    /// <c>-net-join 127.0.0.1</c> joins that host as soon as the main menu is up (<c>-net-port</c> for another port,
    /// <c>-net-name</c> for its player's name, kept in that build's own settings). <c>-voice-test</c> sends a test voice
    /// (a buzzing tone in bursts, as speech comes) instead of the microphone: Net.NetWorld.VoiceTestTone, which the editor's
    /// tests set directly. Logs what it hears from the others every few seconds.
    /// </summary>
    public class NetAutoTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-voice-test") >= 0) Net.NetWorld.VoiceTestTone = true;
            if (System.Array.IndexOf(args, "-net-join") < 0 && System.Array.IndexOf(args, "-net-host") < 0) return;
            var go = new GameObject("NetAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<NetAutoTest>();
        }

        static string Arg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        IEnumerator Start()
        {
            string name0 = Arg("-net-name", null);
            if (!string.IsNullOrEmpty(name0)) Net.NetSession.PlayerName = name0;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-net-host") >= 0)
            {
                // -net-host: hosts a new world of its own (a save folder in the temp cache, never the player's saves).
                Saves.SaveSlots.FolderOverride = System.IO.Path.Combine(Application.temporaryCachePath, "NetTestSaves");
                Saves.SaveSlots.Erase(0);
                float wait = Time.realtimeSinceStartup + 60f;
                while (!Net.NetSession.Instance && Time.realtimeSinceStartup < wait) yield return null;
                yield return new WaitForSecondsRealtime(2f);
                if (Net.NetSession.Host(0, out var herr)) Debug.Log("[NetAutoTest] Hosting a test world.");
                else Debug.Log("[NetAutoTest] Could not host: " + herr);
                while (true) { yield return new WaitForSecondsRealtime(4f); Report(); }
            }
            string addr = Arg("-net-join", "127.0.0.1");
            int port = int.TryParse(Arg("-net-port", "7777"), out var p) ? p : 7777;
            string name = Arg("-net-name", null);
            if (!string.IsNullOrEmpty(name)) Net.NetSession.PlayerName = name;
            // The session lives in the menu: wait for it.
            float until = Time.realtimeSinceStartup + 60f;
            while (!Net.NetSession.Instance && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            for (int tries = 0; tries < 10 && !Net.NetSession.Active; tries++)
            {
                if (Net.NetSession.Join(addr, port, out var err)) Debug.Log($"[NetAutoTest] Joining {addr}:{port}.");
                else Debug.Log("[NetAutoTest] Could not join: " + err);
                yield return new WaitForSecondsRealtime(8f);
            }
            while (true)
            {
                yield return new WaitForSecondsRealtime(4f);
                Debug.Log("[NetAutoTest] " + Net.NetWorld.VoiceReport());
                Report();
            }
        }

        /// <summary>Where this player and the others are (in the hub or not), and whether this one is still on the cot.</summary>
        static void Report()
        {
            var sb = new System.Text.StringBuilder("[NetAutoTest] me ");
            var me = FindAnyObjectByType<CamcorderPlayerController>();
            if (me) sb.Append(me.transform.position.ToString("F2")).Append(Generation.HubPlan.InHub(me.transform.position) ? " (hub)" : " (world)")
                .Append(me.seated ? " lying/seated" : " standing");
            foreach (var rp in FindObjectsByType<Net.RemotePlayer>(FindObjectsInactive.Exclude))
                sb.Append(" | friend ").Append(rp.transform.position.ToString("F2"));
            Debug.Log(sb.ToString());
        }
    }
}
