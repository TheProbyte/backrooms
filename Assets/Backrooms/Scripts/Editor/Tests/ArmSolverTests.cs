using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The arm IK and the MakeHuman rig agree on what an unturned arm is (FirstPersonBody.RestPoseError).</summary>
    public class ArmSolverTests
    {
        [Test]
        public void TheRestPosePutThroughTheSolverStaysAtRest()
        {
            var prefab = Resources.Load<GameObject>("Player/MPFB/PlayerMPFB");
            Assert.IsNotNull(prefab, "the MakeHuman body");
            var go = Object.Instantiate(prefab);
            try
            {
                // Solved to its own rest wrist and hand angle, every arm bone ends up where it was: the upper arm's roll on
                // the elbow's hinge (it was 60-70 degrees off it), the forearm's and the hand's as at rest.
                float right = FirstPersonBody.RestPoseError(go.transform, "r");
                float left = FirstPersonBody.RestPoseError(go.transform, "l");
                Assert.That(right, Is.LessThan(1.5f), "right arm");
                Assert.That(left, Is.LessThan(1.5f), "left arm");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Round 6: chalk is held as a pencil is, running out past the fingertips at 30-45 degrees to the fingers
        /// (it lay along them, under two fingertips).</summary>
        [Test]
        public void ChalkIsHeldAsAPencilIs()
        {
            var go = new GameObject("ChalkGripTest");
            try
            {
                var c = go.AddComponent<Chalk>();
                Vector3 fingers = c.GripRotationLocal * Vector3.forward;
                Assert.That(Vector3.Angle(fingers, Vector3.forward), Is.InRange(30f, 45f));
                Vector3 tip = new Vector3(0f, Chalk.Radius, Chalk.Length);
                Assert.Greater(Vector3.Dot(tip - c.GripLocal, fingers), 0.06f, "the tip well out past the palm");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Round 6: a tape roll is held with the palm off its side and the fingers through the core (the palm was
        /// 1.3 cm from the side and 5.5 mm into the roll).</summary>
        [Test]
        public void TapeRollsPalmIsOffItsSideAndTheFingersGoThroughTheCore()
        {
            var go = new GameObject("TapeGripTest");
            try
            {
                var t = go.AddComponent<TapeRoll>();
                Assert.Greater(t.ContactDistance(t.GripLocal), 0.025f, "palm clear of the roll");
                Vector3 fingers = t.GripRotationLocal * Vector3.forward;
                Vector3 inCore = t.GripLocal + fingers * (0.056f - 0.0f);    // as far along as the roll's middle
                Vector2 radial = new Vector2(inCore.y - 0.0525f, inCore.z);
                Assert.Less(radial.magnitude, 0.038f - 0.02f, "the fingers' line well inside the core");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TouchIsKeptWhereItIsFound()
        {
            // One reach for finding a touch on the world and for keeping it: whatever is found (never past the reach) is
            // kept, with a margin for the body's sway; well past it, let go. (Found at 0.543 m and dropped past 0.532 m,
            // a wall lean flickered on and off eight times a second.)
            float reach = FirstPersonBody.TouchReachOf(0.29f, 0.27f);
            Assert.That(reach, Is.LessThan(0.29f + 0.27f));
            Assert.That(FirstPersonBody.TouchKept(reach, reach), Is.True, "found at the reach");
            Assert.That(FirstPersonBody.TouchKept(reach + 0.03f, reach), Is.True, "swayed a little further");
            Assert.That(FirstPersonBody.TouchKept(reach + 0.06f, reach), Is.False, "well past it");
        }

        [Test]
        public void TheHammersHandleIsWhereTheModelIs()
        {
            // The fingers close on the handle the model draws: oval (13.5 by 17.5 mm), swelling to a knob at the butt
            // (a round 13.5 mm handle from 2 cm up let the fingers 4 mm into its sides and the little finger off the knob).
            var go = new GameObject("Sledge");
            try
            {
                var s = go.AddComponent<Sledgehammer>();
                float k = SledgehammerModel.HandleScale(0.27f);
                Assert.That(SledgehammerModel.HandleScale(0.015f), Is.GreaterThan(1.15f), "the knob");
                Assert.That(k, Is.InRange(0.88f, 0.96f), "the waist");
                Assert.That(Mathf.Abs(s.ContactDistance(new Vector3(SledgehammerModel.HandleA * k, 0.27f, 0f))), Is.LessThan(0.0005f), "across");
                Assert.That(Mathf.Abs(s.ContactDistance(new Vector3(0f, 0.27f, SledgehammerModel.HandleB * k))), Is.LessThan(0.0005f), "in the swing");
                Assert.That(s.ContactDistance(new Vector3(0f, 0.27f, 0f)), Is.LessThan(-0.01f), "inside");
                Assert.That(s.ContactDistance(new Vector3(0.03f, 0.27f, 0f)), Is.InRange(0.016f, 0.019f), "off it");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void APlugsTargetStaysWithinABentArmsReach()
        {
            // The hand takes a plug toward a socket out of reach only as far as a bent arm reaches; the body steps in for the
            // rest (sent straight there, the arm locked straight for a second, the palm 47 cm behind its target). Held back,
            // it gives up within about a second.
            Vector3 shoulder = new Vector3(0f, 1.4f, 0f), socket = new Vector3(0f, 0.3f, 1f), palmOff = new Vector3(0f, 0.05f, -0.05f);
            const float reach = 0.63f;
            Vector3 p = Plug.WithinComfort(socket, palmOff, shoulder, reach, out bool clamped);
            Assert.That(clamped, Is.True);
            Assert.That((p + palmOff - shoulder).magnitude, Is.EqualTo(reach * Plug.ComfortReach).Within(1e-4f));
            Assert.That(Vector3.Dot(p - shoulder, (socket - shoulder).normalized), Is.GreaterThan(0.3f), "on the way to it");
            Vector3 near = new Vector3(0f, 1.1f, 0.3f);
            Assert.That(Plug.WithinComfort(near, palmOff, shoulder, reach, out clamped), Is.EqualTo(near));
            Assert.That(clamped, Is.False);
            Assert.That(Plug.GiveUpAfter, Is.LessThanOrEqualTo(1.2f));
        }

        [Test]
        public void ALampIsSetDownBesideTheFeetOffTheWayToTheOutlet()
        {
            // Plugging in a lamp held in the hand, it is set down beside the feet, off the way to the outlet (set down under
            // the outlet, the body stepping in climbed onto it and the plug stopped 15 cm short), never into a wall.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var player = new GameObject("Player");
            try
            {
                Vector3 o = new Vector3(3000f, 900f, 3000f);                                         // away from anything else
                floor.transform.localScale = new Vector3(10f, 0.2f, 10f); floor.transform.position = o + new Vector3(0f, -0.1f, 0f);
                wall.transform.localScale = new Vector3(0.2f, 2f, 4f); wall.transform.position = o + new Vector3(0.5f, 1f, 0f);   // on the hand's side
                player.transform.position = o;
                var grab = player.AddComponent<PropGrabber>();
                Physics.SyncTransforms();
                Vector3 outlet = o + new Vector3(0f, 0.3f, 1.2f);
                Assert.That(grab.SpotBeside(outlet, new Vector3(0.09f, 0.2f, 0.09f), null, out var at, out _), Is.True);
                at -= o;
                Assert.That(Mathf.Abs(at.x), Is.GreaterThan(0.3f), "off the way to the outlet");
                Assert.That(at.x, Is.LessThan(0f), "the other side, not in the wall");
                Assert.That(at.y, Is.EqualTo(0f).Within(0.01f), "on the floor");
            }
            finally { Object.DestroyImmediate(floor); Object.DestroyImmediate(wall); Object.DestroyImmediate(player); }
        }
    }
}
