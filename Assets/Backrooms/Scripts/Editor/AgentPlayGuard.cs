using System.IO;
using UnityEditor;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Keeps .claude/unity.playing in step with Play mode, so the agent's edit guard
    /// (.claude/hooks/edit_guard.py) can refuse script and shader edits while the game runs.
    /// </summary>
    [InitializeOnLoad]
    static class AgentPlayGuard
    {
        static string Marker => Path.Combine(Path.GetDirectoryName(Application.dataPath), ".claude", "unity.playing");

        static AgentPlayGuard()
        {
            Sync(EditorApplication.isPlayingOrWillChangePlaymode);
            EditorApplication.playModeStateChanged += change =>
                Sync(change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.EnteredPlayMode);
            EditorApplication.quitting += () => Sync(false);
        }

        static void Sync(bool playing)
        {
            try
            {
                string marker = Marker;
                if (!Directory.Exists(Path.GetDirectoryName(marker))) return;
                if (playing) File.WriteAllText(marker, System.DateTime.Now.ToString("s"));
                else if (File.Exists(marker)) File.Delete(marker);
            }
            catch (IOException) { }
        }
    }
}
