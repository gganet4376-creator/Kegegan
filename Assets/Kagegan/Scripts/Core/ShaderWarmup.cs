using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kagegan
{
    /// <summary>
    /// Warms the core shader variants once per app version, quality preset and graphics device, behind the loading screen.
    /// The Kage shaders run under URP: their untagged main passes are SRPDefaultUnlit, the depth passes are tagged passes.
    /// </summary>
    public static class ShaderWarmup
    {
        static readonly string[] ShaderNames = { "KageToon", "KageSurface", "KageOutline", "KageFx", "KageCapri" };
        const string CacheVersion = "kagegan_shader_warmup_v2";

        static string CacheKey => CacheVersion + "_" + Application.version + "_" + (int)GraphicsQuality.Current + "_" + SystemInfo.graphicsDeviceName + "_" + SystemInfo.graphicsDeviceType;

        public static IEnumerator Run()
        {
            string key = CacheKey;
            if (PlayerPrefs.GetInt(key, 0) == 1) yield break;
            int added = 0;
            foreach (string name in ShaderNames)
            {
                var shader = Resources.Load<Shader>("Kagegan/Shaders/" + name);
                if (shader == null || !shader.isSupported)
                {
                    Debug.LogWarning("Kagegan: shader warmup skipped missing or unsupported " + name);
                    continue;
                }
                var variants = new ShaderVariantCollection();
                bool lit = name == "KageToon" || name == "KageSurface";
                bool instanced = lit || name == "KageOutline";
                AddAll(variants, shader, instanced);
                if (lit) AddAll(variants, shader, instanced, "KAGE_CLIP");
                if (name == "KageSurface")
                {
                    AddAll(variants, shader, true, "KAGE_SURF_LITE");
                    AddAll(variants, shader, true, "KAGE_SURF_MID");
                    AddAll(variants, shader, true, "KAGE_SURF_LITE", "KAGE_CLIP");
                    AddAll(variants, shader, true, "KAGE_SURF_MID", "KAGE_CLIP");
                }
                added += variants.variantCount;
                try { if (variants.variantCount > 0) variants.WarmUp(); }
                catch (Exception e) { Debug.LogWarning("Kagegan: shader warmup failed for " + name + ": " + e.Message); }
                UnityEngine.Object.Destroy(variants);
                // give the loading screen a frame between shaders
                yield return null;
            }
            // cache even a partial result: a variant the device can't take will not appear later either,
            // and retrying on every launch would only slow the start down
            if (added == 0) Debug.LogWarning("Kagegan: shader warmup registered no variants");
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }

        static readonly PassType[] Passes = { PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline, PassType.Normal };

        static void AddAll(ShaderVariantCollection c, Shader s, bool instanced, params string[] keywords)
        {
            TryAdd(c, s, keywords);
            if (!instanced) return;
            var withInst = new string[keywords.Length + 1];
            keywords.CopyTo(withInst, 0);
            withInst[keywords.Length] = "INSTANCING_ON";
            TryAdd(c, s, withInst);
        }

        static void TryAdd(ShaderVariantCollection c, Shader s, string[] keywords)
        {
            // a pass type or keyword set the shader doesn't have throws: skip that one, keep the rest
            foreach (var p in Passes)
            {
                try { c.Add(new ShaderVariantCollection.ShaderVariant(s, p, keywords)); }
                catch (ArgumentException) { }
            }
        }
    }
}
