using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// Applies <see cref="CamcorderSettings"/> to the camera, post-processing volume, exposure, shake and
    /// the full-screen video shader every frame. Also runs the autofocus motor (with hunting) that drives
    /// depth of field from a centre-frame raycast, like a consumer camcorder's contrast AF.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CamcorderRig : MonoBehaviour
    {
        public Volume volume;
        public CamcorderAutoExposure exposure;
        public HandheldCameraShake shake;
        public CamcorderOSD osd;

        public CamcorderSettings Settings { get; private set; }
        public float FocusDistance => _focus;
        public float CurrentGainEV => exposure ? Mathf.Max(0f, exposure.CurrentEV + Settings.exposureBias) : 0f;

        Camera _cam;
        VolumeProfile _profile;
        ColorAdjustments _color; WhiteBalance _wb; LiftGammaGain _lgg; Bloom _bloom; FilmGrain _grain;
        Vignette _vig; ChromaticAberration _ca; LensDistortion _lens; DepthOfField _dof; MotionBlur _mb; Tonemapping _tone;
        float _focus = 3f, _focusVel, _focusTarget = 3f, _nextFocusCheck;
        float _baseBreathDeg, _baseBreathHeight;

        static readonly int ID1 = Shader.PropertyToID("_CamVideo1");
        static readonly int ID2 = Shader.PropertyToID("_CamVideo2");
        static readonly int ID3 = Shader.PropertyToID("_CamVideo3");
        static readonly int ID4 = Shader.PropertyToID("_CamVideo4");
        static readonly int ID5 = Shader.PropertyToID("_CamVideo5");
        static readonly int ID6 = Shader.PropertyToID("_CamVideo6");
        static readonly int ID7 = Shader.PropertyToID("_CamVideo7");
        static readonly int ID8 = Shader.PropertyToID("_CamVideo8");

        /// <summary>Set for repeatable stills: no handheld motion, exposure/focus locked, tape effects that vary with time stopped.</summary>
        [System.NonSerialized] public bool freezeForStills;
        Quaternion _prevRot; bool _havePrev; Vector2 _blurVel;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            Settings = CamcorderSettings.Load();
        }

        void Start()
        {
            // The OSD is drawn by its own camera and composited by the video pass.
            _cam.cullingMask &= ~(1 << CamcorderOSD.OsdLayer);
            if (volume)
            {
                _profile = volume.profile; // runtime instance
                _tone = Get<Tonemapping>(); _color = Get<ColorAdjustments>(); _wb = Get<WhiteBalance>(); _lgg = Get<LiftGammaGain>();
                _bloom = Get<Bloom>(); _grain = Get<FilmGrain>(); _vig = Get<Vignette>(); _ca = Get<ChromaticAberration>();
                _lens = Get<LensDistortion>(); _dof = Get<DepthOfField>(); _mb = Get<MotionBlur>();
            }
            if (shake) { _baseBreathDeg = shake.breathingDegrees; _baseBreathHeight = shake.breathingHeight; }
        }

        T Get<T>() where T : VolumeComponent
        {
            if (!_profile.TryGet(out T c)) c = _profile.Add<T>(true);
            c.active = true;
            return c;
        }

        public void SetSettings(CamcorderSettings s) { Settings = s; }

        void OnDisable()
        {
            Shader.SetGlobalVector(ID3, Vector4.zero); // disable video pass in edit mode / other scenes
        }

        void LateUpdate()
        {
            var s = Settings;
            if (freezeForStills)
            {
                s = s.Clone();
                s.shakeIntensity = 0f; s.breathing = 0f; s.lookOvershoot = 0f; s.lookJitter = 0f; s.stabilization = 0f;
                s.autofocus = false; s.autoExposure = false; s.fluorescentFlicker = 0f;
                s.sensorBlur = 0f; s.motionBlur = 0f;
            }
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            _cam.fieldOfView = s.fieldOfView * (_player ? _player.FovMultiplier : 1f);

            if (_profile)
            {
                _tone.mode.Override(TonemappingMode.Neutral);
                _color.contrast.Override(s.contrast);
                _color.saturation.Override(s.saturation);
                _wb.temperature.Override(s.whiteBalanceTemp);
                _wb.tint.Override(s.whiteBalanceTint);
                _lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f)); // pedestal handled by the video pass
                _bloom.intensity.Override(s.bloom);
                _bloom.scatter.Override(s.bloomScatter);
                _bloom.threshold.Override(1.0f);
                _grain.intensity.Override(0f);           // replaced by video gain noise
                _vig.intensity.Override(s.vignette);
                _vig.smoothness.Override(0.45f);
                _ca.intensity.Override(s.chromaticAberration);
                _lens.intensity.Override(s.lensDistortion);
                _lens.scale.Override(1f + Mathf.Abs(s.lensDistortion) * 0.2f);
                _mb.mode.Override(MotionBlurMode.CameraOnly);
                _mb.intensity.Override(s.motionBlur);
                _mb.active = s.motionBlur > 0.001f;
                UpdateFocus(s);
            }

            if (exposure)
            {
                exposure.autoExposure = s.autoExposure;
                exposure.snap = freezeForStills;
                exposure.exposureBias = s.exposureBias;
                exposure.speedScale = s.exposureSpeed;
                exposure.maxEV = s.maxGain;
                exposure.flicker = s.fluorescentFlicker;
            }

            if (shake)
            {
                shake.shakeIntensity = s.shakeIntensity;
                shake.breathingDegrees = _baseBreathDeg * s.breathing;
                shake.breathingHeight = _baseBreathHeight * s.breathing;
                shake.stabilization = s.stabilization;
                shake.lookOvershoot = s.lookOvershoot;
                shake.lookJitter = s.lookJitter;
                shake.roughness = s.handheldRoughness;
            }

            // Sensor motion blur: the camera's turn rate (pan, tilt, handheld shake) times a shutter time gives a smear
            // vector in screen units, so a fast pan streaks the whole frame the way footage from a camcorder does.
            Vector2 blur = Vector2.zero;
            if (Interaction.CrawlPortalSystem.LastWarpFrame >= Time.frameCount - 1) _havePrev = false;   // a trip through a crawl hole is not a whip pan
            if (Time.deltaTime > 1e-4f)
            {
                var rot = transform.rotation;
                if (_havePrev)
                {
                    var d = Quaternion.Inverse(_prevRot) * rot;
                    d.ToAngleAxis(out float ang, out Vector3 axis);
                    if (ang > 180f) ang -= 360f;
                    Vector3 w = axis * ang;                                  // degrees turned this frame, in the camera's own axes
                    float vf = _cam.fieldOfView, hf = 2f * Mathf.Atan(Mathf.Tan(vf * 0.5f * Mathf.Deg2Rad) * _cam.aspect) * Mathf.Rad2Deg;
                    var perSec = new Vector2(-w.y / hf, w.x / vf) / Time.deltaTime;      // screen fractions per second (right turn moves the image left)
                    blur = perSec * (0.033f * s.sensorBlur);                              // open shutter for about 1/30 s
                }
                _prevRot = rot; _havePrev = true;
            }
            _blurVel = Vector2.Lerp(_blurVel, blur, 1f - Mathf.Exp(-Time.deltaTime / 0.04f));
            var bv = Vector2.ClampMagnitude(_blurVel, 0.22f);
            Shader.SetGlobalVector(ID6, new Vector4(bv.x, bv.y, 0f, 0f));
            Shader.SetGlobalVector(ID7, new Vector4(s.headSwitching, s.blockiness, s.bottomLine, freezeForStills ? 0f : s.colourFlicker));

            // Recording frame rate: the picture only changes on recorded frames (see CamcorderFrameHold).
            CamcorderFrameHold.Fps = freezeForStills ? 0f : s.recordFps;
            CamcorderFrameHold.Tick();
            int frameIndex = freezeForStills ? 7 : (s.recordFps > 0.01f ? CamcorderFrameHold.RecordedFrames : Time.frameCount) % 16777216;

            float gain = CurrentGainEV;
            Shader.SetGlobalVector(ID1, new Vector4(s.sharpening, s.softness, s.chromaBleed, s.chromaShift));
            Shader.SetGlobalVector(ID2, new Vector4(s.noise + s.gainNoise * gain, s.chromaNoise * (1f + gain), s.scanlines, s.trackingNoise));
            Shader.SetGlobalVector(ID3, new Vector4(s.pedestal, s.highlightKnee, freezeForStills ? 1f : Time.time, 1f));
            // Frame index re-seeds the noise every recorded frame; the noise grid follows the recording resolution (720-line sensor otherwise); 480 video lines.
            Shader.SetGlobalVector(ID4, new Vector4(frameIndex, s.videoLines > 0 ? s.videoLines : 720f, 480f, s.grainSize));
            Shader.SetGlobalVector(ID8, new Vector4(s.videoLines, s.chromaDetail, s.lineDoubling, 0f));
            // OSD composite: curvature of the viewfinder overlay, and whether the OSD texture is in use.
            Shader.SetGlobalVector(ID5, new Vector4(s.osdCurvature, osd && osd.Curved ? 1f : 0f, 0f, 0f));

            // Frame pacing (vsync, frame cap) lives in F5 > Graphics (GraphicsController).

            if (osd) { osd.visible = s.showOSD; osd.pillarbox = s.aspect43; }

            // Microphone / recording chain (its own gain control; not linked to exposure).
            if (!_audio) _audio = GetComponent<Backrooms.Audio.CamcorderAudioChain>();
            if (_audio)
            {
                _audio.outputVolume = s.audioVolume; _audio.processing = s.audioProcessing;
                _audio.coloration = s.micColoration; _audio.autoGain = s.autoGain; _audio.hiss = s.micHiss;
                _audio.handling = s.handlingNoise; _audio.stereoWidth = s.stereoWidth;
            }
        }

        Backrooms.Audio.CamcorderAudioChain _audio;
        CamcorderPlayerController _player;
        public Backrooms.Audio.CamcorderAudioChain Audio => _audio ? _audio : (_audio = GetComponent<Backrooms.Audio.CamcorderAudioChain>());

        /// <summary>Contrast-AF style: sample the centre, then drive focus with a damped motor that overshoots.</summary>
        void UpdateFocus(CamcorderSettings s)
        {
            _dof.active = s.autofocus;
            if (!s.autofocus) return;
            if (Time.time >= _nextFocusCheck)
            {
                _nextFocusCheck = Time.time + 0.15f;
                float d = 6f;
                if (Physics.SphereCast(transform.position, 0.05f, transform.forward, out var hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    d = hit.distance;
                // Ignore tiny changes (AF dead band) so focus isn't constantly breathing.
                if (Mathf.Abs(d - _focusTarget) / Mathf.Max(_focusTarget, 0.5f) > 0.12f) _focusTarget = d;
            }
            float stiffness = 6f * s.focusSpeed, damping = Mathf.Lerp(2f, 0.6f, s.focusHunting) * Mathf.Sqrt(stiffness);
            float acc = (_focusTarget - _focus) * stiffness - _focusVel * damping;
            _focusVel += acc * Time.deltaTime;
            _focus = Mathf.Max(0.3f, _focus + _focusVel * Time.deltaTime);

            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _dof.focusDistance.Override(_focus);
            _dof.aperture.Override(s.aperture);
            // Zooming in lengthens the focal length, so depth of field gets shallower like a real lens.
            _dof.focalLength.Override(Mathf.Lerp(18f, 32f, Mathf.InverseLerp(80f, 45f, s.fieldOfView)) / Mathf.Max(0.1f, _player ? _player.FovMultiplier : 1f));
            _dof.bladeCount.Override(6);
        }
    }
}
