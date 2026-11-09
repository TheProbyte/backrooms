using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Carrying boxes (2026-11-08, Docs/GameDesign.md "Carrying boxes"; the owner: "the main next thing you do is pick up
    /// boxes and move them ... must feel satisfying"). As the box-moving games do it (Supermarket Simulator's click to take,
    /// click to put; Death Stranding's sway and weight), here:
    /// <list type="bullet">
    /// <item><b>Click a box to take it</b> (up to <see cref="BoxReach"/> from the eye: no walking over, the body bends
    /// quickly for a low one and the box comes up into the hands in about a third of a second; one on a high shelf slides
    /// off toward you). Taken from a stack, it is the top one.</item>
    /// <item><b>Carried low at the hips</b> (PropGrabber.Carry), bobbing a little as you walk and lagging a turn; the heavier
    /// the load, the slower you go. A wall stops a box but never knocks it out of your hands.</item>
    /// <item><b>A few in your arms:</b> hold the button on another box and it goes on top of what you carry, one more every
    /// <see cref="TakeEvery"/> while held (so holding on a stack takes its top few), up to <see cref="ArmLoad"/> kg and
    /// <see cref="ArmHeight"/> m. Turn too fast and the top one falls off.</item>
    /// <item><b>Putting down:</b> a see-through outline shows where the top box goes where you look (a shelf's deck under
    /// where you aim, the floor, another box's top), lined up with what it stands on and flush with an edge or a box beside
    /// it when close (a click is heard); R turns it a quarter turn; a click puts it there. Q sets it down at your feet;
    /// held, Q winds up a gentle toss. Right click does nothing to a box (it flung them spinning).</item>
    /// <item>It lands with a soft settle: a tiny squash, a puff of dust and its thud (ImpactAudio).</item>
    /// </list>
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>How far from the eye a box is taken (m).</summary>
        public const float BoxReach = 2.4f;
        /// <summary>The most the arms carry (kg) and how high a stack in them goes (m).</summary>
        public const float ArmLoad = 20f, ArmHeight = 0.75f;
        /// <summary>Holding the button this long on a box takes it too (s), and another each time again.</summary>
        const float TakeEvery = 0.3f;
        const float PlaceReach = 2.6f;
        static readonly Vector2 BoxTossSpeed = new Vector2(2.2f, 5.5f);
        const float BoxTossFull = 0.8f;

        /// <summary>Carrying a box (with any on top of it).</summary>
        public bool CarryingBox => Carrying && Held && Held.Spec.kind == MovableKind.Box;

        /// <summary>A box on top of the one carried.</summary>
        sealed class ArmBox
        {
            public MovableProp p;
            public Quaternion turn;                 // its turn on the stack (a quarter turn of the carry's)
            public float t;                         // 0..1 coming up onto the stack
            public Vector3 fromPos;                 // where it was taken from, in the body's frame
            public Quaternion fromRot;
            public Collider[] cols;
            public float savedDamping;
        }
        readonly List<ArmBox> _arm = new List<ArmBox>();

        /// <summary>Boxes in the arms now.</summary>
        public int BoxesInArms => CarryingBox ? 1 + _arm.Count : 0;
        /// <summary>Their weight (kg).</summary>
        public float ArmWeight { get { float m = Held ? Held.Mass : 0f; foreach (var a in _arm) if (a.p) m += a.p.Mass; return m; } }
        float ArmStackHeight { get { float h = Held ? Held.Spec.size.y : 0f; foreach (var a in _arm) if (a.p) h += a.p.Spec.size.y; return h; } }
        /// <summary>The box that goes down next: the top one.</summary>
        MovableProp TopBox => _arm.Count > 0 ? _arm[_arm.Count - 1].p : Held;
        bool InArms(MovableProp p) { if (p == Held) return true; foreach (var a in _arm) if (a.p == p) return true; return false; }
        /// <summary>A shelf kit's crate: KitPlacer's outline shows where its shelf goes; set down at your feet.</summary>
        bool KitCrate => Held && KitPlacer.CarryHint(Held) != null;

        float _lmbDownAt = -1f, _nextTakeAt;
        bool _pressUsed;
        float _qBoxDown = -1f;
        /// <summary>0..1 while Q winds up a toss of the top box.</summary>
        public float BoxTossCharge { get; private set; }
        int _placeTurn;

        // The outline.
        bool _outShown, _outValid, _outFlush;
        Vector3 _outPos, _outSize;
        Quaternion _outRot;
        float _flushClickAt;
        static Material s_outGood, s_outFlush, s_outBad;
        static Mesh s_cube;

        // The feel: the quick bend for a low box, the dip as a heavy one comes up, the sway.
        float _quickStoop, _quickStoopT = -1f, _quickStoopDur = 0.3f;
        float _liftDipT = -1f, _liftDipAmt;
        bool _dipped;
        Vector3 _carrySway, _carrySwayVel, _lastPlanar;
        float _boxBob, _turnLag, _turnLagVel, _fastTurnT;
        float _carrySwing = CarrySwingTime;
        /// <summary>How far the carried box goes down as the stack on it grows (m), so its top stays under the eye.</summary>
        float _stackLower, _stackLowerVel;

        static readonly Collider[] s_boxOver = new Collider[32];
        static readonly RaycastHit[] s_boxHits = new RaycastHit[16];

        // ---------------- Taking ----------------

        /// <summary>A click on a light box within reach: take it at once (its stack's top one).</summary>
        bool TryQuickCarry()
        {
            if (!Look(BoxReach, out _, out var prop) || !prop || !BoxTakeable(prop)) return false;
            prop = TopOf(prop);
            ClearPlan();
            _target = prop;
            _gripLocal = Vector3.zero; _normalLocal = Vector3.up; _edgeLocal = Vector3.zero; _fingersLocal = Vector3.zero;
            Attach();                                          // (AttachFurniture: StartCarry)
            if (!CarryingBox) return true;
            _lmbDownAt = Time.time; _pressUsed = true; _nextTakeAt = Time.time + TakeEvery;
            _placeTurn = 0;
            Bend(prop);
            BoxSounds.PickUp(prop.transform.position, prop.Mass);
            return true;
        }

        bool BoxTakeable(MovableProp p) => p && p.Spec.kind == MovableKind.Box && IsLight(p) && !p.Pinned && !p.NetRemote && !p.held && p.Shape != null;

        /// <summary>The body's quick bend for a box low down (the lower, the deeper), and the dip as a heavy one comes up.</summary>
        void Bend(MovableProp p)
        {
            float bottom = p.WorldBounds.min.y - transform.position.y;
            _quickStoop = Mathf.Clamp(0.5f - bottom * 0.65f, 0f, 0.5f);
            _quickStoopT = _quickStoop > 0.02f ? 0f : -1f;
            _quickStoopDur = _carrySwing;
            _dipped = false;
        }

        /// <summary>The top box of the column a box stands in (boxes resting squarely on it, and on those).</summary>
        MovableProp TopOf(MovableProp p)
        {
            for (int guard = 0; guard < 12; guard++)
            {
                var up = OnTopOf(p);
                if (!up) return p;
                p = up;
            }
            return p;
        }

        MovableProp OnTopOf(MovableProp p)
        {
            var s = p.Spec.size;
            var c = p.transform.TransformPoint(new Vector3(0f, s.y + 0.07f, 0f));
            var half = new Vector3(Mathf.Max(0.02f, s.x * 0.5f - 0.05f), 0.05f, Mathf.Max(0.02f, s.z * 0.5f - 0.05f));
            int n = Physics.OverlapBoxNonAlloc(c, half, s_boxOver, p.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = s_boxOver[i].attachedRigidbody;
                var o = rb ? rb.GetComponent<MovableProp>() : null;
                if (o && o != p && BoxTakeable(o) && !InArms(o)) return o;
            }
            return null;
        }

        /// <summary>Holding the button on another box while carrying: it goes on top, if the arms can take it.</summary>
        bool TryTakeAnother(out string why)
        {
            why = null;
            // (Past the boxes in the arms: looking down at a box on the floor, the view met the one carried first.)
            var cam = _cam ? _cam.transform : transform;
            if (!BoxRay(cam.position, cam.forward, BoxReach, out var hit)) return false;
            var prop = hit.collider.GetComponentInParent<MovableProp>();
            if (!prop || InArms(prop) || !BoxTakeable(prop)) return false;
            prop = TopOf(prop);
            if (InArms(prop)) return false;
            if (Improvements.IsKit(prop.Spec.id) || Improvements.IsKit(Held.Spec.id)) { why = "ONE CRATE AT A TIME"; return false; }
            if (ArmWeight + prop.Mass > ArmLoad + 0.01f) { why = "TOO HEAVY TO TAKE MORE"; return false; }
            if (ArmStackHeight + prop.Spec.size.y > ArmHeight + 0.01f) { why = "THAT'S AS HIGH AS YOU CAN CARRY"; return false; }
            var top = TopBox.Spec.size;
            if (Mathf.Max(prop.Spec.size.x, prop.Spec.size.z) > Mathf.Max(top.x, top.z) + 0.12f) { why = "TOO BIG TO GO ON TOP"; return false; }
            AddToArm(prop);
            return true;
        }

        void AddToArm(MovableProp p)
        {
            p.Wake();
            p.held = true;
            var a = new ArmBox { p = p, savedDamping = p.Body.angularDamping, cols = p.GetComponentsInChildren<Collider>() };
            var inv = Quaternion.Inverse(transform.rotation);
            a.fromPos = inv * (p.Body.position - transform.position);
            a.fromRot = inv * p.Body.rotation;
            // Turned on the stack the nearest quarter turn to how it is turned now.
            var carryWorld = transform.rotation * _carryRot;
            float rel = Mathf.DeltaAngle((carryWorld).eulerAngles.y, p.Body.rotation.eulerAngles.y);
            a.turn = Quaternion.Euler(0f, Mathf.Round(rel / 90f) * 90f, 0f);
            p.Body.useGravity = false;
            p.Body.angularDamping = Mathf.Max(a.savedDamping, 3f);
            // Not against the body, nor the boxes it rides with.
            if (!_cc) _cc = GetComponent<CharacterController>();
            foreach (var c in a.cols)
            {
                if (!c) continue;
                if (_cc) Physics.IgnoreCollision(_cc, c, true);
                IgnoreWithArms(c, true);
            }
            _arm.Add(a);
            Bend(p);
            BoxSounds.PickUp(p.transform.position, p.Mass);
        }

        /// <summary>A collider against every box in the arms (the carried one and those on it).</summary>
        void IgnoreWithArms(Collider c, bool ignore)
        {
            if (_carryColliders != null) foreach (var o in _carryColliders) if (o && o != c) Physics.IgnoreCollision(c, o, ignore);
            foreach (var a in _arm) if (a.cols != null) foreach (var o in a.cols) if (o && o != c) Physics.IgnoreCollision(c, o, ignore);
        }

        /// <summary>A box off the stack in the arms: solid to the others again (and to the body once clear of it).</summary>
        void RemoveFromArm(ArmBox a, bool gravity)
        {
            _arm.Remove(a);
            if (!a.p) return;
            foreach (var c in a.cols) if (c) IgnoreWithArms(c, false);
            if (a.cols != null) _letGo.Add((a.cols, a.p));
            a.p.held = false;
            a.p.Body.useGravity = gravity;
            a.p.Body.angularDamping = a.savedDamping;
        }

        /// <summary>Every box on top falls (the carry ended some other way).</summary>
        void DropArmBoxes()
        {
            for (int i = _arm.Count - 1; i >= 0; i--) RemoveFromArm(_arm[i], true);
        }

        // ---------------- Each frame ----------------

        /// <summary>The buttons while carrying a box: click to put down, hold on a box to take it too, Q, R.</summary>
        void UpdateBoxInput(bool focus, bool pressed, bool hold)
        {
            var kb = Keyboard.current;
            if (pressed) { _lmbDownAt = Time.time; _pressUsed = false; _nextTakeAt = Time.time + TakeEvery; }
            if (hold && _lmbDownAt >= 0f && Time.time >= _nextTakeAt)
            {
                _nextTakeAt = Time.time + TakeEvery;
                if (TryTakeAnother(out string why)) _pressUsed = true;
                else if (why != null && !_pressUsed) { Note(why); _pressUsed = true; }
            }
            if (!hold && _lmbDownAt >= 0f)
            {
                _lmbDownAt = -1f;
                if (!_pressUsed) PutDownTop();
                _pressUsed = false;
                if (!CarryingBox) return;
            }
            if (focus && kb != null && kb.rKey.wasPressedThisFrame && !KitCrate) _placeTurn = (_placeTurn + 1) & 3;
            // Q: a tap sets the top box down at your feet; held, a gentle toss on letting go.
            if (focus && kb != null && kb.qKey.wasPressedThisFrame) _qBoxDown = Time.time;
            if (_qBoxDown >= 0f)
            {
                float q = Time.time - _qBoxDown;
                BoxTossCharge = q > throwHold ? Mathf.Clamp01((q - throwHold) / BoxTossFull) : 0f;
                if (kb == null || !kb.qKey.isPressed)
                {
                    float charge = BoxTossCharge;
                    _qBoxDown = -1f; BoxTossCharge = 0f;
                    if (q <= throwHold) DropTopAtFeet(); else TossTop(Mathf.Max(0.05f, charge));
                }
            }
        }

        /// <summary>The click: the top box to where the outline is.</summary>
        void PutDownTop()
        {
            if (KitCrate) { DropTopAtFeet(); return; }
            if (!_outShown) { Note("LOOK WHERE TO PUT IT"); return; }
            if (!_outValid) { Note("NO ROOM THERE"); return; }
            SetDown(TopBox, _outPos, _outRot, 0.22f);
        }

        /// <summary>The top box out of the arms (the carry ends with the last).</summary>
        void LetGoOfTop(MovableProp box, bool gravity)
        {
            if (box == Held) Release("put down");
            else foreach (var a in _arm) if (a.p == box) { RemoveFromArm(a, gravity); break; }
        }

        void SetDown(MovableProp box, Vector3 pos, Quaternion rot, float dur)
        {
            if (!box) return;
            LetGoOfTop(box, true);
            BoxSettle.Begin(box, pos, rot, dur);
            _placeTurn = 0;
        }

        /// <summary>Q tapped: the top box set down on the floor just in front of your feet (or let go, if there is no room).</summary>
        void DropTopAtFeet()
        {
            var box = TopBox;
            if (!box) return;
            var s = box.Spec.size;
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float yaw = Mathf.Round(transform.eulerAngles.y / 90f) * 90f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float depth = Mathf.Abs(Vector3.Dot(rot * Vector3.forward, fwd)) * s.z + Mathf.Abs(Vector3.Dot(rot * Vector3.right, fwd)) * s.x;
            var at = transform.position + fwd * (0.32f + depth * 0.5f);
            if (DownTo(at + Vector3.up * 0.6f, 1.6f, out var floor) && PoseClear(box, floor, rot)) { SetDown(box, floor, rot, 0.2f); return; }
            var v = box.Body.linearVelocity;
            LetGoOfTop(box, true);
            box.Body.linearVelocity = v * 0.5f;
        }

        /// <summary>Q held and let go: the top box tossed gently along the view, harder the longer Q was held.</summary>
        void TossTop(float charge)
        {
            var box = TopBox;
            if (!box) return;
            LetGoOfTop(box, true);
            var cam = _cam ? _cam.transform : transform;
            float speed = Mathf.Lerp(BoxTossSpeed.x, BoxTossSpeed.y, charge) * Mathf.Lerp(1f, 0.7f, Mathf.Clamp01((box.Mass - 5f) / 10f));
            box.Body.linearVelocity = _player.PlanarVelocity + (cam.forward + Vector3.up * 0.25f).normalized * speed;
            // (A little turn about its upright, not a fling end over end.)
            box.Body.angularVelocity = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-1.2f, 1.2f), Random.Range(-0.4f, 0.4f));
            MicThump();
        }

        /// <summary>Each frame while carrying a box (UpdateCarry): the feel, the outline, the top box sliding off a fast turn.</summary>
        void UpdateBoxCarry()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float load = ArmWeight;
            float heavy = Mathf.Clamp01(load / ArmLoad);
            _player.pitchScale = 1f;
            _player.lookScale = Mathf.Lerp(0.95f, 0.72f, heavy);
            _player.moveScale = Mathf.Lerp(0.97f, 0.68f, heavy);
            _player.noRun = load > 9f;
            // The bob of walking, the lag of a turn, a lean against a start or a stop.
            var v = _player.PlanarVelocity;
            float speed = v.magnitude;
            _boxBob += dt * speed / 0.72f * Mathf.PI;
            float walk = Mathf.Clamp01(speed / 1.4f);
            var local = Quaternion.Inverse(transform.rotation);
            var accel = local * ((v - _lastPlanar) / dt);
            _lastPlanar = v;
            float give = 0.7f + heavy * 0.8f;
            var want = new Vector3(Mathf.Clamp(-_yawRate * 0.03f * give, -0.09f, 0.09f) + Mathf.Sin(_boxBob) * 0.006f * walk,
                                   -Mathf.Abs(Mathf.Sin(_boxBob)) * 0.014f * walk * give,
                                   Mathf.Clamp(-accel.z * 0.012f * give, -0.05f, 0.05f));
            _carrySway = Vector3.SmoothDamp(_carrySway, want, ref _carrySwayVel, 0.07f, Mathf.Infinity, dt);
            // (Four small boxes stood up to the eye, round 1 of this: the arms take the bottom one lower, toward the
            // thighs, as more go on it.)
            float above = ArmStackHeight - Held.Spec.size.y;
            _stackLower = Mathf.SmoothDamp(_stackLower, Mathf.Clamp(above * 0.5f, 0f, 0.22f), ref _stackLowerVel, 0.15f, Mathf.Infinity, dt);
            _turnLag = Mathf.SmoothDamp(_turnLag, Mathf.Clamp(-_yawRate * Mathf.Rad2Deg * 0.035f * give, -9f, 9f), ref _turnLagVel, 0.08f, Mathf.Infinity, dt);
            // A heavy box coming up: the view dips with it.
            if (!_dipped && _carryT > 0.65f)
            {
                _dipped = true;
                float m = TopBox ? TopBox.Mass : 0f;
                if (m >= 6f) { _liftDipT = 0f; _liftDipAmt = 0.025f + 0.0055f * (m - 6f); }
            }
            // Turning too fast with a stack: the top one goes.
            if (_arm.Count > 0)
            {
                float limit = 6.2f - 1.4f * (_arm.Count - 1) - 0.8f * heavy;
                if (_fastTurnT < 0f) _fastTurnT = Mathf.Min(0f, _fastTurnT + dt);          // (just lost one: a moment's grace)
                else _fastTurnT = Mathf.Abs(_yawRate) > limit ? _fastTurnT + dt : 0f;
                if (_fastTurnT > 0.06f) { LoseTop(); _fastTurnT = -0.25f; }
            }
            UpdateOutline();
        }

        /// <summary>The top box slides off the stack, flung out by the turn.</summary>
        void LoseTop()
        {
            var a = _arm[_arm.Count - 1];
            var p = a.p;
            RemoveFromArm(a, true);
            if (!p) return;
            var r = p.Body.worldCenterOfMass - transform.position; r.y = 0f;
            var w = Vector3.up * _yawRate;
            p.Body.linearVelocity = _player.PlanarVelocity + Vector3.Cross(w, r) * 1.1f + r.normalized * (_yawRate * _yawRate * r.magnitude * 0.12f);
            p.Body.angularVelocity = w * 0.6f;
            Note("WHOA - ONE FELL OFF");
        }

        // ---------------- Where the boxes go in the arms ----------------

        /// <summary>A box on the stack: where its origin goes (world) and how it is turned.</summary>
        void ArmTarget(int i, Vector3 baseCentreLocal, Quaternion baseRotLocal, out Vector3 pos, out Quaternion rot)
        {
            var a = _arm[i];
            float y = Held.Spec.size.y * 0.5f;
            for (int k = 0; k < i; k++) if (_arm[k].p) y += _arm[k].p.Spec.size.y;
            float h = a.p.Spec.size.y;
            y += h * 0.5f + 0.004f * (i + 1);
            // (Higher up, it sways a little more.)
            var centre = baseCentreLocal + Vector3.up * y + new Vector3(_carrySway.x, 0f, _carrySway.z) * (0.35f * (i + 1));
            var r = Quaternion.Euler(0f, _turnLag * 0.3f * (i + 1), 0f) * baseRotLocal * a.turn;
            var p = centre - r * a.p.Shape.bounds.center;
            float s = Mathf.SmoothStep(0f, 1f, a.t);
            var lp = Vector3.Lerp(a.fromPos, p, s) + Vector3.up * (0.12f * 4f * s * (1f - s));
            var lr = Quaternion.Slerp(a.fromRot, r, s);
            pos = transform.position + transform.rotation * lp;
            rot = transform.rotation * lr;
        }

        /// <summary>FixedUpdate: the boxes on top follow theirs as the carried one follows the hands.</summary>
        void ArmForces(Vector3 baseCentreLocal, Quaternion baseRotLocal)
        {
            Vector3 wT = Vector3.up * _yawRate;
            for (int i = 0; i < _arm.Count; i++)
            {
                var a = _arm[i];
                if (!a.p) { _arm.RemoveAt(i); i--; continue; }
                a.t = Mathf.Min(1f, a.t + Time.fixedDeltaTime / Mathf.Max(0.15f, 0.24f + 0.009f * a.p.Mass));
                ArmTarget(i, baseCentreLocal, baseRotLocal, out var pos, out var rot);
                var rb = a.p.Body;
                if (rb.isKinematic) a.p.Wake();
                Vector3 vT = _player.PlanarVelocity + Vector3.Cross(wT, pos - transform.position);
                rb.linearVelocity = Vector3.ClampMagnitude((pos - rb.position) / 0.07f + vT, 8f);
                (rot * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) ang -= 360f;
                Vector3 w = axis.sqrMagnitude > 0.5f && !float.IsNaN(axis.x) ? axis.normalized * (ang * Mathf.Deg2Rad / 0.09f) : Vector3.zero;
                rb.angularVelocity = Vector3.ClampMagnitude(w + wT, 12f);
            }
        }

        // ---------------- The outline ----------------

        void UpdateOutline()
        {
            _outShown = false;
            var box = TopBox;
            if (!box || KitCrate || _carryT < 0.6f || BoxTossCharge > 0f || !_player.HasFocus) return;
            _outShown = Placement(box, out _outPos, out _outRot, out _outValid, out bool flush);
            _outSize = box.Spec.size;
            if (_outShown && _outValid && flush && !_outFlush && Time.time > _flushClickAt)
            {
                _flushClickAt = Time.time + 0.15f;
                BoxSounds.Fit(_outPos);
            }
            _outFlush = _outShown && _outValid && flush;
            if (_outShown) DrawOutline();
        }

        void DrawOutline()
        {
            if (!s_cube) s_cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (!s_outGood)
            {
                s_outGood = Ghost(new Color(0.55f, 1f, 0.55f, 0.24f));
                s_outFlush = Ghost(new Color(0.75f, 1f, 0.75f, 0.38f));
                s_outBad = Ghost(new Color(1f, 0.35f, 0.25f, 0.3f));
            }
            var m = Matrix4x4.TRS(_outPos + _outRot * new Vector3(0f, _outSize.y * 0.5f, 0f), _outRot, _outSize);
            Graphics.DrawMesh(s_cube, m, !_outValid ? s_outBad : _outFlush ? s_outFlush : s_outGood, 0);
        }

        static Material Ghost(Color c)
        {
            var m = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            m.SetInt("_ZWrite", 0);
            m.SetColor("_Color", c);
            return m;
        }

        /// <summary>The nearest hit along a ray that is not the body nor a box in the arms (nor a box being set down).</summary>
        bool BoxRay(Vector3 from, Vector3 dir, float range, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(from, dir, s_boxHits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = s_boxHits[i];
                if (h.distance >= best || h.collider.transform.IsChildOf(transform)) continue;
                var rb = h.collider.attachedRigidbody;
                var mp = rb ? rb.GetComponent<MovableProp>() : null;
                if (mp && (InArms(mp) || mp.GetComponent<BoxSettle>() is BoxSettle bs && bs.enabled)) continue;
                best = h.distance; hit = h;
            }
            return best < float.MaxValue;
        }

        bool DownTo(Vector3 from, float range, out Vector3 at)
        {
            at = default;
            if (!BoxRay(from, Vector3.down, range, out var h) || h.normal.y < 0.7f) return false;
            at = h.point;
            return true;
        }

        /// <summary>
        /// Where the box goes where the view is: on what the view is on (a top: the floor, a deck, a box), or below where it
        /// meets a wall or a side (aimed between a shelf's decks: the deck under it). Turned the way you face to the
        /// nearest quarter (R turns it on), or lined up with what it stands on. Kept on a deck or a box's top it stands on,
        /// and snapped flush with its edges, or with a box beside it, when within a few centimetres (flush).
        /// </summary>
        bool Placement(MovableProp box, out Vector3 pos, out Quaternion rot, out bool valid, out bool flush)
        {
            pos = default; rot = Quaternion.identity; valid = false; flush = false;
            var cam = _cam ? _cam.transform : transform;
            if (!BoxRay(cam.position, cam.forward, PlaceReach, out var hit)) return false;
            var size = box.Spec.size;
            Vector3 support; Collider supportCol;
            if (hit.normal.y > 0.7f) { support = hit.point; supportCol = hit.collider; }
            else
            {
                var n = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (n.sqrMagnitude < 1e-4f) return false;
                float back = Mathf.Max(size.x, size.z) * 0.5f + 0.02f;
                if (!BoxRay(hit.point + n.normalized * back + Vector3.up * 0.02f, Vector3.down, 2.6f, out var down) || down.normal.y < 0.7f) return false;
                support = down.point; supportCol = down.collider;
            }
            float you = transform.eulerAngles.y;
            float yaw = Mathf.Round(you / 90f) * 90f;
            var under = supportCol ? supportCol.GetComponentInParent<MovableProp>() : null;
            if (under)
            {
                float uy = under.transform.eulerAngles.y;
                yaw = uy + Mathf.Round(Mathf.DeltaAngle(uy, you) / 90f) * 90f;
            }
            yaw += _placeTurn * 90f;
            rot = Quaternion.Euler(0f, yaw, 0f);
            pos = support;
            if (under && supportCol is BoxCollider bc) flush |= KeepOn(bc, under, size, rot, ref pos);
            flush |= Snug(box, size, ref rot, ref pos);
            pos.y = support.y + 0.004f;
            if ((pos - cam.position).sqrMagnitude > (PlaceReach + 0.4f) * (PlaceReach + 0.4f)) return false;
            valid = pos.y - transform.position.y < 2.1f && PoseClear(box, pos, rot);
            return true;
        }

        /// <summary>
        /// On a box-shaped top (a shelf's deck, a table, a box): the box kept on it (clear of a shelf's posts and back), and
        /// flush with an edge it is within 6 cm of. True if flush.
        /// </summary>
        static bool KeepOn(BoxCollider bc, MovableProp under, Vector3 size, Quaternion rot, ref Vector3 pos)
        {
            var t = bc.transform;
            var local = t.InverseTransformPoint(pos) - bc.center;
            var sc = t.lossyScale;
            float hx = Mathf.Abs(bc.size.x * sc.x) * 0.5f, hz = Mathf.Abs(bc.size.z * sc.z) * 0.5f;
            float rel = Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, rot.eulerAngles.y)) % 180f;
            bool across = rel > 45f && rel < 135f;
            float fx = (across ? size.z : size.x) * 0.5f, fz = (across ? size.x : size.z) * 0.5f;
            bool shelf = under.Spec.kind == MovableKind.Shelf;
            float ix = shelf ? 0.056f : 0.002f, iz = shelf ? 0.017f : 0.002f;
            bool flush = false;
            local.x = Keep(local.x, hx - ix, fx, ref flush);
            local.z = Keep(local.z, hz - iz, fz, ref flush);
            local.y = bc.size.y * 0.5f;
            var w = t.TransformPoint(local + bc.center);
            pos = new Vector3(w.x, pos.y, w.z);
            return flush;

            static float Keep(float v, float h, float f, ref bool fl)
            {
                if (f >= h) return 0f;                                         // bigger than the top: in its middle
                float lo = -h + f, hi = h - f;
                if (v >= hi - 0.06f) { fl = true; return hi; }
                if (v <= lo + 0.06f) { fl = true; return lo; }
                return v;
            }
        }

        /// <summary>Flush against a box standing beside it (within 6 cm), turned to line up with it if within 15 degrees.
        /// True if so.</summary>
        bool Snug(MovableProp box, Vector3 size, ref Quaternion rot, ref Vector3 pos)
        {
            var c = pos + Vector3.up * (size.y * 0.5f + 0.004f);
            var half = new Vector3(size.x * 0.5f + 0.06f, size.y * 0.5f - 0.02f, size.z * 0.5f + 0.06f);
            int n = Physics.OverlapBoxNonAlloc(c, half, s_boxOver, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = s_boxOver[i].attachedRigidbody;
                var o = rb ? rb.GetComponent<MovableProp>() : null;
                if (!o || o == box || o.Spec.kind != MovableKind.Box || InArms(o)) continue;
                var ro = o.transform.rotation;
                float delta = Mathf.DeltaAngle(ro.eulerAngles.y, rot.eulerAngles.y);
                float rel = Mathf.Abs(delta) % 90f;
                if (rel > 15f && rel < 75f) continue;                           // not near lined up with it
                if (Vector3.Angle(o.transform.up, Vector3.up) > 5f) continue;
                var lined = Quaternion.Euler(0f, ro.eulerAngles.y + Mathf.Round(delta / 90f) * 90f, 0f);
                if (Mathf.Abs(o.transform.position.y - pos.y) > 0.04f) continue;  // not standing on the same thing
                var so = o.Spec.size;
                float r2 = Mathf.Abs(Mathf.DeltaAngle(ro.eulerAngles.y, lined.eulerAngles.y)) % 180f;
                bool across = r2 > 45f && r2 < 135f;
                float ax = (across ? size.z : size.x) * 0.5f, az = (across ? size.x : size.z) * 0.5f;
                var oc = o.transform.position;
                var d = Quaternion.Inverse(ro) * (pos - oc);
                float gx = Mathf.Abs(d.x) - (so.x * 0.5f + ax), gz = Mathf.Abs(d.z) - (so.z * 0.5f + az);
                if (gx > -0.03f && gx < 0.06f && Mathf.Abs(d.z) < so.z * 0.5f + az - 0.03f) d.x = Mathf.Sign(d.x) * (so.x * 0.5f + ax + 0.003f);
                else if (gz > -0.03f && gz < 0.06f && Mathf.Abs(d.x) < so.x * 0.5f + ax - 0.03f) d.z = Mathf.Sign(d.z) * (so.z * 0.5f + az + 0.003f);
                else continue;
                var p = oc + ro * d;
                pos = new Vector3(p.x, pos.y, p.z);
                rot = lined;
                return true;
            }
            return false;
        }

        /// <summary>Nothing in the way of the box at this pose (it, the body and the boxes in the arms aside).</summary>
        bool PoseClear(MovableProp box, Vector3 pos, Quaternion rot)
        {
            var size = box.Spec.size;
            var c = pos + rot * new Vector3(0f, size.y * 0.5f + 0.006f, 0f);
            var half = new Vector3(Mathf.Max(0.01f, size.x * 0.5f - 0.01f), Mathf.Max(0.01f, size.y * 0.5f - 0.006f), Mathf.Max(0.01f, size.z * 0.5f - 0.01f));
            int n = Physics.OverlapBoxNonAlloc(c, half, s_boxOver, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_boxOver[i];
                if (col.transform.IsChildOf(transform)) continue;
                var rb = col.attachedRigidbody;
                var mp = rb ? rb.GetComponent<MovableProp>() : null;
                if (mp && (mp == box || InArms(mp))) continue;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A box being set down (PropGrabber.Boxes): it moves to its spot in a short arc, held still (kinematic), lands the last
    /// centimetre under its own weight (its thud: ImpactAudio), then settles with a tiny squash and a puff of dust.
    /// </summary>
    public class BoxSettle : MonoBehaviour
    {
        MovableProp _p;
        Vector3 _a, _b;
        Quaternion _ra, _rb;
        float _t, _dur, _landT = -1f;
        bool _moving;
        Transform[] _vis;
        Vector3[] _visPos, _visScale;

        public static void Begin(MovableProp p, Vector3 to, Quaternion rot, float dur)
        {
            if (!p || !p.Body) return;
            var s = p.GetComponent<BoxSettle>();
            if (!s) s = p.gameObject.AddComponent<BoxSettle>();
            s._p = p; s._a = p.Body.position; s._ra = p.Body.rotation; s._b = to; s._rb = rot;
            s._t = 0f; s._dur = Mathf.Max(0.05f, dur); s._moving = true; s._landT = -1f; s.enabled = true;
            p.held = true;                                    // (sent to the others while it moves: NetWorld)
            p.Body.linearVelocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero;
            p.Body.isKinematic = true;
            p.Body.interpolation = RigidbodyInterpolation.Interpolate;
            // (Through things on its way: it swept a box's corner beside its spot and turned it 10 degrees. The spot itself
            // was checked clear.)
            p.Body.detectCollisions = false;
        }

        void FixedUpdate()
        {
            if (!_moving) return;
            if (!_p) { enabled = false; return; }
            _t += Time.fixedDeltaTime / _dur;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
            // Up over an edge as it goes, then down onto the spot (a centimetre above it).
            float arc = 0.06f * 4f * s * (1f - s);
            _p.Body.MovePosition(Vector3.Lerp(_a, _b + Vector3.up * 0.012f, s) + Vector3.up * arc);
            _p.Body.MoveRotation(Quaternion.Slerp(_ra, _rb, s));
            if (_t >= 1f) Land();
        }

        void Land()
        {
            _moving = false;
            _p.Body.detectCollisions = true;
            _p.Body.isKinematic = false;
            _p.Body.useGravity = true;
            _p.Body.linearVelocity = Vector3.down * 0.25f;
            _p.Body.angularVelocity = Vector3.zero;
            _p.held = false;
            _p.Wake();
            _landT = 0f;
            // The parts that are drawn (not its shadow proxies): squashed a little about its foot.
            var list = new List<Transform>();
            foreach (var r in _p.GetComponentsInChildren<MeshRenderer>())
                if (r.transform != _p.transform && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) list.Add(r.transform);
            _vis = list.ToArray();
            _visPos = new Vector3[_vis.Length]; _visScale = new Vector3[_vis.Length];
            for (int i = 0; i < _vis.Length; i++) { _visPos[i] = _vis[i].localPosition; _visScale[i] = _vis[i].localScale; }
            BoxDust.Puff(_p, Mathf.Clamp01(_p.Mass / 12f));
        }

        void Update()
        {
            if (_landT < 0f || _vis == null) { if (!_moving && _landT < 0f) enabled = false; return; }
            _landT += Time.deltaTime;
            const float T = 0.24f;
            float u = Mathf.Clamp01(_landT / T);
            // Down 5% at once, back with a small overshoot.
            float sq = 0.05f * Mathf.Exp(-6f * u) * Mathf.Cos(u * Mathf.PI * 1.5f) * (1f - u);
            for (int i = 0; i < _vis.Length; i++)
            {
                if (!_vis[i]) continue;
                _vis[i].localPosition = new Vector3(_visPos[i].x * (1f + sq * 0.5f), _visPos[i].y * (1f - sq), _visPos[i].z * (1f + sq * 0.5f));
                _vis[i].localScale = new Vector3(_visScale[i].x * (1f + sq * 0.5f), _visScale[i].y * (1f - sq), _visScale[i].z * (1f + sq * 0.5f));
            }
            if (u >= 1f)
            {
                for (int i = 0; i < _vis.Length; i++) if (_vis[i]) { _vis[i].localPosition = _visPos[i]; _vis[i].localScale = _visScale[i]; }
                _vis = null; _landT = -1f; enabled = false;
            }
        }

        void OnDisable()
        {
            if (_vis != null) for (int i = 0; i < _vis.Length; i++) if (_vis[i]) { _vis[i].localPosition = _visPos[i]; _vis[i].localScale = _visScale[i]; }
            _vis = null;
            if (_moving && _p) { _moving = false; _p.Body.detectCollisions = true; _p.Body.isKinematic = false; _p.held = false; }
        }
    }

    /// <summary>A puff of dust from under a box set down (a few soft motes along its foot, settling).</summary>
    public static class BoxDust
    {
        static ParticleSystem s_ps;

        public static void Puff(MovableProp p, float strength)
        {
            if (!p) return;
            if (!s_ps) s_ps = Make();
            if (!s_ps) return;
            var s = p.Spec.size;
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            int count = 5 + Mathf.RoundToInt(5f * strength);
            for (int i = 0; i < count; i++)
            {
                // Along its foot, out from under it.
                var local = new Vector3(Random.Range(-0.5f, 0.5f) * s.x, 0.01f, Random.Range(-0.5f, 0.5f) * s.z);
                if (Random.value < 0.5f) local.x = Mathf.Sign(local.x) * s.x * 0.52f; else local.z = Mathf.Sign(local.z) * s.z * 0.52f;
                var at = p.transform.TransformPoint(local);
                var outward = Vector3.ProjectOnPlane(at - p.transform.position, Vector3.up).normalized;
                e.position = at;
                e.velocity = outward * Random.Range(0.08f, 0.25f) * (0.6f + strength) + Vector3.up * Random.Range(0.01f, 0.06f);
                e.startSize = Random.Range(0.035f, 0.09f);
                e.startLifetime = Random.Range(0.7f, 1.5f);
                e.startColor = new Color(0.78f, 0.72f, 0.6f, 0.35f);
                s_ps.Emit(e, 1);
            }
        }

        static ParticleSystem Make()
        {
            var go = new GameObject("BoxDust");
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.startSpeed = 0f;
            main.gravityModifier = 0.01f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.4f; limit.limit = 0.08f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Destruction.DemolitionMaterials.Dust) { name = "Box dust", hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_Hard")) mat.SetFloat("_Hard", 0f);
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }

    /// <summary>The boxes' own sounds: the cardboard rustle of taking one, the small click of the outline fitting flush.
    /// (Their thuds and scrapes are ImpactAudio's.)</summary>
    public static class BoxSounds
    {
        static AudioClip[] s_rustle;
        static AudioClip s_fit;

        public static void PickUp(Vector3 at, float kg)
        {
            if (s_rustle == null) { s_rustle = new AudioClip[3]; for (int i = 0; i < 3; i++) s_rustle[i] = Rustle(i); }
            Audio.WorldSound.PlayAt(s_rustle[Random.Range(0, 3)], at, Mathf.Lerp(0.22f, 0.34f, Mathf.Clamp01(kg / 12f)), 10f, Random.Range(0.92f, 1.08f) * Mathf.Lerp(1.05f, 0.9f, Mathf.Clamp01(kg / 12f)));
        }

        public static void Fit(Vector3 at)
        {
            if (!s_fit) s_fit = Click();
            Audio.WorldSound.PlayAt(s_fit, at, 0.12f, 6f, Random.Range(0.97f, 1.03f));
        }

        /// <summary>Cardboard taken in the hands: a soft scuff of fingers on board, a few crinkles, a low knock of its weight.</summary>
        static AudioClip Rustle(int v)
        {
            const int rate = 22050;
            int n = (int)(rate * 0.32f);
            var d = new float[n];
            var rng = new System.Random(911 + v * 37);
            float lp = 0f, hp = 0f, last = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (x - lp) * 0.35f;                                   // the scuff: noise, band-limited
                hp = 0.8f * (hp + lp - last); last = lp;
                float env = Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t * 11f);
                float s = hp * env * 0.55f;
                if (rng.NextDouble() < 0.0016) s += (float)(rng.NextDouble() - 0.5) * 0.9f * Mathf.Exp(-t * 6f);   // crinkles
                s += Mathf.Sin(2f * Mathf.PI * (95f + v * 8f) * t) * Mathf.Exp(-t * 28f) * 0.35f * Mathf.Clamp01(t / 0.004f);  // the knock
                d[i] = s;
            }
            var clip = AudioClip.Create("BoxRustle" + v, n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>A small dry click (two short ticks, card on card).</summary>
        static AudioClip Click()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.06f);
            var d = new float[n];
            var rng = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float a = Mathf.Exp(-t * 400f) + 0.6f * (t > 0.018f ? Mathf.Exp(-(t - 0.018f) * 500f) : 0f);
                d[i] = ((float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f + Mathf.Sin(2f * Mathf.PI * 2100f * t) * 0.5f) * a;
            }
            var clip = AudioClip.Create("BoxFit", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
