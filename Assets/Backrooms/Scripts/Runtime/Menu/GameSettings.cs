using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// The player's own settings (2026-11-04, the owner's "create a settings page": Menu/SettingsMenu.cs), saved under
    /// Backrooms.Player.v1 and kept apart from the developer's F5 tuning, which they scale rather than replace:
    /// <list type="bullet">
    /// <item>Sound: the master volume (the listener's), the building's ambience (hum, lights, ventilation, room tone:
    /// BuildingAudio) and music (what a tape plays: TapeDeckFilter).</item>
    /// <item>Controls: mouse sensitivity (a multiple of the tuned one) and invert Y (on top of the tuned one).</item>
    /// <item>Comfort (Xbox accessibility guideline 117: shake and blur as sliders, not just on and off): camera shake
    /// and motion blur as shares of the camcorder look's own, and brightness as an exposure offset in EV.</item>
    /// </list>
    /// Graphics (quality, frame rate, resolution) and the screen keep their own saved settings (GraphicsOptions,
    /// ScreenSettings); the page only steps them.
    /// </summary>
    public static class GameSettings
    {
        [System.Serializable]
        class Data
        {
            public float master = 1f, ambience = 1f, music = 1f;
            public float sensitivity = 1f;
            public bool invertY;
            public float shake = 1f, blur = 1f, brightness = 0f;
            public float voices = 1f;
            public bool micOn = true, pushToTalk;
            // (2026-11-10, Docs/UiPlan.md U10) Reading and holding.
            public int textSize;
            public bool holdCrouch, holdCrawl, steady, subtitles = true;
            public bool stillMenus;
            public int version = 1;
        }

        const string Key = "Backrooms.Player.v1";
        static Data s_d;
        static Data D { get { if (s_d == null) Load(); return s_d; } }

        /// <summary>Music's level for the audio thread (a plain field: no Unity calls there).</summary>
        public static volatile float MusicLevel = 1f;

        public static float Master { get => D.master; set { D.master = Mathf.Clamp01(value); Changed(); } }
        public static float Ambience { get => D.ambience; set { D.ambience = Mathf.Clamp01(value); Changed(); } }
        public static float Music { get => D.music; set { D.music = Mathf.Clamp01(value); Changed(); } }
        /// <summary>Co-op voice chat (2026-11-07, Net/NetWorld.Voice.cs): how loud the others are, whether your microphone is
        /// sent at all, and whether it is open (sent when you speak) or held open with T.</summary>
        public static float Voices { get => D.voices; set { D.voices = Mathf.Clamp01(value); Changed(); } }
        public static bool MicOn { get => D.micOn; set { D.micOn = value; Changed(); } }
        public static bool PushToTalk { get => D.pushToTalk; set { D.pushToTalk = value; Changed(); } }
        /// <summary>Mouse look, a multiple of the tuned sensitivity (0.2 .. 3).</summary>
        public static float Sensitivity { get => D.sensitivity; set { D.sensitivity = Mathf.Clamp(value, 0.2f, 3f); Changed(); } }
        public static bool InvertY { get => D.invertY; set { D.invertY = value; Changed(); } }
        /// <summary>Camera shake, a share of the look's own (0 .. 1).</summary>
        public static float Shake { get => D.shake; set { D.shake = Mathf.Clamp01(value); Changed(); } }
        /// <summary>Motion blur, a share of the look's own (0 .. 1).</summary>
        public static float MotionBlur { get => D.blur; set { D.blur = Mathf.Clamp01(value); Changed(); } }
        /// <summary>Brightness: an exposure offset (EV, -1.5 .. 1.5).</summary>
        public static float Brightness { get => D.brightness; set { D.brightness = Mathf.Clamp(value, -1.5f, 1.5f); Changed(); } }

        /// <summary>TEXT SIZE: 0 normal, 1 large, 2 larger (UI.UiKit.TextScale: every menu, panel and prompt).</summary>
        public static int TextSize { get => D.textSize; set { D.textSize = Mathf.Clamp(value, 0, 2); Changed(); } }
        /// <summary>Crouch and crawl while the key is held, instead of a press each way.</summary>
        public static bool HoldCrouch { get => D.holdCrouch; set { D.holdCrouch = value; Changed(); } }
        public static bool HoldCrawl { get => D.holdCrawl; set { D.holdCrawl = value; Changed(); } }
        /// <summary>FLASHING off: blinking marks (REC, notices going, LOW LIGHT, the shelf lamps' red) hold steady.</summary>
        public static bool Steady { get => D.steady; set { D.steady = value; Changed(); } }
        /// <summary>SUBTITLES for spoken lines (the PA voice and the orientation tape when they speak: CamcorderOSD.Subtitle).</summary>
        public static bool Subtitles { get => D.subtitles; set { D.subtitles = value; Changed(); } }
        public static event System.Action TextSizeChanged;
        /// <summary>MENU MOTION off (2026-11-10, Docs/UiPlan.md U19): menus appear at once instead of sliding in.</summary>
        public static bool StillMenus { get => D.stillMenus; set { D.stillMenus = value; Changed(); } }

        static void Load()
        {
            s_d = null;
            try { var j = PlayerPrefs.GetString(Key, ""); if (!string.IsNullOrEmpty(j)) s_d = JsonUtility.FromJson<Data>(j); }
            catch (System.Exception) { s_d = null; }
            if (s_d == null) s_d = new Data();
            Apply();
        }

        static int s_lastText = -1;
        static void Changed()
        {
            if (s_lastText != D.textSize) { s_lastText = D.textSize; TextSizeChanged?.Invoke(); }
            try { PlayerPrefs.SetString(Key, JsonUtility.ToJson(D)); PlayerPrefs.Save(); } catch (System.Exception) { }
            Apply();
        }

        /// <summary>What applies straight away (the rest is read where it is used).</summary>
        static void Apply()
        {
            AudioListener.volume = D.master;
            MusicLevel = D.music;
        }

        /// <summary>Sound, controls and comfort back as they come.</summary>
        public static void ResetSound() { D.master = 1f; D.ambience = 1f; D.music = 1f; D.voices = 1f; D.micOn = true; D.pushToTalk = false; Changed(); }
        public static void ResetControls() { D.sensitivity = 1f; D.invertY = false; Changed(); }
        public static void ResetComfort() { D.shake = 1f; D.blur = 1f; D.brightness = 0f; D.stillMenus = false; Changed(); }
        public static void ResetAccess() { D.textSize = 0; D.holdCrouch = D.holdCrawl = false; D.steady = false; D.subtitles = true; Changed(); }

        /// <summary>The motion-sickness preset (as Resident Evil 4's and others' one switch): little shake, no blur.</summary>
        public static void ReduceMotion() { D.shake = 0.25f; D.blur = 0f; D.stillMenus = true; Changed(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { Load(); }
    }
}
