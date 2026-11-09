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
        /// <summary>Sun shafts through the forest canopy (0 = off).</summary>
        public static int SunShafts = 0;
        /// <summary>Sun through leaves and grass (shader scale, 0 = off).</summary>
        public static float FoliageLight = 1f;
        /// <summary>Fighters' third shadow tone and warm skin edge (0 = off).</summary>
        public static float CharTones = 1f;
        public static float CurrentRenderScale { get; private set; } = 1f;
        /// <summary>How many rescue steps PerfMonitor has taken on this preset (0 = full quality).</summary>
        public static int PerfDegrade { get; private set; }

        /// <summary>
        /// One more step of frame-rate rescue once the resolution is already at its floor (or the phone is heating up):
        /// lighter post-processing and fewer particles, then no MSAA / fewer lights and shadows, then no bloom and
        /// shorter grass. Works on every preset. Returns false when nothing is left.
        /// </summary>
        public static bool DegradeForPerf()
        {
            if (PerfDegrade >= 3) return false;
            PerfDegrade++;
            ApplyDegrade();
            return true;
        }

        /// <summary>Takes one rescue step back (the phone cooled down and holds its frame rate).</summary>
        public static bool RestoreForPerf()
        {
            if (PerfDegrade <= 0) return false;
            int keep = PerfDegrade - 1;
            float scale = CurrentRenderScale;
            Apply(Current);          // back to the preset's own values (fires Changed: weather, shadows, grass refresh)
            SetRenderScale(scale, false);
            PerfDegrade = keep;
            ApplyDegrade();
            return true;
        }

        static void ApplyDegrade()
        {
            int d = PerfDegrade;
            if (d >= 1) { ShadowCasters = Mathf.Min(ShadowCasters, 3); ParticleMul *= 0.75f; SunShafts /= 2; }
            if (d >= 2) { QualitySettings.antiAliasing = 0; ShadowCasters = Mathf.Min(ShadowCasters, 2); DynamicLights = Mathf.Min(DynamicLights, 2); SunShafts = 0; }
            if (d >= 3) { ShadowCasters = Mathf.Min(ShadowCasters, 1); ParticleMul *= 0.75f; DynamicLights = Mathf.Min(DynamicLights, 1); FoliageLight = 0f; CharTones = 0f; }
            PushLookGlobals();
            if (ShadowCasters == 0) Shader.SetGlobalFloat("_KageCasterCount", 0f);
            KageShadowMap.ApplyQuality(Current, d);
            Shader.SetGlobalFloat("_KageDynLightCount", DynamicLights);
            GrassBend.ApplyDistance();
            PostFx.Degrade(d);
        }

        static PropertyInfo urpRenderScale;
        static bool urpLookupDone;
        static Vector2Int nativeRes;

        /// <summary>False when the app runs as a 32-bit (ARMv7) process: big textures are then skipped to save address space.</summary>
        public static bool Is64Bit => System.IntPtr.Size == 8;

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
                // a 32-bit system (ARMv7 APK) is always a budget phone, and the process has under 3 GB of address space
                if (!Is64Bit) return GraphicsPreset.Low;
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

        /// <summary>Frame rate the player chose in settings, limited by the panel.</summary>
        public static int WantedFps()
        {
            int want = Save.Data.targetFps <= 0 ? 60 : Save.Data.targetFps;
            int max = DisplayMaxFps();
            if (want > max) want = max >= 119 ? 120 : max >= 89 ? 90 : 60;
            return want;
        }

        /// <summary>Session-only limit set by the frame-rate governor (0 = none). Never saved.</summary>
        public static int FpsCap { get; private set; }

        static int ApplyFps()
        {
            int want = WantedFps();
            if (FpsCap > 0 && want > FpsCap) want = FpsCap;
            Application.targetFrameRate = want;
            return want;
        }

        /// <summary>Re-applies the target (after returning from background Android may drop the swap interval).</summary>
        public static void ReapplyFps() => ApplyFps();

        /// <summary>Governor step: run at `cap` for this session (0 = back to the chosen rate).</summary>
        public static void CapFps(int cap) { FpsCap = cap; ApplyFps(); }

        /// <summary>The player changed the setting: forget the governor's limit.</summary>
        public static void ResetFpsGovernor() { FpsCap = 0; ApplyFps(); PerfMonitor.ResetGovernor(); }

        public static void Apply(GraphicsPreset p)
        {
            Current = p;
            if (nativeRes == Vector2Int.zero) nativeRes = new Vector2Int(Screen.width, Screen.height);

            switch (p)
            {
                case GraphicsPreset.Low:
                    BaseRenderScale = 0.62f; Outlines = false; DynamicLights = 0; ParticleMul = 0.45f; WeatherMul = 0.3f;
                    MaxActiveEnemies = 3; DebrisPerBreak = 3; GroundDetail = false; ShadowCasters = 0;
                    SunShafts = 0; FoliageLight = 1f; CharTones = 0f;
                    QualitySettings.antiAliasing = 0;
                    break;
                case GraphicsPreset.Medium:
                    BaseRenderScale = 0.8f; Outlines = true; DynamicLights = 2; ParticleMul = 0.75f; WeatherMul = 0.65f;
                    MaxActiveEnemies = 4; DebrisPerBreak = 6; GroundDetail = true; ShadowCasters = 0;
                    SunShafts = 4; FoliageLight = 1f; CharTones = 1f;
                    QualitySettings.antiAliasing = 0;
                    break;
                case GraphicsPreset.High:
                    BaseRenderScale = 0.9f; Outlines = true; DynamicLights = 4; ParticleMul = 1f; WeatherMul = 1f;
                    MaxActiveEnemies = 5; DebrisPerBreak = 10; GroundDetail = true; ShadowCasters = 4;
                    SunShafts = 7; FoliageLight = 1f; CharTones = 1f;
                    QualitySettings.antiAliasing = 2;
                    break;
                default: // Ultra: flagship phones. 0.9 of a 2.6K panel is still sharper than High, and 2x MSAA instead
                         // of 4x: flagship SoCs throttle hard after a few minutes, a lighter frame keeps them cool
                    BaseRenderScale = 0.9f; Outlines = true; DynamicLights = 4; ParticleMul = 1.25f; WeatherMul = 1.4f;
                    MaxActiveEnemies = 5; DebrisPerBreak = 14; GroundDetail = true; ShadowCasters = 6;
                    SunShafts = 10; FoliageLight = 1f; CharTones = 1f;
                    QualitySettings.antiAliasing = 2;
                    break;
            }

            QualitySettings.vSyncCount = 0;
            ApplyFps();
            // sun shadows come from our own shadow maps (KageShadowMap); the old capsule / blob shadows are only a
            // fallback for a GPU that can't render them. Unity's built-in shadows stay off.
            if (KageShadowMap.On) ShadowCasters = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.skinWeights = p == GraphicsPreset.Low ? SkinWeights.OneBone : SkinWeights.TwoBones;
            QualitySettings.anisotropicFiltering = p >= GraphicsPreset.High ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            QualitySettings.lodBias = p == GraphicsPreset.Low ? 0.6f : (p == GraphicsPreset.Medium ? 1f : 1.5f);
            Shader.SetGlobalFloat("_KageDynLightCount", DynamicLights);
            Shader.SetGlobalFloat("_KageCasterStrength", 0.85f);
            PushLookGlobals();
            if (ShadowCasters == 0) Shader.SetGlobalFloat("_KageCasterCount", 0f);
            KageShadowMap.ApplyQuality(p, 0);
            BlobShadow.SetStrength(KageShadowMap.On ? 0f : ShadowCasters > 0 ? 0.8f : 1f);
            SetRenderScale(BaseRenderScale, true);
            PerfDegrade = 0;
            PostFx.Apply(p);
            Changed?.Invoke();
        }

        static void PushLookGlobals()
        {
            Shader.SetGlobalFloat("_KageTransl", FoliageLight);
            Shader.SetGlobalFloat("_KageCharTones", CharTones);
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

        // ---- frame-rate governor ----
        // At 90/120 Hz, Android frame pacing snaps to the next divisor of the panel when a frame is late
        // (120 -> 60 -> 40 -> 30). A target the phone can't hold therefore lands on 30 with uneven frames.
        // When the chosen rate isn't held for a few seconds (or the phone heats up) step down to a rate it can
        // hold - 90 or 60 - for this session only; try the higher rate again later if there is headroom.
        static float govLow, govGood, heatTimer, coolTime; static int govUps, restores;
        public static void ResetGovernor() { govLow = 0f; govGood = 0f; govUps = 0; heatTimer = coolTime = 0f; restores = 0; }

        void Governor(float dt)
        {
            int tgt = Application.targetFrameRate;
            if (tgt <= 0) return;
            int wanted = GraphicsQuality.WantedFps();
            int thermal = PerfProbe.Thermal;
            if (thermal >= 2 && tgt > 60) // moderate heat: 60 is the stable choice, before the OS throttles us to 30
            {
                GraphicsQuality.CapFps(60); govLow = govGood = 0f;
                return;
            }
            if (tgt > 60)
            {
                if (Fps < tgt * 0.8f) govLow += dt; else govLow = Mathf.Max(0f, govLow - dt * 0.5f);
                if (govLow >= 3.5f)
                {
                    bool has90 = System.Array.IndexOf(GraphicsQuality.FpsOptions(), 90) >= 0;
                    GraphicsQuality.CapFps(tgt > 90 && has90 && Fps >= 82f ? 90 : 60);
                    govLow = govGood = 0f;
                }
            }
            // Heat (Android ADPF advice): shed load *before* the OS throttles the clocks, which halves the frame rate
            // for the rest of the session. Headroom >= 0.85 or "moderate" status: one rescue step every 8 s.
            // Cooled down (headroom < 0.65, status light or none) and holding the rate: undo one step every 25 s.
            float head = PerfProbe.Headroom;
            bool hot = thermal >= 2 || (!float.IsNaN(head) && head >= 0.85f);
            bool cool = thermal <= 1 && (float.IsNaN(head) || head < 0.65f);
            heatTimer += dt;
            if (hot)
            {
                coolTime = 0f;
                if (heatTimer >= 8f) { heatTimer = 0f; GraphicsQuality.DegradeForPerf(); }
            }
            else if (cool && GraphicsQuality.PerfDegrade > 0 && restores < 4 && Fps >= Mathf.Min(tgt, 60) * 0.95f)
            {
                coolTime += dt;
                if (coolTime >= 25f) { coolTime = 0f; restores++; GraphicsQuality.RestoreForPerf(); } // capped: no endless ping-pong
            }
            else coolTime = 0f;

            if (tgt < wanted && thermal < 2 && govUps < 2)
            {
                if (Fps > tgt * 0.97f) govGood += dt; else govGood = 0f;
                if (govGood >= 30f) { govUps++; govGood = govLow = 0f; GraphicsQuality.CapFps(0); }
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.5f) return;
            emaDt = Mathf.Lerp(emaDt, dt, 0.05f);
            Fps = 1f / emaDt;
            sumFps += 1.0 / dt; samples++;
            Governor(dt);

            if (!Save.Data.dynamicResolution) return;
            checkTimer += dt;
            if (checkTimer < 1.5f) return;
            checkTimer = 0f;

            // Judge against 60 at most: on a 90/120 Hz phone the game can run at 85 fps and still look smooth,
            // and chasing 120 used to crush the resolution to x0.46 (blurry, "no shaders" look).
            float target = Application.targetFrameRate > 0 ? Mathf.Min(Application.targetFrameRate, 60f) : 60f;
            float scale = GraphicsQuality.CurrentRenderScale;
            float floor = MinScale(GraphicsQuality.Current);
            // every effects step is already taken and the game still crawls (a throttled GPU): resolution is the only
            // lever left, so allow it lower than the preset's usual floor rather than sit at 15-30 fps
            if (GraphicsQuality.PerfDegrade >= 3 && Fps < target * 0.7f) floor = Mathf.Min(floor, 0.55f);
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
                    // ladder finished: let the resolution steps run again down to the emergency floor
                    if (GraphicsQuality.DegradeForPerf() || GraphicsQuality.CurrentRenderScale > 0.551f) { stuckDrops = 0; lastDropFps = 0f; }
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
