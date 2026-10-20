using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms
{
    /// <summary>
    /// Consumer-camcorder style auto exposure. A tiny probe camera renders the view a few
    /// times per second without post-processing; its average luminance drives
    /// ColorAdjustments.postExposure with a lagging, slightly overshooting response, so
    /// turning into the dark side slowly "opens up" and turning back briefly blooms.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CamcorderAutoExposure : MonoBehaviour
    {
        [Tooltip("Volume whose Color Adjustments post exposure is driven.")]
        public Volume volume;

        [Tooltip("Linear scene luminance the camera tries to reach (lower = darker overall image).")]
        [Range(0.005f, 0.2f)] public float targetLuminance = 0.018f;
        [Tooltip("Limits of the automatic exposure, in EV.")]
        public float minEV = -1.0f, maxEV = 0.35f;
        [Tooltip("How much of the brightness difference is compensated (1 = fully, 0 = off). Camcorders under-compensate.")]
        [Range(0f, 1f)] public float compensation = 0.4f;
        [Tooltip("EV per second when the image needs to get darker.")]
        [Range(0.1f, 5f)] public float darkenSpeed = 1.6f;
        [Tooltip("EV per second when the image needs to get brighter (slower: the iris opens lazily).")]
        [Range(0.1f, 5f)] public float brightenSpeed = 0.55f;
        [Tooltip("Base exposure offset applied on top of the automatic value.")]
        public float exposureBias = 0f;
        [Tooltip("Automatic iris on/off. Off holds the nominal exposure plus bias.")]
        public bool autoExposure = true;
        [Tooltip("Multiplier on both adaptation speeds.")]
        [Range(0.1f, 3f)] public float speedScale = 1f;
        [Tooltip("Subtle brightness beating from the camera shutter against fluorescent mains flicker.")]
        [Range(0f, 1f)] public float flicker = 0f;
        [Tooltip("Probe renders per second.")]
        [Range(1f, 30f)] public float sampleRate = 6f;

        public float CurrentEV { get; private set; }
        /// <summary>Geometric-mean scene luminance from the last probe sample (before exposure).</summary>
        public float MeasuredLuminance { get; private set; } = -1f;
        public int SampleCount { get; private set; }

        Camera _main, _probe;
        RenderTexture _rt;
        ColorAdjustments _color;
        float _nextSample;
        bool _pending;

        void Start()
        {
            _main = GetComponent<Camera>();
            // Volume.profile returns a runtime copy, so play mode never dirties the profile asset.
            if (volume && volume.profile.TryGet(out _color))
                _color.postExposure.overrideState = true;

            _rt = new RenderTexture(64, 36, 16, RenderTextureFormat.ARGBHalf) { name = "ExposureProbe" };
            var go = new GameObject("ExposureProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            _probe = go.AddComponent<Camera>();
            _probe.CopyFrom(_main);
            _probe.enabled = false;
            _probe.targetTexture = _rt;
            var data = _probe.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;
            data.antialiasing = AntialiasingMode.None;
        }

        void Update()
        {
            if (_color == null) return;

            if (!_pending && Time.unscaledTime >= _nextSample)
            {
                _nextSample = Time.unscaledTime + 1f / sampleRate;
                _probe.fieldOfView = _main.fieldOfView;
                var request = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(_probe, request))
                {
                    RenderPipeline.SubmitRenderRequest(_probe, request);
                    _pending = true;
                    AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBAHalf, OnReadback);
                }
            }

            float target = autoExposure && MeasuredLuminance > 0f
                ? Mathf.Clamp(Mathf.Log(targetLuminance / Mathf.Max(MeasuredLuminance, 1e-4f), 2f) * compensation, minEV, maxEV)
                : 0f;
            float speed = (target < CurrentEV ? darkenSpeed : brightenSpeed) * speedScale;
            CurrentEV = Mathf.MoveTowards(CurrentEV, target, speed * Time.deltaTime);

            // Shutter beating against mains-driven tubes: a slow, slightly irregular brightness pulse.
            float beat = flicker > 0f
                ? (Mathf.Sin(Time.time * 6.2831853f * 0.9f) * 0.6f + Mathf.Sin(Time.time * 6.2831853f * 2.3f + 1.7f) * 0.4f) * 0.05f * flicker
                : 0f;
            _color.postExposure.value = CurrentEV + exposureBias + beat;
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            _pending = false;
            if (req.hasError || this == null) return;
            var px = req.GetData<ushort>();
            double logSum = 0; int n = 0;
            for (int i = 0; i + 2 < px.Length; i += 4)
            {
                float r = Mathf.HalfToFloat(px[i]), g = Mathf.HalfToFloat(px[i + 1]), b = Mathf.HalfToFloat(px[i + 2]);
                float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                logSum += Mathf.Log(Mathf.Max(lum, 1e-4f));
                n++;
            }
            if (n == 0) return;
            float avg = Mathf.Exp((float)(logSum / n));
            MeasuredLuminance = avg;
            SampleCount++;
        }

        void OnDestroy()
        {
            if (_probe) Destroy(_probe.gameObject);
            if (_rt) { _rt.Release(); Destroy(_rt); }
        }
    }
}
