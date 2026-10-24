using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>Debug: turns the player at a steady rate (degrees per second) so motion blur can be checked in screenshots.</summary>
    public class SpinProbe : MonoBehaviour
    {
        public float yawPerSecond = 120f, pitchPerSecond;
        CamcorderPlayerController _pl;

        void Update()
        {
            if (!_pl) _pl = FindAnyObjectByType<CamcorderPlayerController>();
            if (_pl) _pl.AddLook(yawPerSecond * Time.deltaTime, pitchPerSecond * Time.deltaTime);
        }
    }
}
