using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Look check for the hand: holds each item (eye view and a close side view), grabs the nearest sofa at a few spots,
    /// and flicks the nearest light switch (frames of the flick, and the room before and after). Add in Play Mode with
    /// <c>HandShots.Run(folder, what)</c> (what: "items", "sofa", "switch", or "all"); <see cref="Done"/> when finished.
    /// </summary>
    public class HandShots : MonoBehaviour
    {
        public string folder, what = "all";
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();

        CamcorderPlayerController _pl;
        PropGrabber _grab;
        Camera _shot;
        Light _fill;
        public bool fill = true;
        RenderTexture _rt;
        Texture2D _tex;

        public static HandShots Run(string folder, string what = "all")
        {
            var go = new GameObject("HandShots");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var h = go.AddComponent<HandShots>();
            h.folder = folder; h.what = what;
            go.SetActive(true);
            return h;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _grab = _pl ? _pl.GetComponent<PropGrabber>() : null;
            if (!_grab) { _log.AppendLine("no player"); Done = true; yield break; }
            var go = new GameObject("HandShotsCam");
            go.transform.SetParent(transform, false);
            _shot = go.AddComponent<Camera>();
            _shot.enabled = false;
            // A fill light on the camera, only while a close view is taken (the hand is often in shadow).
            var lg = new GameObject("HandShotsFill");
            lg.transform.SetParent(go.transform, false);
            _fill = lg.AddComponent<Light>();
            _fill.type = LightType.Point; _fill.range = 3f; _fill.intensity = 1.6f; _fill.shadows = LightShadows.None;
            _fill.renderingLayerMask = -1;
            _fill.enabled = false;
            _rt = new RenderTexture(640, 480, 24);
            _tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
            if (what == "all" || what == "items") yield return Items();
            if (what == "all" || what == "sofa") yield return Sofa();
            if (what == "all" || what == "switch") yield return Switch();
            if (what == "all" || what == "door") yield return Doors();
            if (what == "all" || what == "tape") yield return Tape();
            Destroy(_rt); Destroy(_tex);
            Done = true;
        }

        /// <summary>Under a lit ceiling light near the player (the hand shows; a dark room's raised gain would wash it out).</summary>
        IEnumerator GoSomewhereLit()
        {
            Light best = null; float bd = float.MaxValue;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.enabled || l.type != LightType.Spot || l.intensity < 1f || l.transform.position.y < 2f) continue;
                float d = (l.transform.position - _pl.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = l; }
            }
            if (!best) yield break;
            Vector3 p = best.transform.position + new Vector3(0.7f, 0f, 0.4f);
            if (Physics.Raycast(p, Vector3.down, out var floor, 4f)) p = floor.point + Vector3.up * 0.05f;
            _pl.Teleport(p, _pl.transform.eulerAngles.y, 0f);
            yield return Wait(2.5f);
        }

        IEnumerator Items()
        {
            yield return GoSomewhereLit();
            foreach (var id in new[] { "tape", "desklamp", "extensioncord", "spraycan", "chalk" })
            {
                var t = _pl.transform;
                var item = ItemFactory.Spawn(id, t.position + t.forward * 0.6f + Vector3.up * 0.3f, t.rotation);
                yield return Wait(0.3f);
                _grab.PickUp(item);
                yield return Wait(0.8f);
                Eye($"item_{id}_eye.png", 0f);
                Eye($"item_{id}_down.png", 35f);
                Side($"item_{id}_side.png", _grab.HandPosition, 0.55f);
                Side($"item_{id}_front.png", _grab.HandPosition, 0.5f, front: true);
                _grab.Drop();
                yield return Wait(0.2f);
                if (item) Destroy(item.gameObject);
                yield return null;
            }
        }

        IEnumerator Sofa()
        {
            MovableProp sofa = null; float best = float.MaxValue;
            foreach (var p in FindObjectsByType<MovableProp>())
            {
                if (p.Spec.kind != MovableKind.Sofa) continue;
                float d = (p.transform.position - _pl.transform.position).sqrMagnitude;
                if (d < best) { best = d; sofa = p; }
            }
            if (!sofa) { _log.AppendLine("no sofa loaded"); yield break; }
            var b = new Bounds(sofa.transform.position, Vector3.zero);
            foreach (var r in sofa.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            _log.AppendLine($"sofa {sofa.name} at {sofa.transform.position} size {b.size}");
            // Spots: top of the back, an arm, the front of the seat (in the sofa's frame: front is +Z).
            var t = sofa.transform;
            var spots = new (string name, Vector3 local, Vector3 from)[]
            {
                ("back", new Vector3(0.1f, 0.78f, -0.35f), new Vector3(0.1f, 0f, 0.9f)),
                ("arm", new Vector3(b.extents.x * 0.92f, 0.6f, 0.0f), new Vector3(b.extents.x + 0.7f, 0f, 0.35f)),
                ("seat", new Vector3(-0.2f, 0.42f, 0.38f), new Vector3(-0.2f, 0f, 1.05f)),
            };
            foreach (var s in spots)
            {
                Vector3 target = t.TransformPoint(s.local);
                Vector3 stand = t.TransformPoint(s.from); stand.y = t.position.y + 0.05f;
                Vector3 look = target - (stand + Vector3.up * _pl.standEye);
                float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                _pl.Teleport(stand, yaw, pitch);
                yield return Wait(0.5f);
                _grab.botHold = true;
                for (float w = 0; w < 3f && _grab.Current != PropGrabber.State.Holding; w += Time.deltaTime) yield return null;
                yield return Wait(0.6f);
                _log.AppendLine($"{s.name}: {_grab.Current} hand {_grab.HandPosition} curl {_grab.HandCurl:0.00}");
                Eye($"sofa_{s.name}_eye.png", -1f);
                Side($"sofa_{s.name}_side.png", _grab.HandPosition, 0.6f);
                Side($"sofa_{s.name}_front.png", _grab.HandPosition, 0.55f, front: true);
                _grab.botHold = false;
                yield return Wait(0.5f);
            }
        }

        IEnumerator Switch()
        {
            LightSwitch sw = null; float best = float.MaxValue;
            foreach (var s in FindObjectsByType<LightSwitch>())
            {
                float d = (s.transform.position - _pl.transform.position).sqrMagnitude;
                if (d < best) { best = d; sw = s; }
            }
            if (!sw) { _log.AppendLine("no switch loaded"); yield break; }
            var t = sw.transform;
            Vector3 stand = t.position + t.forward * 0.65f; stand.y = t.position.y - 1.2f + 0.05f;
            stand.y = Mathf.Max(stand.y, _pl.transform.position.y - 5f);
            Vector3 look = sw.FocusPoint - (stand + Vector3.up * _pl.standEye);
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            _pl.Teleport(stand, yaw, pitch);
            yield return Wait(0.8f);
            var lights = FindAnyObjectByType<FixtureLightManager>();
            _log.AppendLine($"switch at {t.position}; lights on {(lights ? lights.ActiveLights : -1)}");
            // The room, behind the player (turned round), before.
            Room("switch_room_before.png", t);
            _grab.botHold = true;
            for (int f = 0; f < 40; f++)
            {
                if (f % 3 == 0) Side($"switch_flick_{f:00}.png", sw.FocusPoint, 0.45f);
                yield return null;
            }
            _grab.botHold = false;
            yield return Wait(1.2f);
            _log.AppendLine($"after: lights on {(lights ? lights.ActiveLights : -1)}");
            Room("switch_room_after.png", t);
            Eye("switch_after_eye.png", -1f);
            // And back on.
            yield return Wait(0.5f);
            _grab.botHold = true;
            yield return Wait(0.5f);
            _grab.botHold = false;
            yield return Wait(1.5f);
            _log.AppendLine($"on again: lights on {(lights ? lights.ActiveLights : -1)}");
            Room("switch_room_on_again.png", t);
        }

        /// <summary>The room the switch lights: from 3 m out in it, looking back at the switch's wall and up a little.</summary>
        IEnumerator Doors()
        {
            var shut = new List<Door>();
            foreach (var d in FindObjectsByType<Door>()) if (d.Angle < 0.5f) shut.Add(d);
            shut.Sort((a, b) => (a.transform.position - _pl.transform.position).sqrMagnitude.CompareTo((b.transform.position - _pl.transform.position).sqrMagnitude));
            _log.AppendLine($"doors loaded {FindObjectsByType<Door>().Length}, shut {shut.Count}");
            for (int k = 0; k < Mathf.Min(2, shut.Count); k++)
            {
                var d = shut[k];
                bool push = k == 0;                         // first: from the far side, pushing; second: from its swing side, pulling
                var t = d.transform;
                Vector3 handle = d.GripPoint(push ? -1 : 1, 0f);
                Vector3 n = t.forward * (push ? -1f : 1f);
                Vector3 stand = handle + n * 0.75f + t.right * 0.1f; stand.y = t.position.y + 0.05f;
                Vector3 look = handle - (stand + Vector3.up * _pl.standEye);
                _pl.Teleport(stand, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
                yield return Wait(0.6f);
                string tag = push ? "push" : "pull";
                Eye($"door_{tag}_0_before.png", -1f);
                _grab.botHold = true;
                yield return Wait(1.0f);
                _log.AppendLine($"{tag}: on door {_grab.OnDoor}, angle {d.Angle:0.0}, latched {d.Latched}");
                Eye($"door_{tag}_1_grip.png", -1f);
                Side($"door_{tag}_1_grip_side.png", _grab.HandPosition, 0.5f);
                _pl.botInput = new Vector2(0f, push ? 0.6f : -0.6f);
                yield return Wait(0.35f);
                _pl.botInput = Vector2.zero;
                yield return Wait(0.4f);
                _log.AppendLine($"{tag}: a step, angle {d.Angle:0.0}, on door {_grab.OnDoor}");
                Eye($"door_{tag}_2_peek.png", -1f);
                _pl.botInput = new Vector2(0f, push ? 1f : -1f);
                yield return Wait(1.0f);
                _log.AppendLine($"{tag}: walked on, angle {d.Angle:0.0}, speed {d.Speed:0} deg/s, on door {_grab.OnDoor}");
                _grab.botHold = false;
                yield return Wait(0.1f);
                _pl.botInput = Vector2.zero;
                yield return Wait(1.2f);
                _log.AppendLine($"{tag}: let go, angle {d.Angle:0.0}");
                Eye($"door_{tag}_3_after.png", -1f);
            }
        }

        IEnumerator Tape()
        {
            // At a wall with an outside corner near: right mouse to aim, left to stick and drag round the corner.
            var t = _pl.transform;
            var item = ItemFactory.Spawn("tape", t.position + t.forward * 0.4f + Vector3.up * 0.3f, t.rotation);
            yield return Wait(0.3f);
            _grab.PickUp(item);
            yield return Wait(0.6f);
            Eye("tape_hold.png", 20f);
            Side("tape_hold_side.png", _grab.HandPosition, 0.45f);
            if (!Physics.Raycast(t.position + Vector3.up * 1.2f, t.forward, out var wall, 6f)) { _log.AppendLine("tape: no wall ahead"); yield break; }
            Vector3 n = wall.normal; n.y = 0f; n.Normalize();
            Vector3 stand = wall.point - Vector3.up * 1.2f + n * 0.85f; stand.y = t.position.y + 0.05f;
            float yaw = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;
            _pl.Teleport(stand, yaw - 12f, 8f);
            yield return Wait(0.5f);
            _grab.botRmb = true;
            yield return Wait(0.5f);
            Eye("tape_aim.png", -1f);
            _grab.botHold = true;
            yield return Wait(0.2f);
            _grab.botRmb = false;
            for (int f = 0; f < 50; f++) { _pl.AddLook(0.8f, f < 25 ? 0.12f : -0.12f); yield return null; }
            Eye("tape_pull.png", -1f);
            _grab.botHold = false;
            yield return Wait(0.4f);
            Eye("tape_laid.png", -1f);
            var tape = item as TapeRoll;
            _log.AppendLine($"tape: {(tape ? tape.metres : -1f):0.00} m left on the roll");
            _grab.Drop();
            if (item) Destroy(item.gameObject, 1f);
        }

        void Room(string name, Transform sw)
        {
            var main = Camera.main;
            _shot.CopyFrom(main);
            _shot.fieldOfView = 80f;
            Vector3 from = sw.position + sw.forward * 3f + Vector3.up * 0.5f;
            _shot.transform.position = from;
            _shot.transform.rotation = Quaternion.LookRotation(-sw.forward + Vector3.up * 0.35f);
            Save(name);
        }

        /// <summary>From the eye; pitch &lt; 0 keeps the view's own pitch.</summary>
        void Eye(string name, float pitch)
        {
            var main = Camera.main;
            _shot.CopyFrom(main);
            var rot = pitch < 0f ? main.transform.rotation : Quaternion.Euler(pitch, main.transform.eulerAngles.y, 0f);
            _shot.transform.SetPositionAndRotation(main.transform.position, rot);
            Save(name);
        }

        /// <summary>A close view of a point from the player's right (or from in front of the player, looking back).</summary>
        void Side(string name, Vector3 at, float dist, bool front = false)
        {
            var main = Camera.main;
            _shot.CopyFrom(main);
            _shot.fieldOfView = 45f; _shot.nearClipPlane = 0.02f;
            var t = _pl.transform;
            Vector3 from = front ? at + t.forward * dist + Vector3.up * 0.08f - t.right * 0.1f : at + t.right * dist + Vector3.up * 0.12f + t.forward * 0.05f;
            _shot.transform.position = from;
            _shot.transform.LookAt(at);
            _fill.enabled = fill;
            Save(name);
            _fill.enabled = false;
        }

        void Save(string name)
        {
            _shot.targetTexture = _rt;
            _shot.Render();
            _shot.targetTexture = null;
            var old = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0); _tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(folder, name), _tex.EncodeToPNG());
        }

        static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }
    }
}
