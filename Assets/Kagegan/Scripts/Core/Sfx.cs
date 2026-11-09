using UnityEngine;

namespace Kagegan
{
    public enum SfxId
    {
        Hit, HeavyHit, Whoosh, Block, Parry, Explosion, Fire, Thunder, Energy, Poof, Step,
        Absorb, Release, Teleport, Crumble, Roar, Click, Clash, Ko, Hiss, Charge, Land,
        WireSwing, WireCrack, WireZap, Count
    }

    /// <summary>
    /// All audio is synthesised at startup (offline game, zero audio assets to ship).
    /// Short one-shots are generated at 22 kHz mono; music loops are generated lazily per theme.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        const int Rate = 22050;
        static Sfx inst;
        AudioClip[] clips;
        AudioSource[] voices;
        int nextVoice;
        AudioSource music, musicB, ambience;
        AudioClip[] themes = new AudioClip[4];
        int currentTheme = -1;
        string currentTrack = "";
        float musicFade = 1f;
        AudioClip[][] variants;
        int[] lastVariant;
        readonly System.Collections.Generic.Dictionary<string, AudioClip> musicClips = new System.Collections.Generic.Dictionary<string, AudioClip>();
        // per-sound loudness trims for the recorded samples
        static readonly float[] trim = new float[(int)SfxId.Count];
        static System.Random rng = new System.Random(1234);

        public static void Init(GameObject host)
        {
            inst = host.AddComponent<Sfx>();
            inst.Build();
        }

