using UnityEngine;

namespace Kagegan
{
    /// <summary>Shared materials and procedurally generated textures. Everything uses vertex colors, so a handful of materials cover the game.</summary>
    public static class Mat
    {
        public static Material ToonWind;
        public static Material Toon, Outline, FxAdd, FxAlpha, FxAddSolid, FxAlphaSolid, Sky, Ghost, Decal;
        public static Texture2D SoftDot, Ring, Crack, Streak, White;
        static bool built;

        public static void Build()
        {
            if (built) return;
            built = true;
            Shader toon = Load("KageToon", "Unlit/Color");
            Shader outline = Load("KageOutline", "Unlit/Color");
            Shader fx = Load("KageFx", "Sprites/Default");
            fxShader = fx;

            SoftDot = MakeRadial(64, (r) => Mathf.Pow(Mathf.Clamp01(1f - r), 2f));
            Ring = MakeRadial(128, (r) => Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) * 7f) * Mathf.Clamp01(r * 1.3f));
            Streak = MakeStreak();
            Crack = MakeCrack(128);
            White = Texture2D.whiteTexture;

            Toon = new Material(toon) { name = "KageToon", enableInstancing = true };
            Outline = new Material(outline) { name = "KageOutline", enableInstancing = true };
            ToonWind = new Material(toon) { name = "KageToonWind" };
            ToonWind.SetFloat("_Wind", 1f);

            FxAdd = MakeFx(fx, SoftDot, true, "FxAdd");
            FxAlpha = MakeFx(fx, SoftDot, false, "FxAlpha");
            FxAddSolid = MakeFx(fx, White, true, "FxAddSolid");
            FxAlphaSolid = MakeFx(fx, White, false, "FxAlphaSolid");
            Ghost = MakeFx(fx, White, true, "Ghost");
            Ghost.SetFloat("_Cull", 2f);
            Decal = MakeFx(fx, Crack, false, "Decal");
            Decal.renderQueue = 2450;

            Sky = MakeFx(fx, White, false, "Sky");
            Sky.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            Sky.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            Sky.SetFloat("_Cull", 1f); // render inside of the dome
            Sky.renderQueue = 1000;
        }

        static Shader Load(string name, string fallback)
        {
            var s = Resources.Load<Shader>("Kagegan/Shaders/" + name);
            if (s == null || !s.isSupported)
            {
                Debug.LogWarning("Kagegan: shader " + name + " missing/unsupported, using " + fallback);
                s = Shader.Find(fallback);
            }
            return s;
        }

        static Shader fxShader;

        /// <summary>Effect material for an imported VFX texture (flipbooks, puffs). Null-safe: returns the soft-dot material if the texture is missing.</summary>
        public static Material FxTex(Texture tex, bool additive, string name)
        {
            if (tex == null) return additive ? FxAdd : FxAlpha;
            return MakeFx(fxShader, tex, additive, name);
        }

        static Material MakeFx(Shader s, Texture tex, bool additive, string name)
        {
            var m = new Material(s) { name = name, mainTexture = tex };
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); // shader outputs premultiplied colour
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = 3000;
            return m;
        }

        static Texture2D MakeRadial(int size, System.Func<float, float> f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    byte a = (byte)(Mathf.Clamp01(f(Mathf.Sqrt(dx * dx + dy * dy))) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D MakeStreak()
        {
            int w = 64, h = 16;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / (w - 1), v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    float a = Mathf.Sin(u * Mathf.PI) * Mathf.Clamp01(1f - v * v);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D MakeCrack(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(30, 22, 18, 0);
            var r = new System.Random(5);
            int c = size / 2;
            for (int arm = 0; arm < 9; arm++)
            {
                float ang = arm / 9f * Mathf.PI * 2f + (float)r.NextDouble() * 0.5f;
                float x = c, y = c;
                int len = size / 3 + r.Next(size / 6);
                for (int s = 0; s < len; s++)
                {
                    ang += ((float)r.NextDouble() - 0.5f) * 0.5f;
                    x += Mathf.Cos(ang); y += Mathf.Sin(ang);
                    int thick = s < len / 3 ? 2 : 1;
                    for (int oy = -thick; oy <= thick; oy++)
                        for (int ox = -thick; ox <= thick; ox++)
                        {
                            int px0 = (int)x + ox, py0 = (int)y + oy;
                            if (px0 < 0 || py0 < 0 || px0 >= size || py0 >= size) continue;
                            px[py0 * size + px0].a = (byte)Mathf.Max(px[py0 * size + px0].a, 230 - s * 2);
                        }
                    if (r.NextDouble() < 0.04) { ang += 0.8f; }
                }
            }
            // dark scorched centre
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size * 0.18f);
                    byte a = (byte)(Mathf.Clamp01(1f - d) * 150);
                    if (a > px[y * size + x].a) px[y * size + x].a = a;
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        public static Color32 C(float r, float g, float b, float emit = 0f) =>
            new Color32((byte)(Mathf.Clamp01(r) * 255), (byte)(Mathf.Clamp01(g) * 255), (byte)(Mathf.Clamp01(b) * 255), (byte)(Mathf.Clamp01(emit) * 255));

        public static Color32 C(Color c, float emit = 0f) => C(c.r, c.g, c.b, emit);
    }
}
