using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// An orange extension cord, <see cref="Length"/> long, with a plug at one end and a box of three sockets at the other.
    ///  - Coiled (as found), it is one bundle. Holding it, click a wall outlet (or another cord's box): the plug goes in and
    ///    you keep the coil; the cord pays out of it as you walk away (lying on the floor behind you), until it is all out
    ///    and you hold just the box at its end.
    ///  - The box's sockets have power when its plug does, so a lamp (or another cord) plugged into it works anywhere the
    ///    cord reaches. At most <see cref="MaxChain"/> cords in a row.
    ///  - Walk further than the cord and the plug pops out. Right mouse while holding it coils it back up (anything
    ///    plugged into it comes out).
    /// The cord is a physics rope (<see cref="Cord"/>): it lies on the floor, drapes over things and catches on corners.
    /// Models: Resources/Items/CordCoil.fbx, CordSocket.fbx, Plug.fbx.
    /// </summary>
    public class CordBox : PowerUser, IHandGrip, IGripRound
    {
        public const float Length = 8f;
        public const int MaxChain = 3;
        public static readonly Color Orange = new Color(0.95f, 0.38f, 0.05f);
        static readonly Vector3[] SocketFaces = { new Vector3(0f, 0.0362f, 0.0259f), new Vector3(0f, 0.0362f, 0.0034f), new Vector3(0f, 0.0362f, -0.0191f) };
        static readonly Vector3 CordEntry = new Vector3(0f, 0.0137f, -0.06f);
        /// <summary>Where the cord leaves the coil while some is still wound (on the ring, away from the hand).</summary>
        static readonly Vector3 CoilExit = new Vector3(-0.03f, 0.045f, -0.105f);
        static readonly Vector3 CoilSize = new Vector3(0.28f, 0.06f, 0.28f), BoxSize = new Vector3(0.045f, 0.036f, 0.075f);
        [Tooltip("Furthest outlet you can plug into from your eye (m).")] public float reach = 2.2f;

        [NotTuned] public bool coiled = true;
        Plug _plug;
        Cord _cord;
        GameObject _coilModel, _boxModel, _bundlePlug;
        readonly List<Socket> _sockets = new List<Socket>();
        BoxCollider _col;
        AudioSource _fx;

        public override Plug Plug => _plug;
        public IReadOnlyList<Socket> BoxSockets => _sockets;
        public bool Powered => !coiled && PluggedLive;
        /// <summary>Plugged in (or laid out) with some of it still on the coil.</summary>
        public bool Wound => !coiled && _cord && _cord.Feeding;
        /// <summary>Metres of cord out of the coil.</summary>
        public float PaidOut => coiled ? 0f : _cord ? _cord.length : Length;
        /// <summary>Held and carried as a coil: all wound up, or still partly.</summary>
        bool Bundle => coiled || Wound;
        public int Depth => 1 + (_plug && _plug.Socket != null ? _plug.Socket.Depth : 0);
        /// <summary>Let go of out of its coil, the box just falls from the hand (as its plug does).</summary>
        public override bool DropsStraight => !coiled;
        // The coil: its strands lie between radius 0.10 and 0.135 m, 0-36 mm up (measured on the model). Held by its near
        // rim, a little to the right: the palm on top of the strands, the fingers forward over them and curled under, the
        // forearm coming straight from behind (held by the outer rim, fingers outward, the hand was 60 degrees past what a
        // wrist turns). (The grip used to be inside a ring fatter than the strands: two fingers through it, two on nothing.)
        // The bundle's section (fitted to the model's strands by the grip, 2026-10-28): an oval 36 mm across and 28 mm high,
        // leaning in at the top (a round section 35 mm across stood 10 mm proud of the strands on the far side: the fingers
        // stopped short of them).
        static readonly Vector3 Rim = new Vector3(0.26f, 0f, -0.966f);
        const float CoilR = 0.118f, CoilY = 0.017f, BundleHalfR = 0.018f, BundleHalfY = 0.014f, BundleLean = 0.2f;
        public override Vector3 HoldOffset => Bundle ? new Vector3(0.17f, -0.37f, 0.4f) : new Vector3(0.15f, -0.22f, 0.35f);
        public override Vector3 GripLocal => Bundle ? Rim * (CoilR - BundleLean * BundleHalfY) + Vector3.up * (CoilY + BundleHalfY + 0.002f)
                                                    : new Vector3(0.02f, 0.02f, 0f);
        public override Quaternion GripRotationLocal => Bundle ? Quaternion.LookRotation(new Vector3(-0.26f, 0f, 0.966f), Vector3.up)
                                                                : Quaternion.LookRotation(new Vector3(-0.4f, -0.3f, 0.86f), Vector3.up);
        /// <summary>Round 10: the coil taken by its rim on whichever side (IGripRound): round its middle.</summary>
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = Vector3.zero; axis = Vector3.up; }
        public override float GripCurl => 0.9f;
        public float FingerCurl(int finger) => 0.92f;
        public float ThumbCurl => Bundle ? 0.8f : 0.7f;
        public float ThumbOpposition => 0.75f;
        /// <summary>Coiled: the ring of cord, of the bundle's oval section (the hand round one side of it); out: the box.</summary>
        public override float ContactDistance(Vector3 p)
        {
            if (!Bundle) return base.ContactDistance(p);
            float y = p.y - CoilY;
            float u = (new Vector2(p.x, p.z).magnitude - (CoilR - BundleLean * y)) / BundleHalfR, v = y / BundleHalfY;
            // An oval's distance (Quilez's approximation).
            float k0 = Mathf.Sqrt(u * u + v * v), k1 = Mathf.Sqrt(u * u / (BundleHalfR * BundleHalfR) + v * v / (BundleHalfY * BundleHalfY));
            return k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -BundleHalfY;
        }
        /// <summary>Saved: 0 coiled, else the share of the cord that is out (old saves: 1, all of it).</summary>
        public override float Amount { get => coiled ? 0f : Mathf.Max(0.02f, PaidOut / Length); set { } }
        public override string Status => coiled ? $"extension cord ({Length:0} m): click an outlet"
                                                : (Powered ? "extension cord: live" : "extension cord: no power")
                                                  + (Wound ? $", {PaidOut:0.0} of {Length:0} m out" : "") + "  (right mouse: coil)";

        public static CordBox Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("ExtensionCord", position, rotation, 1.4f, "CordCoil", CoilSize, "CordOrange", false, new Vector3(0f, 0.03f, 0f));
            go.transform.Find("Model").name = "Coil";
            var box = go.AddComponent<CordBox>();
            box.itemId = "extensioncord"; box.displayName = "Extension cord";
            Generation.PoolLitSwitch.Attach(go);
            return box;
        }

        protected override void Awake()
        {
            base.Awake();
            _fx = ItemModels.Source(gameObject, 10f);
            _col = GetComponent<BoxCollider>();
            var c = transform.Find("Coil");
            _coilModel = c ? c.gameObject : null;
            if (c) _coilScale = c.localScale;
            var boxRoot = new GameObject("Box").transform;
            boxRoot.SetParent(transform, false);
            _boxModel = ItemModels.Attach(boxRoot, "CordSocket", BoxSize, "CordOrange");
            _boxModel = boxRoot.gameObject;
            var plugRoot = new GameObject("BundlePlug").transform;
            plugRoot.SetParent(transform, false);
            ItemModels.Attach(plugRoot, "Plug", new Vector3(0.035f, 0.03f, 0.045f), "PlugBody");
            foreach (var r in plugRoot.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "Item_PlugBody") mats[i] = ItemModels.Mat("PlugBody_" + ColorUtility.ToHtmlStringRGB(Orange), Orange);
                r.sharedMaterials = mats;
            }
            _bundlePlug = plugRoot.gameObject;
            foreach (var f in SocketFaces) _sockets.Add(new Socket { anchor = _boxModel.transform, local = f, localNormal = Vector3.up, box = this });
            Shape();
        }

        bool _shapedBundle;
        Vector3 _coilScale = Vector3.one;

        /// <summary>Coiled: the coil, with the box and the plug tucked beside it. Out: just the box. Paying out: the coil with
        /// the box tucked in, thinner as it empties, the cord running from it.</summary>
        void Shape()
        {
            bool bundle = Bundle;
            _shapedBundle = bundle;
            if (_coilModel) _coilModel.SetActive(bundle);
            _bundlePlug.SetActive(coiled);
            if (_cord) _cord.bLocal = bundle ? CoilExit : CordEntry;
            if (bundle)
            {
                _boxModel.transform.localPosition = new Vector3(0.175f, 0f, 0.07f);
                _boxModel.transform.localRotation = Quaternion.Euler(0f, 70f, 0f);
                _bundlePlug.transform.localPosition = new Vector3(0.17f, 0.016f, -0.06f);
                _bundlePlug.transform.localRotation = Quaternion.Euler(0f, 120f, 0f);
                _col.size = CoilSize; _col.center = new Vector3(0f, 0.03f, 0f);
                Body.mass = 1.4f;
                Thin();
            }
            else
            {
                _boxModel.transform.localPosition = Vector3.zero;
                _boxModel.transform.localRotation = Quaternion.identity;
                _col.size = BoxSize; _col.center = new Vector3(0f, 0.018f, -0.012f);
                Body.mass = 0.35f;
            }
            foreach (var s in _sockets) { if (coiled) Sockets.RemoveBox(s); else Sockets.AddBox(s); }
        }

        /// <summary>The coil as much as is left on it: thinner, and a little narrower, as the cord pays out.</summary>
        void Thin()
        {
            float left = coiled ? 1f : Mathf.Clamp01(1f - PaidOut / Length);
            if (_coilModel) _coilModel.transform.localScale = Vector3.Scale(_coilScale, new Vector3(Mathf.Lerp(0.82f, 1f, left), Mathf.Lerp(0.3f, 1f, left), Mathf.Lerp(0.82f, 1f, left)));
            if (!coiled) Body.mass = Mathf.Lerp(0.35f, 1.4f, left);
        }

        void LateUpdate()
        {
            // Paid out to the end: the coil is gone, the box is in the hand (or on the floor).
            if (!coiled && Bundle != _shapedBundle) Shape();
            else if (Wound) Thin();
        }

        public override void WakeCord() { if (_cord) _cord.Wake(); }
        public override float CordReach => Length;
        protected override Cord GetCord() => _cord;

        public override bool TakesSocket(Socket s)
        {
            if (s == null || s.occupant != null || s.dead || s.box == this) return false;
            if (coiled) return s.Depth + 1 <= MaxChain;
            return _plug && !_plug.Plugged && _plug.CanUse(s) && Vector3.Distance(s.Position, transform.position) < Length * 0.95f;
        }

        /// <summary>
        /// Out of its coil with the plug in a socket (from a save: plugged = null, the plug lies at plugPosition, paidOut
        /// metres of it are out). Just plugged in, only enough comes off the coil to reach from the outlet to the coil; the
        /// rest pays out as it is carried away (Cord.Feed).
        /// </summary>
        public void Deploy(Socket socket, Vector3? plugPosition = null, float paidOut = -1f)
        {
            if (!coiled) return;
            coiled = false;
            Vector3 at = socket != null ? socket.Position : plugPosition ?? transform.position + Vector3.up * 0.05f;
            _plug = Plug.Create("cordplug", this, at, socket != null ? socket.PlugRotation : Quaternion.identity, Orange);
            if (socket != null) _plug.PlugInto(socket);
            Vector3 exit = transform.TransformPoint(CoilExit);
            float first = paidOut > 0f ? paidOut : Vector3.Distance(at, exit) + 0.3f;
            _cord = Cord.Create("ExtensionCordRope", _plug, Plug.CordPoint, this, CoilExit, Mathf.Clamp(first, 0.5f, Length), 0.0045f,
                                ItemModels.Mat("CordOrange", Orange), Length);
            _cord.onTaut = c => { if (_plug && _plug.Plugged) _plug.Unplug(true); };
            Shape();
        }

        public override void ReelFromPlug(Plug plug, in HoldInput i)
        {
            if (coiled || plug != _plug) return;
            if (!ReelUpdate(_cord, _plug, i.reel, i.dt, i.hand)) return;
            var hand = i.hand;
            Coil();                       // lets go of the plug (it is part of the coil again)
            if (hand) hand.PickUp(this);
        }

        /// <summary>Back into its coil: its plug comes out, and so does anything plugged into it.</summary>
        public void Coil()
        {
            if (coiled) return;
            StopReel(_cord);
            foreach (var s in _sockets) if (s.occupant) s.occupant.Unplug(true);
            if (_plug) { _plug.Unplug(false); var hand = FindAnyObjectByType<PropGrabber>(); if (hand && hand.HeldItem == _plug) hand.LetGo(_plug); Destroy(_plug.gameObject); }
            if (_cord) Destroy(_cord.gameObject);
            _plug = null; _cord = null;
            coiled = true;
            Shape();
            _fx.PlayOneShot(SoundBank.CordWhip, 0.6f * SoundCatalog.Volume(SoundIds.Cords));
        }

        // ---------------- Plugging in from the coil in the hand ----------------
        Socket _going;
        float _goT, _goPose, _pushT = -1f, _stuckT, _bestGap;
        Vector3 _goPos; Quaternion _goRot = Quaternion.identity;
        const float CoilApproach = 0.05f, CoilPushTime = 0.15f;
        /// <summary>The coil in the hand is on its way to push its plug into a socket (for tests).</summary>
        public bool GoingInto => _going != null;
        public override bool Busy => GoingInto;

        void BeginCoilPlug(Socket s) { _going = s; _goT = 0f; _pushT = -1f; _stuckT = 0f; _bestGap = float.MaxValue; }

        void EndCoilPlug() { _going = null; _pushT = -1f; }

        /// <summary>
        /// One frame of the coil going to a socket in the hand: turned so the plug on its edge points into the socket, 5 cm
        /// out, then pushed in along the socket's axis. The hand's point is kept within a comfortable reach of the shoulder
        /// while the body bends and steps in (as a plug goes in: Plug.WithinComfort). In, the cord is let out of the coil
        /// (Deploy) and the hand comes back with the coil.
        /// </summary>
        void CoilGoingIn(in HoldInput i)
        {
            var s = _going;
            if (s == null || s.occupant != null || s.dead || s.Depth + 1 > MaxChain || !i.hand) { EndCoilPlug(); return; }
            if (i.hand.ItemWaitingForHand) return;
            _goT += i.dt;
            _goPose = Mathf.MoveTowards(_goPose, 1f, i.dt * 3.2f);
            Transform bp = _bundlePlug.transform;
            Quaternion rot = s.PlugRotation * Quaternion.Inverse(bp.localRotation);
            Vector3 outward = -(s.PlugRotation * Vector3.forward);
            float d = _pushT < 0f ? CoilApproach : CoilApproach * (1f - Mathf.SmoothStep(0f, 1f, _pushT / CoilPushTime));
            Vector3 pos = s.Position + outward * d - rot * bp.localPosition;
            Vector3 palmOff = rot * GripLocal;
            Vector3 shoulder = i.hand.ReachShoulder;
            Vector3 held = Plug.WithinComfort(pos, palmOff, shoulder, i.hand.ArmReach, Mathf.Lerp(Plug.ComfortReach, 1.1f, Mathf.Clamp01((_goT - 0.4f) * 2f)), out bool clamped);
            _goPos = held; _goRot = rot;
            i.hand.ReachToward(s.Position - rot * bp.localPosition + palmOff, _pushT < 0f, Plug.ComfortReach - 0.06f);
            if (_pushT < 0f)
            {
                float gap = clamped ? (held - pos).magnitude : 0f;
                // There: square to it, the palm where it should be. (Or after half a second, within 3.5 cm: the push takes
                // the rest, as a hand does.)
                bool there = _goPose >= 1f && !clamped && i.hand.PalmError < 0.02f;
                bool nearly = _goT > 0.5f && _goPose >= 1f && gap < 0.035f && i.hand.PalmError < 0.04f;
                if (there || nearly) { _pushT = 0f; return; }
                if (gap < _bestGap - 0.01f) { _bestGap = gap; _stuckT = 0f; }
                else if (_goPose >= 1f) _stuckT += i.dt;
                if (_stuckT > 1f || _goT > 4f) EndCoilPlug();
                return;
            }
            _pushT += i.dt;
            if (_pushT < CoilPushTime) return;
            EndCoilPlug();
            Deploy(s);
            MicThumpOf(i.hand);
        }

        static void MicThumpOf(PropGrabber hand) { var l = FindAnyObjectByType<AudioListener>(); var c = l ? l.GetComponent<Audio.CamcorderAudioChain>() : null; if (c) c.HandlingImpulse(); }

        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            if (_going == null) _goPose = Mathf.MoveTowards(_goPose, 0f, Time.deltaTime * 3.5f);
            position = _goPos; rotation = _goRot;
            return _goPose > 0f ? Mathf.SmoothStep(0f, 1f, _goPose) : 0f;
        }

        protected override void OnDropped() { EndCoilPlug(); _goPose = 0f; base.OnDropped(); }

        public override void HoldUpdate(in HoldInput i)
        {
            if (_going != null) { CoilGoingIn(i); return; }
            if (i.secondary) { Coil(); return; }
            if (!coiled && ReelUpdate(_cord, _plug, i.reel, i.dt, i.hand)) { StopReel(_cord); Coil(); return; }
            if (!i.pressed) return;
            var s = i.socket;
            if (s == null && i.hit && i.hitInfo.distance <= reach) s = Sockets.Nearest(i.hitInfo.point, 0.1f, this);
            if (s == null) return;
            if (coiled)
            {
                if (s.Depth + 1 > MaxChain) return;
                // Round 11 (handling plan, step 3): the coil stays in the hand. The hand takes the whole coil to the outlet,
                // the plug on its edge pinched against it, pushes the plug in and comes back with the coil, the cord paying
                // out as you walk off. (Round 4 to 10: the coil was set down beside the feet and the plug carried in alone,
                // 1.5 s, and the coil then had to be picked up again to go anywhere.)
                if (i.hand) { BeginCoilPlug(s); return; }
                Deploy(s);
            }
            else if (_plug && !_plug.Plugged && _plug.CanUse(s) && Vector3.Distance(s.Position, transform.position) < Length * 0.95f) _plug.PlugInto(s);
        }

        void OnDestroy()
        {
            foreach (var s in _sockets) { Sockets.RemoveBox(s); if (s.occupant) s.occupant.Unplug(true); }
            if (_plug) Destroy(_plug.gameObject);
            if (_cord) Destroy(_cord.gameObject);
        }
    }
}
