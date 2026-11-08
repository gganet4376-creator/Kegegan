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

        /// <summary>Weather for an arena from the player's setting. battle = false (training, menu) keeps "auto" clear.</summary>
        public static WeatherType Choose(ArenaId id, bool battle)
        {
            int s = Save.Data != null ? Save.Data.weather : 0;
            if (s >= 2) return (WeatherType)Mathf.Clamp(s - 2, 0, 4);
            if (s == 1) return (WeatherType)Random.Range(0, 5);
            if (!battle) return WeatherType.Clear;
            // auto: the ruined city always has its storm, the rest roll a weighted chance
            float r = Random.value;
            switch (id)
            {
                case ArenaId.City: return WeatherType.Storm;
                case ArenaId.Forest: return r < 0.45f ? WeatherType.Clear : r < 0.72f ? WeatherType.Rain : r < 0.87f ? WeatherType.Fog : WeatherType.Storm;
                case ArenaId.MoonTemple: return r < 0.55f ? WeatherType.Clear : r < 0.8f ? WeatherType.Snow : WeatherType.Fog;
                default: return r < 0.5f ? WeatherType.Clear : r < 0.7f ? WeatherType.Rain : r < 0.88f ? WeatherType.Snow : WeatherType.Fog;
            }
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

        public void Begin(Arena a, WeatherType t)
        {
            arena = a;
            Type = t;
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
                    snow = 0.3f; snowTarget = 0.85f;
                    break;
                case WeatherType.Fog:
                    Mist(new Color(0.85f, 0.87f, 0.9f, 0.32f), 22);
                    wet = wetTarget = 0.15f;
                    break;
            }
            // grass and dirt only get damp: full puddles + sky reflection read as a lake outside the asphalt city
            if (a == null || a.Id != ArenaId.City) { wet *= 0.35f; wetTarget *= 0.35f; }
            Shader.SetGlobalFloat("_KageWet", wet);
            Shader.SetGlobalFloat("_KageSnow", snow);
            GraphicsQuality.Changed += Refresh;
        }

        void Update()
        {
            // the ground gets wetter (or whiter) the longer it rains (or snows)
            if (wet < wetTarget) { wet = Mathf.MoveTowards(wet, wetTarget, Time.deltaTime / 45f); Shader.SetGlobalFloat("_KageWet", wet); }
            if (snow < snowTarget) { snow = Mathf.MoveTowards(snow, snowTarget, Time.deltaTime / 70f); Shader.SetGlobalFloat("_KageSnow", snow); }
        }

        void OnDestroy()
        {
            GraphicsQuality.Changed -= Refresh;
            // a new arena may already have set its own weather this frame
            if (Arena.Current == null || Arena.Current == arena) ResetGlobals();
        }

        void Refresh()
        {
            foreach (var (ps, rate) in systems)
            {
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = rate * GraphicsQuality.WeatherMul;
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
            em.rateOverTime = rate * GraphicsQuality.WeatherMul;
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

        /// <summary>Low drifting mist from the CC0 smoke-puff atlas.</summary>
        void Mist(Color c, int count)
        {
            var tex = Resources.Load<Texture2D>("Kagegan/Vfx/puff_white");
            if (tex == null) return;
            tex.wrapMode = TextureWrapMode.Clamp;
            float life = 24f;
            var m = Layer("Mist", follow, new Vector3(0f, 1.2f, 8f), count / life, life, Mat.FxTex(tex, false, "Mist"));
            var main = m.main;
            main.prewarm = true;
            main.startSize = new ParticleSystem.MinMaxCurve(7f, 14f);
            main.startColor = c;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Box(m, new Vector3(60f, 1.5f, 30f));
            Velocity(m, new Vector3(0.3f, 0f, -0.1f), new Vector3(0.8f, 0.05f, 0.2f));
            var col = m.colorOverLifetime; col.enabled = true; col.color = Fade(0.2f, 0.8f);
            var tsa = m.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
            var rot = m.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            m.GetComponent<ParticleSystemRenderer>().maxParticleSize = 2f;
            m.Play();
        }
    }
}
