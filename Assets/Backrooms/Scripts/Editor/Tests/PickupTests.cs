using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Round 8 (2026-10-28): items are picked up by hand (PropGrabber.Pickup), never flown to it.</summary>
    public class PickupTests
    {
        // The body standing (the feet at 0, facing +z, the eye at the origin's 1.61 m): its right shoulder 1.43 m up, 0.15 m
        // behind the eye and 0.2 m to the side; the arm's reach 0.63 m. Bending at the hips moves the shoulder about as the
        // rig's does (0.47 m from the hips, and 0.1 m forward under the view at 60 degrees).
        const float Reach = 0.63f;

        static PropGrabber.ReachBody Body(float room)
        {
            System.Func<float, float, Vector2> shoulder = (h, s) => new Vector2(Mathf.Sin(h * Mathf.Deg2Rad) * 0.47f + 0.1f * Mathf.Clamp01(h / 60f), (1f - Mathf.Cos(h * Mathf.Deg2Rad)) * 0.47f);
            System.Func<float, float, Vector2> head = (h, s) => new Vector2(Mathf.Sin(h * Mathf.Deg2Rad) * 0.66f + 0.1f * Mathf.Clamp01(h / 60f), (1f - Mathf.Cos(h * Mathf.Deg2Rad)) * 0.66f);
            return new PropGrabber.ReachBody
            {
                shoulder = new Vector3(0.2f, 1.43f, -0.15f), eye = new Vector3(0f, 1.61f, 0f), heading = Vector3.forward, stepDir = Vector3.forward,
                room = room, feet = 0f, stoopMax = 0.85f, hingeMax = PropGrabber.MaxHinge, reach = Reach, hingeShoulder = shoulder, hingeHead = head,
            };
        }

        static bool Offered(Vector3 p, float room) => PropGrabber.PlanReach(p, Body(room), PropGrabber.PickReachShare, null, out _);

        [Test]
        public void AnItemOutOfTheBodysReachIsNotOffered()
        {
            // On the floor 1.5 m out (it flew to the hand from up to 2.1 m): the player walks closer.
            Assert.That(Offered(new Vector3(0f, 0.05f, 1.5f), PropGrabber.PickStep), Is.False, "floor 1.5 m out");
            // High over the head and out: no.
            Assert.That(Offered(new Vector3(0f, 2.3f, 0.5f), PropGrabber.PickStep), Is.False, "too high");
            // On a desk 1 m in from its edge, standing at it: no.
            Assert.That(Offered(new Vector3(0f, 0.82f, 1.3f), 0f), Is.False, "desk 1 m in");
            // Within reach, but the arm's way there blocked (a shelf board, a desk's edge): no.
            Assert.That(Offered(new Vector3(0f, 1.1f, 0.3f), 0f), Is.True, "within reach");
            Assert.That(PropGrabber.PlanReach(new Vector3(0f, 1.1f, 0.3f), Body(0f), 1f, (c, s) => false, out _), Is.False, "blocked");
            Assert.That(PropGrabber.PickStep, Is.LessThanOrEqualTo(0.7f), "one step");
        }

        [Test]
        public void StandingCloserNeverLosesAnItemOnADesk()
        {
            // Round 9: a can 0.2 m in from a desk's edge was refused standing at the desk (0.25-0.35 m off it) and taken from
            // 0.6 m. Now one 0.4 m in is offered from any stand 0.3-0.8 m off the edge (the room to step is what is left).
            foreach (float stand in new[] { 0.3f, 0.35f, 0.6f, 0.8f })
                foreach (float inFrom in new[] { 0.2f, 0.4f })
                {
                    var can = new Vector3(0f, 0.82f, stand + inFrom);
                    Assert.That(Offered(can, stand - 0.3f), Is.True, $"stand {stand} m, can {inFrom} m in");
                }
        }

        [Test]
        public void WaistHighIsABendAtTheHipsTheFloorASquat()
        {
            // A can on a desk from 0.3 m: the body bends forward from the hips, the knees nearly straight, the elbow bent.
            Assert.That(PropGrabber.PlanReach(new Vector3(0f, 0.82f, 0.6f), Body(0f), PropGrabber.PickPlanShare, null, out var desk), Is.True);
            Assert.That(desk.hinge, Is.GreaterThanOrEqualTo(30f), "bent at the hips");
            Assert.That(desk.stoop, Is.LessThanOrEqualTo(PropGrabber.HighStoop + 1e-4f), "knees nearly straight");
            Assert.That(desk.share, Is.LessThanOrEqualTo(PropGrabber.PickPlanShare), "the arm bent");
            // A can on the floor 0.9 m ahead: one step and a squat.
            Assert.That(Offered(new Vector3(0f, 0.1f, 0.9f), PropGrabber.PickStep), Is.True, "floor 0.9 m ahead");
            Assert.That(PropGrabber.PlanReach(new Vector3(0f, 0.1f, 0.6f), Body(PropGrabber.PickStep), PropGrabber.PickPlanShare, null, out var floor), Is.True);
            Assert.That(floor.stoop, Is.GreaterThan(0.4f), "a squat for the floor");
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
