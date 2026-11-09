using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kagegan
{
    /// <summary>
    /// Console-style image pipeline for the High and Ultra presets (URP post-processing, built at runtime):
    /// HDR + bloom so chakra, fire and explosions glow, per-arena colour grade, soft vignette,
    /// background depth of field that follows the camera, MSAA, dithering against sky banding,
    /// and a chromatic-aberration kick on heavy hits. Low and Medium stay untouched (no full-screen passes).
    /// </summary>
    public class PostFx : MonoBehaviour
    {
        static PostFx inst;
        static Camera cam;
        static VolumeProfile profile;
        static Bloom bloom;
        static ColorAdjustments grade;
        static Vignette vignette;
        static DepthOfField dof;
        static ChromaticAberration chroma;
        static ShadowsMidtonesHighlights smh;
        static Tonemapping tone;
        static float kick;

        public static bool Enabled { get; private set; }

        /// <summary>Called once the main camera exists.</summary>
        public static void Attach(Camera c)
        {
            cam = c;
            if (inst == null) inst = c.gameObject.AddComponent<PostFx>();
            Apply(GraphicsQuality.Current);
        }

        /// <summary>Turns the pipeline on or off and sets its quality for the preset.</summary>
        public static void Apply(GraphicsPreset p)
        {
            bool on = p == GraphicsPreset.High || p == GraphicsPreset.Ultra;
            bool ultra = p == GraphicsPreset.Ultra;
            Enabled = on;
            Shader.SetGlobalFloat("_KageGlow", on ? 2.2f : 1f);

            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
            {
                asset.supportsHDR = on;
                asset.msaaSampleCount = on ? 2 : 1; // 4x on a 2.6K panel cost a lot of bandwidth (and heat) for little gain
                // depth of field reads it; on GLES with MSAA the depth texture can cost a whole extra depth pass,
                // so only Ultra pays for it
                asset.supportsCameraDepthTexture = ultra;
            }
            if (cam == null) return;
            cam.allowHDR = on;
            cam.allowMSAA = on;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = on;
                data.antialiasing = AntialiasingMode.None; // MSAA does the edges; keeps the toon lines crisp
                data.dithering = on;
                data.requiresDepthTexture = ultra;
                // a single NaN/Inf pixel (driver quirks) would otherwise bloom into a white blob on HDR targets
                data.stopNaN = on;
            }
            if (!on) { if (inst != null) inst.enabled = false; return; }

            EnsureVolume();
            dof.active = ultra;
            bloom.active = true;
            bloom.intensity.value = ultra ? 0.65f : 0.5f;
            bloom.threshold.value = 1.25f;
            bloom.scatter.value = 0.62f;
            bloom.highQualityFiltering.value = false; // HQ (bicubic) upsampling is a known mobile cost
            bloom.maxIterations.value = 5;
            vignette.intensity.value = 0.22f;
            vignette.smoothness.value = 0.45f;
            dof.mode.value = DepthOfFieldMode.Gaussian;
            dof.gaussianMaxRadius.value = ultra ? 1.1f : 0.8f;
            dof.highQualitySampling.value = false;
            chroma.intensity.value = 0f;
            // HDR -> screen with a soft shoulder: glowing chakra and the sun-lit haze roll off instead of clipping,
            // without ACES's heavy contrast and hue shifts that would dull the anime palette
            tone.mode.value = TonemappingMode.Neutral;
            tone.active = true;
            // film-like split: cool shadows, warm highlights (baked into the grading LUT, no extra pass)
            smh.shadows.value = new Vector4(0.96f, 0.99f, 1.07f, 0f);
            smh.midtones.value = new Vector4(1f, 1f, 1f, 0f);
            smh.highlights.value = new Vector4(1.05f, 1.01f, 0.95f, 0f);
            if (inst != null) inst.enabled = true;
            SetGrade(currentGrade);
        }

        /// <summary>
        /// Frame-rate rescue on High / Ultra (called by PerfMonitor when lowering the resolution was not enough):
        /// 1 = no depth of field, simple bloom; 2 = MSAA off too; 3 = bloom off as well.
        /// Reset by Apply (a preset change).
        /// </summary>
        public static void Degrade(int level)
        {
            if (!Enabled || profile == null) return;
            if (level >= 1)
            {
                dof.active = false;
                bloom.highQualityFiltering.value = false;
                bloom.maxIterations.value = 4;
            }
            if (level >= 2)
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset != null) asset.msaaSampleCount = 1;
                if (cam != null) cam.allowMSAA = false;
            }
            if (level >= 3) bloom.active = false;
        }

        static ArenaId currentGrade = ArenaId.Dojo;

        /// <summary>Colour grade per arena (applied only while post-processing is on).</summary>
        public static void SetGrade(ArenaId id)
        {
            currentGrade = id;
            if (grade == null) return;
            switch (id)
            {
                case ArenaId.Forest: Grade(new Color(1f, 0.98f, 0.92f), 10f, 12f); break;      // warm afternoon
                case ArenaId.City: Grade(new Color(0.9f, 0.95f, 1.06f), 16f, -6f); break;      // cold storm
                case ArenaId.MoonTemple: Grade(new Color(0.95f, 0.92f, 1.06f), 12f, 6f); break; // violet night
                default: Grade(new Color(1.02f, 1f, 0.95f), 8f, 10f); break;                   // bright dojo
            }
        }

        static void Grade(Color filter, float contrast, float saturation)
        {
            grade.colorFilter.value = filter;
            grade.contrast.value = contrast;
            grade.saturation.value = saturation;
            grade.postExposure.value = 0.15f; // offsets the slight darkening of neutral tonemapping
        }

        /// <summary>Brief lens fringe on heavy impacts (scaled like camera shake).</summary>
        public static void Kick(float amount)
        {
            if (!Enabled) return;
            kick = Mathf.Min(1f, kick + amount * 1.6f);
        }

        static void EnsureVolume()
        {
            if (profile != null) return;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            bloom = profile.Add<Bloom>(true);
            grade = profile.Add<ColorAdjustments>(true);
            vignette = profile.Add<Vignette>(true);
            dof = profile.Add<DepthOfField>(true);
            chroma = profile.Add<ChromaticAberration>(true);
            smh = profile.Add<ShadowsMidtonesHighlights>(true);
            tone = profile.Add<Tonemapping>(true);
            var go = new GameObject("PostFxVolume");
            DontDestroyOnLoad(go);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
        }

        void LateUpdate()
        {
            if (!Enabled || dof == null || cam == null) return;
            if (dof.active)
            {
                // keep the fighters sharp, melt the scenery behind them
                Vector3 cp = cam.transform.position;
                float focus = Mathf.Clamp(Mathf.Abs(cp.z) + 0.5f, 3f, 14f);
                if (CameraRig.Inst != null && CameraRig.Inst.MenuMode) focus = CameraRig.Inst.MenuDistance;
                dof.gaussianStart.value = focus + 7f;
                dof.gaussianEnd.value = focus + 32f;
            }
            kick = Mathf.MoveTowards(kick, 0f, Time.unscaledDeltaTime * 2.5f);
            chroma.intensity.value = kick * 0.6f;
        }
    }
}
