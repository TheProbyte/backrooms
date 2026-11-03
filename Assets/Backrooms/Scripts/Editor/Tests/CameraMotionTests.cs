using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The recorded camcorder motion (CameraMotionClip, Resources/CameraMotion from the phone recordings): the walk, the side-steps, standing and turning.</summary>
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

        static CameraMotionClip Clip(string name)
        {
            var clip = CameraMotionClip.Load("CameraMotion/" + name);
            Assert.IsNotNull(clip, "Resources/CameraMotion/" + name + ".json is missing or could not be read");
            return clip;
        }

        static void MaxStep(CameraMotionClip w, out float rot, out float pos)
        {
            int n = w.Strides * w.SamplesPerStride;
            rot = 0f; pos = 0f;
            for (int i = 0; i < n; i++)
            {
                int a = i, b = (i + 1) % n;
                for (int c = 0; c < 6; c++)
                {
                    float d = Mathf.Abs(w.Raw(b / w.SamplesPerStride, b % w.SamplesPerStride, c) - w.Raw(a / w.SamplesPerStride, a % w.SamplesPerStride, c));
                    if (c < 3) rot = Mathf.Max(rot, d); else pos = Mathf.Max(pos, d);
                }
            }
        }

        [Test]
        public void TheOtherRecordingsLoad()
        {
            var idle = Clip("Idle");
            Assert.Greater(idle.Seconds, 30f, "standing: seconds of it");
            foreach (var name in new[] { "StrafeLeft", "StrafeRight" })
            {
                var s = Clip(name);
                Assert.GreaterOrEqual(s.Strides, 8, name);
                Assert.That(s.StrideSeconds, Is.InRange(0.9f, 1.8f), name);
            }
            var look = Clip("Look");
            Assert.IsTrue(look.HasTurnTremor);
            // Faster turns, more tremor (never less), within what a hand does: a fraction of a degree to a couple.
            float last = 0f;
            foreach (float sp in new[] { 0f, 20f, 60f, 120f, 250f, 600f })
            {
                float t = look.TurnTremor(sp);
                Assert.GreaterOrEqual(t, last, $"tremor at {sp} deg/s");
                Assert.That(t, Is.InRange(0.05f, 2.5f));
                last = t;
            }
            Assert.Greater(look.TurnTremor(250f), look.TurnTremor(0f) * 2f, "a whip shakes more than standing");
            // Its texture: one degree rms, no spikes (the first build divided by a near-zero envelope at its edges: 340 degrees).
            int n = look.Strides * look.SamplesPerStride;
            double sum = 0; float max = 0f;
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(look.Raw(i / look.SamplesPerStride, i % look.SamplesPerStride, 0), look.Raw(i / look.SamplesPerStride, i % look.SamplesPerStride, 1),
                    look.Raw(i / look.SamplesPerStride, i % look.SamplesPerStride, 2));
                sum += v.sqrMagnitude; max = Mathf.Max(max, v.magnitude);
            }
            Assert.AreEqual(1f, Mathf.Sqrt((float)(sum / n)), 0.15f, "texture rms, degrees");
            Assert.Less(max, 4f, "texture peak, degrees");
        }

        [Test]
        public void StandingIsAStillHandsDriftAndLoopsWithoutJumps()
        {
            var w = Clip("Idle");
            int n = w.Strides * w.SamplesPerStride;
            for (int c = 0; c < 6; c++)
            {
                double s = 0, s2 = 0;
                for (int i = 0; i < n; i++) { float v = w.Raw(i / w.SamplesPerStride, i % w.SamplesPerStride, c); s += v; s2 += v * v; }
                float rms = Mathf.Sqrt((float)(s2 / n - (s / n) * (s / n)));
                if (c < 3) Assert.That(rms, Is.InRange(0.1f, 2f), $"rotation channel {c}, degrees rms");
                else Assert.That(rms, Is.InRange(0.0005f, 0.02f), $"movement channel {c}, metres rms");
            }
            MaxStep(w, out var rot, out var pos);
            Assert.Less(rot, 0.5f, "degrees between samples (100 a second)");
            Assert.Less(pos, 0.002f, "metres between samples");
        }

        [Test]
        public void TheSideStepsLoopWithoutJumps()
        {
            foreach (var name in new[] { "StrafeLeft", "StrafeRight" })
            {
                MaxStep(Clip(name), out var rot, out var pos);
                Assert.Less(rot, 1.5f, name + ": degrees between samples");
                Assert.Less(pos, 0.01f, name + ": metres between samples");
            }
        }

        [Test]
        public void WalkJitterKeepsTheStepsRhythmAndScalesTheRest()
        {
            var w = Walk();
            // At 0 every stride is the same (the strides' average); at 1 the recording itself.
            w.Sample(0.3f, out var r0, out var p0, 0f);
            for (int k = 1; k < w.Strides; k++)
            {
                w.Sample(k + 0.3f, out var rk, out var pk, 0f);
                Assert.AreEqual(r0.x, rk.x, 1e-4f); Assert.AreEqual(p0.y, pk.y, 1e-5f);
            }
            w.Sample(5f + 7f / w.SamplesPerStride, out var r1, out _, 1f);
            Assert.AreEqual(w.Raw(5, 7, 0), r1.x, 1e-4f);
            // Halfway between: half the difference.
            w.Sample(5f + 7f / w.SamplesPerStride, out var rh, out _, 0.5f);
            Assert.AreEqual(0.5f * (w.Raw(5, 7, 0) + w.Mean(7, 0)), rh.x, 1e-4f);
            // The shared rhythm still bobs a few centimetres (the steps are kept).
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < w.SamplesPerStride; i++) { lo = Mathf.Min(lo, w.Mean(i, 4)); hi = Mathf.Max(hi, w.Mean(i, 4)); }
            Assert.That(hi - lo, Is.InRange(0.005f, 0.06f));
        }
    }
}
