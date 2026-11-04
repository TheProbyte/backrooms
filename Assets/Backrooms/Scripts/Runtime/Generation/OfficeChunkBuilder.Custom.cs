using System.Collections.Generic;
using Backrooms.CustomRooms;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Build mode's Backrooms touches in the world (round 18): marks on the walls (water stains with their tide line, damp,
    /// mould, scorch, handprints in the grime: the wear overlay; peeling paper: the wear's own curl; faded notices: a sheet
    /// with its lines), sounds that play there (a hum, a buzzing fitting, a drip), triggers (walk in and the lights cut out,
    /// flicker, a door slams, a sound), doorways linked to each other, and models the player brought in.
    /// Data on the worker thread (BuildCustomRoomExtras), objects on the main thread (InstantiateCustomRoomExtras).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Sounds to make (chunk-local): the layout's own and those of buzzing fittings.</summary>
        readonly List<RoomSound> _sounds = new List<RoomSound>();

        void BuildCustomRoomExtras()
        {
            foreach (var s in _L.sounds) _sounds.Add(s);
            foreach (var d in _L.decals) WallMark(d);
        }

        /// <summary>One mark on a wall face (see the class notes).</summary>
        void WallMark(WallDecal d)
        {
            int along = Mathf.FloorToInt(d.along / C);
            int cx = d.vertical ? (d.side < 0 ? d.line - 1 : d.line) : along, cz = d.vertical ? along : (d.side < 0 ? d.line - 1 : d.line);
            if (!_L.InChunk(cx, cz)) return;
            var room = _L.RoomAt(cx, cz);
            uint m = CellMask(cx, cz);
            Vector3 n = d.vertical ? new Vector3(d.side, 0, 0) : new Vector3(0, 0, d.side);
            Vector3 alongDir = d.vertical ? Vector3.forward : Vector3.right;
            float plane = d.line * C + d.side * T * 0.5f;
            Vector3 face = d.vertical ? new Vector3(plane, room.floor, d.along) : new Vector3(d.along, room.floor, plane);
            Vector3 P(float a, float y, float off = 0.0012f) => face + alongDir * a + Vector3.up * y + n * off;
            var rng = new System.Random(d.seed * 7919 + 13);
            float R() => (float)rng.NextDouble();
            float s = Mathf.Max(0.05f, d.size), y = d.height;
            switch ((DecalKind)d.kind)
            {
                case DecalKind.WaterStain:
                    // A round stain with a darker tide line, and its run down the wall.
                    _wallWear.Rect(P(0f, y), alongDir, Vector3.up, s, s * 0.85f, new Color32(132, 104, 66, 205), new Vector2(-0.3f, 0.85f));
                    _wallWear.Rect(P((R() - 0.5f) * s * 0.5f, y - s * 1.3f), alongDir, Vector3.up, s * 0.14f, s * 0.9f, new Color32(150, 124, 86, 150), new Vector2(0.3f, 0.6f));
                    break;
                case DecalKind.Damp:
                    _wallWear.Rect(P(0f, y), alongDir, Vector3.up, s * 1.3f, s, new Color32(120, 122, 104, 170), new Vector2(0.15f, 0.2f));
                    _wallWear.Rect(P(0f, y - s * 0.4f), alongDir, Vector3.up, s * 0.8f, s * 0.5f, new Color32(104, 108, 90, 150), new Vector2(0.2f, 0.25f));
                    break;
                case DecalKind.Mould:
                    for (int i = 0; i < 26; i++)
                    {
                        float a = (R() - 0.5f) * 2f * s, b = (R() - 0.5f) * 1.6f * s, r = 0.008f + 0.03f * R() * R();
                        if (a * a / (s * s) + b * b / (s * s * 0.64f) > 1f) continue;
                        _wallWear.Rect(P(a, y + b), alongDir, Vector3.up, r, r, new Color32(46, 52, 38, (byte)(170 + 70 * R())), new Vector2(-0.4f, 0.2f));
                    }
                    break;
                case DecalKind.Scorch:
                    _wallWear.Rect(P(0f, y), alongDir, Vector3.up, s, s * 1.4f, new Color32(40, 36, 32, 220), new Vector2(-0.15f, 0.3f));
                    _wallWear.Rect(P(0f, y + s * 1.1f), alongDir, Vector3.up, s * 0.7f, s * 0.9f, new Color32(80, 74, 66, 160), new Vector2(0.1f, 0.4f));
                    break;
                case DecalKind.Handprints:
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (k - 1) * s * 0.9f + (R() - 0.5f) * 0.1f, b = (R() - 0.5f) * 0.25f, lean = (R() - 0.5f) * 0.4f;
                        var right = Quaternion.AngleAxis(lean * Mathf.Rad2Deg, n) * alongDir; var up = Vector3.Cross(n, right) * -1f;
                        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
                        var c = P(a, y + b);
                        _wallWear.Rect(c, right, up, 0.045f, 0.05f, new Color32(70, 60, 52, 200), new Vector2(0.3f, 0.3f));
                        for (int f = 0; f < 4; f++) _wallWear.Rect(c + up * 0.085f + right * ((f - 1.5f) * 0.022f), right, up, 0.0075f, 0.03f, new Color32(70, 60, 52, 190), new Vector2(0.2f, 0.5f));
                        _wallWear.Rect(c + right * 0.06f + up * 0.01f, right, up, 0.008f, 0.022f, new Color32(70, 60, 52, 180), new Vector2(0.2f, 0.5f));
                    }
                    break;
                case DecalKind.Peeling:
                {
                    var paper = WMask(WallMat(room), m);
                    var bare = WMask(_a.paintedWall ? _a.paintedWall : WallMat(room), m);
                    float legY = R() < 0.5f ? s * 1.2f : -s * 1.2f;
                    Peel(paper, bare, P(0f, 0f, 0f), alongDir * (R() < 0.5f ? 1f : -1f), n, 0f, y, s * 1.1f, legY, 0.04f + 0.05f * R());
                    break;
                }
                case DecalKind.Notice:
                {
                    // A sheet of paper a little off the wall (a tape strip at the top), its print faded to grey lines.
                    var sheet = WMask(_a.paintedWall ? _a.paintedWall : WallMat(room), m);
                    float hw = s * 0.72f, hh = s, off = 0.0025f;
                    var c = P(0f, y, off);
                    sheet.Quad(c - alongDir * hw - Vector3.up * hh, c + alongDir * hw - Vector3.up * hh, c + alongDir * hw + Vector3.up * hh, c - alongDir * hw + Vector3.up * hh, n, MeshWriter.MetreUV);
                    var o = P(0f, y, off + 0.0012f);
                    _wallWear.Rect(o + Vector3.up * hh * 0.72f, alongDir, Vector3.up, hw * 0.7f, hh * 0.09f, new Color32(70, 70, 72, 220), new Vector2(0.95f, 0.9f));
                    for (int i = 0; i < 7; i++)
                        _wallWear.Rect(o + Vector3.up * (hh * 0.42f - i * hh * 0.16f) - alongDir * hw * 0.08f * R(), alongDir, Vector3.up, hw * (0.55f + 0.3f * R()), hh * 0.025f, new Color32(120, 120, 122, 200), new Vector2(0.95f, 0.8f));
                    _wallWear.Rect(o, alongDir, Vector3.up, hw, hh, new Color32(200, 190, 160, 160), new Vector2(0.4f, 0.4f));      // yellowed
                    _wallWear.Rect(o + Vector3.up * hh, alongDir, Vector3.up, hw * 0.3f, 0.012f, new Color32(190, 176, 130, 230), new Vector2(0.9f, 0.6f));
                    break;
                }
            }
        }

        /// <summary>A model the player brought in: its loaded template (CustomModels), placed as it was posed; static.</summary>
        void CustomModelProp(Prop p, Vector3 at)
        {
            var prefab = CustomModels.Get(p.model);
            if (!prefab) return;
            float s = p.scale > 0.01f ? p.scale : 1f;
            _instances.Add(new PropInstance
            {
                prefab = prefab, position = at + Vector3.up * p.elevation, rotation = Quaternion.Euler(p.pitch, p.yaw, p.roll),
                scale = Vector3.one * s, mask = LayerBit(p.roomColor), keepCollider = true,
            });
        }

        // ---------------- Main thread ----------------

        void InstantiateCustomRoomExtras(Transform root)
        {
            foreach (var s in _sounds)
            {
                var go = new GameObject("RoomSound");
                go.transform.SetParent(root, false);
                go.transform.localPosition = s.position;
                go.AddComponent<RoomSoundEmitter>().Init((SoundKind)s.kind, s.volume, s.range);
            }
            var chunk = new Vector3Int(_L.coord.x, _L.storey, _L.coord.y);
            foreach (var t in _L.triggers)
            {
                var go = new GameObject("RoomTrigger");
                go.transform.SetParent(root, false);
                var r = t.cells;
                float floor = _L.FloorAt(Mathf.Clamp(r.x, 0, _L.N - 1), Mathf.Clamp(r.y, 0, _L.N - 1));
                go.transform.localPosition = new Vector3(r.center.x * C, floor, r.center.y * C);
                var rooms = new List<int>();
                for (int x = r.xMin; x < r.xMax; x++) for (int z = r.yMin; z < r.yMax; z++)
                    if (_L.InChunk(x, z) && !rooms.Contains(_L.roomOf[x, z])) rooms.Add(_L.roomOf[x, z]);
                Transform leaf = null;
                if (t.hasDoor) leaf = DoorLeaf(root, t.door);
                go.AddComponent<RoomTriggerZone>().Init(t.id, new Vector3(r.width * C, 2.6f, r.height * C), (TriggerAction)t.action, (SoundKind)t.sound, t.once, t.delay, chunk, rooms, leaf);
            }
            foreach (var l in _L.links)
            {
                var go = new GameObject("LinkedDoorway");
                go.transform.SetParent(root, false);
                go.AddComponent<LinkedDoorway>().Init(DoorFrame(l.a), DoorFrame(l.b), l.flip, l.a.from, l.a.to, l.a.sill, l.a.top, l.b.to - l.b.from);
            }
        }

        /// <summary>A doorway's frame in the world: its middle on the wall line at the floor, +X along it, +Z its normal (+X / +Z).</summary>
        Pose DoorFrame(WallCutout c)
        {
            float mid = (c.from + c.to) * 0.5f;
            var local = c.vertical ? new Vector3(c.line * C, 0f, mid) : new Vector3(mid, 0f, c.line * C);
            var rot = c.vertical ? Quaternion.LookRotation(Vector3.right) : Quaternion.LookRotation(Vector3.forward);
            return new Pose(_origin + local, rot);
        }

        /// <summary>A door leaf hung in a doorway, swung open against the wall: a trigger slams it shut.</summary>
        Transform DoorLeaf(Transform root, WallCutout c)
        {
            float width = c.to - c.from, height = c.top - c.sill;
            var hinge = new GameObject("SlamDoor").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = c.vertical ? new Vector3(c.line * C, c.sill, c.from) : new Vector3(c.from, c.sill, c.line * C);
            hinge.localRotation = c.vertical ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(hinge, false);
            leaf.transform.localPosition = new Vector3(width * 0.5f, height * 0.5f, 0f);
            leaf.transform.localScale = new Vector3(width - 0.01f, height - 0.01f, 0.045f);
            var mr = leaf.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _a.wood ? _a.wood : _a.paintedWall;
            mr.renderingLayerMask = 0xFFFFFFFFu;
            hinge.localRotation *= Quaternion.Euler(0f, -88f, 0f);         // open
            return hinge;
        }
    }
}
