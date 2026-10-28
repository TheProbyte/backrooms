using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Round 8 (2026-10-28): items are picked up by hand (PropGrabber.Pickup), never flown to it.</summary>
    public class PickupTests
    {
        // The body standing: its shoulder 1.4 m up, down to 0.65 m stooping; the arm's reach 0.63 m.
        static readonly Vector3 Shoulder = new Vector3(0f, 1.4f, 0f);
        const float Fall = 0.75f, Reach = 0.63f;

        [Test]
        public void AnItemOutOfTheBodysReachIsNotOffered()
        {
            float r = Reach * PropGrabber.PickReachShare;
            // On a desk just past the arm: a short step does it, but not against the desk (no room to step).
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 0.8f, 0.85f), Shoulder, Fall, r, PropGrabber.PickStep, null), Is.True, "desk, a step in");
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 0.8f, 0.85f), Shoulder, Fall, r, 0f, null), Is.False, "desk, no room to step");
            // On the floor by the feet: stooping.
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 0.08f, 0.15f), Shoulder, Fall, r, 0f, null), Is.True, "floor by the feet");
            // On the floor 1.5 m out (it flew to the hand from up to 2.1 m): the player walks closer.
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 0.05f, 1.5f), Shoulder, Fall, r, PropGrabber.PickStep, null), Is.False, "floor 1.5 m out");
            // High over the head and out: no.
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 2.3f, 0.5f), Shoulder, Fall, r, PropGrabber.PickStep, null), Is.False, "too high");
            // Within reach, but the arm's way there blocked (a shelf board, a desk's edge): no.
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 1.1f, 0.4f), Shoulder, Fall, r, 0f, null), Is.True, "within reach");
            Assert.That(PropGrabber.ReachableWith(new Vector3(0f, 1.1f, 0.4f), Shoulder, Fall, r, 0f, (a, b) => false), Is.False, "blocked");
            Assert.That(PropGrabber.PickStep, Is.LessThanOrEqualTo(0.6f), "a short step");
        }

        [Test]
        public void AnItemDoesNotMoveBeforeThePalmIsOnIt()
        {
            // The hand coming in: never taken short of its grip, nor while its target is held short of the grip (the arm
            // going out bent), nor with the arm not all the way out.
            float nearT = 0f;
            for (float d = 0.4f; d > 0.016f; d -= 0.01f)
                Assert.That(PropGrabber.PalmTakes(d, false, true, ref nearT, 0.02f), Is.False, $"palm {d * 1000f:0} mm off");
            Assert.That(PropGrabber.PalmTakes(0f, true, true, ref nearT, 0.02f), Is.False, "the hand's target held short of it");
            Assert.That(PropGrabber.PalmTakes(0f, false, false, ref nearT, 0.02f), Is.False, "the arm not all the way out");
            Assert.That(PropGrabber.PalmTakes(0.01f, false, true, ref nearT, 0.02f), Is.True, "the palm on it");
            // Within 1.5 cm and no nearer: taken after a moment, not before.
            nearT = 0f;
            Assert.That(PropGrabber.PalmTakes(0.014f, false, true, ref nearT, 0.05f), Is.False);
            Assert.That(PropGrabber.PalmTakes(0.014f, false, true, ref nearT, 0.05f), Is.False);
            Assert.That(PropGrabber.PalmTakes(0.014f, false, true, ref nearT, 0.06f), Is.True);
            Assert.That(PropGrabber.PalmOnGrip, Is.LessThanOrEqualTo(PropGrabber.PalmNearGrip));
            Assert.That(PropGrabber.PalmNearGrip, Is.LessThanOrEqualTo(0.015f));
        }
    }
}
