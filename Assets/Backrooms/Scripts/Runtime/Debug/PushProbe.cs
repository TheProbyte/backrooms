using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks pushing furniture (PropGrabber.Furniture): a piece is put in front of the player and taken by its back, then
    /// pushed ahead, steered round, pulled back, heaved up into both hands (a sharp mouse move up), lifted, lowered onto the
    /// floor and set down (back to pushing). Logs where it went and how it lay, and the frame-to-frame shake, with
    /// screenshots. Add in Play Mode in a world (World 3), set <see cref="folder"/>.
    /// </summary>
    public class PushProbe : MonoBehaviour
    {
        public string folder;
        public string furniture = "Armchair";
        public bool done;

        readonly StringBuilder _log = new StringBuilder();
        CamcorderPlayerController _ctrl;
        PropGrabber _grab;
        GameObject _chair;
        MovableProp _mp;

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            _ctrl = ws.player.GetComponent<CamcorderPlayerController>();
            _grab = ws.player.GetComponent<PropGrabber>();
            ItemCatalog.Entry entry = null;
            foreach (var e in ItemCatalog.Build(ws.assets, ws)) if (entry == null && e.name.StartsWith(furniture)) entry = e;
            if (entry == null) { Finish("no " + furniture); yield break; }
            _log.AppendLine("furniture: " + entry.name);
            // Face the most open way (knee high), so it has room to be pushed and steered.
            {
                Vector3 from = ws.player.position + Vector3.up * 0.5f;
                float bestD = -1f, bestYaw = 0f;
                for (int i = 0; i < 24; i++)
                {
                    float yaw = i * 15f;
                    Vector3 d = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    float free = Physics.SphereCast(from, 0.3f, d, out var h, 12f, ~0, QueryTriggerInteraction.Ignore) ? h.distance : 12f;
                    if (free > bestD) { bestD = free; bestYaw = yaw; }
                }
                _ctrl.TeleportSafe(ws.player.position, bestYaw, 0f);
                _log.AppendLine($"facing the most open way: {bestD:F1} m clear");
                yield return null; yield return null;
            }
            Vector3 fwd = Vector3.ProjectOnPlane(ws.player.forward, Vector3.up).normalized;
            _chair = entry.spawn(ws.player.position + fwd * 1.0f, Quaternion.LookRotation(fwd), false);
            _mp = _chair.GetComponent<MovableProp>();
            yield return new WaitForSeconds(1.2f);

            var sb = _mp.Shape.bounds;
            yield return Aim(_chair.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.03f, sb.min.z + 0.06f)));
            _grab.botHold = true;
            float t0 = 0f;
            while (_grab.Current != PropGrabber.State.Holding && t0 < 4f) { t0 += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.8f);
            Log("taken");
            Shot("1_taken");

            Vector3 p0 = _chair.transform.position;
            float shake = 0f;
            yield return Run(2.5f, new Vector2(0f, 1f), 0f, Vector2.zero, s => shake = s);
            Log($"pushed ahead {Flat(_chair.transform.position - p0):F2} m, shake {shake * 1000f:F1} mm");
            Shot("2_pushed");

            float yaw0 = _chair.transform.eulerAngles.y;
            yield return Run(2.5f, new Vector2(0f, 0.8f), 40f, Vector2.zero, s => shake = s);
            Log($"steered: it turned {Mathf.DeltaAngle(yaw0, _chair.transform.eulerAngles.y):F0} deg, shake {shake * 1000f:F1} mm");
            Shot("3_steered");

            p0 = _chair.transform.position;
            yield return Run(1.5f, new Vector2(0f, -0.8f), 0f, Vector2.zero, s => shake = s);
            Log($"pulled back {Flat(_chair.transform.position - p0):F2} m");

            // Heave: a sharp move up.
            yield return Run(0.15f, null, 0f, new Vector2(0f, 120f), s => shake = s);
            yield return new WaitForSeconds(0.8f);
            Log("heaved");
            Shot("4_heaved");
            yield return Run(0.8f, null, 0f, new Vector2(0f, 6f), s => shake = s);
            yield return new WaitForSeconds(0.4f);
            Log($"lifted, bottom {_mp.WorldBounds.min.y - _ctrl.transform.position.y:F2} m off the floor");
            Shot("5_lifted");

            // Lower onto the floor, then keep lowering sharply: pushing again.
            for (int i = 0; i < 150 && _mp.WorldBounds.min.y > _ctrl.transform.position.y + 0.04f && _grab.Current == PropGrabber.State.Holding; i++)
            { _grab.botMouse = new Vector2(0f, -6f); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.4f);
            Log("lowered");
            yield return Run(0.2f, null, 0f, new Vector2(0f, -120f), s => shake = s);
            yield return new WaitForSeconds(0.8f);
            Log("set down");
            Shot("6_set_down");
            p0 = _chair.transform.position;
            yield return Run(1.5f, new Vector2(0f, 1f), 0f, Vector2.zero, s => shake = s);
            Log($"pushed again {Flat(_chair.transform.position - p0):F2} m");

            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(0.8f);
            Log("let go");
            Finish("ok");
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>Walks, turns and moves the mouse for a while; reports the furniture's frame-to-frame shake (RMS of each
        /// position's distance from the midpoint of its neighbours, m).</summary>
        IEnumerator Run(float secs, Vector2? walk, float turnRate, Vector2 mouse, System.Action<float> shake)
        {
            Vector3 a = _chair.transform.position, b = a;
            float ta = 0f, tb = 0f;
            double sum = 0; int n = 0;
            for (float t = 0f; t < secs; t += Time.deltaTime)
            {
                _ctrl.botInput = walk;
                _grab.botMouse = mouse;
                if (turnRate != 0f) _ctrl.AddLook(turnRate * Time.deltaTime, 0f);
                yield return new WaitForEndOfFrame();
                Vector3 c = _chair.transform.position;
                float tc = Time.time;
                // (Against the straight line between its neighbours at the frames' own times: frames are not evenly spaced.)
                if (t > 0.1f && tc > ta + 1e-4f) { sum += (b - Vector3.Lerp(a, c, (tb - ta) / (tc - ta))).sqrMagnitude; n++; }
                a = b; b = c; ta = tb; tb = tc;
            }
            _ctrl.botInput = null; _grab.botMouse = Vector2.zero;
            shake((float)System.Math.Sqrt(sum / Mathf.Max(1, n)));
        }

        IEnumerator Aim(Vector3 p)
        {
            var eye = _ctrl.cameraPitchPivot ? _ctrl.cameraPitchPivot.position : _ctrl.transform.position + Vector3.up * 1.58f;
            Vector3 d = p - eye;
            _ctrl.TeleportSafe(_ctrl.transform.position, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
            yield return null; yield return null;
            var cam = Camera.main.transform;
            for (int i = 0; i < 30; i++)
            {
                if (Physics.Raycast(cam.position, cam.forward, out var hit, 2.5f) && hit.collider.GetComponentInParent<MovableProp>()) break;
                Vector3 to = (p - cam.position).normalized;
                float dy = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.forward, Vector3.up), Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up);
                float dp = Mathf.Asin(Mathf.Clamp(-to.y, -1f, 1f)) * Mathf.Rad2Deg - Mathf.Asin(Mathf.Clamp(-cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                _ctrl.AddLook(dy * 0.5f, -dp * 0.5f);
                yield return null;
            }
        }

        void Log(string what)
        {
            var c = _chair.transform;
            var cam = Camera.main.transform;
            _log.AppendLine($"{Time.time:F1}s {what}: state={_grab.Current} pushing={_grab.Pushing} twoHanded={_grab.TwoHanded} strap={_grab.CamcorderOnStrap} " +
                            $"tilt={Vector3.Angle(c.up, Vector3.up):F0} height={c.position.y - _ctrl.transform.position.y:F2} camera {cam.position.y - _ctrl.transform.position.y:F2} m up, " +
                            $"strain={_grab.Strain:F2} last release='{_grab.LastRelease}'");
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));

        void Finish(string why)
        {
            _log.AppendLine("finished: " + why);
            File.WriteAllText(Path.Combine(folder, "push.txt"), _log.ToString());
            done = true;
        }
    }
}
