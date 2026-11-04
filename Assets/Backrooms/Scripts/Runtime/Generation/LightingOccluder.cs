using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Existing furniture collider boxes used by the bounded runtime contact/bounce approximation.</summary>
    public sealed class LightingOccluder : MonoBehaviour
    {
        internal static readonly HashSet<LightingOccluder> All = new HashSet<LightingOccluder>();
        internal BoxCollider[] Boxes;
        void OnEnable() { if (Boxes == null) Boxes = GetComponents<BoxCollider>(); All.Add(this); }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() { All.Clear(); }
    }
}
