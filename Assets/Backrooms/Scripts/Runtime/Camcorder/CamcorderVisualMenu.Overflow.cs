using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > PLACES > OVERFLOW (2026-11-08, the owner: "a way to teleport to the hub or to the separate seams quickly"):
    /// jumps to the hub's rooms, the lift on your floor, your starter shelf, each client's seam (press again for the next
    /// one of that client) and the inside of each client's place.
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        readonly Dictionary<int, int> _seamCycle = new Dictionary<int, int>();

        void BuildOverflowLocations()
        {
            Section("OVERFLOW", "Quick travel for the working day: the hub, your floor, and the clients' seams and places.", true);
            var zone = _list;

            _list = Group("THE HUB", "The company's office: where every day starts.");
            var h = BtnRow(_list);
            Button(h, "LOBBY (THE BOARD)", () => GoHub(11.0f, 11.0f, 11.0f, 14.4f, "the hub's lobby"));
            Button(h, "SUPPLY COUNTER", () => GoHub(6.9f, 8.8f, 6.9f, 7.2f, "the supply counter"));
            var h2 = BtnRow(_list);
            Button(h2, "TRAINING ROOM", () => GoHub(5.6f, 17.4f, 5.6f, 21.6f, "the training room"));
            Button(h2, "HUB LIFT", () => GoHub(11.4f, 4.6f, 11.4f, 2.0f, "the lift hall"));

            _list = zone;
            _list = Group("YOUR FLOOR", "The floor the lift goes down to. You wait in place until it has loaded.");
            var f = BtnRow(_list);
            Button(f, "BY THE LIFT", GoFloorLift);
            Button(f, "STARTER SHELF", GoStarterShelf);

            _list = zone;
            _list = Group("SEAMS", "In front of a client's seam on your floor (press again for their next one). INSIDE takes you into their place, by the pickup pallet.");
            for (int k = 0; k < LeaseSeams.Kinds.Length; k++)
            {
                int kind = k;
                var row = BtnRow(_list);
                Button(row, LeaseSeams.Kinds[k] + " SEAM", () => GoSeam(kind));
                Button(row, "INSIDE " + LeaseSeams.Kinds[k], () => GoInside(kind));
            }
            _list = zone;
        }

        void GoHub(float x, float z, float lookX, float lookZ, string what)
            => Go(HubPlan.World(x, z) + Vector3.up * 0.05f, HubPlan.World(lookX, lookZ), what);

        void GoFloorLift()
        {
            if (LeasePlan.Current < 0) { Say("This world has no floor yet."); return; }
            var cab = HubPlan.WorldCab;
            Go(cab.position + cab.rotation * new Vector3(0f, 0.05f, 2.4f), cab.position + cab.rotation * new Vector3(0f, 0f, 8f), "the lift on your floor");
        }

        void GoStarterShelf()
        {
            var d = Saves.GameSession.Data;
            if (d == null) { Say("No world loaded."); return; }
            foreach (var id in d.shelves)
            {
                if (!Interaction.Shelves.Pose(id, out var at, out var rot, out int v) || Interaction.ShelfTypes.IsBroken(v)) continue;
                Go(at + rot * new Vector3(0.4f, 0.05f, 2.2f), at + Vector3.up, "your first shelf");
                return;
            }
            Say("No standing shelf on your floor.");
        }

        void GoSeam(int kind)
        {
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (!ws || ws.World == null || LeasePlan.Current < 0) { Say("This world has no floor yet."); return; }
            var seams = new List<LeaseSeams.SeamInfo>();
            LeaseSeams.Survey(ws.World, LeasePlan.Current, seams, null);
            var mine = seams.FindAll(s => s.kind == kind);
            if (mine.Count == 0) { Say($"No {LeaseSeams.Kinds[kind]} seam on the leased part of your floor."); return; }
            _seamCycle.TryGetValue(kind, out int i);
            i %= mine.Count;
            _seamCycle[kind] = i + 1;
            var seam = mine[i];
            var out_ = Quaternion.Euler(0f, seam.yaw, 0f) * Vector3.forward;          // into the room, away from the wall
            Go(seam.position + out_ * 2.2f + Vector3.up * 0.05f, seam.position + Vector3.up, $"the {LeaseSeams.Kinds[kind]} seam {i + 1} of {mine.Count}");
        }

        void GoInside(int kind)
        {
            SeamPlan.Tear(kind, out var tear, out _);
            var pallet = SeamPlan.Stack(kind, 0);
            var dir = pallet - tear; dir.y = 0f;
            var stand = tear + dir.normalized * Mathf.Min(1.6f, dir.magnitude * 0.5f);
            stand.y = pallet.y + 0.05f;
            Go(stand, pallet, $"inside the {LeaseSeams.Kinds[kind]}");
        }

        void Say(string text) { if (_locReadout != null) _locReadout.text = text; }
    }
}
