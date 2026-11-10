using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Watches for Unity's own audio bug UUM-146734 (2026-11-06): inside the engine's audio thread manager
    /// (audio::DualThreadManager::ControlUpdate, called from AudioManager::Update every editor tick) the assert "Access
    /// version should be odd when acquiring lock" starts firing and never stops: tens of thousands a second, a log file
    /// of gigabytes, and in the end a crash when memory runs out. Nothing in the game's code causes it; Unity fixed it
    /// in 6000.3.22f1 and 6000.5.8f1 but in no 6000.4 build (this project is on 6000.4.5f1). It showed up here after
    /// leaving Play mode.
    /// When it starts, this restarts the audio engine (AudioSettings.Reset, which cuts it to a trickle) and says once,
    /// in the console and on the Game and Scene views, that Unity needs a restart to clear it. Only in the editor, never
    /// in Play mode.
    /// </summary>
    [InitializeOnLoad]
    static class AudioAssertWatchdog
    {
        const string AssertText = "Access version should be odd when acquiring lock";
        const int Threshold = 200;          // asserts before acting (one or two can be noise)

        static int s_count, s_countAtReset;
        static bool s_reset, s_warned;

        static AudioAssertWatchdog()
        {
            Application.logMessageReceivedThreaded += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Assert && message != null && message.StartsWith(AssertText))
                Interlocked.Increment(ref s_count);
        }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            int n = Volatile.Read(ref s_count);
            if (!s_reset && n >= Threshold)
            {
                s_reset = true;
                s_countAtReset = n;
                AudioSettings.Reset(AudioSettings.GetConfiguration());
                Debug.LogWarning("[Backrooms] Unity's audio bug UUM-146734 started (the assert \"" + AssertText +
                                 "\" repeating). The audio engine was restarted to slow it down; restart Unity to clear it. " +
                                 "Fixed by Unity in 6000.3.22f1 / 6000.5.8f1, not yet in 6000.4.");
            }
            if (s_reset && !s_warned && n - s_countAtReset >= Threshold)
            {
                s_warned = true;
                const string note = "Unity audio bug UUM-146734 is flooding the log.\nRestart Unity to clear it.";
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    var t = w.GetType().Name;
                    if (t == "GameView" || t == "SceneView") w.ShowNotification(new GUIContent(note), 30.0);
                }
                Debug.LogWarning("[Backrooms] " + note.Replace('\n', ' '));
            }
        }
    }
}
