using UnityEngine;

namespace Kagegan
{
    public enum WeatherType { Clear, Rain, Storm, Snow, Fog }

    /// <summary>
    /// Arena weather: rain (streaks, splashes, distant rain curtains, wet ground), thunderstorm (heavy rain + lightning
    /// and recorded thunder), snow (drifting flakes that slowly settle on the scenery) and fog (dense haze + low mist).
    /// Sets the shader globals _KageWet / _KageSnow, the lighting mood, the wind and the ambience loop.
    /// Particle counts scale with GraphicsQuality.WeatherMul, so Low stays cheap.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public const int SettingCount = 7; // auto, random, clear, rain, storm, snow, fog

        public WeatherType Type { get; private set; }
        Arena arena;
        float wet, wetTarget, snow, snowTarget;
        readonly System.Collections.Generic.List<(ParticleSystem ps, float rate)> systems = new System.Collections.Generic.List<(ParticleSystem, float)>();
        Transform follow; // emitters that move with the camera
        float fade = 1f, fadeTarget = 1f, fadeSpeed = 1f; // emission multiplier for mid-fight changes
        bool retiring;   // being replaced: stops writing the global wet / snow state
        float retireAt;
        static WeatherType lastAuto = (WeatherType)(-1);

        /// <summary>Weather for an arena from the player's setting. battle = false (training): "auto" gives the winter dojo.</summary>
        public static WeatherType Choose(ArenaId id, bool battle)
        {
            int s = Save.Data != null ? Save.Data.weather : 0;
            if (s >= 2) return (WeatherType)Mathf.Clamp(s - 2, 0, 4);
            if (s == 1) return (WeatherType)Random.Range(0, 5);
            // training: the winter dojo (falling snow, white ground), unless the player picked a weather
            if (!battle) return id == ArenaId.Dojo && s == 0 ? WeatherType.Snow : WeatherType.Clear;
            // auto: the ruined city always has its storm, the rest roll a weighted chance;
            // one re-roll if it repeats the previous fight, so consecutive fights look different
            if (id == ArenaId.City) return lastAuto = WeatherType.Storm;
            var w = RollAuto(id);
            if (w == lastAuto) w = RollAuto(id);
            return lastAuto = w;
        }

        static WeatherType RollAuto(ArenaId id)
        {
            float r = Random.value;
            switch (id)
            {
                case ArenaId.Forest: return r < 0.4f ? WeatherType.Clear : r < 0.68f ? WeatherType.Rain : r < 0.85f ? WeatherType.Fog : WeatherType.Storm;
                case ArenaId.MoonTemple: return r < 0.5f ? WeatherType.Clear : r < 0.78f ? WeatherType.Snow : WeatherType.Fog;
                default: return r < 0.45f ? WeatherType.Clear : r < 0.67f ? WeatherType.Rain : r < 0.86f ? WeatherType.Snow : WeatherType.Fog;
            }
        }

        /// <summary>
        /// "Auto" weather can turn during a fight: clouds roll in and it starts to rain, rain grows into a storm,
        /// a storm eases off, fog lifts, snow starts or stops. Only for the auto setting (a fixed choice stays fixed).
        /// </summary>
        public static void PlanChange(Arena a, bool battle)
        {
            if (a == null || !battle || Save.Data == null || Save.Data.weather != 0 || a.Id == ArenaId.City) return;
            if (Random.value > 0.55f) return;
            var from = a.WeatherKind;
            WeatherType to;
            float r = Random.value;
            switch (from)
            {
                case WeatherType.Clear:
                    to = a.Id == ArenaId.MoonTemple ? (r < 0.6f ? WeatherType.Snow : WeatherType.Fog) : a.Id == ArenaId.Dojo ? (r < 0.55f ? WeatherType.Rain : WeatherType.Snow) : WeatherType.Rain;
                    break;
                case WeatherType.Rain: to = r < 0.6f ? WeatherType.Storm : WeatherType.Clear; break;
                case WeatherType.Storm: to = WeatherType.Rain; break;
                case WeatherType.Snow: to = r < 0.5f ? WeatherType.Clear : WeatherType.Fog; break;
                default: to = a.Id == ArenaId.MoonTemple && r < 0.4f ? WeatherType.Snow : r < 0.7f ? WeatherType.Clear : WeatherType.Rain; break;
            }
            a.ScheduleWeatherChange(to, Random.Range(35f, 80f));
        }

