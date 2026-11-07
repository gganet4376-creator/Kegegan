using UnityEditor;

namespace Kagegan.EditorTools
{
    /// <summary>Import settings for the Quaternius Stylized Nature MegaKit (CC0) models in Resources/Kagegan/Nature.
    /// Materials keep their names (the game swaps them for its toon shader at runtime); no animation, cameras or lights.</summary>
    public class KageganNatureImport : AssetPostprocessor
    {
        const string Folder = "/Kagegan/Resources/Kagegan/Nature/";
        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains(Folder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
        }
    }
}
