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
        /// <summary>How far the body steps in for an item, at most (m): one step.</summary>
        public const float PickStep = 0.65f;
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
                float u = _itemT < 1f ? _itemT * 0.7f : 0.7f + 0.3f * Mathf.Clamp01((Time.time - _liftDoneAt) / 0.3f);
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
            ReachHinge = Mathf.Max(0f, Springs.Damper(ReachHinge, ref _hingeVel, want, 0.13f, Time.deltaTime));
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
            Vector2 e = ReachHinge > 0f && _body && _body.HasArm ? _body.HingeHead(ReachHinge, _player.stoop) : Vector2.zero;
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

        /// <summary>Where the hand takes an item, as it lies now (its grip: the palm's point).</summary>
        Vector3 GripOf(WorldItem item) => item.transform.TransformPoint(Mirror(item.GripLocal));

        /// <summary>The hand's turn taking an item as it lies now.</summary>
        Quaternion GripTurnOf(WorldItem item)
        {
            Quaternion g = item.GripRotationLocal;
            return item.transform.rotation * Quaternion.LookRotation(Mirror(g * Vector3.forward), Mirror(g * Vector3.up));
        }

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
            float hingeTop = Mathf.Min(b.hingeMax, low ? LowHinge : MaxHinge);
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
                // (A light item the capsule passes through (WorldItem.IgnorePlayer) only has to stay clear of the feet, which
                // are behind the view: the eye may come within 0.15 m of it.)
                var cc = GetComponent<CharacterController>();
                var ic = item.GetComponentInChildren<Collider>();
                bool passes = cc && ic && Physics.GetIgnoreCollision(cc, ic);
                float r = passes ? 0.15f : (cc ? cc.radius : 0.3f) + ItemRadius(item) + 0.05f;
                Vector3 to = item.transform.position - transform.position; to.y = 0f;
                float along = Vector3.Dot(to, b.stepDir), side2 = to.sqrMagnitude - along * along;
                // (Less what the body runs on as it stops: it came 0.1 m further than planned and pushed a can 4 cm.)
                if (side2 < r * r) b.room = Mathf.Clamp(along - Mathf.Sqrt(r * r - side2) - 0.1f, 0f, b.room);
            }
            return b;
        }

        /// <summary>How far the item reaches out from its middle across the floor (m), from its colliders.</summary>
        static float ItemRadius(WorldItem item)
        {
            float r = 0.03f;
            foreach (var c in item.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                Vector3 e = c.bounds.extents, o = c.bounds.center - item.transform.position;
                o.y = 0f;
                r = Mathf.Max(r, o.magnitude + Mathf.Max(e.x, e.z));
            }
            return Mathf.Min(r, 0.3f);
        }

        /// <summary>The body's plan for a grip (the arm's way and the head clear), within share of the reach.</summary>
        bool PlanFor(Vector3 p, WorldItem item, Collider[] own, float share, out ReachPlan plan, out ReachBody b)
        {
            b = BodyNow(p, item);
            var body = b;
            return PlanReach(p, b, share, (c, s) => ArmWayClear(s, p, item, own) && HeadClear(body, c, item, null), out plan);
        }

        /// <summary>
        /// Can the body get its palm to this item with a step, a squat or a bend at the hips, the arm's way there and the
        /// head clear? (What the focus marks offer and a click takes: the same plan the body then carries out.)
        /// </summary>
        public bool BodyReaches(WorldItem item)
        {
            if (!item) return false;
            if (!_body || !_body.HasArm) return (GripOf(item) - Eye).magnitude <= itemRange;
            Vector3 p = GripOf(item);
            if ((p - ReachShoulder).magnitude > ArmReach + PickStep + 0.9f) return false;
            return PlanFor(p, item, null, PickReachShare, out _, out _);
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
            Vector3 g = GripOf(item);
            _planned = _body && _body.HasArm;
            if (_planned && !PlanFor(g, item, null, PickPlanShare, out _plan, out var b) && !PlanFor(g, item, null, PickReachShare + 0.03f, out _plan, out b))
            {
                // (Taken by code with the body unable to get there: it reaches as far as it can and gives up.)
                _plan = new ReachPlan { step = b.room, stoop = b.stoopMax * (g.y - b.feet < SquatBelow ? 1f : 0f), hinge = 0f, share = 1f };
            }
            if (_planned) BeginPlannedReach(g);
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
            if (palmError <= PalmNearGrip) { nearT += dt; return nearT > 0.15f; }
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
            Vector3 S = ReachShoulder;
            float reach = ArmReach;
            if (!_body || !_body.HasArm) { _pickHandAt = grip; return 1; }

            // The step: as far as planned, toward the grip, slowing as it gets there.
            Vector3 moved = transform.position - _pickStart; moved.y = 0f;
            float stepLeft = _plan.step - Vector3.Dot(moved, _pickStepDir);
            _pickHeldOffT = _pickStepping && _player.PlanarVelocity.magnitude < 0.08f && _pickT > 0.3f ? _pickHeldOffT + dt : 0f;
            if (_pickHeldOffT > 0.2f || moved.magnitude > _plan.step + 0.15f) _pickNoStep = true;
            if (!_pickNoStep && stepLeft > 0.03f)
            {
                Vector3 local = transform.InverseTransformDirection(_pickStepDir);
                _player.botInput = new Vector2(local.x, local.z).normalized * Mathf.Clamp(0.3f + stepLeft * 1.8f, 0.3f, 0.85f);
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
            bool bodyThere = stepDone && ReachHinge >= _hingeWant - 5f && _player.stoop >= _pickStoop - 0.06f;

            // The hand: bent while the body comes; then out to the grip (as far as planned, a little more), then further
            // if it gets no nearer.
            float within = bodyThere ? Mathf.Lerp(Mathf.Max(PickBent, _plan.share + 0.04f), PickStretch, _pickStretch) : PickBent;
            _pickHandAt = Plug.WithinComfort(grip, Vector3.zero, S, reach, within, out _pickClamped);

            float palmToGrip = (_body.PalmNow - grip).magnitude;
            bool solved = HandBlend >= 0.97f && _switchT >= 1f;
            if (PalmTakes(PalmError, _pickClamped, solved, ref _pickNearT, dt)) return 1;

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
            if (_pickStuckT > 1f || _pickT > 4f || (_pickBlockedT > 0.8f && _pickStuckT > 0.4f)) { PickGaveUp = true; return -1; }
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
