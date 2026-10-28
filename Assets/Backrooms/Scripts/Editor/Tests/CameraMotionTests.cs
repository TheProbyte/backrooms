using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The recorded camcorder walk (CameraMotionClip, Resources/CameraMotion/Walk from the phone recordings).</summary>
    public class CameraMotionTests
    {
        static CameraMotionClip Walk()
        {
            var clip = CameraMotionClip.Load("CameraMotion/Walk");
            Assert.IsNotNull(clip, "Resources/CameraMotion/Walk.json is missing or could not be read");
            return clip;
        }

        [Test]
        public void TheWalkLoadsWithAWalkersPace()
        {
            var w = Walk();
            Assert.GreaterOrEqual(w.Strides, 8);
            Assert.AreEqual(64, w.SamplesPerStride);
            // A stride is two steps: 0.9-1.6 s is 75-135 steps a minute; the feet alternate about half way.
            Assert.That(w.StrideSeconds, Is.InRange(0.9f, 1.6f));
            Assert.That(w.RightFootPhase, Is.InRange(0.4f, 0.6f));
        }

        [Test]
        public void TheLoopHasNoJumps()
        {
            // Including where the recording's stretches join and where the loop wraps: no sample further from the last than
            // the hand's own quickest movement (about 1 degree, a few mm at 64 samples a stride).
            var w = Walk();
            int n = w.Strides * w.SamplesPerStride;
            float rot = 0f, pos = 0f;
            for (int i = 0; i < n; i++)
            {
                int a = i, b = (i + 1) % n;
                for (int c = 0; c < 6; c++)
                {
                    float d = Mathf.Abs(w.Raw(b / w.SamplesPerStride, b % w.SamplesPerStride, c) - w.Raw(a / w.SamplesPerStride, a % w.SamplesPerStride, c));
                    if (c < 3) rot = Mathf.Max(rot, d); else pos = Mathf.Max(pos, d);
                }
            }
            Assert.Less(rot, 1.5f, "degrees between samples");
            Assert.Less(pos, 0.01f, "metres between samples");
        }

        [Test]
        public void TheCameraIsLowestAsEachFootLands()
        {
            // Walking is an inverted pendulum: the body (and the camera in the hand) is lowest as a foot takes the weight
            // and highest over the standing leg, a quarter stride later. Averaged over the strides.
            var w = Walk();
            float AvgUp(float phase)
            {
                float s = 0f;
                for (int k = 0; k < w.Strides; k++) { w.Sample(k + phase, out _, out var p); s += p.y; }
                return s / w.Strides;
            }
            float landL = AvgUp(0f), landR = AvgUp(w.RightFootPhase);
            float overL = AvgUp(w.RightFootPhase * 0.5f), overR = AvgUp((w.RightFootPhase + 1f) * 0.5f);
            Assert.Less(landL, overL);
            Assert.Less(landR, overR);
            // A real walk's bob at the camera: a few centimetres.
            Assert.That(overL - landL, Is.InRange(0.005f, 0.06f));
        }

        [Test]
        public void SamplingHitsTheRecordedSamplesAndWraps()
        {
            var w = Walk();
            w.Sample(3f + 10f / w.SamplesPerStride, out var r, out var p);
            Assert.AreEqual(w.Raw(3, 10, 1), r.y, 1e-4f);
            Assert.AreEqual(w.Raw(3, 10, 4), p.y, 1e-5f);
            w.Sample(w.Strides + 0.5f, out var r2, out _);
            w.Sample(0.5f, out var r3, out _);
            Assert.AreEqual(r3.x, r2.x, 1e-4f);
            Assert.IsNull(CameraMotionClip.FromJson("{\"strides\":2,\"samplesPerStride\":4,\"strideSeconds\":1,\"data\":[1,2]}"));
        }
    }
}
