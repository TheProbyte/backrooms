using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// Applies <see cref="CamcorderSettings"/> every frame: the lens (URP post-processing: glare, distortion, colour fringes,
    /// vignette, depth of field), the exposure (<see cref="CamcorderAutoExposure"/>), the handheld motion, and the camcorder
    /// picture itself (<see cref="CamcorderPipeline"/>: sensor, camera processing, recording format, playback). Also runs the
    /// autofocus (contrast AF: a motor in diopters that hunts in the dark) and, with the 4:3 frame on, renders only the 4:3
    /// part of the screen (the camera's viewport), so no work is spent under the black bars.
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
        /// <summary>The AGC gain in stops (noise grows with it).</summary>
        public float CurrentGainEV => exposure ? exposure.GainEV : 0f;
        /// <summary>The lens's f-number now (the iris closes from the widest aperture).</summary>
        public float FNumber { get; private set; } = 1.8f;
        /// <summary>Whether URP's depth of field is running this frame (only when the blur would be visible).</summary>
        public bool DepthOfFieldOn => _dof != null && _dof.mode.value == DepthOfFieldMode.Bokeh;

        void SetDof(bool on) => _dof.mode.Override(on ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off);
        public CamcorderPipeline Pipeline => _pipeline;

        Camera _cam;
        VolumeProfile _profile;
        ColorAdjustments _color; WhiteBalance _wb; LiftGammaGain _lgg; Bloom _bloom; FilmGrain _grain;
        Vignette _vig; ChromaticAberration _ca; LensDistortion _lens; DepthOfField _dof; MotionBlur _mb; Tonemapping _tone;
        float _focus = 3f, _nextFocusCheck;
        float _dFocus = 1f / 3f, _dVel, _dTarget = 1f / 3f, _defocusT, _zNear = 10f;
        float _huntUntil, _nextHunt, _huntTarget;
        Backrooms.Interaction.PropGrabber _hand;
        float _baseBreathDeg, _baseBreathHeight;
        CamcorderPipeline _pipeline;
        Rect _viewRect = new Rect(0, 0, 1, 1);
        ScriptableRendererFeature _oldVideo;
        bool _oldVideoWasActive;
        UniversalRenderPipelineAsset _urp;
        ColorGradingMode _gradingWas;
        bool _hooked;

        /// <summary>The tape breaking up (noclip doors): 0 clean .. 1 tracking lost, tearing, rolling. Set by NoclipTravel.</summary>
        public static float Glitch;
        /// <summary>How far the picture has rolled (0..1 of the frame) while the glitch lasts.</summary>
        public static float GlitchRoll;

        /// <summary>Set for repeatable stills: no handheld motion, exposure/focus locked, tape effects that vary with time stopped.</summary>
        [System.NonSerialized] public bool freezeForStills;
        /// <summary>Fixed focus (metres) and aperture (f-number) instead of the autofocus, e.g. for the main menu; 0 or less: off.</summary>
        [System.NonSerialized] public float focusOverride = -1f, apertureOverride = -1f;

        static readonly int IdSensor = Shader.PropertyToID("_CamSensor"), IdNoise2 = Shader.PropertyToID("_CamNoise2"), IdSmearK = Shader.PropertyToID("_CamSmearK");
        static readonly int IdWB = Shader.PropertyToID("_CamWB"), IdTone = Shader.PropertyToID("_CamTone"), IdLowLight = Shader.PropertyToID("_CamLowLight");
        static readonly int IdFmt = Shader.PropertyToID("_CamFmt"), IdLumaW = Shader.PropertyToID("_CamLumaW"), IdChroma = Shader.PropertyToID("_CamChroma");
        static readonly int IdDetail = Shader.PropertyToID("_CamDetail"), IdTape = Shader.PropertyToID("_CamTape"), IdTBC = Shader.PropertyToID("_CamTBC");
        static readonly int IdTrack = Shader.PropertyToID("_CamTrack"), IdGlitch = Shader.PropertyToID("_CamGlitch"), IdDisplay = Shader.PropertyToID("_CamDisplay");

        void Awake()
        {
            _cam = GetComponent<Camera>();
            Settings = CamcorderSettings.Load();
        }

        void Start()
        {
            // The OSD is drawn by its own camera and composited by the camcorder picture.
            _cam.cullingMask &= ~(1 << CamcorderOSD.OsdLayer);
            if (volume)
            {
                _profile = volume.profile; // runtime instance
                _tone = Get<Tonemapping>(); _color = Get<ColorAdjustments>(); _wb = Get<WhiteBalance>(); _lgg = Get<LiftGammaGain>();
                _bloom = Get<Bloom>(); _grain = Get<FilmGrain>(); _vig = Get<Vignette>(); _ca = Get<ChromaticAberration>();
                _lens = Get<LensDistortion>(); _dof = Get<DepthOfField>(); _mb = Get<MotionBlur>();
                // Neutral, so nothing from a lower-priority volume shows through (the camera's own processing replaces them).
                _lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f)); _lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f)); _lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));
                _grain.intensity.Override(0f); _wb.temperature.Override(0f); _wb.tint.Override(0f);
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

        void OnEnable()
        {
            var shader = Shader.Find("Hidden/Backrooms/CamcorderPipeline");
            if (shader && shader.isSupported) _pipeline = new CamcorderPipeline(shader);
            else Debug.LogWarning("[Backrooms] The camcorder picture shader is missing or unsupported: showing the plain image.");
            if (_pipeline != null)
            {
                // The old single full-screen pass and its frame hold are replaced by the pipeline while this camera lives.
                _oldVideo = FindRendererFeature("CamcorderVideo");
                if (_oldVideo) { _oldVideoWasActive = _oldVideo.isActive; _oldVideo.SetActive(false); }
                CamcorderFrameHold.Bypass = true;
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_urp) _gradingWas = _urp.colorGradingMode;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                _hooked = true;
            }
            if (exposure) exposure.pipeline = _pipeline;
        }

        void OnDisable()
        {
            if (_hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            _hooked = false;
            if (_oldVideo) _oldVideo.SetActive(_oldVideoWasActive);
            _oldVideo = null;
            CamcorderFrameHold.Bypass = false;
            if (_urp) _urp.colorGradingMode = _gradingWas;
            if (_cam) _cam.rect = new Rect(0, 0, 1, 1);
            if (exposure) exposure.pipeline = null;
            _pipeline?.Dispose();
            _pipeline = null;
        }

        static ScriptableRendererFeature FindRendererFeature(string name)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (!urp || !(f?.GetValue(urp) is ScriptableRendererData[] list)) return null;
            foreach (var data in list)
                if (data) foreach (var feat in data.rendererFeatures) if (feat && feat.name == name) return feat;
            return null;
        }

        /// <summary>
        /// Before this camera renders: the 4:3 viewport on screen (the whole texture when rendering into one, e.g. stills), and
        /// the camcorder picture added after post-processing.
        /// </summary>
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam) return;
            cam.rect = cam.targetTexture ? new Rect(0, 0, 1, 1) : _viewRect;
            if (_pipeline == null || CamcorderPipeline.Suspended || cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            data.scriptableRenderer?.EnqueuePass(_pipeline);
        }

        void LateUpdate()
        {
            var s = Settings;
            if (freezeForStills)
            {
                s = s.Clone();
                s.shakeIntensity = 0f; s.breathing = 0f; s.lookOvershoot = 0f; s.lookJitter = 0f; s.stabilization = 0f;
                s.autofocus = false; s.autoExposure = false; s.fluorescentFlicker = 0f; s.motionBlur = 0f;
            }
            if (!_player && Time.unscaledTime >= _nextPlayerSearch)
            {
                _player = FindAnyObjectByType<CamcorderPlayerController>();
                _nextPlayerSearch = Time.unscaledTime + 1f;
            }
            float zoomMul = _player ? _player.FovMultiplier : 1f;
            // Focus breathing: a video zoom lens's angle of view tightens a little as it focuses close (about 3.5 % from
            // infinity to half a metre), so a refocus on the hand, or the autofocus hunting in the dark, makes the frame
            // creep in and out.
            float breathe = 1f - 0.035f * Mathf.Max(0f, s.focusBreathing) * Mathf.Clamp01(_dFocus / 2f) * (s.autofocus || focusOverride > 0f ? 1f : 0f);
            _cam.fieldOfView = s.fieldOfView * zoomMul * breathe;
            float zoomT = Mathf.InverseLerp(1f, 0.25f, zoomMul);   // 0 wide .. 1 full optical zoom

            // The 4:3 picture is the camera's own viewport (the bars are painted by the OSD's overlay canvas).
            _viewRect = new Rect(0, 0, 1, 1);
            if (s.aspect43 && osd)
            {
                float a = (4f / 3f) / ((float)Screen.width / Mathf.Max(1, Screen.height));
                if (a < 0.999f) _viewRect = new Rect((1f - a) * 0.5f, 0f, a, 1f);
            }
            if (!_cam.targetTexture) _cam.rect = _viewRect;

            bool look = s.camcorderLook && _pipeline != null;
            if (exposure)
            {
                exposure.autoExposure = s.autoExposure;
                exposure.snap = freezeForStills;
                exposure.exposureBias = s.exposureBias;
                exposure.speedScale = s.exposureSpeed;
                exposure.compensation = s.exposureCompensation;
                exposure.maxGainEV = s.maxGain;
                exposure.shutterTime = s.ShutterTime;
                exposure.slowShutter = s.slowShutter;
                exposure.flicker = s.fluorescentFlicker;
                exposure.autoWhiteBalance = s.autoWhiteBalance && look;
                exposure.awbSpeed = s.awbSpeed;
                exposure.awbStrength = s.awbStrength;
            }
            float irisEV = exposure ? exposure.IrisEV : -2f;

            if (_profile)
            {
                if (_urp) _urp.colorGradingMode = look ? ColorGradingMode.HighDynamicRange : _gradingWas;
                // The camera's own transfer curve, knee and clip are in the camcorder picture: URP passes linear light on.
                _tone.mode.Override(look ? TonemappingMode.None : TonemappingMode.Neutral);
                _color.contrast.Override(0f);
                _color.saturation.Override(0f);
                // Lens glare: a few percent of all the light spread wide by the zoom's many elements (no threshold).
                _bloom.threshold.Override(0f);
                _bloom.intensity.Override(0.06f * s.bloom);
                _bloom.scatter.Override(Mathf.Lerp(0.6f, 0.85f, Mathf.Clamp01(s.bloomScatter)));
                _bloom.highQualityFiltering.Override(false);
                _bloom.downscale.Override(BloomDownscaleMode.Quarter);
                // Wide open the corners fall off most; stopping down evens the frame.
                _vig.intensity.Override(s.vignette * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(-irisEV / 3f)));
                _vig.smoothness.Override(0.45f);
                _vig.rounded.Override(true);
                // Zooming in: the barrel turns to slight pincushion, the colour fringes shrink.
                _ca.intensity.Override(Mathf.Lerp(s.chromaticAberration, s.chromaticAberration * 0.6f, zoomT));
                float dist = Mathf.Lerp(s.lensDistortion, -0.04f, zoomT);
                _lens.intensity.Override(dist);
                _lens.scale.Override(1f + Mathf.Abs(dist) * 0.2f);
                _mb.intensity.Override(0f);   // the shutter's blur is in the camcorder picture
                UpdateFocus(s, zoomT, irisEV);
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
                shake.recordedWalk = s.recordedWalk;
            }

            // Recording cadence: the recording frame rate, 30 pictures a second while the slow shutter is on, or 59.94 fields a
            // second when fields are shown (so they alternate at the real rate whatever the display's frame rate).
            bool slow = exposure && exposure.ShutterEV > 0.5f;
            CamcorderFrameHold.Fps = freezeForStills ? 0f : (s.recordFps > 0.01f ? s.recordFps : slow ? 29.97f : s.interlace ? 59.94f : 0f);
            CamcorderFrameHold.Tick();
            int frameIndex = freezeForStills ? 7 : CamcorderFrameHold.RecordedFrames % 16777216;

            if (_pipeline != null) ApplyPicture(s, frameIndex);

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
        float _nextPlayerSearch;
        public Backrooms.Audio.CamcorderAudioChain Audio => _audio ? _audio : (_audio = GetComponent<Backrooms.Audio.CamcorderAudioChain>());

        /// <summary>Rec.709-style transfer curve with a given exponent (0.45 standard).</summary>
        public static float Oetf(float x, float gamma)
        {
            float toe = (1.099f * Mathf.Pow(0.018f, gamma) - 0.099f) / 0.018f;
            return x < 0.018f ? toe * x : 1.099f * Mathf.Pow(x, gamma) - 0.099f;
        }

        /// <summary>Knee slope that brings 400 % light exactly to the white clip.</summary>
        public static float KneeSlope(float kneePoint, float whiteClip, float gamma)
        {
            float top = Oetf(4f, gamma);
            return kneePoint >= whiteClip - 1e-3f || top <= kneePoint ? 1f : Mathf.Clamp((whiteClip - kneePoint) / (top - kneePoint), 0f, 1f);
        }

        /// <summary>Every value of the camcorder picture for this frame.</summary>
        void ApplyPicture(CamcorderSettings s, int frameIndex)
        {
            var p = _pipeline;
            var m = p.Material;
            var fmt = CamcorderFormat.Get(s.format);
            float gainEV = exposure ? exposure.GainEV : 0f;
            float lowLight = Mathf.Clamp01(gainEV / 3f);
            float T = exposure ? exposure.ShutterTimeNow : s.ShutterTime;

            p.lines = s.videoLines > 0 ? s.videoLines : 480;
            p.record = CamcorderFrameHold.RecordThisFrame;
            p.meterOnly = !s.camcorderLook;
            p.exposure = exposure ? exposure.Exposure : 0.8f;
            p.shutterTime = T;
            p.motionBlur = s.motionBlur;
            p.softness = Mathf.Max(0f, s.softness);
            p.frameIndex = frameIndex;
            p.noiseReduction = s.noiseReduction > 0.001f;
            p.osd = osd && osd.Curved && s.showOSD;
            p.osdCurvature = s.osdCurvature;
            p.viewRect = _viewRect;

            // Sensor: AGC gain (noise grows with it), read and shot noise, colour-filter share, fixed pattern, smear.
            float gNoise = Mathf.Pow(2f, gainEV * Mathf.Max(0f, s.gainNoise));
            m.SetVector(IdSensor, new Vector4(gNoise, 0.002f, 1.6e-5f, Mathf.Max(0f, s.noise)));
            m.SetVector(IdNoise2, new Vector4(Mathf.Clamp01(0.5f * s.chromaNoise), s.fixedPattern, s.fixedPattern, T * 60f));
            m.SetVector(IdSmearK, new Vector4(0.003f * s.smear, s.purpleFringe, 0f, 0f));

            // White balance (auto, plus the user's shift), colour matrix, transfer curve, knee, clip.
            var wb = exposure ? exposure.WhiteBalance : Vector3.one;
            var shift = GraphicsOptions.ColourShift(4500f - s.whiteBalanceTemp * 40f, -s.whiteBalanceTint / 50f);
            m.SetVector(IdWB, new Vector4(wb.x * shift.r, wb.y * shift.g, wb.z * shift.b, Mathf.Max(0f, 1f + s.saturation / 100f) * Mathf.Lerp(1f, 0.85f, lowLight)));
            float gamma = Mathf.Clamp(0.45f + s.contrast * 0.002f, 0.3f, 0.7f);
            float clip = Mathf.Clamp(s.whiteClip, 0.8f, 1.1f), knee = Mathf.Clamp(s.highlightKnee, 0.5f, clip);
            m.SetVector(IdTone, new Vector4(gamma, knee, KneeSlope(knee, clip, gamma), clip));
            m.SetVector(IdLowLight, new Vector4(lowLight * 0.8f, Mathf.Clamp(s.noiseReduction * Mathf.Lerp(0.3f, 1f, lowLight), 0f, 0.7f),
                Mathf.Clamp(s.highlightColour, 0.4f, 0.98f), 0f));

            // Format: bandwidths, edge enhancement, tape and playback.
            // A digital format's blocks: the codec has a fixed budget per frame, so a noisy picture (low light, gain up) spends
            // it on the noise and every block comes out coarser.
            float busy = 1f + 1.2f * Mathf.Clamp01(gainEV / 3f);
            m.SetVector(IdFmt, new Vector4(fmt.analog ? 1f : 0f, fmt.comb ? 1f : 0f, fmt.dv411 ? 1f : 0f, fmt.compression * s.blockiness * busy));
            var lw = CamcorderFormat.LumaWeights(fmt.lumaSigma);
            m.SetVector(IdLumaW, new Vector4(lw.x, lw.y, lw.z, 0f));
            m.SetVector(IdChroma, new Vector4(fmt.chromaSigma / Mathf.Max(0.2f, s.chromaDetail), fmt.chromaTrail * s.chromaBleed, fmt.chromaDelay * s.chromaShift,
                fmt.phaseNoiseDeg * Mathf.Deg2Rad * s.chromaPhaseNoise));
            float detailCut = Mathf.Lerp(1f, 0.5f, lowLight);   // less edge enhancement at high gain
            m.SetVector(IdDetail, new Vector4((fmt.detailCamera + fmt.detailPlayback) * s.sharpening * detailCut, 0.01f + 0.004f * gainEV,
                0.4f * fmt.detailCamera * s.sharpening * detailCut, 0.1f));
            bool still = freezeForStills;
            m.SetVector(IdTape, new Vector4(fmt.lumaNoise * s.tapeNoise, fmt.chromaNoise * s.tapeNoise, still ? 0f : fmt.dropouts * s.dropouts, fmt.crossColour * s.crossColour));
            m.SetVector(IdTBC, new Vector4(fmt.jitter * s.timebaseJitter, fmt.flagging * s.timebaseJitter, s.headSwitching > 0.001f ? fmt.headSwitchLines : 0f,
                fmt.headSwitchSkew * s.headSwitching));
            float t = still ? 1f : Time.time;
            float barPos = 0.85f + 0.2f * (Mathf.PerlinNoise(t * 0.03f, 0.5f) - 0.5f);
            m.SetVector(IdTrack, new Vector4(fmt.analog ? s.trackingNoise : 0f, barPos, Mathf.Clamp(s.pedestal, 0f, 0.15f), s.bottomLine));
            m.SetVector(IdGlitch, new Vector4(Mathf.Clamp01(Glitch), GlitchRoll, Random.value, t));
            m.SetVector(IdDisplay, new Vector4(Mathf.Clamp01(s.lineDoubling), s.interlace ? 1f : 0f, frameIndex & 1, Mathf.Clamp01(s.scanlines)));
        }

        /// <summary>
        /// Contrast AF in diopters (1 / metres), the way a focus motor moves: it refocuses once the blur would be visible
        /// (0.6 recording pixels) for a moment, drives at a limited speed with some overshoot, and in the dark (no contrast
        /// to lock on to) hunts near and far. Depth of field follows the real lens (a 1/4" CCD at the iris's f-number), so it
        /// only shows on close-ups, at full zoom and while hunting: URP's depth of field runs only then.
        /// </summary>
        void UpdateFocus(CamcorderSettings s, float zoomT, float irisEV)
        {
            bool manual = focusOverride > 0f;
            int lines = s.videoLines > 0 ? s.videoLines : 480;
            float N = Mathf.Lerp(s.aperture, s.aperture * 1.45f, zoomT) * Mathf.Pow(2f, -irisEV * 0.5f);
            FNumber = N;
            float tanHalf = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float K = lines * 0.0027f / (8f * Mathf.Max(0.5f, N) * tanHalf * tanHalf);   // blur radius, recording pixels per diopter

            if (manual)
            {
                // The main menu's shot: a fixed focus and aperture, as before.
                _focus = focusOverride; _dFocus = _dTarget = 1f / focusOverride; _dVel = 0f;
                SetDof(true);
                _dof.focusDistance.Override(_focus);
                _dof.aperture.Override(apertureOverride > 0f ? apertureOverride : 4f);
                _dof.focalLength.Override(Mathf.Lerp(18f, 32f, Mathf.InverseLerp(80f, 45f, s.fieldOfView)));
                _dof.bladeCount.Override(6);
                return;
            }
            if (!s.autofocus) { SetDof(false); return; }

            if (Time.time >= _nextFocusCheck)
            {
                _nextFocusCheck = Time.time + 0.15f;
                float d = 6f;
                var origin = transform.position;
                if (Physics.SphereCast(origin, 0.05f, transform.forward, out var hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    d = hit.distance;
                // The hand, or what it holds, close in front of the lens: the camera focuses on it (and the room goes soft).
                if (!_hand) _hand = FindAnyObjectByType<Backrooms.Interaction.PropGrabber>();
                float near = d;
                if (_hand && _hand.HandBlend > 0.5f)
                {
                    Vector3 hp = _hand.HeldItem ? _hand.HeldItem.transform.position : _hand.HandPosition;
                    Vector3 to = hp - origin;
                    if (to.magnitude < d && to.magnitude < 0.9f && Vector3.Angle(transform.forward, to) < 13f) d = Mathf.Max(0.3f, to.magnitude);
                    // In frame (it can be soft while the camera focuses past it).
                    if (to.magnitude < 1.2f && Vector3.Angle(transform.forward, to) < _cam.fieldOfView * 0.5f) near = Mathf.Min(near, Mathf.Max(0.2f, to.magnitude));
                }
                // The nearest thing in two corners of the frame (what could show as soft while focused far).
                for (int i = 0; i < 2; i++)
                {
                    var dir = Quaternion.AngleAxis(i == 0 ? -12f : 12f, transform.up) * Quaternion.AngleAxis(i == 0 ? -9f : 9f, transform.right) * transform.forward;
                    if (Physics.Raycast(origin, dir, out var h2, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) near = Mathf.Min(near, h2.distance);
                }
                _zNear = Mathf.Max(0.15f, near);
                // In the dark a contrast autofocus has nothing to lock on: it hunts, sweeping near and far now and then.
                bool dark = exposure && exposure.MeasuredLuminance > 0f && exposure.GainEV > 1.5f && exposure.MeteredLevel < exposure.targetLevel * 0.35f;
                if (dark && Time.time > _nextHunt)
                {
                    _nextHunt = Time.time + Random.Range(1.4f, 3.2f) / Mathf.Lerp(0.5f, 1.5f, s.focusHunting);
                    _huntUntil = Time.time + Random.Range(0.35f, 0.8f);
                    _huntTarget = Random.value < 0.5f ? Random.Range(0.4f, 1.2f) : Random.Range(6f, 20f);
                }
                if (dark && Time.time < _huntUntil) d = _huntTarget;
                float dSubject = 1f / Mathf.Max(0.25f, d);
                // Refocus only once the blur would show, and has for a moment (the AF's dead band and dead time).
                bool visible = K * Mathf.Abs(dSubject - _dTarget) > 0.6f;
                _defocusT = visible ? _defocusT + 0.15f : 0f;
                if (_defocusT >= 0.15f || Time.time < _huntUntil) _dTarget = dSubject;
            }
            // The motor: a damped spring in diopters with a top speed; hunting means less damping (overshoot).
            float speed = 3f * Mathf.Max(0.1f, s.focusSpeed) / (1f + 2f * zoomT);
            float w = 9f * Mathf.Sqrt(Mathf.Max(0.1f, s.focusSpeed)), zeta = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(s.focusHunting));
            _dVel += (w * w * (_dTarget - _dFocus) - 2f * zeta * w * _dVel) * Time.deltaTime;
            _dVel = Mathf.Clamp(_dVel, -speed, speed);
            _dFocus = Mathf.Clamp(_dFocus + _dVel * Time.deltaTime, 0.02f, 4f);
            _focus = 1f / _dFocus;

            // How soft anything in frame could be: the far distance, the nearest thing, the subject while refocusing.
            float coc = K * Mathf.Max(_dFocus, Mathf.Max(Mathf.Abs(_dFocus - 1f / _zNear), Mathf.Abs(_dFocus - _dTarget)));
            SetDof(coc > 0.6f);
            if (!DepthOfFieldOn) return;
            // URP's bokeh blur, scaled so its blur circle equals the real lens's (URP: radius = (1 - P/z) * maxCoC * 14 px).
            float kDisplay = K * _cam.pixelHeight / lines;                         // display pixels per diopter
            float fl = Mathf.Clamp(Mathf.Sqrt(285.7f * kDisplay), 10f, 300f);    // focal length that keeps the f-number in URP's range
            float P = Mathf.Max(_focus, fl / 1000f + 0.05f);
            float nUrp = 14f * P * fl * fl / (1000f * Mathf.Max(1e-4f, kDisplay) * (P - fl / 1000f));
            _dof.focusDistance.Override(P);
            _dof.focalLength.Override(fl);
            _dof.aperture.Override(Mathf.Clamp(nUrp, 1f, 32f));
            _dof.bladeCount.Override(6);
        }
    }
}
