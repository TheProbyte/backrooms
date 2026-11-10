using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The player's body model (2026-10-27): the MakeHuman body (Resources/Player/MPFB/PlayerMPFB: MPFB's game-engine
    /// skeleton, exported by Tools/Blender/export_player_mpfb.py), or the older procedural one (Player/PlayerBody with
    /// the separate Player/PlayerArm models).
    ///
    /// The MakeHuman body is one skinned body with both arms: the working arm is its own (driven by the same IK as the
    /// separate arm was, its shoulder carried by the collarbone), the other arm holds the camcorder to the eye. Its parts
    /// are split at export by the bones that move them (.Head, .ArmL, .ArmR): the head and the camcorder arm are drawn into
    /// the shadows only, as the camera is in the head and the camcorder at the eye. The separate arm model is kept for
    /// holding things while crawling (the body is hidden then).
    /// </summary>
    public partial class FirstPersonBody
    {
        [Tooltip("Use the MakeHuman body (Resources/Player/MPFB) when it is there.")]
        public bool useMakeHumanBody = true;
        [Tooltip("The hazmat suit (Resources/Player/Hazmat/PlayerHazmatSuit), rigged on the MakeHuman body's skeleton, in its place.")]
        public bool useHazmatSuit = true;
        bool _isHazmat;

        bool _isMpfb;

        /// <summary>The body's bone for a part, in either model.</summary>
        string Bone(string part)
        {
            if (!_isMpfb) return part;
            switch (part)
            {
                case "Hips": return "pelvis";
                case "Torso": return "spine_01";
                case "Neck": return "neck_01";
                case "Head": return "head";
                case "ThighL": return "thigh_l";
                case "ThighR": return "thigh_r";
                case "ShinL": return "calf_l";
                case "ShinR": return "calf_r";
                case "FootL": return "foot_l";
                case "FootR": return "foot_r";
                default: return part;
            }
        }

        // ---------------- Spine ----------------
        // The torso's bend, twist and lean spread over the spine's bones (one bone on the old body).

        class SpineBone { public Transform t; public Quaternion rest; public Vector3 bend, up, fwd; public float w; }
        readonly List<SpineBone> _spine = new List<SpineBone>();

        void MeasureSpine()
        {
            _spine.Clear();
            if (_isMpfb)
            {
                AddSpine(Find(_body, "spine_01"), 0.4f);
                AddSpine(Find(_body, "spine_02"), 0.35f);
                AddSpine(Find(_body, "spine_03"), 0.25f);
            }
            if (_spine.Count == 0) AddSpine(_torso, 1f);
        }

        void AddSpine(Transform t, float w)
        {
            if (!t) return;
            _spine.Add(new SpineBone
            {
                t = t, rest = t.localRotation, w = w,
                bend = t.InverseTransformDirection(transform.right), up = t.InverseTransformDirection(transform.up),
                fwd = t.InverseTransformDirection(transform.forward),
            });
        }

        /// <summary>How far forward and down the shoulder goes as the spine bends 0, 10 .. 90 degrees (m, measured on the rig).</summary>
        readonly float[] _bendAhead = new float[10], _bendDrop = new float[10];
        bool _bendMeasured;

        /// <summary>
        /// Measures where the shoulder goes as the spine bends (the body is placed so the shoulder keeps its place under the
        /// eye). A rigid 0.65 m lever from the hips put it 17-27 cm further back than the rig's bending spine does: stooping
        /// to a sofa's seat, the shoulder ended up 0.5 m behind the eye, the seat out of the arm's reach.
        /// </summary>
        void MeasureBend()
        {
            _bendMeasured = false;
            Transform s = Find(_body, "upperarm_r");
            if (!s) s = Find(_body, Bone("Neck"));
            if (!s || _spine.Count == 0) return;
            Vector3 p0 = Vector3.zero;
            for (int i = 0; i < 10; i++)
            {
                PoseSpine(i * 10f, 0f, 0f);
                Vector3 p = _body.InverseTransformPoint(s.position);
                if (i == 0) p0 = p;
                _bendAhead[i] = p.z - p0.z;
                _bendDrop[i] = p0.y - p.y;
            }
            PoseSpine(0f, 0f, 0f);
            _bendMeasured = _bendAhead[9] > 0.05f;
        }

        // ---------------- Hinging at the hips (round 9) ----------------
        // Reaching over a desk or a board the body bends forward from the hips, the knees nearly straight: the pelvis tips
        // forward over the thighs (HingePelvis of the angle), the spine bends the rest and turns the reaching shoulder a
        // little forward, and the hips stay where they are (a stoop puts them back so the shoulder stays under the eye). The
        // shoulder and the head go forward and down; the view goes with the head (PropGrabber.ApplyHingeView). Before, the
        // body squatted for a desk with the arm straight and reached about 0.2 m in from its edge.

        /// <summary>How far forward and down the shoulder and the head go hinging 0, 10 .. 80 degrees (m, measured on the rig),
        /// for a spine already bent 0, 11 .. 44 degrees by a squat (HingeBends): bent over already, a hinge takes the shoulder
        /// mostly down, not forward (one table for upright put a floor reach's shoulder 15 cm too far forward).</summary>
        readonly Vector2[,] _hingeShoulder = new Vector2[HingeBends, 9], _hingeHead = new Vector2[HingeBends, 9];
        const int HingeBends = 5;
        const float HingeBendStep = 11f;
        bool _hingeMeasured;
        float _hingeTwistSign = 1f;
        /// <summary>The share of a hinge the pelvis takes (tipping forward over the thighs); the spine bends the rest.</summary>
        public const float HingePelvis = 0.65f;
        /// <summary>The spine's turn bringing the reaching shoulder forward, at a 60-degree hinge (degrees).</summary>
        public const float HingeTwist = 20f;
        /// <summary>The feet and hips come this far forward under the view at a full hinge (m): up to the desk's edge.</summary>
        public const float HingeShiftMax = 0.1f;

        static float HingeTwistAt(float h) => HingeTwist * Mathf.Clamp01(h / 60f);
        /// <summary>How far the body comes forward under the view for a hinge (m).</summary>
        public static float HingeShift(float h) => HingeShiftMax * Mathf.Clamp01(h / 60f);

        void MeasureHinge()
        {
            _hingeMeasured = false;
            Transform s = Find(_body, "upperarm_r"), hd = Find(_body, Bone("Head"));
            if (!s || !hd || !_hips || _spine.Count == 0) return;
            Quaternion rest = _hips.rotation;
            Vector3 right = _body.right;
            // Which way the spine's turn brings the right shoulder forward.
            PoseSpine(0f, 15f, 0f);
            float plus = _body.InverseTransformPoint(s.position).z;
            PoseSpine(0f, -15f, 0f);
            _hingeTwistSign = plus >= _body.InverseTransformPoint(s.position).z ? 1f : -1f;
            for (int b = 0; b < HingeBends; b++)
            {
                Vector3 s0 = Vector3.zero, h0 = Vector3.zero;
                for (int i = 0; i < 9; i++)
                {
                    float h = i * 10f;
                    _hips.rotation = Quaternion.AngleAxis(h * HingePelvis, right) * rest;
                    PoseSpine(b * HingeBendStep + h * (1f - HingePelvis), HingeTwistAt(h) * _hingeTwistSign, 0f);
                    Vector3 p = _body.InverseTransformPoint(s.position), q = _body.InverseTransformPoint(hd.position);
                    if (i == 0) { s0 = p; h0 = q; }
                    _hingeShoulder[b, i] = new Vector2(p.z - s0.z, s0.y - p.y);
                    _hingeHead[b, i] = new Vector2(q.z - h0.z, h0.y - q.y);
                }
            }
            _hips.rotation = rest;
            PoseSpine(0f, 0f, 0f);
            _hingeMeasured = _hingeShoulder[0, 6].x > 0.1f;
        }

        Vector2 HingeTable(Vector2[,] t, float h, float bend, float lever)
        {
            if (!_hingeMeasured)
            {
                float a0 = Mathf.Clamp(bend, 0f, 80f) * Mathf.Deg2Rad, a = Mathf.Clamp(bend + h, 0f, 120f) * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(a) - Mathf.Sin(a0), Mathf.Cos(a0) - Mathf.Cos(a)) * lever;
            }
            float x = Mathf.Clamp(h, 0f, 80f) / 10f, y = Mathf.Clamp(bend, 0f, HingeBendStep * (HingeBends - 1)) / HingeBendStep;
            int i = Mathf.Min((int)x, 7), j = Mathf.Min((int)y, HingeBends - 2);
            Vector2 lo = Vector2.Lerp(t[j, i], t[j, i + 1], x - i), hi = Vector2.Lerp(t[j + 1, i], t[j + 1, i + 1], x - i);
            return Vector2.Lerp(lo, hi, y - j);
        }

        /// <summary>The spine's bend for a stoop (the eye down this far, m), as UpdateBody makes it standing.</summary>
        public static float StoopBend(float stoop) => Mathf.Clamp(stoop, 0f, 0.8f) * 55f;

        /// <summary>The working shoulder's move for a hinge of h degrees (forward, down; m) on top of a stoop (the eye down that
        /// far, m), the body's shift forward included.</summary>
        public Vector2 HingeShoulder(float h, float stoop = 0f) => HingeTable(_hingeShoulder, h, StoopBend(stoop), 0.47f) + new Vector2(HingeShift(h), 0f);
        /// <summary>The head's (the view's) move for a hinge of h degrees (forward, down; m) on top of a stoop, the shift included.</summary>
        public Vector2 HingeHead(float h, float stoop = 0f) => HingeTable(_hingeHead, h, StoopBend(stoop), 0.66f) + new Vector2(HingeShift(h), 0f);

        static float BendTable(float[] t, float bend)
        {
            float x = Mathf.Clamp(bend, 0f, 90f) / 10f;
            int i = Mathf.Min((int)x, 8);
            return Mathf.Lerp(t[i], t[i + 1], x - i);
        }

        void PoseSpine(float bend, float twist, float roll)
        {
            // Rest first, all of them (a child's measured axes are in its own frame, so this order works top to bottom).
            foreach (var s in _spine)
                s.t.localRotation = s.rest * Quaternion.AngleAxis(bend * s.w, s.bend) * Quaternion.AngleAxis(twist * s.w, s.up)
                                    * Quaternion.AngleAxis(roll * s.w, s.fwd);
        }

        // ---------------- Arms ----------------

        /// <summary>One arm's bones and what was measured of them (a separate arm model, or one of the body's own arms).</summary>
        class ArmRig
        {
            public Transform root;                         // a separate model's root, moved to the shoulder; null for the body's arm
            public Transform clavicle, upper, fore, hand;
            public Quaternion clavicleRest;
            public float upperLen, foreLen;
            public Vector3 upperAxis, upperBack, foreAxis, foreBack, handFingers, handBack, wristToGrip, wristToTip;
            public Vector3 thumbOppAxis;                  // the thumb's base turns about this toward the palm (its local frame)
            public readonly List<(Transform t, Quaternion rest, Vector3 axis, float weight, string finger)> joints =
                new List<(Transform, Quaternion, Vector3, float, string)>();
            public List<Finger> fingers;
            public Renderer[] renderers;
            public SkinnedMeshRenderer skin;               // the mesh with the hand's skin (held things are kept out of it)
            public int[] handVerts;                        // its vertices on the palm side of the hand (palm, heel, finger-base pads)
        }
        ArmRig _bodyArm, _looseArm, _curArm;

        /// <summary>
        /// The arm's skinned mesh with the hand's skin, and which of its vertices are the palm's (picked in the rest pose: the
        /// palm, its heel and the pads at the base of the fingers, not the fingers or the thumb past its base): a held item
        /// is kept out of the skin itself (a few points on the palm missed the pad at the ring finger's base, where a
        /// hammer's handle sat 10.6 mm in; picked where they were each frame, curled fingertips under the palm counted and
        /// pushed the handle 20 mm off the palm). side: "r" or "l" for the body's arm (its .ArmR / .ArmL part), null for a
        /// separate arm. Call with the arm at rest.
        /// </summary>
        static void FindHandSkin(ArmRig r, Transform root, string side)
        {
            var baked = new Mesh();
            var vs = new List<Vector3>();
            var near = new List<int>();
            int bestN = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = smr.name;
                if (n.Contains("suit") || (side != null && !n.EndsWith(side == "r" ? ".ArmR" : ".ArmL"))) continue;
                smr.BakeMesh(baked, true);
                baked.GetVertices(vs);
                near.Clear();
                Vector3 w = r.hand.position, p = smr.transform.position;
                Quaternion q = smr.transform.rotation;
                Quaternion inv = Quaternion.Inverse(Quaternion.LookRotation(r.hand.TransformDirection(r.handFingers), r.hand.TransformDirection(r.handBack)));
                // Which way the thumb is (the hand's frame is not mirrored for a left hand).
                float thumbSide = -1f;
                foreach (var j in r.joints) if (j.finger == "Thumb") { thumbSide = Mathf.Sign((inv * (j.t.position - w)).x); break; }
                for (int i = 0; i < vs.Count; i++)
                {
                    Vector3 l = inv * (p + q * vs[i] - w);
                    if (l.y > 0.004f || l.y < -0.04f || l.z < -0.01f || l.z > 0.12f || Mathf.Abs(l.x) > 0.055f) continue;
                    if (l.x * thumbSide > 0.035f && l.z > 0.05f) continue;           // the thumb past its base
                    near.Add(i);
                }
                if (near.Count > bestN) { bestN = near.Count; r.skin = smr; r.handVerts = near.ToArray(); }
            }
            if (Application.isPlaying) Object.Destroy(baked); else Object.DestroyImmediate(baked);
        }

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

        /// <summary>A finger joint (1..3) of an arm: "Index2" on the separate model, "index_02_r" on the body.</summary>
        static Transform FingerOf(Transform root, string finger, int i, string side)
            => Find(root, side == null ? finger + i : finger.ToLowerInvariant() + "_0" + i + "_" + side);

        /// <summary>
        /// Measures an arm in its rest pose. back: the back of the hand at rest (a separate model's watch face, or across
        /// the knuckles on the body's hand), so the code never depends on how the FBX axes were converted.
        /// </summary>
        static ArmRig MeasureArmRig(Transform root, Transform clavicle, Transform upper, Transform fore, Transform hand, string side, bool setThumb)
        {
            var r = new ArmRig { root = side == null ? root : null, clavicle = clavicle, upper = upper, fore = fore, hand = hand };
            if (clavicle) r.clavicleRest = clavicle.localRotation;
            r.upperLen = Vector3.Distance(upper.position, fore.position);
            r.foreLen = Vector3.Distance(fore.position, hand.position);
            Vector3 boneW = (fore.position - upper.position).normalized;
            Vector3 foreW = (hand.position - fore.position).normalized;
            var middle = FingerOf(root, "Middle", 1, side);
            Vector3 fingersRaw = middle ? (middle.position - hand.position).normalized : foreW;
            Vector3 backW;
            var watch = side == null ? Find(root, "WatchCase") : null;
            if (watch) backW = Vector3.ProjectOnPlane(watch.position - fore.position, boneW).normalized;
            else
            {
                var ix = FingerOf(root, "Index", 1, side); var pk = FingerOf(root, "Pinky", 1, side);
                if (ix && pk)
                {
                    // Across the knuckles toward the thumb, crossed with the fingers: the back of the hand (the right hand's
                    // thumb is on the left of its back, so the cross is the other way round for the left hand).
                    Vector3 across = (ix.position - pk.position).normalized;
                    backW = Vector3.Cross(across, fingersRaw).normalized * (side == "l" ? -1f : 1f);
                }
                else backW = Vector3.up;
            }
            r.upperAxis = upper.InverseTransformDirection(boneW);
            // The upper arm's back: the elbow's hinge at rest (the solver puts the hinge there), signed like the back of
            // the hand (which lies within a few degrees of it in MakeHuman's rest pose); a straight rest arm has no hinge,
            // so then the back of the hand.
            Vector3 hingeW = Vector3.Cross(boneW, foreW);
            Vector3 upperBackW = Vector3.ProjectOnPlane(backW, boneW).normalized;
            if (hingeW.sqrMagnitude > 0.03f * 0.03f)
            {
                hingeW = Vector3.ProjectOnPlane(hingeW, boneW).normalized;
                upperBackW = Vector3.Dot(hingeW, upperBackW) >= 0f ? hingeW : -hingeW;
            }
            r.upperBack = upper.InverseTransformDirection(upperBackW);
            r.foreAxis = fore.InverseTransformDirection(foreW);
            r.foreBack = fore.InverseTransformDirection(Vector3.ProjectOnPlane(backW, foreW).normalized);
            Vector3 fingersW = Vector3.ProjectOnPlane(fingersRaw, backW).normalized;
            backW = Vector3.ProjectOnPlane(backW, fingersW).normalized;
            r.handFingers = hand.InverseTransformDirection(fingersW);
            r.handBack = hand.InverseTransformDirection(backW);
            // Where the hand touches what it holds: under the knuckles, on the palm side.
            float palmLen = middle ? Vector3.Dot(middle.position - hand.position, fingersW) : 0.1f;
            r.wristToGrip = new Vector3(0f, -0.018f, palmLen * 0.78f);     // (side, back, fingers)

            var ix2 = FingerOf(root, "Index", 2, side); var ix3 = FingerOf(root, "Index", 3, side);
            if (ix2 && ix3)
            {
                Vector3 tipW = ix3.position + (ix3.position - ix2.position).normalized * (Vector3.Distance(ix2.position, ix3.position) * 0.9f);
                Vector3 d = tipW - hand.position, sideRest = Vector3.Cross(backW, fingersW).normalized;
                r.wristToTip = new Vector3(Vector3.Dot(d, sideRest), Vector3.Dot(d, backW), Vector3.Dot(d, fingersW));
            }

            Vector3 sideW = Vector3.Cross(backW, fingersW).normalized;
            var thumb1 = FingerOf(root, "Thumb", 1, side); var thumb2 = FingerOf(root, "Thumb", 2, side);
            if (thumb1 && thumb2)
            {
                if (Vector3.Dot(sideW, thumb1.position - hand.position) < 0f) sideW = -sideW;      // toward the thumb
                if (setThumb)
                {
                    // The separate model's thumb points up and out: out to the side, forward and down toward the palm.
                    Vector3 now = (thumb2.position - thumb1.position).normalized;
                    Vector3 want = (sideW * 0.45f + fingersW * 0.75f - backW * 0.5f).normalized;
                    thumb1.rotation = Quaternion.FromToRotation(now, want) * thumb1.rotation;
                }
            }
            // Finger joints bend toward the palm: the bend axis whose turn takes the middle fingertip away from the back.
            Vector3 bendW = Vector3.Cross(backW, fingersW).normalized;
            var m2 = FingerOf(root, "Middle", 2, side);
            if (middle && m2)
            {
                Quaternion r0 = middle.rotation;
                Vector3 before = m2.position;
                middle.rotation = Quaternion.AngleAxis(30f, bendW) * r0;
                if (Vector3.Dot(m2.position - before, backW) > 0f) bendW = -bendW;
                middle.rotation = r0;
            }
            // The thumb folds across the palm (toward the index finger's base and the palm side).
            Vector3 thumbBendW = bendW;
            if (thumb1 && thumb2)
            {
                Vector3 thumbDir = (thumb2.position - thumb1.position).normalized;
                Vector3 across = (-sideW * 0.75f - backW * 0.55f + fingersW * 0.25f).normalized;
                Vector3 ax = Vector3.Cross(thumbDir, across);
                if (ax.sqrMagnitude > 1e-6f) thumbBendW = ax.normalized;
            }
            // The thumb's opposition: its base turns toward the palm side (signed so a positive turn takes the thumb tip
            // toward the palm), so it can close against the fingers round what the hand holds.
            if (thumb1 && thumb2)
            {
                Vector3 thumbDir = (thumb2.position - thumb1.position).normalized;
                Vector3 oppW = Vector3.Cross(thumbDir, -backW);
                if (oppW.sqrMagnitude > 1e-6f)
                {
                    oppW.Normalize();
                    var t3 = FingerOf(root, "Thumb", 3, side);
                    Transform tipT = t3 ? t3 : thumb2;
                    Quaternion r0 = thumb1.rotation;
                    float before = Vector3.Dot(tipT.position - hand.position, -backW);
                    thumb1.rotation = Quaternion.AngleAxis(25f, oppW) * r0;
                    if (Vector3.Dot(tipT.position - hand.position, -backW) < before) oppW = -oppW;
                    thumb1.rotation = r0;
                    r.thumbOppAxis = thumb1.InverseTransformDirection(oppW);
                }
            }
            foreach (var name in FingerNames)
                for (int i = 1; i <= 3; i++)
                {
                    var t = FingerOf(root, name, i, side);
                    if (!t) continue;
                    // The thumb's joints flex further (0.25, 0.75, 0.85 of a finger's: it could only get to 29 degrees).
                    float w = name == "Thumb" ? (i == 1 ? 0.25f : i == 2 ? 0.75f : 0.85f) : i == 1 ? 1f : i == 2 ? 1.25f : 0.8f;
                    r.joints.Add((t, t.localRotation, t.InverseTransformDirection(name == "Thumb" ? thumbBendW : bendW), w, name));
                }
            return r;
        }

        /// <summary>Makes this arm the one the hand code drives (the body's own, or the separate model while crawling).</summary>
        void UseArm(ArmRig r)
        {
            _curArm = r;
            if (r == null) return;
            _arm = r.root ? r.root : r.upper;
            _upper = r.upper; _fore = r.fore; _hand = r.hand;
            _upperLen = r.upperLen; _foreLen = r.foreLen;
            _upperAxis = r.upperAxis; _upperBack = r.upperBack; _foreAxis = r.foreAxis; _foreBack = r.foreBack;
            _handFingers = r.handFingers; _handBack = r.handBack; _wristToGrip = r.wristToGrip; _wristToTip = r.wristToTip;
            _fingerJoints.Clear(); _fingerJoints.AddRange(r.joints);
            _thumbOppAxis = r.thumbOppAxis;
            if (r.fingers == null) { _fingers = null; GroupFingers(); r.fingers = _fingers; }
            else _fingers = r.fingers;
            _swivelSet = false;
        }

        /// <summary>
        /// The body's shoulder brought toward where the arm wants it (the collarbone turns: up, down, forward, as a real
        /// shoulder goes with a reach), at most maxDeg from its pose; returns where the shoulder joint then is.
        /// </summary>
        static Vector3 CarryShoulder(Transform clavicle, Quaternion clavicleRest, Transform upper, Vector3 want, float maxDeg)
        {
            if (!clavicle) return upper.position;
            clavicle.localRotation = clavicleRest;
            Vector3 from = upper.position - clavicle.position, to = want - clavicle.position;
            Quaternion turn = Quaternion.FromToRotation(from, to);
            turn.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            if (Mathf.Abs(ang) > maxDeg) turn = Quaternion.AngleAxis(Mathf.Sign(ang) * maxDeg, axis);
            clavicle.rotation = turn * clavicle.rotation;
            return upper.position;
        }

        // ---------------- The MakeHuman body's materials and parts ----------------

        /// <summary>Backrooms/Lit materials with the MakeHuman textures (Resources/Player/MPFB/Textures, by asset name).</summary>
        Material MpfbMaterial(Material imported, Shader shader)
        {
            string key = imported ? imported.name.Replace(" (Instance)", "") : "PlayerMPFB.body";
            if (_mats.TryGetValue(key, out var m)) return m;
            string asset = key.StartsWith("PlayerMPFB.") ? key.Substring("PlayerMPFB.".Length) : key;
            m = new Material(shader) { name = "FP_" + key };
            NoDust(m);
            Texture tex = imported ? imported.mainTexture : null;
            if (!tex) tex = Resources.Load<Texture2D>("Player/MPFB/Textures/" + asset + "_diffuse");
            if (!tex) tex = Resources.Load<Texture2D>("Player/MPFB/Textures/" + asset);
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            var nrm = Resources.Load<Texture2D>("Player/MPFB/Textures/" + asset + "_normal");
            if (nrm && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            bool skin = asset == "body";
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", skin ? 0.38f : asset.StartsWith("shoes") ? 0.25f : 0.12f);
            _mats[key] = m;
            return m;
        }

        /// <summary>
        /// The hazmat suit's own materials (URP Lit, from the style lab) moved onto Backrooms/Lit, which has the same properties,
        /// so the light grid lights the suit as it lights the world; their settings (transparent glass and paint, emission) kept.
        /// </summary>
        Material HazmatMaterial(Material src, Shader shader)
        {
            if (!src) return src;
            string key = "Hazmat_" + src.name;
            if (_mats.TryGetValue(key, out var m)) return m;
            m = new Material(src) { name = "FP_" + src.name };
            if (shader && src.shader && src.shader.name == "Universal Render Pipeline/Lit")
            {
                int queue = src.renderQueue;
                m.shader = shader;
                m.renderQueue = queue;
            }
            NoDust(m);
            _mats[key] = m;
            return m;
        }

        /// <summary>The suit's look (face, hair, mask, name): its materials made the same way, and first-person mode.</summary>
        void SetupHazmat(Transform body)
        {
            var ap = body.GetComponent<StyleLab.HazmatAppearance>();
            if (!ap) return;
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (ap.skinTones != null) for (int i = 0; i < ap.skinTones.Length; i++) ap.skinTones[i] = HazmatMaterial(ap.skinTones[i], shader);
            if (ap.hairTones != null) for (int i = 0; i < ap.hairTones.Length; i++) ap.hairTones[i] = HazmatMaterial(ap.hairTones[i], shader);
            ap.labelMaterial = HazmatMaterial(ap.labelMaterial, shader);
            ap.firstPerson = true;
            _appearance = ap;
            ApplyOwnLook();
            StyleLab.PlayerLook.Changed -= ApplyOwnLook;
            StyleLab.PlayerLook.Changed += ApplyOwnLook;
        }

        StyleLab.HazmatAppearance _appearance;

        /// <summary>How loud you are speaking in co-op (0..1, NetWorld.Voice): your own suit's mouth moves with it.</summary>
        public void SetTalk(float level) { if (_appearance) _appearance.talk = Mathf.Clamp01(level); }

        /// <summary>Your look (YOUR LOOK) and your name on the chest tape; again whenever you change it.</summary>
        void ApplyOwnLook()
        {
            if (!_appearance) { StyleLab.PlayerLook.Changed -= ApplyOwnLook; return; }
            StyleLab.PlayerLook.Saved.ApplyTo(_appearance);
            _appearance.playerName = Net.NetSession.PlayerName;
            _appearance.Apply();
        }

        [Tooltip("First person shows little of the body: the trunk (chest, belly, shirt) only in the shadows, the working arm " +
                 "only while it reaches, holds, points or touches. Looking down shows the legs and shoes.")]
        public bool firstPersonTrim = true;

        /// <summary>
        /// A part of the MakeHuman body drawn into the shadows only: the head, the trunk (with firstPersonTrim), and the arm
        /// holding the camcorder. The shadow keeps the whole body.
        /// </summary>
        bool ShadowOnlyPart(string rendererName)
        {
            if (rendererName.EndsWith(".Head")) return true;
            if (firstPersonTrim && rendererName.EndsWith(".Torso")) return true;
            float side = _grab ? _grab.Side : 1f;
            return rendererName.EndsWith(side > 0f ? ".ArmL" : ".ArmR");
        }

        /// <summary>The working arm's parts (the hand that holds things), drawn only while in use (SetWorkingArmShown).</summary>
        bool WorkingArmPart(string rendererName)
        {
            float side = _grab ? _grab.Side : 1f;
            return rendererName.EndsWith(side > 0f ? ".ArmR" : ".ArmL");
        }

        readonly List<Renderer> _workArm = new List<Renderer>();
        bool _workArmShown = true;

        /// <summary>
        /// The working arm drawn (reaching, holding, pointing, a hand on the world) or in the shadows only (hanging at the
        /// side, where it is out of view anyway: it never pops in or out on screen).
        /// </summary>
        void SetWorkingArmShown(bool on)
        {
            if (!firstPersonTrim) on = true;
            if (on == _workArmShown) return;
            _workArmShown = on;
            var mode = on ? (castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off) : ShadowCastingMode.ShadowsOnly;
            foreach (var r in _workArm) if (r) r.shadowCastingMode = mode;
        }

        /// <summary>The working arm is drawn now (for tests).</summary>
        public bool WorkingArmShown => _workArmShown;
    }
}
