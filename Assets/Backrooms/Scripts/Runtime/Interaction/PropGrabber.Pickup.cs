using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Picking items up by hand (round 8, 2026-10-28), as the hand plugs a cord in: the hand goes out to the item where it
    /// lies, the body bending and stepping in for it; the item is not touched until the palm is on its grip. Then the
    /// fingers close on it and it comes up with the hand. An item the body cannot get to with a step, a squat or a bend at the
    /// hips is not offered (no focus marks, a click does nothing); one the hand cannot get to after all is left where it is
    /// and the hand comes back. (It flew to the hand: from up to 2.1 m away at once, or after waiting 0.9 s for a hand that
    /// had not got there.)
    /// Round 9: the body's way to an item is planned once, at the click (PlanReach): how far to step, how far to squat (only
    /// for something low) and how far to bend forward from the hips (over a desk or a board, the knees nearly straight), so
    /// the grip ends within PickPlanShare of the arm's reach (the elbow bent 20-40 degrees). The step, the squat and the
    /// bend run alongside the arm going out. The focus marks use the same plan, so what is offered is what the body does.
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>How far the body steps in for an item, at most (m): a long step. (Round 11: 0.65 left an item a little
        /// further off unmarked, and the player shuffled about to get it offered.)</summary>
        public const float PickStep = 0.95f;
        /// <summary>
        /// Round 11 (handling plan, step 1): a reach has this long (s) to get the palm onto an item's grip (FloorTakeBy for a
        /// low one); from then on a palm within TakeSlack of it takes it, and the hand closes the last of the gap as the
        /// fingers close (TakeGap). It waited for the palm within 12-15 mm however long that took: 0.5-1.4 s.
        /// </summary>
        public const float TakeBy = 0.32f, FloorTakeBy = 0.5f, TakeSlack = 0.045f;
        /// <summary>Given up on (no nearer, or too long), a palm this near the grip still takes it (m).</summary>
        public const float GiveUpSlack = 0.08f;
        /// <summary>An item is offered if the body can bring the grip within this much of the arm's reach (straight).</summary>
        public const float PickReachShare = 1.0f;
        /// <summary>The body is planned so the grip ends within this much of the arm's reach: the elbow bent 20-40 degrees.</summary>
        public const float PickPlanShare = 0.95f;
        /// <summary>The hand's target is kept this far out (of the arm's reach) while the body still comes closer: bent.</summary>
        const float PickBent = 0.88f;
        /// <summary>Held off (the body can come no closer), the arm may go out to this much of its reach.</summary>
        const float PickStretch = 1.06f;
        /// <summary>The palm is on the grip within this (m); within PalmNearGrip it is taken once it gets no nearer.</summary>
        public const float PalmOnGrip = 0.012f, PalmNearGrip = 0.015f;
        /// <summary>The fingers' curl going out to an item (open).</summary>
        const float PickOpenCurl = 0.2f;
        /// <summary>The shoulder comes down this share of what the eye does when the body squats; never below LowestShoulder.</summary>
        public const float ShoulderFall = 0.875f, LowestShoulder = 0.65f;
        /// <summary>The most the body bends forward from the hips for an item (degrees); squatting for something low, at most LowHinge.</summary>
        public const float MaxHinge = 62f, LowHinge = 25f;
        /// <summary>Only a grip lower than this (m above the feet) is squatted for; higher, the knees stay nearly straight
        /// (the eye comes down at most HighStoop: a little give).</summary>
        public const float SquatBelow = 0.42f, HighStoop = 0.08f;
        /// <summary>A grip this low (m above the feet) may have the body bend further at the hips over the squat.</summary>
        public const float VeryLow = 0.15f;

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

        /// <summary>1..0 over the lift: the item is held in the hand as the hand took it where it lay, until the lift is half done,
        /// then eased to how the hand carries it (FirstPersonBody). Put onto the palm from the first frame, a can slid 2-3.5 cm
        /// in the palm as it started up.</summary>
        public float HoldAsTaken
        {
            get
            {
                if (!HeldItem || !_planned || _itemT <= 0f) return 0f;
                // (The change of grip goes on 0.3 s past the lift: in the last half of the lift alone, the hand turned up to
                // 60-90 degrees on the item in a quarter of a second.)
                float u = _itemT < 1f ? _itemT * 0.7f : 0.7f + 0.3f * Mathf.Clamp01((Time.time - _liftDoneAt) / 0.2f);
                return 1f - Ease(0.35f, 1f, u);
            }
        }
        float _liftDoneAt = -1f;
        /// <summary>The lift is done (the item in the carry): the change of grip finishes after it (HoldAsTaken).</summary>
        void LiftDone() { _liftDoneAt = Time.time; }

        /// <summary>0 at or before a, 1 at or after b, eased between. (Mathf.SmoothStep(a, b, t) is a value between a and b,
        /// not this: used so, it read 0.5 at the start of a lift.)</summary>
        static float Ease(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        bool _graspOn;                   // the palm is on the item it took where it lay: the fingers close, then it comes up
        float _graspT, _liftTime = 0.3f;
        // The reach for a grip: how long, the nearest the palm has come, how long without getting nearer, the arm let out
        // further (held off), the arm's way blocked, near enough but no nearer, the step held off.
        float _pickT, _pickBest, _pickStuckT, _pickSlowT, _pickStretch, _pickBlockedT, _pickNearT, _pickHeldOffT;
        Vector3 _pickHandAt, _pickStart, _pickStepDir;
        bool _pickClamped, _pickNoStep, _pickStepping, _pickFrozen;
        // The plan (PlanReach) and what the body is told: the squat (the eye down, m), extra squat and bend when held off.
        ReachPlan _plan;
        bool _planned;
        float _pickStoop, _pickStoopExtra, _pickHingeExtra;
        // The hand turned about the palm's normal (fingers down toward a low grip) as the arm reaches it best, and that turn
        // taken back off over the lift.
        float _pickTiltNext;
        Quaternion _pickTurn = Quaternion.identity, _pickTurnWant = Quaternion.identity;   // in the hand's own frame
        static readonly float[] PickTilts = { 0f, 25f, -25f, 50f, -50f, 75f, -75f };
        static readonly float[] PickRolls = { 0f, 25f, -25f };
        static readonly float[] PickPitches = { 0f, 30f, -30f };

        void StopPickStep() { if (_pickStepping) { _player.botInput = null; _pickStepping = false; } }

        // ---------------- Bending at the hips ----------------

        /// <summary>
        /// How far the body bends forward from the hips (degrees), reaching over a desk or a board for an item (or a piece
        /// of furniture): the pelvis tips over the thighs, the knees stay nearly straight, the hips stay, and the shoulder
        /// and the view go out over it (FirstPersonBody.HingeShoulder, HingeHead). Eased in alongside the arm.
        /// </summary>
        public float ReachHinge { get; private set; }
        float _hingeWant, _hingeVel;

        /// <summary>The hinge eased each frame: as planned while the hand goes for something, back off as an item comes up,
        /// none otherwise.</summary>
        void UpdateReachHinge()
        {
            bool going = _fetch || (HeldItem && _itemT < 1f) || (_target && Current != State.Idle);
            if (!going) _hingeWant = 0f;
            float want = HeldItem && _itemT > 0f ? _hingeWant * (1f - Ease(0.15f, 0.9f, _itemT)) : _hingeWant;
            // Putting an item down by hand (E): the bend at the hips its spot asks for.
            if (PlaceBends) want = Mathf.Max(want, _placeHinge);
            // (Round 11: 0.08 s, was 0.13: the bend took about half a second to get there.)
            ReachHinge = Mathf.Max(0f, Springs.Damper(ReachHinge, ref _hingeVel, want, 0.08f, Time.deltaTime));
            if (ReachHinge < 0.05f && want <= 0f) { ReachHinge = 0f; _hingeVel = 0f; }
            ApplyHingeView();
        }

        float _viewDrop, _viewFwd, _viewYSet = float.NaN;

        /// <summary>
        /// The view goes with the head as the body bends at the hips (forward and down), set after the controller has placed
        /// the eye this frame: the held item, the body and the camera all see it. (Leaning out with the view left where it
        /// was, the eye would have been over the body's own back.)
        /// </summary>
        void ApplyHingeView()
        {
            var pivot = _player ? _player.cameraPitchPivot : null;
            if (!pivot) return;
            Vector2 e = ReachHinge > 0f && Armed ? _body.HingeHead(ReachHinge, _player.stoop) : Vector2.zero;
            if (e == Vector2.zero && _viewFwd == 0f && _viewDrop == 0f) return;
            Vector3 lp = pivot.localPosition;
            // (The controller sets the eye's height every frame; if it has not this frame, what was taken off goes back first.)
            if (Mathf.Abs(lp.y - _viewYSet) < 1e-6f) lp.y += _viewDrop;
            lp.y -= e.y; lp.z = e.x;
            pivot.localPosition = lp;
            _viewYSet = lp.y; _viewDrop = e.y; _viewFwd = e.x;
        }

        // ---------------- The hand's turn ----------------

        /// <summary>
        /// The hand's turn reaching for an item: as it will hold it, turned about the palm's normal (the fingers down along a
        /// can, a lamp's rod) and rolled a little about the fingers, as far as the arm needs (FirstPersonBody.ArmStrain). A
        /// can on the floor held square, fingers level, wants the wrist 10 cm off the floor: the arm cannot get it there.
        /// </summary>
        Quaternion ReachTurn(Quaternion square, Vector3 grip)
        {
            if (Armed && Time.time >= _pickTiltNext)
            {
                _pickTiltNext = Time.time + 0.15f;
                float best = float.MaxValue;
                foreach (float a in PickTilts)
                    foreach (float r in PickRolls)
                        foreach (float pt in PickPitches)
                        {
                            // (Pitched: the fingers down over it, as a hand takes a stick of chalk off the floor from above.)
                            Quaternion rel = Quaternion.AngleAxis(a, Vector3.up) * Quaternion.AngleAxis(r, Vector3.forward) * Quaternion.AngleAxis(pt, Vector3.right);
                            // (Round 10: and how far the palm would still miss once the wrist is moved for it: a turn the
                            // wrist could not do left the palm 28-36 mm off a lamp's rod until the reach gave up.)
                            float s = _body.ArmStrainFrom(ReachShoulder, grip, square * rel, out float over, out float ws, out float miss) + over * 0.02f
                                      + miss * 200f + Mathf.Max(0f, ws - 0.97f) * 40f
                                      + (Mathf.Abs(a) + (Mathf.Abs(r) + Mathf.Abs(pt)) * 1.5f) * 0.008f + Quaternion.Angle(rel, _pickTurn) * 0.001f;
                            if (s < best) { best = s; _pickTurnWant = rel; }
                        }
            }
            _pickTurn = Quaternion.Slerp(_pickTurn, _pickTurnWant, 1f - Mathf.Exp(-Time.deltaTime / 0.12f));
            return square * _pickTurn;
        }

        /// <summary>The reaching turn (ReachTurn) on a hand turned square, a share of it (taken back off as the item comes up).</summary>
        Quaternion WithReachTurn(Quaternion square, float share) => square * Quaternion.Slerp(Quaternion.identity, _pickTurn, share);
        static readonly RaycastHit[] s_pickHits = new RaycastHit[16];

        /// <summary>Where the hand takes an item, as it lies now (its grip: the palm's point), on the side chosen (IGripRound).</summary>
        Vector3 GripOf(WorldItem item) => item.transform.TransformPoint(GripLocalOf(item, AltShare(item)));

        /// <summary>The hand's turn taking an item as it lies now, on the side chosen.</summary>
        Quaternion GripTurnOf(WorldItem item) => item.transform.rotation * GripTurnLocalOf(item, AltShare(item));

        // ---------------- Round 10: the side it is taken from ----------------

        WorldItem _altItem;              // the item whose grip is turned about its axis (IGripRound) for this pick-up
        Vector3 _altPoint;               // a point on its axis, in the item's space as the hand sees it (mirrored for the left hand)
        Quaternion _altTurn = Quaternion.identity;   // the grip's turn about that point: round the axis, and maybe flipped
        float _altAngle;                 // degrees round the axis (for tests)
        bool _altFlip;                   // the hand turned end for end on the handle (for tests)
        static readonly float[] AltAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        static readonly int[] s_altOrder = new int[16];
        static readonly float[] s_altScore = new float[16];
        Quaternion AltQ(float share) => Quaternion.Slerp(Quaternion.identity, _altTurn, share);

        /// <summary>How much of the chosen side's turn applies: all of it going for the item, eased off to the carry's grip as
        /// it comes up (with HoldAsTaken: the hand and the item turn together, the palm stays on the item's surface).</summary>
        float AltShare(WorldItem item) => !item || item != _altItem ? 0f : _altPlan || _fetch == item ? 1f
                                          : HeldItem == item ? (KeepsSide(item) ? 1f : _itemT > 0f ? HoldAsTaken : 1f) : 0f;

        /// <summary>
        /// Round 11 (handling plan, step 5): the grip it was taken by is the grip it is carried by. Taken from another side
        /// round its axis (a can, a lamp's rod, a coil, a roll of tape), it is carried turned so about its axis, the hand as it
        /// always carries; it turned in the hand on the way up to the one carry grip, up to 135 degrees (a can spinning in the
        /// fingers). Not the sledgehammer (it plans its own carry) nor a handle taken end for end (the head would hang the
        /// wrong way).
        /// </summary>
        bool KeepsSide(WorldItem item) => !_altFlip && !(item is Sledgehammer);
        bool _altPlan;                   // planning: the side being tried applies in full

        /// <summary>The held item's grip in its own space (mirrored), on the side it was taken from as far as that still applies.</summary>
        public Vector3 HeldGripLocal => HeldItem ? GripLocalOf(HeldItem, AltShare(HeldItem)) : Vector3.zero;

        /// <summary>
        /// The held item's turn in the hand since it was taken (item space: a point on its axis and the turn about it): taken
        /// from another side round its grip's axis, it turns in the hand toward the carry's grip as it comes up (FirstPersonBody
        /// puts it there, as the hand took it, then this).
        /// </summary>
        public bool TakenRegrip(out Vector3 point, out Quaternion turn)
        {
            point = Vector3.zero; turn = Quaternion.identity;
            if (!HeldItem || HeldItem != _altItem) return false;
            point = _altPoint;
            turn = _altTurn * Quaternion.Inverse(AltQ(AltShare(HeldItem)));
            return true;
        }

        /// <summary>The grip's palm point in the item's space (mirrored), turned a share of the chosen side about its axis.</summary>
        Vector3 GripLocalOf(WorldItem item, float share)
        {
            Vector3 g = Mirror(item.GripLocal);
            if (item != _altItem || share <= 0f) return g;
            return _altPoint + AltQ(share) * (g - _altPoint);
        }

        /// <summary>The grip's hand turn in the item's space (mirrored), turned a share of the chosen side about its axis.</summary>
        Quaternion GripTurnLocalOf(WorldItem item, float share)
        {
            Quaternion g = item.GripRotationLocal;
            Quaternion t = Quaternion.LookRotation(Mirror(g * Vector3.forward), Mirror(g * Vector3.up));
            if (item != _altItem || share <= 0f) return t;
            return AltQ(share) * t;
        }

        /// <summary>
        /// Plans the body's reach for an item and the side its grip is taken from (IGripRound): the sides are tried best first
        /// (the back of the hand toward the shoulder and up, the grip near), never one with the hand in what the item lies
        /// on, and the first the body and the arm can get to is kept (_altItem). A coil was reached for across itself, its
        /// grip on the far rim; a hammer or a can lying down had its grip under it.
        /// </summary>
        bool PlanSide(WorldItem item, float share, out ReachPlan plan, out ReachBody b)
        {
            _altPlan = true;
            try { return PlanSideCore(item, share, out plan, out b); }
            finally { _altPlan = false; }
        }

        bool PlanSideCore(WorldItem item, float share, out ReachPlan plan, out ReachBody b)
        {
            _altItem = null; _altAngle = 0f;
            var round = item as IGripRound;
            if (round == null || !Armed) return PlanFor(GripOf(item), item, null, share, out plan, out b);
            round.GripAxis(out Vector3 pt, out Vector3 ax);
            Vector3 mp = Mirror(pt), ma = Mirror(ax).normalized;
            SupportOf(item, out float top);
            Vector3 S = ReachShoulder;
            Vector3 g0 = Mirror(item.GripLocal);
            Quaternion t0 = GripTurnLocalOf(item, 0f);
            int n = 0;
            for (int i = 0; i < AltAngles.Length * 2; i++)
            {
                Quaternion r = Quaternion.AngleAxis(AltAngles[i % AltAngles.Length], ma);
                Vector3 gl = mp + r * (g0 - mp);
                Quaternion tl = r * t0;
                if (i >= AltAngles.Length)
                {
                    // Flipped: the hand turned end for end about its back, the handle across the palm the other way (a hammer
                    // lying with its face toward you is taken palm down with the fingers away from you, not back at you). Only
                    // where the back of the hand stands straight out from the axis (a handle, a rod, a neck, a can's side).
                    Vector3 onAxis = mp + ma * Vector3.Dot(gl - mp, ma), U0 = tl * Vector3.up;
                    if ((gl - onAxis).magnitude < 0.003f || Vector3.Cross(U0, gl - onAxis).magnitude > 0.004f) continue;
                    tl = Quaternion.AngleAxis(180f, U0) * tl;
                }
                Vector3 g = item.transform.TransformPoint(gl);
                Quaternion turn = item.transform.rotation * tl;
                // The hand's lowest point (the palm's edges, the heel, the fingers' first part, the back) not in the floor.
                Vector3 R = turn * Vector3.right, F = turn * Vector3.forward, U = turn * Vector3.up;
                float low = Mathf.Abs(R.y) * 0.04f + Mathf.Max(0f, -F.y) * 0.05f + Mathf.Max(0f, F.y) * 0.03f + Mathf.Max(0f, -U.y) * 0.03f;
                if (g.y - low < top - 0.004f) continue;
                Vector3 toS = S - g;
                float score = -Vector3.Dot(U, toS.normalized) - U.y * 0.5f + Mathf.Abs(AltAngles[i % AltAngles.Length]) / 180f * 0.3f + toS.magnitude
                              + (i >= AltAngles.Length ? 1f : 0f);
                int at = n;
                while (at > 0 && s_altScore[at - 1] > score) { s_altScore[at] = s_altScore[at - 1]; s_altOrder[at] = s_altOrder[at - 1]; at--; }
                s_altScore[at] = score; s_altOrder[at] = i; n++;
            }
            for (int k = 0; k < n; k++)
            {
                int i = s_altOrder[k];
                float angle = AltAngles[i % AltAngles.Length];
                Quaternion r = Quaternion.AngleAxis(angle, ma);
                _altItem = item; _altPoint = mp; _altTurn = r; _altAngle = angle; _altFlip = i >= AltAngles.Length;
                if (_altFlip)
                {
                    // (About the axis's point under the grip: the flip leaves the grip where it is, the turn round the axis too.)
                    Vector3 gl = mp + r * (g0 - mp);
                    _altPoint = mp + ma * Vector3.Dot(gl - mp, ma);
                    _altTurn = Quaternion.AngleAxis(180f, r * t0 * Vector3.up) * r;
                }
                if (PlanFor(GripOf(item), item, null, share, out plan, out b)) return true;
            }
            _altItem = null; _altAngle = 0f; _altFlip = false; _altTurn = Quaternion.identity;
            PlanFor(GripOf(item), item, null, share, out plan, out b);
            return false;
        }

        static Quaternion[] s_reachRels;

        /// <summary>
        /// Can the arm put the palm on grip p from shoulder S (where the plan puts the body) with the hand turned about as the
        /// item asks (square), turned as ReachTurn may turn it? The arm is solved as each frame solves it, the wrist's and
        /// forearm's limits included: the hand at most 25 degrees off and the wrist within reach once the palm is on its point.
        /// </summary>
        bool HandGets(Vector3 S, Vector3 p, Quaternion square, float share)
        {
            if (!Armed) return true;
            if (s_reachRels == null)
            {
                var list = new System.Collections.Generic.List<RelCost>();
                foreach (float a in PickTilts)
                    foreach (float r in PickRolls)
                        foreach (float pt in PickPitches)
                            list.Add(new RelCost { cost = Mathf.Abs(a) + (Mathf.Abs(r) + Mathf.Abs(pt)) * 1.5f,
                                rel = Quaternion.AngleAxis(a, Vector3.up) * Quaternion.AngleAxis(r, Vector3.forward) * Quaternion.AngleAxis(pt, Vector3.right) });
                list.Sort((x, y) => x.cost.CompareTo(y.cost));
                s_reachRels = new Quaternion[list.Count];
                for (int i = 0; i < list.Count; i++) s_reachRels[i] = list[i].rel;
            }
            float wristMax = share >= 1f ? 1.0f : 0.97f;
            foreach (var rel in s_reachRels)
            {
                _body.ArmStrainFrom(S, p, square * rel, out float over, out float ws, out float miss);
                if (over <= 25f && ws <= wristMax && miss <= 0.012f) return true;
            }
            return false;
        }
        struct RelCost { public float cost; public Quaternion rel; }

        // ---------------- Planning the body's way to it ----------------

        /// <summary>How the body brings its shoulder near a grip: a step (m), a squat (the eye down, m), a bend at the hips
        /// (degrees), and how far out of the arm's reach the grip is then.</summary>
        public struct ReachPlan { public float step, stoop, hinge, share; }

        /// <summary>The body as it would stand upright where it is, for planning a reach (PlanReach).</summary>
        public struct ReachBody
        {
            /// <summary>The working shoulder and the eye standing upright here (no squat, no bend at the hips).</summary>
            public Vector3 shoulder, eye;
            /// <summary>Flat: the way the body faces (it bends that way) and the way it would step (toward the grip).</summary>
            public Vector3 heading, stepDir;
            /// <summary>How far it may step (m), the feet's height, the most the eye may come down (m), the most it may bend
            /// at the hips (degrees), the arm's reach (m).</summary>
            public float room, feet, stoopMax, hingeMax, reach;
            /// <summary>The shoulder's and the head's move (forward, down; m) bending at the hips this many degrees.</summary>
            public System.Func<float, float, Vector2> hingeShoulder, hingeHead;
        }

        /// <summary>The shoulder for a plan (pure).</summary>
        public static Vector3 ShoulderFor(in ReachBody b, float step, float stoop, float hinge)
        {
            Vector2 hs = hinge > 0f && b.hingeShoulder != null ? b.hingeShoulder(hinge, stoop) : Vector2.zero;
            return b.shoulder + b.stepDir * step + b.heading * hs.x + Vector3.down * (stoop * ShoulderFall + hs.y);
        }

        /// <summary>The head (the view) for a plan (pure).</summary>
        public static Vector3 HeadFor(in ReachBody b, in ReachPlan p)
        {
            Vector2 hh = p.hinge > 0f && b.hingeHead != null ? b.hingeHead(p.hinge, p.stoop) : Vector2.zero;
            return b.eye + b.stepDir * p.step + b.heading * hh.x + Vector3.down * (p.stoop + hh.y);
        }

        static readonly ReachPlan[] s_planBest = new ReachPlan[8];
        static readonly float[] s_planCost = new float[8];

        /// <summary>
        /// Pure (for tests): the body's least-effort way to bring grip p within share of the arm's reach of the shoulder,
        /// the arm's way and the head clear (clear: null for no check). A step costs most, then bending at the hips; a squat
        /// is cheap only for something low (below SquatBelow): waist-high the knees stay nearly straight and the body bends
        /// forward from the hips (it squatted for a desk, the arm straight). A grip nearer than 0.78 of the reach costs a
        /// little (the arm cramped).
        /// </summary>
        public static bool PlanReach(Vector3 p, in ReachBody b, float share, System.Func<ReachPlan, Vector3, bool> clear, out ReachPlan plan)
        {
            plan = new ReachPlan();
            bool low = p.y - b.feet < SquatBelow;
            float stoopTop = Mathf.Min(b.stoopMax, low ? 0.85f : HighStoop);
            // (Round 10: lower than VeryLow, a squat and a bend of up to LowHinge + 10: a coil's rim at 3 cm.)
            float hingeTop = Mathf.Min(b.hingeMax, low ? (p.y - b.feet < VeryLow ? LowHinge + 10f : LowHinge) : MaxHinge);
            int nS = b.room > 0.02f ? 6 : 1, kept = 0;
            for (int i = 0; i < nS; i++)
            {
                float s = nS == 1 ? 0f : b.room * i / (nS - 1);
                for (float h = 0f; h <= hingeTop + 0.01f; h += 5f)
                    for (float st = 0f; st <= stoopTop + 0.001f; st += 0.05f)
                    {
                        // (The squat brings the shoulder down to LowestShoulder at most; bending at the hips takes it lower.)
                        if (b.shoulder.y - st * ShoulderFall < b.feet + LowestShoulder - 0.01f) break;
                        Vector3 S = ShoulderFor(b, s, st, h);
                        float d = (p - S).magnitude / Mathf.Max(0.1f, b.reach);
                        if (d > share) continue;
                        // (The torso past 60 degrees from upright, squatting and bent at the hips, costs more: 80-92 looked
                        // like folding over for a can a step away.)
                        float torso = (low ? FirstPersonBody.StoopBend(st) : 0f) + h;
                        float cost = s * 0.8f + h * 0.006f + st * (low ? 0.2f : 2.5f) + Mathf.Max(0f, 0.9f - d) * 1.5f + Mathf.Max(0f, torso - 60f) * 0.015f;
                        // Kept among the cheapest few (then the first whose way is clear).
                        int at = kept;
                        while (at > 0 && s_planCost[at - 1] > cost) at--;
                        if (at >= s_planBest.Length) continue;
                        for (int k = Mathf.Min(kept, s_planBest.Length - 1); k > at; k--) { s_planBest[k] = s_planBest[k - 1]; s_planCost[k] = s_planCost[k - 1]; }
                        s_planBest[at] = new ReachPlan { step = s, stoop = st, hinge = h, share = d };
                        s_planCost[at] = cost;
                        if (kept < s_planBest.Length) kept++;
                    }
            }
            for (int k = 0; k < kept; k++)
            {
                var c = s_planBest[k];
                if (clear == null || clear(c, ShoulderFor(b, c.step, c.stoop, c.hinge))) { plan = c; return true; }
            }
            return false;
        }

        System.Func<float, float, Vector2> _hingeShoulderFn, _hingeHeadFn;

        /// <summary>The body as it would stand upright where it is now (the squat and the bend taken back off), toward p.</summary>
        ReachBody BodyNow(Vector3 p, WorldItem item)
        {
            if (_hingeShoulderFn == null) { _hingeShoulderFn = (h, s) => _body.HingeShoulder(h, s); _hingeHeadFn = (h, s) => _body.HingeHead(h, s); }
            var b = new ReachBody { feet = transform.position.y, reach = ArmReach, hingeShoulder = _hingeShoulderFn, hingeHead = _hingeHeadFn };
            b.heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            bool standing = _player.CurrentStance == CamcorderPlayerController.Stance.Stand;
            float eyeTop = standing ? _player.BaseEye : _player.Eye;
            Vector3 S = ReachShoulder;
            Vector2 hs = _body.HingeShoulder(ReachHinge, _player.stoop);
            S -= b.heading * hs.x; S.y += hs.y;
            S.y += Mathf.Max(0f, eyeTop - _player.Eye) * ShoulderFall;
            b.shoulder = S;
            b.eye = transform.position + Vector3.up * eyeTop;
            b.stoopMax = standing ? Mathf.Clamp(eyeTop - 0.6f, 0f, 0.85f) : 0f;
            b.hingeMax = standing ? MaxHinge : 0f;
            Vector3 flat = p - S; flat.y = 0f;
            b.stepDir = flat.sqrMagnitude > 1e-4f ? flat.normalized : b.heading;
            b.room = flat.sqrMagnitude > 1e-4f ? StepRoom(b.stepDir, item) : 0f;
            // Not onto it: the body's capsule stays clear of what it reaches for (stepping on until the shoulder was over a can
            // on the floor, the body stood on it and pushed it 4 cm).
            if (item && b.room > 0f)
            {
                // (The capsule passes over what the hand goes for (PassOver), so only the feet keep clear of it: the toes are
                // about 0.04 m ahead of the view standing, 0.12 m bent over: ToeClear off its footprint. Kept off by the
                // capsule, a coil or a lamp on the floor stayed 0.6 m off the feet and out of reach: never offered; kept off
                // a round about its middle, a hammer lying across the way stayed 0.45 m off its handle.)
                // (Less what the body runs on as it stops: it came 0.1 m further than planned and pushed a can 4 cm.)
                b.room = Mathf.Clamp(StepToItem(item, transform.position, b.stepDir, b.feet) - 0.1f, 0f, b.room);
            }
            return b;
        }

        /// <summary>How far the toes keep off an item reached for on the floor (m): ahead of the view 0.04 standing, 0.12 bent.</summary>
        public const float ToeClear = 0.15f;

        /// <summary>
        /// How far the body may step from <paramref name="from"/> along flat <paramref name="dir"/> before it comes within
        /// ToeClear of the item's footprint: its colliders down at the feet, as boxes on the floor (a lamp's shade may hang
        /// over the toes). 0 if it is there already; very far if the way passes by it.
        /// </summary>
        static float StepToItem(WorldItem item, Vector3 from, Vector3 dir, float feet)
        {
            float best = float.MaxValue;
            foreach (var c in item.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger && c.bounds.min.y <= feet + 0.25f) best = Mathf.Min(best, StepToBox(c.bounds, from, dir));
            return best;
        }

        /// <summary>Pure (for tests): how far from <paramref name="from"/> along flat <paramref name="dir"/> until within ToeClear
        /// of a box on the floor (seen from above); 0 if there already, float.MaxValue if the way passes by it.</summary>
        public static float StepToBox(Bounds box, Vector3 from, Vector3 dir)
        {
            // (From the middle and from each foot, 0.11 m to the side: a can in front of the right foot was passed by.)
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * 0.11f;
            float best = float.MaxValue;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 o = from + side * k;
                float t0 = 0f, t1 = float.MaxValue;
                if (!Slab(o.x, dir.x, box.min.x - ToeClear, box.max.x + ToeClear, ref t0, ref t1)) continue;
                if (!Slab(o.z, dir.z, box.min.z - ToeClear, box.max.z + ToeClear, ref t0, ref t1)) continue;
                best = Mathf.Min(best, t0);
            }
            return best;
        }

        /// <summary>One slab of a ray-box test: narrows the ray's [t0, t1] to where it is between lo and hi (false: never).</summary>
        static bool Slab(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-6f) return o >= lo && o <= hi;
            float a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { float t = a; a = b; b = t; }
            t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, b);
            return t0 <= t1;
        }

        /// <summary>The body's plan for a grip (the arm's way and the head clear), within share of the reach.</summary>
        bool PlanFor(Vector3 p, WorldItem item, Collider[] own, float share, out ReachPlan plan, out ReachBody b)
        {
            b = BodyNow(p, item);
            var body = b;
            // (Round 10: with an item, the arm is solved from the planned shoulder too: what is offered is taken.)
            Quaternion turn = item ? GripTurnOf(item) : Quaternion.identity;
            bool loose = _looseTurn;
            return PlanReach(p, b, share, (c, s) => ArmWayClear(s, p, item, own) && HeadClear(body, c, item, null) && (!item || loose || HandGets(s, p, turn, share)), out plan);
        }

        /// <summary>Round 11: planning without the hand's turn checked (the palm's point only). An item the arm reaches but not
        /// quite at the turn it asks for is offered too: taken a little off its grip (TakeSlack, GiveUpSlack), it eases into
        /// the hand on the way up. (Refused, it looked as if the item could only be had from one exact spot.)</summary>
        bool _looseTurn;

        bool PlanSideLoose(WorldItem item, float share, out ReachPlan plan, out ReachBody b)
        {
            _looseTurn = true;
            try { return PlanSide(item, share, out plan, out b); }
            finally { _looseTurn = false; }
        }

        /// <summary>
        /// Can the body get its palm to this item with a step, a squat or a bend at the hips, the arm's way there and the
        /// head clear? (What the focus marks offer and a click takes: the same plan the body then carries out.)
        /// </summary>
        public bool BodyReaches(WorldItem item)
        {
            if (!item) return false;
            if (!Armed) return (GripOf(item) - Eye).magnitude <= itemRange;
            if ((item.transform.position - ReachShoulder).magnitude > ArmReach + PickStep + 0.9f) return false;
            // (Asked every frame for what the view is on: kept a quarter of a second while the body and the item stay put.)
            float yaw = transform.eulerAngles.y;
            if (_reachMemo.TryGetValue(item, out var m) && Time.time - m.t < 0.25f && (m.at - transform.position).sqrMagnitude < 1e-3f
                && Mathf.Abs(Mathf.DeltaAngle(m.yaw, yaw)) < 4f && (m.itemAt - item.transform.position).sqrMagnitude < 1e-4f && Mathf.Abs(m.stoop - _player.stoop) < 0.03f)
                return m.yes;
            // (Without disturbing the side chosen for an item the hand is going for.)
            var keepItem = _altItem; Vector3 keepP = _altPoint; Quaternion keepT = _altTurn; float keepAngle = _altAngle; bool keepFlip = _altFlip;
            bool yes = PlanSide(item, PickReachShare, out _, out _) || PlanSideLoose(item, PickReachShare, out _, out _);
            _altItem = keepItem; _altPoint = keepP; _altTurn = keepT; _altAngle = keepAngle; _altFlip = keepFlip;
            if (_reachMemo.Count > 64) _reachMemo.Clear();
            _reachMemo[item] = new ReachMemo { t = Time.time, at = transform.position, itemAt = item.transform.position, yaw = yaw, stoop = _player.stoop, yes = yes };
            return yes;
        }

        struct ReachMemo { public float t, yaw, stoop; public Vector3 at, itemAt; public bool yes; }
        readonly System.Collections.Generic.Dictionary<WorldItem, ReachMemo> _reachMemo = new System.Collections.Generic.Dictionary<WorldItem, ReachMemo>();

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

        static bool Ignored(Collider c, Transform self, Collider[] own) =>
            !c || c.isTrigger || c.transform.IsChildOf(self) || c.GetComponentInParent<WorldItem>() || (own != null && System.Array.IndexOf(own, c) >= 0);

        /// <summary>
        /// Nothing solid on the line from the shoulder to the grip (the arm would go through a shelf or a desk). The edge of
        /// what the item stands on is left out (a hand comes over it: a can set back on a high board, a roll of tape on a
        /// shelf above the shoulder, were refused for the board's front edge). own: the colliders of what is reached for.
        /// </summary>
        bool ArmWayClear(Vector3 s, Vector3 p, WorldItem item, Collider[] own = null)
        {
            // Not from a shoulder inside something (a step and a bend into a table's box: a ray from inside it sees nothing).
            int m = Physics.OverlapSphereNonAlloc(s, 0.05f, s_pickOver, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++) if (!Ignored(s_pickOver[i], transform, own)) return false;
            Vector3 d = p - s;
            float len = d.magnitude - 0.04f;
            if (len <= 0.01f) return true;
            float top = p.y;
            Collider support = item ? SupportOf(item, out top) : null;
            int n = Physics.RaycastNonAlloc(s, d / d.magnitude, s_pickHits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_pickHits[i].collider;
                if (Ignored(col, transform, own)) continue;
                if ((s_pickHits[i].point - p).magnitude < 0.1f) continue;
                if (col == support && s_pickHits[i].point.y > top - 0.05f) continue;
                return false;
            }
            return true;
        }
        static readonly Collider[] s_pickOver = new Collider[8];

        /// <summary>What the item stands on (a desk, a board, the floor) and the top of it there, or null.</summary>
        Collider SupportOf(WorldItem item, out float top)
        {
            top = item.transform.position.y;
            Vector3 from = item.transform.position + Vector3.up * 0.05f;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, s_pickHits, 0.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider c = null;
            for (int i = 0; i < n; i++)
            {
                var col = s_pickHits[i].collider;
                if (Ignored(col, transform, null) || s_pickHits[i].distance >= best) continue;
                best = s_pickHits[i].distance; c = col; top = s_pickHits[i].point.y;
            }
            return c;
        }

        /// <summary>The head clear where the plan puts it, and on the way there (the view goes with it).</summary>
        bool HeadClear(in ReachBody b, in ReachPlan c, WorldItem item, Collider[] own)
        {
            if (c.hinge <= 0f) return true;
            Vector3 to = HeadFor(b, c), from = b.eye + b.stepDir * c.step + Vector3.down * c.stoop;
            for (int k = 1; k <= 2; k++)
            {
                int m = Physics.OverlapSphereNonAlloc(Vector3.Lerp(from, to, k * 0.5f), 0.11f, s_pickOver, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m; i++) if (!Ignored(s_pickOver[i], transform, own)) return false;
            }
            return true;
        }

        // ---------------- Going for it ----------------

        void BeginReachFor(WorldItem item)
        {
            _pickTurn = _pickTurnWant = Quaternion.identity; _pickTiltNext = 0f;
            PickGaveUp = false;
            _planned = Armed;
            ReachBody b = new ReachBody();
            if (_planned && !PlanSide(item, PickPlanShare, out _plan, out b) && !PlanSide(item, PickReachShare + 0.03f, out _plan, out b)
                && !PlanSideLoose(item, PickPlanShare, out _plan, out b) && !PlanSideLoose(item, PickReachShare + 0.03f, out _plan, out b))
            {
                // (Taken by code with the body unable to get there: it reaches as far as it can and gives up.)
                Vector3 g0 = GripOf(item);
                _plan = new ReachPlan { step = b.room, stoop = b.stoopMax * (g0.y - b.feet < SquatBelow ? 1f : 0f), hinge = 0f, share = 1f };
            }
            if (_planned) BeginPlannedReach(GripOf(item));
        }

        /// <summary>The counters for a reach the body carries out by its plan (_plan), toward grip g.</summary>
        void BeginPlannedReach(Vector3 g)
        {
            _planned = true;
            _pickT = 0f; _pickBest = float.MaxValue; _pickStuckT = _pickSlowT = _pickStretch = _pickBlockedT = _pickNearT = _pickHeldOffT = 0f;
            _pickStart = transform.position; _pickNoStep = false; _pickFrozen = false;
            _pickStoopExtra = _pickHingeExtra = 0f;
            StopPickStep();
            Vector3 flat = g - ReachShoulder; flat.y = 0f;
            _pickStepDir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            _pickStoop = _plan.stoop;
            _hingeWant = _plan.hinge;
        }

        /// <summary>No plan (an item taken straight into the hand): the body neither squats nor bends for it.</summary>
        void ClearPlan() { _planned = false; _plan = new ReachPlan(); _pickStoop = 0f; _pickStoopExtra = 0f; _pickHingeExtra = 0f; _hingeWant = 0f; }

        /// <summary>
        /// Pure (for tests): the palm is on the grip and the item may be taken. Only with the arm all the way out (solved) to
        /// the grip itself (not a point short of it, clamped), the palm within PalmOnGrip of it, or within PalmNearGrip and
        /// no nearer for 0.15 s (nearT counts that).
        /// </summary>
        public static bool PalmTakes(float palmError, bool clamped, bool solved, ref float nearT, float dt)
        {
            if (!solved || clamped) { nearT = 0f; return false; }
            if (palmError <= PalmOnGrip) return true;
            if (palmError <= PalmNearGrip) { nearT += dt; return nearT > 0.06f; }
            nearT = 0f;
            return false;
        }

        /// <summary>
        /// One frame of the hand going for a grip where an item lies, the body carrying out its plan alongside: a step in
        /// (easing off as it gets there), the squat and the bend at the hips (UpdateStoop, UpdateReachHinge). The hand's target
        /// (_pickHandAt) is kept within a bent arm's reach of the shoulder until the body is nearly there, then on the grip.
        /// Held off (no nearer), the arm goes on out and the body bends a little further. 1: the palm is on it; -1: give up (no
        /// nearer for a second, the arm's way blocked, or 4 s); 0: on the way.
        /// </summary>
        int ReachFor(Vector3 grip, WorldItem item, float dt, Collider[] own = null)
        {
            _pickT += dt;
            _pickHandGrip = grip;
            Vector3 S = ReachShoulder;
            float reach = ArmReach;
            if (!Armed) { _pickHandAt = grip; return 1; }

            // The step: as far as planned, toward the grip, slowing as it gets there.
            Vector3 moved = transform.position - _pickStart; moved.y = 0f;
            float stepLeft = _plan.step - Vector3.Dot(moved, _pickStepDir);
            _pickHeldOffT = _pickStepping && _player.PlanarVelocity.magnitude < 0.08f && _pickT > 0.3f ? _pickHeldOffT + dt : 0f;
            if (_pickHeldOffT > 0.2f || moved.magnitude > _plan.step + 0.15f) _pickNoStep = true;
            if (!_pickNoStep && stepLeft > 0.03f)
            {
                Vector3 local = transform.InverseTransformDirection(_pickStepDir);
                _player.botInput = new Vector2(local.x, local.z).normalized * Mathf.Clamp(0.5f + stepLeft * 2.2f, 0.5f, 1f);
                _pickStepping = true;
            }
            else StopPickStep();
            bool stepDone = _pickNoStep || stepLeft <= 0.05f;

            // The squat and the bend at the hips, as planned (more if held off). Once the grip is within the planned share of
            // the reach from the shoulder as it really is, the body goes no further down (it came 4 cm nearer than planned
            // and the elbow was bent 54-59 degrees at the grab).
            if (!_pickFrozen && stepDone && _pickStretch <= 0f && (grip - S).magnitude <= reach * PickPlanShare && HandBlend > 0.5f)
            {
                _pickFrozen = true;
                _plan.hinge = Mathf.Min(_plan.hinge, ReachHinge + 2f);
                _plan.stoop = Mathf.Min(_plan.stoop, _player.stoop);
            }
            _pickStoop = _plan.stoop + _pickStoopExtra;
            _hingeWant = Mathf.Min(_plan.hinge + _pickHingeExtra, MaxHinge + 8f);
            // (As planned, the extra bend for an arm held off left out: waiting for a bend the body could not make, the hand was
            // kept to a bent arm, 45 mm off a lamp's rod, until it gave up.)
            // (Round 11: the arm goes on out while the body finishes its last 10 degrees and 10 cm: it waited for all of it.)
            bool bodyThere = stepDone && ReachHinge >= Mathf.Min(_hingeWant, _plan.hinge) - 10f && _player.stoop >= Mathf.Min(_pickStoop, _plan.stoop) - 0.1f;

            // The hand: bent while the body comes; then out to the grip (as far as planned, a little more), then further
            // if it gets no nearer.
            float within = bodyThere || _pickStretch > 0f ? Mathf.Lerp(Mathf.Max(PickBent, _plan.share + 0.04f), PickStretch, _pickStretch) : PickBent;
            _pickHandAt = Plug.WithinComfort(grip, Vector3.zero, S, reach, within, out _pickClamped);

            float palmToGrip = (_body.PalmNow - grip).magnitude;
            bool solved = HandBlend >= 0.97f && _switchT >= 1f;
            if (PalmTakes(PalmError, _pickClamped, solved, ref _pickNearT, dt)) return Took(0f);
            // On time (round 11): past its time, a palm near enough takes an item and closes the gap as the fingers close.
            if (item && solved && _pickT >= (grip.y - transform.position.y < SquatBelow ? FloorTakeBy : TakeBy) && palmToGrip <= TakeSlack)
                return Took(palmToGrip);

            // No nearer once the body is there: the arm goes on out and the body bends a little further.
            if (palmToGrip < _pickBest - 0.004f) { _pickBest = palmToGrip; _pickStuckT = 0f; _pickSlowT = 0f; }
            else if (HandBlend > 0.9f) { _pickStuckT += dt; if (bodyThere) _pickSlowT += dt; }
            // (Once going on out it keeps on: each bit of headway it made reset the wait, and it crept out 4 mm at a time.)
            if (_pickSlowT > 0.12f || _pickStretch > 0f)
            {
                _pickStretch = Mathf.MoveTowards(_pickStretch, 1f, dt * 3f);
                if (grip.y - transform.position.y < SquatBelow) _pickStoopExtra = Mathf.MoveTowards(_pickStoopExtra, 0.12f, dt * 0.6f);
                _pickHingeExtra = Mathf.MoveTowards(_pickHingeExtra, 10f, dt * 30f);
            }
            _pickBlockedT = ArmWayClear(S, grip, item, own) ? 0f : _pickBlockedT + dt;
            if (_pickStuckT > 1f || _pickT > 4f || (_pickBlockedT > 0.8f && _pickStuckT > 0.4f))
            {
                // (Round 11: near enough, it is taken rather than given up: the hand came back from 45 mm off a lamp's rod.)
                if ((item || LowGrab) && solved && palmToGrip <= GiveUpSlack) return Took(palmToGrip);
                PickGaveUp = true; return -1;
            }
            // (Held off with the palm already near, it need not wait the second out.)
            if ((item || LowGrab) && solved && _pickStuckT > 0.25f && palmToGrip <= GiveUpSlack) return Took(palmToGrip);
            return 0;
        }

        /// <summary>The palm (solved) is taken onto the grip from where it is: the gap closes as the fingers close (TakeGap).</summary>
        int Took(float gap)
        {
            _takeGap = gap > 0.001f && Armed ? _body.PalmNow - _pickHandGrip : Vector3.zero;
            return 1;
        }
        Vector3 _takeGap, _pickHandGrip;

        /// <summary>Round 11: the hand's offset from the grip as the fingers close on an item taken a little short of it,
        /// gone in GapClose seconds (it is the hand that moves the last few centimetres, never the item).</summary>
        Vector3 TakeGap => _graspOn && _itemT <= 0f && _takeGap != Vector3.zero ? _takeGap * (1f - Ease(0f, GapClose, _graspT)) : Vector3.zero;
        const float GapClose = 0.09f;

        /// <summary>The hand goes out to take an item where it lies (not yet touched).</summary>
        void StartFetch(WorldItem item)
        {
            _fetch = item;
            PassOver(item);
            BeginReachFor(item);
            if (HandBlend > 0.5f) { _switchPos = HandPosition; _switchRot = HandRotation; _switchT = 0f; } else _switchT = 1f;
            _pickHandAt = HandBlend > 0.5f ? HandPosition : GripOf(item);
        }

        /// <summary>
        /// Round 10: the body's capsule passes over the item the hand goes for (as it does small things), so the feet can stand
        /// up to it as a person's do; it is solid to the body again once the body has stepped away (UpdatePassing).
        /// </summary>
        void PassOver(WorldItem item)
        {
            var cc = GetComponent<CharacterController>();
            if (!cc || !item) return;
            foreach (var c in item.GetComponentsInChildren<Collider>(true)) if (c && c.enabled) Physics.IgnoreCollision(cc, c, true);
            if (!_passing.Contains(item)) _passing.Add(item);
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
            if (!item || item.Held || !item.gameObject.activeInHierarchy) { EndFetch(); ClearPlan(); return; }
            int r = ReachFor(GripOf(item), item, Time.deltaTime);
            if (r < 0) { EndFetch(); ClearPlan(); _itemReachUntil = -1f; return; }
            if (r > 0) { EndFetch(); Take(item, true, true); }
        }

        /// <summary>The hand going out to the item (UpdateHandPose): open, turned as it will take it.</summary>
        void FetchHandPose()
        {
            HandPosition = _pickHandAt;
            HandRotation = ReachTurn(GripTurnOf(_fetch), GripOf(_fetch));
            if (_switchT < 1f)
            {
                _switchT = Mathf.Min(1f, _switchT + Time.deltaTime / SwitchTime);
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
            if (!Armed) { palmOn = true; return true; }
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
            Note("CAN'T REACH");
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
            ClearPlan();
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
