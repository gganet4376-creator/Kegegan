using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kagegan
{
    /// <summary>
    /// Low / Medium / High presets. Everything that costs GPU or CPU on weak phones is driven from here:
    /// render scale, outlines, dynamic lights in the toon shader, particle budgets, weather density,
    /// max simultaneously active enemies and destruction debris count.
    /// Works with URP (render scale via reflection, no compile-time dependency) and falls back to the built-in pipeline.
    /// </summary>
    public static class GraphicsQuality
    {
        public static GraphicsPreset Current { get; private set; } = GraphicsPreset.Medium;
        public static event Action Changed;

        public static float BaseRenderScale = 1f;
        public static bool Outlines = true;
        public static int DynamicLights = 2;
        public static float ParticleMul = 1f;
        public static float WeatherMul = 1f;
        public static int MaxActiveEnemies = 4;
        public static int DebrisPerBreak = 6;
        public static bool GroundDetail = true;
        /// <summary>Fighters casting a capsule sun-shadow on the scenery (0 = blob shadows only).</summary>
        public static int ShadowCasters = 0;
        public static float CurrentRenderScale { get; private set; } = 1f;
        /// <summary>How many rescue steps PerfMonitor has taken on this preset (0 = full quality).</summary>
        public static int PerfDegrade { get; private set; }

        /// <summary>
        /// One more step of frame-rate rescue once the resolution is already at its floor:
        /// lighter post-processing, fewer capsule shadows, then no MSAA / bloom. Returns false when nothing is left.
        /// </summary>
        public static bool DegradeForPerf()
        {
            if (Current < GraphicsPreset.High || PerfDegrade >= 3) return false;
            PerfDegrade++;
            if (PerfDegrade == 1) ShadowCasters = Mathf.Min(ShadowCasters, 2);
            if (PerfDegrade == 2) QualitySettings.antiAliasing = 0;
            PostFx.Degrade(PerfDegrade);
            return true;
        }

        static PropertyInfo urpRenderScale;
        static bool urpLookupDone;
        static Vector2Int nativeRes;

        public static GraphicsPreset AutoDetect()
        {
            int ram = SystemInfo.systemMemorySize;
            string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            bool weakGpu = gpu.Contains("powervr") || gpu.Contains("ge8") || gpu.Contains("mali-g52") || gpu.Contains("mali-g51")
                           || gpu.Contains("mali-t") || gpu.Contains("adreno (tm) 5") || gpu.Contains("adreno (tm) 610");
            bool flagshipGpu = gpu.Contains("immortalis") || gpu.Contains("mali-g92") || gpu.Contains("mali-g72") && gpu.Contains("mc12")
                               || gpu.Contains("adreno (tm) 8") || gpu.Contains("adreno (tm) 7") || gpu.Contains("xclipse");
            if (Application.isMobilePlatform)
            {
                if (ram <= 4600 || weakGpu || SystemInfo.processorCount <= 4) return GraphicsPreset.Low;
                if (ram <= 6500) return GraphicsPreset.Medium;
                if (flagshipGpu && ram >= 9000) return GraphicsPreset.Ultra; // e.g. Vivo X200 Pro mini (Dimensity 9400, Immortalis-G925)
                return GraphicsPreset.High;
            }
            return GraphicsPreset.Ultra;
        }

        /// <summary>Highest refresh rate the display reports (60 if unknown).</summary>
        public static int DisplayMaxFps()
        {
            int best = 60;
            try
            {
#if UNITY_2022_2_OR_NEWER
                double hz = Screen.currentResolution.refreshRateRatio.value;
#else
                double hz = Screen.currentResolution.refreshRate;
#endif
                if (hz > 1) best = Mathf.RoundToInt((float)hz);
                foreach (var r in Screen.resolutions)
                {
#if UNITY_2022_2_OR_NEWER
                    int h = Mathf.RoundToInt((float)r.refreshRateRatio.value);
#else
                    int h = r.refreshRate;
#endif
                    if (h > best) best = h;
                }
            }
            catch { }
            return Mathf.Clamp(best, 30, 144);
        }

        /// <summary>Frame-rate options offered in settings: 30 / 60 / 90 / 120 limited by the panel.</summary>
        public static int[] FpsOptions()
        {
            int max = DisplayMaxFps();
            var list = new System.Collections.Generic.List<int> { 30, 60 };
            if (max >= 89) list.Add(90);
            if (max >= 119) list.Add(120);
            return list.ToArray();
        }

        static int ApplyFps()
        {
            int want = Save.Data.targetFps <= 0 ? 60 : Save.Data.targetFps;
            int max = DisplayMaxFps();
            if (want > max) want = max >= 119 ? 120 : max >= 89 ? 90 : 60;
            Application.targetFrameRate = want;
            return want;
        }

        public static void Apply(GraphicsPreset p)
        {
            Current = p;
            if (nativeRes == Vector2Int.zero) nativeRes = new Vector2Int(Screen.width, Screen.height);

            switch (p)
            {
                case GraphicsPreset.Low:
                    BaseRenderScale = 0.62f; Outlines = false; DynamicLights = 0; ParticleMul = 0.45f; WeatherMul = 0.3f;
                    MaxActiveEnemies = 3; DebrisPerBreak = 3; GroundDetail = false; ShadowCasters = 0;
                    QualitySettings.antiAliasing = 0;
                    break;
                case GraphicsPreset.Medium:
                    BaseRenderScale = 0.8f; Outlines = true; DynamicLights = 2; ParticleMul = 0.75f; WeatherMul = 0.65f;
                    MaxActiveEnemies = 4; DebrisPerBreak = 6; GroundDetail = true; ShadowCasters = 0;
                    QualitySettings.antiAliasing = 0;
                    break;
                case GraphicsPreset.High:
                    BaseRenderScale = 0.9f; Outlines = true; DynamicLights = 4; ParticleMul = 1f; WeatherMul = 1f;
                    MaxActiveEnemies = 5; DebrisPerBreak = 10; GroundDetail = true; ShadowCasters = 4;
                    QualitySettings.antiAliasing = 2;
                    break;
                default: // Ultra: flagship phones (full resolution, 4x MSAA, dense particles and weather)
                    BaseRenderScale = 1f; Outlines = true; DynamicLights = 4; ParticleMul = 1.25f; WeatherMul = 1.4f;
                    MaxActiveEnemies = 5; DebrisPerBreak = 14; GroundDetail = true; ShadowCasters = 6;
                    QualitySettings.antiAliasing = 4;
                    break;
            }

            QualitySettings.vSyncCount = 0;
            ApplyFps();
            QualitySettings.shadows = ShadowQuality.Disable; // characters use cheap blob shadows on every preset
            QualitySettings.skinWeights = SkinWeights.OneBone;
            QualitySettings.anisotropicFiltering = p >= GraphicsPreset.High ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            QualitySettings.lodBias = p == GraphicsPreset.Low ? 0.6f : (p == GraphicsPreset.Medium ? 1f : 1.5f);
            Shader.SetGlobalFloat("_KageDynLightCount", DynamicLights);
            Shader.SetGlobalFloat("_KageCasterStrength", 0.78f);
            if (ShadowCasters == 0) Shader.SetGlobalFloat("_KageCasterCount", 0f);
            BlobShadow.SetStrength(ShadowCasters > 0 ? 0.6f : 1f); // real shadows take over; the blob stays as contact darkening
            SetRenderScale(BaseRenderScale, true);
            PerfDegrade = 0;
            PostFx.Apply(p);
            Changed?.Invoke();
        }

        /// <summary>Sets render scale through URP when available, else changes the backbuffer size (only on preset change).</summary>
        public static void SetRenderScale(float scale, bool allowResolutionChange)
        {
            scale = Mathf.Clamp(scale, 0.45f, 1f);
            CurrentRenderScale = scale;
            if (TrySetUrpRenderScale(scale)) return;
            if (!allowResolutionChange || !Application.isMobilePlatform) return;
            int w = Mathf.RoundToInt(nativeRes.x * scale), h = Mathf.RoundToInt(nativeRes.y * scale);
            Screen.SetResolution(Mathf.Max(640, w), Mathf.Max(360, h), true);
        }

        static bool TrySetUrpRenderScale(float scale)
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return false;
            if (!urpLookupDone)
            {
                urpLookupDone = true;
                urpRenderScale = asset.GetType().GetProperty("renderScale", BindingFlags.Public | BindingFlags.Instance);
            }
            if (urpRenderScale == null || !urpRenderScale.CanWrite) return false;
            try { urpRenderScale.SetValue(asset, scale); return true; }
            catch { return false; }
        }
    }

    /// <summary>Frame-time meter + dynamic resolution: drops render scale when frames run long, raises it back slowly.</summary>
    public class PerfMonitor : MonoBehaviour
    {
        public static float Fps { get; private set; } = 30f;
        public static float AvgFps => samples > 0 ? (float)(sumFps / samples) : Fps;
        static double sumFps; static long samples;

        float emaDt = 1f / 30f;
        float checkTimer, goodTime;

        public static void ResetAverage() { sumFps = 0; samples = 0; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.5f) return;
            emaDt = Mathf.Lerp(emaDt, dt, 0.05f);
            Fps = 1f / emaDt;
            sumFps += 1.0 / dt; samples++;

            if (!Save.Data.dynamicResolution) return;
            checkTimer += dt;
            if (checkTimer < 1.5f) return;
            checkTimer = 0f;

            // Judge against 60 at most: on a 90/120 Hz phone the game can run at 85 fps and still look smooth,
            // and chasing 120 used to crush the resolution to x0.46 (blurry, "no shaders" look).
            float target = Application.targetFrameRate > 0 ? Mathf.Min(Application.targetFrameRate, 60f) : 60f;
            float scale = GraphicsQuality.CurrentRenderScale;
            float floor = MinScale(GraphicsQuality.Current);
            if (Fps < target * 0.88f && scale > floor + 0.001f && stuckDrops < 3)
            {
                goodTime = 0f;
                // the last drops didn't help: either CPU-bound, or the frame time sits just past a vsync step
                // (Android snaps to 120/60/40/30), where small cuts show no change. Try the effects ladder instead.
                if (lastDropFps > 0f && Fps < lastDropFps * 1.04f) stuckDrops++;
                else stuckDrops = 0;
                lastDropFps = Fps;
                if (stuckDrops < 3) GraphicsQuality.SetRenderScale(Mathf.Max(floor, scale - 0.06f), false);
            }
            else if (Fps < target * 0.8f)
            {
                // resolution is at its floor (or cuts stopped helping) and the game still runs well under target:
                // drop the expensive full-screen passes, one step every ~4.5 s
                goodTime = 0f;
                if (++slowChecks >= 3)
                {
                    slowChecks = 0;
                    if (GraphicsQuality.DegradeForPerf()) { stuckDrops = 0; lastDropFps = 0f; }
                }
                return;
            }
            else if (Fps > target * 0.97f)
            {
                slowChecks = 0;
                goodTime += 1.5f;
                lastDropFps = 0f; stuckDrops = 0;
                if (goodTime >= 6f && scale < GraphicsQuality.BaseRenderScale)
                {
                    goodTime = 0f;
                    GraphicsQuality.SetRenderScale(Mathf.Min(GraphicsQuality.BaseRenderScale, scale + 0.04f), false);
                }
            }
        }

        float lastDropFps; int stuckDrops, slowChecks;

        static float MinScale(GraphicsPreset p)
        {
            switch (p)
            {
                case GraphicsPreset.Ultra: return 0.75f; // 0.75 of a 1440p+ panel is still sharper than High
                case GraphicsPreset.High: return 0.7f;
                case GraphicsPreset.Medium: return 0.65f;
                default: return 0.5f;
            }
        }
    }
}
