using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Measures the handheld camera motion (HandheldCameraShake with the phone recordings, 2026-11-03): stands still, walks
    /// forward, side-steps left then right, turns slowly and whips, three times over (the recordings as recorded, the walk
    /// jitter at 0, the recordings off), writing the camera's local rotation and position every frame to a CSV. Add in
    /// Play Mode in a world (World 3), set <see cref="folder"/>; <see cref="done"/> when finished. Puts the settings back.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CameraMotionProbe : MonoBehaviour
    {
        public string folder;
        public bool done;
        public string result = "";

        readonly StringBuilder _csv = new StringBuilder();
        CamcorderPlayerController _ctrl;
        HandheldCameraShake _shake;
        MethodInfo _look;
        float _turn;          // degrees a second to turn this frame
        string _pass = "", _phase = "";

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            _ctrl = ws.player.GetComponent<CamcorderPlayerController>();
            _shake = FindAnyObjectByType<HandheldCameraShake>();
            var rig = FindAnyObjectByType<CamcorderRig>();
            _look = typeof(CamcorderPlayerController).GetMethod("UpdateLook", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!_ctrl || !_shake || !rig || _look == null) { result = "missing parts"; done = true; yield break; }
            var s = rig.Settings;
            bool rec0 = s.recordedWalk; float jit0 = s.walkJitter;
            Vector3 start = ws.player.position; float yaw0 = ws.player.eulerAngles.y;
            _csv.AppendLine("t,pass,phase,pitch,yaw,roll,x,y,z,speed,strafeL,strafeR,turn");
            var passes = new[] { ("recorded", true, 1f), ("jitter0", true, 0f), ("generated", false, 1f) };
            foreach (var (name, rec, jit) in passes)
            {
                s.recordedWalk = rec; s.walkJitter = jit; _pass = name;
                _ctrl.Teleport(start, yaw0);
                yield return Phase("stand", 8f, Vector2.zero, 0f);
                yield return Phase("walk", 5f, new Vector2(0f, 1f), 0f);
                _ctrl.botInput = null; yield return new WaitForSeconds(1f);
                _ctrl.Teleport(start, yaw0);
                yield return Phase("left", 5f, new Vector2(-1f, 0f), 0f);
                yield return Phase("right", 5f, new Vector2(1f, 0f), 0f);
                _ctrl.botInput = null;
                yield return Phase("settle", 1.5f, Vector2.zero, 0f);
                // Slow turns: 60 degrees a second, a second each way; then whips: 300 a second for 0.4 s, a pause between.
                for (int k = 0; k < 4; k++) yield return Phase("turn", 1f, Vector2.zero, (k & 1) == 0 ? 60f : -60f);
                for (int k = 0; k < 4; k++)
                {
                    yield return Phase("whip", 0.4f, Vector2.zero, (k & 1) == 0 ? 300f : -300f);
                    yield return Phase("whip", 0.8f, Vector2.zero, 0f);
                }
                _turn = 0f;
            }
            _ctrl.botInput = null;
            s.recordedWalk = rec0; s.walkJitter = jit0;
            _ctrl.Teleport(start, yaw0);
            File.WriteAllText(Path.Combine(folder, "camera_motion.csv"), _csv.ToString());
            result = "ok";
            done = true;
        }

        IEnumerator Phase(string phase, float seconds, Vector2 move, float turn)
        {
            _phase = phase;
            _ctrl.botInput = move == Vector2.zero ? (Vector2?)null : move;
            _turn = turn;
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        void LateUpdate()
        {
            if (done || !_ctrl) return;
            // Turns fed the way the mouse does (before HandheldCameraShake reads LookDelta this frame).
            if (_turn != 0f) _look.Invoke(_ctrl, new object[] { new Vector2(_turn * Time.deltaTime / Mathf.Max(1e-4f, _ctrl.mouseSensitivity), 0f) });
        }

        void Update()
        {
            if (done || !_shake || _pass == "") return;
            var t = _shake.transform;
            Vector3 e = t.localEulerAngles;
            float W(float a) => Mathf.DeltaAngle(0f, a);
            Vector3 p = t.localPosition;
            var w = _shake.StrafeWeights;
            _csv.AppendLine(string.Join(",", Time.time.ToString("F4", CultureInfo.InvariantCulture), _pass, _phase,
                F(W(e.x)), F(W(e.y)), F(W(e.z)), F(p.x), F(p.y), F(p.z), F(_ctrl.PlanarVelocity.magnitude), F(w.x), F(w.y), F(_turn)));
        }

        static string F(float v) => v.ToString("F5", CultureInfo.InvariantCulture);
    }
}
