using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Backrooms.Saves
{
    /// <summary>
    /// One world save: the seed (the world itself is always rebuilt from it) plus the changes made to it. Written as
    /// JSON (world{n}.json); the paint is a separate binary file (world{n}.paint, see SprayPaint.Write).
    /// </summary>
    [Serializable]
    public class WorldSaveData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public int slot;
        public int seed;
        public string created, saved;          // ISO 8601, local time
        public double playSeconds;             // time spent in the world (the camcorder's tape counter)
        public string zone = "";               // where the player was, for the slot card

        public bool hasPlayer;
        public Vector3 playerPosition;
        public float playerYaw, playerPitch;
        public int stance;
        public bool noclip;

        public List<PropData> props = new List<PropData>();
        public List<ItemData> items = new List<ItemData>();
        public List<PlacedData> placed = new List<PlacedData>();
        /// <summary>Things picked up in special rooms (their rooms do not make them again).</summary>
        public List<string> takenLoot = new List<string>();
        /// <summary>Rooms whose lights were switched off at the wall: chunk x, storey, z and room, four ints each (Generation.LightZones).</summary>
        public List<int> lightsOff = new List<int>();
        /// <summary>Districts whose breaker was switched on: district x, storey, z, three ints each (Interaction.PowerGrid).</summary>
        public List<int> breakersOn = new List<int>();
        /// <summary>Rooms claimed as home: district x, storey, district z, room id, four ints each (Interaction.Homes).</summary>
        public List<int> homes = new List<int>();
        /// <summary>Doors that were opened or shut (Interaction.DoorStates): their keys and angles.</summary>
        public List<string> doorKeys = new List<string>();
        public List<float> doorAngles = new List<float>();
        /// <summary>Doors slide-bolted shut (their keys).</summary>
        public List<string> doorBolts = new List<string>();
        /// <summary>The door a Poolrooms noclip door leads back to (Interaction.NoclipTravel).</summary>
        public bool hasNoclipOrigin;
        public Vector3 noclipOrigin, noclipOriginNormal;
        /// <summary>Walls broken with the sledgehammer (Destruction.WallDamage): what is left of each, and its rubble.</summary>
        public List<WallData> walls = new List<WallData>();
    }

    /// <summary>
    /// One broken wall panel. The pieces it breaks into come from the seed; this lists which are gone (bits, base64) and
    /// where its debris settled.
    /// </summary>
    [Serializable]
    public class WallData
    {
        public bool vertical;
        public int line, along, storey;
        public string board0, board1, crack0, crack1, insulation;
        public int baseboard;
        public float[] baseboardDamage;
        /// <summary>Per stud: the number of lengths standing, each length (from, to), then its damage per 0.45 m.</summary>
        public float[] studs;
        public List<Destruction.WallDamage.Rubble> rubble = new List<Destruction.WallDamage.Rubble>();
    }

    /// <summary>A movable prop that has been disturbed (generated or spawned from the menu), and where it is now.</summary>
    [Serializable]
    public class PropData
    {
        public string id;                      // hex
        public int kind;
        public long renderMask;
        public string proxy;
        public Vector3 size;
        public int variant;
        public bool cardboard;
        public string[] materials;
        public List<PartData> parts = new List<PartData>();
        public Vector3 origin;
        public float originYaw;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public class PartData
    {
        public string prefab, material;
        public Vector3 position, scale;
        public float yaw;
    }

    /// <summary>An item lying in the world or in the hand.</summary>
    [Serializable]
    public class ItemData
    {
        public string id;                      // WorldItem.itemId
        public Vector3 position;
        public Quaternion rotation;
        public float amount = 1f;              // paint left, chalk left, tape left, a lamp's switch, a cord coiled (0) or the share of it out (1: all)
        public bool held;
        /// <summary>In a pocket (0-3: Interaction.PropGrabber.Pockets), or -1.</summary>
        public int pocket = -1;
        /// <summary>Held: the pocket that is its own (kept for it while it is out), or -1.</summary>
        public int home = -1;
        // Cords: where the plug is, and what it is in: -1 nothing, 0 the wall socket at plugWall, n > 0 socket plugSocket
        // of the extension cord that is item n - 1.
        public bool hasPlug;
        public Vector3 plugPosition;
        public Quaternion plugRotation;
        public int plugTarget = -1, plugSocket;
        public Vector3 plugWall;
    }

    /// <summary>A fixed piece spawned from the developer menu (floor lamp, wall clock).</summary>
    [Serializable]
    public class PlacedData
    {
        public string name;                    // ItemCatalog entry
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>The three save slots on disk (Application.persistentDataPath/Saves).</summary>
    public static class SaveSlots
    {
        public const int Count = 3;

        /// <summary>Another folder for the saves (tests); null: the player's own.</summary>
        public static string FolderOverride;
        public static string Folder => FolderOverride ?? Path.Combine(Application.persistentDataPath, "Saves");
        public static string DataPath(int slot) => Path.Combine(Folder, $"world{slot + 1}.json");
        public static string PaintPath(int slot) => Path.Combine(Folder, $"world{slot + 1}.paint");
        public static string MarksPath(int slot) => Path.Combine(Folder, $"world{slot + 1}.marks");

        public static bool Exists(int slot) => File.Exists(DataPath(slot));

        /// <summary>The slot's save, or null if it is empty or unreadable.</summary>
        public static WorldSaveData Load(int slot)
        {
            try
            {
                var path = DataPath(slot);
                if (!File.Exists(path)) return null;
                var d = JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(path));
                if (d == null) return null;
                d.slot = slot;
                return d;
            }
            catch (Exception e) { Debug.LogWarning($"[Backrooms] Could not read save {slot + 1}: {e.Message}"); return null; }
        }

        /// <summary>A new world in the slot: a random seed, nothing done yet. Written straight away.</summary>
        public static WorldSaveData Create(int slot)
        {
            var now = DateTime.Now.ToString("o");
            var d = new WorldSaveData { slot = slot, seed = new System.Random().Next(1, int.MaxValue), created = now, saved = now };
            Write(d, null);
            return d;
        }

        /// <summary>Writes the save (and the paint, if given) safely: to a temporary file first, then over the old one.</summary>
        public static void Write(WorldSaveData d, byte[] paint) => Write(d, paint, null);

        public static void Write(WorldSaveData d, byte[] paint, byte[] marks)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                WriteAtomic(DataPath(d.slot), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(d, true)));
                if (paint != null) WriteAtomic(PaintPath(d.slot), paint);
                if (marks != null) WriteAtomic(MarksPath(d.slot), marks);
            }
            catch (Exception e) { Debug.LogError($"[Backrooms] Could not write save {d.slot + 1}: {e}"); }
        }

        static void WriteAtomic(string path, byte[] bytes)
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static byte[] ReadMarks(int slot)
        {
            try { var p = MarksPath(slot); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
            catch (Exception e) { Debug.LogWarning($"[Backrooms] Could not read marks of save {slot + 1}: {e.Message}"); return null; }
        }

        public static byte[] ReadPaint(int slot)
        {
            try { var p = PaintPath(slot); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
            catch (Exception e) { Debug.LogWarning($"[Backrooms] Could not read paint of save {slot + 1}: {e.Message}"); return null; }
        }

        /// <summary>Empties the slot.</summary>
        public static void Erase(int slot)
        {
            try
            {
                foreach (var p in new[] { DataPath(slot), PaintPath(slot), MarksPath(slot) }) if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception e) { Debug.LogWarning($"[Backrooms] Could not erase save {slot + 1}: {e.Message}"); }
        }
    }

    /// <summary>
    /// Which world the game is in. The world scene starts on the main menu unless a save slot has been chosen
    /// (<see cref="Slot"/>) or build mode is testing a room. Choosing a slot reloads the world scene with its seed.
    /// </summary>
    public static class GameSession
    {
        /// <summary>The save being played (0..2), or -1 (main menu, or a world that is not saved).</summary>
        public static int Slot { get; private set; } = -1;
        /// <summary>The save being played, as loaded when the world started.</summary>
        public static WorldSaveData Data { get; private set; }

        /// <summary>Should the world scene show the main menu?</summary>
        public static bool InMenu => Slot < 0 && !CustomRooms.BuildSession.Testing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Slot = -1; Data = null; }

        /// <summary>Plays a slot: its saved world, or a new one if it is empty.</summary>
        public static void Play(int slot)
        {
            var d = SaveSlots.Load(slot);
            bool fresh = d == null;
            if (fresh) d = SaveSlots.Create(slot);
            Slot = slot; Data = d;
            Menu.TapeTransition.Run(fresh ? "PLAY ►  NEW WORLD" : "PLAY ►", () => UnityEngine.SceneManagement.SceneManager.LoadScene(CustomRooms.SceneIds.World));
        }

        /// <summary>Saves and leaves the world for the main menu.</summary>
        public static void ToMenu()
        {
            WorldSaver.SaveNow();
            Slot = -1; Data = null;
            Menu.TapeTransition.Run("STOP ■", () => UnityEngine.SceneManagement.SceneManager.LoadScene(CustomRooms.SceneIds.World));
        }

        /// <summary>Saves and forgets the slot (when leaving the world for another scene).</summary>
        public static void Leave()
        {
            WorldSaver.SaveNow();
            Slot = -1; Data = null;
        }
    }
}
