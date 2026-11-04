using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A small open box of 2-inch wire nails (about a pound: <see cref="Full"/> of them). In a pocket, it is what the
    /// <see cref="Hammer"/> drives in (one a blow; the claw puts a pulled one back). Picked up, carried on the palm, dropped,
    /// thrown, set down and put in a pocket. Model: Resources/Items/Nails.fbx (Tools/Blender/build_items2.py), origin at the
    /// bottom centre, 11 x 5 x 7 cm, the open flap at the back (-Z).
    /// </summary>
    public class Nails : WorldItem
    {
        static readonly Vector3 Size = new Vector3(0.11f, 0.05f, 0.07f);

        // (At -0.27 it was carried below the picture: only the fingertips showed.)
        public override Vector3 HoldOffset => new Vector3(0.15f, -0.18f, 0.4f);
        // On the palm, under the box: the back of the hand down, the fingers along it, away from you.
        public override Vector3 GripLocal => Vector3.zero;
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(Vector3.forward, Vector3.down);
        public override float GripCurl => 0.3f;
        public override Quaternion[] RestTurns => s_rests;
        static readonly Quaternion[] s_rests = { Quaternion.identity, Quaternion.Euler(0f, 90f, 0f) };
        public override bool Pocketable => true;
        public override bool UsesAim => false;
        public const int Full = 80;
        [Tooltip("Nails left in the box.")] public int count = Full;
        public override string Status => count <= 0 ? "box of nails: empty" : $"box of nails: {count} (in a pocket, the hammer drives them)";
        public override float Amount { get => count / (float)Full; set => count = Mathf.Clamp(Mathf.RoundToInt(value * Full), 0, Full); }

        public static Nails Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("Nails", position, rotation, 0.45f, "Nails", Size, "NailBoxCardboard");
            var n = go.AddComponent<Nails>();
            n.itemId = "nails"; n.displayName = "Box of nails";
            Generation.PoolLitSwitch.Attach(go);
            return n;
        }
    }
}
