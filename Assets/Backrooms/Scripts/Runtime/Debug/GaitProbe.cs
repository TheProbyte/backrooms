using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Measures the first-person body's gait (2026-10-28). On a flat test floor high above the world (no walls; numbers
    /// only, nothing is lit up there) the bot stands, walks, runs, stops, strafes, backs up, turns on the spot,
    /// crouch-walks, and climbs and descends a test staircase. Per phase it logs: cadence and step length, stance share,
    /// the fastest a planted foot's pivot (heel or ball) moved, the deepest a heel, ball or toe went into the floor, the
    /// fastest any body bone turned and the biggest one-frame spike (pops), the knee's range, its swing peak and fastest
    /// change, the closest the feet came across the body, pelvis rotation / obliquity / tilt, its vertical and lateral
    /// motion, the thorax's rotation against the pelvis, the arm swing and its phase against the legs, the toe bend, the
    /// foot's angle at heel strike and toe-off. <c>GaitProbe.Run(folder)</c> in Play; <see cref="Done"/>, <see cref="Report"/>.
    /// <c>GaitProbe.Film(folder)</c>: side and front frames of a walk cycle in the office (shadow-only parts drawn).
    /// <c>GaitProbe.View(folder)</c>: first-person frames at pitch 0/45/max/70 standing and walking, and how much of each
    /// frame is the body.
    /// </summary>
    public class GaitProbe : MonoBehaviour
    {
        public string folder;
        public string mode = "gait";
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();

        public static GaitProbe Run(string folder) => Make(folder, "gait");
        public static GaitProbe Film(string folder) => Make(folder, "film");
        public static GaitProbe View(string folder) => Make(folder, "view");

        static GaitProbe Make(string folder, string mode)
        {
            var go = new GameObject("GaitProbe");
            go.SetActive(false);
            var p = go.AddComponent<GaitProbe>();
            p.folder = folder; p.mode = mode;
            go.SetActive(true);
            return p;
        }

        CamcorderPlayerController _pl;
        FirstPersonBody _body;
        Transform _root;
        FieldInfo _fLegA, _fLegB, _fSwinging, _fAir, _fLegRight, _fLegFwd;
        Transform _pelvis, _spine3;
        readonly Transform[] _thigh = new Transform[2], _calf = new Transform[2], _foot = new Transform[2], _ball = new Transform[2];
        Transform _upperR, _lowerR, _handR;
        readonly List<Transform> _bones = new List<Transform>();
        // Contact points measured on the shoes at rest: the heel (in the foot's frame), the ball and the toe tip (in the
        // ball's frame).
        readonly Vector3[] _heelL = new Vector3[2], _ballL = new Vector3[2], _toeL = new Vector3[2];
        Quaternion _pelvisRest, _spineRest;
        Vector3 _pelvisFwdL, _pelvisRightL, _spineFwdL;
        readonly Quaternion[] _toeRest = new Quaternion[2];

        IEnumerator Start()
        {
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            _pl = ws && ws.player ? ws.player.GetComponent<CamcorderPlayerController>() : FindAnyObjectByType<CamcorderPlayerController>();
            _body = _pl ? _pl.GetComponent<FirstPersonBody>() : null;
            if (!_body) { _log.AppendLine("no player body"); Done = true; yield break; }
            _root = _pl.transform;
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            _fLegA = typeof(FirstPersonBody).GetField("_legA", bf); _fLegB = typeof(FirstPersonBody).GetField("_legB", bf);
            _fAir = typeof(FirstPersonBody).GetField("_air", bf);
            _fLegRight = typeof(FirstPersonBody).GetField("_legRight", bf); _fLegFwd = typeof(FirstPersonBody).GetField("_legFwd", bf);
            if (mode == "film") { yield return FilmRoutine(); Done = true; yield break; }
            if (mode == "view") { yield return ViewRoutine(); Done = true; yield break; }
            yield return GaitRoutine();
            Done = true;
        }

        void OnDestroy()
        {
            if (_pl) { _pl.botInput = null; _pl.moveScale = 1f; }
            if (_platform) Destroy(_platform);
            if (_shoeMesh) Destroy(_shoeMesh);
        }

        // ---------------- Bones ----------------

        bool FindBones()
        {
            var all = _root.GetComponentsInChildren<Transform>(true);
            Transform F(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
            _pelvis = F("pelvis"); _spine3 = F("spine_03");
            string[] s = { "l", "r" };
            for (int i = 0; i < 2; i++) { _thigh[i] = F("thigh_" + s[i]); _calf[i] = F("calf_" + s[i]); _foot[i] = F("foot_" + s[i]); _ball[i] = F("ball_" + s[i]); }
            _upperR = F("upperarm_r"); _lowerR = F("lowerarm_r"); _handR = F("hand_r");
            _bones.Clear();
            foreach (var n in new[] { "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "thigh_l", "calf_l", "foot_l", "ball_l", "thigh_r", "calf_r",
                                      "foot_r", "ball_r", "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r", "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l" })
            { var t = F(n); if (t) _bones.Add(t); }
            return _pelvis && _thigh[0] && _thigh[1] && _calf[0] && _calf[1] && _foot[0] && _foot[1] && _ball[0] && _ball[1];
        }

        SkinnedMeshRenderer _shoes;
        Mesh _shoeMesh;
        readonly Vector3[] _footFwdL = new Vector3[2];

        /// <summary>
        /// The shoes' heel, ball and toe tip on the sole, from the skinned shoe mesh in its bind pose (MakeHuman's, standing
        /// level), relative to the foot and toe bones: independent of the pose the feet happen to be in; and the foot's
        /// forward in its bone's frame, for its tilt. Also how far the shoes are into the floor standing now.
        /// </summary>
        void MeasureFeet(float floorY)
        {
            foreach (var r in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name.Contains("shoes")) _shoes = r;
            if (!_shoes) { _log.AppendLine("no shoes"); return; }
            _shoeMesh = new Mesh();
            var mesh = _shoes.sharedMesh;
            // (The mesh is not readable: the vertices come from a bake, mapped into each bone's frame below.)
            _shoes.BakeMesh(_shoeMesh, true);
            var baked = _shoeMesh.vertices;
            var bw = mesh.boneWeights; var bp = mesh.bindposes; var bones = _shoes.bones;
            Vector3 fwdM = _shoes.transform.InverseTransformDirection(_root.forward), upM = _shoes.transform.InverseTransformDirection(_root.up);
            float W(BoneWeight w, int b) => (w.boneIndex0 == b ? w.weight0 : 0f) + (w.boneIndex1 == b ? w.weight1 : 0f) + (w.boneIndex2 == b ? w.weight2 : 0f) + (w.boneIndex3 == b ? w.weight3 : 0f);
            for (int i = 0; i < 2; i++)
            {
                int fb = System.Array.IndexOf(bones, _foot[i]), bb = System.Array.IndexOf(bones, _ball[i]);
                if (fb < 0 || bb < 0) { _log.AppendLine($"foot {i}: bones not in the shoes"); continue; }
                Vector3 fwdF = bp[fb].MultiplyVector(fwdM).normalized, upF = bp[fb].MultiplyVector(upM).normalized;
                Vector3 fwdB = bp[bb].MultiplyVector(fwdM).normalized, upB = bp[bb].MultiplyVector(upM).normalized;
                float minF = 0f, minU = 0f, minUB = 0f, maxFB = 0f;
                for (int v = 0; v < baked.Length && v < bw.Length; v++)
                {
                    float wf = W(bw[v], fb), wb = W(bw[v], bb);
                    if (wf < 0.85f && wb < 0.85f) continue;           // riding (almost) rigidly on one of the two
                    Vector3 w = _shoes.transform.position + _shoes.transform.rotation * baked[v];
                    if (wf >= 0.85f)
                    {
                        Vector3 pf = _foot[i].InverseTransformPoint(w);
                        minU = Mathf.Min(minU, Vector3.Dot(pf, upF)); minF = Mathf.Min(minF, Vector3.Dot(pf, fwdF));
                    }
                    else
                    {
                        Vector3 pb = _ball[i].InverseTransformPoint(w);
                        minUB = Mathf.Min(minUB, Vector3.Dot(pb, upB)); maxFB = Mathf.Max(maxFB, Vector3.Dot(pb, fwdB));
                        Vector3 pf = _foot[i].InverseTransformPoint(w);
                        minU = Mathf.Min(minU, Vector3.Dot(pf, upF));
                    }
                }
                _heelL[i] = fwdF * (minF + 0.01f) + upF * minU;
                _ballL[i] = upB * minUB;
                _toeL[i] = fwdB * (maxFB - 0.012f) + upB * minUB;
                _footFwdL[i] = fwdF;
                _toeRest[i] = Quaternion.Inverse(bp[fb].inverse.rotation) * bp[bb].inverse.rotation;
                Vector3 ballInFoot = bp[fb].MultiplyPoint3x4(bp[bb].inverse.MultiplyPoint3x4(Vector3.zero));
                _log.AppendLine($"foot {i} (bind pose): heel {minF:0.000} m from the ankle, ball {Vector3.Dot(ballInFoot, fwdF):0.000}, toe tip {Vector3.Dot(ballInFoot, fwdF) + maxFB:0.000}, ankle {-minU:0.000} over the sole");
            }
            // Standing now: the shoes' lowest point against the floor.
            _shoes.BakeMesh(_shoeMesh, true);
            float low = float.MaxValue;
            foreach (var lv in _shoeMesh.vertices) low = Mathf.Min(low, (_shoes.transform.position + _shoes.transform.rotation * lv).y);
            _log.AppendLine($"standing now: the shoes' lowest point {(low - floorY) * 100f:0.0} cm off the floor");
            Vector3 fwd = LegFwd, right = LegRight;
            _pelvisRest = _pelvis.rotation;
            _pelvisFwdL = _pelvis.InverseTransformDirection(fwd); _pelvisRightL = _pelvis.InverseTransformDirection(right);
            if (_spine3) _spineFwdL = _spine3.InverseTransformDirection(fwd);
        }

        // The test floor's pieces (the platform and the stairs): the surface height under a point, exactly.
        readonly List<Bounds> _boxes = new List<Bounds>();

        float SurfaceAt(Vector3 p)
        {
            float y = _groundY;
            foreach (var b in _boxes)
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z) y = Mathf.Max(y, b.max.y);
            return y;
        }

        /// <summary>How far a point inside a raised box is from that box's nearest side, across (m).</summary>
        float Inside(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var b in _boxes)
            {
                if (b.max.y <= _groundY + 0.01f || p.y >= b.max.y || p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                best = Mathf.Min(best, Mathf.Min(Mathf.Min(p.x - b.min.x, b.max.x - p.x), Mathf.Min(p.z - b.min.z, b.max.z - p.z)));
            }
            return best == float.MaxValue ? 0f : best;
        }

        /// <summary>How far each shoe's mesh is into the floor or a step at most (m: depth under a floor, or how far into
        /// a step from its side).</summary>
        void ShoeDepth(out float left, out float right)
        {
            left = right = -1f;
            if (!_shoes) return;
            _shoes.BakeMesh(_shoeMesh, true);
            var v = _shoeMesh.vertices;
            Vector3 r = LegRight, o = _root.position;
            Vector3 pos = _shoes.transform.position; Quaternion rot = _shoes.transform.rotation;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = pos + rot * v[i];
                float depth = SurfaceAt(w) - w.y;
                // Inside a step (more than 2 cm under its top): how far in from its side, across (a toe into a riser).
                if (depth > 0.02f) depth = Mathf.Min(depth, Inside(w));
                if (Vector3.Dot(w - o, r) < 0f) left = Mathf.Max(left, depth); else right = Mathf.Max(right, depth);
            }
        }

        Vector3 LegFwd => _fLegFwd != null ? (Vector3)_fLegFwd.GetValue(_body) : _root.forward;
        Vector3 LegRight => _fLegRight != null ? (Vector3)_fLegRight.GetValue(_body) : _root.right;
        float Air => _fAir != null ? (float)_fAir.GetValue(_body) : 0f;

        bool Swinging(int i)
        {
            var leg = (i == 0 ? _fLegA : _fLegB)?.GetValue(_body);
            if (leg == null) return false;
            if (_fSwinging == null) _fSwinging = leg.GetType().GetField("swinging", BindingFlags.Public | BindingFlags.Instance);
            return (bool)_fSwinging.GetValue(leg);
        }

        Vector3 Heel(int i) => _foot[i].TransformPoint(_heelL[i]);
        Vector3 Ball(int i) => _ball[i].TransformPoint(_ballL[i]);
        Vector3 Toe(int i) => _ball[i].TransformPoint(_toeL[i]);

        // ---------------- One phase's numbers ----------------

        class Stats
        {
            public string name;
            public int frames, steps0, steps1;
            public float time, dist;
            public float slipMax, slipSum; public int slipN;
            public float floorMin = 0f, floorMinPlanted = 0f;
            public float angMax; public string angBone = "";
            public float spikeMax; public string spikeBone = "";
            public float footSpeedMax;
            public float kneeMin = 999f, kneeMax, kneeRateMax, kneeSwingPeakSum; public int kneeSwingN;
            public float kneeLandSum; public int kneeLandN;
            public float gapMin = 999f; public int crossings;
            public int planted0, planted1;
            public readonly List<float> pYaw = new List<float>(), pRoll = new List<float>(), pTilt = new List<float>(), pY = new List<float>(), pX = new List<float>(),
                tYaw = new List<float>(), tRel = new List<float>(), arm = new List<float>(), lead = new List<float>();
            public float toeMax, landPitchSum, liftPitchSum; public int landN, liftN;
            public float treadBadMax; public int treadLandings, treadOff;
            public readonly List<float> lands = new List<float>();
            public float airMax; public int ungrounded;
        }

        Stats _st;
        bool _measure;
        readonly float[] _swingKneePeak = new float[2];
        readonly bool[] _wasSwinging = new bool[2];
        Quaternion[] _prevRot;
        float[] _prevW, _prevW2;
        Vector3[] _prevHeel = new Vector3[2], _prevBall = new Vector3[2], _prevAnkle = new Vector3[2];
        float[] _prevKnee = new float[2];
        bool _prevSet;
        float _groundY;
        StringBuilder _csv;

        float Knee(int i) => Vector3.Angle(_calf[i].position - _thigh[i].position, _foot[i].position - _calf[i].position);

        /// <summary>The foot's tilt, toes up positive (the foot bone's forward as it stood level).</summary>
        float FootPitch(int i) => Mathf.Asin(Mathf.Clamp(_foot[i].TransformDirection(_footFwdL[i]).y, -1f, 1f)) * Mathf.Rad2Deg;

        float _poseDt = 1f / 60f;

        void Sample(float frameDt)
        {
            // The pose sampled now was made in the last frame's LateUpdate (the probe runs before it): rates over that
            // frame's time (a long frame then a short one otherwise reads as a jump).
            float dt = _poseDt;
            _poseDt = frameDt;
            int nb = _bones.Count;
            if (_prevRot == null || _prevRot.Length != nb) { _prevRot = new Quaternion[nb]; _prevW = new float[nb]; _prevW2 = new float[nb]; _prevSet = false; }
            Quaternion inv = Quaternion.Inverse(_root.rotation);
            var st = _st;
            for (int b = 0; b < nb; b++)
            {
                Quaternion q = inv * _bones[b].rotation;
                if (_prevSet)
                {
                    float w = Quaternion.Angle(_prevRot[b], q) / dt;
                    if (_measure && st != null)
                    {
                        if (w > st.angMax) { st.angMax = w; st.angBone = _bones[b].name; }
                        // A pop: the previous frame was much faster than both of its neighbours.
                        float spike = _prevW[b] - Mathf.Max(_prevW2[b], w);
                        if (spike > st.spikeMax) { st.spikeMax = spike; st.spikeBone = _bones[b].name; }
                    }
                    _prevW2[b] = _prevW[b]; _prevW[b] = w;
                }
                _prevRot[b] = q;
            }
            Vector3 fwd = LegFwd, right = LegRight, up = Vector3.up;
            bool grounded = _pl.IsGrounded && Air < 0.5f;
            float depthL = -1f, depthR = -1f;
            if (_measure && st != null) ShoeDepth(out depthL, out depthR);
            float pelvisYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(_pelvis.TransformDirection(_pelvisFwdL), up), fwd, up) * -1f;
            for (int i = 0; i < 2; i++)
            {
                bool sw = Swinging(i);
                Vector3 heel = Heel(i), ball = Ball(i), toe = Toe(i), ankle = _foot[i].position;
                float knee = Knee(i);
                if (_prevSet && _measure && st != null)
                {
                    Vector3 dh = heel - _prevHeel[i], db = ball - _prevBall[i];
                    dh.y = 0f; db.y = 0f;
                    float fs = (ankle - _prevAnkle[i]).magnitude / dt;
                    st.footSpeedMax = Mathf.Max(st.footSpeedMax, fs);
                    if (!sw && !_wasSwinging[i] && grounded)
                    {
                        float slip = Mathf.Min(dh.magnitude, db.magnitude) / dt;
                        st.slipMax = Mathf.Max(st.slipMax, slip); st.slipSum += slip; st.slipN++;
                        if (i == 0) st.planted0++; else st.planted1++;
                    }
                    // The shoe mesh into the floor (or a stair) under it, as drawn.
                    float under = -(i == 0 ? depthL : depthR);
                    st.floorMin = Mathf.Min(st.floorMin, under);
                    if (!sw) st.floorMinPlanted = Mathf.Min(st.floorMinPlanted, under);
                    st.kneeMin = Mathf.Min(st.kneeMin, knee); st.kneeMax = Mathf.Max(st.kneeMax, knee);
                    st.kneeRateMax = Mathf.Max(st.kneeRateMax, Mathf.Abs(knee - _prevKnee[i]) / dt);
                    Quaternion rel = Quaternion.Inverse(_foot[i].rotation) * _ball[i].rotation;
                    st.toeMax = Mathf.Max(st.toeMax, Quaternion.Angle(_toeRest[i], rel));
                    if (sw) _swingKneePeak[i] = Mathf.Max(_swingKneePeak[i], knee);
                    if (sw && !_wasSwinging[i]) { st.liftPitchSum += -FootPitch(i); st.liftN++; _swingKneePeak[i] = knee; }
                    if (!sw && _wasSwinging[i])
                    {
                        st.lands.Add(Time.time);
                        st.landPitchSum += FootPitch(i); st.landN++;
                        st.kneeSwingPeakSum += _swingKneePeak[i]; st.kneeSwingN++;
                        st.kneeLandSum += knee; st.kneeLandN++;
                        // On a stair tread whole: the heel and the toe on the same surface height.
                        float hs = SurfaceAt(heel), ts = SurfaceAt(toe);
                        if (Mathf.Abs(hs - _groundY) > 0.02f || Mathf.Abs(ts - _groundY) > 0.02f)
                        {
                            st.treadLandings++;
                            if (Mathf.Abs(hs - ts) > 0.02f) st.treadOff++;
                        }
                    }
                }
                _prevHeel[i] = heel; _prevBall[i] = ball; _prevAnkle[i] = ankle; _prevKnee[i] = knee;
                _wasSwinging[i] = sw;
            }
            if (_measure && st != null)
            {
                Vector3 l = _foot[0].position, r = _foot[1].position;
                float gap = Vector3.Dot(r - l, right);
                st.gapMin = Mathf.Min(st.gapMin, gap);
                if (gap < 0f) st.crossings++;
                st.frames++;
                st.airMax = Mathf.Max(st.airMax, Air);
                if (!_pl.IsGrounded) st.ungrounded++;
                float roll = Mathf.Asin(Mathf.Clamp(_pelvis.TransformDirection(_pelvisRightL).y, -1f, 1f)) * Mathf.Rad2Deg;
                float tilt = Mathf.Asin(Mathf.Clamp(-_pelvis.TransformDirection(_pelvisFwdL).y, -1f, 1f)) * Mathf.Rad2Deg;
                st.pYaw.Add(pelvisYaw); st.pRoll.Add(roll); st.pTilt.Add(tilt);
                st.pY.Add(_pelvis.position.y - _root.position.y);
                st.pX.Add(Vector3.Dot(_pelvis.position - _root.position, right));
                if (_spine3)
                {
                    float ty = -Vector3.SignedAngle(Vector3.ProjectOnPlane(_spine3.TransformDirection(_spineFwdL), up), fwd, up);
                    st.tYaw.Add(ty); st.tRel.Add(Mathf.DeltaAngle(pelvisYaw, ty));
                }
                if (_handR && _upperR) st.arm.Add(Vector3.Dot(_handR.position - _upperR.position, fwd));
                st.lead.Add(Vector3.Dot(r - l, fwd));
                _csv?.AppendLine($"{st.name},{Time.time:0.000},{depthL * 100f:0.0},{depthR * 100f:0.0},{Knee(0):0.0},{Knee(1):0.0},{(Swinging(0) ? 1 : 0)},{(Swinging(1) ? 1 : 0)},{pelvisYaw:0.00},{st.pRoll[st.pRoll.Count - 1]:0.00},{st.pY[st.pY.Count - 1]:0.000},{st.pX[st.pX.Count - 1]:0.000},{FootPitch(0):0.0},{FootPitch(1):0.0},{_foot[0].position.y - _groundY:0.000},{_foot[1].position.y - _groundY:0.000},{(st.arm.Count > 0 ? st.arm[st.arm.Count - 1] : 0f):0.000}");
            }
            _prevSet = true;
        }

        static float Amp(List<float> v) { if (v.Count == 0) return 0f; float a = float.MaxValue, b = float.MinValue; foreach (var x in v) { a = Mathf.Min(a, x); b = Mathf.Max(b, x); } return b - a; }

        static float Corr(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            if (n < 3) return 0f;
            double ma = 0, mb = 0; for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
            return saa > 1e-9 && sbb > 1e-9 ? (float)(sab / System.Math.Sqrt(saa * sbb)) : 0f;
        }

        void Write(Stats s)
        {
            int steps = s.steps1 - s.steps0;
            float spm = s.time > 0f ? steps / s.time * 60f : 0f;
            float stepLen = steps > 0 ? s.dist / steps : 0f;
            float speed = s.time > 0f ? s.dist / s.time : 0f;
            float stance = s.frames > 0 ? (s.planted0 + s.planted1) * 0.5f / s.frames : 0f;
            _log.AppendLine($"[{s.name}] speed {speed:0.00} m/s, {steps} steps in {s.time:0.0} s = {spm:0} steps/min, step {stepLen:0.00} m, stance share {stance:0.00}");
            if (s.lands.Count >= 3)
            {
                float mean = (s.lands[s.lands.Count - 1] - s.lands[0]) / (s.lands.Count - 1), lo = 99f, hi = 0f;
                for (int k = 1; k < s.lands.Count; k++) { float d = s.lands[k] - s.lands[k - 1]; lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
                _log.AppendLine($"   from landing intervals: {60f / mean:0} steps/min, step {speed * mean:0.00} m; step times {lo:0.00}-{hi:0.00} s");
            }
            if (s.airMax > 0.01f || s.ungrounded > 0) _log.AppendLine($"   legs hanging up to {s.airMax:0.00}, frames not grounded {s.ungrounded} of {s.frames}");
            _log.AppendLine($"   planted pivot slip max {s.slipMax:0.000} m/s (mean {(s.slipN > 0 ? s.slipSum / s.slipN : 0f):0.0000}); heel/ball/toe under the floor at most {-s.floorMin * 100f:0.0} cm (planted {-s.floorMinPlanted * 100f:0.0} cm); fastest foot {s.footSpeedMax:0.0} m/s");
            _log.AppendLine($"   fastest bone {s.angMax:0} deg/s ({s.angBone}); biggest one-frame spike {s.spikeMax:0} deg/s ({s.spikeBone})");
            _log.AppendLine($"   knee {s.kneeMin:0}-{s.kneeMax:0} deg, swing peak {(s.kneeSwingN > 0 ? s.kneeSwingPeakSum / s.kneeSwingN : 0f):0} deg, at landing {(s.kneeLandN > 0 ? s.kneeLandSum / s.kneeLandN : 0f):0} deg, fastest {s.kneeRateMax:0} deg/s");
            _log.AppendLine($"   feet apart at least {s.gapMin * 100f:0.0} cm across (frames crossed {s.crossings}); toe bend max {s.toeMax:0} deg; foot at heel strike {(s.landN > 0 ? s.landPitchSum / s.landN : 0f):0.0} deg toes-up, at toe-off {(s.liftN > 0 ? s.liftPitchSum / s.liftN : 0f):0.0} deg heel-up");
            _log.AppendLine($"   pelvis: rotation range {Amp(s.pYaw):0.0} deg, obliquity range {Amp(s.pRoll):0.0} deg, tilt range {Amp(s.pTilt):0.0} deg, vertical {Amp(s.pY) * 100f:0.0} cm p-p, lateral {Amp(s.pX) * 100f:0.0} cm p-p");
            _log.AppendLine($"   thorax: rotation range {Amp(s.tYaw):0.0} deg (corr. with pelvis {Corr(s.tYaw, s.pYaw):0.00}), against the pelvis {Amp(s.tRel):0.0} deg; right hand swing {Amp(s.arm) * 100f:0.0} cm p-p (corr. with right foot lead {Corr(s.arm, s.lead):0.00})");
            if (s.treadLandings > 0) _log.AppendLine($"   stair landings {s.treadLandings}, heel and toe on different treads {s.treadOff}");
        }

        IEnumerator Phase(string name, float time, float warm, System.Action begin, System.Action<float> each = null)
        {
            begin?.Invoke();
            var st = new Stats { name = name };
            _st = st;
            float t = 0f;
            Vector3 start = Vector3.zero;
            while (t < time)
            {
                yield return null;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                t += dt;
                each?.Invoke(t);
                if (!_measure && t >= warm) { _measure = true; st.steps0 = _body.StepsLanded; start = _root.position; }
                Sample(dt);
                if (_measure) st.time += dt;
            }
            Vector3 d = _root.position - start; d.y = 0f;
            st.dist = d.magnitude; st.steps1 = _body.StepsLanded;
            _measure = false;
            Write(st);
        }

        // ---------------- The gait run ----------------

        GameObject _platform;

        IEnumerator GaitRoutine()
        {
            if (!FindBones()) { _log.AppendLine("bones not found (not the MakeHuman body?)"); yield break; }
            _csv = new StringBuilder("phase,t,depthL,depthR,kneeL,kneeR,swingL,swingR,pelvisYaw,pelvisRoll,pelvisY,pelvisX,pitchL,pitchR,footLy,footRy,arm\n");
            // The feet measured standing still where the player is (on the office floor, settled and level).
            _pl.botInput = Vector2.zero;
            _pl.SetStance(CamcorderPlayerController.Stance.Stand);
            yield return Wait(1.5f);
            float floorHere = Physics.Raycast(_root.position + Vector3.up * 0.5f, Vector3.down, out var fh, 2f, ~0, QueryTriggerInteraction.Ignore) ? fh.point.y : _root.position.y;
            MeasureFeet(floorHere);
            // A flat floor 60 m above where the player is, 80 x 80 m, and a staircase on it.
            Vector3 c = _root.position + Vector3.up * 60f;
            _platform = new GameObject("GaitProbeFloor");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(_platform.transform, false);
            floor.transform.position = c - Vector3.up * 0.5f; floor.transform.localScale = new Vector3(80f, 1f, 80f);
            _groundY = c.y;
            float yaw = 0f;
            // Stairs along +x from x = 6: six steps of 17 x 28 cm up, a 1.6 m landing, six down.
            Vector3 sx = new Vector3(1f, 0f, 0f);
            Vector3 s0 = c + new Vector3(0f, 0f, 20f);
            for (int k = 0; k < 6; k++)
            {
                var st = GameObject.CreatePrimitive(PrimitiveType.Cube);
                st.transform.SetParent(_platform.transform, false);
                float h = 0.17f * (k + 1);
                st.transform.position = s0 + sx * (0.28f * k + 0.14f) + Vector3.up * (h * 0.5f);
                st.transform.localScale = new Vector3(0.28f, h, 1.4f);
            }
            {
                var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
                top.transform.SetParent(_platform.transform, false);
                top.transform.position = s0 + sx * (0.28f * 6 + 0.8f) + Vector3.up * (1.02f * 0.5f);
                top.transform.localScale = new Vector3(1.6f, 1.02f, 1.4f);
            }
            for (int k = 0; k < 6; k++)
            {
                var st = GameObject.CreatePrimitive(PrimitiveType.Cube);
                st.transform.SetParent(_platform.transform, false);
                float h = 0.17f * (5 - k);
                if (h <= 0f) continue;
                st.transform.position = s0 + sx * (0.28f * 6 + 1.6f + 0.28f * k + 0.14f) + Vector3.up * (h * 0.5f);
                st.transform.localScale = new Vector3(0.28f, h, 1.4f);
            }
            foreach (var r in _platform.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            Physics.SyncTransforms();          // (or the feet are placed before the new floor is in the physics scene)
            _boxes.Clear();
            foreach (var col in _platform.GetComponentsInChildren<Collider>()) _boxes.Add(col.bounds);

            _pl.Teleport(c + Vector3.up * 0.05f, yaw, 0f);
            _pl.botInput = Vector2.zero;
            yield return Wait(2f);
            _log.AppendLine($"after the teleport: grounded {_pl.IsGrounded}, eye {_pl.Eye:0.00}, feet active {_body.FeetActive}, legs hanging {Air:0.00}, pelvis {_pelvis.position.y - _root.position.y:0.000} over the root, left ankle {_foot[0].position.y - _groundY:0.000} over the floor");
            _prevSet = false;

            yield return Phase("still", 1.0f, 0.2f, () => _pl.botInput = Vector2.zero);
            yield return Phase("start walk", 1.2f, 0f, () => _pl.botInput = new Vector2(0, 1));
            yield return Phase("walk", 4f, 0.2f, null);
            yield return Phase("stop", 1.5f, 0f, () => _pl.botInput = Vector2.zero);
            _pl.Teleport(c + Vector3.up * 0.05f + new Vector3(0, 0, -25f), yaw, 0f); _prevSet = false;
            yield return Wait(0.8f);
            yield return Phase("start run", 1.2f, 0f, () => { _pl.moveScale = _pl.runSpeed / _pl.walkSpeed; _pl.botInput = new Vector2(0, 1); });
            yield return Phase("run", 3.5f, 0.2f, null);
            yield return Phase("stop from run", 1.5f, 0f, () => { _pl.botInput = Vector2.zero; });
            _pl.moveScale = 1f;
            _pl.Teleport(c + Vector3.up * 0.05f + new Vector3(-10f, 0, -10f), yaw, 0f); _prevSet = false;
            yield return Wait(0.8f);
            yield return Phase("slow walk", 3f, 1f, () => { _pl.moveScale = 0.55f; _pl.botInput = new Vector2(0, 1); });
            _pl.moveScale = 1f;
            yield return Phase("strafe right", 3f, 0.8f, () => _pl.botInput = new Vector2(1, 0));
            yield return Phase("strafe left", 3f, 0.8f, () => _pl.botInput = new Vector2(-1, 0));
            yield return Phase("backward", 3f, 0.8f, () => _pl.botInput = new Vector2(0, -1));
            yield return Phase("diagonal fwd-left", 2.5f, 0.6f, () => _pl.botInput = new Vector2(-0.7f, 0.7f));
            yield return Phase("stop 2", 1.2f, 0f, () => _pl.botInput = Vector2.zero);
            float y0 = _root.eulerAngles.y;
            yield return Phase("turn in place 90", 2.2f, 0f, () => _pl.botInput = Vector2.zero, t => _pl.SetYaw(y0 + 90f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f))));
            float y1 = _root.eulerAngles.y;
            yield return Phase("look 25 each way", 2.4f, 0f, null, t => _pl.SetYaw(y1 + 25f * Mathf.Sin(t / 2.4f * Mathf.PI * 2f)));
            yield return Phase("crouch", 1.2f, 0f, () => _pl.SetStance(CamcorderPlayerController.Stance.Crouch));
            yield return Phase("crouch walk", 3f, 0.8f, () => _pl.botInput = new Vector2(0, 1));
            yield return Phase("stand up", 1.2f, 0f, () => { _pl.botInput = Vector2.zero; _pl.SetStance(CamcorderPlayerController.Stance.Stand); });
            // Stairs: from 2 m before the first step, up, over the landing, down, and on.
            _pl.Teleport(s0 + Vector3.up * 0.05f - sx * 2.2f, 90f, 0f); _prevSet = false;
            yield return Wait(1f);
            yield return Phase("stairs up+down", 6.5f, 0.9f, () => _pl.botInput = new Vector2(0, 1));
            _pl.botInput = Vector2.zero;
            yield return Wait(0.5f);
            if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "gait.csv"), _csv.ToString());
            _pl.botInput = null;
            _pl.Teleport(c - Vector3.up * 60f + Vector3.up * 0.05f, yaw, 0f);
            yield return Wait(0.5f);
            Destroy(_platform);
            if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "gait.txt"), _log.ToString());
        }

        static IEnumerator Wait(float s) { float t = 0f; while (t < s) { yield return null; t += Time.deltaTime; } }

        // ---------------- Filmstrip ----------------

        Vector3 FindOpen()
        {
            Vector3 origin = _root.position;
            for (int ring = 0; ring < 16; ring++)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 cc = origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * 1.2f;
                    if (!Physics.Raycast(cc + Vector3.up * 1f, Vector3.down, out var fl, 1.5f) || fl.normal.y < 0.9f) continue;
                    if (Physics.CheckBox(fl.point + Vector3.up * 1f, new Vector3(2.2f, 0.55f, 2.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    return fl.point;
                }
            return origin;
        }

        readonly List<(Renderer r, ShadowCastingMode m)> _shadowOnly = new List<(Renderer, ShadowCastingMode)>();

        void ShowAll(bool on)
        {
            if (on)
            {
                _shadowOnly.Clear();
                foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) { _shadowOnly.Add((r, r.shadowCastingMode)); r.shadowCastingMode = ShadowCastingMode.On; }
                var cc = GameObject.Find("CamcorderShadow");
                if (cc)
                    foreach (var r in cc.GetComponentsInChildren<Renderer>(true))
                        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) { _shadowOnly.Add((r, r.shadowCastingMode)); r.shadowCastingMode = ShadowCastingMode.On; }
            }
            else
            {
                foreach (var (r, m) in _shadowOnly) if (r) r.shadowCastingMode = m;
                _shadowOnly.Clear();
            }
        }

        void Save(Camera cam, RenderTexture rt, Texture2D tex, string file)
        {
            var prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(folder, file), tex.EncodeToPNG());
        }

        IEnumerator FilmRoutine()
        {
            if (!FindBones()) { _log.AppendLine("no bones"); yield break; }
            Vector3 p = FindOpen();
            float yaw = _root.eulerAngles.y;
            _pl.Teleport(p + Vector3.up * 0.05f, yaw, 0f);
            _pl.botInput = Vector2.zero;
            yield return Wait(1f);
            var go = new GameObject("GaitProbeCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main); cam.fieldOfView = 34f; cam.nearClipPlane = 0.05f; cam.enabled = false;
            // A fill light on the camera (the body is often in its own shadow under the ceiling lights).
            var fillGo = new GameObject("GaitProbeFill");
            fillGo.transform.SetParent(go.transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point; fill.range = 6f; fill.intensity = 2.2f; fill.shadows = LightShadows.None; fill.renderingLayerMask = -1;
            var rt = new RenderTexture(360, 480, 24);
            var tex = new Texture2D(360, 480, TextureFormat.RGB24, false);
            var modes = new (string name, Vector2 input, float scale)[] { ("walk", new Vector2(0, 1), 1f), ("run", new Vector2(0, 1), 2.667f), ("strafe", new Vector2(1, 0), 1f), ("back", new Vector2(0, -1), 1f) };
            foreach (var m in modes)
            {
                _pl.Teleport(p + Vector3.up * 0.05f - _root.forward * (m.name == "run" ? 0f : 0f), yaw, 0f);
                _pl.botInput = Vector2.zero; _pl.moveScale = 1f;
                yield return Wait(0.6f);
                _pl.moveScale = m.scale; _pl.botInput = m.input;
                // Into the stride, then one cycle (two steps) at 8 frames, timed by the steps landed.
                yield return Wait(m.name == "run" ? 0.6f : 0.9f);
                int s0 = _body.StepsLanded;
                while (_body.StepsLanded == s0) yield return null;
                float t0 = Time.time;
                int s1 = _body.StepsLanded;
                while (_body.StepsLanded < s1 + 2) yield return null;
                float cycle = Time.time - t0;
                _pl.Teleport(p + Vector3.up * 0.05f, yaw, 0f);
                yield return Wait(m.name == "run" ? 0.6f : 0.9f);
                s0 = _body.StepsLanded;
                while (_body.StepsLanded == s0) yield return null;
                t0 = Time.time;
                for (int f = 0; f < 8; f++)
                {
                    while (Time.time < t0 + cycle * f / 8f) yield return null;
                    ShowAll(true);
                    var t = _root;
                    Vector3 fwd = LegFwd, right = LegRight;
                    Vector3 travel = m.name == "strafe" ? right : fwd;
                    Vector3 side = Vector3.Cross(Vector3.up, travel).normalized;      // to the right of the travel direction
                    Vector3 sideAt = t.position - side * 3.4f + Vector3.up * 0.95f;
                    foreach (float sd in new[] { 3.4f, 2.8f, 2.2f, -3.4f, -2.8f })
                    {
                        Vector3 c = t.position - side * sd + Vector3.up * 0.95f;
                        if (!Physics.Linecast(t.position + Vector3.up * 0.85f, c, ~0, QueryTriggerInteraction.Ignore)) { sideAt = c; break; }
                    }
                    cam.transform.position = sideAt;
                    cam.transform.LookAt(t.position + Vector3.up * 0.85f);
                    Save(cam, rt, tex, $"film_{m.name}_side_{f}.png");
                    // From the front (a little to the side), at the first distance with nothing in the way (pillars).
                    Vector3 look = t.position + Vector3.up * 0.85f;
                    Vector3 at = t.position + travel * 2.4f + Vector3.up * 1.0f;
                    foreach (float ang in new[] { 20f, -20f, 40f, -40f, 0f })
                        foreach (float dist in new[] { 3.2f, 2.6f, 2.0f })
                        {
                            Vector3 c = t.position + Quaternion.AngleAxis(ang, Vector3.up) * travel * dist + Vector3.up * 1.0f;
                            if (!Physics.Linecast(look, c, ~0, QueryTriggerInteraction.Ignore)) { at = c; goto placed; }
                        }
                    placed:
                    cam.transform.position = at;
                    cam.transform.LookAt(look);
                    Save(cam, rt, tex, $"film_{m.name}_front_{f}.png");
                    ShowAll(false);
                }
                _log.AppendLine($"{m.name}: cycle {cycle:0.00} s");
                _pl.botInput = Vector2.zero; _pl.moveScale = 1f;
            }
            _pl.botInput = null;
            Destroy(rt); Destroy(tex); Destroy(go);
            File.WriteAllText(Path.Combine(folder, "film.txt"), _log.ToString());
        }

        // ---------------- First-person view ----------------

        IEnumerator ViewRoutine()
        {
            FindBones();
            Vector3 p = FindOpen();
            float yaw = _root.eulerAngles.y;
            _pl.Teleport(p + Vector3.up * 0.05f, yaw, 0f);
            _pl.botInput = Vector2.zero;
            yield return Wait(1f);
            var go = new GameObject("GaitProbeEye");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            var rt = new RenderTexture(640, 360, 24);
            var tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
            var tex2 = new Texture2D(640, 360, TextureFormat.RGB24, false);
            var pitchField = typeof(CamcorderPlayerController).GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance);
            float limit = _pl.pitchLimitDown;
            _log.AppendLine($"pitch limit down {limit:0}");
            foreach (bool walking in new[] { false, true })
                foreach (float pitch in new[] { 0f, 45f, limit, 70f })
                {
                    _pl.Teleport(p + Vector3.up * 0.05f, yaw, 0f);
                    _pl.pitchLimitDown = Mathf.Max(limit, pitch);
                    pitchField.SetValue(_pl, pitch);
                    _pl.botInput = walking ? new Vector2(0, 1) : Vector2.zero;
                    yield return Wait(walking ? 0.9f : 0.6f);
                    var main = Camera.main;
                    cam.CopyFrom(main);
                    cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                    string name = $"fp_{(walking ? "walk" : "stand")}_{pitch:0}";
                    cam.targetTexture = rt;
                    var prev = RenderTexture.active;
                    cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); tex.Apply();
                    // The same frame without the body (its parts set to shadows only, so the shadow stays): the difference is the body.
                    var hidden = new List<Renderer>();
                    foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
                        if (r.enabled && r.gameObject.activeInHierarchy && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly && !(r is ParticleSystemRenderer)) { hidden.Add(r); r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; }
                    cam.Render();
                    tex2.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); tex2.Apply();
                    foreach (var r in hidden) r.shadowCastingMode = ShadowCastingMode.On;
                    RenderTexture.active = prev;
                    var a = tex.GetPixels32(); var b = tex2.GetPixels32();
                    int body = 0, bottomRow = -1;
                    for (int i = 0; i < a.Length; i++)
                    {
                        int d = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                        if (d > 12) { body++; bottomRow = Mathf.Max(bottomRow, i / 640); }
                    }
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
                    _log.AppendLine($"{name}: body {100f * body / a.Length:0.0} % of the frame, reaching {(bottomRow >= 0 ? 100f * (bottomRow + 1) / 360f : 0f):0} % up from the bottom; camera {Vector3.Dot(main.transform.position - _pl.cameraPitchPivot.position, _root.forward) * 100f:0.0} cm ahead of the eye, {(main.transform.position.y - _pl.cameraPitchPivot.position.y) * 100f:0.0} cm up");
                }
            _pl.pitchLimitDown = limit;
            pitchField.SetValue(_pl, 0f);
            _pl.botInput = null;
            Destroy(rt); Destroy(tex); Destroy(tex2); Destroy(go);
            File.WriteAllText(Path.Combine(folder, "view.txt"), _log.ToString());
        }

        // ---------------- The hand (the hand reviewer's measure, 2026-10-28) ----------------

        /// <summary>
        /// The hand reviewer's measure (scratchpad tools/measure_render_swap.cs, the same numbers): elbow and wrist angles
        /// against human limits, the palm against its grip and the surface, each finger's joints and tip against what is
        /// held, skin vertices inside it; renders (eye and six close views) into dir when render; then drops the held item
        /// and picks up next ("" keeps it, "none" holds nothing).
        /// </summary>
        /// <summary>
        /// The hand reviewer's round-2 probe (scratchpad tools/measure_r2.cs, the same numbers): MeasureHand's plus each
        /// finger joint's angle and the thumb's turn, the shoulder-to-palm distance, and skin vertices inside the held
        /// item's visible mesh (an odd-even ray test). Renders into dir when render.
        /// </summary>
        public static string MeasureR2(string dir, string tag, bool render) => MeasureR2(dir, tag, render, false);

        /// <summary>As above; bodyTouch: against the body's own touch layer (a wall lean, a rail) instead of PropGrabber's.</summary>
        public static string MeasureR2(string dir, string tag, bool render, bool bodyTouch)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab0 = pl.GetComponent<PropGrabber>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var grab = new ContactOf(grab0, bodyTouch ? (System.Func<Vector3, float>)System.Delegate.CreateDelegate(typeof(System.Func<Vector3, float>), fpb,
                                         typeof(FirstPersonBody).GetMethod("TouchDistance", bf)) : null);
            System.Func<string, object> G = n => typeof(FirstPersonBody).GetField(n, bf).GetValue(fpb);
            var upper = (Transform)G("_upper"); var fore = (Transform)G("_fore"); var hand = (Transform)G("_hand");
            Vector3 foreBackL = (Vector3)G("_foreBack"), handFL = (Vector3)G("_handFingers"), handBL = (Vector3)G("_handBack"), upperBackL = (Vector3)G("_upperBack"), w2g = (Vector3)G("_wristToGrip");
            var sb = new StringBuilder();
            var held = grab.HeldItem;
            sb.AppendFormat("[{0}] held={1} pitch={2:0} state={3} onDoor={4}\n", tag, held ? held.name : "-", pl.Pitch, grab.Current, grab.OnDoor);
            Vector3 S = upper.position, E = fore.position, W = hand.position;
            Vector3 u = (E - S).normalized, A = (W - E).normalized;
            float elbowFlex = Vector3.Angle(u, A);
            Vector3 Bk = Vector3.ProjectOnPlane(fore.TransformDirection(foreBackL), A).normalized;
            Vector3 F = hand.TransformDirection(handFL).normalized, H = hand.TransformDirection(handBL).normalized;
            float flex = Mathf.Asin(Mathf.Clamp(-Vector3.Dot(F, Bk), -1f, 1f)) * Mathf.Rad2Deg;
            float dev = Mathf.Atan2(Vector3.Dot(F, Vector3.Cross(Bk, A)), Vector3.Dot(F, A)) * Mathf.Rad2Deg;
            float wristWring = Vector3.SignedAngle(Bk, Vector3.ProjectOnPlane(H, A), A);
            Vector3 b0 = -Vector3.Cross(u, A).normalized;
            float foreTwist = Vector3.SignedAngle(b0, Bk, A);
            float totalTwist = Vector3.SignedAngle(b0, Vector3.ProjectOnPlane(H, A), A);
            Vector3 Ub = Vector3.ProjectOnPlane(upper.TransformDirection(upperBackL), u).normalized;
            float upperRoll = Vector3.SignedAngle(Vector3.ProjectOnPlane(b0, u).normalized, Ub, u);
            Quaternion hf = Quaternion.LookRotation(F, H);
            Vector3 gripW = W + hf * w2g;
            sb.AppendFormat("elbowFlex {0:0} | wrist flex(+palm) {1:0} dev(+ulnar) {2:0} | twist total {3:0} fore {4:0} wristWring {5:0} | upperRoll vs hinge {6:0} | elbow-shoulder dy {7:0.00}m | shoulder->palm {8:0.000}\n", elbowFlex, flex, dev, totalTwist, foreTwist, wristWring, upperRoll, E.y - S.y, (gripW - S).magnitude);
            sb.AppendFormat("blend {0:0.00} curl {1:0.00} contact {2} | palm pt to surface {3:0.0}mm | palm pt off wanted {4:0.0}mm | hand angle off wanted {5:0.0}deg | body PalmError {6:0.0}mm\n", grab.HandBlend, grab.HandCurl, grab.HasContact, grab.ContactDistance(gripW) * 1000f, (grab.HandPosition - gripW).magnitude * 1000f, Quaternion.Angle(hf, grab.HandRotation), fpb.PalmError * 1000f);
            var fingers = (System.Collections.IList)G("_fingers");
            foreach (var f in fingers)
            {
                var ft = f.GetType();
                string name = (string)ft.GetField("name").GetValue(f);
                var joints = (System.Collections.IList)ft.GetField("joints").GetValue(f);
                Vector3 tipLocal = (Vector3)ft.GetField("tipLocal").GetValue(f);
                float c = (float)ft.GetField("curl").GetValue(f);
                var aa = (float[])ft.GetField("a").GetValue(f);
                float od = (float)ft.GetField("oppDeg").GetValue(f);
                sb.AppendFormat("{0,-6} curl {1:0.00} deg({2:0},{3:0},{4:0}) opp {5:0} joints(mm): ", name, c, aa[0], aa[1], aa[2], od);
                Transform last = null;
                foreach (var j in joints) { var t = (Transform)j.GetType().GetField("Item1").GetValue(j); last = t; sb.AppendFormat("{0:0.0} ", grab.ContactDistance(t.position) * 1000f); }
                Vector3 tip = last.TransformPoint(tipLocal);
                sb.AppendFormat("tip {0:0.0}\n", grab.ContactDistance(tip) * 1000f);
            }
            SkinnedMeshRenderer armR = null;
            foreach (var r in fpb.GetComponentsInChildren<SkinnedMeshRenderer>()) if (r.name.EndsWith(".ArmR") && !r.name.Contains("suit")) armR = r;
            var skin = new List<Vector3>();
            if (armR) { var mesh = new Mesh(); armR.BakeMesh(mesh, true); foreach (var lv in mesh.vertices) { Vector3 w = armR.transform.position + armR.transform.rotation * lv; if ((w - W).sqrMagnitude < 0.25f * 0.25f) skin.Add(w); } Destroy(mesh); }
            {
                int inside = 0, inside5 = 0, near = 0; float minD = 1e9f;
                foreach (var w in skin) { float d = grab.ContactDistance(w); if (d < minD) minD = d; if (d < -0.002f) inside++; if (d < -0.005f) inside5++; if (d < 0.003f) near++; }
                sb.AppendFormat("skin vs contact shape: inside>2mm {0}, >5mm {1}, touching<3mm {2}, deepest {3:0.0}mm\n", inside, inside5, near, minD * 1000f);
            }
            if (held)
            {
                int layer = -1; for (int L = 31; L > 8; L--) if (string.IsNullOrEmpty(LayerMask.LayerToName(L))) { layer = L; break; }
                var tmp = new GameObject("CritVis"); var made = new List<Mesh>();
                foreach (var mr in held.GetComponentsInChildren<Renderer>())
                {
                    if (!mr.enabled || mr is ParticleSystemRenderer) continue;
                    Mesh m = null; Matrix4x4 mtx;
                    if (mr is SkinnedMeshRenderer smr) { m = new Mesh(); smr.BakeMesh(m, true); made.Add(m); mtx = Matrix4x4.TRS(mr.transform.position, mr.transform.rotation, Vector3.one); }
                    else { var mf = mr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; m = mf.sharedMesh; mtx = mr.transform.localToWorldMatrix; }
                    var go = new GameObject("v"); go.transform.SetParent(tmp.transform, false);
                    var mc = go.AddComponent<MeshCollider>();
                    var copy = Instantiate(m); var vs = copy.vertices; for (int i = 0; i < vs.Length; i++) vs[i] = mtx.MultiplyPoint3x4(vs[i]); copy.vertices = vs; copy.RecalculateBounds(); made.Add(copy);
                    mc.sharedMesh = copy; go.layer = layer;
                }
                Physics.SyncTransforms();
                bool oldBack = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
                int mask = 1 << layer; int inV = 0; float deepest = 0f; var hits = new RaycastHit[32];
                foreach (var w in skin)
                {
                    int n = Physics.RaycastNonAlloc(w, new Vector3(0.577f, 0.577f, 0.577f), hits, 2f, mask, QueryTriggerInteraction.Collide);
                    if ((n & 1) == 0) continue;
                    int n2 = Physics.RaycastNonAlloc(w, new Vector3(-0.6f, 0.3f, -0.74f).normalized, hits, 2f, mask, QueryTriggerInteraction.Collide);
                    if ((n2 & 1) == 0) continue;
                    inV++;
                    float dmin = 1f;
                    foreach (var dd in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back }) if (Physics.Raycast(w, dd, out var h, 0.2f, mask, QueryTriggerInteraction.Collide)) dmin = Mathf.Min(dmin, h.distance);
                    if (dmin < 1f) deepest = Mathf.Max(deepest, dmin);
                }
                Physics.queriesHitBackfaces = oldBack;
                int vis = 0, outside5 = 0, insideShape10 = 0;
                foreach (var mc in tmp.GetComponentsInChildren<MeshCollider>()) foreach (var v in mc.sharedMesh.vertices) { if ((v - gripW).sqrMagnitude > 0.07f * 0.07f) continue; vis++; float d = grab.ContactDistance(v); if (d > 0.005f) outside5++; if (d < -0.010f) insideShape10++; }
                sb.AppendFormat("skin inside VISIBLE item mesh: {0} verts, deepest ~{1:0.0}mm | item mesh verts within 7cm of palm: {2}, >5mm outside contact shape {3}, >10mm inside it {4}\n", inV, deepest * 1000f, vis, outside5, insideShape10);
                DestroyImmediate(tmp); foreach (var m in made) Destroy(m);
            }
            if (render)
            {
                Directory.CreateDirectory(dir);
                var main = Camera.main;
                var go = new GameObject("CritCam"); var cam = go.AddComponent<Camera>(); cam.enabled = false;
                var lg = new GameObject("CritFill"); lg.transform.SetParent(go.transform, false); var fill = lg.AddComponent<Light>(); fill.type = LightType.Point; fill.range = 3f; fill.intensity = 0.25f; fill.shadows = LightShadows.None; fill.renderingLayerMask = -1;
                var rt = new RenderTexture(960, 720, 24); var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
                var t = pl.transform; Vector3 at = grab.HandBlend > 0.5f ? gripW : W; Vector3 mid = (fore.position + hand.position) * 0.5f;
                var names = new[] { "eyeHD", "right", "left", "front", "top", "below", "arm" };
                var froms = new[] { main.transform.position, at + t.right * 0.3f + Vector3.up * 0.05f, at - t.right * 0.3f + Vector3.up * 0.05f + t.forward * 0.05f, at + t.forward * 0.3f + Vector3.up * 0.04f, at + Vector3.up * 0.3f + t.forward * 0.02f, at - Vector3.up * 0.26f + t.forward * 0.1f + t.right * 0.05f, mid + t.right * 0.6f + Vector3.up * 0.15f + t.forward * 0.1f };
                var ats = new[] { Vector3.zero, at, at, at, at, at, mid };
                for (int k = 0; k < names.Length; k++)
                {
                    cam.CopyFrom(main);
                    if (k == 0) { fill.enabled = false; cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation); }
                    else { cam.fieldOfView = k == 6 ? 45f : 40f; cam.nearClipPlane = 0.02f; fill.enabled = true; cam.transform.position = froms[k]; cam.transform.LookAt(ats[k]); }
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                    var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); tex.Apply(); RenderTexture.active = old;
                    File.WriteAllBytes(Path.Combine(dir, tag + "_" + names[k] + ".png"), tex.EncodeToPNG());
                }
                Destroy(go); Destroy(rt); Destroy(tex);
            }
            return sb.ToString();
        }

        /// <summary>PropGrabber's numbers, its contact (or another surface) for the measure.</summary>
        sealed class ContactOf
        {
            readonly PropGrabber _g; readonly System.Func<Vector3, float> _c;
            public ContactOf(PropGrabber g, System.Func<Vector3, float> c) { _g = g; _c = c; }
            public float ContactDistance(Vector3 p) => _c != null ? _c(p) : _g.ContactDistance(p);
            public WorldItem HeldItem => _g.HeldItem;
            public PropGrabber.State Current => _g.Current;
            public bool OnDoor => _g.OnDoor;
            public float HandBlend => _g.HandBlend;
            public float HandCurl => _g.HandCurl;
            public bool HasContact => _g.HasContact;
            public Vector3 HandPosition => _g.HandPosition;
            public Quaternion HandRotation => _g.HandRotation;
        }

        /// <summary>For hand tests: the held item put away, the player at (x, z) facing yaw and pitch, holding a new id ("" none).</summary>
        public static string Hold(string id, float x, float z, float yaw, float pitch)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab = pl.GetComponent<PropGrabber>();
            if (grab.HeldItem) { var old = grab.HeldItem; grab.Drop(); Destroy(old.gameObject); }
            pl.TeleportSafe(new Vector3(x, pl.transform.position.y + 0.05f, z), yaw, pitch);
            if (string.IsNullOrEmpty(id)) return "holding nothing";
            var t = pl.transform;
            var item = ItemFactory.Spawn(id, t.position + t.forward * 0.4f + Vector3.up * 1.0f, t.rotation);
            grab.PickUp(item);
            return "holding " + id;
        }

        /// <summary>
        /// Round-5 hand test of plugging in (2026-10-28): holding id ("desklamp", the coil "extensioncord", or "cordplug": an
        /// extension cord's plug), the player dist m out from the free wall socket nearest (x, z), facing and aimed at it;
        /// clicks at frame 30 and logs every third frame for frames frames into file (hand and plug to the socket, PalmError,
        /// elbow, stoop, what is held, the item set down and how fast it moves, the player), then a summary. reelFrames: then
        /// holds the right mouse that many frames holding what is in the hand (reeling a cord in).
        /// </summary>
        public static string PlugInRun(string id, float x, float z, float dist, string file, int frames, int reelFrames)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab = pl.GetComponent<PropGrabber>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            Socket sock = null; float bestD = float.MaxValue;
            foreach (var s in Sockets.Around(new Vector3(x, 0f, z)))
            {
                if (!s.IsWall || s.occupant != null || s.dead) continue;
                float d = Vector3.Distance(new Vector3(s.Position.x, 0f, s.Position.z), new Vector3(x, 0f, z));
                if (d < bestD) { bestD = d; sock = s; }
            }
            if (sock == null) return "no socket";
            // (An outlet has two: the plug goes into the one the view picks.)
            var pair = new List<Vector3>();
            foreach (var s in Sockets.Around(sock.Position)) if ((s.Position - sock.Position).magnitude < 0.06f) pair.Add(s.Position);
            Vector3 n = sock.Normal; n.y = 0f; n.Normalize();
            float yaw = Quaternion.LookRotation(-n).eulerAngles.y;
            Vector3 stand = sock.Position + n * dist;
            WorldItem target = null;
            // Nothing left lying about from an earlier run (a lamp under the feet held the body 15 cm up).
            if (grab.HeldItem) { var old = grab.HeldItem; grab.Drop(); Destroy(old.gameObject); }
            foreach (var it in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
                if (!it.Held && Vector3.Distance(it.transform.position, stand) < 4f) Destroy(it.gameObject);
            if (id == "cordplug")
            {
                Hold("", stand.x, stand.z, yaw, 0f);
                var box = ItemFactory.Spawn("extensioncord", pl.transform.position + pl.transform.right * 0.5f + Vector3.up * 0.1f, pl.transform.rotation) as CordBox;
                if (box) { box.Deploy(null, pl.transform.position + pl.transform.forward * 0.3f + Vector3.up * 0.9f); target = box.Plug; grab.PickUp(target); }
            }
            else { Hold(id, stand.x, stand.z, yaw, 0f); target = grab.HeldItem; }
            Vector3 eye = pl.cameraPitchPivot ? pl.cameraPitchPivot.position : pl.transform.position + Vector3.up * 1.58f;
            Vector3 to = sock.Position - eye;
            pl.TeleportSafe(pl.transform.position, yaw, Mathf.Atan2(-to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var upper = (Transform)typeof(FirstPersonBody).GetField("_upper", bf).GetValue(fpb);
            var fore = (Transform)typeof(FirstPersonBody).GetField("_fore", bf).GetValue(fpb);
            var hand = (Transform)typeof(FirstPersonBody).GetField("_hand", bf).GetValue(fpb);
            var sb = new StringBuilder();
            sb.AppendFormat("{0}: socket {1} normal {2}, standing {3:0.00} m out\n", id, sock.Position.ToString("F2"), sock.Normal.ToString("F1"), dist);
            sb.AppendLine("frame | t | held | plugged | gave up | hand->socket m | plug->socket mm | PalmError mm | elbow | stoop | curl | set-down item pos | its speed m/s | player");
            int frame = 0; float t0 = Time.time;
            var owner = target as PowerUser ?? (target is Plug pg ? pg.owner as PowerUser : null);
            float maxErr = 0f, minElbow = 999f, maxSpeed = 0f, inAt = -1f, pressAt = -1f, maxStep = 0f, maxStepAll = 0f; int pumps = 0, plugFrames = 0; float lastElbow = -1f, lastDir = 0f;
            Vector3 lastPlug = Vector3.zero, setAt = Vector3.zero; bool approach = false, setSeen = false;
            UnityEngine.Events.UnityAction act = null;
            act = () =>
            {
                frame++;
                if (frame == 30) { grab.botHold = true; pressAt = Time.time; }
                if (frame == 32) grab.botHold = false;
                if (frame == 33) grab.botHold = null;
                // Reeling from the far end: the hand takes the item (a lamp) and pulls its cord in.
                if (reelFrames > 0 && frame == frames && owner && !owner.Held) grab.PickUp(owner);
                bool reeling = frame > frames + 20 && frame <= frames + reelFrames;
                if (owner && !owner.Held && !setSeen) { setSeen = true; setAt = owner.transform.position; }
                grab.botRmb = reeling;
                var plug = owner ? owner.Plug : target as Plug;
                Vector3 S = upper.position, E = fore.position, W = hand.position;
                float elbow = Vector3.Angle(E - S, W - E);
                bool plugged = plug && plug.Plugged;
                if (plugged && inAt < 0f) inAt = Time.time - pressAt;
                approach = frame > 30 && plug && plug.GoingInto;
                if (approach)
                {
                    maxErr = Mathf.Max(maxErr, fpb.PalmError);
                    minElbow = Mathf.Min(minElbow, elbow);
                    // A pump: the elbow bending back the other way by 5 degrees or more.
                    if (lastElbow < 0f) lastElbow = elbow;
                    else if (Mathf.Abs(elbow - lastElbow) >= 5f) { float dir = Mathf.Sign(elbow - lastElbow); if (lastDir != 0f && dir != lastDir) pumps++; lastDir = dir; lastElbow = elbow; }
                    if (lastPlug != Vector3.zero) maxStep = Mathf.Max(maxStep, (plug.transform.position - lastPlug).magnitude);
                }
                // Every step of the plug from the click on, the last of the way in too (round 6: it snapped 8-9 mm in a frame
                // when the hand let go).
                if (plug && plugged && frame > 30 && lastPlug != Vector3.zero) maxStepAll = Mathf.Max(maxStepAll, (plug.transform.position - lastPlug).magnitude);
                if (plugged) plugFrames++;
                if (plug) lastPlug = plug.transform.position;
                float speed = owner && owner.Body && !owner.Held ? owner.Body.linearVelocity.magnitude : 0f;
                maxSpeed = Mathf.Max(maxSpeed, speed);
                if (frame <= 3 || frame % 3 == 0 || (plugFrames > 0 && plugFrames <= 10))
                    sb.AppendFormat("{0} | {1:0.00} | {2} | {3} | {4} | {5:0.000} | {6:0.0} | {7:0.0} | {8:0} | {9:0.00} | {10:0.00} | {11} | {12:0.00} | {13}\n", frame, Time.time - t0,
                        grab.HeldItem ? grab.HeldItem.name : "-", plugged, plug && plug.GaveUp, Vector3.Distance(grab.HandPosition, sock.Position),
                        plug ? NearestOf(pair, plug.transform.position) * 1000f : -1f, Mathf.Min(fpb.PalmError, 9.999f) * 1000f, elbow, pl.stoop, grab.HandCurl,
                        owner ? owner.transform.position.ToString("F2") : "-", speed, pl.transform.position.ToString("F2"));
                if (frame >= frames + reelFrames)
                {
                    Application.onBeforeRender -= act;
                    grab.botRmb = false; grab.botHold = null;
                    sb.AppendFormat("summary: plugged in {0} s after the click; while carrying: PalmError max {1:0.0} mm, elbow min {2:0}, elbow reversals {3}, plug's biggest step {4:0.0} mm (in the socket, seating: {8:0.0}); set-down item fastest {5:0.00} m/s, moved {6:0.00} m, ends at {7}\n",
                        inAt >= 0f ? inAt.ToString("0.00") : "never", maxErr * 1000f, minElbow, pumps, maxStep * 1000f, maxSpeed,
                        setSeen && !owner.Held ? Vector3.Distance(owner.transform.position, setAt) : 0f, owner ? owner.transform.position.ToString("F2") : "-", maxStepAll * 1000f);
                    File.WriteAllText(file, sb.ToString());
                }
            };
            Application.onBeforeRender += act;
            return "running: socket " + sock.Position.ToString("F2");
        }

        static float NearestOf(List<Vector3> ps, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var q in ps) best = Mathf.Min(best, Vector3.Distance(p, q));
            return best;
        }

        /// <summary>
        /// The skin of each finger against what the hand holds (or the body's touch: a wall, a rail): for the index, middle,
        /// ring, little finger, thumb and the rest of the hand (palm), the nearest its skin comes (mm, negative inside) and
        /// how many of its vertices are more than 2 mm in. Twelve numbers: six nearest, six counts. The arm's mesh as drawn,
        /// each vertex to the finger bone it lies round (within 16.5 mm; the base of the finger is the palm's).
        /// </summary>
        public static float[] FingerSkinGaps(bool bodyTouch)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab = pl.GetComponent<PropGrabber>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            System.Func<Vector3, float> contact = bodyTouch
                ? (System.Func<Vector3, float>)System.Delegate.CreateDelegate(typeof(System.Func<Vector3, float>), fpb, typeof(FirstPersonBody).GetMethod("TouchDistance", bf))
                : grab.ContactDistance;
            var hand = (Transform)typeof(FirstPersonBody).GetField("_hand", bf).GetValue(fpb);
            var fingers = (System.Collections.IList)typeof(FirstPersonBody).GetField("_fingers", bf).GetValue(fpb);
            var res = new float[12];
            for (int k = 0; k < 6; k++) res[k] = 1000f;
            if (fingers == null) return res;
            var pts = new List<Vector3[]>(); var idx = new List<int>();
            foreach (var f in fingers)
            {
                var ft = f.GetType();
                string name = (string)ft.GetField("name").GetValue(f);
                var joints = (System.Collections.IList)ft.GetField("joints").GetValue(f);
                Vector3 tipLocal = (Vector3)ft.GetField("tipLocal").GetValue(f);
                var p = new Vector3[joints.Count + 1];
                Transform last = null;
                for (int j = 0; j < joints.Count; j++) { last = (Transform)joints[j].GetType().GetField("Item1").GetValue(joints[j]); p[j] = last.position; }
                p[joints.Count] = last.TransformPoint(tipLocal);
                pts.Add(p);
                idx.Add(name == "Index" ? 0 : name == "Middle" ? 1 : name == "Ring" ? 2 : name == "Pinky" ? 3 : 4);
            }
            SkinnedMeshRenderer armR = null;
            foreach (var r in fpb.GetComponentsInChildren<SkinnedMeshRenderer>()) if (r.name.EndsWith(".ArmR") && !r.name.Contains("suit")) armR = r;
            if (!armR) return res;
            var mesh = new Mesh(); armR.BakeMesh(mesh, true);
            var vs = mesh.vertices; Destroy(mesh);
            Vector3 sp = armR.transform.position; Quaternion sr = armR.transform.rotation;
            foreach (var lv in vs)
            {
                Vector3 w = sp + sr * lv;
                if ((w - hand.position).sqrMagnitude > 0.2f * 0.2f) continue;
                float bestD = 0.0165f; int g = 5;
                for (int fi = 0; fi < pts.Count; fi++)
                    for (int sgi = 0; sgi + 1 < pts[fi].Length; sgi++)
                    {
                        Vector3 a = pts[fi][sgi], ab = pts[fi][sgi + 1] - a;
                        float t = Mathf.Clamp01(Vector3.Dot(w - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                        float d = (w - (a + ab * t)).magnitude;
                        if (d < bestD && !(sgi == 0 && t < 0.35f)) { bestD = d; g = idx[fi]; }
                    }
                float c = contact(w);
                res[g] = Mathf.Min(res[g], c * 1000f);
                if (c < -0.002f) res[6 + g]++;
            }
            return res;
        }

        /// <summary>
        /// For grip searches (round 6): the held item placed and the arm and fingers worked out again now, n times, the
        /// fingers put straight onto where they close (not eased from the frame before).
        /// </summary>
        public static void Settle(int n)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab = pl.GetComponent<PropGrabber>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var lu = typeof(PropGrabber).GetMethod("LateUpdate", bf);
            var ua = typeof(FirstPersonBody).GetMethod("UpdateArm", bf);
            var fingers = (System.Collections.IList)typeof(FirstPersonBody).GetField("_fingers", bf).GetValue(fpb);
            for (int k = 0; k < n; k++)
            {
                if (fingers != null) foreach (var f in fingers) f.GetType().GetField("placed").SetValue(f, false);
                lu.Invoke(grab, null);
                ua.Invoke(fpb, null);
            }
        }

        /// <summary>
        /// The hand on what it holds (round 6): each finger's end bone's pad clearance (mm; index, middle, ring, little,
        /// thumb), then the index fingertip, the thumb's tip, the hand's fingers direction and the back of the hand (world).
        /// </summary>
        public static float[] GripReport()
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab = pl.GetComponent<PropGrabber>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var T = typeof(FirstPersonBody);
            var fingers = (System.Collections.IList)T.GetField("_fingers", bf).GetValue(fpb);
            var hand = (Transform)T.GetField("_hand", bf).GetValue(fpb);
            Vector3 handFL = (Vector3)T.GetField("_handFingers", bf).GetValue(fpb), handBL = (Vector3)T.GetField("_handBack", bf).GetValue(fpb);
            var bone = T.GetMethod("BoneClearance", BindingFlags.NonPublic | BindingFlags.Static);
            System.Func<Vector3, float> contact = grab.ContactDistance;
            var res = new float[17];
            foreach (var f in fingers)
            {
                var ft = f.GetType();
                string name = (string)ft.GetField("name").GetValue(f);
                var joints = (System.Collections.IList)ft.GetField("joints").GetValue(f);
                int i = name == "Index" ? 0 : name == "Middle" ? 1 : name == "Ring" ? 2 : name == "Pinky" ? 3 : 4;
                res[i] = (float)bone.Invoke(null, new object[] { f, joints.Count - 1, contact }) * 1000f;
                var last = (Transform)joints[joints.Count - 1].GetType().GetField("Item1").GetValue(joints[joints.Count - 1]);
                Vector3 tip = last.TransformPoint((Vector3)ft.GetField("tipLocal").GetValue(f));
                if (i == 0) { res[5] = tip.x; res[6] = tip.y; res[7] = tip.z; }
                if (i == 4) { res[14] = tip.x; res[15] = tip.y; res[16] = tip.z; }
            }
            Vector3 F = hand.TransformDirection(handFL).normalized, H = hand.TransformDirection(handBL).normalized;
            res[8] = F.x; res[9] = F.y; res[10] = F.z; res[11] = H.x; res[12] = H.y; res[13] = H.z;
            return res;
        }

        /// <summary>
        /// The ways a palm can lie on a face (round 6): for fingers turned about the face's normal every 15 degrees from
        /// prefer, the arm solver's best score over the elbow's swing and that pose's forearm twist, wrist bend (+ toward
        /// the palm) and sideways bend (+ ulnar), and how far the hand misses its turn. For choosing touch turns.
        /// </summary>
        public static string TouchTurns(Vector3 point, Vector3 normal, Vector3 prefer)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var grab = pl.GetComponent<PropGrabber>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var T = typeof(FirstPersonBody);
            var solve = T.GetMethod("SolveArm", bf);
            Vector3 w2g = (Vector3)T.GetField("_wristToGrip", bf).GetValue(fpb);
            Vector3 shoulder = fpb.ShoulderNow; Quaternion yaw = pl.transform.rotation; float side = grab.Side;
            Vector3 basis = Vector3.ProjectOnPlane(prefer, normal).normalized;
            var sb = new StringBuilder();
            for (int a = -180; a < 180; a += 15)
            {
                Vector3 f = Quaternion.AngleAxis(a, normal) * basis;
                Quaternion hand = Quaternion.LookRotation(f, normal);
                Vector3 wrist = point - hand * w2g;
                Vector3 dir0 = (wrist - shoulder).normalized;
                Vector3 pole0 = Vector3.ProjectOnPlane(yaw * new Vector3(0.8f * side, -1f, -0.25f), dir0).normalized;
                float best = float.MaxValue; object bestP = null;
                for (int k = -8; k <= 10; k++)
                {
                    object p = solve.Invoke(fpb, new object[] { shoulder, wrist, hand, pole0, k * 15f, side, yaw });
                    float s = (float)p.GetType().GetField("score").GetValue(p);
                    if (s < best) { best = s; bestP = p; }
                }
                var pt = bestP.GetType();
                Vector3 el = (Vector3)pt.GetField("elbow").GetValue(bestP), wr = (Vector3)pt.GetField("wrist").GetValue(bestP), fb = (Vector3)pt.GetField("foreBack").GetValue(bestP);
                Quaternion ph = (Quaternion)pt.GetField("hand").GetValue(bestP);
                float tw = (float)pt.GetField("twist").GetValue(bestP);
                Vector3 fo = (wr - el).normalized;
                Vector3 Fl = Quaternion.Inverse(Quaternion.LookRotation(fo, fb)) * (hand * Vector3.forward);
                float flex = Mathf.Asin(Mathf.Clamp(-Fl.y, -1f, 1f)) * Mathf.Rad2Deg, dev = Mathf.Atan2(Fl.x, Fl.z) * Mathf.Rad2Deg;
                sb.AppendFormat("a {0,4} score {1:0.000} twist {2:0} flex {3:0} dev {4:0} miss {5:0.0} elbowFlex {6:0}\n", a, best, tw, flex, dev, Quaternion.Angle(ph, hand), Vector3.Angle(el - shoulder, wr - el));
            }
            return sb.ToString();
        }

        /// <summary>FingerSkinGaps as a line.</summary>
        public static string FingerSkin(bool bodyTouch)
        {
            var g = FingerSkinGaps(bodyTouch);
            return string.Format("finger skin nearest mm (verts >2 mm in): index {0:0.0} ({6}), middle {1:0.0} ({7}), ring {2:0.0} ({8}), little {3:0.0} ({9}), thumb {4:0.0} ({10}), palm {5:0.0} ({11})",
                                 g[0], g[1], g[2], g[3], g[4], g[5], g[6], g[7], g[8], g[9], g[10], g[11]);
        }

        /// <summary>
        /// Holds each of ids (comma separated) in turn at (x, z) facing yaw and pitch, and after 75 frames writes MeasureR2's
        /// numbers and FingerSkin's into file (renders into dir if render).
        /// </summary>
        public static string MeasureItems(string ids, float x, float z, float yaw, float pitch, string dir, string file, bool render)
        {
            var list = ids.Split(',');
            var sb = new StringBuilder();
            int frame = 0;
            UnityEngine.Events.UnityAction act = null;
            act = () =>
            {
                int k = frame / 90, f = frame % 90;
                frame++;
                if (k >= list.Length) { Application.onBeforeRender -= act; File.WriteAllText(file, sb.ToString()); return; }
                if (f == 0) Hold(list[k].Trim(), x, z, yaw, pitch);
                if (f == 75) { sb.Append(MeasureR2(dir, "r5_" + list[k].Trim(), render)); sb.AppendLine(FingerSkin(false)); sb.AppendLine(); }
            };
            Application.onBeforeRender += act;
            return "measuring " + list.Length;
        }

        /// <summary>
        /// The steadying hand on a test rail (a box 5 by 5 cm, 1 m long, at hip height, 0.38 m to the hand's side) or a test
        /// wall (0.5 m to the side): the player at (x, z) facing yaw with nothing held, the touch put on it as the body's touch
        /// layer would, then after 70 frames MeasureR2 against the touch (renders into dir if render) and FingerSkin, into file.
        /// </summary>
        public static string TouchTest(bool rail, float x, float z, float yaw, string dir, string tag, string file, bool render)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var grab = pl.GetComponent<PropGrabber>();
            Hold("", x, z, yaw, 0f);
            var t = pl.transform;
            float side = grab.Side;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = rail ? "TestRail" : "TestWall";
            cube.transform.rotation = t.rotation;
            if (rail) { cube.transform.localScale = new Vector3(0.05f, 0.05f, 1f); cube.transform.position = t.position + t.right * side * 0.36f + t.forward * 0.2f + Vector3.up * 1.0f; }
            else { cube.transform.localScale = new Vector3(0.1f, 2.2f, 2f); cube.transform.position = t.position + t.right * side * 0.55f + Vector3.up * 1.1f; }
            Physics.SyncTransforms();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var T = typeof(FirstPersonBody);
            int frame = 0;
            UnityEngine.Events.UnityAction act = null;
            act = () =>
            {
                frame++;
                if (frame == 5)
                {
                    // As UpdateTouch would find it.
                    Vector3 shoulder = fpb.ShoulderNow - Vector3.up * 0.18f;
                    var col = cube.GetComponent<Collider>();
                    Vector3 dir = rail ? (cube.transform.position - fpb.ShoulderNow).normalized : t.right * side;
                    if (rail) shoulder = fpb.ShoulderNow;
                    if (!col.Raycast(new Ray(shoulder, dir), out var hit, 1.5f)) { Application.onBeforeRender -= act; Destroy(cube); return; }
                    T.GetField("_touchFingersSet", bf).SetValue(fpb, false);
                    var b = col.bounds;
                    T.GetField("_touching", bf).SetValue(fpb, true);
                    T.GetField("_touchRail", bf).SetValue(fpb, rail);
                    Vector3 point = rail ? new Vector3(hit.point.x, b.max.y, hit.point.z) : hit.point;
                    if (!rail) point = (Vector3)T.GetMethod("ComfortableTouch", bf | BindingFlags.Static).Invoke(fpb, new object[] { hit, fpb.ShoulderNow, t.forward, t.right * side });
                    T.GetField("_touchPoint", bf).SetValue(fpb, point);
                    T.GetField("_touchNormal", bf).SetValue(fpb, hit.normal);
                    T.GetField("_touchAlong", bf).SetValue(fpb, b.size.x > b.size.z ? Vector3.right : Vector3.forward);
                    T.GetField("_touchRailCentre", bf).SetValue(fpb, b.center);
                    T.GetField("_touchRailR", bf).SetValue(fpb, Mathf.Max(0.008f, Mathf.Min(Mathf.Min(b.size.x, b.size.z), b.size.y) * 0.5f));
                    ((Collider[])T.GetField("_touchColArr", bf).GetValue(fpb))[0] = col;
                    T.GetField("_touchUntil", bf).SetValue(fpb, Time.time + 100f);
                }
                if (frame == 75)
                {
                    Application.onBeforeRender -= act;
                    string r = MeasureR2(dir, tag, render, true) + FingerSkin(true) + "\n";
                    File.AppendAllText(file, r + "\n");
                    T.GetField("_touchUntil", bf).SetValue(fpb, Time.time);
                    Destroy(cube);
                }
            };
            Application.onBeforeRender += act;
            return "touch test " + tag;
        }

        public static string MeasureHand(string dir, string tag, string next, bool render)
        {
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var grab = pl.GetComponent<PropGrabber>();
            var fpb = pl.GetComponent<FirstPersonBody>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            System.Func<string, object> G = n => typeof(FirstPersonBody).GetField(n, bf).GetValue(fpb);
            var upper = (Transform)G("_upper"); var fore = (Transform)G("_fore"); var hand = (Transform)G("_hand");
            Vector3 foreBackL = (Vector3)G("_foreBack"), handFL = (Vector3)G("_handFingers"), handBL = (Vector3)G("_handBack"), upperBackL = (Vector3)G("_upperBack"), w2g = (Vector3)G("_wristToGrip");
            var sb = new StringBuilder();
            sb.AppendFormat("[{0}] held={1} pitch={2:0}\n", tag, grab.HeldItem ? grab.HeldItem.name : "-", pl.Pitch);
            Vector3 S = upper.position, E = fore.position, W = hand.position;
            Vector3 u = (E - S).normalized, A = (W - E).normalized;
            float elbowFlex = Vector3.Angle(u, A);
            Vector3 Bk = Vector3.ProjectOnPlane(fore.TransformDirection(foreBackL), A).normalized;
            Vector3 F = hand.TransformDirection(handFL).normalized, H = hand.TransformDirection(handBL).normalized;
            Vector3 Sx = Vector3.Cross(Bk, A);
            float flex = Mathf.Asin(Mathf.Clamp(-Vector3.Dot(F, Bk), -1f, 1f)) * Mathf.Rad2Deg;
            float dev = Mathf.Atan2(Vector3.Dot(F, Sx), Vector3.Dot(F, A)) * Mathf.Rad2Deg;
            float wristWring = Vector3.SignedAngle(Bk, Vector3.ProjectOnPlane(H, A), A);
            Vector3 b0 = -Vector3.Cross(u, A).normalized;
            float foreTwist = Vector3.SignedAngle(b0, Bk, A);
            float totalTwist = Vector3.SignedAngle(b0, Vector3.ProjectOnPlane(H, A), A);
            Vector3 Ub = Vector3.ProjectOnPlane(upper.TransformDirection(upperBackL), u).normalized;
            float upperRoll = Vector3.SignedAngle(Vector3.ProjectOnPlane(b0, u).normalized, Ub, u);
            Quaternion hf = Quaternion.LookRotation(F, H);
            Vector3 gripW = W + hf * w2g;
            sb.AppendFormat("elbowFlex {0:0} | wrist flex(+palm) {1:0} dev(+ulnar) {2:0} | twist total {3:0} fore {4:0} wristWring {5:0} | upperRoll vs hinge {6:0} | elbow height vs shoulder {7:0.00}m\n", elbowFlex, flex, dev, totalTwist, foreTwist, wristWring, upperRoll, E.y - S.y);
            sb.AppendFormat("blend {0:0.00} curl {1:0.00} contact {2} | palm pt to surface {3:0.0}mm | palm pt off wanted {4:0.0}mm | hand angle off wanted {5:0.0}deg | body PalmError {6:0.0}mm\n", grab.HandBlend, grab.HandCurl, grab.HasContact, grab.ContactDistance(gripW) * 1000f, (grab.HandPosition - gripW).magnitude * 1000f, Quaternion.Angle(hf, grab.HandRotation), fpb.PalmError * 1000f);
            var fingers = (System.Collections.IList)G("_fingers");
            foreach (var f in fingers)
            {
                var ft = f.GetType();
                string name = (string)ft.GetField("name").GetValue(f);
                var joints = (System.Collections.IList)ft.GetField("joints").GetValue(f);
                Vector3 tipLocal = (Vector3)ft.GetField("tipLocal").GetValue(f);
                float c = (float)ft.GetField("curl").GetValue(f);
                sb.AppendFormat("{0,-6} curl {1:0.00} joints(mm): ", name, c);
                Transform last = null;
                foreach (var j in joints) { var t = (Transform)j.GetType().GetField("Item1").GetValue(j); last = t; sb.AppendFormat("{0:0.0} ", grab.ContactDistance(t.position) * 1000f); }
                Vector3 tip = last.TransformPoint(tipLocal);
                Vector3 tl = Quaternion.Inverse(hf) * (tip - W);
                sb.AppendFormat("tip {0:0.0} | tip in hand (pinky+,back+,fingers+) ({1:0.000},{2:0.000},{3:0.000})\n", grab.ContactDistance(tip) * 1000f, tl.x, tl.y, tl.z);
            }
            foreach (var r in fpb.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!r.name.EndsWith(".ArmR") || r.name.Contains("suit")) continue;
                var mesh = new Mesh(); r.BakeMesh(mesh, true);
                var vs = mesh.vertices; int inside = 0, inside5 = 0, near = 0; float minD = 1e9f; Vector3 minP = Vector3.zero;
                foreach (var lv in vs)
                {
                    Vector3 w = r.transform.position + r.transform.rotation * lv;
                    if ((w - W).sqrMagnitude > 0.25f * 0.25f) continue;
                    float d = grab.ContactDistance(w);
                    if (d < minD) { minD = d; minP = w; }
                    if (d < -0.002f) inside++;
                    if (d < -0.005f) inside5++;
                    if (d < 0.003f) near++;
                }
                Vector3 mpl = Quaternion.Inverse(hf) * (minP - W);
                sb.AppendFormat("skin: verts inside >2mm {0}, >5mm {1}, touching(<3mm) {2}, deepest {3:0.0}mm at hand({4:0.000},{5:0.000},{6:0.000})\n", inside, inside5, near, minD * 1000f, mpl.x, mpl.y, mpl.z);
                Destroy(mesh);
            }
            if (render)
            {
                Directory.CreateDirectory(dir);
                var main = Camera.main;
                var go = new GameObject("CritCam"); var cam = go.AddComponent<Camera>(); cam.enabled = false;
                var lg = new GameObject("CritFill"); lg.transform.SetParent(go.transform, false); var fill = lg.AddComponent<Light>(); fill.type = LightType.Point; fill.range = 3f; fill.intensity = 1.4f; fill.shadows = LightShadows.None; fill.renderingLayerMask = -1;
                var rt = new RenderTexture(960, 720, 24); var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
                var tr = pl.transform; Vector3 at = grab.HandPosition; Vector3 mid = (fore.position + hand.position) * 0.5f;
                var names = new[] { "eyeHD", "right", "left", "front", "top", "below", "arm" };
                var froms = new[] { main.transform.position, at + tr.right * 0.35f + Vector3.up * 0.05f, at - tr.right * 0.35f + Vector3.up * 0.05f + tr.forward * 0.05f, at + tr.forward * 0.35f + Vector3.up * 0.04f, at + Vector3.up * 0.35f + tr.forward * 0.02f, at - Vector3.up * 0.3f + tr.forward * 0.12f + tr.right * 0.05f, mid + tr.right * 0.6f + Vector3.up * 0.15f + tr.forward * 0.1f };
                var ats = new[] { Vector3.zero, at, at, at, at, at, mid };
                for (int k = 0; k < names.Length; k++)
                {
                    cam.CopyFrom(main);
                    if (k == 0) { fill.enabled = false; cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation); }
                    else { cam.fieldOfView = k == 6 ? 45f : 40f; cam.nearClipPlane = 0.02f; fill.enabled = true; cam.transform.position = froms[k]; cam.transform.LookAt(ats[k]); }
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                    var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); tex.Apply(); RenderTexture.active = old;
                    File.WriteAllBytes(Path.Combine(dir, tag + "_" + names[k] + ".png"), tex.EncodeToPNG());
                }
                Destroy(go); Destroy(rt); Destroy(tex);
            }
            if (next != "")
            {
                var old = grab.HeldItem;
                if (old) { grab.Drop(); Destroy(old.gameObject); }
                if (next == "plug")
                {
                    // The plug of a fresh desk lamp.
                    var tt = pl.transform;
                    var lamp = ItemFactory.Spawn("desklamp", tt.position + tt.forward * 0.6f + Vector3.up * 0.3f, tt.rotation);
                    Plug plug = null;
                    foreach (var p in FindObjectsByType<Plug>(FindObjectsInactive.Exclude)) if (p.owner == lamp) plug = p;
                    if (plug) { grab.PickUp(plug); sb.Append("picked up the lamp's plug\n"); }
                }
                else if (next != "none")
                {
                    var tt = pl.transform;
                    var item = ItemFactory.Spawn(next, tt.position + tt.forward * 0.6f + Vector3.up * 0.3f, tt.rotation);
                    grab.PickUp(item);
                    sb.Append("picked up " + next + "\n");
                }
            }
            return sb.ToString();
        }
    }
}