        void Build()
        {
            voices = new AudioSource[14];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            musicB = gameObject.AddComponent<AudioSource>();
            musicB.loop = true;
            musicB.playOnAwake = false;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.loop = true;
            ambience.playOnAwake = false;
            LoadSamples();

            clips = new AudioClip[(int)SfxId.Count];
            clips[(int)SfxId.Hit] = Make("hit", 0.16f, (t, d) => Noise() * Env(t, 0.002f, 0.12f) * 0.8f + Sine(160 - 300 * t, t) * Env(t, 0.001f, 0.1f) * 0.6f);
            clips[(int)SfxId.HeavyHit] = Make("heavy", 0.35f, (t, d) => Noise() * Env(t, 0.002f, 0.25f) * 0.7f + Sine(90 - 120 * t, t) * Env(t, 0.001f, 0.3f));
            clips[(int)SfxId.Whoosh] = Make("whoosh", 0.22f, (t, d) => Noise() * Mathf.Sin(Mathf.PI * t / d) * 0.35f);
            clips[(int)SfxId.Block] = Make("block", 0.18f, (t, d) => (Square(620, t) * 0.3f + Noise() * 0.3f) * Env(t, 0.001f, 0.12f));
            clips[(int)SfxId.Parry] = Make("parry", 0.5f, (t, d) => (Sine(1320, t) + Sine(1980, t) * 0.5f) * Env(t, 0.001f, 0.45f) * 0.5f);
            clips[(int)SfxId.Explosion] = Make("boom", 1.1f, (t, d) => LowNoise(0.15f) * Env(t, 0.005f, 1.0f) * 1.1f + Sine(55 - 25 * t, t) * Env(t, 0.003f, 0.8f) * 0.7f);
            clips[(int)SfxId.Fire] = Make("fire", 0.7f, (t, d) => LowNoise(0.4f) * Mathf.Sin(Mathf.PI * t / d) * 0.7f);
            clips[(int)SfxId.Thunder] = Make("thunder", 0.9f, (t, d) => (Noise() * (Random01() > 0.6f ? 1f : 0.3f)) * Env(t, 0.001f, 0.8f) * 0.8f + Sine(70, t) * Env(t, 0.01f, 0.6f) * 0.5f);
            clips[(int)SfxId.Energy] = Make("energy", 0.6f, (t, d) => (Sine(220 + 440 * t, t) * 0.5f + Sine(330 + 660 * t, t) * 0.3f) * Mathf.Sin(Mathf.PI * t / d) * 0.6f);
            clips[(int)SfxId.Poof] = Make("poof", 0.3f, (t, d) => LowNoise(0.3f) * Env(t, 0.005f, 0.25f) * 0.8f);
            clips[(int)SfxId.Step] = Make("step", 0.07f, (t, d) => LowNoise(0.5f) * Env(t, 0.001f, 0.06f) * 0.4f);
            clips[(int)SfxId.Absorb] = Make("absorb", 0.6f, (t, d) => Sine(900 - 700 * t, t) * Mathf.Sin(Mathf.PI * t / d) * 0.5f + Noise() * 0.08f);
            clips[(int)SfxId.Release] = Make("release", 0.8f, (t, d) => (Sine(110 + 30 * Mathf.Sin(t * 60), t) * 0.6f + Noise() * 0.3f) * Env(t, 0.01f, 0.7f));
            clips[(int)SfxId.Teleport] = Make("tp", 0.25f, (t, d) => Sine(1600 - 1400 * t / d, t) * Env(t, 0.001f, 0.22f) * 0.4f);
            clips[(int)SfxId.Crumble] = Make("crumble", 0.9f, (t, d) => LowNoise(0.25f) * (Random01() > 0.85f ? 1f : 0.45f) * Env(t, 0.01f, 0.85f));
            clips[(int)SfxId.Roar] = Make("roar", 1.0f, (t, d) => (Saw(95 + 15 * Mathf.Sin(t * 22), t) * 0.4f + LowNoise(0.2f) * 0.4f) * Mathf.Sin(Mathf.PI * t / d));
            clips[(int)SfxId.Click] = Make("click", 0.05f, (t, d) => Sine(1100, t) * Env(t, 0.001f, 0.04f) * 0.4f);
            clips[(int)SfxId.Clash] = Make("clash", 0.6f, (t, d) => (Sine(1500, t) + Sine(2370, t) + Noise() * 0.6f) * Env(t, 0.001f, 0.5f) * 0.35f);
            clips[(int)SfxId.Ko] = Make("ko", 1.4f, (t, d) => (Sine(220, t) + Sine(277, t) + Sine(330, t)) * Env(t, 0.02f, 1.3f) * 0.3f);
            clips[(int)SfxId.Hiss] = Make("hiss", 0.5f, (t, d) => Noise() * Mathf.Sin(Mathf.PI * t / d) * 0.3f);
            clips[(int)SfxId.Charge] = Make("charge", 0.9f, (t, d) => Sine(200 + 600 * t / d, t) * (t / d) * 0.4f + Noise() * 0.05f);
            clips[(int)SfxId.WireSwing] = Make("wireswing", 0.4f, (t, d) => (Noise() * 0.5f + Sine(2000 + 5000 * t / d, t) * 0.25f) * Mathf.Sin(Mathf.PI * t / d) * 0.4f);
            clips[(int)SfxId.WireCrack] = Make("wirecrack", 0.3f, (t, d) => (Noise() * Env(t, 0.0005f, 0.03f) + Square(2400, t) * (Random01() > 0.7f ? 0.3f : 0f) * Env(t, 0.002f, 0.2f)) * 0.7f);
            clips[(int)SfxId.WireZap] = Make("wirezap", 0.22f, (t, d) => Square(180 + 60 * Mathf.Sin(t * 200), t) * (Mathf.Sin(t * 220) > -0.3f ? 1f : 0f) * Env(t, 0.002f, 0.18f) * 0.35f);
            clips[(int)SfxId.Land] = Make("land", 0.12f, (t, d) => LowNoise(0.4f) * Env(t, 0.001f, 0.1f) * 0.6f);
        }

