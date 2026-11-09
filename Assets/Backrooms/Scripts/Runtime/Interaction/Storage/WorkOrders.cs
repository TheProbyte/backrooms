using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Work orders (2026-11-08, Docs/GameDesign.md section 7, "like Satisfactory's milestones"): the board's work is a fixed
    /// sequence, the same in every world. Onboarding comes first, in order (each as soon as the last is done: no waiting for
    /// the next day, the owner: "I'm done, what do I do, wait for 20 minutes"); then tiers of orders done in any
    /// order; then a big gate (the first quarter's shipment). Each order pays when it is done, and finishing a stage unlocks
    /// things at the supply counter and the improvements board (Unlocks). Each tier's orders teach what it unlocks next.
    /// After the gate (past the main floor, still to design) the board posts the old side jobs.
    /// <para>Box work (store, ship, return) is done in batches: each day an open order's batch of boxes waits at
    /// the seam (a ContractData on the board, done as before); paying it in adds to the order. Keeping boxes safe is counted
    /// overnight (the night's safe count); putting up shelves and the chute row are counted as they happen.</para>
    /// </summary>
    public static class WorkOrders
    {
        public enum Goal { ClockIn, Store, Return, PutUpShelves, KeepSafe, Ship, Audit, ChuteRow }

        public struct Order
        {
            public int stage;
            public string title, line;
            public Goal goal;
            public int client, target, pay, byHour;
            public bool fragile;
        }

        public static readonly string[] StageNames = { "ONBOARDING", "TIER 1 - GETTING SET UP", "TIER 2 - MORE CLIENTS", "TIER 3 - RUNNING THE FLOOR", "THE FIRST QUARTER", "OVERFLOW WORK" };

        /// <summary>What each stage unlocks when its last order is done (Docs/GameDesign.md section 7's table).</summary>
        public static readonly string[][] StageUnlocks =
        {
            new[] { "counter", "truck" },
            new[] { "worklamp", "chute", "overtime1" },
            new[] { "tallshelf", "generator", "repairkit" },
            new[] { "conveyor", "floodlight", "rentreview1" },
            new[] { "rack", "overtime2", "overtime3", "backup1", "backup2", "rentreview2", "wings", "poolrooms" },
        };

        static readonly string[] s_unlockNames =
        {
            "THE SUPPLY COUNTER, THE PLATFORM TRUCK",
            "WORK LAMPS, INTAKE CHUTES, OVERTIME",
            "TALL SHELVING, GENERATORS, THE REPAIR KIT",
            "CONVEYORS, FLOODLIGHTS, A RENT REVIEW",
            "PALLET RACKING, MORE UPGRADES, THE WINGS",
        };

        /// <summary>What finishing a stage unlocks, in words ("" past the last).</summary>
        public static string UnlocksOf(int stage) => stage >= 0 && stage < s_unlockNames.Length ? s_unlockNames[stage] : "";

        public static readonly Order[] All =
        {
            // Onboarding, in order, one a day.
            new Order { stage = 0, title = "WATCH THE TAPE, CLOCK IN", line = "SIT DOWN IN THE TRAINING ROOM, THEN RIDE THE LIFT DOWN", goal = Goal.ClockIn, target = 1, pay = 20 },
            new Order { stage = 0, title = "FIRST STORAGE", line = "FOLLOW THE BLUE TAPE - 5 BOXES ONTO THE STARTER SHELF", goal = Goal.Store, client = 0, target = 5, pay = 80 },
            new Order { stage = 0, title = "FIND THE PRINT SHOP", line = "FIND ITS SEAM - BRING BACK THE SAMPLE BOX", goal = Goal.Store, client = 1, target = 1, pay = 60 },
            new Order { stage = 0, title = "RETRIEVAL", line = "3 STORED BOXES BACK TO THEIR OWNER", goal = Goal.Return, client = 0, target = 3, pay = 70 },
            // Tier 1: getting set up.
            new Order { stage = 1, title = "PUT UP 2 SHELVING KITS", line = "FROM THE SUPPLY COUNTER - ANYWHERE ON THE FLOOR", goal = Goal.PutUpShelves, target = 2, pay = 60 },
            new Order { stage = 1, title = "KEEP 20 BOXES SAFE", line = "OVERNIGHT, ON LIT SHELVES - NEW BOXES DAILY", goal = Goal.KeepSafe, target = 20, pay = 150 },
            new Order { stage = 1, title = "SHIP 10 BOXES", line = "FROM ONE CLIENT'S SEAM TO ANOTHER'S", goal = Goal.Ship, target = 10, pay = 120 },
            // Tier 2: more clients.
            new Order { stage = 2, title = "STORE FOR PAPERLARK", line = "8 OF THEIR BOXES ON YOUR SHELVES", goal = Goal.Store, client = 2, target = 8, pay = 120 },
            new Order { stage = 2, title = "A FRAGILE LOAD", line = "4 QUILLBY BOXES - DO NOT DROP OR THROW THEM", goal = Goal.Store, client = 1, target = 4, pay = 140, fragile = true },
            new Order { stage = 2, title = "AN AUDIT FOR TALLMORE", line = "4 OF THEIRS BACK TO THEM BY NOON", goal = Goal.Audit, client = 0, target = 4, pay = 130, byHour = 12 },
            // Tier 3: running the floor.
            new Order { stage = 3, title = "KEEP 50 BOXES SAFE", line = "OVERNIGHT, ON LIT SHELVES - NEW BOXES DAILY", goal = Goal.KeepSafe, target = 50, pay = 300 },
            new Order { stage = 3, title = "A CHUTE AND A SHELF ROW", line = "3 SHELVES WITHIN 12 M OF A SEAM WITH A CHUTE", goal = Goal.ChuteRow, target = 1, pay = 200 },
            // The gate.
            new Order { stage = 4, title = "FIRST QUARTER'S SHIPMENT", line = "80 BOXES SAFE AT ONCE - NEW BOXES DAILY", goal = Goal.KeepSafe, target = 80, pay = 600 },
        };

        /// <summary>Stages with orders; past them, the board posts side jobs.</summary>
        public const int Stages = 5;

        static WorldSaveData D => GameSession.Data;

        public static bool Active => D != null && D.woStage < Stages;
        public static string StageName => D == null ? "" : StageNames[Mathf.Clamp(D.woStage, 0, StageNames.Length - 1)];

        static void Ensure()
        {
            while (D.woProgress.Count < All.Length) D.woProgress.Add(0);
        }

        public static bool Done(int i) => D != null && D.woDone.Contains(i);
        public static int Progress(int i) { if (D == null) return 0; Ensure(); return D.woProgress[i]; }
        public static bool BoxWork(Goal g) => g == Goal.Store || g == Goal.Return || g == Goal.Ship || g == Goal.Audit || g == Goal.KeepSafe;

        /// <summary>The orders on the board now (up to three): onboarding's next one (from the day after the last), or the
        /// stage's orders not done.</summary>
        public static List<int> Open()
        {
            var list = new List<int>();
            if (!Active) return list;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].stage != D.woStage || Done(i)) continue;
                if (D.woStage == 0) { list.Add(i); break; }
                list.Add(i);
                if (list.Count == 3) break;
            }
            return list;
        }

        /// <summary>(Onboarding used to wait a day between orders; it no longer does.)</summary>
        public static bool WaitingForTomorrow => false;

        public static void Credit(int i, int n)
        {
            if (D == null || i < 0 || i >= All.Length || Done(i) || n <= 0) return;
            Ensure();
            D.woProgress[i] = Mathf.Min(All[i].target, D.woProgress[i] + n);
            if (D.woProgress[i] >= All[i].target) Complete(i);
        }

        static void Set(int i, int v)
        {
            if (Done(i)) return;
            Ensure();
            v = Mathf.Clamp(v, 0, All[i].target);
            if (v == D.woProgress[i]) return;
            D.woProgress[i] = v;
            Contracts.Instance?.Changed();
            if (v >= All[i].target) Complete(i);
        }

        static void Complete(int i)
        {
            if (Done(i)) return;
            D.woDone.Add(i);
            var o = All[i];
            D.pay += o.pay; D.earned += o.pay;
            CamcorderOSD.Notice($"WORK ORDER DONE - {o.title} - ${o.pay}");
            bool stageDone = true;
            for (int k = 0; k < All.Length; k++) if (All[k].stage == D.woStage && !Done(k)) stageDone = false;
            if (stageDone) Advance();
            Contracts.Instance?.Changed();
            Supplies.Instance?.Changed();
            Improvements.Instance?.Changed();
        }

        static void Advance()
        {
            int s = D.woStage;
            if (s < StageUnlocks.Length) foreach (var k in StageUnlocks[s]) Unlocks.Give(k);
            D.woStage++;
            string now = s < s_unlockNames.Length ? s_unlockNames[s] : "";
            CamcorderOSD.Notice(D.woStage < Stages ? $"NEW WORK ORDERS - UNLOCKED: {now}" : $"THE MAIN FLOOR IS RUNNING - UNLOCKED: {now}");
        }

        /// <summary>Checked twice a second (Contracts): clocking in, shelves put up, the chute row.</summary>
        public static void Tick(List<LeaseSeams.SeamInfo> seams)
        {
            if (!Active) return;
            foreach (int i in Open())
            {
                var o = All[i];
                switch (o.goal)
                {
                    case Goal.ClockIn: if (D.tapeWatched && D.shift >= 0f) Set(i, 1); break;
                    case Goal.PutUpShelves: Set(i, D.kitShelves); break;
                    case Goal.ChuteRow: if (ChuteRow(seams)) Set(i, 1); break;
                }
            }
        }

        /// <summary>After the night: keeping boxes safe counts what was safe on lit shelves.</summary>
        public static void Night(int safe)
        {
            if (!Active) return;
            foreach (int i in Open()) if (All[i].goal == Goal.KeepSafe) Set(i, Mathf.Max(Progress(i), safe));
        }

        /// <summary>A fitted chute with three standing shelves within 12 m of its seam.</summary>
        static bool ChuteRow(List<LeaseSeams.SeamInfo> seams)
        {
            if (seams == null) return false;
            foreach (var s in seams)
            {
                if (!D.chutes.Contains(s.id)) continue;
                int n = 0;
                foreach (var id in D.shelves)
                    if (Shelves.Pose(id, out var at, out _, out int v) && !ShelfTypes.IsBroken(v) && Vector3.Distance(at, s.position) < 12f) n++;
                if (n >= 3) return true;
            }
            return false;
        }

        // ---------------- Today's batch of an order's boxes ----------------

        /// <summary>The leased seam a client's boxes come from (the nearest to the lift of their kind), or the nearest of any
        /// kind if theirs is not on the floor.</summary>
        public static bool SeamFor(int client, List<LeaseSeams.SeamInfo> seams, Vector3 lift, out LeaseSeams.SeamInfo seam, int not = -1)
        {
            seam = default;
            // The first client's boxes always come from the first seam: the one the company's tape leads to.
            if (client == LeaseSeams.FirstKind && not != LeaseSeams.FirstKind)
                foreach (var s in seams) if (LeaseSeams.IsFirst(s, LeasePlan.Current)) { seam = s; return true; }
            float best = float.MaxValue; bool found = false;
            for (int pass = 0; pass < 2 && !found; pass++)
                foreach (var s in seams)
                {
                    if (s.kind == not || (pass == 0 && s.kind != client)) continue;
                    float d = Vector3.Distance(lift, s.position);
                    if (d < best) { best = d; seam = s; found = true; }
                }
            return found;
        }

        /// <summary>The client whose name an order's row shows (theirs, or the stand-in whose seam is on the floor).</summary>
        public static int ClientFor(int i, List<LeaseSeams.SeamInfo> seams, Vector3 lift)
        {
            var o = All[i];
            int client = o.goal == Goal.KeepSafe || o.goal == Goal.Ship ? (D.day + i) % 4 : o.client;
            return seams != null && SeamFor(client, seams, lift, out var s) ? s.kind : client;
        }

        /// <summary>Today's batch for an order (null: nothing to take: no box work, nothing of theirs stored, no seam).</summary>
        public static ContractData Batch(int i, List<LeaseSeams.SeamInfo> seams, Vector3 lift, int slot, int[] storedBy, out string why)
        {
            why = null;
            var o = All[i];
            if (!BoxWork(o.goal)) { why = "NOTHING TO TAKE - JUST DO IT"; return null; }
            if (seams == null || seams.Count == 0) { why = "NO SEAMS ON THE FLOOR"; return null; }
            int left = o.target - Progress(i);
            var c = new ContractData { slot = slot, orderPlusOne = i + 1, fragile = o.fragile, byHour = o.byHour };
            switch (o.goal)
            {
                case Goal.Store:
                case Goal.KeepSafe:
                {
                    int client = o.goal == Goal.KeepSafe ? (D.day + i) % 4 : o.client;
                    if (!SeamFor(client, seams, lift, out var s)) { why = "NO SEAMS ON THE FLOOR"; return null; }
                    int count = o.goal == Goal.KeepSafe ? Mathf.Clamp(o.target - D.stored.Count, 0, 8) : Mathf.Min(left, 6);
                    if (count <= 0) { why = "ENOUGH STORED - KEEP THEM SAFE TONIGHT"; return null; }
                    c.kind = 0; c.from = s.id; c.fromKind = s.kind; c.count = count;
                    break;
                }
                case Goal.Ship:
                {
                    if (!SeamFor((D.day + i) % 4, seams, lift, out var a) || !SeamFor((D.day + i + 1) % 4, seams, lift, out var b, a.kind))
                    { why = "NEEDS TWO CLIENTS' SEAMS"; return null; }
                    c.kind = 1; c.from = a.id; c.fromKind = a.kind; c.to = b.id; c.toKind = b.kind; c.count = Mathf.Min(left, 5);
                    break;
                }
                default:            // Return, Audit: the client's own stored boxes back to their seam
                {
                    if (!SeamFor(o.client, seams, lift, out var s)) { why = "NO SEAMS ON THE FLOOR"; return null; }
                    int have = storedBy != null ? storedBy[Mathf.Clamp(s.kind, 0, 3)] : 0;
                    int count = Mathf.Min(left, have);
                    if (count <= 0) { why = "NOTHING OF THEIRS IS STORED YET"; return null; }
                    c.kind = 2; c.to = s.id; c.toKind = c.fromKind = s.kind; c.count = count;
                    break;
                }
            }
            c.pay = 5 * c.count;          // (a little a box; the order pays when it is done)
            return c;
        }
    }

    /// <summary>A fragile box (2026-11-08): dropped or thrown hard, it breaks (Contracts.Broke).</summary>
    public class FragileBox : MonoBehaviour
    {
        MovableProp _prop;
        float _since;

        public static void Attach(MovableProp p)
        {
            var d = GameSession.Data;
            if (!p || d == null || !d.fragile.Contains(p.Spec.id) || p.GetComponent<FragileBox>()) return;
            var fb = p.gameObject.AddComponent<FragileBox>();
            fb._prop = p;
            // A red FRAGILE sticker on its lid.
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "FragileSticker";
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(p.transform, false);
            s.transform.localPosition = new Vector3(0f, p.Spec.size.y + 0.002f, 0f);
            s.transform.localScale = new Vector3(0.22f, 0.003f, 0.14f);
            var r = s.GetComponent<MeshRenderer>();
            r.sharedMaterial = Sticker;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Material s_sticker;
        static Material Sticker
        {
            get
            {
                if (s_sticker) return s_sticker;
                var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
                s_sticker = new Material(sh) { name = "FragileSticker", hideFlags = HideFlags.DontSave };
                s_sticker.SetColor("_BaseColor", new Color(0.85f, 0.12f, 0.1f));
                return s_sticker;
            }
        }

        void OnEnable() { _since = Time.time; }

        void OnCollisionEnter(Collision c)
        {
            if (!_prop || _prop.held || Time.time - _since < 1f) return;
            if (c.relativeVelocity.magnitude < 3.4f) return;
            Contracts.Instance?.Broke(_prop.Spec.id);
        }
    }
}
