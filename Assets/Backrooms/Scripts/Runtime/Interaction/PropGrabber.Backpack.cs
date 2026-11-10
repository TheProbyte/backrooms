using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The backpack on your back (2026-11-04, <see cref="Backpack"/>): B with a backpack in the hand swings it up over the
    /// shoulder onto your back; B with the hand empty takes it off into the hand. One on the back at a time. While it is on,
    /// the pocket strip shows it as a fifth place, "B BACK". A thing in the hand clicked on an open backpack goes in it.
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>The backpack on your back now, or null.</summary>
        public Backpack WornBag { get; private set; }
        /// <summary>Tests: B pressed this frame.</summary>
        [System.NonSerialized] public bool botBag;

        float _wearT = -1f;             // swinging the held one up onto the back (0..1), or -1
        const float WearTime = 0.42f;

        /// <summary>A short note in place of the hint (for the items: "IT WON'T FIT").</summary>
        public void Say(string text) => Note(text);

        /// <summary>Swinging a backpack up onto the back right now (it is not used meanwhile).</summary>
        public bool Wearing => _wearT >= 0f;

        /// <summary>B (each frame, before the held item is updated). Returns true while the bag goes onto the back.</summary>
        bool UpdateBackpack(bool focus)
        {
            bool b = botBag || (focus && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame);
            botBag = false;
            if (b) BackpackKey();
            if (_wearT < 0f) return false;
            _wearT += Time.deltaTime / WearTime;
            if (_wearT >= 1f) FinishWear();
            return _wearT >= 0f;
        }

        /// <summary>What B does now (see the class notes).</summary>
        public void BackpackKey()
        {
            _pocketShowUntil = Time.time + 2.5f;
            _pocketNote = null;
            if (_wearT >= 0f || _stowSlot >= 0 || Placing || ThrowCharge > 0f) return;
            if (HeldItem is Backpack held)
            {
                if (_itemT < 1f) return;
                if (WornBag) { _pocketNote = "ONE BACKPACK ON YOUR BACK ALREADY"; return; }
                _wearT = 0f;
                MicThump();
                return;
            }
            if (WornBag)
            {
                if (HeldItem) { _pocketNote = "HANDS FULL"; return; }
                if (Current != State.Idle || OnDoor || _fetch) { _pocketNote = "HANDS BUSY"; return; }
                TakeOffBag();
                return;
            }
            _pocketNote = "NO BACKPACK";
        }

        void FinishWear()
        {
            _wearT = -1f;
            var bag = HeldItem as Backpack;
            if (!bag) return;
            HeldItem = null;
            AimedSocket = null;
            EndPlacing();
            bag.SetHeld(false);
            bag.PutOn(transform);
            WornBag = bag;
        }

        /// <summary>Off the back into the hand: it comes round from behind the shoulder.</summary>
        void TakeOffBag()
        {
            var bag = WornBag;
            WornBag = null;
            if (!bag) return;
            bag.TakeOff();
            Backpack.BackPose(transform, _cam, out var at, out var turn);
            bag.transform.SetPositionAndRotation(at + Vector3.up * 0.25f, turn);
            bag.Body.isKinematic = true;
            Take(bag, true);
            _itemT = 1e-4f; _liftTime = 0.42f; _pickTurn = Quaternion.identity;
            MicThump();
        }

        /// <summary>On the back at once, with no hand (a save being restored).</summary>
        public void WearNow(Backpack bag)
        {
            if (!bag || WornBag) return;
            if (HeldItem == bag) { HeldItem = null; bag.SetHeld(false); }
            bag.PutOn(transform);
            WornBag = bag;
        }

        /// <summary>LateUpdate: swinging up onto the back, the held bag goes up and back over the shoulder out of view.</summary>
        void BagPose(ref Vector3 pos, ref Quaternion rot)
        {
            if (_wearT < 0f) return;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_wearT));
            Backpack.BackPose(transform, _cam, out var at, out var turn);
            Vector3 over = transform.TransformPoint(new Vector3(0.22f * Side, 1.75f, -0.1f));
            Vector3 a = Vector3.Lerp(pos, over, Mathf.Clamp01(s * 1.6f));
            pos = Vector3.Lerp(a, at, Mathf.Clamp01(s * 1.6f - 0.6f));
            rot = Quaternion.Slerp(rot, turn, s);
        }

        /// <summary>The open backpack (or a thing lying in one) the view is on within reach, or null.</summary>
        Backpack LookBag()
        {
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, itemRange, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Backpack bag = null;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                if (HeldItem && c.transform.IsChildOf(HeldItem.transform)) continue;
                best = _hits[i].distance;
                var it = c.GetComponentInParent<WorldItem>();
                bag = it is Backpack b ? b : it ? it.InBag : null;
            }
            return bag;
        }

        /// <summary>A click with a thing in the hand on an open backpack within reach: it goes in. True if the click was used.</summary>
        bool TryPutInBag()
        {
            var item = HeldItem;
            if (!item || item is Backpack || _itemT < 1f || item.Busy || Placing || ThrowCharge > 0f || _wearT >= 0f) return false;
            var bag = LookBag();
            if (!bag || !bag.IsOpen) return false;
            return bag.TryPut(item, this);
        }

        /// <summary>The hint for a backpack: what a click on it does (null: nothing of the bag's).</summary>
        string BagHint()
        {
            if (HeldItem)
            {
                if (HeldItem is Backpack) return WornBag ? "Q DROP · E SET DOWN" : "B PUT IT ON · Q DROP · E SET DOWN";
                var bag = LookBag();
                if (bag && bag.IsOpen) return Backpack.Goes(HeldItem) ? "CLICK TO PUT IT IN THE BACKPACK" : null;
                return null;
            }
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, itemRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; BackpackZip zip = null; bool other = false;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                var z = _hits[i].collider.GetComponentInParent<BackpackZip>();
                if (z == null && _hits[i].collider.isTrigger) continue;
                best = _hits[i].distance; zip = z; other = z == null;
            }
            if (other || !zip || !zip.bag || zip.bag.Held || zip.bag.Worn) return null;
            if (!zip.bag.IsOpen && !zip.OnPanel) return Backrooms.UI.Words.Get("hint.unzip");
            if (zip.bag.IsOpen && zip.OnPanel) return Backrooms.UI.Words.Get("hint.zip");
            return null;
        }
    }
}
