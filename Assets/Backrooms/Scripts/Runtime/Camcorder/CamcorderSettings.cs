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
        // Defaults are the look the player tuned by hand in the F5 panel (2026-10-20).

        // Lens
        public float fieldOfView = 50f;
        public float lensDistortion = 0.2f;
        public float chromaticAberration = 0.15f;
        public float vignette = 0.24f;
        public float bloom = 0.875f;
        public float bloomScatter = 0.71f;

        // Focus
        public bool autofocus = true;
        public float aperture = 4f;          // f-number for the depth-of-field effect
        public float focusSpeed = 0.8f;      // how fast the AF motor drives
        public float focusHunting = 0.35f;   // overshoot and settling when focus changes

        // Exposure / iris
        public bool autoExposure = true;
        public float exposureBias = 0f;      // EV
        public float exposureSpeed = 1f;     // multiplier on iris response
        public float maxGain = 0.35f;        // EV the auto-iris may open beyond nominal
        public float fluorescentFlicker = 0.25f; // shutter beating against 60 Hz tubes

        // Sensor gain noise
        public float noise = 0.062f;
        public float gainNoise = 0.10f;      // extra noise per EV of gain
        public float chromaNoise = 0.032f;
        public float grainSize = 1.2f;       // 1 = 720-line sensor; larger = coarser, clumpier grain

        // Image processing
        public float sharpening = 3f;
        public float softness = 1.78f;       // optical + resolution softness, in pixels
        public float chromaBleed = 5.1f;     // horizontal chroma smear, in pixels
        public float chromaShift = 1.4f;
        public float highlightKnee = 0.88f;
        public float pedestal = 0.02f;

        // Colour
        public float whiteBalanceTemp = 0f;
        public float whiteBalanceTint = 10f;
        public float saturation = 11.5f;
        public float contrast = 11.6f;

        // Motion
        public float shakeIntensity = 1.23f;
        public float breathing = 1.48f;
        public float stabilization = 0f;
        public float motionBlur = 0.12f;

        // Format
        public float scanlines = 0.049f;
        public float trackingNoise = 0.345f;
        public bool aspect43 = true;
        public bool showOSD = true;
        public int frameRateCap = 60;        // 0 = unlimited

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
            return new CamcorderSettings();
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        // ---------------- Presets ----------------

        public static readonly string[] PresetNames = { "Default", "Hi8 '98", "MiniDV '04", "Low light gain", "Clean" };

        public static CamcorderSettings Preset(int index)
        {
            var s = new CamcorderSettings();
            switch (index)
            {
                case 1: // Hi8: soft, smeared chroma, lifted blacks, warm, visible scanlines, 4:3
                    s.softness = 1.6f; s.sharpening = 1.3f; s.chromaBleed = 6f; s.chromaShift = 1.6f;
                    s.pedestal = 0.045f; s.highlightKnee = 0.8f; s.noise = 0.03f; s.chromaNoise = 0.06f;
                    s.saturation = -12f; s.contrast = 8f; s.whiteBalanceTemp = 14f; s.whiteBalanceTint = -2f;
                    s.scanlines = 0.06f; s.trackingNoise = 0.25f; s.aspect43 = true; s.vignette = 0.32f;
                    s.chromaticAberration = 0.12f; s.lensDistortion = -0.12f; s.fieldOfView = 58f;
                    break;
                case 2: // MiniDV: sharper, crunchy edge enhancement, cooler, hard highlight clip
                    s.softness = 0.5f; s.sharpening = 1.8f; s.chromaBleed = 2.2f; s.chromaShift = 0.4f;
                    s.pedestal = 0.012f; s.highlightKnee = 0.94f; s.noise = 0.014f; s.chromaNoise = 0.02f;
                    s.saturation = 4f; s.contrast = 22f; s.whiteBalanceTemp = 2f; s.whiteBalanceTint = -7f;
                    s.vignette = 0.18f; s.motionBlur = 0.05f; s.scanlines = 0f; s.trackingNoise = 0f;
                    s.gainNoise = 0.05f; s.grainSize = 0.8f; s.bloom = 0.5f;
                    break;
                case 3: // Low light: aggressive gain, heavy noise, slow iris
                    s.maxGain = 1.6f; s.gainNoise = 0.08f; s.noise = 0.03f; s.chromaNoise = 0.08f;
                    s.exposureSpeed = 0.6f; s.softness = 1.2f; s.saturation = -20f; s.pedestal = 0.03f;
                    break;
                case 4: // Clean: almost no video character, for comparison
                    s.lensDistortion = 0f; s.chromaticAberration = 0f; s.vignette = 0.1f; s.noise = 0f; s.gainNoise = 0f;
                    s.chromaNoise = 0f; s.sharpening = 0f; s.softness = 0f; s.chromaBleed = 0f; s.chromaShift = 0f;
                    s.pedestal = 0f; s.highlightKnee = 1f; s.fluorescentFlicker = 0f; s.motionBlur = 0f;
                    s.shakeIntensity = 0.5f; s.autofocus = false; s.showOSD = false;
                    s.scanlines = 0f; s.trackingNoise = 0f; s.aspect43 = false; s.bloom = 0.3f; s.lensDistortion = 0f;
                    s.saturation = 0f; s.whiteBalanceTint = 0f; s.fieldOfView = 62f;
                    break;
            }
            return s;
        }
    }
}
