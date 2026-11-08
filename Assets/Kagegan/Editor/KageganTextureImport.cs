using UnityEditor;

namespace Kagegan.EditorTools
{
    /// <summary>Import settings for the Poly Haven (CC0) photo textures and the monowire strip: normal maps are flagged as such,
    /// the 4K skies keep their full 4096 px width, ground/props get trilinear + anisotropic filtering.</summary>
    public class KageganTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

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
