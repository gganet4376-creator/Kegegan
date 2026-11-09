using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kagegan
{
    /// <summary>
    /// Real sun shadows from two orthographic depth maps rendered with a command buffer (works in URP and built-in,
    /// independent of URP's own shadow settings, so the Kage shaders keep full control of the look):
    ///  - dynamic map: every fighter, every frame, in a tight box around them. Gives contact shadows at the feet,
    ///    shadows of standing, jumping and lying bodies, and self-shadowing (arms on the torso, hair on the face).
    ///  - static map: the arena's trees, rocks, buildings and props, rendered once per arena and again while a
    ///    broken prop is falling. Cut-out leaves cast leaf-shaped shadows. Ground and grass blades do not cast.
    /// The toon and surface shaders read both maps (KageShadowMap.cginc) with normal-offset bias, PCF filtering,
    /// edge fade and distance fade. Resolution and filtering come from the quality preset; the FPS governor lowers
    /// the filtering first. Replaces the blob, capsule and CPU-baked shadows (those remain only as a fallback for a
    /// GPU without a usable render-texture format).
    /// </summary>
    public class KageShadowMap : MonoBehaviour
    {
        // ---- settings per preset (see ApplyQuality) ----
        struct Cfg { public int DynRes, StatRes, DynFilter, StatFilter; public float FadeStart, FadeEnd, Strength; }
        static Cfg cfg;

        static KageShadowMap inst;
        static bool formatChecked, supported, pack;
        static RenderTextureFormat format;

        /// <summary>Shadow maps are in use (false only when the GPU can render none of the needed formats).</summary>
        public static bool On => Supported;

        public static bool Supported
        {
            get
            {
                if (!formatChecked)
                {
                    formatChecked = true;
                    // depth packed into two 8-bit channels: 16-bit precision on every GPU (half floats only give 11 bits)
                    pack = true;
                    if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RG16)) { format = RenderTextureFormat.RG16; supported = true; }
                    else if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32)) { format = RenderTextureFormat.ARGB32; supported = true; }
                    else if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)) { format = RenderTextureFormat.RHalf; pack = false; supported = true; }
                    supported &= SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
                }
                return supported;
            }
        }

        static readonly int IdDyn = Shader.PropertyToID("_KageSMDyn");
        static readonly int IdStat = Shader.PropertyToID("_KageSMStat");
        static readonly int IdDynM = Shader.PropertyToID("_KageSMDynM");
        static readonly int IdStatM = Shader.PropertyToID("_KageSMStatM");
        static readonly int IdDynP = Shader.PropertyToID("_KageSMDynP");
        static readonly int IdStatP = Shader.PropertyToID("_KageSMStatP");
        static readonly int IdFade = Shader.PropertyToID("_KageSMFade");
        static readonly int IdOn = Shader.PropertyToID("_KageSMOn");
        static readonly int IdPack = Shader.PropertyToID("_KageSMPack");
        static readonly int IdCur = Shader.PropertyToID("_KageSMCur");
        static readonly int IdLightDir = Shader.PropertyToID("_KageLightDir");

        // world region the static map may cover (the lane runs along x, the camera looks along +z)
        static readonly Bounds Region = new Bounds(new Vector3(0f, 14f, 18f), new Vector3(96f, 32f, 96f));

        struct Draw { public Renderer R; public Material M; public int Sub, Pass; }

        CommandBuffer cb;
        RenderTexture dynRT, statRT;
        Matrix4x4 dynM, statM;
        Vector4 dynP, statP;

        Transform sceneryRoot;
        readonly List<Draw> statDraws = new List<Draw>(256);
        readonly List<Draw> dynDraws = new List<Draw>(64);
        readonly List<Bounds> boundsTmp = new List<Bounds>(256);
        readonly Dictionary<Fighter, Draw[]> fighterCache = new Dictionary<Fighter, Draw[]>();
        static readonly Dictionary<Shader, int> passCache = new Dictionary<Shader, int>();
        readonly List<Draw> drawTmp = new List<Draw>(32);
        readonly Vector3[] corners = new Vector3[8];

        bool statDirty;
        float statRefreshUntil, statNextRefresh;
        bool statValid;
        Vector3 statSun;

        // ================= public API =================

        /// <summary>Use the arena under this root as the static shadow casters (called once the arena is built).</summary>
        public static void SetScenery(Transform root)
        {
            if (!On) return;
            var m = Ensure();
            m.sceneryRoot = root;
            m.CollectScenery();
            m.statDirty = true;
        }

        /// <summary>The arena is gone: no static shadows until the next one.</summary>
        public static void ClearScenery()
        {
            if (inst == null) return;
            inst.sceneryRoot = null;
            inst.statDraws.Clear();
            inst.statValid = false;
            inst.statP.w = 0f;
            Shader.SetGlobalVector(IdStatP, inst.statP);
        }

        /// <summary>Scenery moved (a tree or pillar broke and is falling): re-render the static map for a while.</summary>
        public static void SceneryChanged(float seconds)
        {
            if (inst == null) return;
            inst.statDirty = true;
            inst.statRefreshUntil = Mathf.Max(inst.statRefreshUntil, Time.time + seconds);
        }

        /// <summary>Preset (and FPS-governor step) settings. Called by GraphicsQuality.</summary>
        public static void ApplyQuality(GraphicsPreset p, int degrade)
        {
            switch (p)
            {
                // GE8320-class phones: small dynamic map, 2x2 PCF everywhere
                case GraphicsPreset.Low: cfg = new Cfg { DynRes = 512, StatRes = 1024, DynFilter = 2, StatFilter = 2, FadeStart = 34f, FadeEnd = 52f, Strength = 0.88f }; break;
                case GraphicsPreset.Medium: cfg = new Cfg { DynRes = 1024, StatRes = 1024, DynFilter = 2, StatFilter = 2, FadeStart = 40f, FadeEnd = 62f, Strength = 0.9f }; break;
                case GraphicsPreset.High: cfg = new Cfg { DynRes = 1024, StatRes = 2048, DynFilter = 2, StatFilter = 2, FadeStart = 48f, FadeEnd = 75f, Strength = 0.9f }; break;
                default: cfg = new Cfg { DynRes = 2048, StatRes = 2048, DynFilter = 3, StatFilter = 3, FadeStart = 60f, FadeEnd = 90f, Strength = 0.9f }; break;
            }
            // FPS rescue: soften the filtering first, the shadows themselves go last
            if (degrade >= 1) { cfg.DynFilter = Mathf.Min(cfg.DynFilter, 2); cfg.StatFilter = Mathf.Min(cfg.StatFilter, 2); }
            if (degrade >= 2) cfg.StatFilter = Mathf.Min(cfg.StatFilter, 1);
            if (degrade >= 3) { cfg.DynFilter = Mathf.Min(cfg.DynFilter, 1); cfg.FadeEnd *= 0.75f; cfg.FadeStart *= 0.75f; }

            if (!On) { Shader.SetGlobalFloat(IdOn, 0f); return; }
            var m = Ensure();
            m.MakeTargets();
            m.statDirty = true;
            Shader.SetGlobalFloat(IdOn, 1f);
            Shader.SetGlobalFloat(IdPack, pack ? 1f : 0f);
            Shader.SetGlobalVector(IdFade, new Vector4(cfg.FadeStart, cfg.FadeEnd, cfg.Strength, 0f));
        }

        static KageShadowMap Ensure()
        {
            if (inst != null) return inst;
            var go = new GameObject("KageShadowMap");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<KageShadowMap>();
            inst.cb = new CommandBuffer { name = "Kage sun shadow maps" };
            return inst;
        }

        // ================= render targets =================

        void MakeTargets()
        {
            dynRT = Remake(dynRT, cfg.DynRes, "KageShadowDyn");
            statRT = Remake(statRT, cfg.StatRes, "KageShadowStat");
            Shader.SetGlobalTexture(IdDyn, dynRT);
            Shader.SetGlobalTexture(IdStat, statRT);
        }

        static RenderTexture Remake(RenderTexture rt, int res, string name)
        {
            if (rt != null && rt.width == res) return rt;
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(res, res, 16, format, RenderTextureReadWrite.Linear)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false, antiAliasing = 1
            };
            rt.Create();
            return rt;
        }

        void OnDestroy()
        {
            if (dynRT != null) { dynRT.Release(); Destroy(dynRT); }
            if (statRT != null) { statRT.Release(); Destroy(statRT); }
            cb?.Release();
            if (inst == this) { inst = null; Shader.SetGlobalFloat(IdOn, 0f); }
        }

        // ================= casters =================

        static int ShadowPass(Material m)
        {
            if (m == null || m.shader == null) return -1;
            if (!passCache.TryGetValue(m.shader, out int p)) { p = m.FindPass("KageShadow"); passCache[m.shader] = p; }
            return p;
        }

        static int SubMeshCount(Renderer r)
        {
            if (r is SkinnedMeshRenderer sk) return sk.sharedMesh != null ? sk.sharedMesh.subMeshCount : 0;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 0;
        }

        void AddDraws(Renderer r, List<Draw> into)
        {
            int subs = SubMeshCount(r);
            if (subs == 0) return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < subs; i++)
            {
                var m = mats[i];
                int pass = ShadowPass(m);
                if (pass < 0 || m.renderQueue >= 2500) continue; // glass, glows, fx: no shadow
                into.Add(new Draw { R = r, M = m, Sub = i, Pass = pass });
            }
        }

        void CollectScenery()
        {
            statDraws.Clear();
            if (sceneryRoot == null) return;
            foreach (var r in sceneryRoot.GetComponentsInChildren<MeshRenderer>(false))
                if (SceneryShadow.Eligible(r)) AddDraws(r, statDraws);
        }

        Draw[] FighterDraws(Fighter f)
        {
            if (fighterCache.TryGetValue(f, out var d) && d != null)
            {
                bool ok = true;
                for (int i = 0; i < d.Length; i++) if (d[i].R == null) { ok = false; break; }
                if (ok && (Time.frameCount & 63) != 0) return d; // models can be swapped: re-scan now and then
            }
            if (fighterCache.Count > 48) fighterCache.Clear(); // drop entries of destroyed fighters
            drawTmp.Clear();
            foreach (var r in f.GetComponentsInChildren<Renderer>(false))
                if (r is SkinnedMeshRenderer || r is MeshRenderer) AddDraws(r, drawTmp);
            d = drawTmp.ToArray();
            fighterCache[f] = d;
            return d;
        }

        // ================= light-space fitting =================

        static void Basis(Vector3 toSun, out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            fwd = -toSun.normalized;
            Vector3 ref_ = Mathf.Abs(fwd.y) > 0.98f ? Vector3.forward : Vector3.up;
            right = Vector3.Cross(ref_, fwd).normalized;
            up = Vector3.Cross(fwd, right);
        }

        /// <summary>Light-space box (x, y across the sun's view, z along it) of a set of world boxes.</summary>
        bool Fit(List<Bounds> boxes, Vector3 right, Vector3 up, Vector3 fwd, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = -min;
            if (boxes.Count == 0) return false;
            foreach (var b in boxes)
            {
                Vector3 c = b.center, e = b.extents;
                for (int k = 0; k < 8; k++)
                {
                    var p = c + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
                    var q = new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, fwd));
                    min = Vector3.Min(min, q); max = Vector3.Max(max, q);
                }
            }
            return true;
        }

        /// <summary>Builds view / projection / sampling matrices for a square light-space box. Returns world texel size.</summary>
        static float Matrices(Vector3 right, Vector3 up, Vector3 fwd, float cx, float cy, float half, float z0, float range,
                              out Matrix4x4 view, out Matrix4x4 proj, out Matrix4x4 sample)
        {
            var pos = right * cx + up * cy + fwd * z0;
            var rot = Quaternion.LookRotation(fwd, up);
            view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(pos, rot, Vector3.one).inverse;
            proj = Matrix4x4.Ortho(-half, half, -half, half, 0f, range);
            // clip space (-1..1, OpenGL convention) to 0..1 for u, v and depth
            var bias = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
            sample = bias * proj * view;
            return half * 2f;
        }

        void Render(RenderTexture rt, List<Draw> draws, Matrix4x4 view, Matrix4x4 proj, Matrix4x4 sample)
        {
            cb.Clear();
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.white);
            cb.SetViewProjectionMatrices(view, proj);
            cb.SetGlobalMatrix(IdCur, sample);
            for (int i = 0; i < draws.Count; i++)
            {
                var d = draws[i];
                if (d.R == null || !d.R.enabled || !d.R.gameObject.activeInHierarchy) continue;
                cb.DrawRenderer(d.R, d.M, d.Sub, d.Pass);
            }
            Graphics.ExecuteCommandBuffer(cb);
        }

        static Vector3 SunDir()
        {
            Vector3 l = Shader.GetGlobalVector(IdLightDir);
            if (l.sqrMagnitude < 1e-4f) l = new Vector3(0.4f, 0.8f, -0.3f);
            l.Normalize();
            // a sun right on the horizon would stretch shadows across the whole map
            if (l.y < 0.25f) { l.y = 0.25f; l.Normalize(); }
            return l;
        }

        // ================= per frame =================

        void LateUpdate()
        {
            if (!On || dynRT == null) return;
            var sun = SunDir();
            Basis(sun, out var right, out var up, out var fwd);
            RenderStatic(sun, right, up, fwd);
            RenderDynamic(right, up, fwd);
        }

        void RenderDynamic(Vector3 right, Vector3 up, Vector3 fwd)
        {
            dynDraws.Clear();
            boundsTmp.Clear();
            var all = Fighter.All;
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                if (f == null || !f.isActiveAndEnabled) continue;
                var d = FighterDraws(f);
                if (d.Length == 0) continue;
                for (int k = 0; k < d.Length; k++)
                {
                    if (d[k].R == null || !d[k].R.enabled) continue;
                    dynDraws.Add(d[k]);
                    if (k == 0 || d[k].R != d[k - 1].R) boundsTmp.Add(d[k].R.bounds);
                }
            }
            if (dynDraws.Count == 0 || cfg.DynFilter <= 0 || !Fit(boundsTmp, right, up, fwd, out var mn, out var mx))
            {
                dynP.w = 0f;
                Shader.SetGlobalVector(IdDynP, dynP);
                return;
            }
            const float pad = 1.5f;
            float half = Mathf.Max(mx.x - mn.x, mx.y - mn.y) * 0.5f + pad;
            // size in 2 m steps (a growing box would make the texels crawl), at most 40 m across
            half = Mathf.Clamp(Mathf.Ceil(half / 2f) * 2f, 4f, 20f);
            float texel = half * 2f / cfg.DynRes;
            // snap the centre to whole texels so the shadow edges don't shimmer while the box follows the fighters
            float cx = Mathf.Round((mn.x + mx.x) * 0.5f / texel) * texel;
            float cy = Mathf.Round((mn.y + mx.y) * 0.5f / texel) * texel;
            float z0 = mn.z - pad, range = Mathf.Max(4f, mx.z - mn.z + pad * 2f);
            Matrices(right, up, fwd, cx, cy, half, z0, range, out var view, out var proj, out dynM);
            Render(dynRT, dynDraws, view, proj, dynM);
            dynP = Params(texel, range, cfg.DynFilter, 1f / cfg.DynRes);
            Shader.SetGlobalMatrix(IdDynM, dynM);
            Shader.SetGlobalVector(IdDynP, dynP);
        }

        void RenderStatic(Vector3 sun, Vector3 right, Vector3 up, Vector3 fwd)
        {
            if (Vector3.Angle(sun, statSun) > 0.5f) statDirty = true;
            if (Time.time < statRefreshUntil && Time.time >= statNextRefresh) { statDirty = true; statNextRefresh = Time.time + 0.1f; }
            if (!statDirty) return;
            statDirty = false;
            statSun = sun;
            boundsTmp.Clear();
            for (int i = 0; i < statDraws.Count; i++)
            {
                var r = statDraws[i].R;
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (i > 0 && statDraws[i - 1].R == r) continue;
                var b = r.bounds;
                if (!b.Intersects(Region)) continue;
                // only the part inside the play region decides the map's size
                var mn = Vector3.Max(b.min, Region.min); var mx = Vector3.Min(b.max, Region.max);
                var clipped = new Bounds(); clipped.SetMinMax(mn, mx);
                boundsTmp.Add(clipped);
            }
            if (statDraws.Count == 0 || cfg.StatFilter <= 0 || !Fit(boundsTmp, right, up, fwd, out var lo, out var hi))
            {
                statValid = false;
                statP.w = 0f;
                Shader.SetGlobalVector(IdStatP, statP);
                return;
            }
            const float pad = 1f;
            float half = Mathf.Max(hi.x - lo.x, hi.y - lo.y) * 0.5f + pad;
            float texel = half * 2f / cfg.StatRes;
            float cx = (lo.x + hi.x) * 0.5f, cy = (lo.y + hi.y) * 0.5f;
            // casters outside the region still draw (clipped by the box); the depth range has to hold all of them
            float z0 = lo.z - 12f, range = hi.z - lo.z + 24f;
            Matrices(right, up, fwd, cx, cy, half, z0, range, out var view, out var proj, out statM);
            Render(statRT, statDraws, view, proj, statM);
            statValid = true;
            statP = Params(texel, range, cfg.StatFilter, 1f / cfg.StatRes);
            Shader.SetGlobalMatrix(IdStatM, statM);
            Shader.SetGlobalVector(IdStatP, statP);
        }

        /// <summary>x = texel in uv, y = depth bias (0..1 units), z = normal offset (m), w = filter mode.</summary>
        Vector4 Params(float texelWorld, float range, int filter, float texelUv)
        {
            // depth bias: about one texel of slope, never below the storage precision (half floats: 11 bits near 1)
            float precision = pack ? 1f / 65025f : 1f / 2048f;
            float bias = Mathf.Max(texelWorld * 0.8f / range, precision * 2f);
            float wide = filter >= 3 ? 2.5f : filter == 2 ? 1.5f : 1f; // wider filters reach further: offset a bit more
            return new Vector4(texelUv, bias, texelWorld * wide, filter);
        }

        /// <summary>One-line state for the perf overlay.</summary>
        public static string Describe()
        {
            if (!On) return "нет (запасные)";
            if (inst == null || inst.dynRT == null) return "выкл";
            string f(int x) => x >= 3 ? "pcf16" : x == 2 ? "pcf4" : x == 1 ? "1" : "выкл";
            return "карта " + cfg.DynRes + "/" + (inst.statValid ? cfg.StatRes.ToString() : "—") + " " + f(cfg.DynFilter) + "/" + f(cfg.StatFilter);
        }
    }
}
