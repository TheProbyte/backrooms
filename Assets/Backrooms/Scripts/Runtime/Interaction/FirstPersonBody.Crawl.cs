using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The hand you crawl on (2026-11-09, the owner: "improve animations ... crawling"). Crawling, the body is not drawn
    /// (the eye is too low for the standing rig) and the free arm used to vanish unless it held something, so looking down
    /// in a vent there was nothing of you there. Now the free hand works the floor: flat on it a forearm ahead, staying
    /// where it is in the world while you move over it, then lifting and reaching forward to where it will be wanted
    /// (further at a scramble), as the feet step when you walk. The other arm holds the camcorder; the camera rocks onto
    /// each hand-fall (HandheldCameraShake). If the hand is busy (holding, reaching, on a wall) that wins.
    /// </summary>
    public partial class FirstPersonBody
    {
        Vector3 _crawlPalm, _crawlFrom, _crawlTo;
        float _crawlStep = 1f, _crawlW;
        bool _crawlSet;

        /// <summary>Is the free hand shown working the floor now?</summary>
        bool CrawlHandWanted => _player && _player.IsCrawling && !_player.noclip && !_player.seated && _grab && _grab.HandsShown;

        /// <summary>Replaces the arm's resting pose (hanging at the side) with the hand on the floor, while crawling.</summary>
        void CrawlHand(Quaternion yaw, float side, ref Vector3 wrist, ref Quaternion hand, ref float curl)
        {
            float dt = Time.deltaTime;
            _crawlW = Mathf.MoveTowards(_crawlW, CrawlHandWanted ? 1f : 0f, dt * 3.5f);
            if (_crawlW <= 0.001f) { _crawlSet = false; return; }
            Vector3 fwd = yaw * Vector3.forward, right = yaw * Vector3.right;
            Vector3 vel = _player.PlanarVelocity; vel.y = 0f;
            // Where the hand belongs: on the floor under the shoulder's reach, a forearm ahead.
            Vector3 home = transform.position + fwd * 0.44f + right * (0.2f * side) + Vector3.up * 0.014f;
            if (Physics.Raycast(home + Vector3.up * 0.3f, Vector3.down, out var hit, 0.7f, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                home.y = hit.point.y + 0.014f;
            if (!_crawlSet) { _crawlPalm = home; _crawlSet = true; _crawlStep = 1f; }
            if (_crawlStep >= 1f)
            {
                // Planted: it stays where it is while the body moves over it. Left behind (or dragged aside by a turn),
                // it comes forward to where it will be wanted a moment from now.
                Vector3 off = _crawlPalm - home; off.y = 0f;
                if (off.magnitude > 0.2f)
                {
                    _crawlFrom = _crawlPalm;
                    _crawlTo = home + Vector3.ClampMagnitude(vel * 0.36f, 0.16f);
                    _crawlStep = 0f;
                }
            }
            float lifted = 0f;
            if (_crawlStep < 1f)
            {
                _crawlStep = Mathf.Min(1f, _crawlStep + dt / (_player.CrawlingFast ? 0.2f : 0.3f));
                float k = Mathf.SmoothStep(0f, 1f, _crawlStep);
                lifted = Mathf.Sin(k * Mathf.PI);
                _crawlPalm = Vector3.Lerp(_crawlFrom, _crawlTo, k) + Vector3.up * (lifted * 0.07f);
            }
            // Flat on the floor, the fingers ahead and turned in a little; lifted, the wrist leads and the fingers hang.
            Quaternion flat = Quaternion.LookRotation(fwd - right * (0.28f * side), Vector3.up);
            flat = Quaternion.AngleAxis(20f * lifted, flat * Vector3.right) * flat;
            Vector3 w = _crawlPalm - flat * _wristToGrip;
            wrist = Vector3.Lerp(wrist, w, _crawlW);
            hand = Quaternion.Slerp(hand, flat, _crawlW);
            curl = Mathf.Lerp(curl, 0.06f + 0.3f * lifted, _crawlW);
        }
    }
}
