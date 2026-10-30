using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks the round 12 handling with a player's inputs, in open floor: a couch taken by the top of its back (pushed,
    /// steered, lifted a little), then by the underside of its back (tipped up); an office chair and a box carried; the
    /// pockets as homes (put away, switch, move to an empty pocket, a new thing trading places), and a click on the couch
    /// with the hammer in hand (put away, taken back out on letting go). Logs the hint line at each step; screenshots.
    /// Add in Play Mode in a world (World 3), set <see cref="folder"/>.
    /// </summary>
    public class HandlingProbe : MonoBehaviour
    {
        public string folder;
        public string couch = "Chesterfield sofa";
        public bool done;

        readonly StringBuilder _log = new StringBuilder();
        WorldStreamer _ws;
        CamcorderPlayerController _ctrl;
        PropGrabber _grab;

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            _ws = WorldStreamer.Current;
            _ctrl = _ws.player.GetComponent<CamcorderPlayerController>();
            _grab = _ws.player.GetComponent<PropGrabber>();
            var catalog = ItemCatalog.Build(_ws.assets, _ws);
            if (!FindOpen(out var at, out float yaw)) { Finish("no open floor"); yield break; }
            _ctrl.Teleport(at, yaw);
            yield return new WaitForSeconds(0.5f);
            Vector3 fwd = Flat(_ctrl.transform.forward).normalized;

            // ---- The couch, its back toward you ----
            var e = catalog.Find(x => x.name.StartsWith(couch));
            var sofa = e.spawn(_ctrl.transform.position + fwd * 1.3f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1.2f);
            var sb = sofa.Shape.bounds;
            yield return AimDirect(sofa.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.03f, sb.min.z + 0.06f)));
            _log.AppendLine($"aimed at the top of its back: hint '{_grab.HintText}'");
            Shot("1_couch_aimed");
            yield return Hold();
            Log("couch by the top");
            Vector3 p0 = sofa.transform.position;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                _ctrl.botInput = new Vector2(0f, 1f);
                if (Mathf.Repeat(t, 0.25f) < Time.deltaTime)
                {
                    var rb = sofa.Body; Vector3 ahead = sofa.transform.position + Flat(_ctrl.transform.forward).normalized * 1.2f;
                    string blk = Physics.Raycast(sofa.WorldBounds.center, Flat(_ctrl.transform.forward).normalized, out var bh, 1.5f, ~0, QueryTriggerInteraction.Ignore) && !bh.collider.transform.IsChildOf(sofa.transform) ? $"{bh.collider.name} {bh.distance:F2}" : "-";
                    _log.AppendLine($"   {t:F2}s moved {Flat(sofa.transform.position - p0).magnitude:F2} tilt {Tilt(sofa):F0} up {sofa.transform.up.ToString("F2")} angvel {rb.angularVelocity.ToString("F2")} strain {_grab.Strain:F2} two {_grab.TwoHanded} ahead {blk}");
                }
                yield return null;
            }
            _ctrl.botInput = null;
            Log($"pushed {Flat(sofa.transform.position - p0).magnitude:F2} m");
            Shot("2_couch_pushed");
            float y0 = sofa.transform.eulerAngles.y;
            for (float t = 0f; t < 2f; t += Time.deltaTime) { _ctrl.AddLook(40f * Time.deltaTime, 0f); _ctrl.botInput = new Vector2(0f, 0.7f); yield return null; }
            _ctrl.botInput = null;
            Log($"steered: it turned {Mathf.DeltaAngle(y0, sofa.transform.eulerAngles.y):F0} deg as you turned 80");
            Shot("2b_couch_steered");
            float low0 = sofa.WorldBounds.min.y;
            for (int i = 0; i < 40; i++) { _grab.botMouse = new Vector2(0f, 4f); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            Log($"lifted: bottom up {sofa.WorldBounds.min.y - low0:F3} m, tilt {Tilt(sofa):F0} deg, hint '{_grab.HintText}'");
            Shot("3_couch_lifted");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.2f);
            Log($"let go: tilt {Tilt(sofa):F0} deg");

            // ---- The couch by the underside of its back: tipped up ----
            yield return StandBehind(sofa, 1.35f);
            sb = sofa.Shape.bounds;
            Vector3 low = sofa.transform.TransformPoint(new Vector3(sb.center.x, sb.min.y + 0.06f, sb.min.z - 0.02f));
            yield return AimDirect(low);
            _log.AppendLine($"aimed at the bottom of its back: hint '{_grab.HintText}'");
            Shot("4_couch_aimed_low");
            yield return Hold();
            Log("couch by the underside");
            for (int i = 0; i < 160; i++) { _grab.botMouse = new Vector2(0f, 5f); if (i % 40 == 0) Log($"  tipping {i}: tilt {Tilt(sofa):F0}"); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            Log($"tipped: tilt {Tilt(sofa):F0} deg, hint '{_grab.HintText}'");
            Shot("5_couch_tipped");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.5f);
            Log($"let go: tilt {Tilt(sofa):F0} deg (90: on its back)");
            Shot("6_couch_after");

            // ---- An office chair, carried ----
            yield return Away(sofa.transform.position, 3f);
            fwd = Flat(_ctrl.transform.forward).normalized;
            var ce = catalog.Find(x => x.name == "Office chair");
            var chair = ce.spawn(_ctrl.transform.position + fwd * 1.0f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            var cb = chair.Shape.bounds;
            yield return AimDirect(chair.transform.TransformPoint(new Vector3(cb.center.x, cb.max.y - 0.03f, cb.min.z + 0.05f)));
            _log.AppendLine($"aimed at the office chair: hint '{_grab.HintText}'");
            yield return Hold();
            yield return new WaitForSeconds(0.8f);
            Log($"office chair: {Carry(chair)}");
            yield return Walk(new Vector2(0f, 0.8f), 1.5f);
            Log($"walked: {Carry(chair)}");
            Shot("7_chair_carried");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.2f);
            Log($"let go: tilt {Tilt(chair):F0}");

            // ---- A box, carried ----
            yield return Away(sofa.transform.position, 3f);
            fwd = Flat(_ctrl.transform.forward).normalized;
            var be = catalog.Find(x => x.name == "Cardboard box (medium)");
            var box = be.spawn(_ctrl.transform.position + fwd * 0.9f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            yield return AimDirect(box.WorldBounds.center + Vector3.up * box.WorldBounds.extents.y * 0.9f);
            _log.AppendLine($"aimed at the box: hint '{_grab.HintText}'");
            yield return Hold();
            yield return new WaitForSeconds(0.8f);
            _ctrl.TeleportSafe(_ctrl.transform.position, _ctrl.transform.eulerAngles.y, 5f);
            yield return new WaitForSeconds(0.3f);
            Log($"box: {Carry(box)}");
            Shot("8_box_carried");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);

            yield return Pockets(sofa);
            Finish("ok");
        }

        IEnumerator Pockets(MovableProp sofa)
        {
            yield return Away(sofa.transform.position, 3f);
            _ctrl.TeleportSafe(_ctrl.transform.position, _ctrl.transform.eulerAngles.y, 20f);
            yield return null;
            var hammer = Spawn("hammer"); var chalk = Spawn("chalk");
            yield return new WaitForSeconds(0.8f);
            yield return Take(hammer);
            _log.AppendLine($"hammer in hand={_grab.HeldItem == hammer} (hammer at {hammer.transform.position - _ctrl.transform.position}): hint '{_grab.HintText}'");
            yield return Key(1);  _log.AppendLine($"key 2 (hammer away): {State()}");
            yield return Take(chalk);
            yield return Key(0);  _log.AppendLine($"key 1 (chalk away): {State()}");
            yield return Key(1);  _log.AppendLine($"key 2 (hand empty, hammer out): {State()}");
            yield return Key(0);  _log.AppendLine($"key 1 (switch: hammer back to 2, chalk out): {State()}");
            Shot("9_pockets");
            yield return Key(2);  _log.AppendLine($"key 3 (empty: chalk moves to 3): {State()}");
            var nails = Spawn("nails");
            yield return new WaitForSeconds(0.8f);
            yield return Take(nails);
            yield return Key(1);  _log.AppendLine($"key 2 with new nails (trade: nails in 2, hammer out, no pocket): {State()}");
            // The hammer in hand, a click on an armchair: it goes into a pocket, the chair is taken; let go: back out.
            yield return Away(sofa.transform.position, 3f);
            Vector3 fwd = Flat(_ctrl.transform.forward).normalized;
            var ae = ItemCatalog.Build(_ws.assets, _ws).Find(x => x.name.StartsWith("Armchair"));
            var arm = ae.spawn(_ctrl.transform.position + fwd * 1.2f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            var sb = arm.Shape.bounds;
            yield return AimDirect(arm.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.03f, sb.min.z + 0.06f)));
            _log.AppendLine($"hammer in hand, aimed at an armchair: hint '{_grab.HintText}'");
            yield return Hold();
            _log.AppendLine($"clicked the armchair with the hammer: {State()}, holding={_grab.Current} {_grab.Handling}");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(0.8f);
            _log.AppendLine($"let go of the armchair: {State()}");
            Saves.WorldSaver.SaveNow();
            var data = Saves.SaveSlots.Load(Saves.GameSession.Slot);
            var s = new StringBuilder();
            if (data != null) foreach (var it in data.items) if (it.pocket >= 0 || it.held) s.Append($"{it.id}{(it.held ? $" held (home {it.home + 1})" : $" in {it.pocket + 1}")}; ");
            _log.AppendLine($"saved: {s}");
        }

        WorldItem Spawn(string id) => ItemFactory.Spawn(id, _ctrl.transform.position + Flat(_ctrl.transform.forward).normalized * 0.5f + Vector3.up * 0.9f, Quaternion.identity);

        IEnumerator Take(WorldItem it)
        {
            _grab.PickUp(it);
            for (int i = 0; i < 200 && (_grab.HeldItem != it || _grab.ItemWaitingForHand); i++) yield return null;
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator Key(int i) { _grab.botPocket = i; yield return new WaitForSeconds(0.9f); }

        string State()
        {
            var sb = new StringBuilder($"hand={(_grab.HeldItem ? _grab.HeldItem.itemId : "-")}(home {_grab.HeldHome + 1}) ");
            for (int i = 0; i < PropGrabber.PocketCount; i++) sb.Append($"[{i + 1} {(_grab.Pocket(i) ? _grab.Pocket(i).itemId : "-")}]");
            return sb.ToString();
        }

        string Carry(MovableProp p)
        {
            var local = _ctrl.transform.InverseTransformPoint(p.WorldBounds.center);
            return $"handling={_grab.Handling} strap={_grab.CamcorderOnStrap} two={_grab.TwoHanded}, middle x {local.x:F2} y {local.y:F2} z {local.z:F2}, " +
                   $"bottom {p.WorldBounds.min.y - _ctrl.transform.position.y:F2} m up, tilt {Tilt(p):F0}, hint '{_grab.HintText}', last release '{_grab.LastRelease}'";
        }

        static float Tilt(MovableProp p) => Vector3.Angle(p.transform.up, Vector3.up);
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Behind the couch (its back, -Z, toward you), facing it.</summary>
        IEnumerator StandBehind(MovableProp p, float d)
        {
            Vector3 back = Flat(-p.transform.forward).normalized;
            Vector3 s = p.WorldBounds.center + back * d; s.y = _ctrl.transform.position.y;
            _ctrl.Teleport(s, Quaternion.LookRotation(-back).eulerAngles.y);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>Away from a point, to open floor (the same spot finder), facing the open way.</summary>
        IEnumerator Away(Vector3 from, float min)
        {
            if (FindOpen(out var at, out float yaw, from, min)) _ctrl.Teleport(at, yaw);
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator AimDirect(Vector3 p)
        {
            for (int k = 0; k < 2; k++)
            {
                Vector3 d = p - Camera.main.transform.position;
                _ctrl.TeleportSafe(_ctrl.transform.position, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                yield return null; yield return null;
            }
            yield return null;
        }

        IEnumerator Walk(Vector2 dir, float secs)
        {
            for (float t = 0f; t < secs; t += Time.deltaTime) { _ctrl.botInput = dir; yield return null; }
            _ctrl.botInput = null;
        }

        IEnumerator Hold()
        {
            _grab.botHold = true;
            float t = 0f;
            while (_grab.Current != PropGrabber.State.Holding && t < 4f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.5f);
        }

        bool FindOpen(out Vector3 at, out float yaw) => FindOpen(out at, out yaw, _ctrl.transform.position, 0f);

        /// <summary>Open floor within 12 m (at least min from c): room to stand, and 3 m wide by 6 m long clear ahead.</summary>
        bool FindOpen(out Vector3 at, out float yaw, Vector3 c, float min)
        {
            for (int r = Mathf.CeilToInt(min); r <= 12; r++)
            for (int a = 0; a < (r == 0 ? 1 : 24); a++)
            {
                Vector3 s = c + Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward * r;
                if (!Physics.Raycast(s + Vector3.up * 0.5f, Vector3.down, out var fl, 1f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(fl.point.y - c.y) > 0.1f) continue;
                s.y = fl.point.y;
                if (Physics.CheckCapsule(s + Vector3.up * 0.4f, s + Vector3.up * 1.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                for (int k = 0; k < 8; k++)
                {
                    float y = k * 45f;
                    Vector3 f = Quaternion.Euler(0f, y, 0f) * Vector3.forward;
                    if (Physics.CheckBox(s + f * 3.4f + Vector3.up * 1.0f, new Vector3(1.5f, 0.9f, 3f), Quaternion.LookRotation(f), ~0, QueryTriggerInteraction.Ignore)) continue;
                    at = s; yaw = y; return true;
                }
            }
            at = default; yaw = 0f; return false;
        }

        void Log(string what)
        {
            _log.AppendLine($"{Time.time:F1}s {what}: state={_grab.Current} handling={_grab.Handling} two={_grab.TwoHanded} strap={_grab.CamcorderOnStrap} last release='{_grab.LastRelease}'");
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));

        void Finish(string why)
        {
            _log.AppendLine("finished: " + why);
            File.WriteAllText(Path.Combine(folder, "handling.txt"), _log.ToString());
            done = true;
        }
    }
}
