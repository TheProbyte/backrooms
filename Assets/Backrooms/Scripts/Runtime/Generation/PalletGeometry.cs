using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A wooden pallet, 1.2 x 1.0 m and 14.5 cm high: three runners along its depth, three bottom boards, seven top deck
    /// boards. Built round its foot (the middle of its underside), unrotated: x along its length, z across. Used at spawn by
    /// the movable pallet (Interaction.MovableProp).
    /// </summary>
    public static class PalletGeometry
    {
        public const float Length = 1.2f, Width = 1.0f, Height = 0.145f;

        public static void Build(MeshWriter wood, Vector3 foot)
        {
            void Plank(Vector3 centre, Vector3 size) => wood.Box(foot + centre, size, Quaternion.identity, 63, MeshWriter.MetreUV);
            for (int i = -1; i <= 1; i++) Plank(new Vector3(i * 0.5f, 0.0525f, 0f), new Vector3(0.095f, 0.09f, Width - 0.02f));     // runners
            for (int i = -1; i <= 1; i++) Plank(new Vector3(0f, 0.01f, i * 0.44f), new Vector3(Length, 0.02f, 0.1f));             // bottom boards
            for (int i = 0; i < 7; i++)
                Plank(new Vector3(0f, Height - 0.011f, -Width * 0.5f + 0.05f + i * ((Width - 0.1f) / 6f)), new Vector3(Length, 0.022f, 0.1f));   // top deck
        }
    }
}
