using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Putting kits up (2026-11-08, Docs/GameDesign.md section 8: "a see-through outline of the shelf shows where it will go,
    /// like Satisfactory's build gun"). While you carry a kit crate, a see-through copy of what is in it stands where you look:
    /// on the floor, turned to face you, and snapped flat against a wall when one is close behind it. It is green where it
    /// fits and red where something is in the way (a box test of its space against everything solid, as placement tools
    /// do). R turns it a quarter, right mouse puts it up (in place of throwing the crate).
    /// <para>Looking at a standing shelf with nothing on it, hold G to pack it back into its crate (to move it). A knocked-over
    /// shelf is stood up with the hammer and nails (Shelves.Mend).</para>
    /// <para>Generators, conveyor sections and floodlights go up the same way (their outlines: Machines).</para>
    /// </summary>
    public class KitPlacer : MonoBehaviour
    {
        public static KitPlacer Instance { get; private set; }

        PropGrabber _hand;
        float _turn;
        bool _showing, _valid;
        Vector3 _pos;
        Quaternion _rot;
        ulong _kit;
        Kits.Kind _kind;
        Vector3 _size;
        string _why;
        float _packT;
        ulong _packing;
        readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        Material _green, _red;
        static readonly Collider[] s_over = new Collider[32];
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; foreach (var m in _meshes.Values) if (m) Destroy(m); }

        /// <summary>The hint line while carrying a kit (null: not a kit, or nothing to say).</summary>
        public static string CarryHint(MovableProp held)
        {
            var k = Instance;
            if (!k || !held || !k._showing || held.Spec.id != k._kit) return null;
            return k._valid ? Backrooms.UI.Words.Get("hint.kit_place", Kits.Name(k._kind)) : (k._why ?? Backrooms.UI.Words.Get("hint.no_room")) + " · " + Backrooms.UI.Words.Get("hint.r_turns");
        }

        /// <summary>The hint line with empty hands, looking at a shelf (pack it, stand it up), or null.</summary>
        public static string IdleHint;

        public static bool CanPlace(MovableProp held) { var k = Instance; return k && held && k._showing && k._valid && held.Spec.id == k._kit; }

        /// <summary>The crate has been let go of (right mouse): what was in it goes up where the outline was.</summary>
        public static void Place(MovableProp kit)
        {
            var k = Instance;
            var d = GameSession.Data;
            var mgr = MovablePropManager.Instance;
            if (!k || d == null || !mgr || !kit) return;
            ulong id = kit.Spec.id;
            var kind = Kits.KindOf(id);
            mgr.Forget(id);
            int v = Kits.ShelfVariant(kind);
            if (v >= 0)
            {
                Shelves.Add(v, k._pos, k._rot);
                d.kitShelves++;
                CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.kit_put_up", Kits.Name(kind)));
            }
            else Machines.Add(kind, k._pos, k._rot);
            Audio.WorldSound.PlayAt(Audio.SoundBank.Tick(0), k._pos, 0.6f, 10f, 0.6f);
            k._showing = false;
        }

        void Update()
        {
            if (!_hand) _hand = FindAnyObjectByType<PropGrabber>();
            _showing = false;
            IdleHint = null;
            var held = _hand ? _hand.Held : null;
            if (held && held.Spec.kind == MovableKind.Box && Improvements.IsKit(held.Spec.id))
            {
                var kind = Kits.KindOf(held.Spec.id);
                if (kind != Kits.Kind.Chute)
                {
                    if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) _turn = (_turn + 90f) % 360f;
                    Aim(held, kind);
                }
            }
            if (_showing) Draw();
            if (!held && (!_hand || !_hand.HeldItem)) Packing(); else { _packT = 0f; _packing = 0; }
        }

        // ---------------- The outline ----------------

        public static Vector3 FootprintOf(Kits.Kind kind)
        {
            int v = Kits.ShelfVariant(kind);
            return v >= 0 ? ShelfTypes.Get(v).size : Machines.Size(kind);
        }

        void Aim(MovableProp held, Kits.Kind kind)
        {
            var cam = Camera.main;
            if (!cam) return;
            _kit = held.Spec.id; _kind = kind; _size = FootprintOf(kind);
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            if (!FirstSolid(eye, fwd, 5f, held, out var hit)) return;
            Vector3 at = hit.point;
            // Aimed at a wall: its foot there, a little out from it.
            if (Mathf.Abs(hit.normal.y) < 0.5f) at += new Vector3(hit.normal.x, 0f, hit.normal.z).normalized * (_size.z * 0.5f + 0.02f);
            if (!Floor(at, held, out float y)) return;
            at.y = y;
            // Facing you, turned by R.
            var face = Vector3.ProjectOnPlane(-fwd, Vector3.up);
            if (face.sqrMagnitude < 1e-4f) face = -cam.transform.up;
            var rot = Quaternion.LookRotation(face.normalized, Vector3.up) * Quaternion.Euler(0f, _turn, 0f);
            // A conveyor near the end of another: on in line with it.
            bool lined = false;
            if (kind == Kits.Kind.Conveyor && GameSession.Data != null)
                foreach (var cid in GameSession.Data.conveyors)
                {
                    if (!MovablePropManager.Instance || !MovablePropManager.Instance.Pose(cid, out var cp, out var cr)) continue;
                    float len = Machines.Size(Kits.Kind.Conveyor).z;
                    foreach (int e in new[] { 1, -1 })
                    {
                        var end = cp + cr * Vector3.forward * (len * e);
                        if (Vector3.Distance(new Vector3(end.x, at.y, end.z), at) > 0.6f) continue;
                        at = new Vector3(end.x, cp.y, end.z); rot = cr; lined = true;
                        break;
                    }
                    if (lined) break;
                }
            // A wall close behind it (shelves and generators): flat against it, facing out.
            bool snaps = Kits.ShelfVariant(kind) >= 0 || kind == Kits.Kind.Generator;
            if (snaps && !lined && Wall(at + Vector3.up * 0.9f, rot * Vector3.back, _size.z * 0.5f + 0.7f, held, out var wall))
            {
                var n = new Vector3(wall.normal.x, 0f, wall.normal.z).normalized;
                rot = Quaternion.LookRotation(n, Vector3.up);
                var w = wall.point; w.y = at.y;
                // (Keep where along the wall it was aimed.)
                var along = Vector3.Cross(Vector3.up, n);
                at = w + n * (_size.z * 0.5f + 0.015f) + along * Vector3.Dot(at - w, along);
            }
            _pos = at; _rot = rot; _showing = true;
            _why = null;
            _valid = Fits(held) && Where(out _why);
        }

        bool Where(out string why)
        {
            why = null;
            if (!LeasePlan.OnCurrent(_pos) || HubPlan.InZone(_pos)) { why = "ONLY ON YOUR FLOOR"; return false; }
            return true;
        }

        bool Fits(MovableProp held)
        {
            var centre = _pos + _rot * new Vector3(0f, _size.y * 0.5f + 0.04f, 0f);
            var half = new Vector3(_size.x * 0.5f - 0.02f, _size.y * 0.5f - 0.03f, _size.z * 0.5f - 0.02f);
            int n = Physics.OverlapBoxNonAlloc(centre, half, s_over, _rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_over[i];
                if (!c || Ignored(c, held)) continue;
                _why = "SOMETHING IS IN THE WAY";
                return false;
            }
            return true;
        }

        static bool Ignored(Collider c, MovableProp held)
        {
            if (held && c.GetComponentInParent<MovableProp>() == held) return true;
            if (c is CharacterController || c.GetComponentInParent<CamcorderPlayerController>()) return true;
            if (c.GetComponentInParent<Net.RemotePlayer>()) return true;
            return false;
        }

        bool FirstSolid(Vector3 from, Vector3 dir, float range, MovableProp held, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(from, dir, s_hits, range, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (s_hits[i].distance >= bd || Ignored(s_hits[i].collider, held) || s_hits[i].collider.GetComponentInParent<WorldItem>()) continue;
                bd = s_hits[i].distance; best = s_hits[i];
            }
            return bd < float.MaxValue;
        }

        bool Floor(Vector3 at, MovableProp held, out float y)
        {
            y = at.y;
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 1.2f, Vector3.down, s_hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (h.normal.y < 0.7f || h.distance >= bd || Ignored(h.collider, held) || h.collider.GetComponentInParent<MovableProp>() || h.collider.GetComponentInParent<WorldItem>()) continue;
                bd = h.distance; y = h.point.y;
            }
            return bd < float.MaxValue;
        }

        /// <summary>The building's wall (not a prop or an item) along a ray, within range.</summary>
        bool Wall(Vector3 from, Vector3 dir, float range, MovableProp held, out RaycastHit wall)
        {
            wall = default;
            int n = Physics.RaycastNonAlloc(from, dir, s_hits, range, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (h.distance >= bd || Mathf.Abs(h.normal.y) > 0.3f || Ignored(h.collider, held)) continue;
                if (h.collider.GetComponentInParent<MovableProp>() || h.collider.GetComponentInParent<WorldItem>() || h.collider.attachedRigidbody) continue;
                bd = h.distance; wall = h;
            }
            return bd < float.MaxValue;
        }

        void Draw()
        {
            var mesh = MeshFor(_kind);
            if (!mesh) return;
            if (!_green) { _green = Ghost(new Color(0.45f, 1f, 0.5f, 0.32f)); _red = Ghost(new Color(1f, 0.32f, 0.25f, 0.34f)); }
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(_pos, _rot, Vector3.one), _valid ? _green : _red, 0);
        }

        static Material Ghost(Color c)
        {
            var m = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            m.SetInt("_ZWrite", 0);
            m.SetColor("_Color", c);
            return m;
        }

        /// <summary>The kit's model as one mesh (its fitting's parts), or a box its size.</summary>
        Mesh MeshFor(Kits.Kind kind)
        {
            int v = Kits.ShelfVariant(kind);
            string model = v >= 0 ? ShelfTypes.Model(v) : Machines.Model(kind);
            if (_meshes.TryGetValue(model, out var m) && m) return m;
            var parts = FittingModels.Ready ? FittingModels.Get(model) : null;
            var w = new MeshWriter();
            if (parts != null)
                foreach (var p in parts) w.AddMesh(p.vertices, p.normals, p.tangents, p.uv, p.triangles, Vector3.zero, Quaternion.identity);
            if (w.VertexCount == 0)
            {
                var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                var s = FootprintOf(kind);
                w.AddMesh(cube.vertices, cube.normals, cube.tangents, cube.uv, cube.triangles, new Vector3(0f, s.y * 0.5f, 0f), Quaternion.identity, s);
            }
            m = w.ToMesh("KitGhost_" + model);
            _meshes[model] = m;
            return m;
        }

        // ---------------- Packing a shelf back up ----------------

        void Packing()
        {
            var cam = Camera.main;
            MovableProp shelf = null;
            if (cam && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 3.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                var mp = hit.collider.GetComponentInParent<MovableProp>();
                if (mp && (mp.Spec.kind == MovableKind.Shelf || Machines.Is(mp.Spec.id))) shelf = mp;
            }
            if (!shelf) { _packT = 0f; _packing = 0; return; }
            if (shelf.Spec.kind == MovableKind.Shelf && ShelfTypes.IsBroken(shelf.Spec.variant))
            {
                IdleHint = Backrooms.UI.Words.Get("hint.knocked_over", Shelves.MendLeft(shelf.Spec.id));
                return;
            }
            bool g = Keyboard.current != null && Keyboard.current.gKey.isPressed;
            string click = !Machines.Is(shelf.Spec.id) ? "" : Machines.KindOf(shelf.Spec.id) == Kits.Kind.Generator ? "CLICK: ITS LOAD · "
                         : Machines.PluggedInto(shelf.Spec.id) != 0 ? "CLICK: UNPLUG · " : "CLICK: PLUG INTO A GENERATOR · ";
            IdleHint = g ? Backrooms.UI.Words.Get("hint.packing") : click + Backrooms.UI.Words.Get("hint.pack_up");
            if (!g || _packing != shelf.Spec.id) { _packT = 0f; _packing = g ? shelf.Spec.id : 0; return; }
            _packT += Time.deltaTime;
            if (_packT < 1.1f) return;
            _packT = 0f; _packing = 0;
            if (shelf.Spec.kind == MovableKind.Shelf && HasBoxes(shelf)) { CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.boxes_off_first")); return; }
            var at = shelf.transform.position + shelf.transform.rotation * new Vector3(0f, 0.01f, 0.9f);
            var rot = shelf.transform.rotation;
            Kits.Kind kind;
            if (shelf.Spec.kind == MovableKind.Shelf) { kind = Kits.ForShelf(shelf.Spec.variant); Shelves.Remove(shelf.Spec.id); }
            else { kind = Machines.KindOf(shelf.Spec.id); Machines.Remove(shelf.Spec.id); }
            Kits.Spawn(kind, at, rot);
            CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.packed_up", Kits.Name(kind)));
        }

        static bool HasBoxes(MovableProp shelf)
        {
            var t = ShelfTypes.Get(shelf.Spec.variant);
            var centre = shelf.transform.TransformPoint(new Vector3(0f, t.size.y * 0.5f + 0.1f, 0f));
            int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(t.size.x * 0.5f, t.size.y * 0.5f + 0.1f, t.size.z * 0.5f + 0.1f), s_over, shelf.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var mp = s_over[i] ? s_over[i].GetComponentInParent<MovableProp>() : null;
                if (mp && mp != shelf && mp.Spec.kind == MovableKind.Box) return true;
            }
            return false;
        }
    }
}
