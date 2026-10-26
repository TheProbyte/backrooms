using UnityEngine;

namespace Backrooms.Audio
{
    public static partial class SoundBank
    {
        static AudioClip s_chalk, s_tapePull, s_tapeTear, s_click, s_plugIn, s_plugOut, s_whip, s_crackle;
        static AudioClip[] s_squeeze, s_release;

        public static AudioClip ChalkScratch => s_chalk ? s_chalk : (s_chalk = ProceduralSounds.ChalkScratch());
        public static AudioClip TapePull => s_tapePull ? s_tapePull : (s_tapePull = ProceduralSounds.TapePull());
        public static AudioClip TapeTear => s_tapeTear ? s_tapeTear : (s_tapeTear = ProceduralSounds.TapeTear());
        public static AudioClip SwitchClick => s_click ? s_click : (s_click = ProceduralSounds.SwitchClick());
        public static AudioClip PlugIn => s_plugIn ? s_plugIn : (s_plugIn = ProceduralSounds.Plug(true));
        public static AudioClip PlugOut => s_plugOut ? s_plugOut : (s_plugOut = ProceduralSounds.Plug(false));
        public static AudioClip CordWhip => s_whip ? s_whip : (s_whip = ProceduralSounds.CordWhip());
        public static AudioClip NoclipCrackle => s_crackle ? s_crackle : (s_crackle = ProceduralSounds.NoclipCrackle());

        public static AudioClip ChickenSqueeze(int i)
        {
            if (s_squeeze == null) { s_squeeze = new AudioClip[3]; for (int k = 0; k < 3; k++) s_squeeze[k] = ProceduralSounds.Chicken(true, k); }
            return s_squeeze[Mathf.Abs(i) % 3];
        }

        public static AudioClip ChickenRelease(int i)
        {
            if (s_release == null) { s_release = new AudioClip[3]; for (int k = 0; k < 3; k++) s_release[k] = ProceduralSounds.Chicken(false, k); }
            return s_release[Mathf.Abs(i) % 3];
        }
    }
}
