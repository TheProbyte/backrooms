using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Four pockets (Docs/ProjectBrief.md, "Smaller objects"): keys 1-4 are the front left, front right, back left and back
    /// right pockets, each holding one small thing (<see cref="WorldItem.Pocketable"/>: chalk, spray paint, tape, the
    /// rubber chicken, the hammer, the box of nails; not lamps, cords, the sledgehammer or plugs). Each thing has its own
    /// pocket, kept for it while it is in the hand (owner's choice, round 12: the pockets are homes, not slots that trade):
    /// <list type="bullet">
    /// <item>hand empty, a key: that pocket's thing comes out into the hand;</item>
    /// <item>holding a thing from a pocket, its own key: it goes back; another pocket's key: it goes back into its own and
    /// that pocket's thing comes out (the hammer from 2, press 1: the hammer goes into 2, the chalk from 1 comes out);</item>
    /// <item>holding a thing, the key of an empty pocket: it goes into that one, which is its pocket from now on (how a thing
    /// moves to another pocket);</item>
    /// <item>holding something new (picked up, no pocket yet), a full pocket's key: they trade places, the new thing in, the
    /// pocket's thing out into the hand (with no pocket of its own now).</item>
    /// </list>
    /// Clicking a prop with something small in the hand that is not aimed at things (the hammer, the nails, the chicken)
    /// puts it in its pocket and takes the prop; letting go of the prop brings it back out. A strip at the bottom of the
    /// picture shows the pockets for a moment after a key. A pocketed thing is switched off and carried with the body; the
    /// world save keeps it in its pocket (WorldSaver, ItemData.pocket, ItemData.home).
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
        /// <summary>The thing in a pocket now (not one out in the hand).</summary>
        public WorldItem Pocket(int i) => i >= 0 && i < PocketCount ? _pockets[i] : null;

        /// <summary>The held thing's own pocket (kept for it while it is out), or -1 (picked up, not pocketed yet).</summary>
        public int HeldHome => HeldItem && _homeItem == HeldItem ? _homeSlot : -1;
        WorldItem _homeItem;
        int _homeSlot = -1;

        /// <summary>Tests: a pocket key pressed this frame (0-3), or -1.</summary>
        [System.NonSerialized] public int botPocket = -1;

        int _stowSlot = -1, _drawSlot = -1;
        WorldItem _thenDraw;            // comes out once the held thing is in (a switch, or a trade)
        int _thenDrawFrom = -1, _thenDrawHome = -1;
        float _stowT;
        const float StowTime = 0.22f;
        float _pocketShowUntil = -10f;
        string _pocketNote;
        GUIStyle _pocketStyle;

        // Put away to take a prop: it comes back out when the prop is let go.
        WorldItem _grabStowed;
        int _grabStowSlot = -1;
        bool _returnPending;

        /// <summary>Putting the held thing into a pocket right now (it goes down out of view).</summary>
        public bool Stowing => _stowSlot >= 0;

        Vector3 PocketPoint(int i) => transform.TransformPoint(PocketSpots[i]);
        Quaternion PocketTurn => transform.rotation * Quaternion.Euler(80f, 0f, 0f);

        /// <summary>Keys 1-4 (each frame, before the held item is updated). Returns true while the held thing goes into a
        /// pocket (it is not used meanwhile).</summary>
        bool UpdatePockets(bool focus)
        {
            if (_returnPending)
            {
                _returnPending = false;
                var it = _grabStowed;
                _grabStowed = null;
                if (it && !HeldItem && !_fetch && Current == State.Idle && _pockets[_grabStowSlot] == it) Draw(_grabStowSlot, it, _grabStowSlot);
            }
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
            var there = _pockets[i];
            if (HeldItem)
            {
                if (_itemT < 1f || HeldItem.Busy) return;
                if (!HeldItem.Pocketable) { _pocketNote = $"{HeldItem.displayName.ToUpperInvariant()} WON'T FIT IN A POCKET"; return; }
                int home = HeldHome;
                if (!there) Stow(i, null, -1, -1);                        // its own, or an empty one (it lives there now)
                else if (home >= 0) Stow(home, there, i, i);               // back into its own; that pocket's thing out
                else Stow(i, there, i, -1);                                // new: they trade places
                return;
            }
            if (there) Draw(i, there, i);
        }

        void Stow(int into, WorldItem thenDraw, int thenFrom, int thenHome)
        {
            _stowSlot = into; _stowT = 0f;
            _thenDraw = thenDraw; _thenDrawFrom = thenFrom; _thenDrawHome = thenHome;
            MicThump();
        }

        /// <summary>The held thing is in the pocket: switched off and carried with the body; then what comes out, out.</summary>
        void FinishStow()
        {
            int slot = _stowSlot;
            _stowSlot = -1;
            var item = HeldItem;
            var next = _thenDraw; _thenDraw = null;
            if (!item) return;
            HeldItem = null;
            AimedSocket = null;
            EndPlacing();
            if (next && _pockets[_thenDrawFrom] == next) _pockets[_thenDrawFrom] = null;
            PutAway(item, slot);
            if (next) Draw(_thenDrawFrom, next, _thenDrawHome);
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
            if (_homeItem == item) _homeItem = null;
        }

        /// <summary>Into a pocket at once, with no hand (a save being restored).</summary>
        public void PutInPocket(WorldItem item, int slot)
        {
            if (!item || slot < 0 || slot >= PocketCount) return;
            if (_pockets[slot] && _pockets[slot] != item) { Debug.LogWarning($"[Backrooms] Pocket {slot + 1} is full; {item.displayName} left out of it."); return; }
            if (HeldItem == item) HeldItem = null;
            PutAway(item, slot);
        }

        /// <summary>The thing in the hand has this pocket as its own (a save being restored).</summary>
        public void SetHeldHome(WorldItem item, int slot)
        {
            if (!item || slot < 0 || slot >= PocketCount || _pockets[slot]) return;
            _homeItem = item; _homeSlot = slot;
        }

        /// <summary>Takes a thing out of a pocket: it comes up from there into the hand; home: its own pocket from now on (or -1).</summary>
        void Draw(int from, WorldItem item, int home)
        {
            if (!item) return;
            if (_pockets[from] == item) _pockets[from] = null;
            item.transform.SetParent(null, true);
            item.transform.SetPositionAndRotation(PocketPoint(from), PocketTurn);
            item.gameObject.SetActive(true);
            item.Body.isKinematic = true;
            _drawSlot = from;
            Take(item, true);
            // (Not closing on it where it lies: it is already in the hand, coming up.)
            _itemT = 1e-4f; _liftTime = 0.3f; _pickTurn = Quaternion.identity;
            _homeItem = home >= 0 ? item : null; _homeSlot = home;
            MicThump();
        }

        int FirstFreePocket()
        {
            for (int i = 0; i < PocketCount; i++) if (!_pockets[i] && HeldHome != i) return i;
            return -1;
        }

        /// <summary>
        /// A click on a prop within reach with something small in the hand that is not aimed at things: it goes into its
        /// pocket (or the first free one) at once and the hand takes the prop. Returns true if the click was used.
        /// </summary>
        bool TryGrabWithItem()
        {
            var item = HeldItem;
            if (!item || item.UsesAim || !item.Pocketable || _itemT < 1f || item.Busy || Placing || ThrowCharge > 0f) return false;
            if (!Look(lookRange, out _, out var prop) || !prop) return false;
            int slot = HeldHome >= 0 ? HeldHome : FirstFreePocket();
            if (slot < 0) { Note("POCKETS FULL"); return true; }
            HeldItem = null;
            AimedSocket = null;
            EndPlacing();
            PutAway(item, slot);
            _grabStowed = item; _grabStowSlot = slot;
            _pocketShowUntil = Time.time + 1.5f;
            TryStart();
            if (Current == State.Idle) ReturnStowed();
            return true;
        }

        /// <summary>The prop let go (or the reach given up): what was put away to take it comes back out (next frame).</summary>
        void ReturnStowed()
        {
            if (_grabStowed) _returnPending = true;
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
            if (WornBag)
            {
                var bag = WornBag;
                WornBag = null;
                bag.TakeOff();
                bag.Body.isKinematic = false;
            }
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
            if (_pocketStyle == null) _pocketStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Paper, true) };
            _pocketStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(21f * scale));
            float w = 250f * scale, h = 54f * scale, gap = 10f * scale;
            int cells = PocketCount + (WornBag ? 1 : 0);
            float x0 = (Screen.width - (w * cells + gap * (cells - 1))) * 0.5f, y = Screen.height - h - 70f * scale;
            var old = GUI.color;
            for (int i = 0; i < PocketCount; i++)
            {
                var it = _pockets[i];
                string what;
                bool lit;
                if (_stowSlot == i && HeldItem) { what = HeldItem.displayName.ToUpperInvariant(); lit = true; }
                else if (it) { what = it.displayName.ToUpperInvariant(); lit = true; }
                else if (HeldHome == i) { what = "IN HAND: " + HeldItem.displayName.ToUpperInvariant(); lit = true; }
                else { what = "EMPTY"; lit = false; }
                var r = new Rect(x0 + i * (w + gap), y, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.35f * a);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                string text = $"{i + 1} {PocketNames[i]}\n{what}";
                GUI.color = new Color(0f, 0f, 0f, 0.8f * a);
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, _pocketStyle);
                GUI.color = lit ? new Color(1f, 1f, 1f, a) : new Color(1f, 1f, 1f, 0.5f * a);
                GUI.Label(r, text, _pocketStyle);
            }
            if (WornBag)
            {
                // The backpack on your back, a fifth place (B).
                var r = new Rect(x0 + PocketCount * (w + gap), y, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.35f * a);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                int n = WornBag.Count;
                string text = "B BACK\nBACKPACK" + (n > 0 ? $" ({n} IN IT)" : "");
                GUI.color = new Color(0f, 0f, 0f, 0.8f * a);
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, _pocketStyle);
                GUI.color = new Color(1f, 1f, 1f, a);
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

        /// <summary>The top of the pocket strip while it shows (for the hint line to sit above), or the bottom of the screen.</summary>
        float PocketsTop => Time.time > _pocketShowUntil ? Screen.height : Screen.height - (54f + 70f + 40f) * (Screen.height / 1080f);
    }
}
