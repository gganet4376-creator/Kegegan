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
        public static float CurrentRenderScale { get; private set; } = 1f;

        static PropertyInfo urpRenderScale;
        static bool urpLookupDone;
        static Vector2Int nativeRes;

        public static GraphicsPreset AutoDetect()
        {
            int ram = SystemInfo.systemMemorySize;
            string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            bool weakGpu = gpu.Contains("powervr") || gpu.Contains("ge8") || gpu.Contains("mali-g52") || gpu.Contains("mali-g51")
                           || gpu.Contains("mali-t") || gpu.Contains("adreno (tm) 5") || gpu.Contains("adreno (tm) 610");
            if (Application.isMobilePlatform)
            {
                if (ram <= 4600 || weakGpu || SystemInfo.processorCount <= 4) return GraphicsPreset.Low;
                if (ram <= 6500) return GraphicsPreset.Medium;
                return GraphicsPreset.High;
            }
            return GraphicsPreset.High;
        }

        public static void Apply(GraphicsPreset p)
        {
            Current = p;
            if (nativeRes == Vector2Int.zero) nativeRes = new Vector2Int(Screen.width, Screen.height);

            switch (p)
            {
                case GraphicsPreset.Low:
                    BaseRenderScale = 0.62f; Outlines = false; DynamicLights = 0; ParticleMul = 0.45f; WeatherMul = 0.3f;
                    MaxActiveEnemies = 3; DebrisPerBreak = 3; GroundDetail = false;
                    QualitySettings.antiAliasing = 0;
                    break;
                case GraphicsPreset.Medium:
                    BaseRenderScale = 0.8f; Outlines = true; DynamicLights = 2; ParticleMul = 0.75f; WeatherMul = 0.65f;
                    MaxActiveEnemies = 4; DebrisPerBreak = 6; GroundDetail = true;
                    QualitySettings.antiAliasing = 0;
                    break;
                default:
                    BaseRenderScale = 1f; Outlines = true; DynamicLights = 4; ParticleMul = 1f; WeatherMul = 1f;
                    MaxActiveEnemies = 5; DebrisPerBreak = 10; GroundDetail = true;
                    QualitySettings.antiAliasing = Application.isMobilePlatform ? 2 : 4;
                    break;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Save.Data.targetFps <= 30 ? 30 : 60;
            QualitySettings.shadows = ShadowQuality.Disable; // characters use cheap blob shadows on every preset
            QualitySettings.skinWeights = SkinWeights.OneBone;
            QualitySettings.lodBias = p == GraphicsPreset.Low ? 0.6f : (p == GraphicsPreset.Medium ? 1f : 1.5f);
            Shader.SetGlobalFloat("_KageDynLightCount", DynamicLights);
            SetRenderScale(BaseRenderScale, true);
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

            float target = Application.targetFrameRate > 0 ? Application.targetFrameRate : 30f;
            float scale = GraphicsQuality.CurrentRenderScale;
            if (Fps < target * 0.88f && scale > 0.5f)
            {
                goodTime = 0f;
                GraphicsQuality.SetRenderScale(scale - 0.06f, false);
            }
            else if (Fps > target * 0.97f)
            {
                goodTime += 1.5f;
                if (goodTime >= 6f && scale < GraphicsQuality.BaseRenderScale)
                {
                    goodTime = 0f;
                    GraphicsQuality.SetRenderScale(Mathf.Min(GraphicsQuality.BaseRenderScale, scale + 0.04f), false);
                }
            }
        }
    }
}
