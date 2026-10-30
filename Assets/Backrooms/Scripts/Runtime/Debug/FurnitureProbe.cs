using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks furniture handling (PropGrabber.Furniture) with the inputs a player gives: an office chair is put in front of
    /// the player, its back is taken and tipped over with the wrist (R), the camcorder goes onto its strap (H), the chair
    /// lying there is taken with both hands, lifted, turned upright and set down. Screenshots and a log of how it lay at
    /// each step. Add in Play Mode in a world (World 3), set <see cref="folder"/>.
    /// </summary>
    public class FurnitureProbe : MonoBehaviour
    {
        public string folder;
        /// <summary>Which piece of furniture (an ItemCatalog name).</summary>
        public string furniture = "Office chair";
        public bool done;
        readonly StringBuilder _log = new StringBuilder();

        CamcorderPlayerController _ctrl;
        PropGrabber _grab;
        GameObject _chair;

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            _ctrl = ws.player.GetComponent<CamcorderPlayerController>();
            _grab = ws.player.GetComponent<PropGrabber>();
            ItemCatalog.Entry entry = null;
            foreach (var e in ItemCatalog.Build(ws.assets, ws)) if (entry == null && e.name.StartsWith(furniture)) entry = e;
            if (entry == null) { Finish("no " + furniture + " in the catalog"); yield break; }
            _log.AppendLine("furniture: " + entry.name);

            Vector3 fwd = Vector3.ProjectOnPlane(ws.player.forward, Vector3.up).normalized;
            Vector3 at = ws.player.position + fwd * 1.0f;
            _chair = entry.spawn(at, Quaternion.LookRotation(fwd), false);       // its back toward the player
            yield return new WaitForSeconds(1.2f);
            Log("spawned");

            // 1. Take the top of the back, one hand; R held, mouse up, stepping in: tip it away with the wrist.
            yield return Aim(BackTop());
            yield return Hold();
            Log("held one-handed");
            Shot("1_held");
            for (int i = 0; i < 90; i++) { _grab.botTwist = true; _grab.botMouse = new Vector2(0f, 7f); _ctrl.botInput = new Vector2(0f, 0.35f); yield return null; }
            _grab.botTwist = null; _grab.botMouse = Vector2.zero; _ctrl.botInput = null;
            Log("tipped with the wrist");
            Shot("2_tipping");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.5f);
            Log("let go");
            Shot("3_down");

            // Stand it up again for the two-handed part (as found).
            _chair.transform.SetPositionAndRotation(_ctrl.transform.position + Vector3.ProjectOnPlane(_ctrl.transform.forward, Vector3.up).normalized * 0.9f, Quaternion.LookRotation(_ctrl.transform.forward));
            var body = _chair.GetComponent<MovableProp>().Body; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(1f);

            // 2. The camcorder onto its strap.
            _grab.botStrap = true;
            yield return new WaitForSeconds(1.5f);
            Log("camcorder on strap");
            Shot("4_strap");

            // 3. Both hands on its back; roll it over onto its side with its feet on the floor (as you would); lower it; let go.
            yield return Aim(BackTop());
            yield return Hold();
            Log("held two-handed=" + _grab.TwoHanded);
            Shot("5_held_two");
            for (int i = 0; i < 20; i++) { _grab.botMouse = new Vector2(0f, 3f); yield return null; }
            _grab.botMouse = Vector2.zero;
            Log("taken up a little");
            Shot("6_lifted");
            for (int i = 0; i < 240 && Vector3.Angle(_chair.transform.up, Vector3.up) < 85f; i++)
            {
                _grab.botTwist = true; _grab.botMouse = new Vector2(5f, 0f);
                if (i % 20 == 0) Trace("rolling " + i);
                yield return null;
            }
            _grab.botTwist = null; _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.4f);
            Log("rolled");
            Shot("7_rolled");
            for (int i = 0; i < 200 && _grab.Current == PropGrabber.State.Holding && _chair.GetComponent<MovableProp>().WorldBounds.min.y > 0.03f; i++)
            {
                _grab.botMouse = new Vector2(0f, -6f);
                if (i % 20 == 0) Trace("lowering " + i);
                yield return null;
            }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.5f);
            Log("set on its side");
            Shot("8_on_side");

            // 4. Take it as it lies, with both hands; lift; turn it upright; set it down.
            var bb = _chair.GetComponent<MovableProp>().WorldBounds;
            yield return Aim(bb.center + Vector3.up * bb.extents.y * 0.8f);
            yield return Hold();
            Log("held lying, two-handed=" + _grab.TwoHanded);
            Shot("9_held_lying");
            for (int i = 0; i < 40; i++) { _grab.botMouse = new Vector2(0f, 6f); yield return null; }
            _grab.botMouse = Vector2.zero;
            for (int i = 0; i < 150 && Vector3.Dot(_chair.transform.up, Vector3.up) < 0.96f; i++)
            {
                Vector3 axis = Vector3.Cross(_chair.transform.up, Vector3.up);
                Vector3 local = Quaternion.Inverse(_ctrl.transform.rotation) * axis;
                _grab.botTwist = true;
                _grab.botMouse = new Vector2(-local.z * 10f, local.x * 10f);
                yield return null;
            }
            _grab.botTwist = null; _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.4f);
            Log("turned upright");
            Shot("10_upright");
            for (int i = 0; i < 200 && _grab.Current == PropGrabber.State.Holding && _chair.GetComponent<MovableProp>().WorldBounds.min.y > 0.03f; i++) { _grab.botMouse = new Vector2(0f, -6f); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.5f);
            Log("set down");
            Shot("11_set");
            _grab.botStrap = true;
            yield return new WaitForSeconds(1f);
            Finish("ok");
        }

        /// <summary>The top of its back (backs face -Z in the model).</summary>
        Vector3 BackTop()
        {
            var sb = _chair.GetComponent<MovableProp>().Shape.bounds;
            return _chair.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.03f, sb.min.z + 0.06f));
        }

        IEnumerator Aim(Vector3 p)
        {
            var eye = _ctrl.cameraPitchPivot ? _ctrl.cameraPitchPivot.position : _ctrl.transform.position + Vector3.up * 1.58f;
            Vector3 d = p - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            _ctrl.TeleportSafe(_ctrl.transform.position, yaw, pitch);
            yield return null; yield return null;
            // The camera, not the eye, aims the grab: nudge until its ray meets the chair.
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

        IEnumerator Hold()
        {
            _grab.botHold = true;
            float t = 0f;
            while (_grab.Current != PropGrabber.State.Holding && t < 4f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
        }

        void Log(string what)
        {
            var c = _chair ? _chair.transform : null;
            float tilt = c ? Vector3.Angle(c.up, Vector3.up) : -1f;
            _log.AppendLine($"{Time.time:F1}s {what}: state={_grab.Current} twoHanded={_grab.TwoHanded} strap={_grab.CamcorderOnStrap} " +
                            $"chair tilt={tilt:F0} up={(c ? c.up.ToString("F2") : "-")} height={(c ? c.position.y.ToString("F2") : "-")} strain={_grab.Strain:F2} last release='{_grab.LastRelease}'");
        }

        void Trace(string what)
        {
            var mp = _chair ? _chair.GetComponent<MovableProp>() : null;
            Vector3 s = _grab.StandShoulder, r = _grab.ReachShoulder;
            _log.AppendLine($"  {what}: state={_grab.Current} two={_grab.TwoHanded} chairCOM={(mp ? mp.Body.worldCenterOfMass.ToString("F2") : "-")} " +
                            $"second={_grab.SecondGrip.ToString("F2")} standShoulder={s.ToString("F2")} reachShoulder={r.ToString("F2")} stoop={_ctrl.stoop:F2} strain={_grab.Strain:F2}");
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));

        void Finish(string why)
        {
            _log.AppendLine("finished: " + why);
            File.WriteAllText(Path.Combine(folder, "furniture.txt"), _log.ToString());
            done = true;
        }
    }
}
