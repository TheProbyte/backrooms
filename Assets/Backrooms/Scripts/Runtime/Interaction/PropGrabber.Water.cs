using Backrooms.Audio;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Touching the water (2026-11-01, the Poolrooms review's item 16): crouched (or lying) at a pool's edge, a click on the
    /// water within reach puts the free hand in it. The palm goes down flat and through the surface, the fingers trail a
    /// slow stroke through the water and back (ripples spreading from the hand, the soft lap of it), then the hand comes out
    /// dripping: drops fall from it for a few seconds after, each a little plink where it lands in the pool.
    /// 2026-11-01, the owner: "maybe a shorter 'tap' to water when pressed?" (and the body is not to be in the game): a
    /// quick tap now, about two thirds of a second in all: in, a short flick through the water, out, a second of drips.
    /// </summary>
    public partial class PropGrabber
    {
        enum WaterPhase { None, Down, Stroke, Up }
        const float TapDown = 0.12f, TapStroke = 0.32f, TapUp = 0.18f, TapReach = 0.09f, TapDrips = 1.6f;

        WaterPhase _water;
        float _waterT, _waterLevel, _dripUntil, _nextDrip;
        Vector3 _waterPoint, _waterAlong;
        int _waterPick;
        AudioClip[] _waterLaps, _waterPlinks;

        /// <summary>The free hand is in (or going to or coming out of) the water.</summary>
        public bool TouchingWater => _water != WaterPhase.None;
        /// <summary>Times the hand has been put in the water (for tests).</summary>
        public int WaterTouches { get; private set; }

        /// <summary>The click's last resort: the water the view is on, if the hand can reach it from where you are.</summary>
        bool TryTouchWater()
        {
            // (With no body drawn there is no hand to see, but the water still answers: the ripples, the sound, the drops.)
            if (HeldItem || _water != WaterPhase.None) return false;
            var w = PoolWorld.Instance;
            var cam = _cam ? _cam : Camera.main;
            if (!w || !cam || PoolRipples.PlayerInWater) return false;
            var o = cam.transform.position; var d = cam.transform.forward;
            if (d.y > -0.15f) return false;
            // Where the view meets the water's surface (each storey's), and nothing solid before it.
            if (!w.WaterAt(o + d * 0.8f, out float level) && !w.WaterAt(o + d * 1.4f, out level)) return false;
            float t = (level - o.y) / d.y;
            if (t <= 0f || t > 2.2f) return false;
            var p = o + d * t;
            if (!w.WaterAt(p, out float at) || Mathf.Abs(at - level) > 0.05f) return false;
            if (Physics.Raycast(o, d, t - 0.02f, ~0, QueryTriggerInteraction.Ignore)) return false;
            // The palm a few centimetres in; the shoulder has to reach it (crouched or lying at the edge, not standing).
            var palm = p + Vector3.down * 0.06f;
            if ((palm - ReachShoulder).magnitude > ArmReach * 0.98f) return false;
            _water = WaterPhase.Down; _waterT = 0f; _waterLevel = level;
            _waterPoint = palm;
            var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            _waterAlong = (Vector3.Cross(Vector3.up, flat) * -Side * 0.6f + flat * 0.8f).normalized;
            WaterTouches++;
            return true;
        }

        /// <summary>Each frame: the stroke through the water, and the drops off the hand afterwards.</summary>
        void UpdateWaterTouch()
        {
            float dt = Time.deltaTime;
            if (_water != WaterPhase.None)
            {
                _waterT += dt;
                // Leaving it: moved away, or something else wants the hand.
                if (HeldItem || Current != State.Idle || (_waterPoint - ReachShoulder).magnitude > ArmReach + 0.1f) { EndWater(); }
                else switch (_water)
                {
                    case WaterPhase.Down:
                        if (_waterT > TapDown)
                        {
                            _water = WaterPhase.Stroke; _waterT = 0f;
                            var entry = new Vector3(_waterPoint.x, _waterLevel, _waterPoint.z);
                            PoolRipples.Splash(entry, 0.12f, -0.006f);
                            PoolSplashes.Bubbles(entry, 4, 0.06f, 0.08f);
                            if (_waterLaps == null) { _waterLaps = new AudioClip[4]; for (int k = 0; k < 4; k++) _waterLaps[k] = ProceduralSounds.Lap(k); }
                            WorldSound.PlayAt(_waterLaps[_waterPick++ & 3], entry, 0.16f, 6f, Random.Range(1.15f, 1.3f));
                        }
                        break;
                    case WaterPhase.Stroke:
                    {
                        // Out along the stroke and back, the palm pushing a small bow wave ahead of it.
                        float u = Mathf.Clamp01(_waterT / TapStroke);
                        float s = Mathf.Sin(u * Mathf.PI) * TapReach;
                        float v = Mathf.Cos(u * Mathf.PI) > 0f ? 1f : -1f;
                        var hand = _waterPoint + _waterAlong * s;
                        var surf = new Vector3(hand.x, _waterLevel, hand.z);
                        PoolRipples.Splash(surf + _waterAlong * (0.07f * v), 0.08f, 0.0025f);
                        PoolRipples.Splash(surf - _waterAlong * (0.06f * v), 0.08f, -0.003f);
                        if (u >= 1f)
                        {
                            _water = WaterPhase.Up; _waterT = 0f;
                            PoolSplashes.Burst(surf, 5, 0.5f, Vector3.up * 0.2f, 0.4f, 0.6f);
                            _dripUntil = Time.time + TapDrips; _nextDrip = Time.time + 0.12f;
                        }
                        break;
                    }
                    case WaterPhase.Up:
                        if (_waterT > TapUp) EndWater();
                        break;
                }
            }
            // Drops off the wet hand: quick at first, slowing; each lands as a plink in the pool (or nothing on the deck).
            if (Time.time < _dripUntil && Time.time >= _nextDrip)
            {
                float k = 1f - (_dripUntil - Time.time) / TapDrips;
                _nextDrip = Time.time + Mathf.Lerp(0.1f, 0.6f, k) * Random.Range(0.6f, 1.4f);
                var hand = Armed ? HandPosition : ReachShoulder + transform.forward * 0.3f + Vector3.down * 0.5f;
                var w = PoolWorld.Instance;
                if (w && w.WaterAt(hand, out float level) && hand.y > level)
                {
                    var at = new Vector3(hand.x, level, hand.z);
                    if (_waterPlinks == null) { _waterPlinks = new AudioClip[4]; for (int i = 0; i < 4; i++) _waterPlinks[i] = ProceduralSounds.Plink(i + 2); }
                    WorldSound.PlayAt(_waterPlinks[_waterPick++ & 3], at, 0.07f * (1f - 0.6f * k), 5f, Random.Range(1.05f, 1.25f));
                    PoolRipples.Splash(at, 0.04f, -0.0025f);
                }
            }
        }

        void EndWater() { _water = WaterPhase.None; _waterT = 0f; }

        /// <summary>The hand flat on (and in) the water, palm down, fingers along the stroke.</summary>
        void WaterHandPose()
        {
            float y;
            var p = _waterPoint;
            switch (_water)
            {
                case WaterPhase.Down: y = Mathf.Lerp(0.1f, 0f, Mathf.SmoothStep(0f, 1f, _waterT / TapDown)); break;
                case WaterPhase.Stroke:
                    y = -0.02f * Mathf.Sin(Mathf.Clamp01(_waterT / TapStroke) * Mathf.PI);
                    p += _waterAlong * (Mathf.Sin(Mathf.Clamp01(_waterT / TapStroke) * Mathf.PI) * TapReach);
                    break;
                default: y = Mathf.Lerp(0f, 0.16f, _waterT / TapUp); break;
            }
            HandPosition = p + Vector3.up * y;
            var fingers = Vector3.ProjectOnPlane(_waterAlong + transform.forward * 0.6f, Vector3.up).normalized;
            HandRotation = Quaternion.LookRotation(fingers + Vector3.down * 0.15f, Vector3.up);
            HandCurl = Mathf.MoveTowards(HandCurl, _water == WaterPhase.Stroke ? 0.08f : 0.2f, Time.deltaTime * 3f);
        }
    }
}
