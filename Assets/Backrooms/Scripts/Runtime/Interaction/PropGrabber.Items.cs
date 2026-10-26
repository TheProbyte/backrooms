using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The free hand holds items (<see cref="WorldItem"/>, e.g. the spray can): click one within reach to pick it up,
    /// hold the left mouse button to use it, Q to drop it. While it holds something the hand does nothing else (no
    /// grabbing furniture, no buttons). The item is carried low on the hand's side of the view and raised a little
    /// while in use; the arm (FirstPersonBody) grips it.
    /// </summary>
    public partial class PropGrabber
    {
        public WorldItem HeldItem { get; private set; }
        Quaternion _swayRot;
        Vector3 _swayPos;
        bool _swayInit;
        float _bobPhase;
        [Header("Items")]
        [Tooltip("Reach for picking up items (m).")] public float itemRange = 2.1f;

        float _itemT;                    // 0..1 while the item travels from where it lay into the hand
        Vector3 _itemFromPos;
        Quaternion _itemFromRot;

        [Tooltip("Picking up forgives this much aim (degrees off the centre of the view).")] public float pickAssistAngle = 14f;
        readonly Collider[] _near = new Collider[48];
        /// <summary>What a click would pick up right now (for the focus marks), or null.</summary>
        public WorldItem AimedItem { get; private set; }

        /// <summary>
        /// The item a click picks up: the one the centre of the view is on, or else the item nearest the aim within
        /// <see cref="pickAssistAngle"/> and reach that can be seen from the eye (so small things like chalk are easy).
        /// </summary>
        bool LookItem(float range, out WorldItem item)
        {
            item = null;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider first = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                first = _hits[i].collider;
                item = first.GetComponentInParent<WorldItem>();
            }
            if (item && !item.Held) return true;
            item = null;
            // Looking straight at furniture: that is what the click is for.
            if (first && first.GetComponentInParent<MovableProp>()) return false;

            // Near the aim: everything within reach, scored by how far off the aim it is (and a little by distance).
            Vector3 eye = cam.position, fwd = cam.forward;
            int m = Physics.OverlapSphereNonAlloc(eye + fwd * (range * 0.5f), range * 0.5f + 0.3f, _near, ~0, QueryTriggerInteraction.Ignore);
            float bestScore = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                var it = _near[i].GetComponentInParent<WorldItem>();
                if (!it || it.Held || (it == item)) continue;
                Vector3 c = _near[i].bounds.center, to = c - eye;
                float dist = to.magnitude;
                if (dist > range + 0.2f || dist < 0.05f) continue;
                float angle = Vector3.Angle(fwd, to);
                float off = Vector3.Cross(fwd, to).magnitude;                     // metres from the line of sight
                if (angle > pickAssistAngle && off > 0.12f) continue;
                if (angle > pickAssistAngle * 2f) continue;
                float score = angle + dist * 3f;
                if (score >= bestScore || !InSight(eye, c, it)) continue;
                bestScore = score; item = it;
            }
            return item;
        }

        /// <summary>Nothing solid between the eye and a point of the item.</summary>
        bool InSight(Vector3 eye, Vector3 point, WorldItem it)
        {
            Vector3 d = point - eye;
            int n = Physics.RaycastNonAlloc(eye, d.normalized, _hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = _hits[i].collider.transform;
                if (t.IsChildOf(transform) || t.IsChildOf(it.transform)) continue;
                if (_hits[i].distance < d.magnitude - 0.05f) return false;
            }
            return true;
        }

        /// <summary>Camcorder-style focus marks around what a click would pick up.</summary>
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !AimedItem || HeldItem || Current != State.Idle || !_cam) return;
            var b = new Bounds(AimedItem.transform.position, Vector3.zero);
            foreach (var r in AimedItem.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Vector3 sp = _cam.WorldToScreenPoint(b.center);
            if (sp.z <= 0f) return;
            float size = Mathf.Clamp(b.extents.magnitude / Mathf.Max(0.2f, sp.z) * Screen.height * 0.9f, 22f, 160f);
            float x = sp.x, y = Screen.height - sp.y, h = size * 0.5f, len = Mathf.Max(6f, size * 0.22f), t = Mathf.Max(1.5f, Screen.height / 540f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            void Bar(float bx, float by, float bw, float bh) => GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
            Bar(x - h, y - h, len, t); Bar(x - h, y - h, t, len);
            Bar(x + h - len, y - h, len, t); Bar(x + h - t, y - h, t, len);
            Bar(x - h, y + h - t, len, t); Bar(x - h, y + h - len, t, len);
            Bar(x + h - len, y + h - t, len, t); Bar(x + h - t, y + h - len, t, len);
            GUI.color = old;
        }

        /// <summary>The empty hand clicks something that has its own use (a plug in a socket).</summary>
        bool TryHandUse()
        {
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, itemRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; IHandUse use = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                var u = _hits[i].collider.GetComponentInParent<IHandUse>();
                if (u == null && _hits[i].collider.isTrigger) continue;
                best = _hits[i].distance; use = u;
            }
            return use != null && use.HandUse(this);
        }

        bool TryPickUp()
        {
            if (!LookItem(itemRange, out var item) || item.Held) return false;
            PickUp(item);
            return true;
        }

        /// <summary>Takes an item into the hand (from wherever it is).</summary>
        public void PickUp(WorldItem item)
        {
            if (!item) return;
            if (HeldItem) Drop();
            if (Current != State.Idle) Release("picked up an item");
            HeldItem = item;
            _itemFromPos = item.transform.position; _itemFromRot = item.transform.rotation;
            _itemT = 0f;
            _swayInit = false; _qDown = -1f; ThrowCharge = 0f;
            item.SetHeld(true);
        }

        /// <summary>Lets go of the held item where it is, with no toss (a plug pushed into a socket).</summary>
        public void LetGo(WorldItem item)
        {
            if (!item || HeldItem != item) return;
            HeldItem = null;
            item.SetHeld(false);
        }

        /// <summary>The held item is gone (used up): the hand is free.</summary>
        public void ItemGone(WorldItem item)
        {
            if (HeldItem == item) HeldItem = null;
        }

        /// <summary>Lets go of the held item: it keeps the hand's motion plus a little toss forward (or is set down upright).</summary>
        public void Drop()
        {
            var item = HeldItem;
            if (!item) return;
            if (item.PlaceOnDrop && PlaceSpot(out var at, out var yaw))
            {
                HeldItem = null;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                item.SetHeld(false);
                // Through the body too: an interpolated rigidbody would put it back where the hand had it.
                item.Body.position = at; item.Body.rotation = rot;
                item.transform.SetPositionAndRotation(at, rot);
                item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero;
                item.OnPlaced();
                return;
            }
            HeldItem = null;
            item.SetHeld(false);
            var cam = _cam ? _cam.transform : transform;
            item.Body.linearVelocity = _player.PlanarVelocity + cam.forward * 1.1f + Vector3.up * 0.4f;
            item.Body.angularVelocity = Random.insideUnitSphere * 3f;
        }

        /// <summary>Where Q sets an item down: a floor or table top within reach, the item turned to face you.</summary>
        bool PlaceSpot(out Vector3 at, out float yaw)
        {
            at = default; yaw = transform.eulerAngles.y + 180f;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 2.4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || (HeldItem && c.transform.IsChildOf(HeldItem.transform)) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                found = _hits[i].normal.y > 0.75f;
                at = _hits[i].point;
            }
            if (!found)
            {
                // Looking at a wall: put it on the floor in front of it.
                Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                var from = transform.position + Vector3.up * 0.8f + fwd * Mathf.Min(best < float.MaxValue ? best - 0.25f : 0.7f, 0.7f);
                if (!Physics.Raycast(from, Vector3.down, out var floor, 1.5f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.75f) return false;
                at = floor.point;
            }
            return true;
        }

        bool _wasUse;
        float _qDown = -1f;
        /// <summary>0..1 while Q is held to throw (wind-up), 0 otherwise.</summary>
        public float ThrowCharge { get; private set; }
        [Header("Throwing")]
        [Tooltip("Hold Q longer than this to throw instead of dropping (s).")] public float throwHold = 0.2f;
        [Tooltip("Seconds of holding Q for the hardest throw.")] public float throwFullCharge = 0.9f;
        [Tooltip("Throw speed (m/s) from a short to a full wind-up.")] public Vector2 throwSpeed = new Vector2(3.5f, 13f);

        /// <summary>Throws the held item along the view, harder the longer Q was held.</summary>
        void Throw(float charge)
        {
            var item = HeldItem;
            if (!item) return;
            HeldItem = null;
            item.SetHeld(false);
            var cam = _cam ? _cam.transform : transform;
            float speed = Mathf.Lerp(throwSpeed.x, throwSpeed.y, charge);
            item.Body.linearVelocity = _player.PlanarVelocity + (cam.forward + Vector3.up * 0.12f).normalized * speed;
            item.Body.angularVelocity = Random.insideUnitSphere * (4f + 8f * charge);
            AudioSource.PlayClipAtPoint(Audio.SoundBank.CordWhip, item.transform.position, 0.15f + 0.25f * charge);
        }

        void UpdateHeldItem(bool focus, Mouse mouse)
        {
            var kb = Keyboard.current;
            // Q: a tap drops (or sets down), held it winds up a throw that goes on release.
            if (focus && kb != null && kb.qKey.wasPressedThisFrame) _qDown = Time.time;
            if (_qDown >= 0f)
            {
                float held = Time.time - _qDown;
                ThrowCharge = held > throwHold ? Mathf.Clamp01((held - throwHold) / Mathf.Max(0.05f, throwFullCharge)) : 0f;
                if (kb == null || !kb.qKey.isPressed)
                {
                    float charge = ThrowCharge;
                    _qDown = -1f; ThrowCharge = 0f; _wasUse = false;
                    if (held <= throwHold) Drop(); else Throw(Mathf.Max(0.05f, charge));
                    return;
                }
            }
            bool use = focus && mouse != null && mouse.leftButton.isPressed && _itemT >= 1f;
            if (botHold.HasValue) use = botHold.Value;
            var cam = _cam ? _cam.transform : transform;
            var input = new HoldInput { use = use, pressed = use && !_wasUse, released = !use && _wasUse, eye = cam.position, aimDir = cam.forward, dt = Time.deltaTime, hand = this,
                                        secondary = focus && mouse != null && mouse.rightButton.wasPressedThisFrame };
            _wasUse = use;
            input.hit = AimHit(out input.hitInfo);
            input.aimAt = input.hit ? input.hitInfo.point : cam.position + cam.forward * 2f;
            HeldItem.HoldUpdate(input);
            if (!HeldItem) return;                // used up
            if (_shake) { _shake.dip = 0.12f + 0.1f * HeldItem.UseBlend; _shake.effort = 0f; }
        }

        /// <summary>The nearest thing the centre of the view is on, within 6 m (not the player or the held item).</summary>
        bool AimHit(out RaycastHit hit)
        {
            hit = default;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || (HeldItem && c.transform.IsChildOf(HeldItem.transform)) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; hit = _hits[i]; any = true;
            }
            return any;
        }

        /// <summary>What the centre of the view is on (within 6 m), or a point 2 m ahead.</summary>
        Vector3 AimPoint()
        {
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 at = cam.position + cam.forward * 2f;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || (HeldItem && c.transform.IsChildOf(HeldItem.transform)) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; at = _hits[i].point;
            }
            return at;
        }

        Vector3 Mirror(Vector3 v) => Side > 0f ? v : new Vector3(-v.x, v.y, v.z);

        /// <summary>After the camera has moved: the held item follows the view, and the hand grips it.</summary>
        void LateUpdate()
        {
            if (!HeldItem) return;
            var item = HeldItem;
            Transform pivot = _player.cameraPitchPivot ? _player.cameraPitchPivot : (_cam ? _cam.transform : transform);
            // The item is in the hand, not glued to the lens: it trails the view's turns and the body's moves a little and
            // bobs with the steps, and the hand (which grips it) goes with it.
            float dt = Time.deltaTime;
            if (!_swayInit) { _swayRot = pivot.rotation; _swayPos = pivot.position; _swayInit = true; }
            _swayRot = Quaternion.Slerp(_swayRot, pivot.rotation, 1f - Mathf.Exp(-dt / 0.075f));
            if (Quaternion.Angle(_swayRot, pivot.rotation) > 25f) _swayRot = Quaternion.RotateTowards(pivot.rotation, _swayRot, 25f);
            _swayPos = Vector3.Lerp(_swayPos, pivot.position, 1f - Mathf.Exp(-dt / 0.045f));
            _swayPos = pivot.position + Vector3.ClampMagnitude(_swayPos - pivot.position, 0.06f);
            float speed = _player.PlanarVelocity.magnitude;
            _bobPhase += dt * speed * 5.2f;
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.006f, Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.01f - 0.005f, 0f) * Mathf.Clamp01(speed / 1.5f);
            Quaternion view = _swayRot;
            Vector3 offset = Mirror(Vector3.Lerp(item.HoldOffset, item.UseOffset, item.UseBlend)) + bob;
            // Winding up a throw: back and up over the shoulder.
            offset += new Vector3(0.04f * Side, 0.1f, -0.16f) * ThrowCharge;
            Vector3 pos = _swayPos + view * offset;
            // Upright in the view, turned a little in toward the middle, tipped forward while in use.
            Quaternion rot = view * Quaternion.Euler(-8f * item.UseBlend - 25f * ThrowCharge, -10f * Side, 4f * Side);
            float w = item.UsePose(out var usePos, out var useRot);
            if (w > 0f) { pos = Vector3.Lerp(pos, usePos, w); rot = Quaternion.Slerp(rot, useRot, w); }
            if (_itemT < 1f)
            {
                _itemT = Mathf.Min(1f, _itemT + Time.deltaTime / 0.28f);
                float s = Mathf.SmoothStep(0f, 1f, _itemT);
                pos = Vector3.Lerp(_itemFromPos, pos, s);
                rot = Quaternion.Slerp(_itemFromRot, rot, s);
            }
            item.transform.SetPositionAndRotation(pos, rot);

            Quaternion grip = item.GripRotationLocal;
            Vector3 f = Mirror(grip * Vector3.forward), u = Mirror(grip * Vector3.up);
            HandPosition = item.transform.TransformPoint(Mirror(item.GripLocal));
            HandRotation = item.transform.rotation * Quaternion.LookRotation(f, u);
        }
    }
}
