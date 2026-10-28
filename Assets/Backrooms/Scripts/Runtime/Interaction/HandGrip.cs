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
