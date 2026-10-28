using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Recorded camcorder motion (2026-10-28): how a camera held in the hand moves while walking, measured with a phone
    /// held like the camcorder (Docs/CameraMotionCapture.md) and built by Tools/CameraMotion/build_camera_motion.py. The
    /// look direction is taken out (the mouse does that); what is left is the sway, the bob of each step and the hand's
    /// tremor, cut into strides (a left heel strike to the next) of a fixed number of samples: pitch, yaw, roll (degrees,
    /// the camera's local Euler angles) and x right, y up, z forward (metres). Played by stride, so it keeps step with the
    /// feet (HandheldCameraShake); the strides follow each other as recorded and the loop has no seams.
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
        }

        const int Channels = 6;
        float[] _d;

        public int Strides { get; private set; }
        public int SamplesPerStride { get; private set; }
        /// <summary>A stride's length as recorded (s): two steps.</summary>
        public float StrideSeconds { get; private set; }
        /// <summary>Where in a stride the right foot lands (the left lands at 0).</summary>
        public float RightFootPhase { get; private set; }

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
            return new CameraMotionClip
            {
                _d = d.data, Strides = d.strides, SamplesPerStride = d.samplesPerStride, StrideSeconds = d.strideSeconds,
                RightFootPhase = Mathf.Clamp(d.rightFootPhase, 0.3f, 0.7f),
            };
        }

        /// <summary>
        /// The motion at a point along the loop, in strides (wraps either way): rotation in degrees (pitch, yaw, roll) and
        /// position in metres (right, up, forward). Smooth between samples (Catmull-Rom).
        /// </summary>
        public void Sample(float strides, out Vector3 rotation, out Vector3 position)
        {
            int total = Strides * SamplesPerStride;
            float x = Mathf.Repeat(strides * SamplesPerStride, total);
            int i1 = Mathf.Min((int)x, total - 1);
            float f = x - i1;
            int i0 = (i1 - 1 + total) % total, i2 = (i1 + 1) % total, i3 = (i1 + 2) % total;
            rotation = new Vector3(At(i0, i1, i2, i3, f, 0), At(i0, i1, i2, i3, f, 1), At(i0, i1, i2, i3, f, 2));
            position = new Vector3(At(i0, i1, i2, i3, f, 3), At(i0, i1, i2, i3, f, 4), At(i0, i1, i2, i3, f, 5));
        }

        /// <summary>One raw sample (stride, sample within it, channel 0-5).</summary>
        public float Raw(int stride, int sample, int channel) => _d[(stride * SamplesPerStride + sample) * Channels + channel];

        float At(int i0, int i1, int i2, int i3, float f, int c)
        {
            float p0 = _d[i0 * Channels + c], p1 = _d[i1 * Channels + c], p2 = _d[i2 * Channels + c], p3 = _d[i3 * Channels + c];
            float f2 = f * f, f3 = f2 * f;
            return 0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (3f * p1 - p0 - 3f * p2 + p3) * f3);
        }
    }
}
