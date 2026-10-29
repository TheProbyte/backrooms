using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>The wound-up cord behind a desk lamp: clicking it with an empty hand lets the cord out and puts its plug in the hand.</summary>
    public class CordBundleHandle : MonoBehaviour, IHandUse
    {
        public PowerUser lamp;          // a desk lamp or a floor lamp

        public bool HandUse(PropGrabber hand)
        {
            if (!lamp || lamp.Held) return false;
            lamp.Unbundle();
            var plug = lamp.Plug;
            if (plug) hand.PickUp(plug);
            return true;
        }
    }
}
