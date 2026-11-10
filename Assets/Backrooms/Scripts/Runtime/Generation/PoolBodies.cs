using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Things in the Poolrooms' water (2026-10-31, the water interaction plan): items and props float or sink by how dense
    /// they are, slow down in the water, are pushed aside by you wading past, and splash (spray, a ring on the water, a plop
    /// or a splash by size) when they go in. As Unity's own BoatAttack water does it: the buoyant force (Archimedes: the
    /// weight of the water displaced) shared over sample points spread through the object (here the eight corners of its
    /// collider bounds, drawn in a little), each pushing by how far under the surface it is, so a floating thing finds its
    /// level and tips to lie flat; plus water drag. Only moving bodies near you are simulated (frozen props stay frozen
    /// until disturbed, a floating one freezes where it settles); the lists are the items' and props' own registries, so no
    /// physics queries.
    /// </summary>
    public class PoolBodies : MonoBehaviour
    {
        [Tooltip("Bodies further than this from you (m) are left alone.")] public float range = 30f;
        [Tooltip("Water drag (per second) on a body all under water.")] public float drag = 2.2f;
        public float angularDrag = 1.6f;

        sealed class State
        {
            public Vector3[] points;     // sample points, body-local: the middles of its eight octants
            public float half;           // half the body's height (m): the slice of it each point stands for
            public float density;        // relative to water (< 1 floats)
            public float size;           // its largest dimension (m)
            public bool inWater;
            public float splashAt;
        }

        readonly Dictionary<Rigidbody, State> _known = new Dictionary<Rigidbody, State>();
        readonly List<Rigidbody> _gone = new List<Rigidbody>();
        Transform _player;
        CamcorderPlayerController _ctrl;
        int _plop;

        void FixedUpdate()
        {
            var world = PoolWorld.Instance;
            if (!_player && world && world.streamer) { _player = world.streamer.player; if (_player) _ctrl = _player.GetComponent<CamcorderPlayerController>(); }
            if (!world || !_player || !world.IsPool(_player.position)) return;
            var pp = _player.position;
            bool wading = PoolRipples.PlayerInWater && _ctrl && _ctrl.PlanarVelocity.sqrMagnitude > 0.04f;
            foreach (var it in WorldItem.Live) if (it && !it.Held) Step(world, it.Body, null, it, pp, wading);
            foreach (var mp in MovableProp.Live) if (mp && !mp.held) Step(world, mp.Body, mp, null, pp, wading);
            // Forget what has gone (destroyed, picked up into a pocket).
            _gone.Clear();
            foreach (var kv in _known) if (!kv.Key) _gone.Add(kv.Key);
            foreach (var rb in _gone) _known.Remove(rb);
        }

        void Step(PoolWorld world, Rigidbody rb, MovableProp prop, WorldItem item, Vector3 pp, bool wading)
        {
            if (!rb) return;
            Vector3 c = rb.worldCenterOfMass;
            Vector3 toMe = c - pp; toMe.y = 0f;
            if (toMe.sqrMagnitude > range * range) return;
            if (!world.WaterAt(c, out float level)) { if (_known.TryGetValue(rb, out var st0)) st0.inWater = false; return; }
            if (rb.isKinematic)
            {
                // A frozen prop floating where it settled: wading past wakes it (then the water moves it).
                if (prop && wading && toMe.sqrMagnitude < 1.6f * 1.6f && _known.TryGetValue(rb, out var sk) && sk.inWater && sk.density < 1f) prop.Wake();
                return;
            }
            if (!_known.TryGetValue(rb, out var s)) _known[rb] = s = Make(rb, prop, item);

            // Sample points under the surface (a little lower than the still level as the ripples go by is not worth a read
            // back from the GPU: the push of the wake stands in for it).
            int n = s.points.Length;
            float weight = rb.mass * -Physics.gravity.y;
            float lift = weight / Mathf.Max(0.05f, s.density) / n;
            float under = 0f;
            var t = rb.transform;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = t.TransformPoint(s.points[i]);
                float k = Mathf.Clamp01((level - p.y) / s.half + 0.5f);       // how much of its slice is under
                if (k <= 0f) continue;
                under += k;
                rb.AddForceAtPosition(Vector3.up * (lift * k), p, ForceMode.Force);
            }
            float frac = under / n;
            if (frac <= 0f) { s.inWater = false; return; }

            // Going in: spray, a ring, a plop or a splash by how hard.
            if (!s.inWater)
            {
                float vy = Mathf.Max(0f, -rb.linearVelocity.y);
                float energy = rb.mass * (vy * vy + 0.3f * rb.linearVelocity.sqrMagnitude);
                if (vy > 0.25f && Time.time > s.splashAt)
                {
                    s.splashAt = Time.time + 0.4f;
                    var at = new Vector3(c.x, level, c.z);
                    float strength = Mathf.Clamp01(energy / 30f);
                    PoolSplashes.Splash(at, strength, rb.linearVelocity * 0.25f, rb.mass > 1.5f || s.size > 0.35f);
                    PoolRipples.Splash(at, Mathf.Clamp(s.size * 0.6f, 0.08f, 0.6f), -Mathf.Lerp(0.004f, 0.03f, strength));
                    int cls = s.size > 0.45f || rb.mass > 6f ? 2 : s.size > 0.18f || rb.mass > 0.8f ? 1 : 0;
                    WorldSound.PlayAt(SoundBank.Plop(cls, _plop++), at, Mathf.Lerp(0.25f, 0.9f, Mathf.Sqrt(strength)), 20f, Random.Range(0.93f, 1.07f));
                    // A big splash right in front of the lens.
                    var cam = Camera.main;
                    if (cam && strength > 0.15f && Vector3.Distance(cam.transform.position, at) < 1.3f) Camcorder.LensDrops.Add(Mathf.RoundToInt(strength * 6f) + 1, 0.8f);
                }
                s.inWater = true;
            }

            // A sinking thing trails bubbles for a little while (the air coming out of it).
            if (s.density > 1f && rb.linearVelocity.y < -0.05f && Time.time < s.splashAt + 1.2f && Random.value < 0.25f)
                PoolSplashes.Bubbles(new Vector3(c.x, level, c.z), 2, 0.05f, Mathf.Max(0.05f, level - c.y));

            // Water drag (against the pool's slow current: floating things drift with it, PoolCirculation), and the wake
            // of you wading past pushing it aside and along.
            var current = s.density < 1f ? PoolCirculation.CurrentAt(c, _player ? _player.position : c) : Vector3.zero;
            rb.AddForce(-(rb.linearVelocity - current) * (drag * frac), ForceMode.Acceleration);
            rb.AddTorque(-rb.angularVelocity * (angularDrag * frac), ForceMode.Acceleration);
            if (wading && toMe.sqrMagnitude < 1.4f * 1.4f)
            {
                float d = toMe.magnitude;
                Vector3 away = d > 1e-3f ? toMe / d : Vector3.forward;
                Vector3 push = (away * 0.6f + _ctrl.PlanarVelocity.normalized * 0.4f) * _ctrl.PlanarVelocity.magnitude * (1f - d / 1.4f) * 1.6f;
                rb.AddForce(push * frac / Mathf.Max(0.5f, s.density), ForceMode.Acceleration);
            }
        }

        State Make(Rigidbody rb, MovableProp prop, WorldItem item)
        {
            // The body's colliders' bounds, in its own frame.
            var t = rb.transform;
            bool any = false;
            var b = new Bounds();
            foreach (var col in rb.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || !col.enabled) continue;
                var wb = col.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = t.InverseTransformPoint(new Vector3((k & 1) != 0 ? wb.max.x : wb.min.x, (k & 2) != 0 ? wb.max.y : wb.min.y, (k & 4) != 0 ? wb.max.z : wb.min.z));
                    if (!any) { b = new Bounds(corner, Vector3.zero); any = true; } else b.Encapsulate(corner);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            var e = b.extents * 0.5f;                 // the middle of each octant
            var pts = new Vector3[8];
            for (int k = 0; k < 8; k++) pts[k] = b.center + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
            return new State { points = pts, half = Mathf.Max(0.02f, b.extents.y), density = Density(prop, item), size = b.size.magnitude };
        }

        /// <summary>How dense a thing is against water (below 1 floats; the share of it under water when floating).</summary>
        static float Density(MovableProp prop, WorldItem item)
        {
            if (prop)
            {
                var spec = prop.Spec;
                if (spec != null && spec.cardboard) return 0.3f;                  // an empty carton rides high
                if (spec != null && spec.kind == MovableKind.Pallet) return 0.55f;   // wood
                return 2.5f;                                                       // furniture stands on the bottom
            }
            if (item is RubberChicken) return 0.25f;
            if (item is TapeRoll) return 0.7f;
            if (item is SprayCan) return 0.85f;
            if (item is Chalk) return 1.5f;
            if (item is Plug) return 2.2f;
            if (item is Hammer || item is Sledgehammer) return 4f;
            if (item is Nails) return 5f;
            return 1.6f;
        }
    }
}
