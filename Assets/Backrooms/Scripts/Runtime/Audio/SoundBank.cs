using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>Shared procedural clips (built once on first use) and each sound's base level.</summary>
    public static class SoundBank
    {
        static AudioClip[] s_hum, s_strike, s_tick, s_steps;
        static AudioClip s_buzz, s_vent, s_tone, s_building;

        // Recorded sounds supplied by the owner (Resources/Audio) replace the procedural ones when present.
        static bool s_triedRecorded;
        static AudioClip s_recordedBuzz;
        static AudioClip[] s_recordedSteps;

        static void LoadRecorded()
        {
            if (s_triedRecorded) return;
            s_triedRecorded = true;
            s_recordedBuzz = Resources.Load<AudioClip>("Audio/FluorescentBuzz_Loop");
            var steps = Resources.LoadAll<AudioClip>("Audio/Footsteps");
            if (steps != null && steps.Length > 0)
            {
                System.Array.Sort(steps, (a, b) => string.CompareOrdinal(a.name, b.name));
                s_recordedSteps = steps;
            }
        }

        /// <summary>True when the owner's recorded fluorescent buzz is used for the hum.</summary>
        public static bool RecordedHum { get { LoadRecorded(); return s_recordedBuzz; } }

        public static AudioClip Hum(int variant)
        {
            LoadRecorded();
            if (s_recordedBuzz) return s_recordedBuzz;      // one long loop; fixtures differ by phase and detune
            if (s_hum == null) { s_hum = new AudioClip[4]; for (int i = 0; i < 4; i++) s_hum[i] = ProceduralSounds.FluorescentHum(i); }
            return s_hum[variant & 3];
        }

        public static int StepCount { get { LoadRecorded(); return s_recordedSteps != null ? s_recordedSteps.Length : 8; } }
        public static AudioClip Strike(int variant) { if (s_strike == null) { s_strike = new AudioClip[3]; for (int i = 0; i < 3; i++) s_strike[i] = ProceduralSounds.StrikeOn(i); } return s_strike[Mathf.Abs(variant) % 3]; }
        public static AudioClip Tick(int variant) { if (s_tick == null) { s_tick = new AudioClip[4]; for (int i = 0; i < 4; i++) s_tick[i] = ProceduralSounds.Tick(i); } return s_tick[variant & 3]; }
        public static AudioClip Step(int variant)
        {
            LoadRecorded();
            if (s_recordedSteps != null) return s_recordedSteps[Mathf.Abs(variant) % s_recordedSteps.Length];
            if (s_steps == null) { s_steps = new AudioClip[8]; for (int i = 0; i < 8; i++) s_steps[i] = ProceduralSounds.CarpetStep(i); }
            return s_steps[variant & 7];
        }
        static AudioClip s_zoom;
        public static AudioClip ZoomMotor => s_zoom ? s_zoom : (s_zoom = ProceduralSounds.ZoomMotor());
        public static AudioClip Buzz => s_buzz ? s_buzz : (s_buzz = ProceduralSounds.FaultyBuzz());
        public static AudioClip Ventilation => s_vent ? s_vent : (s_vent = ProceduralSounds.Ventilation());
        public static AudioClip RoomTone => s_tone ? s_tone : (s_tone = ProceduralSounds.RoomTone());
        public static AudioClip BuildingHum => s_building ? s_building : (s_building = ProceduralSounds.BuildingHum());

        /// <summary>Volume of each sound before its catalog multiplier, distance and the camcorder chain.</summary>
        public static float BaseLevel(string id)
        {
            switch (id)
            {
                case SoundIds.FluorescentHum: return RecordedHum ? 0.03f : 0.045f;
                case SoundIds.FluorescentStrike: return 0.12f;
                case SoundIds.FaultyBuzz: return 0.05f;
                case SoundIds.BuildingHum: return 0.022f;
                case SoundIds.Ventilation: return 0.03f;
                case SoundIds.RoomTone: return 0.03f;
                case SoundIds.ZoomMotor: return 0.06f;
                case SoundIds.Footsteps: return s_recordedSteps != null || StepCount != 8 ? 0.05f : 0.08f;
                default: return 0.05f;
            }
        }

        static AnimationCurve s_flat;

        /// <summary>
        /// Applies a catalog sound's spatial settings to a source. distanceInVolume: the caller bakes
        /// distance / wall attenuation into the volume (building emitters, so the level can be smoothed as
        /// one number), and the source only provides direction; otherwise Unity's rolloff follows the
        /// catalog curve.
        /// </summary>
        public static void ApplySpatial(AudioSource src, string id, bool distanceInVolume = false)
        {
            var def = SoundCatalog.Def(id);
            src.dopplerLevel = 0f;
            src.spread = 0f;
            if (!def.positional) { src.spatialBlend = 0f; return; }
            src.spatialBlend = SoundCatalog.SpatialBlend(id);
            src.rolloffMode = AudioRolloffMode.Custom;
            src.minDistance = 0.01f;
            if (distanceInVolume)
            {
                if (s_flat == null) s_flat = AnimationCurve.Constant(0f, 1f, 1f);
                src.maxDistance = 1000f;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, s_flat);
            }
            else
            {
                src.maxDistance = SoundCatalog.MaxDistance(id);
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SoundCatalog.RolloffCurve(id));
            }
        }
    }
}
