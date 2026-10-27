using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks of the realism pass in play mode: shut doors in the light grid (a wall, then an opening when opened), light
    /// under a shut door, Steam Audio geometry, footsteps from the feet, leaning, finger contact, the low-light gain, the
    /// lamp's dust. <c>RealismProbe.Run(folder)</c>; read <see cref="Report"/> when <see cref="Done"/>.
    /// </summary>
    public class RealismProbe : MonoBehaviour
    {
        public string folder;
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();
        CamcorderPlayerController _pl;
        PropGrabber _grab;
        FirstPersonBody _body;
        Camera _shot;
        RenderTexture _rt;
        Texture2D _tex;

        public static RealismProbe Run(string folder)
        {
            var go = new GameObject("RealismProbe");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var p = go.AddComponent<RealismProbe>();
            p.folder = folder;
            go.SetActive(true);
            return p;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _grab = _pl.GetComponent<PropGrabber>();
            _body = _pl.GetComponent<FirstPersonBody>();
            var cg = new GameObject("RealismCam"); cg.transform.SetParent(transform, false);
            _shot = cg.AddComponent<Camera>(); _shot.enabled = false;
            _rt = new RenderTexture(800, 600, 24); _tex = new Texture2D(800, 600, TextureFormat.RGB24, false);
            Vector3 home = _pl.transform.position;
            float homeYaw = _pl.transform.eulerAngles.y;

#if STEAMAUDIO_ENABLED
            _log.AppendLine($"steam audio: {Audio.SteamAudioWorld.Meshes} meshes, {Audio.SteamAudioWorld.Triangles} triangles; shut doors {DoorEdges.Count}");
#endif
            yield return DoorLight();
            yield return Steps(home, homeYaw);
            yield return Lean(home, homeYaw);
            yield return Fingers(home, homeYaw);
            yield return LowLight(home, homeYaw);
            yield return Dust(home, homeYaw);
            yield return Doorways();
            Destroy(_rt); Destroy(_tex);
            Done = true;
        }

        IEnumerator DoorLight()
        {
            var lights = FindAnyObjectByType<FixtureLightManager>();
            Door pick = null; float best = 0f; bool frontDark = false;
            foreach (var d in FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                if (d.Angle > 0.5f) continue;
                var f = typeof(Door).GetField("_spillF", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var b = typeof(Door).GetField("_spillB", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                float sf = (float)f.GetValue(d), sb = (float)b.GetValue(d);
                if (Mathf.Abs(sf - sb) > best) { best = Mathf.Abs(sf - sb); pick = d; frontDark = sf > sb; }
            }
            if (!pick) { _log.AppendLine("doors: none shut"); yield break; }
            var grid = lights ? lights.grid : null;
            var edge = (DoorEdges.Edge)typeof(Door).GetField("_edge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(pick);
            uint Occ()
            {
                if (!grid) return 99;
                int cx = edge.vertical ? edge.line : edge.along, cz = edge.vertical ? edge.along : edge.line;
                uint o = grid.OccupancyAt(cx, cz, edge.storey);
                return edge.vertical ? LightGridMath.WestKind(o) : LightGridMath.SouthKind(o);
            }
            yield return Wait(0.3f);
            _log.AppendLine($"door at {pick.transform.position}: shut, edge kind in the light grid {Occ()} (1 = wall); spill difference {best:0.00}");
            // Stand on the dark side, looking at the bottom of the door.
            var t = pick.transform;
            Vector3 n = t.forward * (frontDark ? 1f : -1f);
            Vector3 stand = t.position + n * 1.3f; stand.y = t.position.y + 0.05f;
            Vector3 look = (t.position + Vector3.up * 0.3f) - (stand + Vector3.up * _pl.standEye);
            _pl.Teleport(stand, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
            yield return Wait(2f);
            Eye("door_spill_shut.png");
            // Open it a little by hand-free means (as the air or a nudge would), then see the grid change.
            var tryMove = typeof(Door).GetMethod("TryMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tryMove.Invoke(pick, new object[] { 25f });
            yield return Wait(1.5f);
            _log.AppendLine($"opened to {pick.Angle:0} deg: edge kind {Occ()} (3 = headed opening)");
            Eye("door_spill_open.png");
            tryMove.Invoke(pick, new object[] { 0f });
            yield return Wait(0.5f);
            _log.AppendLine($"shut again: edge kind {Occ()}");
        }

        IEnumerator Steps(Vector3 home, float yaw)
        {
            _pl.Teleport(home, yaw, 0f);
            yield return Wait(0.5f);
            int before = _body ? _body.StepsLanded : 0, touches = _grab.Touches;
            _pl.botInput = new Vector2(0f, 1f);
            yield return Wait(2.5f);
            _pl.botInput = Vector2.zero;
            yield return Wait(0.8f);
            var pres = _pl.GetComponent<BodyPresence>();
            _log.AppendLine($"walk 2.5 s: {(_body ? _body.StepsLanded - before : -1)} steps from the feet, hand touches {_grab.Touches - touches}, exertion {(pres ? pres.Exertion : -1):0.00}, floor {Audio.Surfaces.At(_pl.transform.position, _pl.transform)}");
        }

        IEnumerator Lean(Vector3 home, float yaw)
        {
            _pl.Teleport(home, yaw, 0f);
            yield return Wait(0.4f);
            _pl.botLean = 1f;
            yield return Wait(1.2f);
            _log.AppendLine($"lean right: {_pl.Lean:0.00}, head {_pl.cameraPitchPivot.localPosition.x:0.00} m to the side");
            Eye("lean_right.png");
            _pl.botLean = -1f;
            yield return Wait(1.5f);
            _log.AppendLine($"lean left: {_pl.Lean:0.00}");
            _pl.botLean = null;
            yield return Wait(1f);
        }

        IEnumerator Fingers(Vector3 home, float yaw)
        {
            foreach (var id in new[] { "tape", "desklamp", "spraycan" })
            {
                _pl.Teleport(home, yaw, 0f);
                var item = ItemFactory.Spawn(id, home + _pl.transform.forward * 0.5f + Vector3.up * 0.3f, Quaternion.identity);
                yield return Wait(0.3f);
                _grab.PickUp(item);
                yield return Wait(1.2f);
                _log.AppendLine($"holding {id}: {(_body ? _body.ContactStops : -1)} fingers stopped by its surface");
                Side($"fingers_{id}.png", _grab.HandPosition, 0.32f);
                _grab.Drop();
                yield return Wait(0.2f);
                if (item) Destroy(item.gameObject);
            }
        }

        IEnumerator LowLight(Vector3 home, float yaw)
        {
            _pl.Teleport(home, yaw, 0f);
            yield return Wait(1f);
            var exp = FindAnyObjectByType<CamcorderAutoExposure>();
            var ws = WorldStreamer.Current;
            var L = ws.LayoutAt(WorldStreamer.ChunkOf(home));
            if (L == null || !exp) { _log.AppendLine("low light: no layout"); yield break; }
            int x = Mathf.FloorToInt(home.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(home.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            var chunk = new Vector3Int(L.coord.x, L.storey, L.coord.y);
            int room = L.roomOf[x, z];
            float ev0 = exp.CurrentEV;
            LightZones.Set(chunk, room, true);
            yield return Wait(0.6f);
            float evDark = exp.CurrentEV;
            Eye("lights_off_0.6s.png");
            yield return Wait(6f);
            _log.AppendLine($"lights out: exposure {ev0:0.00} EV -> {evDark:0.00} at 0.6 s -> {exp.CurrentEV:0.00} at 6.6 s (low-light boost {exp.LowLightBoost:0.00}, luminance {exp.MeasuredLuminance:0.0000})");
            Eye("lights_off_6.6s.png");
            LightZones.Set(chunk, room, false);
            yield return Wait(2f);
        }

        IEnumerator Dust(Vector3 home, float yaw)
        {
            Socket socket = null; float best = float.MaxValue;
            foreach (var s in Sockets.Around(home))
            {
                if (!s.IsWall || s.occupant != null) continue;
                float d = (s.Position - home).sqrMagnitude;
                if (d < best) { best = d; socket = s; }
            }
            if (socket == null) { _log.AppendLine("dust: no socket"); yield break; }
            Vector3 n = socket.Normal; n.y = 0f; n.Normalize();
            Vector3 at = socket.Position + n * 0.6f; at.y = home.y;
            var lamp = (DeskLamp)ItemFactory.Spawn("desklamp", at + Vector3.up * 0.05f, Quaternion.LookRotation(n));
            yield return Wait(0.3f);
            lamp.Unbundle();
            lamp.Plug.PlugInto(socket);
            lamp.switchOn = true;
            Vector3 side = Vector3.Cross(Vector3.up, n);
            Vector3 stand = at + side * 1.4f + n * 0.4f; stand.y = home.y + 0.05f;
            Vector3 look = (at + Vector3.up * 0.25f + n * 0.3f) - (stand + Vector3.up * _pl.standEye);
            _pl.Teleport(stand, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
            yield return Wait(3f);
            var ps = lamp.GetComponentInChildren<ParticleSystem>();
            _log.AppendLine($"lamp lit {lamp.Lit}; dust particles {(ps ? ps.particleCount : -1)}");
            Eye("lamp_dust.png");
            if (lamp) Destroy(lamp.gameObject);
        }

        /// <summary>Walk through a few doorways with the jamb on the hand's side: does the hand steady on it?</summary>
        IEnumerator Doorways()
        {
            var ws = WorldStreamer.Current;
            _grab.steadyChance = 1f;
            int tried = 0, before = _grab.Touches;
            var c0 = WorldStreamer.ChunkOf(_pl.transform.position);
            for (int dc = 0; dc < 9 && tried < 5; dc++)
            {
            var c = new Vector2Int(c0.x + dc % 3 - 1, c0.y + dc / 3 - 1);
            var L = ws.LayoutAt(c);
            if (L == null) continue;
            int n = L.N;
            for (int line = 1; line < n && tried < 5; line++)
            for (int along = 0; along < n && tried < 5; along++)
            {
                if (L.RoomAt(line - 1, along).noFloor || L.RoomAt(line, along).noFloor) continue;
                if (L.vEdges[line, along] != EdgeKind.DoorHeader) continue;
                bool shut = DoorEdges.IsShut(true, c.x * n + line, c.y * n + along, 0);
                if (shut) continue;
                Vector3 centre = new Vector3((c.x * n + line) * OfficeBiome.CellSize, 0.05f, (c.y * n + along + 0.5f) * OfficeBiome.CellSize);
                // Walking +x through it, a little to the right (the jamb on the right, at -z).
                Vector3 start = centre - Vector3.right * 1.6f - Vector3.forward * 0.18f;
                _pl.Teleport(start, 90f, 0f);
                yield return Wait(0.4f);
                _pl.botInput = new Vector2(0f, 1f);
                yield return Wait(2.2f);
                _pl.botInput = Vector2.zero;
                yield return Wait(0.5f);
                tried++;
            }
            }
            _grab.steadyChance = 0.55f;
            _log.AppendLine($"doorways walked through: {tried}, hand steadied on a jamb {_grab.Touches - before} times");
        }

        void Eye(string name)
        {
            var main = Camera.main;
            _shot.CopyFrom(main);
            _shot.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            Save(name);
        }

        void Side(string name, Vector3 at, float dist)
        {
            var main = Camera.main;
            _shot.CopyFrom(main);
            _shot.fieldOfView = 40f; _shot.nearClipPlane = 0.02f;
            var t = _pl.transform;
            _shot.transform.position = at + t.right * dist + Vector3.up * 0.08f + t.forward * 0.04f;
            _shot.transform.LookAt(at);
            Save(name);
        }

        void Save(string name)
        {
            _shot.targetTexture = _rt; _shot.Render(); _shot.targetTexture = null;
            var old = RenderTexture.active; RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0); _tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(folder, name), _tex.EncodeToPNG());
        }

        static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }
    }
}
