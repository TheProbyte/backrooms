using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A compact cassette (2026-11-04; model: Tools/Blender/build_boombox.py): a store-bought C60 someone wrote "lunch
    /// break mix" on, its music Resources/Audio/Cassette_LunchBreakMix (Tools/Audio/make_cassette_music.py). Found in the
    /// rooms behind the crawl vents now and then. Small: it goes in a pocket. Clicked on a boombox while in the hand, it
    /// goes into its deck (<see cref="Boombox.TryInsert"/>); with the deck's door open, the hand takes it back out. Where
    /// the tape is (seconds into side A) is its saved Amount, so it plays on from where it was stopped.
    /// </summary>
    public class Cassette : WorldItem
    {
        static AudioClip s_clip;
        /// <summary>Seconds into side A.</summary>
        public float Position { get; set; }
        public Boombox Deck { get; private set; }
        public string Title => "\"lunch break mix\"";
        public AudioClip Clip
        {
            get
            {
                if (!s_clip) s_clip = Resources.Load<AudioClip>("Audio/Cassette_LunchBreakMix");
                return s_clip;
            }
        }

        public override bool Pocketable => true;
        public override bool UsesAim => true;
        public override float Amount { get => Position; set => Position = Mathf.Max(0f, value); }
        public override string Status => Deck ? "cassette (in the deck)" : $"cassette: {Title} (click a boombox to put it in)";
        // Held between the fingers, label toward you.
        public override Vector3 HoldOffset => new Vector3(0.16f, -0.2f, 0.36f);
        public override Quaternion HoldTurn => Quaternion.Euler(-65f, 0f, 0f);
        public override Vector3 GripLocal => new Vector3(0f, 0.006f, 0.02f);
        protected override bool Pinned => Deck != null;

        public static Cassette Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("Cassette", position, rotation, 0.045f, "Cassette", new Vector3(0.1004f, 0.0125f, 0.0638f), "Plastic_CassetteShell");
            var c = go.AddComponent<Cassette>();
            c.itemId = "cassette"; c.displayName = "Cassette";
            Generation.PoolLitSwitch.Attach(go);
            return c;
        }

        void Start() { if (!Deck) AdoptSelf(); }

        /// <summary>After a load: a cassette lying where a boombox's deck holds one goes back in (they are saved apart).</summary>
        public static void AdoptNear(Boombox box)
        {
            if (!box) return;
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] is Cassette c && !c.Deck && !c.Held && (c.transform.position - box.SlotWorld).sqrMagnitude < 0.05f * 0.05f) { box.Adopt(c); return; }
        }

        void AdoptSelf()
        {
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] is Boombox b && b.CanTake && (transform.position - b.SlotWorld).sqrMagnitude < 0.05f * 0.05f) { b.Adopt(this); return; }
        }

        public override void HoldUpdate(in HoldInput i)
        {
            if (!i.pressed || !i.hit || !i.hitInfo.collider) return;
            var box = i.hitInfo.collider.GetComponentInParent<Boombox>();
            if (box && (i.hitInfo.point - i.eye).magnitude < 2.2f) box.TryInsert(this, i.hand);
        }

        /// <summary>Into a deck's door: held there (kinematic, its parent the door), takeable only while the door is open.</summary>
        public void EnterDeck(Boombox box, Transform door, Vector3 localPos, Quaternion localRot, bool takeable)
        {
            Deck = box;
            if (lootId != 0) { Saves.LootRegistry.Take(lootId); lootId = 0; }
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;      // (interpolated, it kept pulling itself toward where it had been)
            transform.SetParent(door, false);
            transform.localPosition = localPos;
            transform.localRotation = localRot;
            Body.position = transform.position; Body.rotation = transform.rotation;
            SetTakeable(takeable);
        }

        public void SetTakeable(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        protected override void OnPickedUp() => LeaveDeck();

        /// <summary>Out of its deck (taken by hand, here or in another player's game).</summary>
        public void LeaveDeck()
        {
            if (!Deck) return;
            var box = Deck;
            Deck = null;
            transform.SetParent(null, true);
            box.Removed(this);
        }
    }
}
