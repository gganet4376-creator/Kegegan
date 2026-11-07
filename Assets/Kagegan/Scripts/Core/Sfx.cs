using UnityEngine;

namespace Kagegan
{
    public enum SfxId
    {
        Hit, HeavyHit, Whoosh, Block, Parry, Explosion, Fire, Thunder, Energy, Poof, Step,
        Absorb, Release, Teleport, Crumble, Roar, Click, Clash, Ko, Hiss, Charge, Land, Count
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
        AudioSource music;
        AudioClip[] themes = new AudioClip[4];
        int currentTheme = -1;
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
            clips[(int)SfxId.Land] = Make("land", 0.12f, (t, d) => LowNoise(0.4f) * Env(t, 0.001f, 0.1f) * 0.6f);
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
        public static void Play(SfxId id, float volume = 1f, float pitch = 1f)
        {
            if (inst == null || inst.clips == null) return;
            var clip = inst.clips[(int)id];
            if (clip == null) return;
            var v = inst.voices[inst.nextVoice];
            inst.nextVoice = (inst.nextVoice + 1) % inst.voices.Length;
            v.pitch = pitch * (0.93f + 0.14f * Random01());
            v.PlayOneShot(clip, volume * Save.Data.sfx);
        }

        public static void PlayMusic(int theme)
        {
            if (inst == null) return;
            if (inst.currentTheme == theme && inst.music.isPlaying) return;
            if (inst.themes[theme] == null) inst.themes[theme] = inst.BuildTheme(theme);
            inst.currentTheme = theme;
            inst.music.clip = inst.themes[theme];
            inst.music.volume = Save.Data.music * 0.6f;
            inst.music.Play();
        }

        public static void RefreshVolume()
        {
            if (inst != null) inst.music.volume = Save.Data.music * 0.6f;
        }

        public static void StopMusic() { if (inst != null) inst.music.Stop(); }
    }
}