        /// <summary>Loads recorded CC0 samples (Resources/Kagegan/Audio/Sfx/name_N.ogg). Missing ones fall back to synthesis.</summary>
        void LoadSamples()
        {
            variants = new AudioClip[(int)SfxId.Count][];
            lastVariant = new int[(int)SfxId.Count];
            for (int i = 0; i < trim.Length; i++) trim[i] = 1f;
            trim[(int)SfxId.Energy] = 1f; trim[(int)SfxId.Charge] = 1f; trim[(int)SfxId.Release] = 1f;
            trim[(int)SfxId.Whoosh] = 0.55f; trim[(int)SfxId.Land] = 0.7f; trim[(int)SfxId.Crumble] = 0.8f; trim[(int)SfxId.Block] = 0.85f;
            trim[(int)SfxId.WireSwing] = 0.75f; trim[(int)SfxId.WireCrack] = 0.8f; trim[(int)SfxId.Ko] = 0.9f; trim[(int)SfxId.Roar] = 0.9f;
            var all = Resources.LoadAll<AudioClip>("Kagegan/Audio/Sfx");
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<AudioClip>>();
            foreach (var c in all)
            {
                int us = c.name.LastIndexOf('_');
                string key = (us > 0 ? c.name.Substring(0, us) : c.name).ToLowerInvariant();
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new System.Collections.Generic.List<AudioClip>();
                list.Add(c);
            }
            for (int i = 0; i < (int)SfxId.Count; i++)
            {
                string key = ((SfxId)i).ToString().ToLowerInvariant();
                if (groups.TryGetValue(key, out var list) && list.Count > 0) variants[i] = list.ToArray();
            }
            foreach (var key in new[] { "stepgrass", "stepstone", "stepsnow" })
                if (groups.TryGetValue(key, out var list) && list.Count > 0) surfaceSteps[key] = list.ToArray();
        }

        // ---------- synthesis helpers ----------
        delegate float Gen(float t, float dur);
        static float lp;

