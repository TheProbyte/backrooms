namespace Backrooms.Interaction
{
    /// <summary>
    /// A held item's hand shape beyond one curl for all its fingers (2026-10-28): how far each finger closes and the
    /// thumb's flex and opposition (its base turned across the palm toward the fingers), for pinches and pencil grips. The
    /// fingers still stop where they touch the item (FirstPersonBody.ContactCurl), so these are how far they would go.
    /// Items without it close every finger by WorldItem.GripCurl, the thumb opposed in proportion.
    /// </summary>
    public interface IHandGrip
    {
        /// <summary>How far a finger closes, 0 straight .. 1 a fist (0 index, 1 middle, 2 ring, 3 pinky).</summary>
        float FingerCurl(int finger);
        /// <summary>How far the thumb's joints flex, 0..1.</summary>
        float ThumbCurl { get; }
        /// <summary>How far the thumb turns across the palm toward the fingers, 0..1.</summary>
        float ThumbOpposition { get; }
    }
}

namespace Backrooms.Interaction
{
    /// <summary>
    /// A held item that sets some of its fingers' joints itself (round 7): a pencil's tripod, a plug's pad pinch, fingers
    /// hooked through a tape roll's core. Closing until the first touch could not give those shapes (a stick under the
    /// index and middle pads left both fingers straight; the pinch was a straight-fingered claw). The item is placed so
    /// these fingers' pads lie on it; the others close on it as usual.
    /// </summary>
    public interface IHandPose
    {
        /// <summary>
        /// True and the finger's joint bends (degrees, knuckle out; for the thumb, angles[3] is its turn across the palm in
        /// degrees) for a finger (0 index, 1 middle, 2 ring, 3 little, 4 thumb); false: it closes on the item as usual.
        /// </summary>
        bool FingerPose(int finger, float[] angles);
    }

    /// <summary>
    /// An item whose grip is as good turned about an axis (round 10): a can's side, a lamp's rod, a hammer's handle, a coil's
    /// rim. Picking it up, the hand may take it from any side round that axis: from above when it lies on its side, from the
    /// near side when its grip faces away (a coil was reached for across itself, a hammer lying down had its grip under it).
    /// The item turns back to its carry grip in the hand as it comes up.
    /// </summary>
    public interface IGripRound
    {
        /// <summary>The axis in the item's own space: a point on it and its direction.</summary>
        void GripAxis(out UnityEngine.Vector3 point, out UnityEngine.Vector3 axis);
    }
}
