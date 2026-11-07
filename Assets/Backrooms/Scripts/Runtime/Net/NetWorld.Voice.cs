using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Net
{
    /// <summary>
    /// Co-op voice chat (2026-11-07): proximity voice as Lethal Company and the like have it. Your microphone is heard from
    /// your suit's head, near you, round walls through the doorways (Audio.SoundRoute, as the boombox) and muffled through
    /// them (Steam Audio); your suit's mouth moves as you speak (HazmatAppearance.talk), for you and for them.
    ///  - Capture: Unity's Microphone (the system's default device), resampled to 16 kHz mono; sent while you speak (a gate
    ///    on the level over the room's noise, held 0.35 s after) or while T is held (GameSettings.PushToTalk); off with
    ///    YOUR MICROPHONE OFF.
    ///  - On the wire: 60 ms packets of IMA ADPCM (VoiceCodec) with their level, unreliable (UDP when it gets through); the
    ///    host passes them on like a player's movement.
    ///  - Playback: one looping clip per player written just ahead of where it plays (VoiceOut): a 0.1 s cushion against
    ///    uneven arrival, silence on a gap, packets past 0.4 s ahead dropped so it never lags behind.
    /// </summary>
    public partial class NetWorld
    {
        const int VoiceRate = 16000, VoicePacket = 960;       // 60 ms
        const float GateHold = 0.35f;

        AudioClip _mic;
        string _micName;
        int _micRate, _micPos;
        float[] _micBuf = new float[4800];
        double _resampleAt;                                     // where in the device's samples the next 16 kHz one falls
        float _lastMicSample;
        readonly List<float> _voiceOut = new List<float>(VoicePacket * 2);
        float[] _packet = new float[VoicePacket];
        int _encIndex;
        float _noise = 0.004f, _gateUntil, _ownLevel;
        bool _talking;
        float _micRetryAt;

        readonly Dictionary<int, VoiceOut> _voices = new Dictionary<int, VoiceOut>();

        /// <summary>Tests (DebugTools.NetAutoTest, -voice-test): a buzzing test voice in bursts instead of the microphone.</summary>
        public static bool VoiceTestTone;
        double _tonePhase, _toneAt;
        int _packetsOut, _packetsIn;

        /// <summary>For tests: what has gone out and come in, and each voice's state.</summary>
        public static string VoiceReport()
        {
            var w = Instance;
            if (!w) return "no world";
            var sb = new System.Text.StringBuilder();
            sb.Append($"voice out {w._packetsOut} packets (talk {w._ownLevel:0.00}), in {w._packetsIn}");
            foreach (var kv in w._voices) sb.Append($"; player {kv.Key}: {kv.Value.Report()}");
            return sb.ToString();
        }

        /// <summary>How loud you are speaking now (0..1): your own suit's mouth.</summary>
        public static float OwnTalk => Instance ? Instance._ownLevel : 0f;

        void UpdateVoice()
        {
            UpdateCapture();
            foreach (var kv in _voices) kv.Value.Update(Avatar(kv.Key));
            if (_fpb) _fpb.SetTalk(_ownLevel);
        }

        // ---------------- Your microphone ----------------

        void UpdateCapture()
        {
            if (VoiceTestTone && NetSession.Active) { UpdateTestTone(); return; }
            bool want = GameSettings.MicOn && NetSession.Active;
            if (!want) { StopMic(); _ownLevel = Mathf.MoveTowards(_ownLevel, 0f, Time.deltaTime * 4f); return; }
            if (!_mic)
            {
                if (Time.unscaledTime < _micRetryAt) return;
                _micRetryAt = Time.unscaledTime + 5f;
                if (Microphone.devices.Length == 0) return;      // (none plugged in: looked for again in 5 s)
                _micName = null;
                Microphone.GetDeviceCaps(_micName, out int min, out int max);
                _micRate = max == 0 ? 48000 : Mathf.Clamp(48000, Mathf.Max(min, 8000), max);
                _mic = Microphone.Start(_micName, true, 1, _micRate);
                if (!_mic) { Debug.LogWarning("[Net] No microphone could be opened for voice chat."); return; }
                _micRate = _mic.frequency;
                _micPos = 0; _resampleAt = 0; _voiceOut.Clear();
                Debug.Log($"[Net] Voice chat: microphone open ({(_micName ?? Microphone.devices[0])}, {_micRate} Hz).");
            }
            int pos = Microphone.GetPosition(_micName);
            if (pos < 0) return;
            int len = _mic.samples, n = (pos - _micPos + len) % len;
            if (n == 0) return;
            if (_micBuf.Length < n) _micBuf = new float[n];
            // (In two reads when it wraps round the clip's end.)
            int first = Mathf.Min(n, len - _micPos);
            var tmp = new float[first];
            _mic.GetData(tmp, _micPos);
            System.Array.Copy(tmp, 0, _micBuf, 0, first);
            if (n > first) { var tmp2 = new float[n - first]; _mic.GetData(tmp2, 0); System.Array.Copy(tmp2, 0, _micBuf, first, n - first); }
            _micPos = pos;

            // Resample to 16 kHz (linear: speech has little above 8 kHz), measure, gate.
            double step = _micRate / (double)VoiceRate;
            float sum = 0f; int got = 0;
            var fresh = new List<float>(n / 2 + 2);
            while (_resampleAt < n)
            {
                int i = (int)_resampleAt;
                float f = (float)(_resampleAt - i);
                float a = i == 0 ? _lastMicSample : _micBuf[i - 1], b = _micBuf[i];
                float s = Mathf.Lerp(a, b, f);
                fresh.Add(s); sum += s * s; got++;
                _resampleAt += step;
            }
            _resampleAt -= n;
            _lastMicSample = _micBuf[n - 1];
            if (got == 0) return;
            float rms = Mathf.Sqrt(sum / got);
            // The room's noise: follows quiet stretches down quickly, loud ones up very slowly (a long speech must not
            // become the noise: about a minute of talking to rise most of the way).
            _noise = rms < _noise ? Mathf.Lerp(_noise, rms, 0.2f) : Mathf.Lerp(_noise, rms, 0.0004f);
            _noise = Mathf.Clamp(_noise, 0.0005f, 0.05f);
            bool ptt = GameSettings.PushToTalk;
            bool keyHeld = Keyboard.current != null && Keyboard.current.tKey.isPressed && !TypingNow();
            bool open = ptt ? keyHeld : rms > Mathf.Max(0.01f, _noise * 3f);
            if (open) _gateUntil = Time.unscaledTime + (ptt ? 0.05f : GateHold);
            bool sending = Time.unscaledTime < _gateUntil;
            float level = sending ? Mathf.Clamp01(rms * 7f) : 0f;
            _ownLevel = Mathf.Lerp(_ownLevel, level, level > _ownLevel ? 0.6f : 0.15f);
            if (sending) { _voiceOut.AddRange(fresh); _talking = true; }
            else if (_talking)
            {
                // The end of a phrase: what is left goes out, padded with silence.
                if (_voiceOut.Count > 0) while (_voiceOut.Count % VoicePacket != 0) _voiceOut.Add(0f);
                _talking = false;
            }
            while (_voiceOut.Count >= VoicePacket)
            {
                _voiceOut.CopyTo(0, _packet, 0, VoicePacket);
                _voiceOut.RemoveRange(0, VoicePacket);
                SendVoice(_packet);
            }
        }

        /// <summary>The test voice: 0.6 s of a 160 Hz buzz (speech's own range, with its overtones), 0.4 s of quiet.</summary>
        void UpdateTestTone()
        {
            double now = Time.unscaledTimeAsDouble;
            if (_toneAt <= 0 || now - _toneAt > 0.5) _toneAt = now;
            int n = (int)((now - _toneAt) * VoiceRate);
            if (n <= 0) return;
            _toneAt += n / (double)VoiceRate;
            bool on = (now % 1.0) < 0.6;
            for (int i = 0; i < n; i++)
            {
                _tonePhase = (_tonePhase + 160.0 / VoiceRate) % 1.0;
                _voiceOut.Add(on ? (float)((_tonePhase * 2.0 - 1.0) * 0.25) : 0f);
            }
            _ownLevel = Mathf.Lerp(_ownLevel, on ? 0.8f : 0f, 0.3f);
            if (!on && _voiceOut.Count > 0 && _voiceOut.Count < VoicePacket) _voiceOut.Clear();
            while (_voiceOut.Count >= VoicePacket)
            {
                _voiceOut.CopyTo(0, _packet, 0, VoicePacket);
                _voiceOut.RemoveRange(0, VoicePacket);
                bool loud = false;
                for (int i = 0; i < VoicePacket && !loud; i += 40) loud = _packet[i] != 0f;
                if (loud) SendVoice(_packet);
            }
        }

        /// <summary>A text field has the keyboard (the chat, a name): T is a letter then, not talk.</summary>
        static bool TypingNow() => GUIUtility.keyboardControl != 0;

        void SendVoice(float[] pcm)
        {
            float sum = 0f;
            for (int i = 0; i < pcm.Length; i++) sum += pcm[i] * pcm[i];
            byte level = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(sum / pcm.Length) * 7f * 255f), 0, 255);
            var data = VoiceCodec.Encode(pcm, pcm.Length, out short pred, out byte idx, ref _encIndex);
            var w = W(Msg.Voice);
            w.Byte((byte)Me);
            w.Byte(level);
            w.Short(pred);
            w.Byte(idx);
            w.Short((short)pcm.Length);
            w.Raw(data, 0, data.Length);
            NetSession.SendWorld(false);
            _packetsOut++;
        }

        void StopMic()
        {
            if (!_mic) return;
            Microphone.End(_micName);
            Destroy(_mic);
            _mic = null;
            _voiceOut.Clear(); _talking = false;
        }

        // ---------------- The others' voices ----------------

        void OnVoice(NetReader r)
        {
            int id = r.Byte();
            float level = r.Byte() / 255f;
            short pred = r.Short();
            byte idx = r.Byte();
            int count = Mathf.Clamp(r.Short(), 0, VoicePacket * 2);
            int bytes = (count + 1) / 2;
            var data = new byte[bytes];
            for (int i = 0; i < bytes; i++) data[i] = r.Byte();
            if (id == Me || !NetSession.Peers.ContainsKey(id)) return;
            _packetsIn++;
            if (!_voices.TryGetValue(id, out var v)) { v = new VoiceOut(id, transform); _voices[id] = v; }
            var pcm = new float[count];
            VoiceCodec.Decode(data, count, pred, idx, pcm);
            v.Push(pcm, level);
        }

        void DropVoice(int id)
        {
            if (_voices.TryGetValue(id, out var v)) { v.Dispose(); _voices.Remove(id); }
        }

        void StopVoice()
        {
            StopMic();
            foreach (var v in _voices.Values) v.Dispose();
            _voices.Clear();
        }

        /// <summary>
        /// One player's voice, at their head: a looping one-second clip written a little ahead of where it plays (as
        /// UniVoice's circular clip; a streamed clip's reader is asked for large blocks at uneven times, which starved a
        /// short buffer and chopped the speech). Each packet goes in at the write head; when the play head has caught up
        /// (the start of a phrase, or a late packet) the write head is set 0.1 s ahead of it again (the jitter cushion);
        /// more than 0.4 s ahead and the packet is dropped so the voice stays live. What has been played is cleared, so
        /// the loop never plays old speech again.
        /// </summary>
        sealed class VoiceOut
        {
            readonly GameObject _go;
            readonly AudioSource _src;
            readonly AudioClip _clip;
            readonly float[] _zeros = new float[VoiceRate];
            long _writeAbs, _playAbs;             // where they are in samples since it started (not wrapped round the loop)
            int _lastPlay;
            float _level, _levelAt;
            int _pushed, _late, _dropped;
            float _peak;

            const int Len = VoiceRate, Cushion = VoiceRate / 10, MaxAhead = VoiceRate * 2 / 5;

            public VoiceOut(int id, Transform parent)
            {
                _go = new GameObject("Voice " + id);
                _go.transform.SetParent(parent, false);
                _src = Interaction.ItemModels.Source(_go, 30f);
                _src.minDistance = 1.2f;
                _src.loop = true;
                _clip = AudioClip.Create("Voice " + id, Len, 1, VoiceRate, false);
                _clip.SetData(_zeros, 0);
                _src.clip = _clip;
                _src.volume = GameSettings.Voices;
                _src.Play();
            }

            /// <summary>How far the write head is ahead of the play head (samples; below 0: it has been passed).</summary>
            long Ahead => _writeAbs - _playAbs;

            public void Push(float[] pcm, float level)
            {
                if (!_src) return;
                SyncPlay();
                long ahead = Ahead;
                if (ahead < VoiceRate / 50 || ahead > Len / 2)
                {
                    // Caught up (a new phrase, or this packet came late; or a long hitch lost count of the loop): start
                    // again a cushion ahead.
                    if (_pushed > 0 && ahead > -VoiceRate / 10) _late++;
                    _writeAbs = _playAbs + Cushion;
                }
                else if (ahead > MaxAhead) { _dropped++; return; }
                Write(pcm, (int)(_writeAbs % Len));
                _writeAbs += pcm.Length;
                for (int i = 0; i < pcm.Length; i++) if (pcm[i] > _peak) _peak = pcm[i];
                _level = Mathf.Max(_level, level);
                _levelAt = Time.unscaledTime;
                _pushed++;
            }

            void Write(float[] data, int at)
            {
                int first = Mathf.Min(data.Length, Len - at);
                if (first == data.Length) { _clip.SetData(data, at); return; }
                var a = new float[first]; System.Array.Copy(data, 0, a, 0, first); _clip.SetData(a, at);
                var b = new float[data.Length - first]; System.Array.Copy(data, first, b, 0, b.Length); _clip.SetData(b, 0);
            }

            /// <summary>Moves the play head on by what has played since last time, and clears that (the loop must not say it
            /// twice).</summary>
            void SyncPlay()
            {
                int play = _src.timeSamples;
                int passed = ((play - _lastPlay) % Len + Len) % Len;
                _playAbs += passed;
                if (passed > 0 && passed < Len / 2)
                {
                    var z = passed == _zeros.Length ? _zeros : new float[passed];
                    int first = Mathf.Min(passed, Len - _lastPlay);
                    if (first == passed) _clip.SetData(z, _lastPlay);
                    else { _clip.SetData(new float[first], _lastPlay); _clip.SetData(new float[passed - first], 0); }
                }
                _lastPlay = play;
            }

            public void Update(RemotePlayer avatar)
            {
                if (!_go || !_src) return;
                if (avatar)
                {
                    // At their head (its own object, a child of it), then routed round walls through the doorways
                    // (Audio.SoundRoute takes its place under its parent when added, so it is added once it is there).
                    var anchor = avatar.VoiceAnchor;
                    if (_go.transform.parent != anchor)
                    {
                        var old = _go.GetComponent<Audio.SoundRoute>();
                        if (old) Object.DestroyImmediate(old);
                        _go.transform.SetParent(anchor, false);
                        _go.transform.localPosition = Vector3.zero;
                        Audio.SoundRoute.Add(_src);
                    }
                }
                if (!_src.isPlaying) _src.Play();
                SyncPlay();
                _src.volume = GameSettings.Voices;
                // The mouth: the packets' level, falling away when they stop.
                if (Time.unscaledTime - _levelAt > 0.12f) _level = Mathf.MoveTowards(_level, 0f, Time.deltaTime * 5f);
                if (avatar) avatar.SetTalk(_level);
            }

            public string Report() =>
                $"{_pushed} packets, {Ahead * 1000 / VoiceRate} ms ahead, late {_late}, dropped {_dropped}, peak {_peak:0.00}, mouth {_level:0.00}, playing {(_src && _src.isPlaying)}, at {(_go ? _go.transform.parent.name : "-")}";

            public void Dispose()
            {
                if (_src) _src.Stop();
                if (_clip) Object.Destroy(_clip);
                if (_go) Object.Destroy(_go);
            }
        }
    }
}
