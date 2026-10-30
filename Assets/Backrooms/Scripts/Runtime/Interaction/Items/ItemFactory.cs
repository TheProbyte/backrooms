using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>Makes an item from its id (WorldItem.itemId): for saves and the special rooms' loot.</summary>
    public static class ItemFactory
    {
        public static readonly string[] Ids = { "spraycan", "chalk", "tape", "desklamp", "floorlamp", "extensioncord", "rubberchicken", "sledgehammer", "hammer", "nails" };

        public static WorldItem Spawn(string id, Vector3 position, Quaternion rotation)
        {
            switch (id)
            {
                case "spraycan": return SprayCan.Create(position, rotation);
                case "chalk": return Chalk.Create(position, rotation);
                case "tape": return TapeRoll.Create(position, rotation);
                case "desklamp": return DeskLamp.Create(position, rotation);
                case "floorlamp": return FloorLamp.Create(position, rotation);
                case "extensioncord": return CordBox.Create(position, rotation);
                case "rubberchicken": return RubberChicken.Create(position, rotation);
                case "sledgehammer": return Sledgehammer.Create(position, rotation);
                case "hammer": return Hammer.Create(position, rotation);
                case "nails": return Nails.Create(position, rotation);
            }
            return null;
        }
    }
}
