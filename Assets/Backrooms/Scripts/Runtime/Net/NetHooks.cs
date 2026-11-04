using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>
    /// Where the game tells the co-op layer that something changed in the world here (2026-11-04): a light switched, a
    /// door left, a breaker thrown, a button pressed, chalk drawn, a wall broken, a nail driven. Each system calls its hook
    /// only when <see cref="On"/> (in a session, and not while a change from the network is being applied, so nothing is
    /// sent back to where it came from). NetWorld fills them in; with no session they are never called.
    /// </summary>
    public static class NetHooks
    {
        /// <summary>A change from another player is being applied now.</summary>
        public static bool Applying;
        public static bool On => !Applying && NetSession.Active && NetWorld.Ready;

        public static Action<Vector3Int, int, bool, bool> Light;          // chunk, room, off, instant
        public static Action<Vector3Int, int, bool> Drain;                // chunk, room, draining
        public static Action<Vector3Int, bool> Breaker;                   // circuit, on
        public static Action<Interaction.Homes.Key> Home;
        public static Action<string, float> DoorRest;                     // a door left at an angle (its key)
        public static Action<string, bool> DoorBolt;
        public static Action<Interaction.IPokeTarget> Poke;               // a button, a switch pressed by hand
        public static Action<int, Vector3, Vector3, Vector3> Strip;       // chalk (0) or tape (1): a, b, normal
        public static Action<IReadOnlyList<Vector3>, IReadOnlyList<Vector3>> Tape;
        public static Action<IReadOnlyList<Vector3>, float, Color32> Scrape;
        public static Action<Destruction.WallKey, Vector3, Vector3, float, Destruction.Blow, int> Strike;
        public static Action<Vector3, Vector3, ulong> NailAdd;           // head, direction, prop (0: the building)
        public static Action<Vector3> NailRemove;
        public static Action<Interaction.MovableProp> PropWoke, PropRested;

        /// <summary>Runs a change that came from the network (its own hooks stay quiet).</summary>
        public static void Apply(Action a)
        {
            bool was = Applying;
            Applying = true;
            try { a(); }
            finally { Applying = was; }
        }
    }
}
