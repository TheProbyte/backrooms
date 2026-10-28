using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Picking items up by hand (round 8, 2026-10-28), as the hand plugs a cord in: the hand goes out to the item where it
    /// lies, the body bending and stepping in for it; the item is not touched until the palm is on its grip. Then the
    /// fingers close on it and it comes up with the hand. An item the body cannot get to with a lean or a short step is not
    /// offered (no focus marks, a click does nothing); one the hand cannot get to after all (held off by a desk's edge, a
    /// wall, a shelf) is left where it is and the hand comes back. (It flew to the hand: from up to 2.1 m away at once, or
    /// after waiting 0.9 s for a hand that had not got there.)
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>How far the body steps in for an item, at most (m): a short step.</summary>
        public const float PickStep = 0.55f;
        /// <summary>An item is offered within this much of the arm's reach of the shoulder (after the step and the bend).</summary>
        public const float PickReachShare = 1.0f;
        /// <summary>The hand's target is kept this far out (of the arm's reach) while the body still comes closer: bent.</summary>
        const float PickBent = 0.88f;
        /// <summary>Held off (the body can come no closer), the arm may go out to this much of its reach.</summary>
        const float PickStretch = 1.06f;
        /// <summary>The palm is on the grip within this (m); within PalmNearGrip it is taken once it gets no nearer.</summary>
        public const float PalmOnGrip = 0.012f, PalmNearGrip = 0.015f;
        /// <summary>The fingers' curl going out to an item (open).</summary>
        const float PickOpenCurl = 0.2f;
        /// <summary>The shoulder comes down this share of what the eye does when the body stoops; never below LowestShoulder.</summary>
        const float ShoulderFall = 0.875f, LowestShoulder = 0.65f;

        WorldItem _fetch;                // the item the hand is going for: untouched until the palm is on it
        /// <summary>The hand is on its way to take this item (it lies untouched until the palm is on it), or null.</summary>
        public WorldItem Fetching => _fetch;
        /// <summary>The last pick-up was given up: the hand could not get there (for tests).</summary>
        public bool PickGaveUp { get; private set; }
        /// <summary>The held item is in the hand's grasp (the palm on it, the fingers closing), or on its way up.</summary>
        public bool ItemInGrasp => HeldItem && (_graspOn || _itemT > 0f);
        /// <summary>0..1: how fully the held item is put onto the solved palm (and out of its skin): eased in as it starts up
        /// with the hand (all at once, the first frame moved a can 9 mm), 1 once it is up.</summary>
        public float ItemOnPalm => !HeldItem || _itemT <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_itemT / 0.15f));

        bool _graspOn;                   // the palm is on the item it took where it lay: the fingers close, then it comes up
        float _graspT, _liftTime = 0.3f;
        // The reach for a grip: how long, the nearest the palm has come, how long without getting nearer, the arm let out
        // further (held off), the arm's way blocked, near enough but no nearer; the hand's target this frame.
        float _pickT, _pickBest, _pickStuckT, _pickSlowT, _pickStretch, _pickBlockedT, _pickNearT, _pickHeldOffT, _pickRoom, _pickAbove = 0.22f;
        Vector3 _pickHandAt, _pickStart;
        bool _pickClamped, _pickNoStep, _pickStepping;
        // The hand turned about the palm's normal (fingers down toward a low grip) as the arm reaches it best, and that turn
        // taken back off over the lift.
        float _pickTiltNext;
        Quaternion _pickTurn = Quaternion.identity, _pickTurnWant = Quaternion.identity;   // in the hand's own frame
        static readonly float[] PickTilts = { 0f, 25f, -25f, 50f, -50f, 75f, -75f };
        static readonly float[] PickRolls = { 0f, 25f, -25f };
        static readonly float[] PickPitches = { 0f, 30f, -30f };

        void StopPickStep() { if (_pickStepping) { _player.botInput = null; _pickStepping = false; } }

        /// <summary>
        /// How far the torso leans out (degrees, on top of the stoop) reaching for an item beyond the arm with no step left:
        /// over a desk, into a shelf. The hips and the view stay; the shoulder goes forward and down (FirstPersonBody).
        /// </summary>
        public float ReachLean { get; private set; }
        float _leanWant;
        /// <summary>The most the torso leans out for an item (degrees), and about how far that brings the shoulder forward
        /// and down standing (m, measured on the rig).</summary>
        public const float MaxLean = 26f, LeanAhead = 0.07f, LeanDown = 0.04f;

        /// <summary>The lean out eased each frame: while the hand goes for an item, back off as it comes up, none otherwise.</summary>
        void UpdateReachLean()
        {
            bool going = _fetch || (HeldItem && _itemT < 1f);
            if (!going) _leanWant = 0f;
            float want = HeldItem && _itemT > 0f ? _leanWant * (1f - Mathf.SmoothStep(0f, 1f, _itemT)) : _leanWant;
            ReachLean = Mathf.MoveTowards(ReachLean, want, Time.deltaTime * 40f);
        }

        /// <summary>
        /// The hand's turn reaching for an item: as it will hold it, turned about the palm's normal (the fingers down along a
        /// can, a lamp's rod) and rolled a little about the fingers, as far as the arm needs (FirstPersonBody.ArmStrain). A
        /// can on the floor held square, fingers level, wants the wrist 10 cm off the floor: the arm cannot get it there.
        /// </summary>
        Quaternion ReachTurn(Quaternion square, Vector3 grip)
        {
            if (_body && _body.HasArm && Time.time >= _pickTiltNext)
            {
                _pickTiltNext = Time.time + 0.15f;
                float best = float.MaxValue;
                foreach (float a in PickTilts)
                    foreach (float r in PickRolls)
                        foreach (float pt in PickPitches)
                        {
                            // (Pitched: the fingers down over it, as a hand takes a stick of chalk off the floor from above.)
                            Quaternion rel = Quaternion.AngleAxis(a, Vector3.up) * Quaternion.AngleAxis(r, Vector3.forward) * Quaternion.AngleAxis(pt, Vector3.right);
                            float s = _body.ArmStrain(grip, square * rel, out float over) + over * 0.02f
                                      + (Mathf.Abs(a) + (Mathf.Abs(r) + Mathf.Abs(pt)) * 1.5f) * 0.004f + Quaternion.Angle(rel, _pickTurn) * 0.001f;
                            if (s < best) { best = s; _pickTurnWant = rel; }
                        }
            }
            _pickTurn = Quaternion.Slerp(_pickTurn, _pickTurnWant, 1f - Mathf.Exp(-Time.deltaTime / 0.12f));
            return square * _pickTurn;
        }

        /// <summary>The reaching turn (ReachTurn) on a hand turned square, a share of it (taken back off as the item comes up).</summary>
        Quaternion WithReachTurn(Quaternion square, float share) => square * Quaternion.Slerp(Quaternion.identity, _pickTurn, share);
        static readonly RaycastHit[] s_pickHits = new RaycastHit[16];

        /// <summary>Where the hand takes an item, as it lies now (its grip: the palm's point).</summary>
        Vector3 GripOf(WorldItem item) => item.transform.TransformPoint(Mirror(item.GripLocal));

        /// <summary>The hand's turn taking an item as it lies now.</summary>
        Quaternion GripTurnOf(WorldItem item)
        {
            Quaternion g = item.GripRotationLocal;
            return item.transform.rotation * Quaternion.LookRotation(Mirror(g * Vector3.forward), Mirror(g * Vector3.up));
        }

        // ---------------- What the body can reach ----------------

        /// <summary>
        /// Can the body get its palm to this item with a lean or a short step, the arm's way there clear? (What the focus
        /// marks offer and a click takes.) Measured from the body's real shoulder: as high as it stands, as low as the body
        /// stoops (the shoulder no lower than 0.65 m standing), and stepped in as far as there is room, at most PickStep.
        /// </summary>
        public bool BodyReaches(WorldItem item)
        {
            if (!item) return false;
            if (!_body || !_body.HasArm) return (GripOf(item) - Eye).magnitude <= itemRange;
            Vector3 p = GripOf(item);
            ShoulderSpan(out Vector3 top, out float fall);
            Vector3 flat = p - top; flat.y = 0f;
            float room = flat.sqrMagnitude > 1e-4f ? StepRoom(flat.normalized, item) : 0f;
            // The torso leaning out adds a little more along the way the body faces, wherever the feet are (against a desk).
            float ahead = flat.sqrMagnitude > 1e-4f ? Mathf.Max(0f, Vector3.Dot(flat.normalized, Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized)) : 0f;
            return ReachableWith(p, top, fall + LeanDown, ArmReach * PickReachShare, room + LeanAhead * ahead, (a, b) => ArmWayClear(a, b, item));
        }

        /// <summary>
        /// Pure (for tests): some shoulder between top and fall below it, moved toward p by up to step, has p within reach,
        /// the way from it clear (clear: null for no check).
        /// </summary>
        public static bool ReachableWith(Vector3 p, Vector3 shoulderTop, float fall, float reach, float step, System.Func<Vector3, Vector3, bool> clear)
        {
            Vector3 flat = p - shoulderTop; flat.y = 0f;
            float h = flat.magnitude;
            Vector3 dir = h > 1e-4f ? flat / h : Vector3.zero;
            for (int st = 0; st <= 2; st++)
            {
                float move = Mathf.Min(step * st * 0.5f, Mathf.Max(0f, h - 0.1f));
                for (int k = 0; k <= 4; k++)
                {
                    Vector3 s = shoulderTop + dir * move + Vector3.down * (fall * k * 0.25f);
                    if ((p - s).sqrMagnitude > reach * reach) continue;
                    if (clear == null || clear(s, p)) return true;
                }
            }
            return false;
        }

        /// <summary>The shoulder as the body stands (the stoop taken back off) and how far it can come down stooping.</summary>
        void ShoulderSpan(out Vector3 top, out float fall)
        {
            float feet = transform.position.y, standEye = feet + _player.BaseEye;
            top = ReachShoulder;
            top.y += Mathf.Max(0f, standEye - Eye.y) * ShoulderFall;
            float canStoop = Mathf.Clamp(_player.BaseEye - 0.6f, 0f, 0.85f) * ShoulderFall;
            fall = Mathf.Clamp(Mathf.Min(canStoop, top.y - (feet + LowestShoulder)), 0f, 1f);
        }

        /// <summary>How far the body can step this way (m, at most PickStep): its capsule swept, the feet's height left out.</summary>
        float StepRoom(Vector3 dir, WorldItem item)
        {
            var cc = GetComponent<CharacterController>();
            if (!cc) return PickStep;
            float r = cc.radius * 0.95f;
            Vector3 c = transform.position + cc.center;
            float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half + Vector3.up * 0.2f;
            if (b.y > a.y) b = a;
            int n = Physics.CapsuleCastNonAlloc(a, b, r, dir, s_pickHits, PickStep + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            float room = PickStep;
            for (int i = 0; i < n; i++)
            {
                var col = s_pickHits[i].collider;
                if (!col || col.transform.IsChildOf(transform) || col.GetComponentInParent<WorldItem>()) continue;
                room = Mathf.Min(room, Mathf.Max(0f, s_pickHits[i].distance - 0.03f));
            }
            return room;
        }

        /// <summary>Nothing solid on the line from the shoulder to the grip (the arm would go through a shelf or a desk).</summary>
        bool ArmWayClear(Vector3 s, Vector3 p, WorldItem item)
        {
            // Not from a shoulder inside something (a step and a lean into a table's box: a ray from inside it sees nothing).
            int m = Physics.OverlapSphereNonAlloc(s, 0.05f, s_pickOver, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
            {
                var c = s_pickOver[i];
                if (c && !c.transform.IsChildOf(transform) && !c.GetComponentInParent<WorldItem>()) return false;
            }
            Vector3 d = p - s;
            float len = d.magnitude - 0.04f;
            if (len <= 0.01f) return true;
            int n = Physics.RaycastNonAlloc(s, d / d.magnitude, s_pickHits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_pickHits[i].collider;
                if (!col || col.transform.IsChildOf(transform) || col.GetComponentInParent<WorldItem>()) continue;
                // (The edge of what it stands on, just short of it: a hand comes over it. A roll of tape on a shelf above the
                // shoulder was refused for the shelf's front edge.)
                if ((s_pickHits[i].point - p).magnitude < 0.1f) continue;
                return false;
            }
            return true;
        }
        static readonly Collider[] s_pickOver = new Collider[8];

        // ---------------- Going for it ----------------

        void BeginReachFor(WorldItem item)
        {
            _pickT = 0f; _pickBest = float.MaxValue; _pickStuckT = _pickSlowT = _pickStretch = _pickBlockedT = _pickNearT = _pickHeldOffT = 0f;
            _pickStart = transform.position; _pickNoStep = false;
            StopPickStep();
            _pickTurn = _pickTurnWant = Quaternion.identity; _pickTiltNext = 0f;
            _leanWant = 0f;
            Vector3 flat = GripOf(item) - ReachShoulder; flat.y = 0f;
            _pickRoom = flat.sqrMagnitude > 1e-4f ? StepRoom(flat.normalized, item) : 0f;
            PickGaveUp = false;
        }

        /// <summary>
        /// Pure (for tests): the palm is on the grip and the item may be taken. Only with the arm all the way out (solved) to
        /// the grip itself (not a point short of it, clamped), the palm within PalmOnGrip of it, or within PalmNearGrip and
        /// no nearer for 0.15 s (nearT counts that).
        /// </summary>
        public static bool PalmTakes(float palmError, bool clamped, bool solved, ref float nearT, float dt)
        {
            if (!solved || clamped) { nearT = 0f; return false; }
            if (palmError <= PalmOnGrip) return true;
            if (palmError <= PalmNearGrip) { nearT += dt; return nearT > 0.15f; }
            nearT = 0f;
            return false;
        }

        /// <summary>
        /// One frame of the hand going for a grip where an item lies: the hand's target (_pickHandAt) is kept within a bent
        /// arm's reach of the shoulder as the body bends and steps in (Plug.WithinComfort), then on the grip. Held off (the
        /// body as low as it goes, against a desk), the arm goes on out and the body leans lower. 1: the palm is on it; -1:
        /// give up (no nearer for a second, the arm's way blocked, or 5 s); 0: on the way.
        /// </summary>
        int ReachFor(Vector3 grip, WorldItem item, float dt)
        {
            _pickT += dt;
            Vector3 S = ReachShoulder;
            float reach = ArmReach;
            float within = Mathf.Lerp(PickBent, PickStretch, _pickStretch);
            _pickHandAt = Plug.WithinComfort(grip, Vector3.zero, S, reach, within, out _pickClamped);
            if (!_body || !_body.HasArm) return 1;

            // The body: down to it (the shoulder about as high over it as a bent arm reaches out to it from where the body
            // will stand) and a step in while there is room, only until it is that near: stepping on until the shoulder
            // was over a can on the floor, the hand had to come straight down beside the leg and could not (round 8).
            Vector3 flat = grip - S; flat.y = 0f;
            Vector3 moved = transform.position - _pickStart; moved.y = 0f;
            float dhLeft = Mathf.Max(0.15f, flat.magnitude - Mathf.Max(0f, _pickRoom - moved.magnitude));
            float rb = reach * PickBent;
            float above = dhLeft < rb ? Mathf.Sqrt(rb * rb - dhLeft * dhLeft) * 0.9f : 0.05f;
            _pickAbove = Mathf.Clamp(above, 0.05f, 0.5f);
            ReachToward(grip, false, 0f, _pickAbove);
            float shoulderThen = Mathf.Max(transform.position.y + LowestShoulder, Mathf.Min(S.y, grip.y + _pickAbove));
            float dyThen = Mathf.Max(0f, shoulderThen - grip.y), rs = reach * 0.93f;
            float stopAt = Mathf.Max(0.18f, dyThen < rs ? Mathf.Sqrt(rs * rs - dyThen * dyThen) : 0f);
            _pickHeldOffT = _pickStepping && _player.PlanarVelocity.magnitude < 0.1f ? _pickHeldOffT + dt : 0f;
            if (_pickHeldOffT > 0.2f || moved.magnitude > PickStep + 0.2f) _pickNoStep = true;
            if (!_pickNoStep && flat.magnitude > stopAt + 0.02f)
            {
                Vector3 local = transform.InverseTransformDirection(flat);
                _player.botInput = new Vector2(local.x, local.z).normalized * 0.45f;
                _pickStepping = true;
            }
            else StopPickStep();
            // Still beyond the arm with the step done (against a desk or a shelf), or no nearer: the torso leans out to it.
            // The arm goes on out at the same time (one after the other took 1.2 s).
            float shortfall = flat.magnitude - stopAt;
            bool leanOut = !_pickStepping && _pickT > 0.2f && (shortfall > 0.03f || _pickSlowT > 0.15f);
            if (leanOut) { _leanWant = Mathf.MoveTowards(_leanWant, MaxLean, dt * 60f); _pickStretch = Mathf.MoveTowards(_pickStretch, 1f, dt * 2.5f); }
            else if (shortfall < -0.06f) _leanWant = Mathf.MoveTowards(_leanWant, 0f, dt * 30f);

            float palmToGrip = (_body.PalmNow - grip).magnitude;
            bool solved = HandBlend >= 0.97f && _switchT >= 1f;
            if (PalmTakes(PalmError, _pickClamped, solved, ref _pickNearT, dt)) return 1;

            // No nearer: the arm goes on out and the body leans lower (as a plug's carry does: Plug.GoingIn).
            if (palmToGrip < _pickBest - 0.004f) { _pickBest = palmToGrip; _pickStuckT = 0f; _pickSlowT = 0f; }
            else if (HandBlend > 0.9f) { _pickStuckT += dt; _pickSlowT += dt; }
            // (Once going on out it keeps on: each bit of headway it made reset the wait, and it crept out 4 mm at a time.)
            if (_pickSlowT > 0.15f || _pickStretch > 0f)
            {
                _pickStretch = Mathf.MoveTowards(_pickStretch, 1f, dt * 2.5f);
                if (grip.y < S.y - 0.15f) _stoopExtra = Mathf.MoveTowards(_stoopExtra, 0.3f, dt * 1.5f);
            }
            _pickBlockedT = ArmWayClear(S, grip, item) ? 0f : _pickBlockedT + dt;
            if (_pickStuckT > 1f || _pickT > 5f || (_pickBlockedT > 0.8f && _pickStuckT > 0.4f)) { PickGaveUp = true; return -1; }
            return 0;
        }

        /// <summary>The hand goes out to take an item where it lies (not yet touched).</summary>
        void StartFetch(WorldItem item)
        {
            _fetch = item;
            BeginReachFor(item);
            if (HandBlend > 0.5f) { _switchPos = HandPosition; _switchRot = HandRotation; _switchT = 0f; } else _switchT = 1f;
            _pickHandAt = HandBlend > 0.5f ? HandPosition : GripOf(item);
        }

        void EndFetch()
        {
            _fetch = null;
            StopPickStep();
            if (_itemStepping) { _player.botInput = null; _itemStepping = false; }
        }

        void UpdateFetch()
        {
            var item = _fetch;
            if (!item || item.Held || !item.gameObject.activeInHierarchy) { EndFetch(); return; }
            int r = ReachFor(GripOf(item), item, Time.deltaTime);
            if (r < 0) { EndFetch(); _itemReachUntil = -1f; return; }
            if (r > 0) { EndFetch(); Take(item, true); }
        }

        /// <summary>The hand going out to the item (UpdateHandPose): open, turned as it will take it.</summary>
        void FetchHandPose()
        {
            HandPosition = _pickHandAt;
            HandRotation = ReachTurn(GripTurnOf(_fetch), GripOf(_fetch));
            if (_switchT < 1f)
            {
                _switchT = Mathf.Min(1f, _switchT + Time.deltaTime / 0.3f);
                float e = Mathf.SmoothStep(0f, 1f, _switchT);
                HandPosition = Vector3.Lerp(_switchPos, HandPosition, e);
                HandRotation = Quaternion.Slerp(_switchRot, HandRotation, e);
            }
            HandCurl = Mathf.MoveTowards(HandCurl, PickOpenCurl, Time.deltaTime * 4f);
        }

        /// <summary>
        /// Is the item already where the hand holds things (put there: a plug let out of a lamp in the hand, an item loaded
        /// in the hand)? Then it is taken at once and the hand closes on it there.
        /// </summary>
        bool InHandsPlace(WorldItem item, out bool palmOn)
        {
            palmOn = false;
            if (!_body || !_body.HasArm) { palmOn = true; return true; }
            float off = (GripOf(item) - HandPosition).magnitude;
            // (Put right where the palm is, turned as the hand holds it: PlaceInHand.)
            palmOn = HandBlend > 0.9f && off < 0.015f && PalmError < 0.02f && Quaternion.Angle(GripTurnOf(item), HandRotation) < 10f;
            if (palmOn || (HandBlend > 0.5f && off < 0.06f)) return true;
            Transform pivot = _player.cameraPitchPivot ? _player.cameraPitchPivot : (_cam ? _cam.transform : transform);
            Vector3 carry = pivot.position + pivot.rotation * (_player.CamcorderHold * 0.5f + Mirror(item.HoldOffset));
            return item.Body && item.Body.isKinematic && (item.transform.position - carry).magnitude < 0.3f;
        }

        /// <summary>Lets go of an item the hand never got to, where it lay (it was taken into the hand to wait there).</summary>
        void LeaveWhereItLies(WorldItem item)
        {
            HeldItem = null;
            AimedSocket = null;
            _graspOn = false;
            item.SetHeld(false);
            if (item.Body)
            {
                item.Body.position = _itemFromPos; item.Body.rotation = _itemFromRot;
                item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero;
            }
            item.transform.SetPositionAndRotation(_itemFromPos, _itemFromRot);
            PickGaveUp = true;
            StopPickStep();
            if (_itemStepping) { _player.botInput = null; _itemStepping = false; }
            _itemReachUntil = -1f;
        }

        /// <summary>
        /// For tests and tools: the item put straight into the hand as it holds it (no reach), as a pick-up ends.
        /// </summary>
        public void PutInHand(WorldItem item)
        {
            if (!item) return;
            if (_fetch) EndFetch();
            if (HeldItem && HeldItem != item) Drop();
            if (Current != State.Idle) Release("put an item in the hand");
            Take(item, true);
            _itemT = 1f;
            _switchT = 1f;
            _pickTurn = Quaternion.identity;
        }
    }
}
