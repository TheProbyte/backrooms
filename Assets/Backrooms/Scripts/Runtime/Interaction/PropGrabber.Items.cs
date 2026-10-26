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
        [Header("Items")]
        [Tooltip("Reach for picking up items (m).")] public float itemRange = 2.1f;

        float _itemT;                    // 0..1 while the item travels from where it lay into the hand
        Vector3 _itemFromPos;
        Quaternion _itemFromRot;

        bool LookItem(float range, out WorldItem item)
        {
            item = null;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                item = _hits[i].collider.GetComponentInParent<WorldItem>();
            }
            return item;
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

        void UpdateHeldItem(bool focus, Mouse mouse)
        {
            var kb = Keyboard.current;
            if (focus && kb != null && kb.qKey.wasPressedThisFrame) { Drop(); _wasUse = false; return; }
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
            Quaternion view = pivot.rotation;
            Vector3 offset = Mirror(Vector3.Lerp(item.HoldOffset, item.UseOffset, item.UseBlend));
            Vector3 pos = pivot.position + view * offset;
            // Upright in the view, turned a little in toward the middle, tipped forward while in use.
            Quaternion rot = view * Quaternion.Euler(-8f * item.UseBlend, -10f * Side, 4f * Side);
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
