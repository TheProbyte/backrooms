using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms
{
    /// <summary>
    /// A camcorder's automatic exposure and white balance. The camera meters its own picture (an 80 x 60 average of the
    /// exposed recording raster, read back from the GPU every other frame): an arithmetic, centre-weighted mean of the
    /// clipped signal, the top of the frame (ceiling tubes) counting less. It corrects with the iris first (seven stops,
    /// a damped spring with a small dead band), then once the iris is wide open with the AGC gain (about 6 dB a second up,
    /// faster down, and down first when the light returns), and in low-lux mode with a slower 1/30 s shutter.
    /// The white balance pulls slowly toward grey along the warm/cool axis, within limits, like a 1990s camcorder's.
    /// <see cref="Backrooms.Camcorder.CamcorderRig"/> drives the settings and hands the result to the picture.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CamcorderAutoExposure : MonoBehaviour
    {
        [Tooltip("Volume whose Color Adjustments post exposure is used when the camcorder picture is off.")]
        public Volume volume;

        [Tooltip("Average exposed level the camera aims for (linear; 0.18 is about 45 IRE of video).")]
        [Range(0.05f, 0.4f)] public float targetLevel = 0.18f;
        [Tooltip("Exposure (linear multiplier) with the iris wide open and no gain. Calibrated so a lit corridor sits about two stops closed.")]
        public float openExposure = 3.2f;
        [Tooltip("Stops the iris (and its ND) can close.")]
        public float irisRange = 7f;
        [Tooltip("Exposure the camera would pick for a typical lit corridor (stops from wide open); partial compensation pulls toward it.")]
        public float nominalEV = -2f;

        // Driven by CamcorderRig from the settings.
        [System.NonSerialized] public bool autoExposure = true;
        [System.NonSerialized] public float exposureBias, speedScale = 1f, compensation = 0.85f, maxGainEV = 3f, flicker;
        [System.NonSerialized] public float shutterTime = 1f / 60f;
        [System.NonSerialized] public bool slowShutter;
        [System.NonSerialized] public bool autoWhiteBalance = true;
        [System.NonSerialized] public float awbSpeed = 1f, awbStrength = 0.35f;
        /// <summary>Jump straight to the target instead of easing (locked exposure for repeatable stills).</summary>
        [System.NonSerialized] public bool snap;
        /// <summary>The meter to read (the camcorder picture's), its exposure, and the frame it was drawn in.</summary>
        [System.NonSerialized] public Backrooms.Camcorder.CamcorderPipeline pipeline;

        /// <summary>Iris position in stops from wide open (0 = open, negative = closed down).</summary>
        public float IrisEV { get; private set; } = -2f;
        /// <summary>AGC gain in stops (1 = +6 dB).</summary>
        public float GainEV { get; private set; }
        /// <summary>Extra stops from the slow shutter (0 or 1).</summary>
        public float ShutterEV { get; private set; }
        /// <summary>Total exposure in stops from wide open with no gain.</summary>
        public float CurrentEV => IrisEV + GainEV + ShutterEV;
        /// <summary>The gain in use (stops); the old name for it.</summary>
        public float LowLightBoost => GainEV;
        /// <summary>Exposure multiplier for the picture (iris, gain, shutter, tube flicker beat).</summary>
        public float Exposure { get; private set; } = 0.8f;
        /// <summary>The shutter time in use (seconds).</summary>
        public float ShutterTimeNow => shutterTime * (ShutterEV > 0.5f ? 2f : 1f);
        /// <summary>Scene luminance from the last meter reading (metered level divided by the exposure that produced it).</summary>
        public float MeasuredLuminance { get; private set; } = -1f;
        /// <summary>The last metered level (exposed).</summary>
        public float MeteredLevel { get; private set; } = -1f;
        /// <summary>White balance gains (luma 1).</summary>
        public Vector3 WhiteBalance { get; private set; } = Vector3.one;
        public int SampleCount { get; private set; }

        ColorAdjustments _color;
        bool _pending;
        float _irisVel, _openShortT, _slowT, _fastT;
        float _evUsedAtRequest;
        Vector3 _awbTarget = Vector3.one;
        float _flickerPhase;

        void Start()
        {
            if (volume && volume.profile.TryGet(out _color)) _color.postExposure.overrideState = true;
            _flickerPhase = Random.value * 6.2831853f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            RequestMeter();

            float target = 0f;
            bool metered = MeteredLevel > 0f;
            if (autoExposure && metered)
            {
                // What the meter says the exposure should be, then partial compensation around the nominal exposure.
                float full = _evUsedAtRequest + Mathf.Log(targetLevel / Mathf.Max(MeteredLevel, 1e-5f), 2f);
                target = nominalEV + compensation * (full - nominalEV) + exposureBias;
            }
            else target = nominalEV + exposureBias;
            float maxShutter = slowShutter ? 1f : 0f;
            target = Mathf.Clamp(target, -irisRange, maxGainEV + maxShutter);

            if (snap || !autoExposure)
            {
                ShutterEV = 0f;
                GainEV = autoExposure ? Mathf.Clamp(target, 0f, maxGainEV) : 0f;
                IrisEV = Mathf.Clamp(target - GainEV, -irisRange, 0f);
                _irisVel = 0f;
            }
            else
            {
                float k = Mathf.Max(0.05f, speedScale);
                float deficit = target - (GainEV + ShutterEV);          // what is missing with the iris wide open
                // Gain: up only once the iris has been wide open and short for a moment; down at once, before the iris moves.
                bool irisOpen = IrisEV > -0.05f;
                _openShortT = irisOpen && deficit > 0.2f ? _openShortT + dt : 0f;
                if (_openShortT > 0.4f) GainEV = Mathf.MoveTowards(GainEV, Mathf.Min(maxGainEV, GainEV + deficit), 1f * k * dt);
                else if (deficit < -0.1f && GainEV > 0f) GainEV = Mathf.MoveTowards(GainEV, Mathf.Max(0f, GainEV + deficit), 2f * k * dt);
                GainEV = Mathf.Clamp(GainEV, 0f, maxGainEV);
                // Slow shutter: when the gain is all used and it is still too dark for a while; back off with hysteresis.
                if (slowShutter)
                {
                    _slowT = GainEV >= maxGainEV - 0.01f && deficit > 0.7f ? _slowT + dt : 0f;
                    _fastT = ShutterEV > 0.5f && target < maxGainEV - 0.3f ? _fastT + dt : 0f;
                    if (_slowT > 1.5f) ShutterEV = 1f;
                    if (_fastT > 2f) ShutterEV = 0f;
                }
                else ShutterEV = 0f;
                // Iris: a damped spring toward what the gain and shutter leave, held open while the gain is coming down.
                float irisTarget = GainEV > 0.001f ? 0f : Mathf.Clamp(target - ShutterEV, -irisRange, 0f);
                float err = irisTarget - IrisEV;
                if (Mathf.Abs(err) > 0.12f || Mathf.Abs(_irisVel) > 0.05f)
                {
                    float w = 6.2831853f * 0.8f * k, zeta = 0.7f;
                    _irisVel += (w * w * err - 2f * zeta * w * _irisVel) * dt;
                    IrisEV += _irisVel * dt;
                }
                if (IrisEV > 0f) { IrisEV = 0f; _irisVel = Mathf.Min(_irisVel, 0f); }
                if (IrisEV < -irisRange) { IrisEV = -irisRange; _irisVel = Mathf.Max(_irisVel, 0f); }
            }

            // Tubes flicker at 120 Hz; a 1/60 s shutter integrates exactly two cycles (no flicker). Faster shutters catch part
            // of a cycle, which beats slowly against the 59.94 Hz field rate (0.12 Hz).
            float T = ShutterTimeNow, x = Mathf.PI * 120f * T;
            float response = Mathf.Abs(Mathf.Sin(x)) / x;
            float beat = flicker * 0.35f * response * Mathf.Sin(6.2831853f * 0.12f * Time.time + _flickerPhase);
            Exposure = openExposure * Mathf.Pow(2f, CurrentEV) * (1f + beat);

            // White balance: ease toward the target from the meter.
            if (!autoWhiteBalance) _awbTarget = Vector3.one;
            float a = snap ? 1f : 1f - Mathf.Exp(-dt * Mathf.Max(0.05f, awbSpeed) / 1.5f);
            WhiteBalance = Vector3.Lerp(WhiteBalance, _awbTarget, a);

            // The camcorder picture applies the exposure itself; without it, URP does.
            bool own = pipeline != null && !pipeline.meterOnly;
            if (_color != null) _color.postExposure.value = own ? 0f : Mathf.Log(Mathf.Max(Exposure, 1e-4f), 2f);
        }

        void RequestMeter()
        {
            if (_pending || pipeline == null || (Time.frameCount & 1) != 0) return;
            var rt = pipeline.Meter;
            if (!rt || pipeline.MeterFrame < 0) return;
            _evUsedAtRequest = Mathf.Log(Mathf.Max(pipeline.MeterExposure, 1e-5f) / openExposure, 2f);
            float eUsed = pipeline.MeterExposure;
            _pending = true;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAHalf, r => OnReadback(r, eUsed));
        }

        void OnReadback(AsyncGPUReadbackRequest req, float eUsed)
        {
            _pending = false;
            if (req.hasError || this == null) return;
            var px = req.GetData<ushort>();
            int W = Backrooms.Camcorder.CamcorderPipeline.MeterWidth, H = Backrooms.Camcorder.CamcorderPipeline.MeterHeight;
            if (px.Length < W * H * 4) return;
            double sum = 0, ws = 0;
            double ar = 0, ag = 0, ab = 0, aw = 0;
            for (int y = 0; y < H; y++)
            {
                float vTop = 1f - (y + 0.5f) / H;
                float vw = vTop < 0.33f ? 0.6f : 1f;
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 4;
                    float r = Mathf.HalfToFloat(px[i]), g = Mathf.HalfToFloat(px[i + 1]), b = Mathf.HalfToFloat(px[i + 2]);
                    if (float.IsNaN(r) || float.IsNaN(g) || float.IsNaN(b)) continue;
                    float u = (x + 0.5f) / W;
                    float w = (0.25f + Mathf.Exp(-((u - 0.5f) * (u - 0.5f) / 0.09f + (vTop - 0.55f) * (vTop - 0.55f) / 0.07f))) * vw;
                    float Y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    sum += w * Mathf.Min(Y, 4f); ws += w;
                    if (Y > 0.03f && Y < 0.85f) { ar += w * r; ag += w * g; ab += w * b; aw += w; }
                }
            }
            if (ws <= 0) return;
            MeteredLevel = (float)(sum / ws);
            MeasuredLuminance = MeteredLevel / Mathf.Max(eUsed, 1e-5f);
            SampleCount++;
            if (aw > 0 && autoWhiteBalance) _awbTarget = AwbGains(new Vector3((float)(ar / aw), (float)(ag / aw), (float)(ab / aw)), awbStrength);
        }

        /// <summary>
        /// A camcorder's AWB: the red and blue gains are pulled toward the green channel's level, each partly (strength) and
        /// within a limited range, from the average mid-tone measured before white balance. Luma stays 1. In a yellow room
        /// red already sits near green, so it mostly lifts blue and the room stays yellow; a warm/cool axis instead (red down
        /// as far as blue goes up) tipped yellow walls toward green (hue 47 to 62-67 degrees, measured).
        /// </summary>
        public static Vector3 AwbGains(Vector3 avg, float strength)
        {
            avg = Vector3.Max(avg, new Vector3(1e-4f, 1e-4f, 1e-4f));
            float k = Mathf.Clamp01(strength);
            const float range = 0.6f;                                      // the gain range of the AWB circuit (e^0.6 = 1.8x)
            float r = Mathf.Clamp(k * Mathf.Log(avg.y / avg.x), -range, range);
            float b = Mathf.Clamp(k * Mathf.Log(avg.y / avg.z), -range, range);
            var g = new Vector3(Mathf.Exp(r), 1f, Mathf.Exp(b));
            float luma = 0.2126f * g.x + 0.7152f * g.y + 0.0722f * g.z;
            return g / Mathf.Max(luma, 1e-3f);
        }
    }
}
