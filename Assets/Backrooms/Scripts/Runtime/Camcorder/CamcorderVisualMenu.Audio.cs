using System.Collections.Generic;
using System.Text;
using Backrooms.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > AUDIO: output, the camcorder recording chain, audio presets, live audio state and developer
    /// tuning. F5 > SOUND: searchable catalog of every registered sound type with global per-sound
    /// overrides (volume, mute, pitch, distances, attenuation, spatial blend, sound-specific values),
    /// explicit preview / stop, per-sound reset and reset-all.
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        Text _auReadout;
        float _auNextReadout;
        int _auSourceCount; float _auNextCount;

        // Sound tab state.
        string _sndSelected;
        InputField _sndSearch;
        readonly List<(SoundDef def, RectTransform row, Text label)> _sndRows = new List<(SoundDef, RectTransform, Text)>();
        Text _sndTitle, _sndDesc, _sndPreviewLabel;
        RectTransform _sndDetail;
        readonly List<(RectTransform row, System.Func<SoundDef, bool> applies)> _sndFieldRows = new List<(RectTransform, System.Func<SoundDef, bool>)>();
        Text _sndParamALabel, _sndParamBLabel;

        CamcorderAudioChain Chain => rig ? rig.Audio : null;
        static BuildingAudio BA => BuildingAudio.Instance;

        // ---------------- AUDIO ----------------

        void BuildAudio()
        {
            CamcorderSettings S() => rig.Settings;

            Section("LIVE AUDIO", "What the building sound and the microphone are doing now.");
            var readRow = Rect("AudioReadout", _list);
            readRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 150;
            readRow.gameObject.AddComponent<Canvas>();
            _auReadout = Label(readRow, "", 14, TextCol, TextAnchor.UpperLeft);
            Stretch(_auReadout.rectTransform);
            _auReadout.supportRichText = true;

            Section("AUDIO PRESETS", "Mic character for each camcorder look. Only changes audio.");
            var presets = Rect("AudioPresets", _list);
            presets.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var hl = presets.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childForceExpandWidth = true; hl.childControlWidth = true; hl.childControlHeight = true;
            for (int i = 0; i < CamcorderSettings.PresetNames.Length; i++)
            {
                int idx = i;
                Button(presets, CamcorderSettings.PresetNames[i], () =>
                {
                    var s = rig.Settings.Clone(); s.CopyAudioFrom(CamcorderSettings.Preset(idx)); rig.SetSettings(s); RefreshAll();
                });
            }

            Section("OUTPUT", "Each sound, then walls and rooms, then the camcorder, then volume.");
            Slider("Output volume", "Final volume of everything, after the camcorder processing.",
                0f, 1f, "0%", "", () => S().audioVolume, v => S().audioVolume = v);
            Toggle("Camcorder processing", "Runs all sound through the camcorder's microphone and recording circuit (colour, auto gain, compression, hiss). Off gives the clean sound of the world, with room acoustics still on.",
                () => S().audioProcessing, v => S().audioProcessing = v);
            Toggle("Compare: hear unprocessed (not saved)", "Temporarily bypasses the camcorder processing so you can compare, without changing any of your settings. Turns itself off when you close the panel.",
                () => Chain && Chain.compareUnprocessed, v => { if (Chain) Chain.compareUnprocessed = v; });

            Section("MICROPHONE", "The small built-in stereo mic.");
            Slider("Microphone coloration", "Bass cut and softened highs from the tiny capsule and cheap preamp. 0 is a flat, clean microphone.",
                0f, 1f, "0.00", "", () => S().micColoration, v => S().micColoration = v);
            Slider("Stereo width", "How wide the stereo image is. Built-in camcorder mics sit close together, so they sound narrower than real life. 0 is mono, 1 natural.",
                0f, 1.5f, "0.00", "", () => S().stereoWidth, v => S().stereoWidth = v);
            Slider("Hiss level", "The microphone's own noise. The auto gain raises it in quiet rooms, like real tape.",
                0f, 1f, "0.00", "", () => S().micHiss, v => S().micHiss = v);
            Slider("Handling noise", "Low rumble and soft thumps from holding the camera while walking.",
                0f, 1f, "0.00", "", () => S().handlingNoise, v => S().handlingNoise = v);

            Section("AUTO GAIN", "Rises slowly in quiet places, ducks quickly for loud sounds.");
            Slider("Auto-gain strength", "How much the camcorder adjusts its own recording level. 0 is a fixed level. Higher values bring up faint building sound (and hiss) when you stand still in a quiet room.",
                0f, 1f, "0.00", "", () => S().autoGain, v => S().autoGain = v);

            Section("DEVELOPER", "Advanced tuning, saved separately from presets.");
            AudioDevSettings D() => BA ? BA.Dev : _devFallback;
            Slider("Hum voice budget", "Fixtures that get their own positioned hum voice. The rest are summed into the distant building hum.",
                0f, 24f, "0", "", () => D().emitterBudget, v => D().emitterBudget = Mathf.RoundToInt(v), true);
            Slider("Candidate radius", "Fixtures within this distance are considered for a hum voice.",
                6f, 30f, "0.0", " m", () => D().candidateRadius, v => D().candidateRadius = v);
            Slider("Voice hysteresis", "A voiced fixture keeps its voice until it is this much quieter than the weakest selected one. Lower values swap voices less often.",
                0.2f, 1f, "0.00", "", () => D().hysteresis, v => D().hysteresis = v);
            Slider("Voice fade in", "Seconds for a newly assigned hum voice to fade in.", 0.1f, 3f, "0.00", " s", () => D().fadeIn, v => D().fadeIn = v);
            Slider("Voice fade out", "Seconds for a retired hum voice to fade out.", 0.1f, 3f, "0.00", " s", () => D().fadeOut, v => D().fadeOut = v);
            Slider("Wall occlusion", "How much walls muffle sound and push it to the doorway it comes through. 0 turns it off.",
                0f, 1f, "0.00", "", () => D().occlusion, v => D().occlusion = v);
            Slider("Room reverb", "Amount of room reverb. Small carpeted offices stay dry; tall plaster halls ring.",
                0f, 2f, "0.00", "x", () => D().reverb, v => D().reverb = v);
            Slider("Fixture density limit", "Hum loudness above which many fixtures are gently compressed, so big lit halls aren't loud.",
                0.2f, 1.5f, "0.00", "", () => D().densityReference, v => D().densityReference = v);
            Slider("Ambience beds", "Master level for distant hum, ventilation and room tone.",
                0f, 2f, "0.00", "x", () => D().ambience, v => D().ambience = v);
            Slider("AGC target", "Level the auto gain aims for.", -45f, -15f, "0", " dBFS", () => D().agcTargetDb, v => D().agcTargetDb = v);
            Slider("AGC max boost", "Most the auto gain may raise quiet sound (at full strength).", 0f, 24f, "0", " dB", () => D().agcMaxBoostDb, v => D().agcMaxBoostDb = v);
            Slider("AGC attack", "Seconds to duck for a louder sound.", 0.02f, 1f, "0.00", " s", () => D().agcAttack, v => D().agcAttack = v);
            Slider("AGC release", "Seconds to recover after it gets quiet again.", 0.5f, 10f, "0.0", " s", () => D().agcRelease, v => D().agcRelease = v);
            Slider("Compression", "Gentle level compression after the auto gain.", 0f, 1f, "0.00", "", () => D().compression, v => D().compression = v);
            Slider("Saturation", "Soft analogue saturation of loud peaks.", 0f, 1f, "0.00", "", () => D().saturation, v => D().saturation = v);
            var devRow = Rect("DevButtons", _list);
            devRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var dh = devRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            dh.spacing = 6; dh.childForceExpandWidth = true; dh.childControlWidth = true; dh.childControlHeight = true;
            Button(devRow, "Reset developer audio", () =>
            {
                if (BA) { var fresh = new AudioDevSettings(); JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), BA.Dev); BA.Dev.Save(); }
                RefreshAll();
            });
        }

        readonly AudioDevSettings _devFallback = new AudioDevSettings();
        bool _auOpened;      // only save developer audio after the panel has actually been open

        void UpdateAudioTab()
        {
            if (!IsOpen || _tab != 3 || !_auReadout || Time.unscaledTime < _auNextReadout) return;
            _auNextReadout = Time.unscaledTime + 0.25f;
            if (Time.unscaledTime >= _auNextCount)
            {
                _auNextCount = Time.unscaledTime + 1f;
                _auSourceCount = 0;
                foreach (var s in FindObjectsByType<AudioSource>()) if (s.isPlaying) _auSourceCount++;
            }
            var sb = new StringBuilder();
            var ba = BA;
            if (ba)
            {
                sb.Append("<color=#e8c45c>Fixture hum</color>  ").Append(ba.ActiveEmitters).Append(" voiced / ").Append(ba.Dev.emitterBudget)
                  .Append(" budget  (").Append(ba.FadingEmitters).Append(" fading, pool ").Append(ba.PoolSize).Append(")\n");
                sb.Append("Powered fixtures in range  ").Append(ba.Candidates).Append("   voice swaps ").Append(ba.Reassignments).Append('\n');
                sb.Append("Hum loudness  ").Append(ba.HumLoudness.ToString("0.00")).Append("   density comp ").Append(Db(ba.DensityComp))
                  .Append("   distant bed ").Append(ba.DistantLevel.ToString("0.00")).Append('\n');
                sb.Append("Faulty buzz voices  ").Append(ba.BuzzActive).Append("/2   one-shots playing ").Append(ba.EventsPlaying).Append("/4\n");
                sb.Append("<color=#e8c45c>Room</color>  ").Append(ba.SpaceLabel).Append("\nReverb decay ").Append(ba.ReverbDecay.ToString("0.00"))
                  .Append(" s   acoustic map ").Append(ba.Field.RebuildMs.ToString("0.0")).Append(" ms\n");
            }
            else sb.Append("No building audio in this scene.\n");
            var ch = Chain;
            if (ch)
            {
                sb.Append("<color=#e8c45c>Camcorder</color>  ").Append(ch.ProcessedMix > 0.5f ? "processed" : "<color=#ff9c6b>unprocessed</color>")
                  .Append("   in ").Append(ch.InputDb.ToString("0")).Append(" dBFS   AGC ").Append(ch.GainDb >= 0 ? "+" : "").Append(ch.GainDb.ToString("0.0"))
                  .Append(" dB   out ").Append(ch.OutputDb.ToString("0")).Append(" dBFS\n");
            }
            sb.Append("Audio sources playing  ").Append(_auSourceCount);
            _auReadout.text = sb.ToString();
        }

        static string Db(float lin) => (20f * Mathf.Log10(Mathf.Max(lin, 1e-4f))).ToString("0.0") + " dB";

        // ---------------- SOUND ----------------

        void BuildSound()
        {
            Section("SOUND CATALOG", "Every sound in the game. Edits apply everywhere and are saved.");
            var searchRow = Rect("Search", _list);
            searchRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            _sndSearch = InputBox(searchRow, "");
            _sndSearch.contentType = InputField.ContentType.Standard;
            Stretch((RectTransform)_sndSearch.transform);
            var ph = Label((RectTransform)_sndSearch.transform, "Search sounds or categories...", 15, Dim, TextAnchor.MiddleLeft);
            Stretch(ph.rectTransform); ph.rectTransform.offsetMin = new Vector2(8, 0); ph.fontStyle = FontStyle.Italic;
            _sndSearch.placeholder = ph;
            _sndSearch.onValueChanged.AddListener(_ => FilterSounds());

            string lastCat = null;
            foreach (var d in SoundCatalog.All)
            {
                if (d.category != lastCat)
                {
                    lastCat = d.category;
                    var cat = Label(_list, d.category.ToUpperInvariant(), 13, Dim, TextAnchor.LowerLeft);
                    cat.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
                    _sndRows.Add((new SoundDef { id = null, category = d.category, name = "" }, cat.rectTransform, cat));
                }
                var def = d;
                var img = Button(_list, "", () => SelectSound(def.id));
                img.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
                var lbl = img.GetComponentInChildren<Text>();
                lbl.alignment = TextAnchor.MiddleLeft; lbl.rectTransform.offsetMin = new Vector2(10, 0);
                lbl.supportRichText = true;
                _sndRows.Add((def, img.rectTransform, lbl));
            }

            // Detail of the selected sound.
            _sndDetail = Rect("Detail", _list);
            var dl = _sndDetail.gameObject.AddComponent<LayoutElement>(); dl.preferredHeight = 70;
            _sndTitle = Label(_sndDetail, "", 18, Accent, TextAnchor.LowerLeft);
            Place(_sndTitle.rectTransform, 0, -6, 0, 26);
            _sndDesc = Label(_sndDetail, "", 13, Dim, TextAnchor.UpperLeft);
            Place(_sndDesc.rectTransform, 0, -34, 0, 36);

            string Id() => _sndSelected;
            void Field(RectTransform row, System.Func<SoundDef, bool> applies) => _sndFieldRows.Add((row, applies));
            RectTransform LastRow() => (RectTransform)_list.GetChild(_list.childCount - 1);

            Toggle("Mute", "Silences this sound everywhere without losing its other settings.",
                () => Id() != null && SoundCatalog.Muted(Id()), v => { if (Id() != null) Edited(SoundField.Mute, v ? 1f : 0f); });
            Field(LastRow(), d => true);
            Slider("Volume", "Level multiplier for this sound type (Reset restores its default). Power, distance and walls still apply on top, so an unpowered fixture stays silent.",
                0f, 3f, "0.00", "x", () => Id() != null ? SoundCatalog.RawVolume(Id()) : 1f, v => Edited(SoundField.Volume, v));
            Field(LastRow(), d => true);
            Slider("Pitch", "Playback pitch (1 = original). Timing of events never changes; for the hum this shifts the mains tone.",
                0.5f, 1.5f, "0.000", "x", () => Id() != null ? SoundCatalog.Pitch(Id()) : 1f, v => Edited(SoundField.Pitch, v));
            Field(LastRow(), d => d.hasPitch);
            Slider("Min distance", "Within this distance the sound is at full level.",
                0.1f, 5f, "0.00", " m", () => Id() != null ? SoundCatalog.MinDistance(Id()) : 1f, v => Edited(SoundField.MinDistance, v));
            Field(LastRow(), d => d.positional);
            Slider("Max distance", "Beyond this distance (measured along the path sound travels) the sound is silent.",
                2f, 40f, "0.0", " m", () => Id() != null ? SoundCatalog.MaxDistance(Id()) : 10f, v => Edited(SoundField.MaxDistance, v));
            Field(LastRow(), d => d.positional);
            Slider("Attenuation", "How fast the level falls with distance after the min distance. 1 is natural (inverse distance); higher falls off faster.",
                0.3f, 3f, "0.00", "", () => Id() != null ? SoundCatalog.Rolloff(Id()) : 1f, v => Edited(SoundField.Rolloff, v));
            Field(LastRow(), d => d.positional);
            Slider("Spatial blend", "1 = fully positioned in the world, 0 = flat stereo with no direction.",
                0f, 1f, "0.00", "", () => Id() != null ? SoundCatalog.SpatialBlend(Id()) : 1f, v => Edited(SoundField.SpatialBlend, v));
            Field(LastRow(), d => d.positional);
            var sa = Slider("Parameter A", "", 0f, 2f, "0.00", "", () => Id() != null ? SoundCatalog.ParamA(Id()) : 0f, v => Edited(SoundField.ParamA, v));
            _sndParamALabel = sa.transform.parent.GetComponentInChildren<Text>();
            Field(LastRow(), d => d.paramAName != null);
            var sbp = Slider("Parameter B", "", 0f, 2f, "0.00", "", () => Id() != null ? SoundCatalog.ParamB(Id()) : 0f, v => Edited(SoundField.ParamB, v));
            _sndParamBLabel = sbp.transform.parent.GetComponentInChildren<Text>();
            Field(LastRow(), d => d.paramBName != null);
            _sndParamA = sa; _sndParamB = sbp;

            var act = Rect("SoundActions", _list);
            act.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var ah = act.gameObject.AddComponent<HorizontalLayoutGroup>();
            ah.spacing = 6; ah.childForceExpandWidth = true; ah.childControlWidth = true; ah.childControlHeight = true;
            var pv = Button(act, "Preview", () => { if (BA && Id() != null) BA.Preview(Id()); });
            _sndPreviewLabel = pv.GetComponentInChildren<Text>();
            Button(act, "Stop", () => { if (BA) BA.StopPreview(); });
            Button(act, "Reset this sound", () => { if (Id() != null) { SoundCatalog.Reset(Id()); SoundCatalog.Save(); RefreshAll(); UpdateSoundLabels(); } });
            Field(act, d => true);

            var spacer = Rect("Spacer", _list); spacer.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
            var allRow = Rect("ResetAll", _list);
            allRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var alh = allRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            alh.childForceExpandWidth = true; alh.childControlWidth = true; alh.childControlHeight = true;
            Button(allRow, "Reset ALL sounds to defaults", () => { SoundCatalog.ResetAll(); RefreshAll(); UpdateSoundLabels(); });

            _refreshers.Add(UpdateSoundLabels);
            SelectSound(SoundCatalog.All.Count > 0 ? SoundCatalog.All[0].id : null);
        }

        UnityEngine.UI.Slider _sndParamA, _sndParamB;

        void Edited(SoundField f, float v)
        {
            if (_refreshing || _sndSelected == null) return;
            SoundCatalog.Set(_sndSelected, f, v);
            _sndDirty = true;
            UpdateSoundLabels();
        }

        bool _sndDirty;

        void SelectSound(string id)
        {
            _sndSelected = id;
            var d = id != null ? SoundCatalog.Def(id) : null;
            foreach (var (row, applies) in _sndFieldRows) row.gameObject.SetActive(d != null && applies(d));
            if (d != null)
            {
                _sndTitle.text = d.name + "   <size=13><color=#9e998c>" + d.category + "  ·  " + (d.positional ? "positional" : "2D ambience") + "</color></size>";
                _sndTitle.supportRichText = true;
                _sndDesc.text = d.description;
                if (d.paramAName != null) { _sndParamALabel.text = d.paramAName; _sndParamA.minValue = d.paramAMin; _sndParamA.maxValue = d.paramAMax; }
                if (d.paramBName != null) { _sndParamBLabel.text = d.paramBName; _sndParamB.minValue = d.paramBMin; _sndParamB.maxValue = d.paramBMax; }
            }
            // Selecting never starts a preview.
            RefreshAll();
            UpdateSoundLabels();
        }

        void FilterSounds()
        {
            string q = _sndSearch ? _sndSearch.text.Trim().ToLowerInvariant() : "";
            var visibleCats = new HashSet<string>();
            foreach (var (def, row, _) in _sndRows)
            {
                if (def.id == null) continue;
                bool show = q.Length == 0 || def.name.ToLowerInvariant().Contains(q) || def.category.ToLowerInvariant().Contains(q) || def.id.Contains(q);
                row.gameObject.SetActive(show);
                if (show) visibleCats.Add(def.category);
            }
            foreach (var (def, row, _) in _sndRows) if (def.id == null) row.gameObject.SetActive(visibleCats.Contains(def.category));
        }

        void UpdateSoundLabels()
        {
            foreach (var (def, _, label) in _sndRows)
            {
                if (def.id == null) continue;
                bool sel = def.id == _sndSelected;
                string mark = SoundCatalog.IsModified(def.id) ? "  <color=#e8c45c>• edited</color>" : "";
                string mute = SoundCatalog.Muted(def.id) ? "  <color=#ff9c6b>muted</color>" : "";
                label.text = (sel ? "<color=#e8c45c>▶ </color>" : "   ") + def.name + mark + mute;
            }
            if (_sndPreviewLabel) _sndPreviewLabel.text = BA && BA.PreviewingId != null && BA.PreviewingId == _sndSelected ? "Previewing..." : "Preview";
        }

        /// <summary>Saves audio tuning when the panel closes and drops the temporary compare toggle.</summary>
        void CloseAudio()
        {
            if (Chain) Chain.compareUnprocessed = false;
            if (BA) { BA.StopPreview(); if (_auOpened) BA.Dev.Save(); }
            if (_sndDirty) { SoundCatalog.Save(); _sndDirty = false; }
        }
    }
}
