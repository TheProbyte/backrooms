using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A panel of hoarding across a way into a wing of the contract floor not leased yet (OfficeChunkBuilder.LeaseGates):
    /// there, solid, until the wing is leased (Improvements), then gone.
    /// </summary>
    public class LeaseGate : MonoBehaviour
    {
        public static readonly List<LeaseGate> All = new List<LeaseGate>();
        public int wing;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void Start() => Apply();
        void OnDestroy() { All.Remove(this); }

        void Apply()
        {
            bool up = !LeasePlan.WingLeased(wing);
            foreach (Transform t in transform) t.gameObject.SetActive(up);
            var c = GetComponent<Collider>();
            if (c) c.enabled = up;
        }

        /// <summary>After a wing is leased: the hoarding in front of it comes down.</summary>
        public static void Refresh() { for (int i = All.Count - 1; i >= 0; i--) if (All[i]) All[i].Apply(); }
    }
}
