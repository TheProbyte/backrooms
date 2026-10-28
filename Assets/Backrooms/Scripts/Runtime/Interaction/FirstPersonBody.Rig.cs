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
            public readonly List<(Transform t, Quaternion rest, Vector3 axis, float weight, string finger)> joints =
                new List<(Transform, Quaternion, Vector3, float, string)>();
            public List<Finger> fingers;
            public Renderer[] renderers;
        }
        ArmRig _bodyArm, _looseArm, _curArm;

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

        /// <summary>A finger joint (1..3) of an arm: "Index2" on the separate model, "index_02_r" on the body.</summary>
        static Transform FingerOf(Transform root, string finger, int i, string side)
            => Find(root, side == null ? finger + i : finger.ToLowerInvariant() + "_0" + i + "_" + side);

        /// <summary>
        /// Measures an arm in its rest pose. back: the back of the hand at rest (a separate model's watch face, or across
        /// the knuckles on the body's hand), so the code never depends on how the FBX axes were converted.
        /// </summary>
        ArmRig MeasureArmRig(Transform root, Transform clavicle, Transform upper, Transform fore, Transform hand, string side, bool setThumb)
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
            r.upperBack = upper.InverseTransformDirection(Vector3.ProjectOnPlane(backW, boneW).normalized);
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
            foreach (var name in FingerNames)
                for (int i = 1; i <= 3; i++)
                {
                    var t = FingerOf(root, name, i, side);
                    if (!t) continue;
                    float w = name == "Thumb" ? (i == 1 ? 0.2f : 0.5f) : i == 1 ? 1f : i == 2 ? 1.25f : 0.8f;
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
