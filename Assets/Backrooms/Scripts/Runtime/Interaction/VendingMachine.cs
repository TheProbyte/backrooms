using System.Collections;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A drinks vending machine's working parts (2026-11-04; the machine itself is in the chunk's meshes, placed by
    /// SpecialRooms.TryVending and built by OfficeChunkBuilder.Vending). With power its sign and buttons glow
    /// (VendingGlow, backlit: the troffer lens's emissive material with the sign's print), a soft red-white light falls on
    /// the floor in front of it, and it hums: the evaporator fan all the time, the compressor and condenser fan cutting in
    /// and out with a relay's click (a refrigerated vendor's cycle), the sign's tubes buzzing faintly, now and then a tube
    /// flickering. Power: its own circuit (variant bit 0), else the district's breaker (PowerGrid); unplugged (bit 1),
    /// never. Press its buttons (IPokeTarget): a beep; most buttons whirr and drop a can into the bin with a thud, two are
    /// sold out (two beeps). Frame: origin on the floor at the middle of its foot, +Z its front.
    /// </summary>
    public class VendingMachine : MonoBehaviour, IPokeTarget
    {
        const float Front = 0.42f;
        static readonly Vector3 Column = new Vector3(-0.335f, 1.2f, Front);
        const float ButtonZ0 = 1.5f, ButtonDz = 0.068f;
        static readonly bool[] SoldOut = { false, false, true, false, false, false, true, false };

        bool _own, _unplugged, _powered, _compOn;
        Vector3Int _circuit;
        int _powerVersion = -1;
        Renderer _glow;
        Material _on, _off;
        Light _light;
        AudioSource _fan, _comp, _buzz, _fx;
        float _compTimer, _nextFlicker, _flickerUntil, _compLevel;
        int _button;
        System.Random _rng;
        bool _busy;

        static AudioClip s_fan, s_comp, s_buzz, s_click, s_beep, s_whirr, s_clunk;
        static Material s_on, s_off;

        public Vector3 PokeNormal => transform.forward;
        public Vector3 FocusPoint => transform.TransformPoint(Column + Vector3.forward * 0.02f);

        public void Init(int variant, uint renderMask, Vector3Int circuit)
        {
            _own = (variant & 1) != 0;
            _unplugged = (variant & 2) != 0;
            _circuit = circuit;
            _rng = new System.Random(variant ^ 0x5EED);
            _compTimer = 5f + (float)_rng.NextDouble() * 30f;
            _compOn = _rng.NextDouble() < 0.5;
            _nextFlicker = 20f + (float)_rng.NextDouble() * 120f;

            // The glowing faces: their own object, so they can go dark and flicker.
            var g = FittingModels.Spawn("VendingGlow", transform, renderMask, Vector3.zero, Quaternion.identity);
            if (g)
            {
                _glow = g.GetComponent<Renderer>();
                var baked = _glow ? _glow.sharedMaterial : null;
                var tex = baked && baked.HasProperty("_BaseMap") ? baked.GetTexture("_BaseMap") as Texture2D : null;
                MakeMaterials(tex, baked);
            }
            // The light it throws: a wide spot out of its front, red-white like its sign, on its room only.
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(-0.1f, 1.15f, Front + 0.12f);
            lg.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.spotAngle = 150f; _light.innerSpotAngle = 70f;
            _light.range = 4.2f;
            _light.color = new Color(1f, 0.7f, 0.66f);
            _light.shadows = LightShadows.None;
            _light.renderingLayerMask = (int)renderMask;
            _light.enabled = false;

            // Its sounds, from inside it.
            MakeClips();
            _fan = Loop("Fan", s_fan, new Vector3(0f, 0.3f, -0.2f), 0.18f, 10f);
            _comp = Loop("Compressor", s_comp, new Vector3(0.2f, 0.15f, -0.3f), 0f, 14f);
            _buzz = Loop("Buzz", s_buzz, new Vector3(-0.1f, 1.1f, Front - 0.05f), 0.035f, 5f);
            _fx = ItemModels.Source(gameObject, 12f);

            // What the fingers find: the button column.
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = Column + new Vector3(0f, 0f, 0.03f);
            bc.size = new Vector3(0.3f, 0.75f, 0.08f);
            Apply(true);
        }

        AudioSource Loop(string name, AudioClip clip, Vector3 at, float volume, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            var a = ItemModels.Source(go, range);
            a.clip = clip; a.loop = true; a.volume = volume; a.minDistance = 0.8f;
            a.timeSamples = clip ? _rng.Next(clip.samples) : 0;
            return a;
        }

        void MakeMaterials(Texture2D tex, Material baked)
        {
            if (!s_on)
            {
                var src = MaterialMaps.TrofferOn;
                if (src)
                {
                    s_on = new Material(src) { name = "Vending sign (lit)", hideFlags = HideFlags.DontSave };
                    s_on.SetTexture("_BaseMap", tex); s_on.SetTexture("_EmissionMap", tex);
                    s_on.SetTextureScale("_BaseMap", Vector2.one); s_on.SetTextureOffset("_BaseMap", Vector2.zero);
                    s_on.SetColor("_BaseColor", Color.white);
                    s_on.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f));
                }
                s_off = baked ? baked : s_on;
            }
            _on = s_on ? s_on : baked;
            _off = s_off ? s_off : baked;
        }

        void SetGlow(Material m)
        {
            if (!_glow || !m) return;
            var mats = _glow.sharedMaterials;
            if (mats.Length > 0 && mats[0] == m) return;
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            _glow.sharedMaterials = mats;
        }

        void Apply(bool force)
        {
            bool p = !_unplugged && (_own || PowerGrid.On(_circuit));
            if (!force && p == _powered) return;
            _powered = p;
            SetGlow(p ? _on : _off);
            if (_light) _light.enabled = p;
            if (p) { _fan.Play(); _buzz.Play(); if (_compOn) _comp.Play(); }
            else { _fan.Stop(); _buzz.Stop(); _comp.Stop(); }
        }

        void Update()
        {
            if (PowerGrid.Version != _powerVersion) { _powerVersion = PowerGrid.Version; Apply(false); }
            if (!_powered) return;
            // (Made while its chunk was still hidden: a sound asked to play then did not start.)
            if (!_fan.isPlaying) _fan.Play();
            if (!_buzz.isPlaying) _buzz.Play();
            float dt = Time.deltaTime;
            // The compressor's cycle: on 40-90 s, off 25-60 s, a relay's click each way, easing in and out.
            _compTimer -= dt;
            if (_compTimer <= 0f)
            {
                _compOn = !_compOn;
                _compTimer = _compOn ? 40f + (float)_rng.NextDouble() * 50f : 25f + (float)_rng.NextDouble() * 35f;
                _fx.pitch = 1f;
                _fx.PlayOneShot(s_click, 0.5f);
                if (_compOn && !_comp.isPlaying) _comp.Play();
            }
            _compLevel = Mathf.MoveTowards(_compLevel, _compOn ? 1f : 0f, dt * (_compOn ? 0.6f : 1.5f));
            _comp.volume = 0.32f * _compLevel;
            _comp.pitch = 0.94f + 0.06f * _compLevel;
            if (_compLevel <= 0f && _comp.isPlaying) _comp.Stop();
            // Now and then a tube behind the sign flickers.
            _nextFlicker -= dt;
            if (_nextFlicker <= 0f)
            {
                _nextFlicker = 30f + (float)_rng.NextDouble() * 150f;
                _flickerUntil = Time.time + 0.25f + (float)_rng.NextDouble() * 0.6f;
            }
            bool dark = Time.time < _flickerUntil && Mathf.PerlinNoise(Time.time * 23f, 3.1f) < 0.45f;
            SetGlow(dark ? _off : _on);
            if (_light) _light.intensity = dark ? 0.2f : 1.4f;
        }

        /// <summary>The finger comes to one of its buttons and presses it in.</summary>
        public void PokeStroke(out Vector3 from, out Vector3 to)
        {
            if (!_busy) _button = _rng.Next(SoldOut.Length);
            var b = new Vector3(Column.x, ButtonZ0 - _button * ButtonDz, Front - 0.008f);
            from = transform.TransformPoint(b + Vector3.forward * 0.035f);
            to = transform.TransformPoint(b - Vector3.forward * 0.004f);
        }

        public void Press()
        {
            if (!_fx || _busy) return;
            if (!_powered) { _fx.pitch = 1f; _fx.PlayOneShot(s_click, 0.25f); return; }
            StartCoroutine(Vend(_button));
        }

        IEnumerator Vend(int button)
        {
            _busy = true;
            _fx.pitch = 1f;
            _fx.PlayOneShot(s_beep, 0.45f);
            yield return new WaitForSeconds(0.25f);
            if (SoldOut[button])
            {
                _fx.PlayOneShot(s_beep, 0.45f);
                yield return new WaitForSeconds(0.35f);
                _fx.PlayOneShot(s_beep, 0.45f);
            }
            else
            {
                _fx.PlayOneShot(s_whirr, 0.6f);
                yield return new WaitForSeconds(0.9f);
                _fx.pitch = 0.9f + (float)_rng.NextDouble() * 0.2f;
                _fx.PlayOneShot(s_clunk, 0.9f);
            }
            yield return new WaitForSeconds(0.5f);
            _busy = false;
        }

        // ---------------- Sounds (made once, procedurally) ----------------

        const int Rate = 44100;

        static AudioClip Clip(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }

        static void MakeClips()
        {
            if (s_fan) return;
            var r = new System.Random(77);
            float N() => (float)(r.NextDouble() * 2.0 - 1.0);
            // A loop that joins: the last second crossfaded into the first.
            float[] Looped(float[] d, int fade)
            {
                int n = d.Length - fade;
                var o = new float[n];
                for (int i = 0; i < n; i++) o[i] = d[i];
                for (int i = 0; i < fade; i++) { float t = i / (float)fade; o[i] = d[i] * t + d[n + i] * (1f - t); }
                return o;
            }
            // The evaporator fan: soft air through the coil (low-passed noise), its motor's 120 Hz and a little 240.
            {
                int n = Rate * 5; var d = new float[n]; float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.06f; lp2 += (lp - lp2) * 0.2f;
                    float wob = 1f + 0.08f * Mathf.Sin(t * 2f * Mathf.PI * 0.8f);
                    d[i] = (lp2 * 1.4f + 0.05f * Mathf.Sin(t * 2f * Mathf.PI * 120f) + 0.02f * Mathf.Sin(t * 2f * Mathf.PI * 240f)) * wob;
                }
                s_fan = Clip("Vending fan", Looped(d, Rate));
            }
            // The compressor: mains hum (60 Hz and its harmonics), a rattle in its mounts, the condenser fan's rush.
            {
                int n = Rate * 5; var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float hum = 0.32f * Mathf.Sin(t * 2f * Mathf.PI * 60f) + 0.2f * Mathf.Sin(t * 2f * Mathf.PI * 120f + 0.4f)
                              + 0.09f * Mathf.Sin(t * 2f * Mathf.PI * 180f + 1.1f) + 0.04f * Mathf.Sin(t * 2f * Mathf.PI * 300f);
                    lp += (N() - lp) * 0.12f;
                    float rattle = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 2f * Mathf.PI * 60f)), 12f) * N() * 0.12f;
                    d[i] = hum * 0.6f + lp * 0.5f + rattle;
                }
                s_comp = Clip("Vending compressor", Looped(d, Rate));
            }
            // The sign's ballast and tubes: a thin 120 Hz buzz with its overtones.
            {
                int n = Rate * 3; var d = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float s = Mathf.Sin(t * 2f * Mathf.PI * 120f);
                    d[i] = 0.5f * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.3f) * 0.3f + 0.05f * N();
                }
                s_buzz = Clip("Vending buzz", Looped(d, Rate / 2));
            }
            // A relay's click.
            {
                int n = Rate / 20; var d = new float[n];
                for (int i = 0; i < n; i++) d[i] = N() * Mathf.Exp(-i / (Rate * 0.003f)) * 0.8f;
                s_click = Clip("Vending relay", d);
            }
            // A button's beep: a square-ish 1.5 kHz tone, 90 ms.
            {
                int n = (int)(Rate * 0.09f); var d = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Min(1f, i / 200f) * Mathf.Min(1f, (n - i) / 300f);
                    d[i] = Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * 1500f)) * 0.25f * env;
                }
                s_beep = Clip("Vending beep", d);
            }
            // The vend motor: a gear motor spinning up and down, 0.9 s.
            {
                int n = (int)(Rate * 0.9f); var d = new float[n]; float ph = 0f, lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float f = 70f + 60f * Mathf.Sin(t * Mathf.PI);
                    ph += f / Rate;
                    lp += (N() - lp) * 0.3f;
                    float saw = 2f * (ph - Mathf.Floor(ph + 0.5f));
                    d[i] = (saw * 0.35f + lp * 0.2f) * Mathf.Sin(t * Mathf.PI);
                }
                s_whirr = Clip("Vending motor", d);
            }
            // The can landing in the bin: a dull thud, a tinny ring, a short roll.
            {
                int n = (int)(Rate * 0.9f); var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float thud = Mathf.Sin(t * 2f * Mathf.PI * 75f) * Mathf.Exp(-t * 18f) * 0.9f;
                    float ring = Mathf.Sin(t * 2f * Mathf.PI * 1180f) * Mathf.Exp(-t * 30f) * 0.25f + Mathf.Sin(t * 2f * Mathf.PI * 2630f) * Mathf.Exp(-t * 45f) * 0.12f;
                    lp += (N() - lp) * 0.25f;
                    float roll = t > 0.12f ? lp * 0.25f * Mathf.Exp(-(t - 0.12f) * 4f) * (0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 9f)) : 0f;
                    d[i] = thud + ring + roll + (i < 300 ? N() * 0.5f * (1f - i / 300f) : 0f);
                }
                s_clunk = Clip("Vending can", d);
            }
        }
    }
}