        /// <summary>Clears the global wet / snow state (arena rebuilt without weather, back to menu).</summary>
        public static void ResetGlobals()
        {
            Shader.SetGlobalFloat("_KageWet", 0f);
            Shader.SetGlobalFloat("_KageSnow", 0f);
        }

        public static bool Precipitation(WeatherType t) => t == WeatherType.Rain || t == WeatherType.Storm || t == WeatherType.Snow;

        /// <summary>Lighting change for this weather (null = keep the arena's own look).</summary>
        public static Arena.WeatherLook? Look(WeatherType t, ArenaId id)
        {
            // the city is built as a stormy night already
            if (id == ArenaId.City && (t == WeatherType.Rain || t == WeatherType.Storm)) return null;
            bool night = id == ArenaId.MoonTemple;
            switch (t)
            {
                case WeatherType.Rain:
                    return new Arena.WeatherLook { Sun = 0.55f, SunTint = new Color(0.86f, 0.92f, 1f), Amb = 0.9f, Fog = night ? new Color(0.14f, 0.15f, 0.22f) : new Color(0.5f, 0.55f, 0.62f), FogLerp = 0.65f, FogNear = 0.6f, FogMax = 0.85f, Sky = new Color(0.62f, 0.66f, 0.74f) };
                case WeatherType.Storm:
                    return new Arena.WeatherLook { Sun = 0.42f, SunTint = new Color(0.8f, 0.88f, 1f), Amb = 0.75f, Fog = night ? new Color(0.1f, 0.1f, 0.16f) : new Color(0.3f, 0.33f, 0.4f), FogLerp = 0.6f, FogNear = 0.5f, FogMax = 0.9f, Sky = new Color(0.42f, 0.45f, 0.54f) };
                case WeatherType.Snow:
                    return new Arena.WeatherLook { Sun = 0.82f, SunTint = new Color(0.92f, 0.96f, 1.06f), Amb = 1.2f, Fog = night ? new Color(0.3f, 0.32f, 0.42f) : new Color(0.8f, 0.84f, 0.9f), FogLerp = 0.6f, FogNear = 0.65f, FogMax = 0.8f, Sky = new Color(0.85f, 0.88f, 0.95f) };
                case WeatherType.Fog:
                    return new Arena.WeatherLook { Sun = 0.7f, SunTint = Color.white, Amb = 1.1f, Fog = night ? new Color(0.22f, 0.22f, 0.3f) : new Color(0.72f, 0.74f, 0.76f), FogLerp = 0.7f, FogNear = 0.32f, FogMax = 0.95f, Sky = new Color(0.8f, 0.82f, 0.84f) };
                default: return null;
            }
        }

        /// <summary>Ambience loop (Resources/Kagegan/Audio/Music) and its volume for this weather.</summary>
        public (string name, float volume) Ambience()
        {
            switch (Type)
            {
                case WeatherType.Rain: return ("amb_rain", 0.5f);
                case WeatherType.Storm: return ("amb_rain_heavy", 0.55f);
                case WeatherType.Snow: return ("amb_wind", 0.32f);
                case WeatherType.Fog: return ("amb_wind", 0.2f);
            }
            switch (arena != null ? arena.Id : ArenaId.Dojo)
            {
                case ArenaId.Forest: return ("amb_wind", 0.35f);
                case ArenaId.MoonTemple: return ("amb_wind", 0.25f);
                default: return (null, 0f);
            }
        }

