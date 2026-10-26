using System.Collections;
using System.IO;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Look check for the first-person body: the bot walks forwards, backwards and sideways, crouch-walks, and gets up from
    /// a crawl, while frames are saved from a camera beside the player (and from the eye for the getting-up part). Add in
    /// Play Mode with <c>BodyShots.Run(folder)</c>; <see cref="Done"/> when finished.
    /// </summary>
    public class BodyShots : MonoBehaviour
    {
        public string folder;
        public bool Done { get; private set; }
        public string Report { get; private set; } = "running";

        public static BodyShots Run(string folder)
        {
            var go = new GameObject("BodyShots");
            var b = go.AddComponent<BodyShots>();
            b.folder = folder;
            return b;
        }

        public bool shadowOnly, fromEye;

        /// <summary>Walks forward looking down at the floor and saves every frame (the player's shadow, for jitter).</summary>
        public static BodyShots RunShadow(string folder, bool fromEye = false)
        {
            var go = new GameObject("BodyShots");
            go.SetActive(false);
            var b = go.AddComponent<BodyShots>();
            b.folder = folder; b.shadowOnly = true; b.fromEye = fromEye;
            go.SetActive(true);
            return b;
        }

        IEnumerator Shadow(CamcorderPlayerController ctrl)
        {
            // A still camera ahead of the player, looking down at the floor the player walks over (or the eye, looking down).
            var go = new GameObject("ShadowCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main); cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f;
            var t0 = ctrl.transform;
            Vector3 start = t0.position, fwd = t0.forward;
            go.transform.position = start + fwd * 3.2f + Vector3.up * 1.9f;
            go.transform.LookAt(start + fwd * 1.4f);
            if (fromEye) go.transform.SetParent(Camera.main.transform.parent, false);
            var rt = new RenderTexture(480, 360, 24); cam.targetTexture = rt;
            var tex = new Texture2D(480, 360, TextureFormat.RGB24, false);
            ctrl.botInput = new Vector2(0, 1);
            yield return Wait(0.5f);
            for (int f = 0; f < 30; f++)
            {
                if (fromEye) { go.transform.position = Camera.main.transform.position; go.transform.rotation = Quaternion.Euler(62f, ctrl.transform.eulerAngles.y, 0f); }
                Save(cam, rt, tex, $"shadow_{f:00}.png");
                yield return null;
            }
            ctrl.botInput = null;
            cam.targetTexture = null;
            Destroy(rt); Destroy(tex); Destroy(go);
            Report = "done"; Done = true;
        }

        IEnumerator Start()
        {
            if (shadowOnly)
            {
                Directory.CreateDirectory(folder);
                var w = WorldStreamer.Current;
                yield return Shadow(w.player.GetComponent<CamcorderPlayerController>());
                yield break;
            }
            Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            var ctrl = ws && ws.player ? ws.player.GetComponent<CamcorderPlayerController>() : null;
            if (!ctrl) { Report = "no player"; Done = true; yield break; }
            var camGo = new GameObject("BodyShotsCam");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(Camera.main); cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(640, 480, 24); cam.targetTexture = rt;
            var tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
            var start = ctrl.transform.position;
            float yaw = ctrl.transform.eulerAngles.y;

            var modes = new (string name, Vector2 input, CamcorderPlayerController.Stance stance)[]
            {
                ("fwd", new Vector2(0, 1), CamcorderPlayerController.Stance.Stand),
                ("back", new Vector2(0, -1), CamcorderPlayerController.Stance.Stand),
                ("right", new Vector2(1, 0), CamcorderPlayerController.Stance.Stand),
                ("crouch", new Vector2(0, 1), CamcorderPlayerController.Stance.Crouch),
            };
            foreach (var m in modes)
            {
                ctrl.Teleport(start, yaw, 0f);
                ctrl.SetStance(m.stance);
                ctrl.botInput = Vector2.zero;
                yield return Wait(0.8f);
                ctrl.botInput = m.input;
                yield return Wait(0.8f);
                for (int f = 0; f < 6; f++)
                {
                    // Beside the player (on the left of its direction of travel), level with the hips.
                    var t = ctrl.transform;
                    Vector3 side = t.right * -1.6f;
                    cam.transform.position = t.position + side + Vector3.up * 0.9f;
                    cam.transform.LookAt(t.position + Vector3.up * 0.75f);
                    Save(cam, rt, tex, $"{m.name}_{f}.png");
                    if (f % 2 == 0)
                    {
                        // From the eye, looking down at the body.
                        var main = Camera.main;
                        cam.transform.SetPositionAndRotation(main.transform.position, Quaternion.Euler(55f, t.eulerAngles.y, 0f));
                        Save(cam, rt, tex, $"{m.name}_eye_{f / 2}.png");
                    }
                    yield return Wait(0.12f);
                }
                ctrl.botInput = Vector2.zero;
            }
            // Getting up from a crawl, seen from the eye looking down a little, and from the side.
            ctrl.Teleport(start, yaw, 0f);
            ctrl.SetStance(CamcorderPlayerController.Stance.Crawl);
            yield return Wait(1.2f);
            ctrl.SetStanceEased(CamcorderPlayerController.Stance.Stand);
            for (int f = 0; f < 10; f++)
            {
                var main = Camera.main;
                cam.transform.SetPositionAndRotation(main.transform.position, Quaternion.Euler(35f, ctrl.transform.eulerAngles.y, 0f));
                Save(cam, rt, tex, $"getup_eye_{f}.png");
                var t = ctrl.transform;
                cam.transform.position = t.position - t.right * 1.6f + Vector3.up * 0.9f;
                cam.transform.LookAt(t.position + Vector3.up * 0.7f);
                Save(cam, rt, tex, $"getup_side_{f}.png");
                yield return Wait(0.09f);
            }
            ctrl.botInput = null;
            cam.targetTexture = null;
            Destroy(rt); Destroy(tex); Destroy(camGo);
            Report = "done"; Done = true;
        }

        void Save(Camera cam, RenderTexture rt, Texture2D tex, string name)
        {
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(folder, name), tex.EncodeToPNG());
        }

        static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }
    }
}
