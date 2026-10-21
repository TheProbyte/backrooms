using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>Stable ids. Never rename one: saved overrides are keyed by these strings.</summary>
    public static class SoundIds
    {
        public const string FluorescentHum = "lights.fluorescent_hum";
        public const string FluorescentStrike = "lights.fluorescent_strike";
        public const string FaultyBuzz = "lights.faulty_buzz";
        public const string BuildingHum = "ambience.building_hum";
        public const string Ventilation = "ambience.ventilation";
        public const string RoomTone = "ambience.room_tone";
        public const string Footsteps = "player.footsteps_carpet";
    }

    /// <summary>
    /// Definition of one sound type: what it is, which settings make sense for it, and its defaults.
    /// Every sound in the game registers here (see <see cref="SoundCatalog.RegisterBuiltIns"/>), so the
    /// F5 Sound tab can list sounds whose emitters aren't loaded.
    /// </summary>
    public class SoundDef
    {
        public string id, name, category, description;
        public bool positional;          // has distance / attenuation / spatial blend settings
        public bool hasPitch = true;
        public float volume = 1f, pitch = 1f;
        public float minDistance = 1f, maxDistance = 15f, rolloff = 1f, spatialBlend = 1f;
        // Up to two sound-specific parameters (null name = not used).
        public string paramAName, paramADesc; public float paramA, paramAMin, paramAMax = 1f;
        public string paramBName, paramBDesc; public float paramB, paramBMin, paramBMax = 1f;
    }

    /// <summary>A saved edit for one sound. Only fields flagged in <see cref="set"/> override the default.</summary>
    [Serializable]
    public class SoundOverride
    {
        public string id;
        public int set;                  // bit flags of Field
        public bool mute;
        public float volume = 1f, pitch = 1f, minDistance, maxDistance, rolloff, spatialBlend, paramA, paramB;
    }

    public enum SoundField { Volume = 1, Mute = 2, Pitch = 4, MinDistance = 8, MaxDistance = 16, Rolloff = 32, SpatialBlend = 64, ParamA = 128, ParamB = 256 }

    /// <summary>
    /// Central registry of sound definitions plus the player's global tuning profile (one set of overrides
    /// for the whole game, saved to PlayerPrefs). Emitters read effective values from here every frame and
    /// smooth them, so edits apply to every existing and future emitter of that type.
    ///
    /// Processing order: per-sound settings (here) -> environment / spatial (BuildingAudio) -> camcorder
    /// chain (CamcorderAudioChain) -> output volume.
    /// </summary>
    public static class SoundCatalog
    {
        const string PrefsKey = "Backrooms.SoundProfile.v1";

        [Serializable] class Profile { public List<SoundOverride> entries = new List<SoundOverride>(); }

        static readonly List<SoundDef> s_defs = new List<SoundDef>();
        static readonly Dictionary<string, SoundDef> s_byId = new Dictionary<string, SoundDef>();
        static readonly Dictionary<string, SoundOverride> s_over = new Dictionary<string, SoundOverride>();
        static bool s_loaded;

        /// <summary>Bumps whenever any override changes (emitters rebuild curves when it moves).</summary>
        public static int Version { get; private set; }

        public static IReadOnlyList<SoundDef> All { get { Ensure(); return s_defs; } }

        public static void Register(SoundDef d)
        {
            if (s_byId.ContainsKey(d.id)) return;
            s_defs.Add(d); s_byId[d.id] = d;
        }

        static void Ensure()
        {
            if (s_loaded) return;
            s_loaded = true;
            RegisterBuiltIns();
            Load();
        }

        public static SoundDef Def(string id)
        {
            Ensure();
            if (s_byId.TryGetValue(id, out var d)) return d;
            Debug.LogError("[Backrooms] Sound '" + id + "' is not registered in SoundCatalog. Register every new sound so it appears in F5 > Sound.");
            var stub = new SoundDef { id = id, name = id, category = "Unregistered", description = "" };
            Register(stub);
            return stub;
        }

        // ---------------- Effective values ----------------

        static SoundOverride O(string id) { Ensure(); return s_over.TryGetValue(id, out var o) ? o : null; }
        static bool Has(SoundOverride o, SoundField f) => o != null && (o.set & (int)f) != 0;

        public static bool Muted(string id) { var o = O(id); return Has(o, SoundField.Mute) && o.mute; }
        /// <summary>Volume multiplier including mute (0 when muted).</summary>
        public static float Volume(string id) { var o = O(id); if (Has(o, SoundField.Mute) && o.mute) return 0f; return Has(o, SoundField.Volume) ? o.volume : Def(id).volume; }
        public static float RawVolume(string id) { var o = O(id); return Has(o, SoundField.Volume) ? o.volume : Def(id).volume; }
        public static float Pitch(string id) { var o = O(id); return Has(o, SoundField.Pitch) ? o.pitch : Def(id).pitch; }
        public static float MinDistance(string id) { var o = O(id); return Has(o, SoundField.MinDistance) ? o.minDistance : Def(id).minDistance; }
        public static float MaxDistance(string id) { var o = O(id); return Mathf.Max(MinDistance(id) + 0.5f, Has(o, SoundField.MaxDistance) ? o.maxDistance : Def(id).maxDistance); }
        public static float Rolloff(string id) { var o = O(id); return Has(o, SoundField.Rolloff) ? o.rolloff : Def(id).rolloff; }
        public static float SpatialBlend(string id) { var o = O(id); return Has(o, SoundField.SpatialBlend) ? o.spatialBlend : Def(id).spatialBlend; }
        public static float ParamA(string id) { var o = O(id); return Has(o, SoundField.ParamA) ? o.paramA : Def(id).paramA; }
        public static float ParamB(string id) { var o = O(id); return Has(o, SoundField.ParamB) ? o.paramB : Def(id).paramB; }

        public static bool IsModified(string id) { var o = O(id); return o != null && o.set != 0; }

        /// <summary>
        /// Distance gain used by every positional emitter (and by emitter selection): full level inside
        /// min distance, then (min/d)^rolloff, faded smoothly to silence at max distance.
        /// </summary>
        public static float DistanceGain(string id, float d)
        {
            float min = MinDistance(id), max = MaxDistance(id), k = Rolloff(id);
            if (d >= max) return 0f;
            float g = d <= min ? 1f : Mathf.Pow(min / d, k);
            float edge = Mathf.InverseLerp(max, max * 0.7f, d);          // last 30 % fades to zero
            return g * Mathf.SmoothStep(0f, 1f, edge);
        }

        /// <summary>Rolloff curve in Unity's normalised (0..1 of max distance) form.</summary>
        public static AnimationCurve RolloffCurve(string id)
        {
            float max = MaxDistance(id);
            var keys = new Keyframe[16];
            for (int i = 0; i < keys.Length; i++)
            {
                float t = i / (float)(keys.Length - 1);
                t = t * t;                                                // more keys near the source
                keys[i] = new Keyframe(t, DistanceGain(id, t * max));
            }
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        // ---------------- Editing ----------------

        public static void Set(string id, SoundField f, float value)
        {
            Ensure();
            if (!s_over.TryGetValue(id, out var o)) { o = new SoundOverride { id = id }; s_over[id] = o; }
            o.set |= (int)f;
            switch (f)
            {
                case SoundField.Volume: o.volume = value; break;
                case SoundField.Mute: o.mute = value > 0.5f; break;
                case SoundField.Pitch: o.pitch = value; break;
                case SoundField.MinDistance: o.minDistance = value; break;
                case SoundField.MaxDistance: o.maxDistance = value; break;
                case SoundField.Rolloff: o.rolloff = value; break;
                case SoundField.SpatialBlend: o.spatialBlend = value; break;
                case SoundField.ParamA: o.paramA = value; break;
                case SoundField.ParamB: o.paramB = value; break;
            }
            Version++;
        }

        public static void Reset(string id) { Ensure(); s_over.Remove(id); Version++; }

        public static void ResetAll() { Ensure(); s_over.Clear(); Version++; Save(); }

        public static void Save()
        {
            Ensure();
            var p = new Profile();
            foreach (var o in s_over.Values) if (o.set != 0) p.entries.Add(o);
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(p));
            PlayerPrefs.Save();
        }

        static void Load()
        {
            s_over.Clear();
            try
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                if (string.IsNullOrEmpty(json)) return;
                var p = JsonUtility.FromJson<Profile>(json);
                // Overrides for ids that are no longer (or not yet) registered are kept as they are, so
                // nothing is lost when sounds are added, renamed in the UI, or temporarily removed.
                foreach (var o in p.entries) if (!string.IsNullOrEmpty(o.id)) s_over[o.id] = o;
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load sound profile: " + e.Message); }
            Version++;
        }

        // ---------------- Built-in sounds ----------------

        /// <summary>Every sound the game can make. New sounds must be added here.</summary>
        static void RegisterBuiltIns()
        {
            Register(new SoundDef
            {
                id = SoundIds.FluorescentHum, name = "Fluorescent hum", category = "Lights", positional = true,
                description = "The 120 Hz ballast hum of every powered ceiling fixture. Only powered tubes hum, and the hum follows the tube's real brightness.",
                volume = 1f, minDistance = 0.7f, maxDistance = 13f, rolloff = 1.15f, spatialBlend = 1f,
                paramAName = "Tone variation", paramADesc = "How different neighbouring fixtures sound (level, buzz and a tiny detune).",
                paramA = 1f, paramAMin = 0f, paramAMax = 2f,
            });
            Register(new SoundDef
            {
                id = SoundIds.FluorescentStrike, name = "Fluorescent strike-on", category = "Lights", positional = true,
                description = "The tick and ping of a tube starting. Plays when a fixture actually strikes on, so it is in time with the light.",
                volume = 1f, minDistance = 0.8f, maxDistance = 16f, rolloff = 1.1f, spatialBlend = 1f,
            });
            Register(new SoundDef
            {
                id = SoundIds.FaultyBuzz, name = "Faulty tube buzz", category = "Lights", positional = true,
                description = "Crackling buzz and ticks from a failing tube. Driven by the tube's own stutter, so sound and flicker match.",
                volume = 1f, minDistance = 0.7f, maxDistance = 14f, rolloff = 1.1f, spatialBlend = 1f,
                paramAName = "Crackle", paramADesc = "How much the buzz crackles and ticks while the tube stutters (does not change when it stutters).",
                paramA = 1f, paramAMin = 0f, paramAMax = 2f,
            });
            Register(new SoundDef
            {
                id = SoundIds.BuildingHum, name = "Distant building hum", category = "Ambience", positional = false, hasPitch = false,
                description = "The combined hum of powered fixtures that are too far away for their own voices. Rises in big lit halls, fades in dark areas.",
                volume = 1f, spatialBlend = 0f,
            });
            Register(new SoundDef
            {
                id = SoundIds.Ventilation, name = "Ventilation", category = "Ambience", positional = false, hasPitch = true,
                description = "Air moving through ducts and diffusers. Louder and broader in tall halls, closer and duller in small offices.",
                volume = 0.5f, spatialBlend = 0f,
                paramAName = "Airflow drift", paramADesc = "How much the airflow slowly rises and falls.",
                paramA = 1f, paramAMin = 0f, paramAMax = 2f,
            });
            Register(new SoundDef
            {
                id = SoundIds.RoomTone, name = "Room tone", category = "Ambience", positional = false, hasPitch = false,
                description = "The quiet, dark noise floor of an empty building.",
                volume = 0.6f, spatialBlend = 0f,
            });
            Register(new SoundDef
            {
                id = SoundIds.Footsteps, name = "Footsteps (carpet)", category = "Player", positional = true,
                description = "Your own steps on office carpet: a soft thud and fabric scuff, heavier when running.",
                volume = 1f, minDistance = 0.5f, maxDistance = 8f, rolloff = 1f, spatialBlend = 0.4f,
                paramAName = "Step variation", paramADesc = "How much each step differs from the last.",
                paramA = 1f, paramAMin = 0f, paramAMax = 2f,
            });
        }
    }
}
