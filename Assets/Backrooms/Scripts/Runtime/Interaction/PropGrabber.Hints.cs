using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A line of small camcorder lettering at the bottom of the picture saying what the hands would do (owner, round 12:
    /// "very small camcorder-look text at the bottom, like 'click to push couch', simple and out of the way", always, not
    /// only the first time): what a click takes when the view is on something within reach ("HOLD TO PUSH COUCH", "HOLD TO
    /// TIP UP COUCH" at its underside, "HOLD TO CARRY BOX", "CLICK TO PICK UP CHALK"), the controls while holding a prop or an
    /// item, and a short note when something did not work ("CAN'T REACH"). On a prop, faint focus marks show where the
    /// hands would take it.
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>The line shown now (for tests), or null.</summary>
        public string HintText => ForceHint ?? (Time.time < _noteUntil ? _note : _hint);
        /// <summary>For the picture run (DebugTools.UiShots): a hint line shown whatever the hands are on, or null.</summary>
        public static string ForceHint;
        /// <summary>For probes: which grab edge the marks show now (-1: none).</summary>
        public int MarkEdge => _hintMark.HasValue ? _markEdge : -1;
        string _hint, _note;
        float _noteUntil = -10f;
        Vector3? _hintMark;
        /// <summary>No jitter along the edge for the marks (FindGrip varies a real grab a few centimetres; re-rolled each
        /// frame, the marks shook, round 13).</summary>
        sealed class NoJitter : System.Random { public override double NextDouble() => 0.5; }
        static readonly System.Random s_hintRng = new NoJitter();
        Vector3 _markShown;
        MovableProp _markProp;

        /// <summary>A short note in place of the hint for a moment (something did not work).</summary>
        void Note(string text) { _note = text; _noteUntil = Time.time + 1.6f; }

        static string NameOf(MovableProp p)
        {
            switch (p.Spec.kind)
            {
                case MovableKind.Sofa: return Backrooms.UI.Words.Get("thing.sofa");
                case MovableKind.Armchair: return Backrooms.UI.Words.Get("thing.armchair");
                case MovableKind.OfficeChair: return Backrooms.UI.Words.Get("thing.officechair");
                case MovableKind.Table: return Backrooms.UI.Words.Get("thing.table");
                case MovableKind.Pallet: return Backrooms.UI.Words.Get("thing.pallet");
                case MovableKind.WoodenChair: return Backrooms.UI.Words.Get("thing.woodenchair");
                case MovableKind.LawnChair: return Backrooms.UI.Words.Get("thing.lawnchair");
                case MovableKind.SchoolChair: return Backrooms.UI.Words.Get("thing.schoolchair");
                case MovableKind.UtilityCart: return Backrooms.UI.Words.Get("thing.utilitycart");
                case MovableKind.JanitorCart: return Backrooms.UI.Words.Get("thing.janitorcart");
                case MovableKind.PlatformTruck: return Backrooms.UI.Words.Get("thing.platformtruck");
                case MovableKind.Shelf: return Backrooms.UI.Words.Get("thing.shelf");
                default: return Backrooms.UI.Words.Get(Improvements.IsKit(p.Spec.id) ? "thing.kit" : "thing.box");
            }
        }

        /// <summary>What taking this prop at this spot does ("PUSH", "TIP UP", "CARRY").</summary>
        string PropVerb(MovableProp p, Vector3 grip, Vector3 normal) => Backrooms.UI.Words.Get(IsLight(p) ? "verb.carry" : UnderGrip(p, grip, normal) ? "verb.tip_up" : "verb.push");

        /// <summary>The prop the view is on within reach, the spot the hands would take and what that does.</summary>
        bool AimedProp(out MovableProp prop, out Vector3 grip, out string verb)
        {
            grip = default; verb = null;
            if (!Look(lookRange, out var hit, out prop) || !prop || prop.Shape == null || prop.Pinned) return false;
            // (The edge shown last wins a near tie: the marks popped from one chair leg to the other, round 14.)
            int prefer = prop == _markProp ? _markEdge : -1;
            _aimedMarkProp = prop;
            prop.FindGrip(hit.point, hit.normal, Shoulder, s_hintRng, prefer, out grip, out var n, out _, out _markEdge);
            verb = PropVerb(prop, grip, n);
            // Shown where the palm will go on the model itself, as the grab does (round 14: on the collider box a couch's
            // bottom edge was on the carpet in front of its legs). Worked out again only when the spot moves.
            if (prop != _placedProp || (grip - _placedFrom).sqrMagnitude > 0.02f * 0.02f)
            {
                _placedProp = prop; _placedFrom = grip;
                _placedAt = prop.PlaceHand(grip, n, Shoulder, out var palm, out _, out _) ? palm : prop.OnSurface(grip, n.normalized);
            }
            grip = _placedAt;
            return true;
        }
        MovableProp _placedProp;
        Vector3 _placedFrom, _placedAt;

        void UpdateHint()
        {
            _hint = null; _hintMark = null;
            if (Current == State.Holding && Held)
            {
                var kit = KitPlacer.CarryHint(Held);
                if (kit != null) { _hint = kit; return; }
                _hint = Carrying ? Backrooms.UI.Words.Get(Held.Spec.kind == MovableKind.Box ? "hint.carrying_box" : "hint.carrying")
                      : Tipping ? Backrooms.UI.Words.Get("hint.tipping")
                      : Backrooms.UI.Words.Get("hint.pushing");
                return;
            }
            if (Current != State.Idle || _fetch || OnDoor || QuickDooring || _poke != PokePhase.None || !_player.HasFocus) return;
            if (_seating && _seating.Seated && !HeldItem && _seating.Hint != null && !AimedItem)
            {
                // (Seated: what stands you up, unless the view is on something to take.)
                if (!Look(lookRange, out _, out var near) || !near) { _hint = _seating.Hint; return; }
            }
            if (!HeldItem || (!Stowing && _itemT >= 1f))
            {
                var bh = BagHint();
                if (bh != null) { _hint = bh; return; }
            }
            if (HeldItem)
            {
                if (Stowing || _itemT < 1f) return;
                if (AimedSocket != null) { _hint = Backrooms.UI.Words.Get("hint.plug_in"); return; }
                if (!HeldItem.UsesAim && HeldItem.Pocketable && AimedProp(out var p, out var g, out var verb))
                { _hint = Backrooms.UI.Words.Get("hint.hold_to", verb, NameOf(p)); _hintMark = g; SmoothMark(); return; }
                _hint = Backrooms.UI.Words.Get(HeldItem.Pocketable ? "hint.item_pocketable" : "hint.item");
                return;
            }
            if (AimedItem) { _hint = AimedCordPoint.HasValue ? Backrooms.UI.Words.Get("hint.take_plug") : Backrooms.UI.Words.Get("hint.pick_up_item", AimedItem.displayName.ToUpperInvariant()); return; }
            if (AimedPoke != null) { _hint = Backrooms.UI.Words.Get("hint.press"); return; }
            if (AimedDoorGrip.HasValue) { _hint = Backrooms.UI.Words.Get("hint.door"); return; }
            if (AimedProp(out var prop, out var grip, out var v))
            {
                string sit = Seating.CanSitOn(prop) && (!_seating || !_seating.Seated) ? " · " + Backrooms.UI.Words.Get("hint.sit") : "";
                _hint = IsLight(prop) ? (prop.Spec.kind == MovableKind.Box ? Backrooms.UI.Words.Get(OnTopOf(prop) ? "hint.pick_up_stack" : "hint.pick_up") : Backrooms.UI.Words.Get("hint.carry", NameOf(prop))) + sit
                      : Backrooms.UI.Words.Get("hint.hold_to", v, NameOf(prop)) + sit;
                _hintMark = grip;
            }
            if (_hint == null && KitPlacer.IdleHint != null) _hint = KitPlacer.IdleHint;      // a shelf: pack it, stand it up
            SmoothMark();
        }

        /// <summary>The marks glide to where the hands would take it (they popped from edge to edge as the view moved).</summary>
        void SmoothMark()
        {
            if (!_hintMark.HasValue) { _markProp = null; return; }
            var p = _aimedMarkProp;
            if (p != _markProp) { _markProp = p; _markShown = _hintMark.Value; return; }
            _markShown = Vector3.Lerp(_markShown, _hintMark.Value, 1f - Mathf.Exp(-Time.deltaTime / 0.09f));
        }
        MovableProp _aimedMarkProp;
        int _markEdge = -1;

        void HintGUI()
        {
            string text = HintText;
            if (string.IsNullOrEmpty(text)) return;
            float scale = Screen.height / 1080f * Backrooms.UI.UiKit.TextScale;
            // (2026-11-10, Docs/UiPlan.md U5: drawn as prompts, a key cap or the mouse and a verb, UI.Prompts.)
            bool note = Time.time < _noteUntil;
            Backrooms.UI.Prompts.Draw(text, Screen.width * 0.5f, PocketsTop - 56f * scale, scale, note ? 0.95f : 0.88f, note);
            if (_hintMark.HasValue && !note) FocusMarksAlpha(_markShown, 0.035f, 12f, 0.45f);
        }
    }
}
