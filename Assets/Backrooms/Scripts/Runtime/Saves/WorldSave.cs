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
        /// <summary>The contract floor the lift goes down to (Generation.LeasePlan slot), or -1 (2026-11-07).</summary>
        public int lease = -1;
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
        /// <summary>Nails driven in with the hammer (Interaction.NailWork).</summary>
        public List<NailData> nails = new List<NailData>();
        /// <summary>Co-op (2026-11-04): the players who have joined this world, where they left and what they carried
        /// (they get it back when they join again under the same name).</summary>
        public List<GuestData> guests = new List<GuestData>();

        // ---- Contracts (2026-11-07, Docs/ContractFloorsPlan.md; Interaction.Contracts) ----
        /// <summary>The day's board: three jobs on the world's one floor (LeasePlan), taken or not.</summary>
        public List<ContractData> board = new List<ContractData>();
        /// <summary>(Old saves: the one taken contract's index. Unused since several can be taken a day.)</summary>
        public int active = -1;
        /// <summary>Money in hand (dollars: pay and rent in, improvements out), and jobs finished.</summary>
        public int pay, jobsDone;
        /// <summary>(Old saves: the next board's first floor. Unused: one floor a world.)</summary>
        public int nextSlot;
        /// <summary>Office hours: seconds into the shift (-1: no shift on). 9 to 5 on the camcorder's clock.</summary>
        public float shift = -1f;
        /// <summary>The working day (from 1): a board a day, rent at the end of each.</summary>
        public int day = 1;
        /// <summary>Everything ever earned (pay and rent), whatever was spent.</summary>
        public int earned;
        /// <summary>The next box's number (box ids: Interaction.Contracts.BoxId).</summary>
        public int nextBox;
        /// <summary>Boxes kept in the floor's storage for their owners (Store jobs paid): they earn rent every day until a
        /// Retrieve job takes them back.</summary>
        public List<ulong> stored = new List<ulong>();

        // ---- Improvements (Interaction.Improvements): bought at the hub with pay, at most one a night ----
        /// <summary>The floor's wings leased (bits: N, E, S, W; LeasePlan.Wings) and the way into the Poolrooms.</summary>
        public int wings;
        public bool poolrooms;
        /// <summary>Seams with an intake chute fitted (their layout ids); chute kits bought and not fitted yet.</summary>
        public List<ulong> chutes = new List<ulong>();
        public int chuteKits;
        /// <summary>Overtime hours bought (closing at 6, 7, 8 PM), backup lights, rent reviews, platform trucks.</summary>
        public int overtime, backupLights, rentReviews, trucks;
        /// <summary>The day something was last bought (one a night), or 0.</summary>
        public int boughtDay;

        // ---- Storage (2026-11-08, Docs/GameDesign.md section 8; Interaction.Shelves) ----
        /// <summary>Overflow's shelves on the floor (moved-prop ids: the starter shelf, kit shelves, found ones), and the
        /// next shelf's number.</summary>
        public List<ulong> shelves = new List<ulong>();
        public int nextShelf;
        /// <summary>Each shelf's light from your own lamps when last measured (0..1; 1 is safe): ids and levels.</summary>
        public List<ulong> shelfLightIds = new List<ulong>();
        public List<float> shelfLight = new List<float>();
        /// <summary>The starter shelf and lamp have been put out; the districts whose knocked-over shelves are out (x, z pairs).</summary>
        public bool starterSet;
        public List<int> foundIn = new List<int>();
        /// <summary>Stored boxes left in the dark night after night: ids and how many nights running.</summary>
        public List<ulong> darkIds = new List<ulong>();
        public List<int> darkNights = new List<int>();
        /// <summary>Last night: boxes that were safe, moved by the dark, gone for good (the board's report).</summary>
        public int lastSafe, lastMoved, lastGone;
        public bool hasReport;
        /// <summary>The punch clock (2026-11-08): work orders done today (for the pay slip) and the last pay slip, its lines
        /// as "left\tright" (Interaction.Contracts.PunchOut; shown again at the pay window).</summary>
        public List<int> doneToday = new List<int>();
        public string lastSlip = "";

        // ---- The supply counter and kits (Interaction.Supplies, Interaction.Kits, Interaction.KitPlacer) ----
        /// <summary>Things delivered by the lift so far (where the next one waits), the next kit crate's number, shelves put
        /// up from kits.</summary>
        public int delivered, nextKit, kitShelves;
        /// <summary>What finished work orders have unlocked (Interaction.Unlocks keys).</summary>
        public List<string> unlocked = new List<string>();

        // ---- Work orders (2026-11-08, Docs/GameDesign.md section 7; Interaction.WorkOrders) ----
        /// <summary>The stage of the sequence (0 onboarding, tiers, the gate, then after the main floor), each order's
        /// progress (by its index in WorkOrders.All) and whether it is done, and the day the next onboarding order shows.</summary>
        public int woStage;
        public List<int> woProgress = new List<int>();
        public List<int> woDone = new List<int>();
        public int woNextDay;
        /// <summary>The orientation tape has been watched in the training room (the first order: Interaction.HubTvScreen).</summary>
        public bool tapeWatched;
        /// <summary>Fragile boxes (break if dropped or thrown).</summary>
        public List<ulong> fragile = new List<ulong>();
        // ---- Batches (2026-11-09, Docs/GameDesign.md sections 6-7; Interaction.Contracts.Batches) ----
        /// <summary>The save's version of the work: 2 from batches (older saves are moved over once: Contracts.Migrate).</summary>
        public int workVersion;
        /// <summary>The clients' batches: waiting behind their seam, coming in, stored, due back. Returned ones are gone.</summary>
        public List<BatchData> batches = new List<BatchData>();
        public int nextBatch;
        /// <summary>Business time run so far (s): it runs on the floor and behind the seams, never in the hub. The camcorder's
        /// date follows it (client time).</summary>
        public float business;
        /// <summary>When each client's next lot may go out behind its seam (business time), by client.</summary>
        public List<float> lotAt = new List<float>();
        /// <summary>Clients whose seam you have found (been within a few metres of, or through).</summary>
        public List<int> seamsFound = new List<int>();
        /// <summary>A seam other than the taped first one has been found (any client's: a floor may have few kinds).</summary>
        public bool otherSeamFound;
        /// <summary>Batches stored (intake paid), returned, returned within their window; clients returned to.</summary>
        public int batchesIn, batchesDone, batchesOnTime;
        public List<int> clientsServed = new List<int>();
        /// <summary>Scuffed boxes (left unsafe too long while stored: a mark on them, a little off the pay).</summary>
        public List<ulong> scuffed = new List<ulong>();
        /// <summary>Boxes signed for (returned, or a withdrawn lot), left where they are until nobody can see them go
        /// (Contracts.Batches, 2026-11-09).</summary>
        public List<ulong> collect = new List<ulong>();
        /// <summary>This trip's pay slip so far ("left\tright" lines; printed when you ride back up) and trips made.</summary>
        public List<string> trip = new List<string>();
        public int trips;
        /// <summary>The power bill run up and not taken yet (dollars), and what this trip's power came to.</summary>
        public float powerOwed;
        public int tripPower;
        /// <summary>Hand trucks on the floor (x, y, z, yaw each).</summary>
        public List<float> handTrucks = new List<float>();
        /// <summary>BETTER RATES bought (each: new batches pay 15% more).</summary>
        public int rates;

        /// <summary>Generators set up on the floor (the power bill) and their moved-prop ids.</summary>
        public List<ulong> generators = new List<ulong>();
        public List<ulong> conveyors = new List<ulong>();
        public List<ulong> floodlights = new List<ulong>();
        /// <summary>Machines plugged into a generator (by its yellow industrial plug): the machine and the generator.</summary>
        public List<ulong> plugMachine = new List<ulong>();
        public List<ulong> plugGenerator = new List<ulong>();
        /// <summary>Dead ceiling tubes put back with spare tubes (fixture keys: chunk x, storey, z, fixture index).</summary>
        public List<int> tubesFixed = new List<int>();
    }

    /// <summary>
    /// A client's batch (2026-11-09, Docs/GameDesign.md section 7): a lot of boxes taken from behind their seam, stored on
    /// lit shelves for a set business time, then taken back through the same seam. Paid twice: the intake fee once every
    /// box is safe on a shelf, the completion pay when it is back (less lateness and scuffs).
    /// </summary>
    [Serializable]
    public class BatchData
    {
        public int id, client, count;
        /// <summary>0 waiting behind the seam, 1 coming in, 2 stored (its countdown running), 3 due back.</summary>
        public int state;
        public int intake, completion;
        /// <summary>How long it is stored (business seconds), how long is left, the collection window, time since due.</summary>
        public float storeFor, left, window, since;
        public List<ulong> boxes = new List<ulong>();
        /// <summary>While stored: how long each box (by index) has been unsafe without a break.</summary>
        public List<float> unsafeFor = new List<float>();
        public bool fragile, riskSaid, lateSaid;
        /// <summary>A rush lot (2026-11-09): half the storage time for half as much pay again; offered, never forced.</summary>
        public bool rush;
        /// <summary>A rush lot waiting: the business time it was offered at (it is withdrawn if nobody takes it).</summary>
        public float offeredAt;
    }

    /// <summary>A contract on the board (Interaction.Contracts; before 2026-11-09's batches: kept to read old saves).</summary>
    [Serializable]
    public class ContractData
    {
        /// <summary>0 Store: bring a client's boxes in through their seam and shelve them in the floor's storage. 1 Ship: carry
        /// them from one seam to another place's DELIVERIES bay. 2 Retrieve: take a client's stored boxes back to their
        /// place's DELIVERIES bay.</summary>
        public int kind;
        /// <summary>The contract floor (Generation.LeasePlan).</summary>
        public int slot;
        public int count, pay;
        /// <summary>The seams (layout prop ids) and their kinds (Generation.LeaseSeams.Kinds); to only for Ship.</summary>
        public ulong from, to;
        public int fromKind, toKind;
        /// <summary>Boxes out of the from chute so far; boxes stored (last count) or shipped.</summary>
        public int sent, done;
        /// <summary>The boxes (movable prop ids).</summary>
        public List<ulong> boxes = new List<ulong>();
        /// <summary>0 posted, 1 taken, 2 finished (pay waiting at the window), 3 failed (closing time came first), 4 paid.</summary>
        public int state;
        /// <summary>The work order this is today's batch of (Interaction.WorkOrders index + 1; 0: a side job).</summary>
        public int orderPlusOne;
        /// <summary>An audit: done by this hour on the camcorder's clock or lost (0: by closing).</summary>
        public int byHour;
        public bool fragile;
    }

    /// <summary>A player who joined the host's world: where they were and what they carried (items as in the save's list:
    /// held, in a pocket, a backpack on the back and what is in it).</summary>
    [Serializable]
    public class GuestData
    {
        public string name = "";
        public Vector3 position;
        public float yaw;
        public List<ItemData> items = new List<ItemData>();
    }

    /// <summary>A nail: in the building (prop empty: world space) or in a prop (its id, hex: the prop's space).</summary>
    [Serializable]
    public class NailData
    {
        public string prop = "";
        public Vector3 position, direction;
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
        /// <summary>Stuck where it is (nailed in, or build mode's physics off); its size (0: as made).</summary>
        public bool pinned;
        public float scale;
        /// <summary>Built from a named fitting model (MovableSpec.fitting), or empty.</summary>
        public string fitting;
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
        /// <summary>In a backpack (2026-11-04): 1 + the backpack's index in the items, its position and rotation then the
        /// bag's own (where it lies in it); 0 not in one.</summary>
        public int bag;
        /// <summary>A backpack on the player's back.</summary>
        public bool worn;
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

#if UNITY_EDITOR
        /// <summary>An agent's Play test never touches the player's saves: while .claude/unity.lock exists, Play in
        /// the editor uses a test folder (the lock's "saves=" line, else Temp/AgentSaves). Set before any scene
        /// object wakes, so nothing can read or write the real folder first.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void AgentTestFolder()
        {
            FolderOverride = AgentFolder();
            if (FolderOverride != null) Debug.Log("[SaveSlots] agent lock present: saves go to " + FolderOverride);
        }

        /// <summary>The test folder an agent's lock asks for, or null with no lock.</summary>
        public static string AgentFolder()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string lockFile = Path.Combine(root, ".claude", "unity.lock");
            if (!File.Exists(lockFile)) return null;
            string folder = Path.Combine(root, "Temp", "AgentSaves");
            try
            {
                foreach (string line in File.ReadAllLines(lockFile))
                    if (line.StartsWith("saves=")) folder = line.Substring(6).Trim();
            }
            catch (IOException) { }
            Directory.CreateDirectory(folder);
            return folder;
        }