        static AudioClip Make(string name, float dur, Gen g)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            lp = 0f;
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(g((float)i / Rate, dur), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Random01() => (float)rng.NextDouble();
        static float Noise() => (float)rng.NextDouble() * 2f - 1f;
        static float LowNoise(float k) { lp += (Noise() - lp) * k; return lp * 2.2f; }
        static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Square(float f, float t) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t));
        static float Saw(float f, float t) => 2f * (t * f - Mathf.Floor(t * f + 0.5f));
        static float Env(float t, float a, float r) => t < a ? t / a : Mathf.Exp(-(t - a) * 5f / Mathf.Max(0.001f, r));

        // ---------- music ----------
        static readonly float[] Penta = { 0, 3, 5, 7, 10, 12, 15, 17 };

        AudioClip BuildTheme(int theme)
        {
            // theme 0 = menu (calm koto), 1 = battle, 2 = boss, 3 = ending
            float bpm = theme == 0 ? 84f : theme == 1 ? 128f : theme == 2 ? 142f : 76f;
            int bars = 8;
            float beat = 60f / bpm;
            float dur = bars * 4 * beat;
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            var r = new System.Random(77 + theme);
            float root = theme == 2 ? 49f : theme == 3 ? 58.27f : 55f; // A1 / ~Bb1

            int steps = bars * 8; // eighth notes
            float step = beat / 2f;
            int[] melody = new int[steps];
            for (int i = 0; i < steps; i++) melody[i] = r.NextDouble() < (theme == 0 || theme == 3 ? 0.45 : 0.6) ? r.Next(Penta.Length) : -1;
            // repeat phrase every 2 bars for musicality
            for (int i = 16; i < steps; i++) if (r.NextDouble() < 0.6) melody[i] = melody[i % 16];

            for (int s = 0; s < steps; s++)
            {
                int start = Mathf.FloorToInt(s * step * Rate);
                bool downBeat = s % 8 == 0, beatHit = s % 2 == 0;
                // taiko / kick
                if (theme != 3 && (downBeat || (theme >= 1 && beatHit && r.NextDouble() < 0.5) || s % 8 == 6))
                    AddTone(data, start, 0.35f, (t) => Mathf.Sin(2 * Mathf.PI * (60 - 40 * t) * t) * Mathf.Exp(-t * 9f) * (downBeat ? 0.75f : 0.5f));
                // shaker / hat
                if (theme >= 1 && theme <= 2)
                    AddTone(data, start, 0.05f, (t) => ((float)r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 80f) * 0.12f);
                // bass on beats
                if (beatHit && theme != 0)
                {
                    float bf = root * Mathf.Pow(2f, Penta[(s / 8) % 3 == 2 ? 2 : 0] / 12f);
                    AddTone(data, start, beat * 0.9f, (t) => Mathf.Sin(2 * Mathf.PI * bf * t) * Mathf.Exp(-t * 3f) * 0.28f);
                }
                // koto-like pluck melody
                if (melody[s] >= 0)
                {
                    float f = root * 4f * Mathf.Pow(2f, Penta[melody[s]] / 12f);
                    float amp = theme == 0 || theme == 3 ? 0.22f : 0.16f;
                    AddTone(data, start, beat * 1.6f, (t) =>
                        (Mathf.Sin(2 * Mathf.PI * f * t) + 0.4f * Mathf.Sin(4 * Mathf.PI * f * t) + 0.15f * Mathf.Sin(6 * Mathf.PI * f * t)) * Mathf.Exp(-t * 4.5f) * amp);
                }
                // boss: low drone stab
                if (theme == 2 && s % 16 == 0)
                    AddTone(data, start, beat * 4f, (t) => (2f * (t * root * 2f - Mathf.Floor(t * root * 2f + 0.5f))) * Mathf.Exp(-t * 0.8f) * 0.12f);
                // pad for menu / ending
                if ((theme == 0 || theme == 3) && s % 16 == 0)
                {
                    float pf = root * 2f;
                    AddTone(data, start, beat * 8f, (t) => (Mathf.Sin(2 * Mathf.PI * pf * t) + Mathf.Sin(2 * Mathf.PI * pf * 1.5f * t) * 0.6f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / (beat * 8f))) * 0.09f);
                }
            }
            // soft limiter
            for (int i = 0; i < n; i++) data[i] = data[i] / (1f + Mathf.Abs(data[i]));
            var clip = AudioClip.Create("theme" + theme, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void AddTone(float[] buf, int start, float dur, System.Func<float, float> f)
        {
            int len = Mathf.CeilToInt(dur * Rate);
            for (int i = 0; i < len; i++)
            {
                int k = (start + i) % buf.Length; // wraps for seamless loop
                buf[k] += f((float)i / Rate);
            }
        }

        // ---------- API ----------
        public static void Play(SfxId id, float volume = 1f, float pitch = 1f) => Play(id, volume, pitch, 0f);

        /// <summary>Plays a random variant (never the same one twice in a row) with pitch/volume jitter and stereo pan.</summary>
        public static void Play(SfxId id, float volume, float pitch, float pan)
        {
            if (inst == null || inst.clips == null) return;
            AudioClip clip = null;
            var vars = inst.variants != null ? inst.variants[(int)id] : null;
            if (vars != null && vars.Length > 0)
            {
                int k = vars.Length == 1 ? 0 : Random.Range(0, vars.Length);
                if (vars.Length > 1 && k == inst.lastVariant[(int)id]) k = (k + 1) % vars.Length;
                inst.lastVariant[(int)id] = k;
                clip = vars[k];
                volume *= trim[(int)id];
            }
            if (clip == null) clip = inst.clips[(int)id];
            if (clip == null) return;
            var v = inst.voices[inst.nextVoice];
            inst.nextVoice = (inst.nextVoice + 1) % inst.voices.Length;
            v.pitch = pitch * (0.94f + 0.12f * Random01());
            v.panStereo = Mathf.Clamp(pan, -0.8f, 0.8f);
            v.PlayOneShot(clip, Mathf.Clamp01(volume) * Save.Data.sfx);
        }

        readonly System.Collections.Generic.Dictionary<string, AudioClip[]> surfaceSteps = new System.Collections.Generic.Dictionary<string, AudioClip[]>();
        int lastSurfaceStep;

        /// <summary>Footstep that sounds like the ground of the current arena: grass in the forest, stone in the city
        /// and at the moon temple, snow while it snows. The dojo's sandy yard keeps the default step.</summary>
        public static void Step(Vector3 world, float volume)
        {
            if (inst == null) return;
            string key = null;
            var a = Arena.Current;
            if (a != null)
            {
                if (a.WeatherKind == WeatherType.Snow) key = "stepsnow";
                else if (a.Id == ArenaId.Forest) key = "stepgrass";
                else if (a.Id == ArenaId.City || a.Id == ArenaId.MoonTemple) key = "stepstone";
            }
            if (key == null || !inst.surfaceSteps.TryGetValue(key, out var vars)) { PlayAt(SfxId.Step, world, volume); return; }
            int k = Random.Range(0, vars.Length);
            if (vars.Length > 1 && k == inst.lastSurfaceStep) k = (k + 1) % vars.Length;
            inst.lastSurfaceStep = k;
            float pan = 0f;
            var cam = CameraRig.Inst != null ? CameraRig.Inst.Cam : null;
            if (cam != null)
            {
                pan = (cam.WorldToViewportPoint(world).x - 0.5f) * 1.2f;
                volume *= Mathf.Clamp(1.4f - (world - cam.transform.position).magnitude / 30f, 0.35f, 1f);
            }
            var v = inst.voices[inst.nextVoice];
            inst.nextVoice = (inst.nextVoice + 1) % inst.voices.Length;
            v.pitch = 0.94f + 0.12f * Random01();
            v.panStereo = Mathf.Clamp(pan, -0.8f, 0.8f);
            v.PlayOneShot(vars[k], Mathf.Clamp01(volume) * Save.Data.sfx);
        }

        /// <summary>Positional helper: pans the sound by where it happens on screen and attenuates with distance.</summary>
        public static void PlayAt(SfxId id, Vector3 world, float volume = 1f, float pitch = 1f)
        {
            float pan = 0f;
            var cam = CameraRig.Inst != null ? CameraRig.Inst.Cam : null;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(world);
                pan = (vp.x - 0.5f) * 1.2f;
                float d = (world - cam.transform.position).magnitude;
                volume *= Mathf.Clamp(1.4f - d / 30f, 0.35f, 1f);
            }
            Play(id, volume, pitch, pan);
        }

        /// <summary>Hit sound whose sample, loudness and pitch follow the strength of the blow.</summary>
        public static void Impact(float damage, bool heavy, Vector3 world)
        {
            float k = Mathf.Clamp01(damage / 40f);
            if (heavy || damage >= 14f) PlayAt(SfxId.HeavyHit, world, 0.75f + 0.25f * k, 1.05f - 0.2f * k);
            else PlayAt(SfxId.Hit, world, 0.6f + 0.35f * k, 1.08f - 0.15f * k);
        }

        /// <summary>Recorded track from Resources/Kagegan/Audio/Music, cross-faded; falls back to the synthesised theme.</summary>
        public static void PlayTrack(string name, int fallbackTheme)
        {
            if (inst == null) return;
            if (inst.currentTrack == name && (inst.music.isPlaying || inst.musicB.isPlaying)) return;
            if (!inst.musicClips.TryGetValue(name, out var clip))
            {
                clip = Resources.Load<AudioClip>("Kagegan/Audio/Music/" + name);
                inst.musicClips[name] = clip;
            }
            if (clip == null) { PlayMusic(fallbackTheme); return; }
            inst.currentTrack = name;
            inst.currentTheme = -1;
            // swap sources and fade
            var tmp = inst.music; inst.music = inst.musicB; inst.musicB = tmp;
            inst.music.clip = clip;
            inst.music.volume = 0f;
            inst.music.Play();
            inst.musicFade = 0f;
        }

        public static void SetAmbience(string name, float volume)
        {
            if (inst == null) return;
            if (string.IsNullOrEmpty(name)) { inst.StopAllAmbienceFades(); inst.ambience.Stop(); return; }
            var clip = Resources.Load<AudioClip>("Kagegan/Audio/Music/" + name);
            if (clip == null) return;
            inst.StopAllAmbienceFades();
            inst.ambience.clip = clip;
            inst.ambience.volume = volume * Save.Data.sfx;
            inst.ambience.Play();
        }

        Coroutine ambFade;
        void StopAllAmbienceFades() { if (ambFade != null) { StopCoroutine(ambFade); ambFade = null; } }

        /// <summary>Crossfades the ambience loop (weather turning mid-fight): the old loop fades down, the new one fades up.</summary>
        public static void FadeAmbience(string name, float volume, float seconds)
        {
            if (inst == null) return;
            inst.StopAllAmbienceFades();
            inst.ambFade = inst.StartCoroutine(inst.AmbienceFade(name, volume, Mathf.Max(0.2f, seconds)));
        }

        System.Collections.IEnumerator AmbienceFade(string name, float volume, float seconds)
        {
            var clip = string.IsNullOrEmpty(name) ? null : Resources.Load<AudioClip>("Kagegan/Audio/Music/" + name);
            float half = seconds * 0.5f;
            // same loop: only glide the volume
            if (clip != null && ambience.clip == clip && ambience.isPlaying)
            {
                float v0 = ambience.volume;
                for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) { ambience.volume = Mathf.Lerp(v0, volume * Save.Data.sfx, t / seconds); yield return null; }
                ambience.volume = volume * Save.Data.sfx;
                ambFade = null;
                yield break;
            }
            float start = ambience.volume;
            if (ambience.isPlaying)
                for (float t = 0f; t < half; t += Time.unscaledDeltaTime) { ambience.volume = Mathf.Lerp(start, 0f, t / half); yield return null; }
            ambience.Stop();
            if (clip != null)
            {
                ambience.clip = clip;
                ambience.volume = 0f;
                ambience.Play();
                for (float t = 0f; t < half; t += Time.unscaledDeltaTime) { ambience.volume = Mathf.Lerp(0f, volume * Save.Data.sfx, t / half); yield return null; }
                ambience.volume = volume * Save.Data.sfx;
            }
            ambFade = null;
        }

        // ---------- tension: the end of a round pushes the music ----------
        float intensityTarget, intensity, intensitySetAt = -10f;
        float heartbeatAt;

        /// <summary>
        /// 0..1 tension of the fight, set every frame by the battle (low health, a boss in its last phase).
        /// The music gets louder and slightly faster and, near the end, a heartbeat thumps under it.
        /// Falls back to calm by itself when nobody sets it (menus, results).
        /// </summary>
        public static void Intensity(float v)
        {
            if (inst == null) return;
            inst.intensityTarget = Mathf.Clamp01(v);
            inst.intensitySetAt = Time.unscaledTime;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Time.unscaledTime - intensitySetAt > 0.4f) intensityTarget = 0f;
            // rises fast, settles slowly, so a single heal does not snap the track back
            intensity = Mathf.MoveTowards(intensity, intensityTarget, dt * (intensityTarget > intensity ? 0.8f : 0.25f));
            float boost = 1f + intensity * 0.3f;
            music.pitch = musicB.pitch = 1f + intensity * 0.05f;
            float target = Mathf.Min(1f, Save.Data.music * 0.6f * boost);
            if (musicFade < 1f)
            {
                musicFade = Mathf.Min(1f, musicFade + dt / 1.2f);
                music.volume = target * musicFade;
                musicB.volume = target * (1f - musicFade);
                if (musicFade >= 1f) musicB.Stop();
            }
            else if (music.isPlaying) music.volume = target;
            // heartbeat: two low thumps, quicker the worse it gets
            if (intensity > 0.55f && Time.timeScale > 0.01f && Time.unscaledTime >= heartbeatAt)
            {
                float v = (intensity - 0.55f) / 0.45f;
                heartbeatAt = Time.unscaledTime + Mathf.Lerp(1.05f, 0.62f, v);
                StartCoroutine(Heartbeat(0.25f + 0.3f * v));
            }
        }

        System.Collections.IEnumerator Heartbeat(float vol)
        {
            Play(SfxId.Land, vol, 0.42f);
            yield return new WaitForSecondsRealtime(0.17f);
            Play(SfxId.Land, vol * 0.7f, 0.38f);
        }

        public static void PlayMusic(int theme)
        {
            if (inst == null) return;
            if (inst.currentTheme == theme && inst.music.isPlaying) return;
            if (inst.themes[theme] == null) inst.themes[theme] = inst.BuildTheme(theme);
            inst.currentTheme = theme;
            inst.currentTrack = "";
            inst.musicB.Stop();
            inst.musicFade = 1f;
            inst.music.clip = inst.themes[theme];
            inst.music.volume = Save.Data.music * 0.6f;
            inst.music.Play();
        }

        public static void RefreshVolume()
        {
            if (inst != null) { inst.music.volume = Save.Data.music * 0.6f; inst.ambience.volume = Mathf.Min(inst.ambience.volume, Save.Data.sfx); }
        }

        /// <summary>Announcer line (Kenney Voiceover Pack: Fighter, CC0) from Resources/Kagegan/Audio/Voice.</summary>
        public static void Voice(string line, float volume = 1f) => PlayFile("Kagegan/Audio/Voice/" + line, volume);

        /// <summary>Short music stinger from Resources/Kagegan/Audio/Jingles.</summary>
        public static void Jingle(string name, float volume = 0.8f) => PlayFile("Kagegan/Audio/Jingles/" + name, volume * Mathf.Max(0.3f, Save.Data.music / Mathf.Max(0.01f, Save.Data.sfx)));

        /// <summary>Random variant name_0..name_(variants-1) from Resources/Kagegan/Audio/Weather. False if none loaded.</summary>
        public static bool WeatherSound(string name, int variants, float volume)
        {
            if (inst == null) return false;
            int k = Random.Range(0, variants);
            if (variants > 1 && k == inst.lastWeather) k = (k + 1) % variants;
            inst.lastWeather = k;
            return PlayFile("Kagegan/Audio/Weather/" + name + "_" + k, volume);
        }
        int lastWeather = -1;

        static bool PlayFile(string path, float volume)
        {
            if (inst == null) return false;
            if (!inst.fileClips.TryGetValue(path, out var clip))
            {
                clip = Resources.Load<AudioClip>(path);
                inst.fileClips[path] = clip;
            }
            if (clip == null) return false;
            if (inst.announcer == null)
            {
                inst.announcer = inst.gameObject.AddComponent<AudioSource>();
                inst.announcer.playOnAwake = false;
                inst.announcer.spatialBlend = 0f;
            }
            inst.announcer.PlayOneShot(clip, Mathf.Clamp01(volume * Save.Data.sfx));
            return true;
        }

        AudioSource announcer;
        readonly System.Collections.Generic.Dictionary<string, AudioClip> fileClips = new System.Collections.Generic.Dictionary<string, AudioClip>();

        public static void StopMusic() { if (inst != null) { inst.music.Stop(); inst.musicB.Stop(); inst.currentTrack = ""; } }
    }
}
