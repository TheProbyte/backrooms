using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Work orders (2026-11-08, revised 2026-11-09: Docs/GameDesign.md section 7, "like Satisfactory's milestones"): the
    /// company's goals, a fixed sequence, the same in every world. Onboarding comes first, in order; then tiers of orders
    /// done in any order; then a big gate. Each order pays when it is done, and finishing a stage unlocks things at the
    /// supply counter and the improvements board (Unlocks). Since the batches (Contracts.Batches) the goals are broad: store
    /// a batch, have so many boxes stored safely at once, complete so many batches, work for three clients, rather than one
    /// exact job. Everything is counted as it happens.
    /// </summary>
    public static class WorkOrders
    {
        public enum Goal { Tape, StoreBatch, FindSeam, ReturnBatch, PutUpShelves, StoredAtOnce, Batches, Clients, FragileBatch, OnTime, ChuteRow }

        public struct Order
        {
            public int stage;
            public string title, line;
            public Goal goal;
            public int target, pay;
        }

        public static readonly string[] StageNames = { "ONBOARDING", "TIER 1 - GETTING SET UP", "TIER 2 - MORE CLIENTS", "TIER 3 - RUNNING THE FLOOR", "THE FIRST QUARTER", "OVERFLOW WORK" };

        /// <summary>What each stage unlocks when its last order is done (Docs/GameDesign.md section 7's table).</summary>
        public static readonly string[][] StageUnlocks =
        {
            new[] { "counter", "handtruck" },
            new[] { "truck", "worklamp", "chute" },
            new[] { "tallshelf", "generator", "repairkit" },
            new[] { "conveyor", "floodlight", "rates1" },
            new[] { "rack", "rates2", "wings", "poolrooms" },
        };

        static readonly string[] s_unlockNames =
        {
            "THE SUPPLY COUNTER, THE HAND TRUCK",
            "THE PLATFORM TRUCK, WORK LAMPS, INTAKE CHUTES",
            "TALL SHELVING, GENERATORS, THE REPAIR KIT",
            "CONVEYORS, FLOODLIGHTS, BETTER RATES",
            "PALLET RACKING, BETTER RATES, THE WINGS",
        };

        /// <summary>What finishing a stage unlocks, in words ("" past the last).</summary>
        public static string UnlocksOf(int stage) => stage >= 0 && stage < s_unlockNames.Length ? s_unlockNames[stage] : "";

        public static readonly Order[] All =
        {
            // Onboarding, in order.
            new Order { stage = 0, title = "WATCH THE TAPE", line = "SIT DOWN IN THE TRAINING ROOM", goal = Goal.Tape, target = 1, pay = 20 },
            new Order { stage = 0, title = "FIRST STORAGE", line = "FOLLOW THE BLUE TAPE FROM THE LIFT - SHELVE TALLMORE'S BOXES ON THE LIT STARTER SHELF", goal = Goal.StoreBatch, target = 1, pay = 60 },
            new Order { stage = 0, title = "FIND ANOTHER SEAM", line = "WHILE THEY WAIT, FIND ANOTHER SEAM OUT ON THE FLOOR - ITS CARD SAYS WHAT IT OFFERS", goal = Goal.FindSeam, target = 1, pay = 40 },
            new Order { stage = 0, title = "BACK ON TIME", line = "WHEN TALLMORE'S BOXES ARE DUE, TAKE THEM BACK THROUGH THEIR SEAM", goal = Goal.ReturnBatch, target = 1, pay = 60 },
            // Tier 1: getting set up.
            new Order { stage = 1, title = "PUT UP 2 SHELVING KITS", line = "FROM THE SUPPLY COUNTER - ANYWHERE ON THE FLOOR", goal = Goal.PutUpShelves, target = 2, pay = 60 },
            new Order { stage = 1, title = "20 BOXES STORED SAFELY", line = "HAVE 20 BOXES OF STORED BATCHES ON LIT SHELVES AT ONCE", goal = Goal.StoredAtOnce, target = 20, pay = 150 },
            new Order { stage = 1, title = "COMPLETE 4 BATCHES", line = "STORED AND RETURNED - ANY CLIENTS", goal = Goal.Batches, target = 4, pay = 120 },
            // Tier 2: more clients.
            new Order { stage = 2, title = "THREE CLIENTS", line = "RETURN A BATCH TO 3 DIFFERENT CLIENTS (OR EVERY ONE ON YOUR FLOOR)", goal = Goal.Clients, target = 3, pay = 120 },
            new Order { stage = 2, title = "A FRAGILE LOAD", line = "A QUILLBY BATCH MARKED FRAGILE - DO NOT DROP OR THROW THEM", goal = Goal.FragileBatch, target = 1, pay = 140 },
            new Order { stage = 2, title = "ON TIME", line = "RETURN 3 BATCHES INSIDE THEIR WINDOW", goal = Goal.OnTime, target = 3, pay = 130 },
            // Tier 3: running the floor.
            new Order { stage = 3, title = "50 BOXES STORED SAFELY", line = "HAVE 50 BOXES OF STORED BATCHES ON LIT SHELVES AT ONCE", goal = Goal.StoredAtOnce, target = 50, pay = 300 },
            new Order { stage = 3, title = "A CHUTE AND A SHELF ROW", line = "3 SHELVES WITHIN 12 M OF A SEAM WITH A CHUTE", goal = Goal.ChuteRow, target = 1, pay = 200 },
            // The gate.
            new Order { stage = 4, title = "THE FIRST QUARTER", line = "COMPLETE 20 BATCHES", goal = Goal.Batches, target = 20, pay = 600 },
        };

        /// <summary>Stages with orders; past them, the seams' batches are the work.</summary>
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

        /// <summary>The orders open now: onboarding's next one, or the stage's orders not done.</summary>
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

        /// <summary>A fragile load is asked for: QUILLBY's next lot is marked fragile.</summary>
        public static bool WantsFragile { get { foreach (int i in Open()) if (All[i].goal == Goal.FragileBatch) return true; return false; } }

        /// <summary>An order's progress in words for the tracker ("" for one with nothing to count).</summary>
        public static string ProgressText(int i)
        {
            var o = All[i];
            switch (o.goal)
            {
                case Goal.Tape: return D.tapeWatched ? "WATCHED" : "NOT YET";
                case Goal.FindSeam: case Goal.ChuteRow: case Goal.StoreBatch: case Goal.ReturnBatch: case Goal.FragileBatch: return "";
                case Goal.StoredAtOnce: return $"{(Contracts.Instance ? Contracts.Instance.SafeNow : 0)} OF {o.target} SAFE NOW";
                default: return $"{Progress(i)} OF {o.target}";
            }
        }

        /// <summary>An order's count for the checklist ("12/20"), or "" for one done in a single step.</summary>
        public static string CountText(int i)
        {
            var o = All[i];
            switch (o.goal)
            {
                case Goal.StoredAtOnce: return $"{(Contracts.Instance ? Contracts.Instance.SafeNow : 0)}/{o.target}";
                case Goal.PutUpShelves: case Goal.Batches: case Goal.Clients: case Goal.OnTime: return $"{Progress(i)}/{o.target}";
                default: return "";
            }
        }

        /// <summary>The stage whose orders unlock this (-1: none does).</summary>
        public static int StageOf(string unlock)
        {
            for (int s = 0; s < StageUnlocks.Length; s++) foreach (var k in StageUnlocks[s]) if (k == unlock) return s;
            return -1;
        }

        /// <summary>What to do to unlock it, in words: "FINISH ONBOARDING'S WORK ORDERS", "FINISH TIER 2'S WORK ORDERS".</summary>
        public static string FinishText(string unlock)
        {
            int s = StageOf(unlock);
            return s < 0 ? "LOCKED" : s == 0 ? "FINISH ONBOARDING'S WORK ORDERS" : s < Stages - 1 ? $"FINISH TIER {s}'S WORK ORDERS" : "FINISH THE FIRST QUARTER'S WORK ORDER";
        }

        /// <summary>A locked thing's line on a board: which stage's work orders open it ("AFTER ONBOARDING", "AFTER TIER 1").</summary>
        public static string LockedText(string unlock)
        {
            int s = StageOf(unlock);
            return s < 0 ? "LOCKED" : s == 0 ? "AFTER ONBOARDING" : s < Stages - 1 ? $"AFTER TIER {s}" : "AFTER THE QUARTER";
        }

        public static void Credit(int i, int n)
        {
            if (D == null || i < 0 || i >= All.Length || Done(i) || n <= 0) return;
            Ensure();
            D.woProgress[i] = Mathf.Min(All[i].target, D.woProgress[i] + n);
            Contracts.Instance?.Changed();
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
            Contracts.Tell($"WORK ORDER DONE - {o.title} - ${o.pay}");
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
            Contracts.Tell(D.woStage < Stages ? $"NEW WORK ORDERS - UNLOCKED: {now}" : $"THE MAIN FLOOR IS RUNNING - UNLOCKED: {now}");
        }

        /// <summary>Checked twice a second (Contracts): what is counted by looking (the tape, seams found, shelves put up,
        /// boxes stored at once, the chute row, onboarding's batches).</summary>
        public static void Tick(List<LeaseSeams.SeamInfo> seams)
        {
            if (!Active) return;
            foreach (int i in Open())
            {
                var o = All[i];
                switch (o.goal)
                {
                    case Goal.Tape: if (D.tapeWatched) Set(i, 1); break;
                    case Goal.StoreBatch: if (D.batchesIn > 0) Set(i, 1); break;
                    case Goal.FindSeam: { int n = D.otherSeamFound ? 1 : 0; foreach (int k in D.seamsFound) if (k != LeaseSeams.FirstKind) n++; Set(i, n); break; }
                    case Goal.ReturnBatch: if (D.batchesDone > 0) Set(i, 1); break;
                    case Goal.PutUpShelves: Set(i, D.kitShelves); break;
                    case Goal.StoredAtOnce: if (Contracts.Instance) Set(i, Mathf.Max(Progress(i), Contracts.Instance.SafeNow)); break;
                    case Goal.Clients:
                    {
                        // (Every client on your floor, if it has fewer than three.)
                        int need = Contracts.Instance ? Mathf.Clamp(Contracts.Instance.ClientsOnFloor, 1, o.target) : o.target;
                        Set(i, D.clientsServed.Count >= need ? o.target : D.clientsServed.Count);
                        break;
                    }
                    case Goal.ChuteRow: if (ChuteRow(seams)) Set(i, 1); break;
                }
            }
        }

        /// <summary>A batch returned: the orders that count batches, on-time ones and fragile loads.</summary>
        public static void BatchReturned(int client, bool onTime, bool fragile)
        {
            if (!Active) return;
            foreach (int i in Open())
            {
                var g = All[i].goal;
                if (g == Goal.Batches) Credit(i, 1);
                else if (g == Goal.OnTime && onTime) Credit(i, 1);
                else if (g == Goal.FragileBatch && fragile) Credit(i, 1);
            }
        }

        /// <summary>A save from before the batches (2026-11-09): the orders list changed, so the stage it was on starts
        /// again on the new list; the stages before it stay done (and what they unlocked stays unlocked).</summary>
        /// <summary>
        /// The developer skip (L menu, PROGRESSION; 2026-11-09, the owner: "scalable ways for me to skip progression ... so
        /// I can skip to when the supply counter is open"): every order before this stage counted done (not paid), the tape
        /// watched, and every unlock those stages give. Like Satisfactory's Creative Mode "unlock all tiers". It only goes
        /// forward. Stages = everything unlocked.
        /// </summary>
        public static void SkipTo(int stage)
        {
            if (D == null) return;
            stage = Mathf.Clamp(stage, 0, Stages);
            Ensure();
            for (int i = 0; i < All.Length; i++)
                if (All[i].stage < stage && !D.woDone.Contains(i)) { D.woDone.Add(i); D.woProgress[i] = All[i].target; }
            for (int s = 0; s < stage && s < StageUnlocks.Length; s++) foreach (var k in StageUnlocks[s]) Unlocks.Give(k);
            if (stage > 0) D.tapeWatched = true;
            if (stage > D.woStage) D.woStage = stage;
            Contracts.Instance?.Changed();
            Supplies.Instance?.Changed();
            Improvements.Instance?.Changed();
        }

        /// <summary>The skip menu's buttons: one per stage, made from the stage list (a new stage gets its own button).</summary>
        public static string SkipLabel(int stage)
            => stage >= Stages ? "UNLOCK EVERYTHING" : stage == 1 ? "SKIP ONBOARDING" : "SKIP TO " + StageNames[stage].Split(new[] { " - " }, System.StringSplitOptions.None)[0];

        public static void Migrate()
        {
            if (D == null) return;
            D.woProgress.Clear();
            D.woDone.Clear();
            for (int i = 0; i < All.Length; i++) if (All[i].stage < D.woStage) D.woDone.Add(i);
            // (Orders the old list already paid for, done again here without paying twice: the tape.)
            if (D.woStage == 0 && D.tapeWatched) D.woDone.Add(0);
            for (int s = 0; s < D.woStage && s < StageUnlocks.Length; s++) foreach (var k in StageUnlocks[s]) Unlocks.Give(k);
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

        /// <summary>The leased seam a client's boxes come from (the nearest to the lift of their kind; the first client's,
        /// the taped first seam), or the nearest of any kind if theirs is not on the floor.</summary>
        public static bool SeamFor(int client, List<LeaseSeams.SeamInfo> seams, Vector3 lift, out LeaseSeams.SeamInfo seam, int not = -1)
        {
            seam = default;
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