        /// <summary>Starts this weather. fadeIn > 0: a mid-fight change, the precipitation builds up over that many seconds.</summary>
        public void Begin(Arena a, WeatherType t, float fadeIn = 0f)
        {
            arena = a;
            Type = t;
            if (fadeIn > 0f) { fade = 0f; fadeTarget = 1f; fadeSpeed = 1f / fadeIn; }
            var fgo = new GameObject("WeatherFollow");
            fgo.transform.SetParent(transform, false);
            fgo.AddComponent<FollowCameraXZ>();
            follow = fgo.transform;

            switch (t)
            {
                case WeatherType.Rain:
                    Rain(240f, 1f);
                    wet = 0.3f; wetTarget = 0.85f;
                    break;
                case WeatherType.Storm:
                    Rain(480f, 1.3f);
                    Mist(new Color(0.55f, 0.58f, 0.66f, 0.22f), 10);
                    wet = 0.6f; wetTarget = 1f;
                    break;
                case WeatherType.Snow:
                    Snow();
                    snow = 0.2f; snowTarget = 0.55f; // a dusting, not a white sheet over the whole arena
                    if (a != null && a.Winter) snow = snowTarget = 0.9f; // the winter dojo is built snowed in
                    break;
                case WeatherType.Fog:
                    Mist(new Color(0.85f, 0.87f, 0.9f, 0.32f), 22);
                    wet = wetTarget = 0.15f;
                    break;
            }
            // grass and dirt only get damp: full puddles + sky reflection read as a lake outside the asphalt city
            if (a == null || a.Id != ArenaId.City) { wet *= 0.35f; wetTarget *= 0.35f; }
            // a change mid-fight continues from how wet / snowy the ground already is (it dries or melts slowly)
            if (fadeIn > 0f) { wet = Shader.GetGlobalFloat("_KageWet"); snow = Shader.GetGlobalFloat("_KageSnow"); }
            Shader.SetGlobalFloat("_KageWet", wet);
            Shader.SetGlobalFloat("_KageSnow", snow);
            GraphicsQuality.Changed += Refresh;
        }

        void Update()
        {
            if (fade != fadeTarget)
            {
                fade = Mathf.MoveTowards(fade, fadeTarget, Time.deltaTime * fadeSpeed);
                Refresh();
            }
            if (retiring)
            {
                // wait for the last drops / flakes to finish falling, then go
                if (fade <= 0f && Time.time > retireAt) Destroy(gameObject);
                return;
            }
            // the ground gets wetter (or whiter) the longer it rains (or snows), and dries / melts after it stops
            if (wet != wetTarget) { wet = Mathf.MoveTowards(wet, wetTarget, Time.deltaTime / (wet < wetTarget ? 45f : 60f)); Shader.SetGlobalFloat("_KageWet", wet); }
            if (snow != snowTarget) { snow = Mathf.MoveTowards(snow, snowTarget, Time.deltaTime / (snow < snowTarget ? 70f : 50f)); Shader.SetGlobalFloat("_KageSnow", snow); }
        }

        /// <summary>Replaced by another weather: emission eases to zero, falling particles finish, then the object is removed.</summary>
        public void FadeOut(float seconds)
        {
            retiring = true;
            fadeTarget = 0f;
            fadeSpeed = 1f / Mathf.Max(0.5f, seconds);
            retireAt = Time.time + seconds + 26f; // longest particle life (mist) after the last emission
        }

        void OnDestroy()
        {
            GraphicsQuality.Changed -= Refresh;
            if (retiring) return; // its successor owns the wet / snow globals
            // a new arena may already have set its own weather this frame
            if (Arena.Current == null || Arena.Current == arena) ResetGlobals();
        }

        void Refresh()
        {
            // eased so the downpour thickens (or thins) gradually instead of a straight ramp
            float k = GraphicsQuality.WeatherMul * fade * fade * (3f - 2f * fade);
            foreach (var (ps, rate) in systems)
            {
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = rate * k;
            }
        }

        // ---------------- particle layers ----------------

        ParticleSystem Layer(string name, Transform parent, Vector3 localPos, float rate, float life, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(rate * Mathf.Max(1f, GraphicsQuality.WeatherMul) * life * 1.3f) + 4;
            var em = ps.emission;
            em.rateOverTime = rate * GraphicsQuality.WeatherMul * fade;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
            systems.Add((ps, rate));
            return ps;
        }

