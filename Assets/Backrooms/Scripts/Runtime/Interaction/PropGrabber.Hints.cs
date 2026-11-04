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
        public string HintText => Time.time < _noteUntil ? _note : _hint;
        /// <summary>For probes: which grab edge the marks show now (-1: none).</summary>
        public int MarkEdge => _hintMark.HasValue ? _markEdge : -1;
        string _hint, _note;
        float _noteUntil = -10f;
        Vector3? _hintMark;
        GUIStyle _hintStyle;
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
                case MovableKind.Sofa: return "COUCH";
                case MovableKind.Armchair: return "ARMCHAIR";
                case MovableKind.OfficeChair: return "OFFICE CHAIR";
                case MovableKind.Table: return "TABLE";
                case MovableKind.Pallet: return "PALLET";
                case MovableKind.WoodenChair: return "WOODEN CHAIR";
                case MovableKind.LawnChair: return "LAWN CHAIR";
                case MovableKind.SchoolChair: return "CHAIR";
                default: return "BOX";
            }
        }

        /// <summary>What taking this prop at this spot does ("PUSH", "TIP UP", "CARRY").</summary>
        string PropVerb(MovableProp p, Vector3 grip, Vector3 normal) => IsLight(p) ? "CARRY" : UnderGrip(p, grip, normal) ? "TIP UP" : "PUSH";

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
                _hint = Carrying ? (Held.Spec.kind == MovableKind.Box ? "MOUSE UP LIFTS IT · DOWN LOWERS IT · LET GO TO PUT IT DOWN" : "LET GO TO PUT IT DOWN")
                      : Tipping ? "MOUSE UP TIPS IT UP · DOWN LOWERS IT · LET GO TO DROP"
                      : "W PUSH · S PULL · MOUSE TURNS IT · MOUSE UP LIFTS";
                return;
            }
            if (Current != State.Idle || _fetch || OnDoor || QuickDooring || _poke != PokePhase.None || !_player.HasFocus) return;
            if (!HeldItem || (!Stowing && _itemT >= 1f))
            {
                var bh = BagHint();
                if (bh != null) { _hint = bh; return; }
            }
            if (HeldItem)
            {
                if (Stowing || _itemT < 1f) return;
                if (AimedSocket != null) { _hint = "CLICK TO PLUG IN"; return; }
                if (!HeldItem.UsesAim && HeldItem.Pocketable && AimedProp(out var p, out var g, out var verb))
                { _hint = $"HOLD TO {verb} {NameOf(p)}"; _hintMark = g; SmoothMark(); return; }
                _hint = HeldItem.Pocketable ? "Q DROP · E SET DOWN · 1-4 POCKET" : "Q DROP · E SET DOWN";
                return;
            }
            if (AimedItem) { _hint = AimedCordPoint.HasValue ? "CLICK TO TAKE THE PLUG" : "CLICK TO PICK UP " + AimedItem.displayName.ToUpperInvariant(); return; }
            if (AimedPoke != null) { _hint = "CLICK TO PRESS"; return; }
            if (AimedDoorGrip.HasValue) { _hint = "CLICK TO OPEN OR SHUT · HOLD TO MOVE IT SLOWLY"; return; }
            if (AimedProp(out var prop, out var grip, out var v)) { _hint = $"HOLD TO {v} {NameOf(prop)}"; _hintMark = grip; }
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
            float scale = Screen.height / 1080f;
            if (_hintStyle == null) _hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _hintStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(17f * scale));
            float y = PocketsTop - 46f * scale;
            var r = new Rect(0f, y, Screen.width, 26f * scale);
            bool note = Time.time < _noteUntil;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, _hintStyle);
            GUI.color = note ? new Color(1f, 0.9f, 0.6f, 0.9f) : new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(r, text, _hintStyle);
            GUI.color = old;
            if (_hintMark.HasValue && !note) FocusMarksAlpha(_markShown, 0.035f, 12f, 0.45f);
        }
    }
}
