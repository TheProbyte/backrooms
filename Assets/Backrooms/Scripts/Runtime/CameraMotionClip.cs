using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Recorded camcorder motion (2026-10-28; more takes 2026-11-03): how a camera held in the hand moves, measured with a
    /// phone held like the camcorder (Docs/CameraMotionCapture.md) and built by Tools/CameraMotion/build_camera_motion.py.
    /// The look direction is taken out (the mouse does that); what is left is the sway, the bob of each step and the hand's
    /// tremor: pitch, yaw, roll (degrees, the camera's local Euler angles) and x right, y up, z forward (metres).
    ///  - Walk, StrafeLeft, StrafeRight: cut into strides (a heel strike to the same foot's next) of a fixed number of
    ///    samples, played by stride so they keep step with the feet (HandheldCameraShake); the strides follow each other as
    ///    recorded and the loop has no seams. Each stride is the rhythm every stride shares (their average, sample by
    ///    sample) plus how this one differs from it: Sample's variation scales the second part (the walk's irregular
    ///    jitter) and keeps the first (Camera menu > Walk jitter).
    ///  - Idle, Look: seamless loops played by seconds (a "stride" of one second). Look's motion is the tremor of turning
    ///    at one degree rms, with how strong it is at each turn speed (TurnTremor).
    /// </summary>
    public sealed class CameraMotionClip
    {
        [System.Serializable]
        class Data
        {
            public int samplesPerStride;
            public int strides;
            public float strideSeconds;
            public float rightFootPhase;
            public float[] data;
            public float[] turnSpeeds;
            public float[] turnTremor;
        }

        const int Channels = 6;
        float[] _d, _mean;
        float[] _turnSpeeds, _turnTremor;

        public int Strides { get; private set; }
        public int SamplesPerStride { get; private set; }
        /// <summary>A stride's length as recorded (s): two steps. 1 for the loops played by seconds.</summary>
        public float StrideSeconds { get; private set; }
        /// <summary>Where in a stride the right foot lands (the left lands at 0).</summary>
        public float RightFootPhase { get; private set; }
        /// <summary>The whole loop's length as recorded (s).</summary>
        public float Seconds => Strides * StrideSeconds;
        /// <summary>Whether it measured the tremor of turning at each turn speed (Look).</summary>
        public bool HasTurnTremor => _turnSpeeds != null && _turnSpeeds.Length > 0;

        /// <summary>Reads a clip from Resources (a JSON TextAsset); null if it is missing or malformed.</summary>
        public static CameraMotionClip Load(string resource)
        {
            var ta = Resources.Load<TextAsset>(resource);
            if (!ta) return null;
            var clip = FromJson(ta.text);
            if (clip == null) Debug.LogWarning("[Backrooms] Camera motion " + resource + " could not be read.");
            return clip;
        }

        public static CameraMotionClip FromJson(string json)
        {
            Data d;
            try { d = JsonUtility.FromJson<Data>(json); }
            catch (System.Exception) { return null; }
            if (d == null || d.strides < 2 || d.samplesPerStride < 4 || d.data == null ||
                d.data.Length != d.strides * d.samplesPerStride * Channels || d.strideSeconds <= 0f) return null;
            var c = new CameraMotionClip
            {
                _d = d.data, Strides = d.strides, SamplesPerStride = d.samplesPerStride, StrideSeconds = d.strideSeconds,
                RightFootPhase = Mathf.Clamp(d.rightFootPhase, 0.3f, 0.7f),
            };
            if (d.turnSpeeds != null && d.turnTremor != null && d.turnSpeeds.Length == d.turnTremor.Length && d.turnSpeeds.Length > 0)
            {
                c._turnSpeeds = d.turnSpeeds;
                c._turnTremor = d.turnTremor;
            }
            // The rhythm all the strides share: their average, sample by sample.
            int n = d.samplesPerStride * Channels;
            c._mean = new float[n];
            for (int s = 0; s < d.strides; s++)
                for (int i = 0; i < n; i++) c._mean[i] += d.data[s * n + i];
            for (int i = 0; i < n; i++) c._mean[i] /= d.strides;
            return c;
        }

        /// <summary>
        /// The motion at a point along the loop, in strides (wraps either way): rotation in degrees (pitch, yaw, roll) and
        /// position in metres (right, up, forward). Smooth between samples (Catmull-Rom). variation: how much of each
        /// stride's own difference from the shared rhythm is kept (1 as recorded, 0 the same every stride).
        /// </summary>
        public void Sample(float strides, out Vector3 rotation, out Vector3 position, float variation = 1f)
        {
            int total = Strides * SamplesPerStride;
            float x = Mathf.Repeat(strides * SamplesPerStride, total);
            int i1 = Mathf.Min((int)x, total - 1);
            float f = x - i1;
            int i0 = (i1 - 1 + total) % total, i2 = (i1 + 1) % total, i3 = (i1 + 2) % total;
            rotation = new Vector3(Ch(i0, i1, i2, i3, f, 0, variation), Ch(i0, i1, i2, i3, f, 1, variation), Ch(i0, i1, i2, i3, f, 2, variation));
            position = new Vector3(Ch(i0, i1, i2, i3, f, 3, variation), Ch(i0, i1, i2, i3, f, 4, variation), Ch(i0, i1, i2, i3, f, 5, variation));
        }

        float Ch(int i0, int i1, int i2, int i3, float f, int c, float variation)
        {
            float raw = At(_d, i0, i1, i2, i3, f, c);
            if (variation == 1f) return raw;
            int S = SamplesPerStride;
            float mean = At(_mean, i0 % S, i1 % S, i2 % S, i3 % S, f, c);
            return mean + (raw - mean) * variation;
        }

        /// <summary>One raw sample (stride, sample within it, channel 0-5).</summary>
        public float Raw(int stride, int sample, int channel) => _d[(stride * SamplesPerStride + sample) * Channels + channel];

        /// <summary>The strides' shared rhythm (sample within a stride, channel 0-5).</summary>
        public float Mean(int sample, int channel) => _mean[sample * Channels + channel];

        /// <summary>How strong the hand's tremor was while turning this fast (rms degrees, the three axes together), from
        /// the measured points; held at the ends.</summary>
        public float TurnTremor(float degreesPerSecond)
        {
            if (!HasTurnTremor) return 0f;
            float s = Mathf.Abs(degreesPerSecond);
            if (s <= _turnSpeeds[0]) return _turnTremor[0];
            for (int i = 1; i < _turnSpeeds.Length; i++)
                if (s < _turnSpeeds[i])
                    return Mathf.Lerp(_turnTremor[i - 1], _turnTremor[i], (s - _turnSpeeds[i - 1]) / (_turnSpeeds[i] - _turnSpeeds[i - 1]));
            return _turnTremor[_turnTremor.Length - 1];
        }

        static float At(float[] d, int i0, int i1, int i2, int i3, float f, int c)
        {
            float p0 = d[i0 * Channels + c], p1 = d[i1 * Channels + c], p2 = d[i2 * Channels + c], p3 = d[i3 * Channels + c];
            float f2 = f * f, f3 = f2 * f;
            return 0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (3f * p1 - p0 - 3f * p2 + p3) * f3);
        }
    }
}
