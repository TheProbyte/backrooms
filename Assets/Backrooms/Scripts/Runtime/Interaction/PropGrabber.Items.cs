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

        /// <summary>Lets go of the held item: it keeps the hand's motion plus a little toss forward.</summary>
        public void Drop()
        {
            var item = HeldItem;
            if (!item) return;
            HeldItem = null;
            item.SetHeld(false);
            var cam = _cam ? _cam.transform : transform;
            item.Body.linearVelocity = _player.PlanarVelocity + cam.forward * 1.1f + Vector3.up * 0.4f;
            item.Body.angularVelocity = Random.insideUnitSphere * 3f;
        }

        void UpdateHeldItem(bool focus, Mouse mouse)
        {
            var kb = Keyboard.current;
            if (focus && kb != null && kb.qKey.wasPressedThisFrame) { Drop(); return; }
            bool use = focus && mouse != null && mouse.leftButton.isPressed && _itemT >= 1f;
            if (botHold.HasValue) use = botHold.Value;
            HeldItem.Hold(use, AimPoint(), Time.deltaTime);
            if (_shake) { _shake.dip = 0.12f + 0.1f * HeldItem.UseBlend; _shake.effort = 0f; }
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
