using System.Collections;
using System.Collections.Generic;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Scripted check of grabbing: for each kind of movable prop near the player it walks up, grabs, lifts,
    /// drags back, swings and shoves, taking screenshots and logging how far the prop moved. Then it walks
    /// away until the prop's chunk unloads, comes back and checks the prop is where it was left.
    /// Add in play mode and call Run(); read Report when Busy goes false.
    /// </summary>
    public class GrabProbe : MonoBehaviour
    {
        public string folder = "";
        public string prefix = "grab";
        public MovableKind[] kinds = { MovableKind.Box, MovableKind.OfficeChair, MovableKind.Sofa, MovableKind.Table };
        public bool Busy { get; private set; }
        public string Report = "";

        CamcorderPlayerController _pl;
        PropGrabber _grab;
        WorldStreamer _ws;
        readonly StringBuilder _log = new StringBuilder();

        static GrabProbe _running;

        /// <summary>Starts the probe (one at a time: a second Run while another probe works is ignored).</summary>
        public void Run()
        {
            if (Busy || (_running && _running.Busy)) return;
            _running = this;
            StartCoroutine(Go());
        }

        IEnumerator Go()
        {
            Busy = true; _log.Clear();
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _grab = _pl.GetComponent<PropGrabber>();
            _ws = FindAnyObjectByType<WorldStreamer>();
            for (int i = 0; i < 30; i++) yield return null;
            foreach (var k in kinds) yield return Try(k);
            Report = _log.ToString();
            Debug.Log("[GrabProbe]\n" + Report);
            Busy = false;
        }

        IEnumerator Try(MovableKind kind)
        {
            // Nearest one of this kind that has open floor around it.
            MovableProp prop = null; float best = float.MaxValue;
            foreach (var p in FindObjectsByType<MovableProp>())
            {
                if (p.Spec.kind != kind || p.held) continue;
                float d = (p.transform.position - _pl.transform.position).sqrMagnitude;
                if (d < best && Stand(p, out _, out _)) { best = d; prop = p; }
            }
            if (!prop && TravelTo(kind))
            {
                yield return Wait(4f);
                foreach (var p in FindObjectsByType<MovableProp>())
                {
                    if (p.Spec.kind != kind || p.held) continue;
                    float d = (p.transform.position - _pl.transform.position).sqrMagnitude;
                    if (d < best && Stand(p, out _, out _)) { best = d; prop = p; }
                }
            }
            if (!prop) { _log.AppendLine($"{kind}: none found nearby"); yield break; }
            Stand(prop, out var stand, out var aim);
            Vector3 to = aim - (stand + Vector3.up * 1.58f);
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            _pl.Teleport(stand, yaw, pitch);
            for (int i = 0; i < 20; i++) yield return null;

            var p0 = prop.transform.position; var r0 = prop.transform.rotation;
            ulong id = prop.Spec.id;
            _grab.botHold = true; _grab.botMouse = Vector2.zero;
            yield return Wait(1.6f);
            bool held = _grab.Current == PropGrabber.State.Holding;
            Shot(kind, "1_grab");
            Trace("grab");
            float hand0 = _grab.HandPosition.y;
            _grab.botMouse = new Vector2(0f, 9f);
            yield return Wait(1.2f);
            Shot(kind, "2_lift");
            Trace("lift");
            float lifted = _grab.HandPosition.y - hand0;
            float tilt = prop ? Vector3.Angle(prop.transform.up, Vector3.up) : 0f;
            _grab.botMouse = new Vector2(0f, -9f);
            yield return Wait(0.8f);
            _grab.botMouse = Vector2.zero;
            _pl.botInput = new Vector2(0f, -0.8f);
            yield return Wait(1.5f);
            _pl.botInput = null;
            Shot(kind, "3_drag");
            Trace("drag");
            _grab.botMouse = new Vector2(12f, 0f);
            yield return Wait(0.8f);
            _grab.botMouse = Vector2.zero;
            Shot(kind, "4_swing");
            Trace("swing");
            bool stillHeld = _grab.Current == PropGrabber.State.Holding;
            _grab.botShove = true;
            yield return Wait(0.2f);
            _grab.botHold = null;
            yield return Wait(2.5f);
            Shot(kind, "5_shoved");
            if (!prop) { _log.AppendLine($"{kind}: prop vanished"); yield break; }
            var p1 = prop.transform.position;
            float moved = Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z));
            float turned = Quaternion.Angle(r0, prop.transform.rotation);
            var mgr = MovablePropManager.Instance;
            _log.AppendLine($"{kind} (mass {prop.Mass:F0} kg): reached+held={held}, held after drag/swing={stillHeld}, lifted hand {lifted:F2} m (tilt {tilt:F0} deg), " +
                            $"moved {moved:F2} m, turned {turned:F0} deg, frozen={prop.Body.isKinematic}, recorded={mgr && mgr.IsMoved(id)}, y={p1.y - p0.y:F2}");

            // Walk away (teleport) until its chunk unloads, then come back.
            var chunk = mgr ? mgr.ChunkKey(p1) : default;
            Vector3 back = _pl.transform.position;
            _pl.Teleport(back + new Vector3(160f, 0f, 0f), 0f);
            yield return Wait(4f);
            bool unloaded = !prop;
            _pl.Teleport(back, yaw, pitch);
            yield return Wait(4f);
            _log.AppendLine($"   returned: player {_pl.transform.position} (left from {back})");
            MovableProp again = null;
            foreach (var p in FindObjectsByType<MovableProp>()) if (p.Spec.id == id) again = p;
            string where = again ? $"{Vector3.Distance(again.transform.position, p1):F3} m from where it was left" : "missing";
            int copies = 0;
            foreach (var p in FindObjectsByType<MovableProp>()) if (p.Spec.id == id) copies++;
            _log.AppendLine($"   after unload ({chunk}) unloaded={unloaded}: back {where}, copies={copies}");
        }

        /// <summary>Teleports next to the nearest generated prop of this kind (searching the layouts, loaded or not).</summary>
        bool TravelTo(MovableKind kind)
        {
            var want = kind == MovableKind.Box ? PropKind.Box : kind == MovableKind.Sofa || kind == MovableKind.Armchair ? PropKind.Sofa : PropKind.ConferenceTable;
            var c0 = WorldStreamer.ChunkOf(_pl.transform.position);
            for (int r = 1; r <= 8; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                var L = _ws.World.Chunk(new Vector2Int(c0.x + dx, c0.y + dz));
                foreach (var p in L.props)
                {
                    if (p.kind != want) continue;
                    var at = new Vector3((c0.x + dx) * OfficeBiome.ChunkSize + p.position.x, 0f, (c0.y + dz) * OfficeBiome.ChunkSize + p.position.y);
                    // Stand a little way off the prop (the probe picks the exact spot once it has loaded).
                    _pl.Teleport(at + new Vector3(0f, 0f, kind == MovableKind.Table || kind == MovableKind.OfficeChair ? 1.6f : 1.3f), 180f);
                    return true;
                }
            }
            return false;
        }

        /// <summary>A spot 1.1-1.5 m from the prop with floor and a clear view of one of its grab edges.</summary>
        bool Stand(MovableProp p, out Vector3 stand, out Vector3 aim)
        {
            stand = aim = default;
            if (p.Shape == null) return false;
            for (int e = 0; e < p.Shape.edges.Count; e++)
            {
                var g = p.Shape.edges[e];
                Vector3 mid = p.transform.TransformPoint((g.a + g.b) * 0.5f);
                Vector3 along = p.transform.TransformDirection(g.b - g.a).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 c = mid + side * s * 1.15f; c.y = p.transform.position.y;
                    if ((c - p.transform.position).sqrMagnitude < (mid - p.transform.position).sqrMagnitude) continue;   // outside the prop
                    if (Physics.CheckCapsule(c + Vector3.up * 0.4f, c + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Raycast(c + Vector3.up * 0.5f, Vector3.down, 1f)) continue;
                    Vector3 eye = c + Vector3.up * 1.58f;
                    if (Physics.Linecast(eye, mid + (eye - mid).normalized * 0.05f, out var hit) && !hit.collider.GetComponentInParent<MovableProp>()) continue;
                    stand = c; aim = mid; return true;
                }
            }
            return false;
        }

        void Trace(string step)
        {
            var h = _grab.Held;
            _log.AppendLine($"   {step}: {_grab.Current}, strain {_grab.Strain:F2}, grip-shoulder {(_grab.HandPosition - _grab.Shoulder).magnitude:F2} m, stoop {_pl.stoop:F2}" +
                            (h ? $", prop at {h.transform.position} kin={h.Body.isKinematic}" : $", last release: {_grab.LastRelease}"));
        }

        IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }

        void Shot(MovableKind k, string step)
        {
            if (string.IsNullOrEmpty(folder)) return;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, $"{prefix}_{k}_{step}.png"));
        }
    }
}
