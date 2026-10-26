using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>The lit lenses of one room of a chunk (one mesh), with the materials for on and off.</summary>
    public class RoomLens : MonoBehaviour
    {
        public int room;
        public Material on, off;
    }
}
