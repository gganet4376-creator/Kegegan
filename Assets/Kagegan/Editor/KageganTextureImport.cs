using UnityEditor;

namespace Kagegan.EditorTools
{
    /// <summary>Import settings for the Poly Haven (CC0) photo textures and the monowire strip: normal maps are flagged as such,
    /// character atlases stay 2048 px and clamped, the 4K skies keep their full 4096 px width, ground/props get trilinear + anisotropic filtering.</summary>
    public class KageganTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 3;

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            bool ground = path.Contains("/Kagegan/Resources/Kagegan/Ground/");
            bool photo = path.Contains("/Kagegan/Resources/Kagegan/Photo/");
            bool sky4k = path.Contains("/Kagegan/Resources/Kagegan/Sky/") && path.Contains("_4k.");
            if (path.EndsWith("/Kagegan/Resources/Kagegan/Vfx/monowire.png"))
            {
                // strip texture for the monowire line: tiles along the wire, clamped across it so the halo never wraps
                var wt = (TextureImporter)assetImporter;
                wt.wrapModeU = UnityEngine.TextureWrapMode.Repeat;
                wt.wrapModeV = UnityEngine.TextureWrapMode.Clamp;
                wt.alphaIsTransparency = true;
                wt.mipmapEnabled = true;
                wt.filterMode = UnityEngine.FilterMode.Bilinear;
                return;
            }
            if (path.Contains("/Kagegan/Resources/Kagegan/Vfx/capri_") || path.Contains("/Kagegan/Resources/Kagegan/Icons/capri_"))
            {
                // Capri flipbook atlas (8x8 cells of 256 px), rift and HUD portrait: clamped, alpha-correct edges, bilinear mips
                var kt = (TextureImporter)assetImporter;
                kt.maxTextureSize = 2048;
                kt.mipmapEnabled = true;
                kt.alphaIsTransparency = true;
                kt.filterMode = UnityEngine.FilterMode.Bilinear;
                kt.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                return;
            }
            if (path.Contains("/Kagegan/Resources/Kagegan/Chars/"))
            {
                // character atlases: 2048 px, clamped (UV islands sit in padded slots), bilinear mips
                var ct = (TextureImporter)assetImporter;
                ct.maxTextureSize = 2048;
                ct.mipmapEnabled = true;
                ct.filterMode = UnityEngine.FilterMode.Bilinear;
                ct.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                ct.alphaSource = TextureImporterAlphaSource.None;
                return;
            }
            if (!ground && !photo && !sky4k) return;
            var ti = (TextureImporter)assetImporter;
            if (sky4k)
            {
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = false;
                ti.wrapModeU = UnityEngine.TextureWrapMode.Repeat;
                ti.wrapModeV = UnityEngine.TextureWrapMode.Clamp;
                return;
            }
            if (path.Contains("_nor."))
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.sRGBTexture = false;
            }
            ti.mipmapEnabled = true;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            ti.maxTextureSize = 2048;
        }
    }
}
