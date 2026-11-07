using UnityEditor;

namespace Kagegan.EditorTools
{
    /// <summary>Import settings for the Quaternius CC0 kit models in Resources/Kagegan/{Nature,City,Props}.
    /// Materials keep their names (the game swaps them for its toon shader at runtime); no animation, cameras or lights.</summary>
    public class KageganModelImport : AssetPostprocessor
    {
        static readonly string[] Folders = { "/Kagegan/Resources/Kagegan/Nature/", "/Kagegan/Resources/Kagegan/City/", "/Kagegan/Resources/Kagegan/Props/" };
        public override uint GetVersion() => 3;

        static bool InKit(string path)
        {
            path = path.Replace('\\', '/');
            foreach (var f in Folders) if (path.Contains(f)) return true;
            return false;
        }

        void OnPreprocessModel()
        {
            if (!InKit(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            // the game maps materials by their FBX names (MI_Trim_Metal, Leaves_Pine...), not by texture file names
            mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        }
    }
}
