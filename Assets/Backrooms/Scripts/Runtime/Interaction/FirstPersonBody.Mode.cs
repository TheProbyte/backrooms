using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Camera menu > HANDHELD > Body (PropGrabber.bodyMode), to compare (owner, 2026-10-31: "how would this be if i had no
    /// body"):
    ///  - Full: as built (legs and shoes looking down, the arms reaching).
    ///  - Hands only: no legs, trunk or shoes drawn (their shadow stays); an arm comes into view only while it does
    ///    something (it already did: SetWorkingArmShown, SetCamArmShown).
    ///  - None: nothing of the body is drawn, its shadow neither (Amnesia's way: a mark in the middle of the picture, things
    ///    taken from further off: PropGrabber.Bodiless).
    /// The body keeps working underneath either way (the legs still step, so the footsteps keep their timing); only what is
    /// drawn changes (Renderer.forceRenderingOff, and the shadow mode for Hands only).
    /// </summary>
    public partial class FirstPersonBody
    {
        int _modeShown = -1;
        readonly List<Renderer> _modeTrunk = new List<Renderer>();          // the body but its arms (legs, shoes, trunk, head)
        readonly List<Renderer> _modeAll = new List<Renderer>();
        readonly Dictionary<Renderer, ShadowCastingMode> _modeShadow = new Dictionary<Renderer, ShadowCastingMode>();
        bool _modeListed;

        /// <summary>The body mode drawn now (0 full, 1 hands only, 2 none; for tests).</summary>
        public int BodyModeShown => _modeShown;

        void ListModeRenderers()
        {
            _modeListed = true;
            _modeTrunk.Clear(); _modeAll.Clear();
            void Add(Renderer r, bool trunk)
            {
                if (!r || _modeAll.Contains(r)) return;
                _modeAll.Add(r);
                if (trunk) { _modeTrunk.Add(r); _modeShadow[r] = r.shadowCastingMode; }
            }
            if (_body)
                foreach (var r in _body.GetComponentsInChildren<Renderer>(true))
                    Add(r, !_workArm.Contains(r) && !_camArm.Contains(r));
            if (_looseArm != null && _looseArm.renderers != null) foreach (var r in _looseArm.renderers) Add(r, false);
            if (_leftArm) foreach (var r in _leftArm.GetComponentsInChildren<Renderer>(true)) Add(r, false);
            if (_camcorder) foreach (var r in _camcorder.GetComponentsInChildren<Renderer>(true)) Add(r, false);
        }

        void ApplyBodyMode()
        {
            int mode = _grab ? Mathf.Clamp(_grab.bodyMode, 0, 2) : 0;
            if (!_modeListed) ListModeRenderers();
            if (mode == _modeShown) return;
            _modeShown = mode;
            foreach (var r in _modeAll) if (r) r.forceRenderingOff = mode == 2;
            foreach (var r in _modeTrunk)
            {
                if (!r) continue;
                r.shadowCastingMode = mode == 1 ? ShadowCastingMode.ShadowsOnly : _modeShadow[r];
            }
        }
    }
}
