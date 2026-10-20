using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Describes one modular room so later procedural generation can place and connect rooms.
    /// Interior dimensions are multiples of <see cref="GridUnit"/> (one 2' ceiling tile = 0.6 m);
    /// walls are built from 1.2 m segments so any segment can later be swapped for a doorway.
    /// The room origin sits at floor level in the centre of the interior.
    /// </summary>
    public class RoomModule : MonoBehaviour
    {
        public const float GridUnit = 0.6f;
        public const float WallSegmentWidth = 1.2f;

        [Tooltip("Interior width (X) and depth (Z) in metres.")]
        public Vector2 interiorSize = new Vector2(7.2f, 6.0f);
        [Tooltip("Floor to ceiling-tile height in metres.")]
        public float ceilingHeight = 2.7f;
        [Tooltip("Wall thickness used for collision, in metres.")]
        public float wallThickness = 0.15f;

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, ceilingHeight * 0.5f, 0f),
                new Vector3(interiorSize.x, ceilingHeight, interiorSize.y));
        }
    }
}
