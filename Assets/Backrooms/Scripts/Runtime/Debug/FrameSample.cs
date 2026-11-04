using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>Records frame times for a few seconds and keeps median / 95th percentile / worst (editor checks).</summary>
    public class FrameSample : MonoBehaviour
    {
        public static string Last = "";
        public static bool Busy;

        public static void Run(float seconds) { Busy = true; new GameObject("FrameSample").AddComponent<FrameSample>().StartCoroutine2(seconds); }

        void StartCoroutine2(float s) => StartCoroutine(Go(s));

        IEnumerator Go(float seconds)
        {
            yield return new WaitForSecondsRealtime(1f);
            var ms = new List<float>();
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { yield return null; ms.Add(Time.unscaledDeltaTime * 1000f); }
            ms.Sort();
            float P(float q) => ms[Mathf.Clamp(Mathf.RoundToInt(q * (ms.Count - 1)), 0, ms.Count - 1)];
            Last = $"median {P(0.5f):0.0} ms ({1000f / P(0.5f):0} fps)  p95 {P(0.95f):0.0} ms  worst {ms[ms.Count - 1]:0.0} ms  n={ms.Count}  {Screen.width}x{Screen.height}";
            Busy = false;
            Destroy(gameObject);
        }
    }
}
