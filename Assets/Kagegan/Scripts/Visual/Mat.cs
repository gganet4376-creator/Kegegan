using UnityEngine;

namespace Kagegan
{
    /// <summary>Shared materials and effect textures (CC0, with generated fallbacks). Everything uses vertex colors, so a handful of materials cover the game.</summary>
    public static class Mat
    {
        public static Material ToonWind;
        public static Material ToonTree;
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
            surfaceShader = Resources.Load<Shader>("Kagegan/Shaders/KageSurface");

            // CC0 Kenney Particle Pack textures (Resources/Kagegan/Vfx); the generated ones remain only as a fallback
            SoftDot = LoadVfx("soft_dot") ?? MakeRadial(64, (r) => Mathf.Pow(Mathf.Clamp01(1f - r), 2f));
            Ring = LoadVfx("ring_thin") ?? MakeRadial(128, (r) => Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) * 7f) * Mathf.Clamp01(r * 1.3f));
            Streak = LoadVfx("rain_streak") ?? MakeStreak();
            Crack = LoadVfx("scorch") ?? MakeCrack(128);
            White = Texture2D.whiteTexture;

            Toon = new Material(toon) { name = "KageToon", enableInstancing = true };
            Outline = new Material(outline) { name = "KageOutline", enableInstancing = true };
            ToonWind = new Material(toon) { name = "KageToonWind" };
            ToonWind.SetFloat("_Wind", 1f);
            ToonWind.SetFloat("_Flutter", 0.25f);
            ToonWind.SetFloat("_Cull", 0f); // grass blades are single quads, seen from both sides
            ToonWind.SetFloat("_Translucent", 0.55f); // backlit blades glow
            ToonTree = new Material(toon) { name = "KageToonTree" };
            ToonTree.SetFloat("_Wind", 1f);
            ToonTree.SetFloat("_WindObj", 1f);
            ToonTree.SetFloat("_WindHeight", 11f);
            ToonTree.SetFloat("_Flutter", 0.5f);
            ToonTree.SetFloat("_Translucent", 0.7f);
            ToonTree.SetFloat("_TransGreen", 1f); // trunk and crown share the mesh: only the green crown lets light through
            SetWind(new Vector2(0.9f, 0.45f), 1f);

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
        static Shader surfaceShader;

        /// <summary>High and Ultra swap the vertex-coloured ground, temple tiles and some props for photo textures.</summary>
        public static bool PhotoSurfaces => GraphicsQuality.Current >= GraphicsPreset.High && surfaceShader != null && surfaceShader.isSupported;

        /// <summary>Photo-textured material (Kagegan/Surface): albedo + normal map. tile &gt; 0 projects the texture in
        /// object space with that tile size in metres (meshes without UVs); 0 uses the mesh UVs. Null if unavailable.</summary>
        public static Material Surface(string name, Texture2D albedo, Texture2D normal, Color tint, float tile, float antiTile = 0f, float wet = 0f)
        {
            if (!PhotoSurfaces || albedo == null) return null;
            var m = new Material(surfaceShader) { name = name, enableInstancing = true };
            m.SetTexture("_MainTex", albedo);
            if (normal != null) m.SetTexture("_BumpMap", normal);
            m.SetColor("_Color", tint);
            m.SetFloat("_ObjUV", tile);
            m.SetFloat("_AntiTile", antiTile);
            m.SetFloat("_Wet", wet);
            m.SetFloat("_VColor", 0f); // imported meshes carry no vertex colours (default white = full glow)
            return m;
        }

        /// <summary>Ground and floor tiles are photo-textured on every preset: Low/Medium get the cheap "lite" variant
        /// (albedo only: no normal map, anti-tiling, puddle ripples or sky reflection), High/Ultra the full one.</summary>
        public static bool GroundTextures => surfaceShader != null && surfaceShader.isSupported;

        /// <summary>Ground material: the full Surface on High/Ultra, the lite variant below. Null if the shader is unavailable.</summary>
        public static Material GroundSurface(string name, Texture2D albedo, Texture2D normal, Color tint, float tile, float antiTile = 0f, float wet = 0f)
        {
            if (!GroundTextures || albedo == null) return null;
            bool lite = !PhotoSurfaces;
            var m = new Material(surfaceShader) { name = name + (lite ? "_Lite" : ""), enableInstancing = true };
            m.SetTexture("_MainTex", albedo);
            if (normal != null && !lite) m.SetTexture("_BumpMap", normal);
            m.SetColor("_Color", tint);
            m.SetFloat("_ObjUV", tile);
            m.SetFloat("_AntiTile", lite ? 0f : antiTile);
            m.SetFloat("_Wet", wet);
            // Medium keeps the lite fetch count but mirrors the sky in puddles; Low only darkens wet ground
            if (lite) m.EnableKeyword(GraphicsQuality.Current == GraphicsPreset.Medium ? "KAGE_SURF_MID" : "KAGE_SURF_LITE");
            return m;
        }

        /// <summary>Tint that pulls a photo texture's average colour towards the arena palette (amount 0..1).</summary>
        public static Color TintTowards(Color palette, Color textureMean, float amount)
        {
            Color r = new Color(palette.r / Mathf.Max(0.05f, textureMean.r), palette.g / Mathf.Max(0.05f, textureMean.g), palette.b / Mathf.Max(0.05f, textureMean.b), 1f);
            return Color.Lerp(Color.white, r, amount);
        }

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

        static Texture2D LoadVfx(string name)
        {
            var t = Resources.Load<Texture2D>("Kagegan/Vfx/" + name);
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        static Texture2D blobTex;
        /// <summary>Contact-shadow profile: dense under the feet, long soft falloff (the vfx soft_dot is a glow, too thin for a shadow).</summary>
        public static Texture2D BlobShadowTex => blobTex != null ? blobTex : (blobTex = MakeRadial(64, (r) =>
        {
            float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.1f) / 0.55f));
            float soft = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r));
            return core * 0.55f + soft * soft * 0.45f;
        }));

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

        /// <summary>Global wind for grass and trees: direction on the ground, strength (1 = breeze, 2 = storm).</summary>
        public static void SetWind(Vector2 dir, float strength, float speed = 1f)
        {
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            Shader.SetGlobalVector("_KageWind", new Vector4(dir.x, dir.y, strength, speed));
        }
    }
}
