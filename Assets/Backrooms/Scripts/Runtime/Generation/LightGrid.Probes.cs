using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Traced bounce light (F5 > LIGHT > Traced bounce) and the haze channel.
    ///
    /// Bounce: two probes per 1.2 m layout cell and storey (low and high; LG_ProbeHeights), each an L1 spherical harmonic
    /// of the light arriving at it. A cache holds the direct light leaving every cell's floor, ceiling and walls (FaceLight:
    /// every powered fixture, real or not, times the surface's colour), refreshed a quarter of the cells per frame while
    /// anything changes. Each frame a quarter of the probes trace 64 rays through the layout (walls, shut doors, floors,
    /// ceilings, header beams, the nearby furniture boxes) and read the cache where they hit, plus that surface's own bounce
    /// from last frame (more bounces); the result blends into the probe's history. Backrooms/Lit reads the probes with the
    /// same wall-aware filtering as the older bounce field (LGrid_ProbeIrradiance). About 260k short rays a frame.
    ///
    /// Haze: the direct-light pass also stores every fixture's whole light (luminance) in _LGridB.z, which the lit shader
    /// marches along the view ray (BackroomsHaze). Changes of real lights' power redraw their reach for it.
    /// </summary>
    public partial class LightGrid
    {
        [Header("Traced bounce")]
        [Tooltip("Bounced light traced through the layout by probes (off: the older diffused floor-light field).")]
        public bool tracedBounce = false;
        [Tooltip("How much surfaces tint the light they bounce (0 = grey).")]
        [Range(0f, 2f)] public float colourBleed = 1f;
        [Tooltip("Share of the bounced light that bounces again (read from last frame's probes).")]
        [Range(0f, 1f)] public float multiBounce = 0.7f;
        [Tooltip("Rays per probe update = 32 x this.")]
        [Range(1, 4)] public int raysPerThread = 2;
        [Tooltip("History kept per update (higher: smoother, slower to react). A real change (more than the rays' own noise explains) blends in faster.")]
        [Range(0.5f, 0.98f)] public float hysteresis = 0.95f;
        [Tooltip("Blend fast only on changes bigger than the rays' measured noise (off: on any big relative change, which made dark places shimmer).")]
        public bool noiseAwareBlend = true;
        [Tooltip("Shade with each probe averaged with its open neighbours (never across walls); the traced history stays unsmoothed.")]
        public bool probeFilter = true;
        public const int ProbeSlices = 4;

        [Header("Haze")]
        [Tooltip("Thickness of the lit haze (0 = clear air).")]
        [Range(0f, 1f)] public float haze = 0f;

        [Tooltip("Share of the far room's light a crawl hole's 1.9 m duct passes into this room (sheet-metal walls bounce some along).")]
        [Range(0f, 0.5f)] public float holeTransmission = 0.15f;
        public const int MaxHoles = 16;
        readonly Vector4[] _holePos = new Vector4[MaxHoles], _holeNormal = new Vector4[MaxHoles], _holePartner = new Vector4[MaxHoles];
        int _holeCount;
        Texture _ductMouthLight;

        /// <summary>
        /// The crawl holes near the player (CrawlPortalSystem, each frame): the opening (centre, half size), its normal into this
        /// room, the far hole's normal into its room and that mouth's row in the duct mouth light texture (-1: not sampled).
        /// Bounce rays that go into a hole carry the far room's light.
        /// </summary>
        public void SetCrawlHoles(Vector4[] pos, Vector4[] normal, Vector4[] partner, int count, Texture mouthLight)
        {
            _holeCount = Mathf.Min(count, MaxHoles);
            for (int i = 0; i < _holeCount; i++) { _holePos[i] = pos[i]; _holeNormal[i] = normal[i]; _holePartner[i] = partner[i]; }
            _ductMouthLight = mouthLight;
        }

        /// <summary>Probe updates traced so far (for tests).</summary>
        public int ProbeFrames { get; private set; }
        public bool ProbesActive => tracedBounce && _probesValid;
        bool HazeOn => haze > 0.001f;

        ComputeBuffer _faceLight, _probeKey;
        readonly RenderTexture[] _probeA = new RenderTexture[3], _probeB = new RenderTexture[3], _probeSmooth = new RenderTexture[3];
        bool _probeFlip, _probesValid, _faceValid;
        int _faceCycle, _faceSlice;
        int _kFace = -1, _kProbeCopy, _kProbeTrace, _kProbeFilter;
        bool _hazeApplied;
        readonly Dictionary<Vector3, (float lum, float time, int frame)> _hazeLights = new Dictionary<Vector3, (float, float, int)>();
        readonly List<Vector3> _hazeGone = new List<Vector3>();
        int _hazeFrame;

        static readonly string[] ProbePrev = { "_ProbePrevR", "_ProbePrevG", "_ProbePrevB" };
        static readonly string[] ProbeOut = { "_ProbeOutR", "_ProbeOutG", "_ProbeOutB" };
        static readonly string[] ProbeIn = { "_ProbeInR", "_ProbeInG", "_ProbeInB" };
        static readonly string[] ProbeSmooth = { "_ProbeSmoothR", "_ProbeSmoothG", "_ProbeSmoothB" };
        static readonly int[] ProbeGlobal = { Shader.PropertyToID("_LGridProbeR"), Shader.PropertyToID("_LGridProbeG"), Shader.PropertyToID("_LGridProbeB") };
        static readonly int GGI = Shader.PropertyToID("_LGridGI"), GHaze = Shader.PropertyToID("_LGridHaze");

        RenderTexture[] ProbeCurrent => _probeFlip ? _probeB : _probeA;
        /// <summary>What the shaders read: the smoothed probes, or the traced ones.</summary>
        RenderTexture[] ProbeShaded => probeFilter && _probeSmooth[0] ? _probeSmooth : ProbeCurrent;
        RenderTexture[] ProbePrevious => _probeFlip ? _probeA : _probeB;

        void EnsureProbes()
        {
            if (_faceLight != null) return;
            _kFace = compute.FindKernel("FaceLight");
            _kProbeCopy = compute.FindKernel("ProbeCopy");
            _kProbeTrace = compute.FindKernel("ProbeTrace");
            _kProbeFilter = compute.FindKernel("ProbeFilter");
            _faceLight = new ComputeBuffer(OccN * OccN * 2 * 12, 16);
            _faceLight.SetData(new Vector4[OccN * OccN * 2 * 12]);
            _probeKey = new ComputeBuffer(OccN * OccN * 4, 4);
            _probeKey.SetData(new uint[OccN * OccN * 4]);
            for (int i = 0; i < 3; i++)
            {
                _probeA[i] = MakeProbeTexture("Probes" + "RGB"[i] + "0"); _probeB[i] = MakeProbeTexture("Probes" + "RGB"[i] + "1");
                _probeSmooth[i] = MakeProbeTexture("Probes" + "RGB"[i] + "Smooth");
            }
            _probesValid = _faceValid = false; _faceCycle = 0;
        }

        static RenderTexture MakeProbeTexture(string name)
        {
            var r = new RenderTexture(OccN, OccN * 4, 0, RenderTextureFormat.ARGBHalf)
            { enableRandomWrite = true, name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            r.Create();
            return r;
        }

        void ReleaseProbes()
        {
            _faceLight?.Release(); _probeKey?.Release(); _faceLight = _probeKey = null;
            for (int i = 0; i < 3; i++)
            {
                foreach (var r in new[] { _probeA[i], _probeB[i], _probeSmooth[i] })
                    if (r) { r.Release(); if (Application.isPlaying) Destroy(r); else DestroyImmediate(r); }
                _probeA[i] = _probeB[i] = _probeSmooth[i] = null;
            }
            _probesValid = _faceValid = false;
            _hazeLights.Clear();
        }

        /// <summary>One frame of the traced bounce: a slice of the face cache while anything changes, then a slice of probes.</summary>
        void TickProbes()
        {
            EnsureProbes();
            if (_bounceDirty) { _faceCycle = ProbeSlices; _bounceDirty = false; }
            compute.SetVector("_ProbeParams", new Vector4(ProbeFrames, hysteresis, multiBounce, colourBleed));
            compute.SetVector("_ProbeParams2", new Vector4(noiseAwareBlend ? 1f : 0f, 0f, 0f, 0f));
            if (_faceCycle > 0 || !_faceValid)
            {
                int slice = _faceValid ? (_faceSlice++ & (ProbeSlices - 1)) : -1;   // the first time: every cell at once
                compute.SetInts("_ProbeInfo", ProbeSlices, raysPerThread, ProbeFrames & (ProbeSlices - 1), slice);
                compute.SetBuffer(_kFace, IdOcc, _occ); compute.SetBuffer(_kFace, "_Surfaces", _surfaces);
                compute.SetBuffer(_kFace, IdLights, _lights); compute.SetBuffer(_kFace, IdTiles, _tiles); compute.SetBuffer(_kFace, IdTileLights, _tileLights);
                BindOccludersTo(_kFace);
                BindHolesTo(compute, _kFace);
                compute.SetBuffer(_kFace, "_FaceLightOut", _faceLight);
                compute.Dispatch(_kFace, OccN / 8, OccN / 8, 2);
                BounceDispatchesLastFrame++;
                if (!_faceValid) { _faceValid = true; _faceCycle = 0; } else _faceCycle--;
            }
            compute.SetInts("_ProbeInfo", ProbeSlices, raysPerThread, ProbeFrames & (ProbeSlices - 1), -1);

            _probeFlip = !_probeFlip;
            var cur = ProbeCurrent; var prev = ProbePrevious;
            for (int i = 0; i < 3; i++)
            {
                compute.SetTexture(_kProbeCopy, ProbePrev[i], prev[i]); compute.SetTexture(_kProbeCopy, ProbeOut[i], cur[i]);
                compute.SetTexture(_kProbeTrace, ProbePrev[i], prev[i]); compute.SetTexture(_kProbeTrace, ProbeOut[i], cur[i]);
            }
            compute.SetBuffer(_kProbeCopy, IdOcc, _occ); compute.SetBuffer(_kProbeCopy, "_ProbeKey", _probeKey);
            compute.Dispatch(_kProbeCopy, OccN / 8, OccN / 8, 4);
            compute.SetBuffer(_kProbeTrace, IdOcc, _occ); compute.SetBuffer(_kProbeTrace, "_Surfaces", _surfaces);
            compute.SetBuffer(_kProbeTrace, "_FaceLightIn", _faceLight); compute.SetBuffer(_kProbeTrace, "_ProbeKey", _probeKey);
            bool holes = _holeCount > 0 && _ductMouthLight;
            compute.SetInt("_HoleCount", holes ? _holeCount : 0);
            compute.SetFloat("_HoleTransmission", holeTransmission);
            if (holes) { compute.SetVectorArray("_HolePos", _holePos); compute.SetVectorArray("_HoleNormal", _holeNormal); compute.SetVectorArray("_HolePartner", _holePartner); }
            compute.SetTexture(_kProbeTrace, "_DuctMouthLight", holes ? _ductMouthLight : (Texture)(_bounceCur ? _bounceCur : _bounce0));
            BindOccludersTo(_kProbeTrace);
            BindHolesTo(compute, _kProbeTrace);
            compute.Dispatch(_kProbeTrace, OccN / ProbeSlices * OccN * 4, 1, 1);
            BounceDispatchesLastFrame += 2;
            if (probeFilter)
            {
                for (int i = 0; i < 3; i++) { compute.SetTexture(_kProbeFilter, ProbeIn[i], cur[i]); compute.SetTexture(_kProbeFilter, ProbeSmooth[i], _probeSmooth[i]); }
                compute.SetBuffer(_kProbeFilter, IdOcc, _occ);
                BindHolesTo(compute, _kProbeFilter);
                compute.Dispatch(_kProbeFilter, OccN / 8, OccN / 8, 4);
            }
            ProbeFrames++;
            _probesValid = true;
        }

        void BindOccludersTo(int kernel)
        {
            compute.SetBuffer(kernel, "_LocalOccBoxes", _occluders); compute.SetBuffer(kernel, "_LocalOccTiles", _occluderTiles);
            compute.SetBuffer(kernel, "_LocalOccIndices", _occluderIndices);
        }

        /// <summary>
        /// Haze needs every fixture's whole light in the texture, so a change of a real light's power (switched, flickering,
        /// powering up) redraws its reach too; flicker at most a few times a second per tube.
        /// </summary>
        void TrackHazeLights(List<LightGridMath.GridLight> lights)
        {
            if (!HazeOn) { if (_hazeLights.Count > 0) _hazeLights.Clear(); return; }
            _hazeFrame++;
            float now = Time.time;
            foreach (var l in lights)
            {
                var bounceColour = l.colorAll * (1f + l.padding.x);   // smoothed power: a stuttering tube does not redraw the haze
                float lum = 0.2126f * bounceColour.x + 0.7152f * bounceColour.y + 0.0722f * bounceColour.z;
                if (!_hazeLights.TryGetValue(l.pos, out var old))
                {
                    _hazeLights[l.pos] = (lum, now, _hazeFrame);
                    if (lum > 0f) MarkDirty(l.pos, l.range + 0.6f);
                    continue;
                }
                float diff = Mathf.Abs(lum - old.lum);
                bool big = diff > 0.2f * Mathf.Max(old.lum, lum) + 1e-4f || (lum <= 0f) != (old.lum <= 0f);
                if (big && now - old.time > 0.15f) { MarkDirty(l.pos, l.range + 0.6f); _hazeLights[l.pos] = (lum, now, _hazeFrame); }
                else _hazeLights[l.pos] = (old.lum, old.time, _hazeFrame);
            }
            _hazeGone.Clear();
            foreach (var kv in _hazeLights) if (kv.Value.frame != _hazeFrame) _hazeGone.Add(kv.Key);
            foreach (var p in _hazeGone) { MarkDirty(p, 8f); _hazeLights.Remove(p); }
        }

        void BindProbeGlobals()
        {
            bool on = tracedBounce && _probesValid;
            var cur = ProbeShaded;
            for (int i = 0; i < 3; i++) if (cur[i]) Shader.SetGlobalTexture(ProbeGlobal[i], cur[i]);
            Shader.SetGlobalVector(GGI, new Vector4(on ? 1f : 0f, 0f, 0f, 0f));
            // x: extinction per metre, y: in-scatter per metre, z: march distance. Thin at the low end of the slider:
            // 0.25 scatters about 2.5% of the light per 10 m, 1 about 20%.
            float h = Mathf.Clamp01(haze), scatter = 0.02f * h * Mathf.Sqrt(h);
            Shader.SetGlobalVector(GHaze, HazeOn ? new Vector4(0.4f * scatter, scatter, 24f, 1f) : Vector4.zero);
        }

        void BindProbesTo(ComputeShader shader, int kernel)
        {
            var cur = ProbeShaded;
            bool on = tracedBounce && _probesValid && cur[0];
            for (int i = 0; i < 3; i++) shader.SetTexture(kernel, "_LGridProbe" + "RGB"[i], on ? cur[i] : (Texture)(_bounceCur ? _bounceCur : _bounce0));
            shader.SetVector("_LGridGI", new Vector4(on ? 1f : 0f, 0f, 0f, 0f));
        }
    }
}
