using System.Collections;
using System.IO;
using Backrooms.Destruction;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Play-mode check of the sledgehammer: finds a breakable wall near the player, stands in front of it, picks up a
    /// sledgehammer and swings it (full wind-ups, aimed round a spot), with screenshots of the view before, holding it,
    /// winding up, at impact, after several blows, from the other side, and with that side's lights off (light through the
    /// hole). Add it in Play Mode and call Run(folder); read Log when Done.
    /// </summary>
    public class WallBreakProbe : MonoBehaviour
    {
        public bool Done { get; private set; }
        public string Log { get; private set; } = "";
        public WallKey Wall { get; private set; }
        string _folder;
        RenderTexture _full;

        public void Run(string folder) { _folder = folder; Directory.CreateDirectory(folder); StartCoroutine(Go()); }

        /// <summary>After the world was saved and loaded again: stand where the blows were struck and photograph the wall.</summary>
        public void Revisit(string folder, WallKey wall) { _folder = folder; Wall = wall; StartCoroutine(Again()); }

        IEnumerator Again()
        {
            var ctrl = FindAnyObjectByType<CamcorderPlayerController>();
            float pitch = WallDamage.Pitch;
            var p = WallDamage.Find(Wall);
            Note(p == null ? "after loading: the broken wall is not there" : $"after loading: boards gone {p.state.RemovedCount(0)} and {p.state.RemovedCount(1)}, rubble {p.rubble.Count}, taken {WallDamage.Taken(Wall)}");
            Vector3 face = Wall.ToWorld(0.6f, 0f, -WallSpec.T * 0.5f, pitch);
            float yaw = Quaternion.LookRotation(Wall.Across).eulerAngles.y;
            ctrl.TeleportSafe(face - Wall.Across * 1.1f, yaw, 14f);
            yield return new WaitForSeconds(2f);
            Note($"broken panel object: {(BrokenPanel.Live.ContainsKey(Wall) ? "yes" : "no")}");
            yield return Shot("12_after_saving_and_loading");
            Done = true;
        }

        void Note(string s) { Log += s + "\n"; Debug.Log("[WallBreakProbe] " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            if (_full == null || _full.width != Screen.width || _full.height != Screen.height)
            {
                if (_full) _full.Release();
                _full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            var small = RenderTexture.GetTemporary(960, 540, 0, RenderTextureFormat.ARGB32);
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(_full, small, new Vector2(1f, -1f), new Vector2(0f, 1f)); else Graphics.Blit(_full, small);
            var old = RenderTexture.active; RenderTexture.active = small;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            RenderTexture.active = old; RenderTexture.ReleaseTemporary(small);
            File.WriteAllBytes(Path.Combine(_folder, name + ".jpg"), tex.EncodeToJPG(90));
            Destroy(tex);
        }

        IEnumerator Go()
        {
            var streamer = WorldStreamer.Current;
            var ctrl = FindAnyObjectByType<CamcorderPlayerController>();
            var hand = ctrl ? ctrl.GetComponent<PropGrabber>() : null;
            if (!streamer || !ctrl || !hand) { Note("no world/player"); Done = true; yield break; }

            // The nearest breakable wall with a lit office on each side.
            Vector3 me = ctrl.transform.position;
            float best = float.MaxValue; WallKey found = default; bool any = false;
            int n = OfficeBiome.ChunkCells;
            var c0 = WorldStreamer.ChunkOf(me);
            for (int cx = c0.x - 1; cx <= c0.x + 1; cx++)
            for (int cz = c0.y - 1; cz <= c0.y + 1; cz++)
            {
                var L = streamer.LayoutAt(new Vector2Int(cx, cz));
                if (L == null) continue;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                for (int s = 0; s < 2; s++)
                {
                    bool vertical = s == 0;
                    if ((vertical ? L.vEdges[x, z] : L.hEdges[x, z]) != EdgeKind.Wall) continue;
                    var k = vertical ? new WallKey(true, cx * n + x, cz * n + z, 0) : new WallKey(false, cz * n + z, cx * n + x, 0);
                    if (!WallDamage.Breakable(k)) continue;
                    var mid = k.ToWorld(0.6f, 0f, 0f, WallDamage.Pitch);
                    float d = (mid - me).sqrMagnitude;
                    if (d < best) { best = d; found = k; any = true; }
                }
            }
            if (!any) { Note("no breakable wall nearby"); Done = true; yield break; }
            Wall = found;
            Note($"wall {found}");
            float pitch = WallDamage.Pitch;
            Vector3 face = found.ToWorld(0.6f, 0f, -WallSpec.T * 0.5f, pitch);
            Vector3 stand = face - found.Across * 0.72f;
            float yaw = Quaternion.LookRotation(found.Across).eulerAngles.y;
            ctrl.TeleportSafe(stand, yaw, 8f);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("00_before");

            var hammer = ItemFactory.Spawn("sledgehammer", ctrl.transform.position + ctrl.transform.forward * 0.4f + Vector3.up * 0.5f, Quaternion.identity);
            hand.PickUp(hammer);
            yield return new WaitForSeconds(0.9f);
            yield return Shot("01_holding");

            // Blows round a spot at chest height, then lower.
            Vector2[] aims = { new Vector2(0f, 6f), new Vector2(0f, 6f), new Vector2(-4f, 2f), new Vector2(5f, 10f), new Vector2(-3f, 14f), new Vector2(3f, 18f), new Vector2(0f, 24f), new Vector2(-5f, 22f), new Vector2(5f, 26f), new Vector2(0f, 32f) };
            for (int i = 0; i < aims.Length; i++)
            {
                ctrl.TeleportSafe(stand, yaw + aims[i].x, aims[i].y);
                yield return new WaitForSeconds(0.25f);
                hand.botHold = true;
                yield return new WaitForSeconds(0.62f);
                if (i == 0) yield return Shot("02_windup");
                hand.botHold = false;
                yield return new WaitForSeconds(0.19f);
                if (i == 0) yield return Shot("03_impact");
                yield return new WaitForSeconds(0.9f);
                var p = WallDamage.Find(found);
                if (p != null) Note($"blow {i + 1} [{WallBreaker.LastBlow}]: boards gone {p.state.RemovedCount(0)}/{p.model.boards[0].pieces.Count} and {p.state.RemovedCount(1)}/{p.model.boards[1].pieces.Count}, see-through {p.seeThrough}, open {p.openFraction:P0}, debris {WallDebris.Active}, rubble {p.rubble.Count}");
                if (i == 1) yield return Shot("04_two_blows");
                if (i == 5) yield return Shot("05_six_blows");
            }
            hand.botHold = null;
            ctrl.TeleportSafe(stand, yaw, 14f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("06_ten_blows");
            // Closer, looking into it.
            ctrl.TeleportSafe(stand + found.Across * 0.25f, yaw, 18f);
            yield return new WaitForSeconds(0.8f);
            yield return Shot("07_into_the_hole");
            hand.Drop();
            // From the other side.
            Vector3 other = found.ToWorld(0.6f, 0f, WallSpec.T * 0.5f, pitch) + found.Across * 1.4f;
            ctrl.TeleportSafe(other, yaw + 180f, 10f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("08_other_side");
            // The other side's lights off: light from the first room comes through the hole.
            var c1 = found.Chunk(1); var lc = found.LocalCell(1);
            var L1 = streamer.LayoutAt(c1);
            if (L1 != null)
            {
                int room = L1.roomOf[lc.x, lc.y];
                LightZones.Set(c1, room, true);              // off
                yield return new WaitForSeconds(2.5f);
                yield return Shot("09_other_side_dark");
                ctrl.TeleportSafe(other + found.Across * 0.8f, yaw + 180f, 22f);
                yield return new WaitForSeconds(1.2f);
                yield return Shot("10_light_through_the_hole");
                // The foot of the broken wall on the dark side: no light may come through where the wall is whole.
                ctrl.TeleportSafe(found.ToWorld(0.6f, 0f, WallSpec.T * 0.5f, pitch) + found.Across * 0.9f, yaw + 180f, 38f);
                yield return new WaitForSeconds(1.2f);
                yield return Shot("11_foot_of_the_wall_dark");
                LightZones.Set(c1, room, false);
            }
            var panel = WallDamage.Find(found);
            if (panel != null) Note($"final: holed {panel.holed}, see-through {panel.seeThrough}, open {panel.openFraction:P0}, studs {panel.state.studSegments[0].Count}+{panel.state.studSegments[1].Count} lengths, steel {panel.model.steel}, batts {panel.model.insulated}");
            Note($"broken panel object: {(BrokenPanel.Live.ContainsKey(found) ? "yes" : "no")}, taken {WallDamage.Taken(found)}");
            if (_full) { _full.Release(); Destroy(_full); }
            Done = true;
        }
    }
}
