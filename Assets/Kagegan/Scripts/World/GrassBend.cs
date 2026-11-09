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
    /// Low: 2 points. Medium: 6 points. High / Ultra: 12.
    /// Also sets the grass draw distance (_KageGrassFade) from the player's grass setting.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GrassBend : MonoBehaviour
    {
        struct Pt { public Vector3 p; public float r0, r1, k, life, max; }

        static GrassBend inst;
        const int MaxOut = 12;
        readonly Pt[] pts = new Pt[32];
        readonly Vector4[] outP = new Vector4[MaxOut];
        readonly float[] outK = new float[MaxOut];
        readonly float[] outScore = new float[MaxOut];
        struct Contacts
        {
            public Vector3? left, right, body;
        }

        readonly System.Collections.Generic.Dictionary<Fighter, Contacts> contacts =
            new System.Collections.Generic.Dictionary<Fighter, Contacts>();
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
                    case GraphicsPreset.Low: return 2;
                    case GraphicsPreset.Medium: return 6;
                    default: return MaxOut;
                }
            }
        }

        /// <summary>One-shot contact: the pressed patch grows from r0 to r1 metres while it fades over `life` seconds.</summary>
        public static void Impact(Vector3 p, float r0, float r1, float life, float strength = 1f)
        {
            if (inst == null || life <= 0f) return;
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
            if (GraphicsQuality.PerfDegrade >= 3) far *= 0.7f; // heat rescue: fewer far blades to shade
            Shader.SetGlobalVector(IdFade, new Vector4(far * 0.7f, far, 0f, 0f));
        }

        void Start() { ApplyDistance(); GraphicsQuality.Changed += ApplyDistance; }
        void OnDestroy() { GraphicsQuality.Changed -= ApplyDistance; if (inst == this) inst = null; }

        void LateUpdate()
        {
            int max = Slots;
            float dt = Time.deltaTime;
            outN = 0;
            var cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;

            // live feet + trails
            var all = Fighter.All;
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                if (f == null || !f.isActiveAndEnabled) continue;
                Vector3 p = f.transform.position;
                bool lying = f.State == FState.Knockdown || f.State == FState.Dead;
                contacts.TryGetValue(f, out var previous);
                Contacts current = default;
                float bodyRadius = f.Height * 0.55f;
                if (f.Grounded && lying)
                {
                    Vector3 body = f.Rig.BonePos(B.Hips);
                    body.y = p.y;
                    current.body = body;
                    Add(body, bodyRadius, 1f, (body - cp).sqrMagnitude, max);
                }
                else if (f.Grounded && f.State != FState.Mount && f.State != FState.Grabbed)
                {
                    current.left = FootContact(f, B.FootL, p.y, cp, max);
                    current.right = FootContact(f, B.FootR, p.y, cp, max);
                }
                current.left = LeavePrint(previous.left, current.left, 0.44f);
                current.right = LeavePrint(previous.right, current.right, 0.44f);
                current.body = LeavePrint(previous.body, current.body, bodyRadius);
                contacts[f] = current;
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
        }

        public static void Forget(Fighter fighter)
        {
            if (inst != null) inst.contacts.Remove(fighter);
        }

        Vector3? FootContact(Fighter fighter, B bone, float groundY, Vector3 camera, int max)
        {
            Transform foot = fighter.Rig[bone];
            Vector3 p = foot.TransformPoint(new Vector3(0f, -0.035f, 0.06f));
            if (Mathf.Abs(p.y - groundY) > 0.15f) return null;
            p.y = groundY;
            Add(p, 0.44f, 1f, (p - camera).sqrMagnitude, max);
            return p;
        }

        static Vector3? LeavePrint(Vector3? previous, Vector3? current, float radius)
        {
            if (!previous.HasValue) return current;
            if (current.HasValue && (current.Value - previous.Value).sqrMagnitude < 0.28f * 0.28f)
                return previous;
            Impact(previous.Value, radius, radius, 1.2f, 0.8f);
            return current;
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
