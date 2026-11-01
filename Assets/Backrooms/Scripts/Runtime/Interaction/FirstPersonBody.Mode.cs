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
    ///  - None: nothing of the body is drawn (Amnesia's way: a mark in the middle of the picture, things taken from further
    ///    off: PropGrabber.Bodiless). With Graphics > Player shadow on (2026-10-31, the owner: "my shadow looks odd when
    ///    passing by because its not animated"), the whole body is drawn into the shadows only, so the shadow walks,
    ///    crouches and reaches as the body does (as first-person games cast the shadow of a hidden full body); and its
    ///    bones give the capsules of the Poolrooms' traced sun (<see cref="ShadowCapsules"/>, PlayerShadow).
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
        public int BodyModeShown => _modeShown == 3 ? 2 : _modeShown;

        /// <summary>The body is drawn into the shadows only now (None, Player shadow on, the body up: crawling hides it).</summary>
        public bool CastsShadowOnly => _modeShown == 3 && _isMpfb && _body && _body.gameObject.activeInHierarchy;

        static bool ShadowWanted()
        {
            var gc = Backrooms.Camcorder.GraphicsController.Instance;
            return gc == null || gc.Options == null || gc.Options.playerShadow;
        }

        static readonly string[] CapBoneNames =
        {
            "pelvis", "neck_01", "head", "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r",
            "upperarm_l", "lowerarm_l", "hand_l", "upperarm_r", "lowerarm_r", "hand_r",
        };
        Transform[] _capBones;

        /// <summary>
        /// The body as capsules for the Poolrooms' traced sun, which has no shadow maps (world: a, b, radius): 0 the head,
        /// 1 the torso, 2-5 thighs and shins, 6-9 upper arms and forearms. Returns how many (0 when the body casts no shadow).
        /// </summary>
        public int ShadowCapsules(Vector3[] a, Vector3[] b, float[] r)
        {
            if (!CastsShadowOnly) return 0;
            if (_capBones == null)
            {
                _capBones = new Transform[CapBoneNames.Length];
                for (int i = 0; i < CapBoneNames.Length; i++) _capBones[i] = Find(_body, CapBoneNames[i]);
            }
            foreach (var t in _capBones) if (!t) return 0;
            var B = _capBones;
            int n = 0;
            void Cap(Vector3 p, Vector3 q, float rad) { a[n] = p; b[n] = q; r[n] = rad; n++; }
            var up = Vector3.up;
            Vector3 head = B[2].position + up * 0.08f;
            Cap(head, head, 0.1f);
            Cap(B[0].position + up * 0.05f, B[1].position - up * 0.04f, 0.15f);
            Cap(B[3].position, B[4].position, 0.075f); Cap(B[4].position, B[5].position, 0.055f);
            Cap(B[6].position, B[7].position, 0.075f); Cap(B[7].position, B[8].position, 0.055f);
            Cap(B[9].position, B[10].position, 0.045f); Cap(B[10].position, B[11].position, 0.04f);
            Cap(B[12].position, B[13].position, 0.045f); Cap(B[13].position, B[14].position, 0.04f);
            return n;
        }

        void ListModeRenderers()
        {
            _modeListed = true;
            _modeTrunk.Clear(); _modeAll.Clear();
            void Add(Renderer r, bool trunk)
            {
                if (!r || _modeAll.Contains(r)) return;
                _modeAll.Add(r);
                if (trunk) _modeTrunk.Add(r);
                _modeShadow[r] = r.shadowCastingMode;
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
            if (mode == 2 && ShadowWanted()) mode = 3;                  // none drawn, its shadow cast
            if (!_modeListed) ListModeRenderers();
            // Every frame while the shadow is cast: the arms' own switches (SetCamArmShown, SetWorkingArmShown) set their
            // shadow modes as they go.
            if (mode == 3)
                foreach (var r in _modeAll)
                    if (r && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            if (mode == _modeShown) return;
            bool wasShadow = _modeShown == 3;
            _modeShown = mode;
            foreach (var r in _modeAll) if (r) r.forceRenderingOff = mode == 2;
            foreach (var r in _modeTrunk)
            {
                if (!r) continue;
                r.shadowCastingMode = mode == 1 || mode == 3 ? ShadowCastingMode.ShadowsOnly : _modeShadow[r];
            }
            if (wasShadow)
            {
                // Back from the shadow only: the rest as they were, then the arms as their switches have them now.
                foreach (var r in _modeAll) if (r && !_modeTrunk.Contains(r)) r.shadowCastingMode = _modeShadow[r];
                bool cam = _camArmShown; _camArmShown = !cam; SetCamArmShown(cam);
                bool work = _workArmShown; _workArmShown = !work; SetWorkingArmShown(work);
            }
        }
    }
}
