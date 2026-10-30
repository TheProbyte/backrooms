using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Four pockets (Docs/ProjectBrief.md, "Smaller objects"): keys 1-4 are the front left, front right, back left and back
    /// right pockets, each holding one small thing (<see cref="WorldItem.Pocketable"/>: chalk, spray paint, tape, the
    /// rubber chicken, the hammer, the box of nails; not lamps, cords, the sledgehammer or plugs). A key:
    /// <list type="bullet">
    /// <item>with something small in the hand and that pocket empty: the hand puts it away (down out of view);</item>
    /// <item>with the hand empty and something in that pocket: the hand takes it out (up from the pocket);</item>
    /// <item>with both: they swap (the hand's thing goes in, the pocket's comes out).</item>
    /// </list>
    /// A small strip at the bottom of the picture shows what is in each pocket for a moment after a key. A pocketed thing is
    /// switched off and carried with the body; the world save keeps it in its pocket (WorldSaver, ItemData.pocket).
    /// </summary>
    public partial class PropGrabber
    {
        public const int PocketCount = 4;
        public static readonly string[] PocketNames = { "FRONT L", "FRONT R", "BACK L", "BACK R" };
        /// <summary>Where each pocket is on the body (m from the feet, body frame: x right, z forward).</summary>
        static readonly Vector3[] PocketSpots =
        {
            new Vector3(-0.16f, 0.9f, 0.06f), new Vector3(0.16f, 0.9f, 0.06f), new Vector3(-0.14f, 0.92f, -0.12f), new Vector3(0.14f, 0.92f, -0.12f),
        };
        readonly WorldItem[] _pockets = new WorldItem[PocketCount];
        public WorldItem Pocket(int i) => i >= 0 && i < PocketCount ? _pockets[i] : null;

        /// <summary>Tests: a pocket key pressed this frame (0-3), or -1.</summary>
        [System.NonSerialized] public int botPocket = -1;

        int _stowSlot = -1, _drawSlot = -1, _swapSlot = -1;
        float _stowT;
        const float StowTime = 0.22f;
        float _pocketShowUntil = -10f;
        string _pocketNote;
        GUIStyle _pocketStyle;

        /// <summary>Putting the held thing into a pocket right now (it goes down out of view).</summary>
        public bool Stowing => _stowSlot >= 0;

        Vector3 PocketPoint(int i) => transform.TransformPoint(PocketSpots[i]);
        Quaternion PocketTurn => transform.rotation * Quaternion.Euler(80f, 0f, 0f);

        /// <summary>Keys 1-4 (each frame, before the held item is updated). Returns true while the held thing goes into a
        /// pocket (it is not used meanwhile).</summary>
        bool UpdatePockets(bool focus)
        {
            int key = botPocket;
            botPocket = -1;
            var kb = Keyboard.current;
            if (key < 0 && focus && kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) key = 0;
                else if (kb.digit2Key.wasPressedThisFrame) key = 1;
                else if (kb.digit3Key.wasPressedThisFrame) key = 2;
                else if (kb.digit4Key.wasPressedThisFrame) key = 3;
            }
            if (key >= 0) PocketKey(key);
            if (_stowSlot >= 0)
            {
                _stowT += Time.deltaTime / StowTime;
                if (_stowT >= 1f) FinishStow();
                return _stowSlot >= 0;
            }
            return false;
        }

        /// <summary>What a pocket key does now (see the class notes).</summary>
        public void PocketKey(int i)
        {
            if (i < 0 || i >= PocketCount) return;
            _pocketShowUntil = Time.time + 2.5f;
            _pocketNote = null;
            if (_stowSlot >= 0 || Placing || ThrowCharge > 0f) return;
            if (Current != State.Idle || OnDoor || _fetch) { _pocketNote = "HANDS BUSY"; return; }
            var inPocket = _pockets[i];
            if (HeldItem)
            {
                if (_itemT < 1f || HeldItem.Busy) return;
                if (!HeldItem.Pocketable) { _pocketNote = $"{HeldItem.displayName.ToUpperInvariant()} WON'T FIT IN A POCKET"; return; }
                _stowSlot = i; _stowT = 0f;
                _swapSlot = inPocket ? i : -1;            // its pocket's thing comes out once this one is in
                MicThump();
                return;
            }
            if (inPocket) Draw(i);
        }

        /// <summary>The held thing is in the pocket: switched off and carried with the body.</summary>
        void FinishStow()
        {
            int slot = _stowSlot, swap = _swapSlot;
            _stowSlot = -1; _swapSlot = -1;
            var item = HeldItem;
            if (!item) return;
            var out_ = swap >= 0 ? _pockets[swap] : null;
            HeldItem = null;
            AimedSocket = null;
            EndPlacing();
            PutAway(item, slot);
            if (out_) Draw(swap, out_);
        }

        void PutAway(WorldItem item, int slot)
        {
            item.SetHeld(false);
            item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero;
            item.Body.isKinematic = true;
            item.transform.SetParent(transform, true);
            item.transform.SetPositionAndRotation(PocketPoint(slot), PocketTurn);
            item.gameObject.SetActive(false);
            _pockets[slot] = item;
        }

        /// <summary>Into a pocket at once, with no hand (a save being restored).</summary>
        public void PutInPocket(WorldItem item, int slot)
        {
            if (!item || slot < 0 || slot >= PocketCount) return;
            if (_pockets[slot] && _pockets[slot] != item) { Debug.LogWarning($"[Backrooms] Pocket {slot + 1} is full; {item.displayName} left out of it."); return; }
            if (HeldItem == item) { HeldItem = null; }
            PutAway(item, slot);
        }

        /// <summary>Takes the pocket's thing out: it comes up from the pocket into the hand.</summary>
        void Draw(int i, WorldItem item = null)
        {
            item = item ? item : _pockets[i];
            if (!item) return;
            if (_pockets[i] == item) _pockets[i] = null;
            item.transform.SetParent(null, true);
            item.transform.SetPositionAndRotation(PocketPoint(i), PocketTurn);
            item.gameObject.SetActive(true);
            item.Body.isKinematic = true;
            _drawSlot = i;
            Take(item, true);
            // (Not closing on it where it lies: it is already in the hand, coming up.)
            _itemT = 1e-4f; _liftTime = 0.3f; _pickTurn = Quaternion.identity;
            MicThump();
        }

        /// <summary>LateUpdate: going into a pocket, the held thing goes down to it out of view; coming out, it rises from
        /// where the pocket is now (the body moves on meanwhile).</summary>
        void PocketPose(ref Vector3 pos, ref Quaternion rot)
        {
            if (_drawSlot >= 0)
            {
                if (_itemT >= 1f || !HeldItem) _drawSlot = -1;
                else { _itemFromPos = PocketPoint(_drawSlot); _itemFromRot = PocketTurn; }
            }
            if (_stowSlot < 0) return;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_stowT));
            pos = Vector3.Lerp(pos, PocketPoint(_stowSlot), s);
            rot = Quaternion.Slerp(rot, PocketTurn, s);
        }

        /// <summary>Every pocket's thing back out into the world (the player is gone: a world ending, a reset).</summary>
        public void EmptyPockets()
        {
            for (int i = 0; i < PocketCount; i++)
            {
                var it = _pockets[i];
                _pockets[i] = null;
                if (!it) continue;
                it.transform.SetParent(null, true);
                it.gameObject.SetActive(true);
                it.Body.isKinematic = false;
            }
        }

        /// <summary>The pockets for a moment after a key: what is in each, bottom centre, in the camcorder's lettering.</summary>
        void PocketsGUI()
        {
            if (Time.time > _pocketShowUntil) return;
            float a = Mathf.Clamp01((_pocketShowUntil - Time.time) / 0.4f);
            float scale = Screen.height / 1080f;
            if (_pocketStyle == null) _pocketStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _pocketStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(21f * scale));
            float w = 250f * scale, h = 54f * scale, gap = 10f * scale;
            float x0 = (Screen.width - (w * PocketCount + gap * (PocketCount - 1))) * 0.5f, y = Screen.height - h - 70f * scale;
            var old = GUI.color;
            for (int i = 0; i < PocketCount; i++)
            {
                var it = _pockets[i];
                bool here = _stowSlot == i || (_drawSlot == i && HeldItem);
                string what = it ? it.displayName.ToUpperInvariant() : "EMPTY";
                if (_stowSlot == i && HeldItem) what = HeldItem.displayName.ToUpperInvariant();
                var r = new Rect(x0 + i * (w + gap), y, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.35f * a);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                string text = $"{i + 1} {PocketNames[i]}\n{what}";
                GUI.color = new Color(0f, 0f, 0f, 0.8f * a);
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, _pocketStyle);
                GUI.color = it || here ? new Color(1f, 1f, 1f, a) : new Color(1f, 1f, 1f, 0.5f * a);
                GUI.Label(r, text, _pocketStyle);
            }
            if (!string.IsNullOrEmpty(_pocketNote))
            {
                var r = new Rect(0f, y - 40f * scale, Screen.width, 34f * scale);
                GUI.color = new Color(0f, 0f, 0f, 0.8f * a);
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), _pocketNote, _pocketStyle);
                GUI.color = new Color(1f, 0.9f, 0.6f, a);
                GUI.Label(r, _pocketNote, _pocketStyle);
            }
            GUI.color = old;
        }
    }
}
