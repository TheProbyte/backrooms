using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Running into a door from the side it opens away from slams it open (owner, 2026-10-31: "when sprinting into a door
    /// it slams open (if running from the right direction) so you dont have to stop and click it slowly"). As in Fortnite or
    /// Shadows of Doubt: no key, the run is enough. The shoulder takes it: the latch knocks out of its keep, the door
    /// swings wide as fast as you came and bangs on its stop, and you go on through a little slower (the door gives way
    /// under the shoulder instead of stopping the body). From the other side (it opens toward you) a run just meets it.
    /// Bolted, it does not give: a thud, the view jolts and you stop.
    /// </summary>
    public partial class PropGrabber
    {
        [Header("Door bash")]
        [Tooltip("Running into a door at least this fast (m/s), the way it opens, slams it open.")] public float bashSpeed = 2.4f;
        [Tooltip("How square on (cos of the angle between the run and the way the door opens there) a bash has to be.")]
        public float bashSquare = 0.45f;
        Door _bashed;
        float _bashedAt = -10f;

        /// <summary>Doors slammed open by running into them (for tests).</summary>
        public int Bashes { get; private set; }

        bool TryBash(Door door, ControllerColliderHit hit)
        {
            if (!_player || Held || OnDoor) return false;
            Vector3 v = _player.PlanarVelocity; v.y = 0f;
            float speed = v.magnitude;
            // Still pushing a door this run slammed: keep it going (it gives way as fast as you come).
            bool again = door == _bashed && Time.time - _bashedAt < 0.8f;
            if (speed < (again ? 1.2f : bashSpeed)) return false;
            Vector3 opening = door.OpeningAt(hit.point);
            if (opening.sqrMagnitude < 0.5f) return false;
            if (Vector3.Dot(v / speed, opening) < bashSquare) return false;          // the wrong side, or a glance
            Vector3 look = transform.forward; look.y = 0f;
            if (Vector3.Dot(look.normalized, v / speed) < 0.3f) return false;        // backing into it is not a bash
            if (again) { if (!door.Bolted) { door.Bash(hit.point, speed); _player.KeepMomentum(0.12f); } return true; }

            if (!door.Bash(hit.point, speed))
            {
                // Bolted: it does not give. The body stops against it.
                _bashed = door; _bashedAt = Time.time;
                _player.SetPlanarVelocity(Vector3.zero);
                Jolt(new Vector3(7f, (Random.value - 0.5f) * 6f, (Random.value - 0.5f) * 8f));
                Note("IT'S BOLTED");
                return true;
            }
            _bashed = door; _bashedAt = Time.time;
            Bashes++;
            // Through it a little slower: the door takes some of the run.
            _player.SetPlanarVelocity(v * 0.8f);
            _player.KeepMomentum(0.25f);
            float side = Vector3.Dot(transform.right, hit.point - transform.position) > 0f ? 1f : -1f;
            Jolt(new Vector3(3.5f, -side * 3f, side * 4f));
            return true;
        }

        void Jolt(Vector3 kick)
        {
            if (!_shake) _shake = FindAnyObjectByType<HandheldCameraShake>();
            if (_shake) _shake.Jolt(kick);
        }
    }
}
