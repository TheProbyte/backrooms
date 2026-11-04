using System.Collections;
using System.Text;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Scripted check of cords: walks away from a wall outlet with the desk lamp plugged in (it should hold you back, then
    /// pull out if you keep going), walks with the lamp in hand (smooth speed, nothing shoving), and reels an extension cord
    /// in from its plug end (the box is dragged in, coiled and ends up in the hand). <c>CordProbe.Run()</c>; read
    /// <see cref="Report"/> when <see cref="Done"/>.
    /// </summary>
    public class CordProbe : MonoBehaviour
    {
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();
        CamcorderPlayerController _pl;
        PropGrabber _grab;

        public static CordProbe Run()
        {
            var go = new GameObject("CordProbe");
            DontDestroyOnLoad(go);
            return go.AddComponent<CordProbe>();
        }

        IEnumerator Start()
        {
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _grab = _pl ? _pl.GetComponent<PropGrabber>() : null;
            if (!_grab) { _log.AppendLine("no player"); Done = true; yield break; }
            Socket socket = null; float best = float.MaxValue;
            foreach (var s in Sockets.Around(_pl.transform.position))
            {
                if (!s.IsWall || s.occupant != null) continue;
                float d = (s.Position - _pl.transform.position).sqrMagnitude;
                if (d < best) { best = d; socket = s; }
            }
            if (socket == null) { _log.AppendLine("no wall socket near"); Done = true; yield break; }
            Vector3 n = socket.Normal; n.y = 0f; n.Normalize();
            Vector3 stand = socket.Position + n * 0.7f; stand.y = _pl.transform.position.y;
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;          // facing away from the wall

            // 1. Walking with the lamp (cord wound up): speed should be smooth.
            _pl.Teleport(stand, yaw, 10f);
            var lamp = (DeskLamp)ItemFactory.Spawn("desklamp", stand + n * 0.4f + Vector3.up * 0.2f, Quaternion.identity);
            yield return Wait(0.3f);
            _grab.PickUp(lamp);
            yield return Wait(0.6f);
            _pl.botInput = new Vector2(0f, 1f);
            yield return Wait(0.8f);
            float min = float.MaxValue, max = 0f; Vector3 last = _pl.transform.position;
            for (int f = 0; f < 60; f++)
            {
                yield return null;
                Vector3 p = _pl.transform.position;
                float v = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(last.x, 0, last.z)) / Mathf.Max(Time.deltaTime, 1e-4f);
                last = p;
                min = Mathf.Min(min, v); max = Mathf.Max(max, v);
            }
            _pl.botInput = Vector2.zero;
            _log.AppendLine($"walk with lamp: speed {min:0.00}..{max:0.00} m/s (walk speed {_pl.walkSpeed})");
            yield return Wait(0.5f);

            // 2. Plugged in, walking away: held back, then out.
            _pl.Teleport(stand, yaw, 10f);
            yield return Wait(0.4f);
            lamp.Unbundle();
            var plug = lamp.Plug;
            plug.PlugInto(socket);
            yield return Wait(0.6f);
            _pl.botInput = new Vector2(0f, 1f);
            float t0 = Time.time, stopAt = -1f, outAt = -1f;
            while (Time.time - t0 < 4f)
            {
                float d = Vector3.Distance(_pl.transform.position, socket.Position);
                if (stopAt < 0f && _pl.LeashStrain > 0f) stopAt = Time.time - t0;
                if (outAt < 0f && !plug.Plugged) outAt = Time.time - t0;
                if (((int)((Time.time - t0) * 10f)) % 3 == 0)
                    _log.AppendLine($"  t {Time.time - t0:0.00} d {d:0.00} v {_pl.PlanarVelocity.magnitude:0.00} strain {_pl.LeashStrain:0.00} plugged {plug.Plugged}");
                yield return new WaitForSeconds(0.1f);
            }
            _pl.botInput = Vector2.zero;
            _log.AppendLine($"tug: held back after {stopAt:0.00} s, pulled out after {outAt:0.00} s");
            _grab.Drop();
            yield return Wait(0.3f);
            if (lamp) Destroy(lamp.gameObject);

            // 3. An extension cord reeled in from its plug.
            _pl.Teleport(stand, yaw, 10f);
            yield return Wait(0.3f);
            var box = (CordBox)ItemFactory.Spawn("extensioncord", stand + n * 0.3f + Vector3.up * 0.2f, Quaternion.identity);
            yield return Wait(0.3f);
            box.Deploy(socket);
            _grab.PickUp(box);
            yield return Wait(0.3f);
            _pl.Teleport(stand + n * 4f, yaw, 10f);
            yield return Wait(1f);
            _grab.Drop();                                    // set down 4 m out
            yield return Wait(1f);
            _pl.Teleport(stand, yaw + 180f, 20f);
            yield return Wait(0.6f);
            var cordPlug = box.Plug;
            _grab.PickUp(cordPlug);                          // out of the wall, into the hand
            yield return Wait(0.5f);
            _grab.botRmb = true;
            t0 = Time.time;
            while (Time.time - t0 < 9f && _grab.HeldItem != box)
            {
                if (((int)((Time.time - t0) * 10f)) % 10 == 0 && box && box.Plug)
                    _log.AppendLine($"  reel t {Time.time - t0:0.0} box {Vector3.Distance(box.transform.position, _pl.transform.position):0.00} m");
                yield return new WaitForSeconds(0.1f);
            }
            _grab.botRmb = false;
            _log.AppendLine($"reel from plug: {(box && _grab.HeldItem == box ? "box in hand" : "not done")}, coiled {(box ? box.coiled : false)} after {Time.time - t0:0.0} s");
            _grab.Drop();
            yield return Wait(0.3f);
            if (box) Destroy(box.gameObject);
            Debug.Log("[CordProbe]\n" + Report);
            Done = true;
        }

        static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }
    }
}
