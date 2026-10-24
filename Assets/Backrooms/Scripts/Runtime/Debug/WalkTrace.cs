using System.Text;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Debug: records the player's height, grounding and eye each frame while a bot walks, and summarises
    /// bumps (frames where the height jumped more than a step) and time spent airborne. Used to check
    /// stairs and elevators move smoothly.
    /// </summary>
    public class WalkTrace : MonoBehaviour
    {
        public Vector2 input = new Vector2(0f, 1f);
        public float seconds = 20f;
        public bool done;
        public string report = "";

        CamcorderPlayerController _pl;
        float _t, _lastY, _maxStepUp, _maxDrop, _airTime, _minY = float.MaxValue, _maxY = float.MinValue, _eyeMax;
        int _frames, _airFrames;
        Vector3 _start;
        readonly StringBuilder _log = new StringBuilder();

        void Start()
        {
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _start = _pl.transform.position; _lastY = _start.y;
        }

        void Update()
        {
            if (done) return;
            _pl.botInput = input;
            _t += Time.deltaTime; _frames++;
            float y = _pl.transform.position.y;
            float dy = y - _lastY;
            _maxStepUp = Mathf.Max(_maxStepUp, dy); _maxDrop = Mathf.Min(_maxDrop, dy);
            if (!_pl.IsGrounded) { _airFrames++; _airTime += Time.deltaTime; }
            _minY = Mathf.Min(_minY, y); _maxY = Mathf.Max(_maxY, y);
            _lastY = y;
            if (_frames % 20 == 0) _log.AppendLine($"t {_t:0.0} pos {_pl.transform.position:F2} grounded {_pl.IsGrounded}");
            if (_t >= seconds)
            {
                done = true; _pl.botInput = null;
                report = $"walked {Vector3.Distance(_start, _pl.transform.position):0.0} m in {_t:0.0} s, y {_minY:0.00}..{_maxY:0.00}, biggest rise/frame {_maxStepUp:0.000}, biggest drop/frame {_maxDrop:0.000}, airborne {_airFrames}/{_frames} frames\n" + _log;
            }
        }
    }
}
