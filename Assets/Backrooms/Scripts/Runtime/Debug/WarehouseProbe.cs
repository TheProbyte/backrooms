using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks the warehouse and the pockets with the inputs a player gives: goes to a generated pallet, counts the movable
    /// boxes and pallets round it, takes a box off a rack's shelf, pushes a loaded pallet, carries an empty one (walks and
    /// turns with it, then drops it), then picks up the hammer and the box of nails and puts them in and out of pockets
    /// (and checks the save keeps them there). Screenshots and a log. Add in Play Mode in a world (World 3), set
    /// <see cref="folder"/>.
    /// </summary>
    public class WarehouseProbe : MonoBehaviour
    {
        public string folder;
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

            if (!TravelToPallet(out var palletAt)) { Finish("no pallet within 10 chunks"); yield break; }
            // Held where it was put until the floor there has loaded (it fell out of the world 100 m away).
            Vector3 hold = _ctrl.transform.position; float holdYaw = _ctrl.transform.eulerAngles.y;
            for (float t = 0f; t < 25f; t += 0.25f)
            {
                _ctrl.Teleport(hold, holdYaw);
                if (Physics.Raycast(hold + Vector3.up * 1f, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore) && t > 2f) break;
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(3f);
            // The aisle may be blocked on that side: the other.
            float C = OfficeBiome.CellSize;
            if (Physics.CheckCapsule(_ctrl.transform.position + Vector3.up * 0.4f, _ctrl.transform.position + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                _ctrl.Teleport(_rackAt + new Vector3(0f, 0f, C), 180f);
            yield return new WaitForSeconds(1f);
            Count(palletAt);
            _ctrl.TeleportSafe(_ctrl.transform.position, _ctrl.transform.eulerAngles.y + 60f, 5f);
            yield return new WaitForSeconds(0.5f);
            Shot("1_hall");
            _ctrl.TeleportSafe(_ctrl.transform.position, _ctrl.transform.eulerAngles.y - 60f, 5f);
            yield return new WaitForSeconds(0.3f);
            Shot("1b_rack");

            // A box off a rack's shelf: the nearest box standing 0.5-1.7 m up.
            MovableProp shelfBox = null; float best = float.MaxValue;
            foreach (var p in FindObjectsByType<MovableProp>())
            {
                if (p.Spec.kind != MovableKind.Box) continue;
                float up = p.transform.position.y - _ctrl.transform.position.y;
                float d = Vector3.Distance(Flat(p.transform.position), Flat(_ctrl.transform.position));
                if (up > 0.4f && up < 1.4f && d < best && d < 2.2f) { best = d; shelfBox = p; }
            }
            if (shelfBox)
            {
                Vector3 from = shelfBox.transform.position;
                yield return Aim(shelfBox.WorldBounds.center + Vector3.up * shelfBox.WorldBounds.extents.y * 0.6f);
                Shot("2_shelf_box_aimed");
                yield return Hold();
                Log($"shelf box ({shelfBox.Spec.size.x:F2} x {shelfBox.Spec.size.y:F2} m, {shelfBox.Mass:F1} kg, {from.y - _ctrl.transform.position.y:F2} m up) held={_grab.Held == shelfBox}");
                yield return Walk(new Vector2(0f, -0.7f), 1.4f);
                Shot("3_shelf_box_taken");
                _grab.botHold = false; yield return null; _grab.botHold = null;
                yield return new WaitForSeconds(1.5f);
                Log($"shelf box let go: moved {Vector3.Distance(shelfBox.transform.position, from):F2} m, now {shelfBox.transform.position.y - _ctrl.transform.position.y:F2} m up");
            }
            else _log.AppendLine("no box on a shelf near");

            // A pallet in the open: loaded (boxes stacked on it), pushed; the boxes taken away, carried.
            yield return CarryEmpty();
            yield return Pockets();
            Finish("ok");
        }

        IEnumerator PushLoaded(MovableProp pallet)
        {
            // Boxes on it now.
            var onIt = new System.Collections.Generic.List<MovableProp>();
            foreach (var p in FindObjectsByType<MovableProp>())
                if (p.Spec.kind == MovableKind.Box && Vector3.Distance(p.transform.position, pallet.transform.position) < 0.9f && p.transform.position.y > pallet.transform.position.y + 0.1f) onIt.Add(p);
            // Its near long edge (local -z, toward you), at its right end.
            yield return AimDirect(pallet.transform.TransformPoint(new Vector3(0.3f, PalletGeometry.Height, -0.47f)));
            Vector3 p0 = pallet.transform.position;
            yield return Hold();
            Log($"loaded pallet taken: pushing={_grab.Pushing} carrying={_grab.CarryingPallet}, {onIt.Count} boxes on it, mass {pallet.Mass:F0} kg");
            yield return Walk(new Vector2(0f, 1f), 2.5f);
            Log($"pushed {Flat(pallet.transform.position - p0).magnitude:F2} m");
            Shot("4_loaded_pushed");
            float yaw0 = pallet.transform.eulerAngles.y;
            for (float t = 0f; t < 2f; t += Time.deltaTime) { _ctrl.AddLook(35f * Time.deltaTime, 0f); _ctrl.botInput = new Vector2(0f, 0.8f); yield return null; }
            _ctrl.botInput = null;
            Log($"steered: it turned {Mathf.DeltaAngle(yaw0, pallet.transform.eulerAngles.y):F0} deg as you turned 70");
            _grab.botMouse = new Vector2(0f, 120f);           // a heave: a loaded pallet stays pushed
            yield return new WaitForSeconds(0.15f);
            _grab.botMouse = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            Log("after a heave flick");
            Vector3 p1 = pallet.transform.position;
            yield return Walk(new Vector2(0f, -0.8f), 1.5f);
            Log($"pulled back {Flat(pallet.transform.position - p1).magnitude:F2} m");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.2f);
            int still = 0;
            foreach (var b in onIt) if (b && PropGrabber.PalletLoaded(pallet) && Vector3.Distance(Flat(b.transform.position), Flat(pallet.transform.position)) < 0.75f && b.transform.position.y > pallet.transform.position.y + 0.1f) still++;
            Log($"let go: {still} of {onIt.Count} boxes still on it");
            Shot("5_loaded_after");
        }

        IEnumerator CarryEmpty()
        {
            // A pallet in open floor (spawned, as the menu does): a spot with room to stand and 5 m clear ahead.
            var entry = ItemCatalog.Build(_ws.assets, _ws).Find(e => e.name == "Pallet");
            if (entry == null) { _log.AppendLine("no Pallet entry"); yield break; }
            if (!FindOpen(out var openAt, out float openYaw)) { _log.AppendLine("no open floor near"); yield break; }
            _ctrl.Teleport(openAt, openYaw);
            yield return new WaitForSeconds(0.4f);
            Vector3 fwd = Flat(_ctrl.transform.forward).normalized;
            var go = entry.spawn(_ctrl.transform.position + fwd * 1.25f, Quaternion.LookRotation(fwd), false);
            var pallet = go.GetComponent<MovableProp>();
            yield return new WaitForSeconds(1f);
            float bestD = 5f;
            // Loaded: three medium cartons on its far half, two of them stacked.
            var box = ItemCatalog.Build(_ws.assets, _ws).Find(e => e.name == "Cardboard box (medium)");
            var boxes = new System.Collections.Generic.List<GameObject>();
            if (box != null)
            {
                Vector3[] spots = { new Vector3(-0.3f, 0f, 0.22f), new Vector3(0.3f, 0f, 0.22f), new Vector3(-0.3f, 0.37f, 0.22f) };
                foreach (var s in spots)
                    boxes.Add(box.spawn(pallet.transform.TransformPoint(new Vector3(s.x, PalletGeometry.Height + 0.005f + s.y, s.z)), pallet.transform.rotation, false));
            }
            yield return new WaitForSeconds(1.2f);
            _log.AppendLine($"pallet spawned {bestD:F1} m clear ahead with {boxes.Count} cartons, loaded={PropGrabber.PalletLoaded(pallet)}");
            yield return PushLoaded(pallet);
            foreach (var b in boxes) if (b) Destroy(b);
            yield return new WaitForSeconds(0.5f);
            _log.AppendLine($"cartons taken away: loaded={PropGrabber.PalletLoaded(pallet)}");
            // Stand at its near long side again (where the push left it) and take its near edge.
            float zs = Vector3.Dot(pallet.transform.forward, _ctrl.transform.position - pallet.transform.position) >= 0f ? 1f : -1f;
            Vector3 st = pallet.transform.position + pallet.transform.forward * (zs * 1.25f); st.y = _ctrl.transform.position.y;
            if (Physics.CheckCapsule(st + Vector3.up * 0.4f, st + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            { zs = -zs; st = pallet.transform.position + pallet.transform.forward * (zs * 1.25f); st.y = _ctrl.transform.position.y; }
            _ctrl.Teleport(st, Quaternion.LookRotation(Flat(pallet.transform.position - st)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            yield return AimDirect(pallet.transform.TransformPoint(new Vector3(0.2f, PalletGeometry.Height, zs * 0.47f)));
            yield return Hold();
            yield return new WaitForSeconds(1f);
            LogCarry(pallet, "carried, standing");
            Shot("6_pallet_carried");
            yield return Walk(new Vector2(0f, 0.8f), 2f);
            LogCarry(pallet, "walked 2 s");
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { _ctrl.AddLook(60f * Time.deltaTime, 0f); _ctrl.botInput = new Vector2(0f, 0.6f); yield return null; }
            _ctrl.botInput = null;
            LogCarry(pallet, "turned 90 walking");
            Shot("7_pallet_turned");
            _grab.botHold = false; yield return null; _grab.botHold = null;
            yield return new WaitForSeconds(1.5f);
            Log($"dropped: tilt {Vector3.Angle(pallet.transform.up, Vector3.up):F0} deg, {pallet.WorldBounds.min.y - _ctrl.transform.position.y:F2} m off the floor");
            Shot("8_pallet_dropped");
        }

        /// <summary>Looks straight at p (no nudging).</summary>
        IEnumerator AimDirect(Vector3 p)
        {
            for (int k = 0; k < 2; k++)
            {
                var eye = Camera.main.transform.position;
                Vector3 d = p - eye;
                _ctrl.TeleportSafe(_ctrl.transform.position, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                yield return null; yield return null;
            }
        }

        /// <summary>Open floor within 10 m: room to stand, and a corridor 1.5 m wide and 5 m long ahead, up to 1.3 m high.</summary>
        bool FindOpen(out Vector3 at, out float yaw)
        {
            Vector3 c = _ctrl.transform.position;
            for (int r = 0; r <= 10; r++)
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
                    if (Physics.CheckBox(s + f * 2.8f + Vector3.up * 0.7f, new Vector3(0.75f, 0.6f, 2.5f), Quaternion.LookRotation(f), ~0, QueryTriggerInteraction.Ignore)) continue;
                    at = s; yaw = y; return true;
                }
            }
            at = default; yaw = 0f; return false;
        }

        void LogCarry(MovableProp p, string what)
        {
            var local = _ctrl.transform.InverseTransformPoint(p.WorldBounds.center);
            Log($"{what}: carrying={_grab.CarryingPallet} strap={_grab.CamcorderOnStrap} two={_grab.TwoHanded}, middle at x {local.x:F2} y {local.y:F2} z {local.z:F2} (body), " +
                $"up·world {Vector3.Dot(p.transform.forward, Vector3.up):F2}, bottom {p.WorldBounds.min.y - _ctrl.transform.position.y:F2} m, palm off {_grab.PalmError * 100f:F0} cm, strain {_grab.Strain:F2}");
        }

        IEnumerator Pockets()
        {
            _ctrl.TeleportSafe(_ctrl.transform.position, _ctrl.transform.eulerAngles.y, 25f);
            yield return null;
            var hammer = ItemFactory.Spawn("hammer", _ctrl.transform.position + _ctrl.transform.forward * 0.5f + Vector3.up * 1f, Quaternion.identity);
            var nails = ItemFactory.Spawn("nails", _ctrl.transform.position + _ctrl.transform.forward * 0.5f + _ctrl.transform.right * 0.3f + Vector3.up * 1f, Quaternion.identity);
            yield return new WaitForSeconds(1f);
            _grab.PickUp(hammer);
            for (int i = 0; i < 200 && (_grab.HeldItem != hammer || _grab.ItemWaitingForHand); i++) yield return null;
            yield return new WaitForSeconds(0.8f);
            _log.AppendLine($"hammer in hand: {_grab.HeldItem == hammer}");
            Shot("9_hammer");
            _grab.botPocket = 3; yield return new WaitForSeconds(0.6f);
            _log.AppendLine($"key 4: hand={Name(_grab.HeldItem)} pockets={Pockets4()}");
            Shot("10_pocket_strip");
            _grab.PickUp(nails);
            for (int i = 0; i < 200 && (_grab.HeldItem != nails || _grab.ItemWaitingForHand); i++) yield return null;
            yield return new WaitForSeconds(0.8f);
            _log.AppendLine($"nails in hand: {_grab.HeldItem == nails}");
            Shot("11_nails");
            _grab.botPocket = 3; yield return new WaitForSeconds(0.9f);
            _log.AppendLine($"key 4 again (swap): hand={Name(_grab.HeldItem)} pockets={Pockets4()}");
            _grab.botPocket = 0; yield return new WaitForSeconds(0.6f);
            _log.AppendLine($"key 1: hand={Name(_grab.HeldItem)} pockets={Pockets4()}");
            _grab.botPocket = 3; yield return new WaitForSeconds(0.9f);
            _log.AppendLine($"key 4: hand={Name(_grab.HeldItem)} pockets={Pockets4()}");
            Shot("12_from_pocket");
            // A lamp does not fit.
            var lamp = ItemFactory.Spawn("desklamp", _ctrl.transform.position + _ctrl.transform.forward * 0.5f + Vector3.up * 1f, Quaternion.identity);
            _grab.botPocket = 1; yield return new WaitForSeconds(0.6f);       // the nails away first
            yield return new WaitForSeconds(0.5f);
            _grab.PickUp(lamp);
            for (int i = 0; i < 200 && (_grab.HeldItem != lamp || _grab.ItemWaitingForHand); i++) yield return null;
            yield return new WaitForSeconds(0.6f);
            _grab.botPocket = 2; yield return new WaitForSeconds(0.4f);
            _log.AppendLine($"lamp, key 3: hand={Name(_grab.HeldItem)} pockets={Pockets4()}");
            Shot("13_lamp_wont_fit");
            // Saved: the pockets are in the save.
            Saves.WorldSaver.SaveNow();
            var data = Saves.SaveSlots.Load(Saves.GameSession.Slot);
            var sb = new StringBuilder();
            if (data != null) foreach (var it in data.items) if (it.pocket >= 0) sb.Append($"{it.id}@{it.pocket + 1} ");
            _log.AppendLine($"saved pockets: {sb}");
        }

        string Pockets4()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < PropGrabber.PocketCount; i++) sb.Append($"[{i + 1} {Name(_grab.Pocket(i))}] ");
            return sb.ToString();
        }
        static string Name(WorldItem i) => i ? i.itemId : "-";

        /// <summary>Into the aisle in front of a generated rack (a hall with racks and pallets), facing the rack.</summary>
        bool TravelToPallet(out Vector3 at)
        {
            at = default;
            var c0 = WorldStreamer.ChunkOf(_ctrl.transform.position);
            float C = OfficeBiome.CellSize;
            for (int r = 0; r <= 6; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                var L = _ws.World.Chunk(new Vector2Int(c0.x + dx, c0.y + dz));
                Prop rack = default; bool hasRack = false, pallets = false;
                foreach (var q in L.props) { if (q.kind == PropKind.StorageRack && !hasRack) { rack = q; hasRack = true; } pallets |= q.kind == PropKind.Pallet; }
                if (!hasRack || !pallets) continue;
                Vector3 origin = new Vector3((c0.x + dx) * OfficeBiome.ChunkSize, 0f, (c0.y + dz) * OfficeBiome.ChunkSize);
                at = origin + new Vector3(rack.position.x, 0f, rack.position.y);
                _rackAt = at;
                _ctrl.Teleport(at + new Vector3(0f, 0f, -C), 0f);       // the aisle on its -z side, facing +z
                _log.AppendLine($"rack in chunk {c0.x + dx},{c0.y + dz} at {at} ({rack.size.y:F1} m high)");
                return true;
            }
            return false;
        }
        Vector3 _rackAt;

        void Count(Vector3 at)
        {
            int boxes = 0, high = 0, pallets = 0;
            foreach (var p in FindObjectsByType<MovableProp>())
            {
                if (Vector3.Distance(Flat(p.transform.position), Flat(at)) > 25f) continue;
                if (p.Spec.kind == MovableKind.Pallet) pallets++;
                else if (p.Spec.kind == MovableKind.Box) { boxes++; if (p.transform.position.y - at.y > 0.3f) high++; }
            }
            _log.AppendLine($"within 25 m: {pallets} pallets, {boxes} boxes ({high} off the floor), chunk upload {_ws.AverageUploadMs:F1} ms avg");
        }

        static T Near<T>(Vector3 at, float r, MovableKind kind) where T : MovableProp
        {
            foreach (var p in FindObjectsByType<MovableProp>()) if (p.Spec.kind == kind && Vector3.Distance(Flat(p.transform.position), Flat(at)) < r) return p as T;
            return null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        IEnumerator Overview(Vector3 look)
        {
            Vector3 d = look - (_ctrl.transform.position + Vector3.up * 1.5f);
            _ctrl.TeleportSafe(_ctrl.transform.position, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 8f);
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>Stands about dist from the prop's middle (flat), on the side toward where the player is, facing it.</summary>
        IEnumerator StandAt(MovableProp p, float dist)
        {
            Vector3 c = p.WorldBounds.center;
            Vector3 dir = Flat(_ctrl.transform.position - c);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 best = default; bool found = false;
            for (int k = 0; k < 16 && !found; k++)
            {
                Vector3 d = Quaternion.Euler(0f, (k % 2 == 0 ? 1 : -1) * (k / 2) * 22.5f, 0f) * dir;
                for (float extra = 0f; extra < 0.6f && !found; extra += 0.15f)
                {
                    Vector3 s = c + d * (dist + extra); s.y = _ctrl.transform.position.y;
                    if (Physics.CheckCapsule(s + Vector3.up * 0.4f, s + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    best = s; found = true;
                }
            }
            if (!found) best = c + dir * dist;
            best.y = _ctrl.transform.position.y;
            _ctrl.Teleport(best, Quaternion.LookRotation(Flat(c - best)).eulerAngles.y);
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Walk(Vector2 dir, float secs)
        {
            for (float t = 0f; t < secs; t += Time.deltaTime) { _ctrl.botInput = dir; yield return null; }
            _ctrl.botInput = null;
        }

        IEnumerator Hold()
        {
            var cam = Camera.main.transform;
            string on = Physics.Raycast(cam.position, cam.forward, out var hit, 3f) ? $"{hit.collider.name} at {hit.distance:F2} m" : "nothing";
            _grab.botHold = true;
            float t = 0f;
            string seen = "";
            while (_grab.Current != PropGrabber.State.Holding && t < 4f) { if (!seen.EndsWith(_grab.Current.ToString())) seen += " " + _grab.Current; t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.4f);
            _log.AppendLine($"  (hold: the view on {on}; states{seen} -> {_grab.Current})");
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
            _log.AppendLine($"{Time.time:F1}s {what}: state={_grab.Current} held={(_grab.Held ? _grab.Held.Spec.kind.ToString() : "-")} pushing={_grab.Pushing} " +
                            $"carrying={_grab.CarryingPallet} strap={_grab.CamcorderOnStrap} last release='{_grab.LastRelease}'");
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));

        void Finish(string why)
        {
            _log.AppendLine("finished: " + why);
            File.WriteAllText(Path.Combine(folder, "warehouse.txt"), _log.ToString());
            done = true;
        }
    }
}
