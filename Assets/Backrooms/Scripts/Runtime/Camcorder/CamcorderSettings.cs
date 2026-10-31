using System;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// Every user-tunable aspect of the camcorder, grouped the way a real camera's signal chain is:
    /// lens -> focus -> exposure (iris, gain, shutter) -> white balance -> sensor -> camera processing -> colour ->
    /// recording format and playback -> display. Serialised to PlayerPrefs as JSON.
    /// Values marked "x" are multipliers on what is physical or typical for the chosen format (1 = as the real thing).
    /// </summary>
    [Serializable]
    public class CamcorderSettings
    {
        /// <summary>Version of the picture settings' units. Older saves are converted on load (see <see cref="Migrate"/>).</summary>
        public const int CurrentLookVersion = 2;
        public int lookVersion;

        /// <summary>The camcorder picture (off: the clean rendered image, for comparison).</summary>
        public bool camcorderLook = true;

        // Lens
        public float fieldOfView = 54.5f;       // vertical degrees; a stock 1/4" camcorder is ~36, more is a wide converter
        public float lensDistortion = 0.15f;    // positive = barrel (wide lenses), negative = pincushion
        public float chromaticAberration = 0.03f;
        public float vignette = 0.16f;
        public float bloom = 1f;                // lens glare (veiling glare from the zoom's many elements), x
        public float bloomScatter = 0.75f;      // glare spread
        public float purpleFringe = 0.3f;       // blue-violet edges around blown-out lights

        // Focus
        public bool autofocus = true;
        public float aperture = 1.8f;           // widest aperture (f-number); the iris closes from here
        public float focusSpeed = 1f;           // focus motor speed, x
        public float focusHunting = 0.5f;       // how readily it hunts and overshoots
        public float focusBreathing = 1f;       // the picture's slight zoom as the focus moves (a video zoom lens's breathing), x

        // Exposure: iris first, then gain, then (optionally) a slower shutter
        public bool autoExposure = true;
        public float exposureBias = 0f;         // EV offset of the metering target (not gain)
        public float exposureSpeed = 1f;        // iris speed, x
        public float exposureCompensation = 0.85f; // how fully the iris and gain correct (1 = fully)
        public float maxGain = 3f;              // AGC limit in EV (3 = +18 dB)
        public int shutter = 0;                 // index into ShutterNames (0 = 1/60, the auto shutter)
        public bool slowShutter = false;        // low-light 1/30 s (the picture repeats every other field)
        public float fluorescentFlicker = 0.3f; // tube flicker depth (shows only with a shutter faster than 1/60)

        // White balance
        public bool autoWhiteBalance = true;
        public float awbSpeed = 1f;
        public float awbStrength = 0.3f;        // how far it pulls toward grey (1 = fully, which would turn the yellow rooms grey)
        public float whiteBalanceTemp = 0f;     // shift on top of the auto white balance (+ warmer)
        public float whiteBalanceTint = 0f;     // (+ magenta, - green)

        // Sensor
        public float noise = 1f;                // read noise, x
        public float gainNoise = 1f;            // noise growth with gain, x
        public float chromaNoise = 1f;          // colour (filter) noise, x
        public float smear = 1f;                // CCD vertical smear through bright lights, x
        public float fixedPattern = 1f;         // fixed-pattern noise and hot pixels, x
        public float noiseReduction = 0f;       // temporal (3D) noise reduction in low light: ghost trails

        // Camera processing
        public int videoLines = 480;            // recording raster lines (width follows the frame shape)
        public float softness = 0.6f;           // optics + optical low-pass filter, in raster pixels
        public float sharpening = 1f;           // edge enhancement (camera aperture correction + VCR peaking), x
        public float chromaDetail = 1f;         // colour resolution, x
        public float chromaBleed = 1f;          // analog colour trailing to the right, x
        public float chromaShift = 1f;          // colour delay, x
        public float highlightKnee = 0.85f;     // knee point (bright light compressed above it); 1 = no knee
        public float whiteClip = 1f;            // hard white clip (1 = 100 IRE)
        public float highlightColour = 0.8f;    // highlight chroma suppression starts at this level: colour fades to none at the clip
        public float pedestal = 0.075f;         // setup: black level (7.5 IRE on analog NTSC)

        // Colour
        public float saturation = 0f;           // camera colour matrix, percent
        public float contrast = 0f;             // camera gamma (0 = the standard 0.45)

        // Motion
        public float shakeIntensity = 1.23f;
        public float breathing = 1.48f;
        public float stabilization = 0f;
        public float motionBlur = 1f;           // shutter motion blur, x (1 = exactly what the shutter time gives)
        public float lookOvershoot = 0.5f;
        public float lookJitter = 0.5f;
        public float handheldRoughness = 0f;
        public bool recordedWalk = true;        // walking plays the phone recording of a carried camera (HandheldCameraShake)
        public int handsInView = 1;             // 0 full, 1 low (held things at the bottom edge), 2 off (no hand drawn): PropGrabber
        public int body = 0;                    // 0 full body, 1 hands only (arms while they do something), 2 none (a crosshair): PropGrabber.bodyMode

        // Format and playback
        public int format = 0;                  // CamcorderFormat.Names
        public float timebaseJitter = 1f;       // x
        public float tapeNoise = 1f;            // x
        public float dropouts = 1f;             // x
        public float chromaPhaseNoise = 1f;     // x
        public float crossColour = 1f;          // x
        public float headSwitching = 1f;        // x
        public float trackingNoise = 0f;        // a mistracking noise bar (worn tape), 0..1
        public float blockiness = 1f;           // DV compression, x
        public float recordFps = 0f;            // recording frame rate (0 = every rendered frame)
        public bool interlace = false;          // show fields (bob): thin horizontal edges twitter
        public float lineDoubling = 0f;         // each recorded line shown as a band instead of smoothly scaled
        public float bottomLine = 0f;           // the episode's thin lilac line along the bottom edge
        public float scanlines = 0f;            // CRT scanlines (a TV, not the recording)
        public bool aspect43 = true;
        public bool showOSD = false;
        public float osdCurvature = 0f;         // viewfinder overlay bent like the eyepiece (0 = flat)
        public int frameRateCap = 0;            // legacy; frame pacing moved to GraphicsOptions

        // Audio: the camcorder's microphone and recording chain (independent of the visual exposure).
        public float audioVolume = 0.8f;
        public bool audioProcessing = true;
        public float micColoration = 0.85f;
        public float autoGain = 0.8f;
        public float micHiss = 0.036f;
        public float handlingNoise = 0.6f;
        public float stereoWidth = 0.55f;

        public static readonly string[] ShutterNames = { "1/60", "1/100", "1/250", "1/500", "1/1000" };
        static readonly float[] ShutterSeconds = { 1f / 60f, 1f / 100f, 1f / 250f, 1f / 500f, 1f / 1000f };
        /// <summary>The chosen shutter time in seconds.</summary>
        public float ShutterTime => ShutterSeconds[Mathf.Clamp(shutter, 0, ShutterSeconds.Length - 1)];

        public CamcorderSettings Clone() => (CamcorderSettings)MemberwiseClone();

        const string PrefsKey = "Backrooms.CamcorderSettings.v1";
        const string BackupKey = "Backrooms.CamcorderSettings.v1.BeforeLook2";
        const string HighlightKey = "Backrooms.CamcorderSettings.RefMatchHighlight";

        public static CamcorderSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var s = JsonUtility.FromJson<CamcorderSettings>(json);
                    if (s.lookVersion < CurrentLookVersion)
                    {
                        // The picture settings changed units: keep a copy of the old ones, convert, save.
                        if (!PlayerPrefs.HasKey(BackupKey)) PlayerPrefs.SetString(BackupKey, json);
                        s = Migrate(s, CurrentPreset);
                        s.Save();
                    }
                    else if (CurrentPreset == 7 && !PlayerPrefs.HasKey(HighlightKey))
                    {
                        // Once: the Reference Match preset took the footage's highlight colour (2026-10-27). Only that value
                        // changes, and only if it was still the default; everything else stays as the player left it.
                        PlayerPrefs.SetInt(HighlightKey, 1);
                        if (Mathf.Approximately(s.highlightColour, 0.8f)) { s.highlightColour = BuiltIn(7).highlightColour; s.Save(); }
                    }
                    return s;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load camcorder settings: " + e.Message); }
            return UserDefault();
        }

        /// <summary>
        /// Settings saved before the camcorder was rebuilt as a real signal chain: the picture takes the new values of the
        /// preset that was in use; the frame shape, field of view, handheld motion, recording frame rate and audio are kept.
        /// </summary>
        public static CamcorderSettings Migrate(CamcorderSettings old, int preset)
        {
            var n = BuiltIn(preset);
            n.fieldOfView = old.fieldOfView; n.aspect43 = old.aspect43; n.showOSD = old.showOSD; n.autofocus = old.autofocus;
            n.autoExposure = old.autoExposure;
            n.shakeIntensity = old.shakeIntensity; n.breathing = old.breathing; n.stabilization = old.stabilization;
            n.lookOvershoot = old.lookOvershoot; n.lookJitter = old.lookJitter; n.handheldRoughness = old.handheldRoughness;
            n.recordedWalk = old.recordedWalk;
            n.handsInView = old.handsInView;
            n.body = old.body;
            n.recordFps = old.recordFps; n.lineDoubling = old.lineDoubling; n.bottomLine = old.bottomLine;
            if (old.videoLines > 0) n.videoLines = old.videoLines;
            n.CopyAudioFrom(old);
            n.lookVersion = CurrentLookVersion;
            return n;
        }

        const string DefaultKey = "Backrooms.CamcorderSettings.UserDefault";

        /// <summary>The player's saved default, or the built-in one.</summary>
        public static CamcorderSettings UserDefault()
        {
            try
            {
                string json = PlayerPrefs.GetString(DefaultKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var s = JsonUtility.FromJson<CamcorderSettings>(json);
                    if (s.lookVersion >= CurrentLookVersion) return s;
                    PlayerPrefs.DeleteKey(DefaultKey);   // in the old units: the built-in default replaces it
                }
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load the saved default: " + e.Message); }
            return BuiltIn(0);
        }

        public void Save()
        {
            lookVersion = CurrentLookVersion;
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        /// <summary>Copies only the audio fields (presets are applied per tab).</summary>
        public void CopyAudioFrom(CamcorderSettings o)
        {
            audioVolume = o.audioVolume; audioProcessing = o.audioProcessing; micColoration = o.micColoration;
            autoGain = o.autoGain; micHiss = o.micHiss; handlingNoise = o.handlingNoise; stereoWidth = o.stereoWidth;
        }

        /// <summary>Sets the format and the values that come with it (black level, noise reduction).</summary>
        public void SetFormat(int f)
        {
            format = Mathf.Clamp(f, 0, CamcorderFormat.Names.Length - 1);
            var spec = CamcorderFormat.Get(format);
            pedestal = spec.setup;
        }

        // ---------------- Presets ----------------

        public static readonly string[] PresetNames = { "Default", "Hi8 '98", "MiniDV '04", "Low light gain", "Clean", "VHS '96", "Found footage", "Reference Match" };

        // Saved presets. The built-in values below are only the starting point: SET AS DEFAULT stores the current settings
        // as the preset in use. In the editor that goes into the project (Resources/CamcorderPresets.json, so it ships with
        // the game); in a built game, into that player's PlayerPrefs (which then win over the project's).
        [Serializable] class PresetEntry { public string name; public CamcorderSettings settings; }
        [Serializable] class PresetFile { public System.Collections.Generic.List<PresetEntry> presets = new System.Collections.Generic.List<PresetEntry>(); }
        const string PresetResource = "CamcorderPresets", PresetFilePath = "Assets/Backrooms/Resources/CamcorderPresets.json";
        const string CurrentPresetKey = "Backrooms.CamcorderSettings.Preset", PlayerPresetKey = "Backrooms.CamcorderPreset.";
        static PresetFile s_presetFile;

        /// <summary>The preset last chosen (what SET AS DEFAULT saves into).</summary>
        public static int CurrentPreset
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(CurrentPresetKey, 0), 0, PresetNames.Length - 1);
            set { PlayerPrefs.SetInt(CurrentPresetKey, Mathf.Clamp(value, 0, PresetNames.Length - 1)); PlayerPrefs.Save(); }
        }

        static PresetFile Presets()
        {
            if (s_presetFile != null) return s_presetFile;
            s_presetFile = new PresetFile();
            var ta = Resources.Load<TextAsset>(PresetResource);
            if (ta)
            {
                try { s_presetFile = JsonUtility.FromJson<PresetFile>(ta.text) ?? new PresetFile(); }
                catch (Exception e) { Debug.LogWarning("[Backrooms] Could not read the camcorder presets: " + e.Message); }
            }
            return s_presetFile;
        }

        /// <summary>The saved values of a preset (the player's, then the project's), or null if it only has the built-in ones
        /// (or was saved in the old units).</summary>
        static CamcorderSettings Saved(int index)
        {
            string name = PresetNames[index];
#if !UNITY_EDITOR
            string json = PlayerPrefs.GetString(PlayerPresetKey + name, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { var p = JsonUtility.FromJson<CamcorderSettings>(json); if (p.lookVersion >= CurrentLookVersion) return p; } catch (Exception) { }
            }
#endif
            foreach (var e in Presets().presets)
                if (e.name == name && e.settings != null && e.settings.lookVersion >= CurrentLookVersion) return e.settings.Clone();
            return null;
        }

        /// <summary>Stores settings as a preset's values (see above for where).</summary>
        public static void SavePreset(int index, CamcorderSettings settings)
        {
            index = Mathf.Clamp(index, 0, PresetNames.Length - 1);
            string name = PresetNames[index];
            var copy = settings.Clone();
            copy.lookVersion = CurrentLookVersion;
#if UNITY_EDITOR
            var file = Presets();
            file.presets.RemoveAll(e => e.name == name);
            file.presets.Add(new PresetEntry { name = name, settings = copy });
            System.IO.File.WriteAllText(PresetFilePath, JsonUtility.ToJson(file, true));
            UnityEditor.AssetDatabase.ImportAsset(PresetFilePath);
#else
            PlayerPrefs.SetString(PlayerPresetKey + name, JsonUtility.ToJson(copy));
            PlayerPrefs.Save();
#endif
        }

        public static CamcorderSettings Preset(int index)
        {
            var saved = Saved(index);
            if (saved != null) return saved;
            if (index == 0) return UserDefault();
            return BuiltIn(index);
        }

        /// <summary>The built-in values of a preset (index 0: the default camcorder).</summary>
        public static CamcorderSettings BuiltIn(int index)
        {
            var s = new CamcorderSettings { lookVersion = CurrentLookVersion };
            switch (index)
            {
                case 1: // Hi8: sharper than VHS, the same colour-under, metal tape (fewer dropouts, more than VHS's noise floor would suggest)
                    s.SetFormat(2);
                    s.fieldOfView = 58f; s.lensDistortion = 0.12f; s.chromaticAberration = 0.04f; s.vignette = 0.18f;
                    s.whiteBalanceTemp = 4f;
                    s.micColoration = 0.8f; s.autoGain = 0.75f; s.micHiss = 0.35f; s.handlingNoise = 0.6f; s.stereoWidth = 0.6f;
                    break;
                case 2: // MiniDV: digital (no tape noise), crisper edge enhancement, 4:1:1 colour, 3D noise reduction, no setup
                    s.SetFormat(3);
                    s.lensDistortion = 0.1f; s.vignette = 0.12f; s.noiseReduction = 0.6f; s.purpleFringe = 0.4f;
                    s.whiteBalanceTint = -3f;
                    s.micColoration = 0.45f; s.autoGain = 0.6f; s.micHiss = 0.12f; s.handlingNoise = 0.45f; s.stereoWidth = 0.9f;
                    break;
                case 3: // Low light: low-lux mode (slow shutter), noise reduction, slower iris
                    s.slowShutter = true; s.noiseReduction = 0.4f; s.exposureSpeed = 0.6f;
                    s.autoGain = 1f; s.micHiss = 0.35f;   // the mic gain also rides up in quiet places
                    break;
                case 4: // Clean: no camcorder picture at all, for comparison
                    s.camcorderLook = false;
                    s.lensDistortion = 0f; s.chromaticAberration = 0f; s.vignette = 0f; s.bloom = 0.5f;
                    s.shakeIntensity = 0.5f; s.autofocus = false; s.showOSD = false; s.lookOvershoot = 0f; s.lookJitter = 0f;
                    s.aspect43 = false; s.fieldOfView = 62f;
                    s.micColoration = 0f; s.autoGain = 0f; s.micHiss = 0f; s.handlingNoise = 0f; s.stereoWidth = 1f;
                    break;
                case 5: // VHS '96: VHS-C SP through a VCR, warm-green fluorescent cast left in, heavy lens glare
                    s.SetFormat(0);
                    s.fieldOfView = 60f; s.lensDistortion = 0.18f; s.chromaticAberration = 0.05f; s.vignette = 0.2f;
                    s.bloom = 1.3f; s.bloomScatter = 0.85f; s.trackingNoise = 0.08f;
                    s.whiteBalanceTemp = 6f; s.whiteBalanceTint = -4f; s.awbStrength = 0.2f;
                    s.micColoration = 0.85f; s.autoGain = 0.8f; s.micHiss = 0.4f; s.handlingNoise = 0.6f; s.stereoWidth = 0.55f;
                    break;
                case 6: // Found footage: a worn EP tape from a cheap camcorder with a wide converter
                    s.SetFormat(1);
                    s.fieldOfView = 60f; s.lensDistortion = 0.18f; s.chromaticAberration = 0.05f; s.vignette = 0.22f;
                    s.bloom = 1.4f; s.bloomScatter = 0.9f; s.purpleFringe = 0.4f;
                    s.timebaseJitter = 1.4f; s.dropouts = 2f; s.headSwitching = 1.2f; s.trackingNoise = 0.15f; s.tapeNoise = 1.3f;
                    s.exposureSpeed = 0.7f; s.whiteBalanceTemp = 4f; s.whiteBalanceTint = -3f;
                    s.micColoration = 0.8f; s.autoGain = 0.75f; s.micHiss = 0.3f; s.handlingNoise = 0.6f; s.stereoWidth = 0.6f;
                    break;
                case 7: // Reference Match: tuned against the episode (Docs/ReferenceMatch.md): about 536 recorded lines shown
                        // line-doubled, digital (faint 8 x 8 compression blocks), very little visible noise, a thin lilac line at
                        // the bottom edge, walking jitter of ~1.5-1.9 px at 4 bounces a second.
                    s.SetFormat(3);
                    s.fieldOfView = 75f; s.lensDistortion = 0.12f; s.chromaticAberration = 0.02f; s.vignette = 0.1f;
                    s.bloom = 0.6f; s.bloomScatter = 0.6f;
                    s.videoLines = 536; s.lineDoubling = 1f; s.bottomLine = 1f; s.handheldRoughness = 1.3f;
                    s.sharpening = 0.7f; s.softness = 0.8f; s.blockiness = 0.6f; s.noise = 0.4f; s.gainNoise = 0.5f;
                    s.pedestal = 0.02f; s.highlightKnee = 0.9f; s.exposureBias = 0.25f; s.maxGain = 1f;
                    s.highlightColour = 0.6f;    // the footage's bright walls go pale cream: half their colour at 80 %, measured
                    s.whiteBalanceTint = -20f;   // and its yellow leans 4-5 degrees greener than the game's (hue 47-50, measured)
                    s.autofocus = false; s.aspect43 = false; s.showOSD = false; s.awbStrength = 0.15f;
                    s.micColoration = 0.8f; s.autoGain = 0.75f; s.micHiss = 0.3f; s.handlingNoise = 0.6f; s.stereoWidth = 0.6f;
                    break;
            }
            return s;
        }
    }
}
