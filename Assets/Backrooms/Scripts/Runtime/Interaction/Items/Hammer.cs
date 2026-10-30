using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A claw hammer (16 oz, 33 cm, hickory handle). No use yet (it is meant to nail boards and pallets in place, with
    /// <see cref="Nails"/>): it is picked up, carried head up at the lower right, dropped, thrown, set down and put in a
    /// pocket. Model: Resources/Items/Hammer.fbx (Tools/Blender/build_items2.py), origin at the handle's butt, the handle up
    /// +Y, the striking face toward +Z and the claw toward -Z.
    /// </summary>
    public class Hammer : WorldItem, IGripRound
    {
        /// <summary>The head's axis, up from the butt (m).</summary>
        public const float HeadY = 0.315f;
        /// <summary>Where the hand takes the handle, up from the butt (m), and the handle's half thickness there across the
        /// head's line (x) and along it (z).</summary>
        const float GripY = 0.09f, HalfX = 0.0111f, HalfZ = 0.0150f;

        public override Vector3 HoldOffset => new Vector3(0.2f, -0.33f, 0.4f);
        // Palm on the handle's side, the back of the hand outward (+x), the fingers round toward the striking face (+z):
        // as the sledgehammer is held (Sledgehammer.GripTurn).
        public override Vector3 GripLocal => new Vector3(HalfX * 0.95f, GripY, 0f);
        static readonly Quaternion GripTurn = Quaternion.LookRotation(Vector3.forward, Vector3.right) * Quaternion.AngleAxis(18f, Vector3.down);
        public override Quaternion GripRotationLocal => GripTurn;
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = Vector3.zero; axis = Vector3.up; }
        public override float GripCurl => 0.95f;
        /// <summary>Set down by hand: lying on one side of its head, then the other.</summary>
        public override Quaternion[] RestTurns => s_rests;
        static readonly Quaternion[] s_rests = { Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(0f, 0f, -90f) };
        public override bool Pocketable => true;
        public override string Status => "hammer";

        /// <summary>The handle (an oval, narrowing up to the head) and the head (face to claw).</summary>
        public override float ContactDistance(Vector3 p)
        {
            // The oval as an ellipse scaled to a circle across the head's line.
            float t = Mathf.Clamp01(p.y / 0.3f);
            float rz = Mathf.Lerp(HalfZ, 0.0110f, t), rx = Mathf.Lerp(HalfX, 0.0085f, t);
            float handle = Mathf.Max(new Vector2(p.x * rz / rx, p.z).magnitude - rz, Mathf.Max(-p.y, p.y - HeadY));
            float head = BoxDistance(p, new Vector3(0f, HeadY, -0.011f), new Vector3(0.015f, 0.021f, 0.076f));
            return Mathf.Min(handle, head);
        }

        public static Hammer Create(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Hammer");
            go.transform.SetPositionAndRotation(position, rotation);
            ItemModels.Attach(go.transform, "Hammer", new Vector3(0.03f, 0.33f, 0.03f), "HammerWood");
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = 0.014f; cap.height = HeadY; cap.center = new Vector3(0f, HeadY * 0.5f, 0f);
            var head = go.AddComponent<BoxCollider>();
            head.center = new Vector3(0f, HeadY, -0.011f); head.size = new Vector3(0.03f, 0.042f, 0.152f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.6f; rb.linearDamping = 0.1f; rb.angularDamping = 0.5f;
            rb.centerOfMass = new Vector3(0f, 0.25f, 0f);                // the head is most of its weight
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var h = go.AddComponent<Hammer>();
            h.itemId = "hammer"; h.displayName = "Hammer";
            Generation.PoolLitSwitch.Attach(go);
            return h;
        }
    }
}
