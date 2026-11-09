using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Pushes sun, ambient, fog and up to 4 short-lived point lights (explosions, fire, chakra) into the toon shader globals.
    /// This is our "dynamic lighting": it costs a few ALU ops per pixel and is switched off on the Low preset.
    /// </summary>
    public class WorldLighting : MonoBehaviour
    {
        struct DynLight { public Vector3 pos; public Color col; public float range, life, maxLife; }

        static WorldLighting inst;
        readonly DynLight[] lights = new DynLight[8];
        readonly Vector4[] posArr = new Vector4[4];
        readonly Vector4[] colArr = new Vector4[4];
        static readonly int IdPos = Shader.PropertyToID("_KageDynPos");
        static readonly int IdCol = Shader.PropertyToID("_KageDynCol");
        static readonly int IdCasters = Shader.PropertyToID("_KageCasters");
        static readonly int IdCasterCount = Shader.PropertyToID("_KageCasterCount");
        readonly Vector4[] casters = new Vector4[6];
        readonly float[] casterDist = new float[6];

        public static void Init(GameObject host) { inst = host.AddComponent<WorldLighting>(); }

        public static void SetSun(Vector3 dirToSun, Color sun, Color ambient)
        {
            Shader.SetGlobalVector("_KageLightDir", dirToSun.normalized);
            Shader.SetGlobalColor("_KageLightColor", sun);
            Shader.SetGlobalColor("_KageAmbient", ambient);
        }

        public static void SetFog(Color color, float start, float end, float max)
        {
            Shader.SetGlobalColor("_KageFogColor", color);
            Shader.SetGlobalVector("_KageFogParams", new Vector4(start, end, max, 0));
        }

        /// <summary>
        /// Height fog and sun haze. heightFog: x = ground height, y = thinning per metre up, z = density (0 = off),
        /// w = camera distance where it starts. fogSun 0..1: the haze turns sun-coloured when looking into the light.
        /// </summary>
        public static void SetAtmosphere(Vector4 heightFog, float fogSun)
        {
            Shader.SetGlobalVector("_KageHeightFog", heightFog);
            Shader.SetGlobalFloat("_KageFogSun", fogSun);
        }

        public static void Flash(Vector3 pos, Color col, float range, float life)
        {
            if (inst == null || GraphicsQuality.DynamicLights == 0) return;
            int slot = 0; float minLife = float.MaxValue;
            for (int i = 0; i < inst.lights.Length; i++)
            {
                if (inst.lights[i].life <= 0f) { slot = i; break; }
                if (inst.lights[i].life < minLife) { minLife = inst.lights[i].life; slot = i; }
            }
            inst.lights[slot] = new DynLight { pos = pos, col = col, range = range, life = life, maxLife = life };
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            int n = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].life <= 0f) continue;
                lights[i].life -= dt;
                if (n < 4 && lights[i].life > 0f)
                {
                    float k = lights[i].life / lights[i].maxLife;
                    posArr[n] = new Vector4(lights[i].pos.x, lights[i].pos.y, lights[i].pos.z, lights[i].range);
                    colArr[n] = lights[i].col * k;
                    n++;
                }
            }
            for (int i = n; i < 4; i++) { posArr[i] = Vector4.zero; colArr[i] = Vector4.zero; }
            Shader.SetGlobalVectorArray(IdPos, posArr);
            Shader.SetGlobalVectorArray(IdCol, colArr);
            Shader.SetGlobalFloat("_KageDynLightCount", Mathf.Min(n, GraphicsQuality.DynamicLights));
            Shader.SetGlobalFloat("_KageOutlineScale", Screen.height / 1080f * 1.15f);
            PushCasters();
        }

        /// <summary>Feeds the fighters nearest to the camera into the capsule sun-shadow (High: 4, Ultra: 6, else off).</summary>
        void PushCasters()
        {
            int max = GraphicsQuality.ShadowCasters;
            int n = 0;
            if (max > 0)
            {
                var cam = Camera.main;
                Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
                var all = Fighter.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var f = all[i];
                    if (f == null || !f.isActiveAndEnabled) continue;
                    Vector3 p = f.transform.position;
                    float d = (p - cp).sqrMagnitude;
                    // lying bodies cast a low hump, not a standing figure
                    bool down = f.State == FState.Knockdown || f.State == FState.Dead;
                    var c = new Vector4(p.x, p.y, p.z, down ? f.Height * 0.32f : f.Height);
                    // keep the closest ones (insertion into a tiny sorted list)
                    int slot = n < max ? n++ : -1;
                    if (slot < 0) { if (d >= casterDist[n - 1]) continue; slot = n - 1; }
                    while (slot > 0 && casterDist[slot - 1] > d) { casters[slot] = casters[slot - 1]; casterDist[slot] = casterDist[slot - 1]; slot--; }
                    casters[slot] = c; casterDist[slot] = d;
                }
            }
            for (int i = n; i < casters.Length; i++) casters[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IdCasters, casters);
            Shader.SetGlobalFloat(IdCasterCount, n);
        }
    }
}
