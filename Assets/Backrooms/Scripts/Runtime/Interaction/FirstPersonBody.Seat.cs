using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The body seated (2026-11-04, Interaction.Seating): the hips eased onto the seat and the feet onto the floor in front
    /// of it, side by side and planted, so the legs (their two-bone solve, knees toward the front) bend as a seated body's
    /// do: thighs along the seat, shins down to the floor. Its shadow sits too (the body is drawn into the shadows when it
    /// is not drawn).
    /// </summary>
    public partial class FirstPersonBody
    {
        bool _seatOn;
        float _sitW, _sitWVel;
        Vector3 _seatHip, _seatFootL, _seatFootR, _seatFwd = Vector3.forward;

        /// <summary>Sitting (on, the hips at hip, the feet at footL and footR on the floor, facing fwd) or not.</summary>
        public void SetSeat(bool on, Vector3 hip, Vector3 footL, Vector3 footR, Vector3 fwd)
        {
            _seatOn = on;
            if (!on) return;
            _seatHip = hip; _seatFootL = footL; _seatFootR = footR; _seatFwd = fwd;
        }

        /// <summary>How far into sitting the body is (0 standing .. 1 sat).</summary>
        public float SitWeight => _sitW;
        /// <summary>Where the hips are on the seat (world).</summary>
        public Vector3 SeatHip => _seatHip;

        void SeatHips(ref Vector3 hipsBase, float dt)
        {
            _sitW = Mathf.Clamp01(Springs.Damper(_sitW, ref _sitWVel, _seatOn ? 1f : 0f, 0.12f, dt));
            if (_sitW <= 0.001f) return;
            hipsBase = Vector3.Lerp(hipsBase, _seatHip, _sitW);
            _pelvisDrop = Mathf.Lerp(_pelvisDrop, 0f, _sitW);
        }

        void SeatFeet(Leg[] legs, Vector3[] soles, Vector3[] fwds, float[] pitches)
        {
            if (_sitW <= 0.001f) return;
            for (int i = 0; i < 2; i++)
            {
                var g = legs[i];
                if (g == null) continue;
                Vector3 want = g.side < 0f ? _seatFootL : _seatFootR;
                soles[i] = Vector3.Lerp(soles[i], want, _sitW);
                fwds[i] = Vector3.Slerp(fwds[i], _seatFwd, _sitW);
                pitches[i] = Mathf.Lerp(pitches[i], 0f, _sitW);
                if (_sitW > 0.5f) { g.swinging = false; g.plant = soles[i]; g.plantFwd = fwds[i]; g.heel = 0f; }
            }
        }
    }
}