        static Gradient Fade(float inT, float outT)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, inT), new GradientAlphaKey(1, outT), new GradientAlphaKey(0, 1) });
            return g;
        }

        static void Box(ParticleSystem ps, Vector3 size)
        {
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = size;
        }

        static void Velocity(ParticleSystem ps, Vector3 min, Vector3 max)
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(min.x, max.x);
            vel.y = new ParticleSystem.MinMaxCurve(min.y, max.y);
            vel.z = new ParticleSystem.MinMaxCurve(min.z, max.z);
        }

        void Rain(float rate, float heavy)
        {
            Vector4 w = Shader.GetGlobalVector("_KageWind");
            float wx = w.x * 2.5f * heavy, wz = w.y * 1.2f * heavy;
            var streakMat = Mat.FxTex(Mat.Streak, false, "RainStreak");

            // near rain around the fighters: long thin streaks
            var near = Layer("Rain", follow, new Vector3(0f, 15f, 6f), rate, 1.1f, streakMat);
            var main = near.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = new Color(0.78f, 0.82f, 0.92f, 0.5f);
            Box(near, new Vector3(48f, 1f, 34f));
            Velocity(near, new Vector3(wx - 0.4f, -21f, wz - 0.4f), new Vector3(wx + 0.4f, -17f, wz + 0.4f));
            var col = near.colorOverLifetime; col.enabled = true; col.color = Fade(0.08f, 0.9f);
            var r = near.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1f;
            near.Play();

            // rain curtains far behind the arena: few, big and faint, so the downpour has depth
            if (GraphicsQuality.Current >= GraphicsPreset.Medium)
            {
                var far = Layer("RainFar", follow, new Vector3(0f, 26f, 45f), rate * 0.12f, 1.6f, streakMat);
                var fm = far.main;
                fm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
                fm.startColor = new Color(0.7f, 0.74f, 0.84f, 0.22f);
                Box(far, new Vector3(140f, 1f, 50f));
                Velocity(far, new Vector3(wx - 0.5f, -24f, wz), new Vector3(wx + 0.5f, -20f, wz));
                var fc = far.colorOverLifetime; fc.enabled = true; fc.color = Fade(0.1f, 0.85f);
                var fr = far.GetComponent<ParticleSystemRenderer>();
                fr.renderMode = ParticleSystemRenderMode.Stretch;
                fr.velocityScale = 0.06f;
                fr.lengthScale = 1f;
                far.Play();
            }

            // splashes: small rings that open on the ground (flat, facing up)
            var splash = Layer("RainSplash", follow, new Vector3(0f, 0.04f, 4f), rate * 0.45f, 0.35f, Mat.FxTex(Mat.Ring, false, "RainSplash"));
            var sm = splash.main;
            sm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            sm.startColor = new Color(0.85f, 0.88f, 0.95f, 0.55f);
            Box(splash, new Vector3(36f, 0f, 22f));
            var sz = splash.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.2f, 1f, 1f));
            var sc = splash.colorOverLifetime; sc.enabled = true; sc.color = Fade(0.05f, 0.4f);
            splash.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            splash.Play();

            // droplets that bounce off the splashes (skipped on Low)
            if (GraphicsQuality.Current >= GraphicsPreset.Medium)
            {
                var drops = Layer("RainDrops", follow, new Vector3(0f, 0.05f, 4f), rate * 0.25f, 0.28f, Mat.FxAlpha);
                var dm = drops.main;
                dm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                dm.startColor = new Color(0.85f, 0.9f, 1f, 0.6f);
                dm.gravityModifier = 2.2f;
                Box(drops, new Vector3(36f, 0f, 22f));
                Velocity(drops, new Vector3(-0.8f, 1.6f, -0.8f), new Vector3(0.8f, 2.6f, 0.8f));
                drops.Play();
            }
        }

        void Snow()
        {
            Vector4 w = Shader.GetGlobalVector("_KageWind");
            var flake = Mat.FxTex(Mat.SoftDot, false, "Snow");
            var s = Layer("Snow", follow, new Vector3(0f, 13f, 6f), 150f, 11f, flake);
            var main = s.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.prewarm = true;
            Box(s, new Vector3(50f, 1f, 36f));
            Velocity(s, new Vector3(w.x * 0.6f - 0.3f, -1.5f, w.y * 0.4f - 0.3f), new Vector3(w.x * 0.9f + 0.3f, -0.9f, w.y * 0.6f + 0.3f));
            var col = s.colorOverLifetime; col.enabled = true; col.color = Fade(0.05f, 0.9f);
            if (GraphicsQuality.Current >= GraphicsPreset.Medium)
            {
                // flakes tumble on the air instead of falling in straight lines
                var n = s.noise;
                n.enabled = true;
                n.strength = 0.7f;
                n.frequency = 0.35f;
                n.scrollSpeed = 0.2f;
                n.quality = GraphicsQuality.Current >= GraphicsPreset.High ? ParticleSystemNoiseQuality.Medium : ParticleSystemNoiseQuality.Low;
            }
            s.Play();

            // big soft flakes close to the camera (High/Ultra) for depth
            if (GraphicsQuality.Current >= GraphicsPreset.High)
            {
                var near = Layer("SnowNear", follow, new Vector3(0f, 9f, -3f), 14f, 7f, flake);
                var nm = near.main;
                nm.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
                nm.startColor = new Color(1f, 1f, 1f, 0.55f);
                nm.prewarm = true;
                Box(near, new Vector3(22f, 1f, 8f));
                Velocity(near, new Vector3(w.x * 0.6f - 0.3f, -1.6f, -0.2f), new Vector3(w.x * 0.9f + 0.3f, -1f, 0.2f));
                var nc = near.colorOverLifetime; nc.enabled = true; nc.color = Fade(0.1f, 0.85f);
                near.Play();
            }
        }

        static Texture2D mistTex;

        /// <summary>
        /// Soft cloud blot made in code: radial falloff times smooth value noise, fully transparent at the edges.
        /// (The old smoke-puff atlas had solid white centres and one straight-edged frame; at 14 m it read as white sheets.)
        /// </summary>
        static Texture2D MistTexture()
        {
            if (mistTex != null) return mistTex;
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "MistSoft", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float fall = Mathf.Clamp01(1f - r);
                    fall = fall * fall * (3f - 2f * fall);           // smooth to exactly zero at the rim
                    float n = 0.55f * Mathf.PerlinNoise(x * 0.045f + 3.1f, y * 0.045f + 7.7f)
                            + 0.3f * Mathf.PerlinNoise(x * 0.11f + 11.3f, y * 0.11f + 1.9f)
                            + 0.15f * Mathf.PerlinNoise(x * 0.23f + 5.2f, y * 0.23f + 9.4f);
                    float a = fall * Mathf.Clamp01(n * 1.35f - 0.15f) * 0.75f; // never fully opaque
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            mistTex = t;
            return t;
        }

        /// <summary>Low drifting ground mist behind the fighters (soft procedural blots, thin and wide).</summary>
        void Mist(Color c, int count)
        {
            float life = 24f;
            var mm = Mat.FxTex(MistTexture(), false, "Mist");
            mm.SetFloat("_Lit", 1f);   // mist takes the scene's light colour (dark at night, warm at dusk)
            mm.SetFloat("_Soft", 1f);  // fades out near the camera so it never fills the screen
            c.a *= 0.55f;              // many thin layers instead of a few dense sheets
            // starts behind the lane (the camera looks along +Z): nothing drifts between the camera and the fighters
            var m = Layer("Mist", follow, new Vector3(0f, 0.9f, 17f), count / life, life, mm);
            var main = m.main;
            main.prewarm = true;
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startColor = c;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Box(m, new Vector3(56f, 1f, 16f));
            Velocity(m, new Vector3(0.3f, 0f, -0.05f), new Vector3(0.7f, 0.03f, 0.1f));
            var col = m.colorOverLifetime; col.enabled = true; col.color = Fade(0.25f, 0.75f);
            var rot = m.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            var r = m.GetComponent<ParticleSystemRenderer>();
            r.maxParticleSize = 0.6f; // cap the screen coverage: a close blot can never become a full-screen wall
            r.sortMode = ParticleSystemSortMode.Distance;
            m.Play();
        }
    }
}
