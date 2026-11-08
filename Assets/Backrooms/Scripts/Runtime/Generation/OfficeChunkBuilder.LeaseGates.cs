using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The ways from the contract floor's core into its wings (LeasePlan): every opening across the core's edge into a wing
    /// gets a panel of plywood hoarding (LeaseHoarding, NOT LEASED on every other one), solid, the room's height. They come
    /// down when the wing is leased (Interaction.LeaseGate). The plan underneath does not change, so leasing a wing moves
    /// nothing.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        void InstantiateLeaseGates(Transform root)
        {
            if (_L.storey != 0 || LeasePlan.Current < 0) return;
            var district = WorldGenSettings.DistrictOf(_L.coord);
            if (LeasePlan.FloorOf(district) < 0 || LeasePlan.WingOf(district) >= 0) return;      // (the core's side only)
            int n = _L.N;
            for (int side = 0; side < 4; side++)
            {
                var step = side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up;
                var other = WorldGenSettings.DistrictOf(_L.coord + step);
                if (other == district) continue;
                int wing = LeasePlan.WingOf(other);
                if (wing < 0) continue;
                for (int i = 0; i < n; i++)
                {
                    int x = side == 0 ? 0 : side == 1 ? n - 1 : i, z = side == 2 ? 0 : side == 3 ? n - 1 : i;
                    if (_L.roomOf[x, z] < 0 || _L.EdgeOf(x, z, side) == EdgeKind.Wall) continue;
                    var room = _L.RoomAt(x, z);
                    if (room.noFloor) continue;
                    // On the wall line, a hair into this cell, facing into it.
                    var into = new Vector3(-step.x, 0f, -step.y);
                    var line = new Vector3((x + 0.5f) * C + step.x * C * 0.5f, _L.FloorAt(x, z), (z + 0.5f) * C + step.y * C * 0.5f);
                    float h = Mathf.Max(room.ceiling, 2.4f);
                    var go = new GameObject("LeaseGate");
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = line + into * (T * 0.5f + 0.012f);
                    go.transform.localRotation = Quaternion.LookRotation(into, Vector3.up);
                    string model = ((x + z + _L.coord.x * 7 + _L.coord.y * 13) & 1) == 0 ? "LeaseHoarding" : "LeaseHoardingPlain";
                    FittingModels.Spawn(model, go.transform, LayerBit(room.color), Vector3.zero, Quaternion.identity, new Vector3(1f, h / 2.7f, 1f));
                    var col = go.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, h * 0.5f, 0.02f); col.size = new Vector3(C, h, 0.1f);
                    go.AddComponent<Interaction.LeaseGate>().wing = wing;
                }
            }
        }
    }
}
