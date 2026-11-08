using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Grass trampling. Feeds up to 12 contact points into the toon shader (_KageBend / _KageBendK), where grass
    /// blades lean away from them (KageToon.shader, KageWind). Contacts come from:
    ///   - every grounded fighter's feet (live), lying bodies press a wider patch;
    ///   - a short trail behind running / dashing fighters that springs back over ~1.2 s;
    ///   - one-shot impacts: landings, falls, slides (Fx.Dust), cracks, ground shockwaves and explosions,
    ///     whose radius grows outward while the strength fades.
    /// Low: off. Medium: 6 points. High / Ultra: 12.
    /// Also sets the grass draw distance (_KageGrassFade) from the player's grass setting.
    /// </summary>
    public class GrassBend : MonoBehaviour
    {
        struct Pt { public Vector3 p; public float r0, r1, k, life, max; }

        static GrassBend inst;
        const int MaxOut = 12;
        readonly Pt[] pts = new Pt[32];
        readonly Vector4[] outP = new Vector4[MaxOut];
        readonly float[] outK = new float[MaxOut];
        readonly float[] outScore = new float[MaxOut];
        readonly System.Collections.Generic.Dictionary<Fighter, Vector3> lastTrail = new System.Collections.Generic.Dictionary<Fighter, Vector3>();
        int outN;

        static readonly int IdBend = Shader.PropertyToID("_KageBend");
        static readonly int IdK = Shader.PropertyToID("_KageBendK");
        static readonly int IdCount = Shader.PropertyToID("_KageBendCount");
        static readonly int IdFade = Shader.PropertyToID("_KageGrassFade");

        public static void Init(GameObject host)
        {
            if (inst == null) inst = host.AddComponent<GrassBend>();
        }

        public static int Slots
        {
            get
            {
                switch (GraphicsQuality.Current)
                {
                    case GraphicsPreset.Low: return 0;
                    case GraphicsPreset.Medium: return 6;
                    default: return MaxOut;
                }
            }
        }

        /// <summary>One-shot contact: the pressed patch grows from r0 to r1 metres while it fades over `life` seconds.</summary>
        public static void Impact(Vector3 p, float r0, float r1, float life, float strength = 1f)
        {
            if (inst == null || Slots == 0 || life <= 0f) return;
            if (p.y > 1.5f) return; // high in the air: nothing reaches the grass
            var a = inst.pts;
            int slot = 0; float worst = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].life <= 0f) { slot = i; break; }
                float left = a[i].life * a[i].k;
                if (left < worst) { worst = left; slot = i; }
            }
            a[slot] = new Pt { p = p, r0 = Mathf.Max(0.1f, r0), r1 = Mathf.Max(r0, r1), k = Mathf.Clamp01(strength), life = life, max = life };
        }

        static readonly float[] densityMul = { 0.5f, 1f, 1.4f, 1.8f };
        static readonly float[] distanceMul = { 0.7f, 1f, 1.25f, 1.5f };
        static int Level => Mathf.Clamp(Save.Data.grassLevel, 0, 3);
        /// <summary>Player's grass setting: multiplies the number of tufts (applied when an arena is built).</summary>
        public static float DensityMul => densityMul[Level];
        public static float DistanceMul => distanceMul[Level];
        public static string LevelName => new[] { "Мало", "Норма", "Густо", "Максимум" }[Level];

        /// <summary>Grass draw distance in metres from the settings (Low uses a short one).</summary>
        public static void ApplyDistance()
        {
            float far;
            switch (GraphicsQuality.Current)
            {
                case GraphicsPreset.Low: far = 16f; break;
                case GraphicsPreset.Medium: far = 24f; break;
                case GraphicsPreset.High: far = 34f; break;
                default: far = 44f; break;
            }
            far *= DistanceMul;
            Shader.SetGlobalVector(IdFade, new Vector4(far * 0.7f, far, 0f, 0f));
        }

        void Start() { ApplyDistance(); GraphicsQuality.Changed += ApplyDistance; }
        void OnDestroy() { GraphicsQuality.Changed -= ApplyDistance; if (inst == this) inst = null; }

        void LateUpdate()
        {
            int max = Slots;
            float dt = Time.deltaTime;
            outN = 0;
            if (max == 0)
            {
                Shader.SetGlobalFloat(IdCount, 0f);
                return;
            }
            var cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;

            // live feet + trails
            var all = Fighter.All;
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                if (f == null || !f.isActiveAndEnabled) continue;
                Vector3 p = f.transform.position;
                bool lying = f.State == FState.Knockdown || f.State == FState.Dead || f.State == FState.Mount;
                if (!f.Grounded && !lying) { lastTrail.Remove(f); continue; }
                float r = lying ? f.Height * 0.55f : 0.55f;
                Add(p, r, 1f, (p - cp).sqrMagnitude, max);
                // the patch they just left recovers slowly instead of popping back up
                if (!lastTrail.TryGetValue(f, out var last) || (p - last).sqrMagnitude > 0.45f * 0.45f)
                {
                    if (lastTrail.ContainsKey(f)) Impact(p, 0.5f, 0.55f, 1.3f, 0.85f);
                    lastTrail[f] = p;
                }
            }

            // impacts and trails
            for (int i = 0; i < pts.Length; i++)
            {
                if (pts[i].life <= 0f) continue;
                pts[i].life -= dt;
                if (pts[i].life <= 0f) continue;
                float t = 1f - pts[i].life / pts[i].max;          // 0 -> 1
                float r = Mathf.Lerp(pts[i].r0, pts[i].r1, 1f - (1f - t) * (1f - t));
                float k = pts[i].k * (1f - t * t * (3f - 2f * t)); // smooth spring-back
                if (k < 0.02f) continue;
                Add(pts[i].p, r, k, (pts[i].p - cp).sqrMagnitude, max);
            }

            for (int i = outN; i < MaxOut; i++) { outP[i] = Vector4.zero; outK[i] = 0f; }
            Shader.SetGlobalVectorArray(IdBend, outP);
            Shader.SetGlobalFloatArray(IdK, outK);
            Shader.SetGlobalFloat(IdCount, outN);

            // forget fighters that are gone
            if (lastTrail.Count > all.Count + 4) lastTrail.Clear();
        }

        /// <summary>Keeps the strongest contacts closest to the camera.</summary>
        void Add(Vector3 p, float r, float k, float camDist2, int max)
        {
            float score = k / (1f + camDist2 * 0.002f);
            int slot;
            if (outN < max) slot = outN++;
            else
            {
                slot = 0;
                for (int i = 1; i < outN; i++) if (outScore[i] < outScore[slot]) slot = i;
                if (outScore[slot] >= score) return;
            }
            outP[slot] = new Vector4(p.x, p.y, p.z, r);
            outK[slot] = k;
            outScore[slot] = score;
        }
    }
}
