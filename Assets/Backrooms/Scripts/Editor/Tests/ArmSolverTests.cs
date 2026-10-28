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
    }
}
