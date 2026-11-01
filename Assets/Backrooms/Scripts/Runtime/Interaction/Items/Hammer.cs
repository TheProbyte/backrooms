using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A claw hammer (16 oz, 33 cm, hickory handle), for nails (owner, 2026-10-30):
    ///  - With a box of <see cref="Nails"/> in a pocket, hold the right mouse button: the hammer comes up, and a see-through
    ///    nail and a ring show where the nail will go (on a wall, the floor, a pallet or any prop in reach; red where it
    ///    cannot: out of reach, or no nails).
    ///  - Tap the left button: the left hand leaves the camcorder with a nail and holds it to the spot, the hammer comes down
    ///    on it and drives it in flush, and the hand goes back to the camcorder. One nail from the box.
    ///  - What it does: through a prop into the building behind it (a pallet stood against a wall, a box on the floor) it
    ///    nails the prop there (stuck, saved so); into another prop it nails the two together; else it is a nail in the wall.
    ///  - Aimed at a nail already in: the claw pulls it out (back into the box); a prop's last nail out, it comes free.
    /// Otherwise it is carried head up at the lower right, dropped, thrown, set down and put in a pocket. Model:
    /// Resources/Items/Hammer.fbx (Tools/Blender/build_items2.py), origin at the handle's butt, the handle up +Y, the striking
    /// face toward +Z and the claw toward -Z.
    /// </summary>
    public class Hammer : WorldItem, IGripRound
    {
        /// <summary>The head's axis, up from the butt (m).</summary>
        public const float HeadY = 0.315f;
        /// <summary>Where the hand takes the handle, up from the butt (m), and the handle's half thickness there across the
        /// head's line (x) and along it (z).</summary>
        const float GripY = 0.09f, HalfX = 0.0111f, HalfZ = 0.0150f;
        /// <summary>The middle of the striking face (the head's front, +Z).</summary>
        static readonly Vector3 Face = new Vector3(0f, HeadY, 0.065f), Claw = new Vector3(0f, HeadY + 0.005f, -0.085f);

        [Tooltip("Furthest from the eye a nail can be put standing (m); lower down (the floor, a pallet on it) the body bends to it.")] public float reach = 1.1f;
        /// <summary>Probes: hold a nail's timeline at this moment (s; below 0: run as usual).</summary>
        [System.NonSerialized] public static float probeHoldT = -1f;

        public override Vector3 HoldOffset => new Vector3(0.2f, -0.33f, 0.4f);
        // Palm on the handle's side, the back of the hand outward (+x), the fingers round toward the striking face (+z):
        // as the sledgehammer is held (Sledgehammer.GripTurn).
        public override Vector3 GripLocal => new Vector3(HalfX * 0.95f, GripY, 0f);
        static readonly Quaternion GripTurn = Quaternion.LookRotation(Vector3.forward, Vector3.right) * Quaternion.AngleAxis(18f, Vector3.down);
        public override Quaternion GripRotationLocal => GripTurn;
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = Vector3.zero; axis = Vector3.up; }
        public override float GripCurl => 0.95f;
        /// <summary>Set down by hand: lying on one side of its head, then the other.</summary>
        public override Quaternion[] RestTurns => s_rests;
        static readonly Quaternion[] s_rests = { Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(0f, 0f, -90f) };
        public override bool Pocketable => true;
        // Raised to nail, a click is the nail (not "put the hammer away and take the prop": PropGrabber.TryGrabWithItem).
        public override bool UsesAim => _phase != Phase.Carry;
        public override bool Busy => _phase == Phase.Strike || _phase == Phase.Pull;
        public override float UseBlend => _poseW;

        public override string Status
        {
            get
            {
                if (_phase == Phase.Carry) return BoxOfNails(out var b) && b.count > 0 ? $"hammer ({b.count} nails: hold right mouse to aim)" : "hammer (a box of nails in a pocket to nail with)";
                if (_phase == Phase.Strike) return "hammer: nailing";
                if (_phase == Phase.Pull) return "hammer: pulling the nail";
                if (_pullIdx >= 0) return "hammer: left click pulls this nail out";
                if (!_aimHit) return "hammer: aim at a wall, the floor or a prop";
                if (!_aimNear) return "hammer: closer";
                if (!BoxOfNails(out var box) || box.count <= 0) return "hammer: no nails (a box of nails in a pocket)";
                return _aimWhat;
            }
        }

        /// <summary>The handle (an oval, narrowing up to the head) and the head (face to claw).</summary>
        public override float ContactDistance(Vector3 p)
        {
            // The oval as an ellipse scaled to a circle across the head's line.
            float t = Mathf.Clamp01(p.y / 0.3f);
            float rz = Mathf.Lerp(HalfZ, 0.0110f, t), rx = Mathf.Lerp(HalfX, 0.0085f, t);
            float handle = Mathf.Max(new Vector2(p.x * rz / rx, p.z).magnitude - rz, Mathf.Max(-p.y, p.y - HeadY));
            float head = BoxDistance(p, new Vector3(0f, HeadY, -0.011f), new Vector3(0.015f, 0.021f, 0.076f));
            return Mathf.Min(handle, head);
        }

        public static Hammer Create(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Hammer");
            go.transform.SetPositionAndRotation(position, rotation);
            ItemModels.Attach(go.transform, "Hammer", new Vector3(0.03f, 0.33f, 0.03f), "HammerWood");
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = 0.014f; cap.height = HeadY; cap.center = new Vector3(0f, HeadY * 0.5f, 0f);
            var head = go.AddComponent<BoxCollider>();
            head.center = new Vector3(0f, HeadY, -0.011f); head.size = new Vector3(0.03f, 0.042f, 0.152f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.6f; rb.linearDamping = 0.1f; rb.angularDamping = 0.5f;
            rb.centerOfMass = new Vector3(0f, 0.25f, 0f);                // the head is most of its weight
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var h = go.AddComponent<Hammer>();
            h.itemId = "hammer"; h.displayName = "Hammer";
            Generation.PoolLitSwitch.Attach(go);
            return h;
        }

        // ---------------- State ----------------

        enum Phase { Carry, Ready, Strike, Pull }
        Phase _phase = Phase.Carry;
        float _t, _poseW;
        PropGrabber _hand;
        FirstPersonBody _body;
        HandheldCameraShake _shake;
        // The aim (while raised) and the job (once clicked).
        bool _aimHit, _aimNear;
        Vector3 _aimPoint, _aimNormal;
        Collider _aimCollider;
        string _aimWhat = "";
        int _pullIdx = -1;
        Vector3 _jobPoint, _jobNormal;
        Collider _jobCollider;
        int _jobNail = -1;
        bool _driven;
        GameObject _heldNail, _ghost, _ring;
        Material _ghostBad;

        // Timeline of a nail (s): the left hand up with it, the hammer back, down onto it, then everything back.
        const float HandUp = 0.16f, Cocked = 0.22f, Hit = 0.32f, Done = 0.62f;
        // And of a pull: the claw onto the head, levered back (out at PullOut), back.
        const float ClawOn = 0.14f, PullOut = 0.32f, PullDone = 0.55f;

        protected override void OnDropped()
        {
            _phase = Phase.Carry; _poseW = 0f;
            if (_heldNail) Destroy(_heldNail);
            ShowAim(false);
        }

        protected override void OnDisable() { base.OnDisable(); if (_heldNail) Destroy(_heldNail); ShowAim(false); }
        void OnDestroy() { if (_ghost) Destroy(_ghost); if (_ring) Destroy(_ring); if (_ghostBad) Destroy(_ghostBad); }

        /// <summary>The box of nails in a pocket (with nails in it first), if there is one.</summary>
        bool BoxOfNails(out Nails box)
        {
            box = null;
            if (!_hand) return false;
            for (int i = 0; i < PropGrabber.PocketCount; i++)
                if (_hand.Pocket(i) is Nails n && (box == null || (box.count <= 0 && n.count > 0))) box = n;
            return box != null;
        }

        // ---------------- Controls ----------------

        public override void HoldUpdate(in HoldInput i)
        {
            _hand = i.hand;
            if (!_body && _hand) _body = _hand.GetComponent<FirstPersonBody>();
            if (!_shake) _shake = FindAnyObjectByType<HandheldCameraShake>();
            float dt = i.dt;
            _t += dt;
            if (probeHoldT >= 0f && (_phase == Phase.Strike || _phase == Phase.Pull) && _approachDone) _t = probeHoldT;
            if (_phase == Phase.Carry || _phase == Phase.Ready) Aim(i);
            switch (_phase)
            {
                case Phase.Carry:
                    if (i.aim) { _phase = Phase.Ready; _t = 0f; }
                    break;
                case Phase.Ready:
                    if (!i.aim) { _phase = Phase.Carry; _t = 0f; break; }
                    if (i.pressed && _t > 0.1f) Click();
                    break;
                case Phase.Strike:
                    Strike();
                    break;
                case Phase.Pull:
                    PullStep();
                    break;
            }
            _poseW = Mathf.MoveTowards(_poseW, _phase == Phase.Carry ? 0f : 1f, dt / 0.18f);
            ShowAim(_phase == Phase.Ready && _aimHit);
            // Bent down to a low nail: stays down until it is in (or out).
            if ((_phase == Phase.Strike || _phase == Phase.Pull) && _approachDone && _hand && _jobPoint.y < _hand.ReachShoulder.y - 0.3f)
                _hand.ReachToward(_jobPoint + _jobNormal * 0.2f, false, 0f, 0.45f);
            // The left hand: off the camcorder with the nail while it is held to the wall.
            if (_phase == Phase.Strike && _body)
            {
                float w = _t < HandUp ? _t / HandUp : _t < Hit + 0.06f ? 1f : Mathf.Max(0f, 1f - (_t - Hit - 0.06f) / 0.2f);
                LeftHandPose(_jobPoint, _jobNormal, out var palm, out var rot);
                _body.LeftReach(palm, rot, 0.62f, w);
            }
        }

        /// <summary>What the view is on, within reach: where a nail would go, or a nail to pull.</summary>
        void Aim(in HoldInput i)
        {
            _aimHit = false; _aimNear = false; _pullIdx = -1; _aimCollider = null;
            if (!i.hit || i.hitInfo.collider.isTrigger || i.hitInfo.collider.GetComponentInParent<WorldItem>() || i.hitInfo.collider.GetComponent<CharacterController>()) return;
            if (i.hitInfo.distance > 2.1f) return;
            _aimHit = true;
            _aimPoint = i.hitInfo.point; _aimNormal = i.hitInfo.normal; _aimCollider = i.hitInfo.collider;
            _aimNear = i.hitInfo.distance <= reach && (!_hand || (_aimPoint - _hand.ReachShoulder).magnitude < _hand.ArmReach + 0.3f);
            // Low (the floor, a pallet on it, the foot of a wall) or a step away: on the click the body steps in and bends
            // down to it first (Approach).
            if (!_aimNear && _hand)
            {
                Vector3 flat = Vector3.ProjectOnPlane(_aimPoint - _hand.ReachShoulder, Vector3.up);
                _aimNear = flat.magnitude < 1.5f && i.hitInfo.distance < 2.1f;
            }
            // A nail already there: the claw, not another nail.
            int n = NailWork.Near(_aimPoint, 0.03f, out var head, out var dir, out _);
            if (n >= 0 && _aimNear) { _pullIdx = n; _aimPoint = head; _aimNormal = dir; return; }
            var prop = _aimCollider.GetComponentInParent<MovableProp>();
            if (prop)
            {
                bool behind = Behind(_aimPoint, _aimNormal, prop, out _, out var other);
                _aimWhat = behind ? (other ? "hammer: left click nails these two together" : "hammer: left click nails it in place") : "hammer: left click drives a nail into it";
            }
            else _aimWhat = Mathf.Abs(_aimNormal.y) > 0.7f ? "hammer: left click drives a nail into the floor" : "hammer: left click drives a nail into the wall";
        }

        void Click()
        {
            if (!_aimHit || !_aimNear) { WorldSound.PlayAt(SoundBank.Tick(0), transform.position, 0.2f, 4f); return; }
            if (_pullIdx >= 0)
            {
                _phase = Phase.Pull; _t = 0f; _jobPoint = _aimPoint; _jobNormal = _aimNormal; _jobNail = _pullIdx; _driven = false;
                _approachDone = false; _approachT = 0f;
                return;
            }
            if (!BoxOfNails(out var box) || box.count <= 0) { WorldSound.PlayAt(SoundBank.Tick(0), transform.position, 0.2f, 4f); return; }
            box.count--;
            _phase = Phase.Strike; _t = 0f; _driven = false; _approachDone = false; _approachT = 0f;
            _jobPoint = _aimPoint; _jobNormal = _aimNormal; _jobCollider = _aimCollider;
            if (!_heldNail)
            {
                _heldNail = new GameObject("HeldNail");
                _heldNail.AddComponent<MeshFilter>().sharedMesh = NailWork.Mesh;
                var r = _heldNail.AddComponent<MeshRenderer>(); r.sharedMaterial = NailWork.Steel; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _heldNail.SetActive(true);
        }

        /// <summary>
        /// Before a nail goes in or comes out: the body steps in and bends down until the spot is in the arm's reach (the
        /// timeline waits at its start); given up after 2 s (the nail goes back in the box).
        /// </summary>
        bool Approach()
        {
            if (_approachDone || !_hand) return true;
            Vector3 at = _jobPoint + _jobNormal * 0.2f;
            if ((_jobPoint - _hand.ReachShoulder).magnitude < _hand.ArmReach + 0.3f) { _approachDone = true; _t = 0f; return true; }
            _hand.ReachToward(at, true, 1.2f, 0.45f);            // (stepping in to 0.75 of the arm, it walked onto the pallet)
            _approachT += Time.deltaTime;
            if (_approachT > 2.5f)
            {
                if (_phase == Phase.Strike && BoxOfNails(out var box) && box.count < Nails.Full) box.count++;
                if (_heldNail) _heldNail.SetActive(false);
                _phase = Phase.Ready; _t = 0.1f;
                WorldSound.PlayAt(SoundBank.Tick(0), transform.position, 0.2f, 4f);
            }
            _t = 0f;
            return false;
        }

        bool _approachDone;
        float _approachT;

        /// <summary>A nail going in: carried up by the left hand, held to the spot, struck flush (see the timeline).</summary>
        void Strike()
        {
            if (!Approach()) return;
            if (_heldNail)
            {
                // Up from the camcorder hand to the spot, then standing out of the surface, then driven home.
                var cam = Camera.main ? Camera.main.transform : transform;
                Vector3 from = cam.position + cam.rotation * new Vector3(-0.08f, -0.14f, 0.3f);
                Vector3 standing = _jobPoint + _jobNormal * (NailWork.Length - 0.004f), flush = _jobPoint + _jobNormal * 0.0015f;
                float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / HandUp));
                Vector3 head = _t < Hit ? Vector3.Lerp(from, standing, up) : Vector3.Lerp(standing, flush, Mathf.Clamp01((_t - Hit) / 0.03f));
                Vector3 along = Vector3.Slerp(-cam.forward, _jobNormal, up);
                if (along.sqrMagnitude < 1e-4f) along = _jobNormal;
                _heldNail.transform.SetPositionAndRotation(head, Quaternion.FromToRotation(Vector3.up, along.normalized));
            }
            if (!_driven && _t >= Hit) Drive();
            if (_t >= Done) { _phase = Phase.Ready; _t = 0.1f; }
        }

        void Drive()
        {
            _driven = true;
            Vector3 head = _jobPoint + _jobNormal * 0.0015f;
            var prop = _jobCollider ? _jobCollider.GetComponentInParent<MovableProp>() : null;
            if (prop)
            {
                if (Behind(_jobPoint, _jobNormal, prop, out _, out var other))
                {
                    if (other) Join(prop, other); else prop.NailDown();
                }
            }
            NailWork.Add(head, _jobNormal, prop);
            if (_heldNail) _heldNail.SetActive(false);
            WorldSound.PlayAt(NailWork.Drive(Random.Range(0, 3)), _jobPoint, 0.9f * SoundCatalog.Volume(SoundIds.Sledgehammer), 24f, Random.Range(0.95f, 1.06f));
            if (_shake) _shake.Jolt(new Vector3(2.5f, (Random.value - 0.5f) * 2f, -1.2f));
        }

        /// <summary>The nail's way on through a prop: the building or another prop behind it, within 25 cm (through the board
        /// into what it stands against).</summary>
        static readonly RaycastHit[] s_hits = new RaycastHit[12];
        bool Behind(Vector3 point, Vector3 normal, MovableProp prop, out Collider what, out MovableProp other)
        {
            what = null; other = null;
            int n = Physics.RaycastNonAlloc(point + normal * 0.005f, -normal, s_hits, 0.25f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                var c = s_hits[k].collider;
                if (!c || c.transform.IsChildOf(prop.transform) || c.GetComponent<CharacterController>() || c.GetComponentInParent<WorldItem>() || (_hand && c.transform.IsChildOf(_hand.transform))) continue;
                if (s_hits[k].distance >= best) continue;
                best = s_hits[k].distance; what = c; other = c.GetComponentInParent<MovableProp>();
            }
            return what;
        }

        /// <summary>Two props nailed together: they move as one (for this session: the joint is not saved).</summary>
        static void Join(MovableProp a, MovableProp b)
        {
            if (a.Pinned || b.Pinned) { if (!a.Pinned) a.NailDown(); if (!b.Pinned) b.NailDown(); return; }
            foreach (var j in a.GetComponents<FixedJoint>()) if (j.connectedBody == b.Body) return;
            var fj = a.gameObject.AddComponent<FixedJoint>();
            fj.connectedBody = b.Body;
            fj.breakForce = 6000f; fj.breakTorque = 6000f;
        }

        /// <summary>A nail coming out: the claw onto its head, levered back, and the nail back in the box.</summary>
        void PullStep()
        {
            if (!Approach()) return;
            if (!_driven && _t >= PullOut)
            {
                _driven = true;
                NailWork.Remove(_jobNail);
                if (BoxOfNails(out var box) && box.count < Nails.Full) box.count++;
                WorldSound.PlayAt(NailWork.Pull, _jobPoint, 0.7f * SoundCatalog.Volume(SoundIds.Sledgehammer), 14f, Random.Range(0.95f, 1.08f));
                if (_shake) _shake.Jolt(new Vector3(-1.2f, 0.6f, 0.4f));
            }
            if (_t >= PullDone) { _phase = Phase.Ready; _t = 0.1f; }
        }

        // ---------------- The aim's marks ----------------

        void ShowAim(bool on)
        {
            if (!on) { if (_ghost) _ghost.SetActive(false); if (_ring) _ring.SetActive(false); return; }
            if (!_ghost)
            {
                _ghost = new GameObject("NailGhost");
                _ghost.AddComponent<MeshFilter>().sharedMesh = NailWork.Mesh;
                var gr = _ghost.AddComponent<MeshRenderer>(); gr.sharedMaterial = NailWork.Ghost; gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ring = new GameObject("NailRing");
                _ring.AddComponent<MeshFilter>().sharedMesh = NailWork.RingMesh;
                var rr = _ring.AddComponent<MeshRenderer>(); rr.sharedMaterial = NailWork.RingMaterial; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ghostBad = new Material(NailWork.Ghost) { color = new Color(1f, 0.3f, 0.25f, 0.6f), hideFlags = HideFlags.DontSave };
            }
            bool ok = _aimNear && (_pullIdx >= 0 || (BoxOfNails(out var box) && box.count > 0));
            var turn = Quaternion.FromToRotation(Vector3.up, _aimNormal);
            _ring.SetActive(true);
            _ring.transform.SetPositionAndRotation(_aimPoint + _aimNormal * 0.0015f, turn);
            _ring.transform.localScale = Vector3.one * (_pullIdx >= 0 ? 1.4f : 1f);
            _ring.GetComponent<MeshRenderer>().sharedMaterial = ok ? NailWork.RingMaterial : _ghostBad;
            // The nail where it will stand before it is struck (none over a nail to pull).
            _ghost.SetActive(_pullIdx < 0);
            _ghost.transform.SetPositionAndRotation(_aimPoint + _aimNormal * (NailWork.Length - 0.004f), turn);
            _ghost.GetComponent<MeshRenderer>().sharedMaterial = ok ? NailWork.Ghost : _ghostBad;
        }

        // ---------------- Poses ----------------

        /// <summary>The left hand holding a nail to a point: the palm beside it, the fingers along the surface to it, the back
        /// of the hand out of it (thumb and finger pinch the nail clear of the hammer's face).</summary>
        void LeftHandPose(Vector3 point, Vector3 normal, out Vector3 palm, out Quaternion rot)
        {
            var cam = Camera.main ? Camera.main.transform : transform;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, normal);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(cam.up, normal);
            right.Normalize();
            Vector3 down = Vector3.Cross(normal, right).normalized;
            if (Vector3.Dot(down, Vector3.down) < 0f && Mathf.Abs(normal.y) < 0.7f) down = -down;
            Vector3 fingers = (right * 0.9f + down * 0.25f).normalized;
            palm = point + normal * 0.035f - fingers * 0.058f;
            rot = Quaternion.LookRotation(fingers, normal);
        }

        /// <summary>The hammer struck onto a nail standing out of a point: its face on the head, the handle across the blow
        /// (the head up from the hand on a wall, away from you on the floor).</summary>
        Quaternion StrikeTurn(Vector3 normal, Transform cam)
        {
            Vector3 up = Vector3.ProjectOnPlane(cam.up + cam.forward * 0.6f, normal);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.up, normal);
            return Quaternion.LookRotation(-normal, up.normalized);
        }

        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            position = transform.position; rotation = transform.rotation;
            if (!Held || _poseW <= 0f) return 0f;
            var cam = Camera.main ? Camera.main.transform : transform;
            // Raised: held up at the right, the head back over the hand, the face toward what you look at.
            Vector3 readyGrip = cam.position + cam.rotation * new Vector3(0.24f, -0.12f, 0.36f);
            Quaternion readyRot = Quaternion.LookRotation(cam.rotation * new Vector3(-0.1f, 0.55f, 0.85f), cam.rotation * new Vector3(0.1f, 0.8f, -0.55f));
            Vector3 gripPos = readyGrip; Quaternion rot = readyRot;
            if (_phase == Phase.Strike || _phase == Phase.Pull)
            {
                bool pull = _phase == Phase.Pull;
                // On the nail: the face on its head (or the claw under it).
                Quaternion hitRot = StrikeTurn(_jobNormal, cam);
                if (pull) hitRot = hitRot * Quaternion.AngleAxis(180f, Vector3.up);         // turned round: the claw toward it
                Vector3 headAt = _jobPoint + _jobNormal * (pull ? 0.004f : _driven ? 0.0015f : NailWork.Length - 0.004f);
                Vector3 hitGrip = headAt - hitRot * ((pull ? Claw : Face) - GripLocal);
                Vector3 axis = Vector3.Cross(hitRot * Vector3.up, -_jobNormal).normalized;
                if (!pull)
                {
                    // Back over the hand (55 degrees), then down onto the nail, speeding into it, a little spring back off it.
                    Quaternion cockRot = Quaternion.AngleAxis(-55f, axis) * hitRot;
                    Vector3 cockGrip = hitGrip - (cam.forward * 0.05f);
                    if (_t < Cocked) { float k = Mathf.SmoothStep(0f, 1f, _t / Cocked); gripPos = Vector3.Lerp(readyGrip, cockGrip, k); rot = Quaternion.Slerp(readyRot, cockRot, k); }
                    else if (_t < Hit) { float k = (_t - Cocked) / (Hit - Cocked); k *= k; gripPos = Vector3.Lerp(cockGrip, hitGrip, k); rot = Quaternion.Slerp(cockRot, hitRot, k); }
                    else
                    {
                        float k = Mathf.Clamp01((_t - Hit) / (Done - Hit));
                        float spring = Mathf.Sin(Mathf.Clamp01(k * 3f) * Mathf.PI) * 9f;
                        Quaternion off = Quaternion.AngleAxis(-spring, axis) * hitRot;
                        float back = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.25f) / 0.75f));
                        gripPos = Vector3.Lerp(hitGrip, readyGrip, back); rot = Quaternion.Slerp(off, readyRot, back);
                    }
                }
                else
                {
                    // The claw onto the head, then levered back about it (the nail comes out), then back up.
                    Quaternion leverRot = Quaternion.AngleAxis(35f, axis) * hitRot;
                    Vector3 leverGrip = headAt + leverRot * (GripLocal - Claw) + _jobNormal * 0.03f;
                    if (_t < ClawOn) { float k = Mathf.SmoothStep(0f, 1f, _t / ClawOn); gripPos = Vector3.Lerp(readyGrip, hitGrip, k); rot = Quaternion.Slerp(readyRot, hitRot, k); }
                    else if (_t < PullOut) { float k = Mathf.SmoothStep(0f, 1f, (_t - ClawOn) / (PullOut - ClawOn)); gripPos = Vector3.Lerp(hitGrip, leverGrip, k); rot = Quaternion.Slerp(hitRot, leverRot, k); }
                    else { float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_t - PullOut) / (PullDone - PullOut))); gripPos = Vector3.Lerp(leverGrip, readyGrip, k); rot = Quaternion.Slerp(leverRot, readyRot, k); }
                }
            }
            // The hand is where an arm can be.
            if (_hand)
            {
                Vector3 s = _hand.ReachShoulder;
                gripPos = s + Vector3.ClampMagnitude(gripPos - s, _hand.ArmReach * 0.98f);
            }
            rotation = rot;
            position = gripPos - rot * GripLocal;
            return Mathf.SmoothStep(0f, 1f, _poseW);                 // up from the carry and back down, eased
        }
    }
}
