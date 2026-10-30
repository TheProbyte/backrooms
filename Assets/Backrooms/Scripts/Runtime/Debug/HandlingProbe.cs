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
        [Tooltip("Only the round 14 checks: any edge of a chair on its side, crouched low grabs, a box lifted, a pallet under the arm, hand depths.")]
        public bool round14;
        [Tooltip("Only the round 15 checks: the floral sofa, boxes carried while turning (hand and forearm depths).")]
        public bool round15;

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
            if (round14) { yield return Round14(catalog); Finish("ok"); yield break; }
            if (round15) { yield return Round15(catalog); Finish("ok"); yield break; }
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

            // ---- The couch by its side (the end of an arm): held still, then pushed; it must not spin ----
            yield return StandAtEnd(sofa, 1.2f);
            sb = sofa.Shape.bounds;
            yield return AimDirect(sofa.transform.TransformPoint(new Vector3(sb.max.x - 0.08f, sb.min.y + sb.size.y * 0.72f, sb.center.z + 0.1f)));
            _log.AppendLine($"aimed at its end: hint '{_grab.HintText}'");
            yield return Hold();
            Log("couch by its side");
            float ys = sofa.transform.eulerAngles.y, maxSpin = 0f;
            for (float t = 0f; t < 2f; t += Time.deltaTime) { maxSpin = Mathf.Max(maxSpin, Mathf.Abs(sofa.Body.angularVelocity.y)); yield return null; }
            Log($"held still 2 s: it turned {Mathf.DeltaAngle(ys, sofa.transform.eulerAngles.y):F0} deg, fastest spin {maxSpin:F2} rad/s");
            Vector3 ps = sofa.transform.position; ys = sofa.transform.eulerAngles.y; maxSpin = 0f;
            for (float t = 0f; t < 2f; t += Time.deltaTime) { _ctrl.botInput = new Vector2(0f, 0.8f); maxSpin = Mathf.Max(maxSpin, Mathf.Abs(sofa.Body.angularVelocity.y)); yield return null; }
            _ctrl.botInput = null;
            Log($"pushed by its side 2 s: moved {Flat(sofa.transform.position - ps).magnitude:F2} m, turned {Mathf.DeltaAngle(ys, sofa.transform.eulerAngles.y):F0} deg, fastest spin {maxSpin:F2} rad/s");
            Shot("3b_couch_by_side");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);

            // ---- The couch by the underside of its back: tipped up ----
            yield return StandBehind(sofa, 1.35f);
            sb = sofa.Shape.bounds;
            Vector3 low = sofa.transform.TransformPoint(new Vector3(sb.center.x, sb.min.y + 0.06f, sb.min.z - 0.02f));
            yield return AimDirect(low);
            _log.AppendLine($"aimed at the bottom of its back: hint '{_grab.HintText}'");
            Shot("4_couch_aimed_low");
            yield return Hold();
            Log("couch by the underside");
            yield return new WaitForSeconds(0.6f);
            Log("couch by the underside, settled");
            for (int i = 0; i < 160; i++) { _grab.botMouse = new Vector2(0f, 5f); if (i % 40 == 0) Log($"  tipping {i}: tilt {Tilt(sofa):F0}"); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            yield return new WaitForSeconds(0.8f);
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
            Log("office chair carried");
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

        IEnumerator Round15(System.Collections.Generic.List<ItemCatalog.Entry> catalog)
        {
            var body = _ws.player.GetComponent<FirstPersonBody>();
            Vector3 home = _ctrl.transform.position;
            float homeYaw = _ctrl.transform.eulerAngles.y;
            Vector3 fwd = Flat(_ctrl.transform.forward).normalized;

            // ---- The floral sofa: seen from the front, then pushed by the top of its back ----
            var fe = catalog.Find(x => x.name == "Floral sofa");
            if (fe == null) { _log.AppendLine("no floral sofa in the catalog"); yield break; }
            var sofa = fe.spawn(home + fwd * 2.6f, Quaternion.LookRotation(-fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1.2f);
            _log.AppendLine($"floral sofa: {sofa.Shape.edges.Count} grab edges, mass {sofa.Mass:F0} kg, bounds {sofa.WorldBounds.size}");
            {
                Vector3 c = sofa.WorldBounds.center, front = sofa.transform.forward;
                View("r15_1_floral_front", c + front * 2.9f + Vector3.up * 0.45f, c + Vector3.down * 0.1f);
                View("r15_2_floral_angle", c + Quaternion.Euler(0f, 38f, 0f) * front * 2.6f + Vector3.up * 0.7f, c + Vector3.down * 0.1f);
                View("r15_2b_floral_close", c + Quaternion.Euler(0f, -30f, 0f) * front * 1.3f + sofa.transform.right * 0.6f + Vector3.up * 0.35f, c + sofa.transform.right * 0.75f + Vector3.down * 0.05f);
            }
            yield return StandBehind(sofa, 1.25f);
            var sb = sofa.Shape.bounds;
            yield return AimDirect(sofa.transform.TransformPoint(new Vector3(sb.center.x + 0.3f, sb.max.y - 0.03f, sb.min.z + 0.06f)));
            _log.AppendLine($"aimed at its back: hint '{_grab.HintText}'");
            yield return Hold();
            Log($"floral sofa by the top: {Depths(body)}");
            Vector3 p0 = sofa.transform.position;
            yield return Walk(new Vector2(0f, 1f), 1.5f);
            Log($"pushed {Flat(sofa.transform.position - p0).magnitude:F2} m: {Depths(body)}");
            Shot("r15_3_floral_pushed");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);
            yield return Home(sofa, home, homeYaw);

            // ---- Boxes carried while turning and stepping side to side: how far do the hands and forearms go in ----
            foreach (var size in new[] { "medium", "large" })
            {
                fwd = Flat(_ctrl.transform.forward).normalized;
                var box = catalog.Find(x => x.name == $"Cardboard box ({size})").spawn(_ctrl.transform.position + fwd * 0.9f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
                yield return new WaitForSeconds(1f);
                yield return AimDirect(box.WorldBounds.center + Vector3.up * box.WorldBounds.extents.y * 0.9f);
                yield return Hold();
                yield return new WaitForSeconds(0.8f);
                yield return new WaitForEndOfFrame();
                Log($"{size} box carried: {Depths(body)}");
                ArmsView($"r15_3b_{size}_box_still_above", box);
                float maxR = 0f, maxL = 0f;
                float mrh = 0f, mrf = 0f, mlh = 0f, mlf = 0f; Vector3 rAtMax = default, lAtMax = default;
                body.HandDepthsDetail(out float srh, out float srf, out float slh, out float slf, out Vector3 sra, out Vector3 sla);
                _log.AppendLine($"   still: right hand {srh * 100f:F1} forearm {srf * 100f:F1} (at {box.transform.InverseTransformPoint(sra).ToString("F2")} in the box), left hand {slh * 100f:F1} forearm {slf * 100f:F1} (at {box.transform.InverseTransformPoint(sla).ToString("F2")}), box half {(box.Spec.size * 0.5f).ToString("F2")}");
                for (float t = 0f; t < 3f; t += Time.deltaTime)
                {
                    float dir = Mathf.Sign(Mathf.Sin(t * Mathf.PI));          // a second each way
                    _ctrl.AddLook(dir * 120f * Time.deltaTime, 0f);
                    _ctrl.botInput = new Vector2(dir, 0.2f);
                    yield return null;
                    yield return new WaitForEndOfFrame();          // after the body has posed its arms this frame
                    body.HandDepths(out float r, out float l);
                    maxR = Mathf.Max(maxR, r); maxL = Mathf.Max(maxL, l);
                    body.HandDepthsDetail(out float rh, out float rf, out float lh, out float lf, out Vector3 ra, out Vector3 la);
                    mrh = Mathf.Max(mrh, rh); mlh = Mathf.Max(mlh, lh);
                    if (rf > mrf) { mrf = rf; rAtMax = box.transform.InverseTransformPoint(ra); }
                    if (lf > mlf) { mlf = lf; lAtMax = box.transform.InverseTransformPoint(la); }
                    if (Mathf.Abs(t - 1.5f) < Time.deltaTime) { Shot($"r15_4_{size}_box_turning"); ArmsView($"r15_4b_{size}_box_turning_above", box); }
                }
                _ctrl.botInput = null;
                _log.AppendLine($"   turning: right hand {mrh * 100f:F1} forearm {mrf * 100f:F1} (at {rAtMax.ToString("F2")}), left hand {mlh * 100f:F1} forearm {mlf * 100f:F1} (at {lAtMax.ToString("F2")})");
                Log($"{size} box, 3 s turning 120 deg/s and stepping side to side: deepest right {maxR * 100f:F1} cm, left {maxL * 100f:F1} cm (hands and forearms), two={_grab.TwoHanded}");
                yield return new WaitForSeconds(0.5f);
                Shot($"r15_5_{size}_box_still");
                Log($"{size} box still again: {Depths(body)}");
                _grab.botHold = false; yield return null; _grab.botHold = null;
                yield return new WaitForSeconds(1f);
                yield return Home(box, home, homeYaw);
            }
        }

        IEnumerator Round14(System.Collections.Generic.List<ItemCatalog.Entry> catalog)
        {
            var body = _ws.player.GetComponent<FirstPersonBody>();
            Vector3 home = _ctrl.transform.position;
            float homeYaw = _ctrl.transform.eulerAngles.y;
            Vector3 fwd = Flat(_ctrl.transform.forward).normalized;

            // ---- An office chair on its side: how many of its edges can be taken, and do the marks hop about ----
            var chair = catalog.Find(x => x.name == "Office chair").spawn(home + fwd * 1.15f + Vector3.up * 0.3f,
                Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd)) * Quaternion.Euler(0f, 0f, 90f), false).GetComponent<MovableProp>();
            yield return null;
            chair.transform.position += Vector3.up * (home.y - chair.WorldBounds.min.y);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.5f);
            _log.AppendLine($"chair on its side: tilt {Tilt(chair):F0}, {chair.Shape.edges.Count} grab edges in all");
            var wb = chair.WorldBounds;
            Vector3 eye = Camera.main.transform.position, to = Flat(wb.center - eye).normalized, across = Vector3.Cross(Vector3.up, to);
            var seen = new System.Collections.Generic.HashSet<int>();
            int aims = 0, hinted = 0;
            for (int v = 0; v < 6; v++)
            for (int u = 0; u < 7; u++)
            {
                Vector3 p = wb.center + across * ((u / 6f - 0.5f) * wb.size.magnitude * 0.55f) + Vector3.up * ((v / 5f - 0.5f) * wb.size.y);
                yield return AimDirect(p);
                aims++;
                if (_grab.MarkEdge >= 0) { hinted++; seen.Add(_grab.MarkEdge); }
            }
            _log.AppendLine($"swept {aims} aims over it: {hinted} offered a grab, on {seen.Count} different edges, hint '{_grab.HintText}'");
            // A slow sweep across it at the middle's height: how often the marks change edge.
            int changes = 0, last = -2;
            for (int k = 0; k <= 40; k++)
            {
                Vector3 p = wb.center + across * ((k / 40f - 0.5f) * wb.size.magnitude * 0.5f);
                Vector3 d = p - Camera.main.transform.position;
                _ctrl.TeleportSafe(_ctrl.transform.position, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                yield return null; yield return null;
                int e = _grab.MarkEdge;
                if (e != last && last != -2) changes++;
                last = e;
            }
            _log.AppendLine($"slow sweep across it (41 steps): the marks changed edge {changes} times");
            yield return AimDirect(wb.center);
            Shot("r14_1_chair_side_aimed");
            yield return Hold();
            yield return new WaitForSeconds(0.6f);
            Log($"chair on its side taken: {Carry(chair)}, {Depths(body)}");
            Shot("r14_2_chair_side_carried");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);
            yield return Home(chair, home, homeYaw);

            // ---- The couch, crouched, taken at the bottom of its front: it tips ----
            fwd = Flat(_ctrl.transform.forward).normalized;
            var sofa = catalog.Find(x => x.name.StartsWith(couch)).spawn(_ctrl.transform.position + fwd * 1.25f, Quaternion.LookRotation(-fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1.2f);
            _ctrl.SetStance(CamcorderPlayerController.Stance.Crouch);
            yield return new WaitForSeconds(0.8f);
            var sb = sofa.Shape.bounds;
            yield return AimDirect(sofa.transform.TransformPoint(new Vector3(sb.center.x + 0.2f, sb.min.y + 0.07f, sb.max.z)));
            _log.AppendLine($"crouched, aimed at the bottom of the couch's front: hint '{_grab.HintText}', edge {_grab.MarkEdge}");
            Shot("r14_3_couch_low_aimed");
            yield return Hold();
            Log($"couch taken low, crouched: {Depths(body)}");
            for (int i = 0; i < 200; i++) { _grab.botMouse = new Vector2(0f, 5f); if (i % 50 == 0) Log($"  tipping {i}: tilt {Tilt(sofa):F0}"); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.8f);
            Log($"tipped: tilt {Tilt(sofa):F0}, {Depths(body)}");
            Shot("r14_4_couch_tipped");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.5f);
            _ctrl.SetStance(CamcorderPlayerController.Stance.Stand);
            yield return new WaitForSeconds(0.6f);
            yield return Home(sofa, home, homeYaw);

            // ---- An armchair by the top, two hands: how deep are the hands ----
            fwd = Flat(_ctrl.transform.forward).normalized;
            var arm = catalog.Find(x => x.name.StartsWith("Armchair")).spawn(_ctrl.transform.position + fwd * 1.2f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            sb = arm.Shape.bounds;
            yield return AimDirect(arm.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y - 0.03f, sb.min.z + 0.06f)));
            yield return Hold();
            yield return new WaitForSeconds(0.4f);
            Log($"armchair by the top: {Depths(body)}");
            Shot("r14_5_armchair_top");
            yield return Walk(new Vector2(0f, 0.8f), 1.2f);
            Log($"armchair pushed: {Depths(body)}");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);
            yield return Home(arm, home, homeYaw);

            // ---- A box, carried, lifted up to stack height and lowered to the floor ----
            fwd = Flat(_ctrl.transform.forward).normalized;
            var box = catalog.Find(x => x.name == "Cardboard box (medium)").spawn(_ctrl.transform.position + fwd * 0.9f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            yield return AimDirect(box.WorldBounds.center + Vector3.up * box.WorldBounds.extents.y * 0.9f);
            yield return Hold();
            yield return new WaitForSeconds(0.8f);
            Log($"box carried: {Carry(box)}, {Depths(body)}");
            float pitch0 = _ctrl.Pitch;
            for (int i = 0; i < 90; i++) { _grab.botMouse = new Vector2(0f, 6f); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.5f);
            Log($"box lifted: {Carry(box)}, view pitch {pitch0:F0} -> {_ctrl.Pitch:F0}, {Depths(body)}");
            Shot("r14_6_box_lifted");
            for (int i = 0; i < 200; i++) { _grab.botMouse = new Vector2(0f, -6f); yield return null; }
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            Log($"box lowered: {Carry(box)}, {Depths(body)}");
            Shot("r14_7_box_lowered");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);
            Log($"box let go: bottom {box.WorldBounds.min.y - _ctrl.transform.position.y:F2} m up, tilt {Tilt(box):F0}");

            // ---- An empty pallet: under the arm, the camcorder at the eye ----
            yield return Home(box, home, homeYaw);
            fwd = Flat(_ctrl.transform.forward).normalized;
            var pal = catalog.Find(x => x.name == "Pallet").spawn(_ctrl.transform.position + fwd * 1.0f, Quaternion.LookRotation(fwd), false).GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            var pb = pal.Shape.bounds;
            yield return AimDirect(pal.transform.TransformPoint(new Vector3(0.1f, pb.max.y, pb.min.z + 0.04f)));
            _log.AppendLine($"aimed at the pallet: hint '{_grab.HintText}'");
            yield return Hold();
            yield return new WaitForSeconds(0.8f);
            Log($"pallet: {Carry(pal)}, {Depths(body)}");
            yield return Walk(new Vector2(0f, 0.8f), 1.5f);
            Log($"pallet walked: {Carry(pal)}");
            Shot("r14_8_pallet");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1f);
        }

        /// <summary>Back to the open spot the checks started at, the last prop out of the way.</summary>
        IEnumerator Home(MovableProp done, Vector3 home, float yaw)
        {
            if (done) Destroy(done.gameObject);
            yield return null;
            _ctrl.Teleport(home, yaw);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>The box and both arms from above and behind the left shoulder (to see whether the arms go into it).</summary>
        void ArmsView(string name, MovableProp box)
        {
            var t = _ctrl.transform;
            View(name, t.position + Vector3.up * 2.05f - t.forward * 0.35f - t.right * 0.25f, box.WorldBounds.center);
        }

        /// <summary>Renders a view from a camera like the main one into a PNG.</summary>
        void View(string name, Vector3 eye, Vector3 target)
        {
            var main = Camera.main;
            var go = new GameObject("ProbeViewCam");
            var cam = go.AddComponent<Camera>();
            if (main) cam.CopyFrom(main);
            cam.fieldOfView = 55f; cam.nearClipPlane = 0.02f;
            go.transform.position = eye;
            go.transform.LookAt(target, Vector3.up);
            var rt = new RenderTexture(1280, 800, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Destroy(rt); Destroy(tex); Destroy(go);
        }

        string Depths(FirstPersonBody body)
        {
            if (!body) return "no body";
            body.HandDepths(out float r, out float l);
            return $"hands in it: right {r * 100f:F1} cm, left {l * 100f:F1} cm";
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

        /// <summary>At the couch's +X end, facing it.</summary>
        IEnumerator StandAtEnd(MovableProp p, float d)
        {
            Vector3 side = Flat(p.transform.right).normalized;
            Vector3 s = p.WorldBounds.center + side * (p.Shape.bounds.extents.x + d); s.y = _ctrl.transform.position.y;
            _ctrl.Teleport(s, Quaternion.LookRotation(-side).eulerAngles.y);
            yield return new WaitForSeconds(0.5f);
        }

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
            _log.AppendLine($"{Time.time:F1}s {what}: state={_grab.Current} handling={_grab.Handling} two={_grab.TwoHanded} strap={_grab.CamcorderOnStrap} " +
                            $"camera {Camera.main.transform.position.y - _ctrl.transform.position.y:F2} m up (stoop {_ctrl.stoop:F2}) last release='{_grab.LastRelease}'");
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
