using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The player's free (right) hand: grab, drag, tip and shove movable props, hold items (see
    /// PropGrabber.Items.cs). The left hand keeps the camcorder; taking furniture hangs it on its strap and the left hand
    /// helps (PropGrabber.Furniture); R held turns what the hands hold.
    ///
    /// Hold left mouse on a prop within reach (step in automatically if it is a little too far). The hand
    /// takes the nearest grab edge to where you look (with a little variation, never the identical spot).
    /// While holding:
    ///  - mouse left/right swings your end of the prop sideways; past the arm's limit you turn with it
    ///  - mouse up/down lifts or lowers your end; lift far enough and a long thing tips up on its end
    ///  - WASD walks and drags it with you, slower the heavier it is
    ///  - right mouse shoves it away and lets go; release left mouse to let go
    /// The hand pulls with a limited, spring-like force at the grip point, so heavy things lag, scrape and
    /// pivot about whatever stays on the floor, and nothing goes through walls.
    /// Right mouse without holding gives a quick push. Walking into light props nudges them.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
    [DefaultExecutionOrder(90)]                 // after the player controller: the held item follows this frame's view
    public partial class PropGrabber : MonoBehaviour
    {
        [Header("Reach")]
        public float lookRange = 2.4f;
        [Tooltip("Arm reach from the shoulder to the palm (m); further and you step in.")] public float reach = 0.7f;
        [Tooltip("Shoulder relative to the eye, in the body's frame (x is mirrored to the working side).")] public Vector3 shoulderOffset = new Vector3(0.2f, -0.28f, -0.08f);
        [Tooltip("The free hand is the right one (the camcorder is in the left). Off: the old left-handed setup.")]
        public bool rightHanded = true;
        /// <summary>+1 when the free hand is the right one, -1 for the left.</summary>
        public float Side => rightHanded ? 1f : -1f;
        Vector3 ShoulderLocal => new Vector3(Mathf.Abs(shoulderOffset.x) * Side, shoulderOffset.y, shoulderOffset.z);

        [Header("Strength")]
        [Tooltip("Most the hand can pull or push sideways (N).")] public float maxPush = 650f;
        [Tooltip("Most the hand can lift (N).")] public float maxLift = 480f;
        [Tooltip("Hold stiffness (rad/s of the hand's spring).")] public float stiffness = 11f;
        [Tooltip("Props up to this mass (kg) are carried rather than dragged (boxes).")] public float carryMass = 12f;

        [Header("Controls")]
        [Tooltip("Metres the held end moves per mouse count.")] public float mouseToMetres = 0.0011f;
        [Tooltip("Mouse look left while holding (the rest of the mouse moves the prop).")] public float lookWhileHolding = 0.1f;
        [Tooltip("Shove impulse (N s), capped at 3.5 m/s of change.")] public float shove = 65f;

        [Header("View")]
        [Tooltip("0 full: held things in view, the hand on them. 1 low: held lower, at the bottom edge of the picture, the hand " +
                 "mostly out of shot. 2 off: no hand drawn (its shadow only). Camera menu > HANDHELD > Hands in view.")]
        public int handsInView = 1;
        /// <summary>The working hand is drawn in first person (Hands in view is not Off, and there is a body to draw).</summary>
        public bool HandsShown => handsInView != 2 && bodyMode != 2;

        [Tooltip("0 full body. 1 hands only: no legs or body drawn, the arm only while it does something. 2 none: nothing of " +
                 "the body, no arm reaches (things are taken from up to bodilessReach), a crosshair. Camera menu > HANDHELD > Body.")]
        public int bodyMode = 2;
        [Tooltip("With no body: how far from the shoulder things are taken and held (m).")] public float bodilessReach = 1.6f;
        /// <summary>No body (Body: None): nothing drawn, no arm to reach with.</summary>
        public bool Bodiless => bodyMode == 2;
        /// <summary>The body's arm does the reaching (there is one, and Body is not None).</summary>
        bool Armed => _body && _body.HasArm && bodyMode != 2;
        /// <summary>Where held things are carried for Hands in view (view space, m; x mirrored): lower and further out to the
        /// side, so the picture shows the top of what you hold.</summary>
        public Vector3 HandsInViewOffset => handsInView == 1 ? new Vector3(0.035f, -0.075f, 0.01f) : handsInView == 2 ? new Vector3(0.05f, -0.13f, 0f) : Vector3.zero;

        // Read by the first-person body (hand IK) and the HUD.
        public enum State { Idle, Reaching, Holding }
        public State Current { get; private set; }
        public MovableProp Held { get; private set; }
        /// <summary>How far the arm is out (0 resting, 1 at the grip), eased.</summary>
        public float HandBlend { get; private set; }
        public Vector3 HandPosition { get; private set; }
        /// <summary>Hand frame: forward = along the fingers, up = back of the hand.</summary>
        public Quaternion HandRotation { get; private set; }
        /// <summary>Finger curl 0 (flat) .. 1 (fist).</summary>
        public float HandCurl { get; private set; }
        /// <summary>0..1: how hard the arm is working.</summary>
        public float Strain { get; private set; }
        public Vector3 Shoulder => Eye + transform.rotation * ShoulderLocal;
        /// <summary>Where the shoulder would be standing upright: what the held position is measured from, so bending
        /// down to follow a low hand never moves the hand itself.</summary>
        public Vector3 StandShoulder => transform.position + Vector3.up * _player.BaseEye + transform.rotation * ShoulderLocal;
        Vector3 Eye => _player.cameraPitchPivot ? _player.cameraPitchPivot.position : transform.position + Vector3.up * 1.58f;

        FirstPersonBody _body;
        /// <summary>
        /// The shoulder the hand really reaches from: the body's own (its joint as the body stands and bends; the eye's
        /// fixed offset was 24 cm off it stooping), or the offset when there is no body.
        /// </summary>
        public Vector3 ReachShoulder => Armed ? _body.ShoulderNow : Shoulder;
        /// <summary>How far the palm reaches from <see cref="ReachShoulder"/>: the body's arm (0.6 m), or the reach setting.</summary>
        public float ArmReach => Armed ? _body.ArmReach : Bodiless ? bodilessReach : reach;
        /// <summary>The body's arm has the palm (or fingertip) where it was sent: within 2 cm. Nothing is taken before.</summary>
        public bool HandArrived => !Armed || _body.PalmError < 0.02f;
        /// <summary>How far the solved palm is from where it is wanted (m), or 0 with no body.</summary>
        public float PalmError => Armed ? _body.PalmError : 0f;
        float _notThereT, _stoopExtra;
        bool _botHeld, _itemStepping;
        Vector3 _itemReachAt;
        float _itemReachUntil = -1f;
        /// <summary>The held item wants the hand at a point this frame (a plug going to a socket): the body bends and steps toward it.</summary>
        public void ReachToward(Vector3 p, bool step = true) => ReachToward(p, step, 0f);

        /// <summary>
        /// As above; stepWithin above 0: the body steps in until p is within that much of the arm's reach from the shoulder,
        /// wherever the palm is (an item that keeps its own target within reach, a plug: the palm is always there, so
        /// "until the palm gets there" would never step).
        /// </summary>
        public void ReachToward(Vector3 p, bool step, float stepWithin) => ReachToward(p, step, stepWithin, 0.22f);

        /// <summary>As above; the body bends until the shoulder is about this far above p (m).</summary>
        public void ReachToward(Vector3 p, bool step, float stepWithin, float above)
        {
            _itemReachAt = p; _itemReachUntil = Time.time + 0.1f; _itemMayStep = step; _itemStepWithin = stepWithin; _itemReachAbove = above;
        }
        bool _itemMayStep;
        float _itemStepWithin, _itemReachAbove = 0.22f;
        bool ItemReaching => (HeldItem || _fetch) && Time.time < _itemReachUntil;
        /// <summary>0..1: the held item's grip opened (the hand going back out hand over hand along a cord).</summary>
        public float GripOpen { get; private set; }
        Vector3 _reelDir;
        float _reelUntil = -1f, _reelPhase, _reelW;
        /// <summary>Reeling a cord in this frame, the far end that way: the hand pulls hand over hand.</summary>
        public void ReelStroke(Vector3 towardFarEnd) { _reelDir = towardFarEnd; _reelUntil = Time.time + 0.1f; }
        /// <summary>Reeling: how far one pull back along the cord goes (m), and pulls a second.</summary>
        public const float ReelStrokeLength = 0.3f, ReelRate = 1.7f;
        float _reelPulled, _reelOut;
        /// <summary>How much cord the hand has pulled in since this was last asked (m): what the reel takes in.</summary>
        public float TakeReeled() { float r = _reelPulled; _reelPulled = 0f; return r; }

        /// <summary>
        /// Out with the arm but the palm not there (the IK cannot get it there): after a moment step in toward it and bend
        /// lower. Returns false once that has gone on too long (give up).
        /// </summary>
        bool ComeCloser(Vector3 grip, float maxTime)
        {
            if (HandBlend < 0.95f || HandArrived) { _notThereT = 0f; return true; }
            _notThereT += Time.deltaTime;
            if (_notThereT > 0.2f)
            {
                Vector3 local = transform.InverseTransformDirection(grip - ReachShoulder); local.y = 0f;
                var flat = new Vector2(local.x, local.z);
                _player.botInput = flat.magnitude > 0.12f ? flat.normalized * 0.45f : (Vector2?)null;
                if (grip.y < ReachShoulder.y - 0.2f) _stoopExtra = Mathf.MoveTowards(_stoopExtra, 0.4f, Time.deltaTime * 0.8f);
            }
            return _notThereT < maxTime;
        }

        // Automated tests (bypass the mouse).
        [System.NonSerialized] public bool? botHold;
        [System.NonSerialized] public Vector2 botMouse;
        [System.NonSerialized] public bool botShove;

        CamcorderPlayerController _player;
        HandheldCameraShake _shake;
        Camera _cam;
        readonly System.Random _rng = new System.Random();
        readonly RaycastHit[] _hits = new RaycastHit[8];

        MovableProp _target;
        Vector3 _gripLocal, _normalLocal, _edgeLocal;
        Vector3 _fingersLocal;           // which way the fingers lie on the prop (PlaceHand), or zero
        bool _hook;                      // they hook over an edge there
        Vector3 _offset;                 // wanted grip position: shoulder + yaw * _offset
        float _reachT, _blockedT, _slipT, _savedAngularDamping;
        Vector3 _force;
        float _lag;                      // metres the grip trails where the hand wants it

        void Awake()
        {
            _player = GetComponent<CamcorderPlayerController>();
            if (_player) _player.ClimbTooHigh += OnTooHigh;
        }

        void OnDestroy() { if (_player) _player.ClimbTooHigh -= OnTooHigh; }

        /// <summary>Space at a ledge too tall to climb: said in the hint line.</summary>
        void OnTooHigh(float height) => Note("TOO HIGH TO CLIMB");

        void Start()
        {
            _body = GetComponent<FirstPersonBody>();
            _cam = Camera.main;
            _shake = FindAnyObjectByType<HandheldCameraShake>();
        }

        void OnDisable() { Release(); if (HeldItem) Drop(); if (_fetch) EndFetch(); UpdatePalletStep(); }

        void Update()
        {
            var mouse = Mouse.current;
            bool focus = _player.HasFocus || botHold.HasValue;
            bool hold = botHold ?? (focus && mouse != null && mouse.leftButton.isPressed);
            // (A bot's press is the moment its hold starts, as a mouse's: held on, it took again and again what it could
            // not reach, and took a door's lever again mid-pull after letting go.)
            bool pressed = botHold.HasValue ? hold && !_botHeld && Current == State.Idle : focus && mouse != null && mouse.leftButton.wasPressedThisFrame;
            _botHeld = botHold.HasValue && hold;
            bool shovePressed = botShove || (focus && mouse != null && mouse.rightButton.wasPressedThisFrame);
            botShove = false;
            Vector2 md = botHold.HasValue ? botMouse : focus && mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            UpdateStrap();
            UpdateYawRate();
            bool stowing = UpdatePockets(focus) | UpdateBackpack(focus);

            bool free = !HeldItem && !_fetch && Current == State.Idle && _poke == PokePhase.None && !OnDoor && focus;
            AimedPoke = free ? LookPoke() : null;
            AimedItem = AimedPoke == null && free && LookItem(itemRange, out var aimed) ? aimed : null;
            _lastAimed = AimedItem;
            AimedDoorGrip = null;
            if (free && AimedPoke == null && !AimedItem)
            {
                var d = LookDoor(lookRange);
                if (d) AimedDoorGrip = d.GripPoint(d.SideOf(Eye), 0f);
            }
            // Something small in the hand that is not aimed at things (the hammer, the nails, the chicken), and a click on a prop
            // within reach: the hand puts it in its pocket and takes the prop (it comes back out on letting go).
            if (HeldItem && pressed && !stowing && TryPutInBag()) { }
            else if (HeldItem && pressed && !stowing && TryGrabWithItem()) { }
            else if (HeldItem) { if (!stowing) UpdateHeldItem(focus, mouse); }
            else switch (Current)
            {
                case State.Idle:
                    if (_fetch) { UpdateFetch(); break; }
                    if (_poke != PokePhase.None) break;
                    if (QuickDooring) { UpdateQuickDoor(hold); break; }
                    if (OnDoor) { UpdateDoor(hold); break; }
                    // (Round 11: a click on a door opens it, or shuts it if it is well open; held on, it is the careful way.)
                    if (_water != WaterPhase.None) break;
                    if (pressed) { if (!TryHandUse() && !TryStartPoke() && !TryPickUp() && !TryQuickDoor(true) && !TryTouchWater()) TryStart(); }
                    else if (shovePressed) { if (!TryQuickDoor()) Push(); }
                    break;
                case State.Reaching:
                    if (!hold || !_target) { Cancel(); break; }
                    UpdateReach();
                    break;
                case State.Holding:
                    if (!hold || !Held) { Release(!hold ? "let go" : "prop gone"); break; }
                    if (shovePressed) { ShoveAndRelease(); break; }
                    // Light things are carried, heavy ones pushed by the top or tipped from underneath (PropGrabber.Carry,
                    // PropGrabber.Furniture).
                    if (Carrying) UpdateCarry(md);
                    else if (Tipping) UpdateTip(md);
                    else UpdatePush(md);
                    break;
            }
            if (_poke != PokePhase.None) UpdatePoke();
            UpdatePokeFingers();
            UpdateWaterTouch();
            UpdateTouch(!Bodiless && !HeldItem && !_fetch && Current == State.Idle && _poke == PokePhase.None && !OnDoor && !Poking && !QuickDooring && _water == WaterPhase.None);
            // No climbing with the hands full of furniture or a door.
            _player.noMantle = Current != State.Idle || OnDoor || TwoHanded;
            UpdateCarriedWeight();
            UpdatePassing();
            UpdateCarryPassing();
            UpdateHint();
            UpdateStoop();
            UpdateReachHinge();
            UpdateHandPose();

            // Camcorder hand: dips while the other hand works, trembles with effort (not on its strap: no hand holds it).
            if (_shake && !HeldItem && CamcorderOnStrap) { _shake.dip = 0f; _shake.effort = 0f; }
            else if (_shake && !HeldItem)
            {
                float m = Held ? Held.Mass : 0f;
                _shake.dip = Current == State.Holding ? (CarryingPallet ? 0.12f : Mathf.Lerp(0.2f, 1f, Mathf.Clamp01((m - 4f) / 40f))) : Current == State.Reaching ? 0.15f : Poking ? 0.08f : 0f;
                _shake.effort = Current == State.Holding ? Strain * Mathf.Clamp01(m / 25f) : 0f;
            }
        }

        /// <summary>Bend down for low things: the shoulder goes no more than ~0.35 m above the hand.</summary>
        void UpdateStoop()
        {
            float want = 0f;
            if (Current != State.Idle && _target)
            {
                float handY = Current == State.Holding ? (StandShoulder + transform.rotation * _offset).y : _target.transform.TransformPoint(_gripLocal).y;
                want = Mathf.Clamp(StandShoulder.y - (handY + 0.35f), 0f, 0.8f);
            }
            // Picking something up: the squat the body planned for it (PlanReach: only for something low), held while the
            // hand goes out and closes on it, and straightened as it comes up.
            bool picking = _planned && (_fetch || (HeldItem && _itemT < 1f) || (_target && Current != State.Idle));
            if (picking) want = Mathf.Max(want, _pickStoop * (HeldItem && _itemT > 0f ? 1f - Ease(0.15f, 0.9f, _itemT) : 1f));
            // Putting an item down by hand (E): the squat its spot asks for (PropGrabber.Place).
            if (PlaceBends) { want = Mathf.Max(want, _placeStoop); picking = true; }
            // A held item taking the hand somewhere (a plug to a socket, an item off the floor): bend down for it and step
            // in when it is out of the arm's reach.
            if (ItemReaching)
            {
                // (The shoulder down to 0.22 m above it: 0.3 left a socket 0.28 m up out of reach, the arm straight.)
                want = Mathf.Max(want, Mathf.Clamp(StandShoulder.y - (_itemReachAt.y + _itemReachAbove), 0f, 0.85f));
                Vector3 to = _itemReachAt - ReachShoulder;
                Vector3 local = transform.InverseTransformDirection(to); local.y = 0f;
                var flat = new Vector2(local.x, local.z);
                // (Only while the palm is not there yet: stepping on through a plug's push left it 8 cm short.)
                bool far = _itemStepWithin > 0f ? to.magnitude > ArmReach * _itemStepWithin && flat.magnitude > 0.2f
                                                : PalmError > 0.03f && to.magnitude > ArmReach * 0.9f && flat.magnitude > 0.3f;
                if (_itemMayStep && far) { _player.botInput = flat.normalized * 0.45f; _itemStepping = true; }
                else if (_itemStepping) { _player.botInput = null; _itemStepping = false; }
            }
            else if (_itemStepping) { _player.botInput = null; _itemStepping = false; }
            // Further down while the palm cannot get there (ComeCloser).
            // (Round 13: not only once idle: held on to it, it kept the body down at the floor for the whole hold.)
            if (Current != State.Reaching && !OnDoor && !QuickDooring) _stoopExtra = Mathf.MoveTowards(_stoopExtra, 0f, Time.deltaTime * 0.5f);
            want = Mathf.Min(0.85f, want + _stoopExtra);
            if (LowPush) want = Mathf.Min(want, 0.3f);           // pushing a pallet: bent a little, not down at its edge
            // (Going down for an item, alongside the arm: 1.9 a second.)
            // (Round 11: 3.2 down and 2.6 up for an item: a squat for the floor took a third of a second, then as long again
            // to come up.)
            _player.stoop = Mathf.MoveTowards(_player.stoop, want, Time.deltaTime * (want > _player.stoop ? (picking ? 3.2f : 1.6f) : picking ? 2.6f : 1.0f));
        }

        // ---------------- Grab ----------------

        bool Look(float range, out RaycastHit hit, out MovableProp prop)
        {
            prop = null;
            var cam = _cam ? _cam.transform : transform;
            hit = default;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;   // not your own body
                best = _hits[i].distance; hit = _hits[i];
            }
            if (best == float.MaxValue) return false;
            prop = hit.collider.GetComponentInParent<MovableProp>();
            return prop;
        }

        void TryStart()
        {
            if (!Look(lookRange, out var hit, out var prop)) return;
            if (prop.Pinned) { Note("IT WON'T BUDGE"); return; }
            // (The edge the marks show, so the hand goes where they said.)
            prop.FindGrip(hit.point, hit.normal, Shoulder, _rng, prop == _markProp ? _markEdge : -1, out var grip, out var normal, out var edge, out _);
            // On the model, not the collider box: the hand placed on its real surface, fingers the best way round.
            if (prop.PlaceHand(grip, normal, Shoulder, out var palm, out var fingers, out bool hook))
            {
                grip = palm;
                _fingersLocal = prop.transform.InverseTransformDirection(fingers);
                _hook = hook;
            }
            else
            {
                grip = prop.OnSurface(grip, normal.normalized);
                _fingersLocal = Vector3.zero;
            }
            _target = prop;
            _gripLocal = prop.transform.InverseTransformPoint(grip);
            _normalLocal = prop.transform.InverseTransformDirection(normal);
            _edgeLocal = prop.transform.InverseTransformDirection(edge);
            _reachT = 0f; _blockedT = 0f;
            Current = State.Reaching;
            // Round 9: the body's way to it planned as for an item (a step, a squat for something low, a bend at the hips),
            // so the elbow is bent when the hand gets there (it reached with the arm straight, the elbow 3-4 degrees).
            ClearPlan();
            if (Armed)
            {
                _reachOwn = prop.GetComponentsInChildren<Collider>();
                if (PlanFor(grip, null, _reachOwn, PickPlanShare, out _plan, out _) || PlanFor(grip, null, _reachOwn, PickReachShare + 0.04f, out _plan, out _))
                    BeginPlannedReach(grip);
                else ClearPlan();
            }
        }
        Collider[] _reachOwn;

        void UpdateReach()
        {
            Vector3 grip = _target.transform.TransformPoint(_gripLocal);
            Vector3 toGrip = grip - ReachShoulder;
            _reachT += Time.deltaTime;
            if (_planned)
            {
                int r = ReachFor(grip, null, Time.deltaTime, _reachOwn);
                if (r > 0) { StopPickStep(); Attach(); }
                else if (r < 0) { StopPickStep(); GiveUpReach(grip); }
                return;
            }
            // Taken only once the palm is on it (it used to be taken from up to 0.37 m away, held from a distance). The
            // solved palm decides; the reach (a little short of what the shoulder's carry adds) only says when to step in.
            if (HandBlend > 0.85f && HandArrived && toGrip.magnitude <= ArmReach + 0.15f) { _player.botInput = null; Attach(); return; }
            if (toGrip.magnitude <= ArmReach)
            {
                if (!ComeCloser(grip, 2.5f)) GiveUpReach(grip);
                else if (_notThereT <= 0.2f) _player.botInput = null;
                return;
            }
            // A little too far: step in (gives up after a moment if something is in the way).
            if (_reachT > 3f || toGrip.magnitude > lookRange + 0.5f) { GiveUpReach(grip); return; }
            // Stepping in but held back (by the prop itself, a wall) with it still out of reach: the hand comes back
            // (the top of a sofa's back from in front of the seat is 1.2 m from the shoulder; it used to be taken from
            // 0.4 m away, then the arm stood out at full stretch for 3 s).
            _blockedT = _player.botInput.HasValue && _player.PlanarVelocity.magnitude < 0.1f && _reachT > 1.2f ? _blockedT + Time.deltaTime : 0f;
            if (_blockedT > 0.6f) { GiveUpReach(grip); return; }
            // Too far out in front: step in. Too far down: bend lower (the stoop alone stopped short of a sofa's seat).
            Vector3 local = transform.InverseTransformDirection(toGrip); local.y = 0f;
            var flat = new Vector2(local.x, local.z);
            _player.botInput = flat.magnitude > 0.25f ? flat.normalized * 0.6f : (Vector2?)null;
            if (grip.y < ReachShoulder.y - 0.25f) _stoopExtra = Mathf.MoveTowards(_stoopExtra, 0.45f, Time.deltaTime * 0.8f);
        }

        /// <summary>The body knocks the camcorder (a grab, a pick-up, a throw): the mic hears it.</summary>
        void MicThump()
        {
            if (!_chain) { var l = FindAnyObjectByType<AudioListener>(); if (l) _chain = l.GetComponent<Audio.CamcorderAudioChain>(); }
            if (_chain) _chain.HandlingImpulse();
        }
        Audio.CamcorderAudioChain _chain;

        void Attach()
        {
            MicThump();
            Held = _target;
            Held.Wake();
            Held.held = true;
            Current = State.Holding;
            _savedAngularDamping = Held.Body.angularDamping;
            Held.Body.angularDamping = Mathf.Max(_savedAngularDamping, Held.Mass > carryMass ? 1.2f : 3f);
            Vector3 grip = Held.transform.TransformPoint(_gripLocal);
            _offset = ClampOffset(Quaternion.Inverse(transform.rotation) * (grip - StandShoulder));
            _slipT = 0f;
            AttachFurniture();
        }

        /// <summary>
        /// The hand could not get onto it as planned: taken anyway if the palm is near enough (it closes the last bit as the
        /// fingers do: a grab never just does nothing), else "CAN'T REACH" (round 12: the owner found grabs that silently did
        /// nothing, at a pallet's edge and deep on a low shelf).
        /// </summary>
        void GiveUpReach(Vector3 grip)
        {
            _player.botInput = null;
            if (Vector3.Distance(grip, ReachShoulder) <= ArmReach + 0.35f) { Attach(); return; }
            Cancel();
            Note("CAN'T REACH");
        }

        void Cancel()
        {
            ClearPlan();
            StopPickStep();
            _notThereT = 0f;
            _player.botInput = null;
            _target = null;
            Current = State.Idle;
            ReturnStowed();
        }

        public void Release() => Release("let go");

        /// <summary>Why the last hold ended (for debugging).</summary>
        public string LastRelease { get; private set; } = "";

        public void Release(string reason)
        {
            if (Current == State.Holding) MicThump();
            if (Current == State.Holding) LastRelease = reason;
            if (_target && Current != State.Idle) { ClearPlan(); StopPickStep(); }
            if (Held)
            {
                Held.held = false;
                Held.Body.angularDamping = _savedAngularDamping;
            }
            ReleaseFurniture();
            Held = null;
            _target = null;
            if (_player)
            {
                _player.botInput = null;
                _player.lookScale = 1f; _player.pitchScale = 1f; _player.moveScale = 1f; _player.noRun = false;
            }
            Strain = 0f;
            Current = State.Idle;
            ReturnStowed();
        }

        void ShoveAndRelease()
        {
            var p = Held;
            Vector3 grip = p.transform.TransformPoint(_gripLocal);
            Release();
            Shove(p, grip, 1f);
        }

        void Push()
        {
            if (!Look(1.7f, out var hit, out var prop)) return;
            Shove(prop, hit.point, 0.6f);
        }

        void Shove(MovableProp p, Vector3 at, float strength)
        {
            p.Wake();
            var cam = _cam ? _cam.transform : transform;
            Vector3 dir = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized + Vector3.up * 0.08f;
            float j = Mathf.Min(shove * strength, p.Mass * 3.5f);
            p.Body.AddForceAtPosition(dir.normalized * j, at, ForceMode.Impulse);
        }

        // ---------------- Holding ----------------

        Vector3 ClampOffset(Vector3 o)
        {
            // Measured from the upright shoulder: anywhere from the floor (the body stoops) to a little above
            // the shoulder, and within an arm.s length around the body.
            float floor = transform.position.y + 0.03f - StandShoulder.y;
            // Further out on the hand's own side than across the body.
            o.x = Side > 0f ? Mathf.Clamp(o.x, -0.42f, 0.62f) : Mathf.Clamp(o.x, -0.62f, 0.42f);
            o.y = Mathf.Clamp(o.y, floor, 0.5f);
            o.z = Mathf.Clamp(o.z, CamcorderOnStrap ? 0.38f : 0.15f, 0.8f);   // (on the strap: kept out from the chest camera)
            float flat = Mathf.Sqrt(o.x * o.x + o.z * o.z);
            if (flat > 0.82f) { o.x *= 0.82f / flat; o.z *= 0.82f / flat; }
            return o;
        }

        void UpdateHold(Vector2 md)
        {
            float k = mouseToMetres * _player.FovMultiplier;
            var want = _offset + new Vector3(md.x * k, md.y * k, 0f);
            // (Pushing: the hands stay where they took hold. Clamped, the target was pulled toward the shoulder off the grip,
            // and a couch held by its far side was swung round by it and spun, round 13.)
            var clamped = Pushing ? want : ClampOffset(want);
            // Within the arm's reach from the real shoulder: the prop comes to the hand, not the hand stretching after it.
            {
                // (A little past the reach, as far as the arm took it: at 0.97 of it, a sofa taken by its seat at full
                // stretch was pulled 4 cm in under the player's feet, who rode up onto it.)
                Vector3 T = StandShoulder + transform.rotation * clamped, RS = ReachShoulder;
                // (Pushing: the arm kept bent, so it has room to push ahead as you walk. A pallet, at your feet: not kept
                // within the arm at all, or the hand pulled it up toward the shoulder instead of pushing it.)
                float r = ArmReach + 0.05f;
                if (!Pushing && (T - RS).sqrMagnitude > r * r) clamped = Quaternion.Inverse(transform.rotation) * (RS + Vector3.ClampMagnitude(T - RS, r) - StandShoulder);
            }
            // Past the arm's limit sideways the body turns with it (so you can keep swinging it round);
            // past the lift limit the view follows.
            float overX = (want.x - clamped.x) / k, overY = (want.y - clamped.y) / k;
            if (Mathf.Abs(overX) > 0f || Mathf.Abs(overY) > 0f)
                _player.AddLook(overX * _player.mouseSensitivity * 0.5f, -overY * _player.mouseSensitivity * 0.3f);
            _offset = clamped;

            float m = Held.Mass;
            _player.lookScale = lookWhileHolding;
            _player.noRun = true;
            float heavy = Mathf.Clamp01(1f - m / 110f);
            float scale = Mathf.Lerp(0.4f, 1f, heavy) * (1f - 0.5f * Mathf.Clamp01(_lag / 0.4f));
            // Pulling away from something stuck (the hand is well behind where it should be): you can barely move.
            Vector3 grip = Held.transform.TransformPoint(_gripLocal);
            Vector3 away = transform.position - grip; away.y = 0f;
            if (_lag > 0.3f && Vector3.Dot(_player.PlanarVelocity, away) > 0f) scale *= 0.3f;
            _player.moveScale = Mathf.Max(0.12f, scale);

            // Keep the hand in view: the camera eases toward the grip when it drifts far off centre (not pushing: you look
            // over it at where it goes).
            if (_cam && !Pushing)
            {
                Vector3 to = grip - Eye;
                float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - 12f;
                float diff = wantPitch - _player.Pitch;
                if (Mathf.Abs(diff) > 14f) _player.AddLook(0f, Mathf.Sign(diff) * Mathf.Min(Mathf.Abs(diff) - 14f, 60f * Time.deltaTime));
            }

            // Lost grip: the prop got stuck or was pulled out of reach, or the hand cannot stay on it.
            float d = Vector3.Distance(grip, ReachShoulder);
            // (With no body the hold has more give: nothing shows an arm at full stretch, and a heavy thing dragged lags.)
            bool off = LowPush ? d > ArmReach + 0.9f : d > ArmReach + (Bodiless ? 0.65f : 0.15f) || PalmError > 0.1f;
            // (Not while the hands work it round: the grip lags the turn a moment.)
            _slipT = off ? _slipT + Time.deltaTime : 0f;
            if (_slipT > 0.4f) Release($"slipped (grip {d:F2} m from the shoulder, palm {PalmError * 100f:F0} cm off it)");
        }

        void FixedUpdate()
        {
            if (Current != State.Holding || !Held) return;
            var rb = Held.Body;
            if (rb.isKinematic) Held.Wake();
            if (Carrying) { CarryForces(rb); return; }
            if (Tipping) { TipForces(rb); return; }
            Vector3 T = StandShoulder + transform.rotation * _offset;
            Vector3 T0 = T;
            // Pushing: the hand goes ahead of you the way you walk (the body alone stops against it), and pushes at its
            // balance point's height so it slides and turns rather than tipping over.
            // (Not back toward you: walking back, the hand just holds on and it follows; led back, it was pulled into
            // the legs and the body stepped up onto it.)
            _pushLead = Vector3.MoveTowards(_pushLead, Pushing ? new Vector3(_player.MoveInput.x, 0f, Mathf.Max(0f, _player.MoveInput.y)) * pushLead : Vector3.zero, Time.fixedDeltaTime * 2f);
            if (_pushLead.sqrMagnitude > 1e-6f)
            {
                T += transform.rotation * _pushLead;
                Vector3 RS = ReachShoulder;
                // (The lead never takes the hands further than the arm, or than where they hold it now: clamped to the arm
                // alone, a grip taken at full stretch was pulled in, off the grip, and the couch swung round.)
                float r = Mathf.Max(ArmReach + 0.05f, (T0 - RS).magnitude);
                if (!LowPush && (T - RS).sqrMagnitude > r * r) T = RS + Vector3.ClampMagnitude(T - RS, r);
            }
            Vector3 G = rb.position + rb.rotation * _gripLocal;
            Vector3 low = new Vector3(G.x, rb.worldCenterOfMass.y, G.z);
            Vector3 vG = rb.GetPointVelocity(G);
            Vector3 vT = _player.PlanarVelocity;
            float m = rb.mass;
            float mEff = Mathf.Min(m, 30f), w = stiffness;
            Vector3 F = mEff * (w * w * (T - G) + 2f * w * (vT - vG));
            // (Each part sized to the weight felt where it is applied: the push at the balance height, the hands' hold up
            // or down at the grip.)
            F = AtPointMass(rb, low, new Vector3(F.x, 0f, F.z), mEff) + AtPointMass(rb, G, Vector3.up * F.y, mEff);
            // A hand is only so strong.
            Vector3 h = new Vector3(F.x, 0f, F.z);
            if (h.magnitude > maxPush) h = h.normalized * maxPush;
            // (Pushing, the hands only steady it up and down, unless the mouse lifts your end: then up to all they have.)
            float up = Mathf.Clamp(F.y, -200f, maxLift * PushLiftShare);
            F = h + Vector3.up * up;
            rb.AddForceAtPosition(h, low); rb.AddForceAtPosition(Vector3.up * up, G);
            _force = F;
            FurnitureForces(rb, T);
            float err = Vector3.Distance(T, G);
            _lag = err;
            Strain = Mathf.Clamp01(Mathf.Max(err / 0.3f, F.magnitude / (maxPush * 1.1f)));
        }

        // ---------------- What the fingers touch ----------------

        Collider[] _targetColliders;
        MovableProp _contactProp;

        /// <summary>The hand holds or touches something its fingers should stop at.</summary>
        public bool HasContact => HeldItem || _fetch || (OnDoor && _door) || (QuickDooring && _quickDoor) || (_target && (Current == State.Holding || Current == State.Reaching)) || TouchingWall;

        /// <summary>
        /// Distance from a point to the surface of what the hand holds (negative or zero inside it): a held item's own
        /// shape, a door's lever, the furniture's colliders, the wall the hand rests on. Large if there is nothing.
        /// </summary>
        public float ContactDistance(Vector3 p)
        {
            if (HeldItem) return HeldItem.ContactDistanceWorld(p);
            if (_fetch) return _fetch.ContactDistanceWorld(p);
            if (OnDoor && _door) return _door.LeverDistance(_doorSide, _doorTurn, p);
            if (QuickDooring && _quickDoor) return _quickLever ? _quickDoor.LeverDistance(_quickSide, 1f, p) : _quickDoor.SlabDistance(p);
            if (_target && (Current == State.Holding || Current == State.Reaching))
            {
                // Its shape where it is drawn (MovableProp.ShapeDistance), not its colliders a physics step ahead.
                if (_target.Shape != null) return _target.ShapeDistance(p);
                if (_contactProp != _target) { _contactProp = _target; _targetColliders = _target.GetComponentsInChildren<Collider>(); }
                return CollidersDistance(_targetColliders, p);
            }
            if (TouchingWall) return CollidersDistance(_touchColliders, p);
            return float.MaxValue;
        }

        internal static float CollidersDistance(Collider[] cols, Vector3 p)
        {
            float best = float.MaxValue;
            if (cols == null) return best;
            foreach (var c in cols)
            {
                if (!c || !c.enabled || c.isTrigger) continue;
                if (c is MeshCollider mc && !mc.convex) continue;
                float d;
                if (c is BoxCollider b)
                {
                    // A box: how far inside too (ClosestPoint only says "inside", so a finger 1 mm in and one 3 cm in
                    // looked the same, and a hand on a wall could not tell which way out was least in).
                    Vector3 s = b.transform.lossyScale;
                    s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                    Vector3 q = Vector3.Scale(b.transform.InverseTransformPoint(p) - b.center, s);
                    d = WorldItem.BoxDistance(q, Vector3.zero, Vector3.Scale(b.size * 0.5f, s));
                }
                else
                {
                    Vector3 q = Physics.ClosestPoint(p, c, c.transform.position, c.transform.rotation);
                    d = (q - p).sqrMagnitude < 1e-12f ? -0.001f : Vector3.Distance(q, p);
                }
                if (d < best) best = d;
            }
            return best;
        }

        // ---------------- Hand pose (for the body / IK) ----------------

        void UpdateHandPose()
        {
            // (A quick door's hand once it has let go fades out quickly: it was at full blend 0.15 s with the palm off the lever.)
            bool letGo = _quick == QuickPhase.Back || Time.time < _letGoUntil;
            bool active = (Current != State.Idle && _target) || Poking || HeldItem || _fetch || OnDoor || _touching || _water != WaterPhase.None || (QuickDooring && !letGo);
            // (Going out for an item, a little slower: it was there in 0.3 s, and the grab at 0.43 s looked snatched.)
            // (Round 11: out in about 0.22 s, back in 0.3: 2.2 a second made every reach start with half a second of the arm
            // coming up.)
            bool goingFor = _fetch || (HeldItem && _itemT <= 0f) || (_planned && Current == State.Reaching);
            // (No body: nothing to watch reach out, so it takes hold at once.)
            HandBlend = Mathf.MoveTowards(HandBlend, active ? 1f : 0f, Time.deltaTime * (Bodiless ? 14f : active ? (goingFor ? 4.5f : 4.5f) : letGo ? 5f : 3.4f));
            // (Hand over hand on a cord: the grip opens going out and closes on the pull, as the fingers do. Going out to an
            // item it waits for, the hand is open; it closes once the palm is on it.)
            if (HeldItem)
            {
                float want = ItemWaitingForHand && !_graspOn ? PickOpenCurl : HeldItem.GripCurl * (1f - 0.6f * GripOpen);
                HandCurl = Mathf.MoveTowards(HandCurl, want, Time.deltaTime * (_graspOn && _itemT <= 0f ? 11f : 6f));
                return;                                                                                           // pose set in LateUpdate
            }
            if (_fetch) { FetchHandPose(); return; }
            if (Poking) PokePose = true; else if (HandBlend <= 0.01f) PokePose = false;
            if (OnDoor && _door) { PokePose = false; DoorHandPose(); return; }
            if (_touching) { PokePose = false; TouchHandPose(); return; }
            if (_water != WaterPhase.None) { PokePose = false; WaterHandPose(); return; }
            if (QuickDooring && _quickDoor) { PokePose = false; QuickDoorPose(); return; }
            // A door's lever just let go: the hand stays where it was from the shoulder while it fades (left at the lever, the
            // arm went out straight after it as the body walked on).
            if (!active && Time.time < _letGoUntil) { HandPosition = ReachShoulder + _letGoOff; HandCurl = Mathf.MoveTowards(HandCurl, 0.3f, Time.deltaTime * 4f); return; }
            if (!_target) { HandCurl = Mathf.MoveTowards(HandCurl, 0.25f, Time.deltaTime * 2f); return; }
            Vector3 grip = _target.transform.TransformPoint(_gripLocal);
            Vector3 n = _target.transform.TransformDirection(_normalLocal).normalized;
            Vector3 edge = _target.transform.TransformDirection(_edgeLocal);
            Vector3 fingers;
            if (_fingersLocal.sqrMagnitude > 0.5f)
            {
                // Chosen on the model's surface when the hand went for it (PlaceHand).
                fingers = Vector3.ProjectOnPlane(_target.transform.TransformDirection(_fingersLocal), n).normalized;
            }
            else if (edge.sqrMagnitude > 0.01f)
            {
                // Across the edge, pointing away from the body: palm on top, fingers over the far side.
                fingers = Vector3.Cross(n, edge).normalized;
                if (Vector3.Dot(fingers, grip - Shoulder) < 0f) fingers = -fingers;
            }
            else fingers = Vector3.ProjectOnPlane(grip - Shoulder, n).normalized;
            if (fingers.sqrMagnitude < 0.01f) fingers = transform.forward;
            // (Reaching with a plan: bent while the body comes, then on it: PropGrabber.ReachFor.)
            HandPosition = Current == State.Reaching && _planned ? _pickHandAt : grip;
            HandRotation = Quaternion.LookRotation(fingers, n);
            // Fingers hook over an edge only where they reach past the outside of the prop; resting across
            // its top (the near edge) they lie nearly flat instead of sinking into it.
            Vector3 centre = _target.Body ? _target.Body.worldCenterOfMass : _target.transform.position;
            bool overOutside = Vector3.Dot(fingers, Vector3.ProjectOnPlane(centre - grip, n)) < 0f;
            float hook = _fingersLocal.sqrMagnitude > 0.5f ? (_hook ? 0.75f : 0.12f) : edge.sqrMagnitude > 0.01f && overOutside ? 0.75f : 0.12f;
            float curl = Current == State.Holding ? hook + 0.15f * Strain : 0.2f;
            HandCurl = Mathf.MoveTowards(HandCurl, curl, Time.deltaTime * 3f);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Walking into a door that is not latched pushes it along.
            var door = hit.collider ? hit.collider.GetComponentInParent<Door>() : null;
            if (door)
            {
                if (door != _door && !TryBash(door, hit)) door.Nudge(hit.point, hit.moveDirection * _player.PlanarVelocity.magnitude);
                return;
            }
            // Walking into light props nudges them along.
            var p = hit.rigidbody ? hit.rigidbody.GetComponent<MovableProp>() : null;
            if (!p || p == Held || p == _target || p.Mass > 25f) return;     // not what the hand is reaching for
            Vector3 dir = hit.moveDirection; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f || _player.PlanarVelocity.sqrMagnitude < 0.04f) return;
            p.Wake();
            p.Body.AddForceAtPosition(dir.normalized * Mathf.Min(140f, p.Mass * 12f) * _player.PlanarVelocity.magnitude, hit.point);
        }

        void OnDrawGizmosSelected()
        {
            if (Current == State.Idle || !_player) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Shoulder + transform.rotation * _offset, 0.04f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Shoulder, HandPosition);
            Gizmos.DrawRay(HandPosition, _force * 0.002f);
        }
    }
}