#endif
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
        public static bool InMenu => Slot < 0 && !NetClient && !CustomRooms.BuildSession.Testing;

        /// <summary>Co-op (2026-11-04): playing in another player's world (it is theirs: nothing is saved here).</summary>
        public static bool NetClient { get; private set; }
        /// <summary>The joined world's paint and chalk, as the host sent them.</summary>
        public static byte[] NetPaint, NetMarks;
        /// <summary>In a world that can be shared now (a save being played, or a joined world).</summary>
        public static bool InWorldForNet => (Slot >= 0 || NetClient) && Data != null && Generation.WorldStreamer.Current && !Generation.WorldStreamer.Current.MenuMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Slot = -1; Data = null; NetClient = false; NetPaint = NetMarks = null; }

        /// <summary>Joins the host's world: its save as sent (never written here), its paint and chalk.</summary>
        public static void PlayNet(WorldSaveData d, byte[] paint, byte[] marks)
        {
            Slot = -1; Data = d; NetClient = true; NetPaint = paint; NetMarks = marks;
            Menu.TapeTransition.Run("PLAY ►  JOINING", () => UnityEngine.SceneManagement.SceneManager.LoadScene(CustomRooms.SceneIds.World));
        }

        /// <summary>Plays a slot: its saved world, or a new one if it is empty.</summary>
        public static void Play(int slot)
        {
            var d = SaveSlots.Load(slot);
            bool fresh = d == null;
            if (fresh) d = SaveSlots.Create(slot);
            Slot = slot; Data = d;
            Menu.TapeTransition.Run(fresh ? "PLAY ►  NEW WORLD" : "PLAY ►", () => UnityEngine.SceneManagement.SceneManager.LoadScene(CustomRooms.SceneIds.World));
        }

        /// <summary>Saves and leaves the world for the main menu (a joined world: just leaves it; hosting: the others are told).</summary>
        public static void ToMenu()
        {
            WorldSaver.SaveNow();
            if (Net.NetSession.Active) Net.NetSession.Stop(Net.NetSession.IsHost ? "THE HOST ENDED THE GAME" : null, false);
            Slot = -1; Data = null; NetClient = false; NetPaint = NetMarks = null;
            Menu.TapeTransition.Run("STOP ■", () => UnityEngine.SceneManagement.SceneManager.LoadScene(CustomRooms.SceneIds.World));
        }

        /// <summary>Saves and forgets the slot (when leaving the world for another scene).</summary>
        public static void Leave()
        {
            WorldSaver.SaveNow();
            if (Net.NetSession.Active) Net.NetSession.Stop(Net.NetSession.IsHost ? "THE HOST ENDED THE GAME" : null, false);
            Slot = -1; Data = null; NetClient = false; NetPaint = NetMarks = null;
        }
    }
}
