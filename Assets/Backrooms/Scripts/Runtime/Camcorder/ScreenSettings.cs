using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// The game window: how it is shown (fullscreen, borderless fullscreen, a window, a borderless window) and its size.
    /// Set from the main menu's SETTINGS and F5 > DISPLAY. Saved here (Unity keeps no borderless window of its own) and put
    /// back when the game starts. On a wide monitor a 1920 x 1080 window or borderless window keeps the game to that size.
    /// A borderless window is Windows only (elsewhere it is a plain window); nothing changes in the editor's Game view.
    /// </summary>
    public static class ScreenSettings
    {
        public enum Mode { Fullscreen, Borderless, Windowed, BorderlessWindow }
        public static readonly string[] ModeNames = { "FULLSCREEN", "BORDERLESS FULLSCREEN", "WINDOWED", "BORDERLESS WINDOW" };
        const string Key = "Backrooms.Screen.v1";

        [Serializable]
        class Saved { public int mode = (int)Mode.Borderless; public int width, height; }

        public static Mode CurrentMode { get; private set; } = Mode.Borderless;
        /// <summary>Chosen size (0 x 0: the monitor's own).</summary>
        public static Vector2Int CurrentSize { get; private set; }

        /// <summary>The monitor's own size.</summary>
        public static Vector2Int Native
        {
            get
            {
                var d = Screen.mainWindowDisplayInfo;
                if (d.width > 0 && d.height > 0) return new Vector2Int(d.width, d.height);
                var r = Screen.currentResolution;
                return new Vector2Int(r.width, r.height);
            }
        }

        /// <summary>Sizes to choose from: the monitor's own first, then its other modes and common 16:9 sizes that fit, largest first.</summary>
        public static List<Vector2Int> Sizes()
        {
            var n = Native;
            var set = new HashSet<Vector2Int>();
            foreach (var r in Screen.resolutions)
                if (r.width >= 1024 && r.height >= 600 && r.width <= n.x && r.height <= n.y) set.Add(new Vector2Int(r.width, r.height));
            foreach (var s in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                if (s.x <= n.x && s.y <= n.y) set.Add(s);
            set.Remove(n);
            var list = new List<Vector2Int>(set);
            list.Sort((a, b) => b.x * b.y != a.x * a.y ? (b.x * b.y).CompareTo(a.x * a.y) : b.x.CompareTo(a.x));
            list.Insert(0, n);
            return list;
        }

        public static string SizeName(Vector2Int s) => s.x + " × " + s.y + (s == Native ? " (FULL)" : "");

        /// <summary>The size in use: the chosen one, or the monitor's own when none is chosen or it no longer fits.</summary>
        public static Vector2Int EffectiveSize
        {
            get
            {
                var n = Native;
                var s = CurrentSize;
                return s.x <= 0 || s.y <= 0 || s.x > n.x || s.y > n.y ? n : s;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Load()
        {
            try
            {
                var j = PlayerPrefs.GetString(Key, "");
                if (string.IsNullOrEmpty(j))
                {
                    // Nothing chosen yet: what Unity started with.
                    CurrentMode = Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? Mode.Fullscreen
                        : Screen.fullScreenMode == FullScreenMode.Windowed ? Mode.Windowed : Mode.Borderless;
                    CurrentSize = Vector2Int.zero;
                    return;
                }
                var s = JsonUtility.FromJson<Saved>(j);
                CurrentMode = (Mode)Mathf.Clamp(s.mode, 0, 3);
                CurrentSize = new Vector2Int(s.width, s.height);
            }
            catch (Exception) { return; }
            if (!Application.isEditor) Apply(CurrentMode, CurrentSize);
        }

        /// <summary>Shows the game this way and remembers it.</summary>
        public static void Apply(Mode mode, Vector2Int size)
        {
            CurrentMode = mode; CurrentSize = size;
            try { PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Saved { mode = (int)mode, width = size.x, height = size.y })); PlayerPrefs.Save(); }
            catch (Exception) { }
            if (Application.isEditor) return;

            var s = EffectiveSize;
            switch (mode)
            {
                case Mode.Fullscreen: Screen.SetResolution(s.x, s.y, FullScreenMode.ExclusiveFullScreen); break;
                case Mode.Borderless: Screen.SetResolution(s.x, s.y, FullScreenMode.FullScreenWindow); break;
                default: Screen.SetResolution(s.x, s.y, FullScreenMode.Windowed); break;
            }
            Fixer.Run(mode == Mode.BorderlessWindow ? s : Vector2Int.zero, mode == Mode.Windowed);
        }

        /// <summary>After Unity has changed the window (a frame or two later): takes the frame off a borderless window and centres
        /// it, or gives a plain window its frame back.</summary>
        class Fixer : MonoBehaviour
        {
            static Fixer s_fixer;
            Vector2Int _size; bool _framed; int _frames;

            public static void Run(Vector2Int borderless, bool framed)
            {
                if (!s_fixer)
                {
                    var go = new GameObject("ScreenSettings") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    s_fixer = go.AddComponent<Fixer>();
                }
                s_fixer._size = borderless; s_fixer._framed = framed; s_fixer._frames = 3; s_fixer.enabled = true;
            }

            void Update()
            {
                if (--_frames > 0) return;
                enabled = false;
                if (_size.x > 0) Win32.MakeBorderless(_size);
                else if (_framed) Win32.MakeFramed();
            }
        }

        static class Win32
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            const int GWL_STYLE = -16;
            const long WS_POPUP = 0x80000000L, WS_VISIBLE = 0x10000000L, WS_OVERLAPPEDWINDOW = 0x00CF0000L;
            const uint SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOZORDER = 0x0004, SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001;
            const uint MONITOR_DEFAULTTONEAREST = 2;

            [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
            [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
            delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

            [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
            [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder s, int n);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
            [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
            [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
            [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

            static IntPtr s_found;

            static IntPtr Window()
            {
                s_found = IntPtr.Zero;
                uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                EnumWindows((h, _) =>
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    if (pid != me || !IsWindowVisible(h)) return true;
                    var sb = new System.Text.StringBuilder(64);
                    GetClassName(h, sb, 64);
                    if (sb.ToString() != "UnityWndClass") return true;
                    s_found = h;
                    return false;
                }, IntPtr.Zero);
                return s_found;
            }

            public static void MakeBorderless(Vector2Int size)
            {
                var h = Window();
                if (h == IntPtr.Zero) return;
                SetWindowLongPtr(h, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                int x = 0, y = 0;
                if (GetMonitorInfo(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), ref info))
                {
                    var m = info.rcMonitor;
                    x = m.left + (m.right - m.left - size.x) / 2;
                    y = m.top + (m.bottom - m.top - size.y) / 2;
                }
                SetWindowPos(h, IntPtr.Zero, x, y, size.x, size.y, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOZORDER);
            }

            public static void MakeFramed()
            {
                var h = Window();
                if (h == IntPtr.Zero) return;
                long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
                if ((style & WS_POPUP) == 0) return;
                SetWindowLongPtr(h, GWL_STYLE, new IntPtr((style & ~WS_POPUP) | WS_OVERLAPPEDWINDOW | WS_VISIBLE));
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
            }
#else
            public static void MakeBorderless(Vector2Int size) { }
            public static void MakeFramed() { }
#endif
        }
    }
}
