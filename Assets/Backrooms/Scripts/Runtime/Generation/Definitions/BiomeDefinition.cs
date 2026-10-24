using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A biome: every number the room generator uses in it. Zones list biomes along their density field.
    /// Create with Assets > Create > Backrooms > World Gen > Biome, then add it to a zone.
    /// </summary>
    [CreateAssetMenu(menuName = "Backrooms/World Gen/Biome", fileName = "Biome")]
    public class BiomeDefinition : ScriptableObject
    {
        [Tooltip("The name is used by zones (edge biome) and landmarks (allowed biomes).")]
        public BiomeRules rules = new BiomeRules();

        public BiomeRules ToRules() => rules.Clone();
    }
}
