using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Atmosphere
{
    /// <summary>
    /// One button in the enemies menu (<see cref="EnemiesMenu"/>). A <c>null</c> run makes it a section header.
    /// </summary>
    public struct DevAction
    {
        public string label, tip;
        public System.Action run;
        public DevAction(string label, System.Action run, string tip = null) { this.label = label; this.run = run; this.tip = tip; }
        public static DevAction Header(string label) => new DevAction(label, null);
    }

    /// <summary>
    /// An enemy the developer menu can test (owner, 2026-10-31: "a way to test him or future enemies instead of waiting
    /// around"). An enemy implements this and calls <see cref="EnemyDebug.Register"/>; the menu lists it with its status and
    /// its buttons, and the overlay marks where it is.
    /// </summary>
    public interface IDevEnemy
    {
        string DevName { get; }
        /// <summary>One line: what it is doing now.</summary>
        string DevStatus { get; }
        /// <summary>In the level now (the overlay marks it)?</summary>
        bool DevPresent { get; }
        /// <summary>Where it really is (feet).</summary>
        Vector3 DevPosition { get; }
        /// <summary>Where you can see it, if anywhere (its shadow on the wall, say); null when nothing of it shows.</summary>
        Vector3? DevShownAt { get; }
        /// <summary>Its buttons, in order (headers between).</summary>
        void DevActions(List<DevAction> into);
    }

    /// <summary>
    /// The enemies the developer menu knows, and its switches (in the spirit of Unreal's Gameplay Debugger and Valve's
    /// notarget / ai_disable: see what an AI is doing and push it into a state, without waiting for the game to get there).
    /// Not saved; reset each play.
    /// </summary>
    public static class EnemyDebug
    {
        static readonly List<IDevEnemy> s_all = new List<IDevEnemy>();
        public static IReadOnlyList<IDevEnemy> All => s_all;

        /// <summary>The tension director leaves the enemies alone: it does not send them, make them bold or call them off.
        /// Turned on by any button that brings an enemy (else in a lull the director would send it straight back).</summary>
        public static bool Hold;
        /// <summary>An enemy that reaches you does not catch you (it backs off instead).</summary>
        public static bool NoCatch;
        /// <summary>Enemies stand still and their anger stays where it is (to look at them at a stage).</summary>
        public static bool Freeze;
        /// <summary>Their status on screen and a marker on each, with the menu closed too.</summary>
        public static bool Overlay;
        /// <summary>The last thing that happened, for the menu and the overlay.</summary>
        public static string Note = "";
        public static float NoteTime;

        public static void Register(IDevEnemy e) { if (e != null && !s_all.Contains(e)) s_all.Add(e); }
        public static void Unregister(IDevEnemy e) { s_all.Remove(e); }
        public static void Say(string note) { Note = note; NoteTime = Time.unscaledTime; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_all.Clear(); Hold = NoCatch = Freeze = Overlay = false; Note = ""; }
    }
}
