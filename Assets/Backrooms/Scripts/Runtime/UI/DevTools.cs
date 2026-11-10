using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// One switch for the developer's tools (Docs/UiPlan.md U12; the owner, 2026-11-10: "build mode and dev is for me
    /// technically, not future players"). Off, a player pressing every key cannot spawn, skip or fly:
    /// <list type="bullet">
    /// <item>L (items, furniture, PROGRESSION: skip a stage, add money), K (enemies), V (fly through walls), F2 (item
    /// tuning);</item>
    /// <item>the pause menu's REFERENCE ROOM and BUILD A SECTION / ROOM LIBRARY (build mode).</item>
    /// </list>
    /// <para>The tools are there at all only in the editor, in a development build, or with <c>-dev</c> on the command line
    /// (<see cref="Available"/>); a player's build without the flag has none and no way to turn them on. Where they are
    /// there they start on, and Settings has a DEVELOPER TOOLS line to turn them off and see the player's game. While on,
    /// the viewfinder shows a small DEV.</para>
    /// </summary>
    public static class DevTools
    {
        const string Pref = "DevToolsOff";
        static int _available = -1;

        /// <summary>The editor, a development build, or <c>-dev</c> on the command line.</summary>
        public static bool Available
        {
            get
            {
                if (_available < 0)
                    _available = Application.isEditor || Debug.isDebugBuild
                        || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-dev") >= 0 ? 1 : 0;
                return _available == 1;
            }
        }

        /// <summary>The tools answer their keys and show their menu entries.</summary>
        public static bool On
        {
            get => Available && PlayerPrefs.GetInt(Pref, 0) == 0;
            set { if (Available) { PlayerPrefs.SetInt(Pref, value ? 0 : 1); PlayerPrefs.Save(); } }
        }
    }
}
