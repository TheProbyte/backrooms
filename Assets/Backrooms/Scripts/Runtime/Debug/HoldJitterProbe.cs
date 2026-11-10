using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Measures shake while holding furniture: spawns a piece in front of the player, takes its back, then stands still,
    /// walks, turns and swings it, writing the camera, body and furniture every frame to a CSV (and how much each moves
    /// frame to frame beyond a smooth path). Add in Play Mode in a world (World 3), set <see cref="folder"/>.
    /// </summary>
    public class HoldJitterProbe : MonoBehaviour
    {
        public string folder;
        public string furniture = "Armchair";
        public bool done;

        readonly StringBuilder _csv = new StringBuilder();
        CamcorderPlayerController _ctrl;
        PropGrabber _grab;
        HandheldCameraShake _shake;
        GameObject _chair;
        string _phase = "";

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            _ctrl = ws.player.GetComponent<CamcorderPlayerController>();
            _grab = ws.player.GetComponent<PropGrabber>();
            _shake = FindAnyObjectByType<HandheldCameraShake>();
            ItemCatalog.Entry entry = null;
            foreach (var e in ItemCatalog.Build(ws.assets, ws)) if (entry == null && e.name.StartsWith(furniture)) entry = e;
            if (entry == null) { Finish("no " + furniture); yield break; }
            Vector3 fwd = Vector3.ProjectOnPlane(ws.player.forward, Vector3.up).normalized;
            _chair = entry.spawn(ws.player.position + fwd * 1.0f, Quaternion.LookRotation(fwd), false);
            yield return new WaitForSeconds(1.2f);

            var sb = _chair.GetComponent<MovableProp>().Shape.bounds;
            Vector3 top = _chair.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.03f, sb.min.z + 0.06f));
            yield return Aim(top);
            _grab.botHold = true;
            float t0 = 0f;
            while (_grab.Current != PropGrabber.State.Holding && t0 < 4f) { t0 += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.2f);
            _csv.AppendLine("phase,t,dt,camX,camY,camZ,camYaw,camPitch,camRoll,bodyX,bodyZ,bodyYaw,chX,chY,chZ,chYaw,chPitch,chRoll,chAngVel,swingY,clearY,strap,two,strain");
            Shot("held");

            _phase = "still"; yield return Record(3f, null, Vector2.zero, 0f);
            _phase = "walk"; yield return Record(3f, new Vector2(0f, 0.6f), Vector2.zero, 0f);
            _phase = "turn"; yield return Record(3f, null, Vector2.zero, 60f);
            Shot("turning");
            _phase = "swing"; yield return Record(3f, null, new Vector2(4f, 0f), 0f, true);
            _phase = "walkturn"; yield return Record(3f, new Vector2(0f, 0.6f), Vector2.zero, -45f);
            _ctrl.botInput = null; _grab.botMouse = Vector2.zero;
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);
            Finish("ok");
        }

        IEnumerator Record(float secs, Vector2? walk, Vector2 mouse, float turnRate, bool alternate = false)
        {
            float t = 0f;
            while (t < secs)
            {
                _ctrl.botInput = walk;
                _grab.botMouse = alternate ? mouse * Mathf.Sign(Mathf.Sin(t * Mathf.PI)) : mouse;
                if (turnRate != 0f) _ctrl.AddLook(turnRate * Time.deltaTime, 0f);
                yield return new WaitForEndOfFrame();
                Row(t);
                t += Time.deltaTime;
            }
        }

        void Row(float t)
        {
            var cam = Camera.main.transform;
            var b = _ctrl.transform;
            var c = _chair.transform;
            var rb = _chair.GetComponent<MovableProp>().Body;
            Vector3 ce = cam.eulerAngles, ch = c.eulerAngles;
            var inv = CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("F5", inv);
            _csv.AppendLine(string.Join(",", _phase, F(t), F(Time.deltaTime),
                F(cam.position.x), F(cam.position.y), F(cam.position.z), F(ce.y), F(ce.x), F(ce.z),
                F(b.position.x), F(b.position.z), F(b.eulerAngles.y),
                F(c.position.x), F(c.position.y), F(c.position.z), F(ch.y), F(ch.x), F(ch.z), F(rb.angularVelocity.magnitude),
                F(_shake ? _shake.StrapSwing.y : 0f), F(_shake ? _shake.StrapClear.y : 0f), F(_shake ? _shake.StrapAmount : 0f),
                _grab.TwoHanded ? "1" : "0", F(_grab.Strain)));
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

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));

        void Finish(string why)
        {
            File.WriteAllText(Path.Combine(folder, "jitter.csv"), _csv.ToString());
            File.WriteAllText(Path.Combine(folder, "result.txt"), why);
            done = true;
        }
    }
}
