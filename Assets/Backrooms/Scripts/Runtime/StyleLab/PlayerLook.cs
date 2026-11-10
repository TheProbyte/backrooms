using System;
using UnityEngine;

namespace Backrooms.StyleLab
{
    /// <summary>
    /// The player's own look in the suit (2026-11-06, Phase 5 of Docs/StyleRollout.md): the face's choices
    /// (<see cref="HazmatAppearance"/>) and the crew number on the back, chosen on the YOUR LOOK page (Menu.LookMenu).
    /// The name on the chest tape is the co-op name (Net.NetSession.PlayerName).
    /// Saved in PlayerPrefs as one number (<see cref="Pack"/>), and sent as that number to the other players in co-op
    /// (with the name when joining, and again when it changes: small shared state, so a late joiner gets everyone's
    /// look without replaying changes).
    /// </summary>
    [Serializable]
    public struct PlayerLook
    {
        public int eyes, nose, brows, hair, facialHair, mask, skinTone, hairTone, crew;
        public bool glasses;

        // How many choices each has (HazmatAppearance's enums; the tones as the suit prefab has them, at most 8).
        public const int Eyes = 3, Noses = 3, Brows = 3, Hairs = 4, FacialHairs = 4, Masks = 3, MaxTones = 8, MaxCrew = 99;

        const string Key = "Backrooms.Look";
        const int ValidBit = 1 << 30;

        /// <summary>Raised when the saved look changes (your own body takes it at once, co-op sends it).</summary>
        public static event Action Changed;

        /// <summary>The suit's look before anyone chose one (HazmatAppearance's defaults).</summary>
        public static PlayerLook Default => new PlayerLook { eyes = 0, nose = 0, brows = 0, hair = 0, facialHair = 1, mask = 0, skinTone = 0, hairTone = 0, crew = 7 };

        /// <summary>Your look, as saved (the default until you choose one).</summary>
        public static PlayerLook Saved
        {
            get { int v = PlayerPrefs.GetInt(Key, 0); return Valid(v) ? Unpack(v) : Default; }
            set { PlayerPrefs.SetInt(Key, value.Pack()); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        /// <summary>One number for the save and the network: 2 bits a feature, 3 a tone, 7 the crew number.</summary>
        public int Pack()
        {
            return (eyes & 3) | (nose & 3) << 2 | (brows & 3) << 4 | (hair & 3) << 6 | (facialHair & 3) << 8 | (mask & 3) << 10
                 | (glasses ? 1 : 0) << 12 | (skinTone & 7) << 13 | (hairTone & 7) << 16 | (Mathf.Clamp(crew, 0, MaxCrew) & 127) << 19 | ValidBit;
        }

        public static bool Valid(int v) => (v & ValidBit) != 0;

        public static PlayerLook Unpack(int v)
        {
            return new PlayerLook
            {
                eyes = Mathf.Min(v & 3, Eyes - 1), nose = Mathf.Min(v >> 2 & 3, Noses - 1), brows = Mathf.Min(v >> 4 & 3, Brows - 1),
                hair = v >> 6 & 3, facialHair = v >> 8 & 3, mask = Mathf.Min(v >> 10 & 3, Masks - 1), glasses = (v >> 12 & 1) != 0,
                skinTone = v >> 13 & 7, hairTone = v >> 16 & 7, crew = Mathf.Min(v >> 19 & 127, MaxCrew),
            };
        }

        /// <summary>A look picked at random (the mask now and then, as most people show their face).</summary>
        public static PlayerLook Random(int skinTones, int hairTones, System.Random rng = null)
        {
            rng ??= new System.Random();
            return new PlayerLook
            {
                eyes = rng.Next(Eyes), nose = rng.Next(Noses), brows = rng.Next(Brows), hair = rng.Next(Hairs), facialHair = rng.Next(FacialHairs),
                mask = rng.NextDouble() < 0.15 ? 1 + rng.Next(Masks - 1) : 0, glasses = rng.NextDouble() < 0.3,
                skinTone = rng.Next(Mathf.Max(1, skinTones)), hairTone = rng.Next(Mathf.Max(1, hairTones)), crew = 1 + rng.Next(MaxCrew),
            };
        }

        /// <summary>A co-op player who has not sent a look (an older game): one from their player number, as before.</summary>
        public static PlayerLook FromPeer(int peer)
        {
            int k = Mathf.Abs(peer);
            return new PlayerLook
            {
                eyes = k % 3, nose = (k + 1) % 3, brows = k % 3, hair = k % 4, facialHair = (k + 2) % 4,
                skinTone = k % 3, hairTone = (k + 1) % 4, crew = k + 1,
            };
        }

        /// <summary>Puts the look on a suit (then HazmatAppearance.Apply). The tones are kept within what the suit has.</summary>
        public void ApplyTo(HazmatAppearance ap)
        {
            if (!ap) return;
            ap.eyes = (HazmatAppearance.EyeStyle)Mathf.Clamp(eyes, 0, Eyes - 1);
            ap.nose = (HazmatAppearance.NoseStyle)Mathf.Clamp(nose, 0, Noses - 1);
            ap.brows = (HazmatAppearance.BrowStyle)Mathf.Clamp(brows, 0, Brows - 1);
            ap.hair = (HazmatAppearance.HairStyle)Mathf.Clamp(hair, 0, Hairs - 1);
            ap.facialHair = (HazmatAppearance.FacialHairStyle)Mathf.Clamp(facialHair, 0, FacialHairs - 1);
            ap.mask = (HazmatAppearance.MaskStyle)Mathf.Clamp(mask, 0, Masks - 1);
            ap.glasses = glasses;
            int skins = ap.skinTones != null ? ap.skinTones.Length : 1, hairs = ap.hairTones != null ? ap.hairTones.Length : 1;
            ap.skinTone = Mathf.Clamp(skinTone, 0, Mathf.Max(0, skins - 1));
            ap.hairTone = Mathf.Clamp(hairTone, 0, Mathf.Max(0, hairs - 1));
            ap.crewNumber = Mathf.Clamp(crew, 0, MaxCrew).ToString("00");
        }
    }
}
