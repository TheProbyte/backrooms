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
    }
}
