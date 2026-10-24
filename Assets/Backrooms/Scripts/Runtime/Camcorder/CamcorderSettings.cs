using System;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// Every user-tunable aspect of the camcorder look, grouped the way a real camera's signal chain is:
    /// lens -> exposure/iris -> sensor gain -> image processing -> colour -> recording format -> OSD.
    /// Serialised to PlayerPrefs as JSON.
    /// </summary>
    [Serializable]
    public class CamcorderSettings
    {
        // Defaults are the look the player tuned by hand in the F5 panel (saved from their settings, 2026-10-23).

        // Lens
        public float fieldOfView = 54.5f;
        public float lensDistortion = 0.252f;
        public float chromaticAberration = 0.081f;
        public float vignette = 0.316f;
        public float bloom = 1.276f;
        public float bloomScatter = 0.646f;

        // Focus
        public bool autofocus = true;
        public float aperture = 4f;          // f-number for the depth-of-field effect
        public float focusSpeed = 0.799f;      // how fast the AF motor drives
        public float focusHunting = 0.317f;   // overshoot and settling when focus changes

        // Exposure / iris
        public bool autoExposure = true;
        public float exposureBias = 0.5f;      // EV
        public float exposureSpeed = 0.642f;     // multiplier on iris response
        public float maxGain = 1.6f;        // EV the auto-iris may open beyond nominal
        public float fluorescentFlicker = 0.25f; // shutter beating against 60 Hz tubes

        // Sensor gain noise
        public float noise = 0.017f;
        public float gainNoise = 0.1603f;      // extra noise per EV of gain
        public float chromaNoise = 0.08f;
        public float grainSize = 1.2f;       // 1 = 720-line sensor; larger = coarser, clumpier grain

        // Image processing
        public int videoLines = 0;           // simulated recording resolution in lines (0 = the display's own resolution)
        public float chromaDetail = 0.5f;    // colour resolution as a fraction of the recorded luma resolution
        public float sharpening = 4f;
        public float softness = 3f;       // optical + resolution softness, in pixels
        public float chromaBleed = 10f;     // horizontal chroma smear, in pixels
        public float chromaShift = 3f;
        public float highlightKnee = 1f;
        public float pedestal = 0.0698f;

        // Colour
        public float whiteBalanceTemp = 1.9f;
        public float whiteBalanceTint = -5.2f;
        public float saturation = 10.1f;
        public float contrast = 22.8f;

        // Motion
        public float shakeIntensity = 1.23f;
        public float breathing = 1.48f;
        public float stabilization = 0f;
        public float sensorBlur = 0.8f;       // directional smear from how fast the camera turns (shutter open for part of each frame)
        public float motionBlur = 0.344f;
        public float lookOvershoot = 0.5f;   // camera carries on a touch when you stop turning
        public float lookJitter = 0.5f;      // small wobble while turning
        public float handheldRoughness = 0f; // fast irregular hand shake on top of the smooth bob (0 = off)

        // Format
        public float scanlines = 0.0977f;
        public float headSwitching = 0.4f;   // the flickering blue/magenta noise band along the bottom of VHS footage
        public float blockiness = 0.25f;     // blotchy compression: chroma and brightness held in blocks that change every few frames
        public float recordFps = 0f;         // recording frame rate: the picture only changes this often (0 = every rendered frame)
        public float lineDoubling = 0f;      // each recorded line shown twice (stair-stepped edges), as in field-doubled SD video
        public float colourFlicker = 0f;     // each recorded frame is a touch lighter/darker and tinted differently, like film or a tired sensor (0 = off)
        public float bottomLine = 0f;        // a thin steady lilac line along the very bottom edge (the episode's head-switching line)
        public float trackingNoise = 0.154f;
        public bool aspect43 = true;
        public bool showOSD = false;
        public float osdCurvature = 0.961f;    // viewfinder overlay bends with the lens (0 = flat)
        public int frameRateCap = 0;         // legacy; frame pacing moved to GraphicsOptions

        // Audio: the camcorder's microphone and recording chain (independent of the visual exposure).
        // New fields keep their defaults when older saved settings are loaded.
        public float audioVolume = 0.8f;     // output volume, applied last
        public bool audioProcessing = true;  // camcorder chain on (off = clean reference)
        public float micColoration = 0.85f;   // bass cut + softened highs
        public float autoGain = 0.8f;        // AGC strength
        public float micHiss = 0.036f;          // mic self-noise
        public float handlingNoise = 0.6f;   // body noise from walking
        public float stereoWidth = 0.55f;     // 0 mono, 1 natural

        public CamcorderSettings Clone() => (CamcorderSettings)MemberwiseClone();

        const string PrefsKey = "Backrooms.CamcorderSettings.v1";

        public static CamcorderSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<CamcorderSettings>(json);
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load camcorder settings: " + e.Message); }
            return UserDefault();
        }

        const string DefaultKey = "Backrooms.CamcorderSettings.UserDefault";

        /// <summary>Remembers the current settings as the player's own default: RESET, the "Default" preset and a fresh start use them.</summary>
        public void SaveAsDefault()
        {
            PlayerPrefs.SetString(DefaultKey, JsonUtility.ToJson(this));
            Save();
        }

        public static bool HasUserDefault => PlayerPrefs.HasKey(DefaultKey);

        /// <summary>The player's saved default, or the built-in one.</summary>
        public static CamcorderSettings UserDefault()
        {
            try
            {
                string json = PlayerPrefs.GetString(DefaultKey, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<CamcorderSettings>(json);
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load the saved default: " + e.Message); }
            return new CamcorderSettings();
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        /// <summary>Copies only the audio fields (presets are applied per tab).</summary>
        public void CopyAudioFrom(CamcorderSettings o)
        {
            audioVolume = o.audioVolume; audioProcessing = o.audioProcessing; micColoration = o.micColoration;
            autoGain = o.autoGain; micHiss = o.micHiss; handlingNoise = o.handlingNoise; stereoWidth = o.stereoWidth;
        }

        // ---------------- Presets ----------------

        public static readonly string[] PresetNames = { "Default", "Hi8 '98", "MiniDV '04", "Low light gain", "Clean", "VHS '96", "Found footage", "Reference Match" };

        public static CamcorderSettings Preset(int index)
        {
            var s = new CamcorderSettings();
            if (index == 0) return UserDefault();
            switch (index)
            {
                case 1: // Hi8: soft, smeared chroma, lifted blacks, warm, visible scanlines, 4:3
                    s.softness = 1.6f; s.sharpening = 1.3f; s.chromaBleed = 6f; s.chromaShift = 1.6f;
                    s.pedestal = 0.045f; s.highlightKnee = 0.8f; s.noise = 0.03f; s.chromaNoise = 0.06f;
                    s.saturation = -12f; s.contrast = 8f; s.whiteBalanceTemp = 14f; s.whiteBalanceTint = -2f;
                    s.scanlines = 0.06f; s.trackingNoise = 0.25f; s.aspect43 = true; s.vignette = 0.32f;
                    s.chromaticAberration = 0.12f; s.lensDistortion = -0.12f; s.fieldOfView = 58f;
                    s.micColoration = 0.8f; s.autoGain = 0.75f; s.micHiss = 0.35f; s.handlingNoise = 0.6f; s.stereoWidth = 0.6f;
                    break;
                case 2: // MiniDV: sharper, crunchy edge enhancement, cooler, hard highlight clip
                    s.softness = 0.5f; s.sharpening = 1.8f; s.chromaBleed = 2.2f; s.chromaShift = 0.4f;
                    s.pedestal = 0.012f; s.highlightKnee = 0.94f; s.noise = 0.014f; s.chromaNoise = 0.02f;
                    s.saturation = 4f; s.contrast = 22f; s.whiteBalanceTemp = 2f; s.whiteBalanceTint = -7f;
                    s.vignette = 0.18f; s.motionBlur = 0.05f; s.scanlines = 0f; s.trackingNoise = 0f;
                    s.gainNoise = 0.05f; s.grainSize = 0.8f; s.bloom = 0.5f;
                    s.micColoration = 0.45f; s.autoGain = 0.6f; s.micHiss = 0.12f; s.handlingNoise = 0.45f; s.stereoWidth = 0.9f;
                    break;
                case 3: // Low light: aggressive gain, heavy noise, slow iris
                    s.maxGain = 1.6f; s.gainNoise = 0.08f; s.noise = 0.03f; s.chromaNoise = 0.08f;
                    s.exposureSpeed = 0.6f; s.softness = 1.2f; s.saturation = -20f; s.pedestal = 0.03f;
                    s.autoGain = 1f; s.micHiss = 0.35f;   // the mic gain also rides up in quiet places
                    break;
                case 4: // Clean: almost no video character, for comparison
                    s.lensDistortion = 0f; s.chromaticAberration = 0f; s.vignette = 0.1f; s.noise = 0f; s.gainNoise = 0f;
                    s.chromaNoise = 0f; s.sharpening = 0f; s.softness = 0f; s.chromaBleed = 0f; s.chromaShift = 0f;
                    s.pedestal = 0f; s.highlightKnee = 1f; s.fluorescentFlicker = 0f; s.motionBlur = 0f;
                    s.shakeIntensity = 0.5f; s.autofocus = false; s.showOSD = false; s.lookOvershoot = 0f; s.lookJitter = 0f;
                    s.scanlines = 0f; s.trackingNoise = 0f; s.aspect43 = false; s.bloom = 0.3f; s.lensDistortion = 0f;
                    s.saturation = 0f; s.whiteBalanceTint = 0f; s.fieldOfView = 62f;
                    // Clean audio reference: chain on but neutral (no colour, AGC, hiss or handling; natural width).
                    s.micColoration = 0f; s.autoGain = 0f; s.micHiss = 0f; s.handlingNoise = 0f; s.stereoWidth = 1f;
                    break;
                case 5: // VHS '96, matched to the owner's reference frames: very soft, haloed edges, yellow-green
                        // fluorescent cast, tubes blown out to white with wide glow, smeared colour, 4:3
                    s.softness = 2.2f; s.sharpening = 1.7f; s.chromaBleed = 8f; s.chromaShift = 1.8f;
                    s.pedestal = 0.025f; s.highlightKnee = 0.7f; s.noise = 0.035f; s.chromaNoise = 0.08f; s.gainNoise = 0.07f;
                    s.saturation = 6f; s.contrast = 14f; s.whiteBalanceTemp = 18f; s.whiteBalanceTint = -12f;
                    s.bloom = 1.2f; s.bloomScatter = 0.85f; s.vignette = 0.34f;
                    s.scanlines = 0.04f; s.trackingNoise = 0.18f; s.aspect43 = true;
                    s.chromaticAberration = 0.18f; s.lensDistortion = -0.18f; s.fieldOfView = 60f; s.maxGain = 1.4f;
                    s.micColoration = 0.85f; s.autoGain = 0.8f; s.micHiss = 0.4f; s.handlingNoise = 0.6f; s.stereoWidth = 0.55f;
                    break;
                case 6: // Found footage: the frames of the owner's reference series. Soft and low-res, flat and pale, lifted
                        // blacks, blown-out glowing tubes, wide barrel lens, smeared colour, a lot of motion blur.
                    s.fieldOfView = 68f; s.lensDistortion = -0.16f; s.chromaticAberration = 0.12f; s.vignette = 0.2f;
                    s.bloom = 1.4f; s.bloomScatter = 0.9f;
                    s.softness = 3.2f; s.sharpening = 0f; s.chromaBleed = 7f; s.chromaShift = 1.2f;
                    s.pedestal = 0.07f; s.highlightKnee = 0.66f; s.noise = 0.05f; s.chromaNoise = 0.05f; s.gainNoise = 0.06f;
                    s.saturation = -8f; s.contrast = 2f; s.whiteBalanceTemp = 12f; s.whiteBalanceTint = -6f;
                    s.motionBlur = 0.4f; s.sensorBlur = 1.8f; s.headSwitching = 0.8f; s.blockiness = 0.5f; s.exposureBias = 0.35f; s.maxGain = 1.1f;
                    s.scanlines = 0.03f; s.trackingNoise = 0.22f; s.aspect43 = true;
                    s.micColoration = 0.8f; s.autoGain = 0.75f; s.micHiss = 0.3f; s.handlingNoise = 0.6f; s.stereoWidth = 0.6f;
                    break;
                case 7: // Reference Match: tuned against a single reference photograph (see Docs/ReferenceMatch.md).
                    s.fieldOfView = 75f; s.lensDistortion = -0.08f; s.chromaticAberration = 0.02f; s.vignette = 0.1f;
                    s.bloom = 0.35f; s.bloomScatter = 0.6f;
                    // Measured from the episode: about 536 recorded lines shown line-doubled, very soft, 24 frames a second,
                    // almost no visible noise, faint 8 x 8 compression blocks, a thin lilac line at the bottom edge.
                    s.videoLines = 536; s.lineDoubling = 1f; s.recordFps = 24f; s.bottomLine = 1f;
                    s.handheldRoughness = 1.3f;   // walking: ~1.5-1.9 px frame-to-frame jitter, 4 bounces a second, as measured in the episode
                    s.chromaDetail = 0.28f; s.sharpening = 0.7f; s.chromaShift = 0.4f;
                    s.softness = 0.8f; s.chromaBleed = 0f;
                    s.noise = 0.004f; s.gainNoise = 0.01f; s.chromaNoise = 0.005f; s.grainSize = 1f;
                    s.pedestal = 0.02f; s.highlightKnee = 0.9f; s.saturation = 0f; s.contrast = 0f;
                    s.whiteBalanceTemp = 0f; s.whiteBalanceTint = 0f; s.exposureBias = 0.25f; s.maxGain = 0.6f;
                    s.autofocus = false; s.scanlines = 0f; s.trackingNoise = 0f; s.headSwitching = 0f; s.blockiness = 0.15f;
                    s.sensorBlur = 0.5f; s.motionBlur = 0.15f; s.aspect43 = false; s.showOSD = false;
                    s.micColoration = 0.8f; s.autoGain = 0.75f; s.micHiss = 0.3f; s.handlingNoise = 0.6f; s.stereoWidth = 0.6f;
                    break;
            }
            return s;
        }
    }
}
