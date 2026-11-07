using UnityEditor;

namespace Kagegan.EditorTools
{
    /// <summary>
    /// Import settings for the Quaternius Universal Animation Library (CC0) placed in Resources/Kagegan/Anim,
    /// following the author's Unity setup: Humanoid rig with its own avatar, Bake Axis Conversion,
    /// looping on every clip whose name ends in _Loop. Root motion is baked into the pose (the game moves fighters itself).
    /// </summary>
    public class KageganAnimImport : AssetPostprocessor
    {
        const string Folder = "/Kagegan/Resources/Kagegan/Anim/";

        public override uint GetVersion() => 4;

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains(Folder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.bakeAxisConversion = true;
            mi.importAnimation = true;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.resampleCurves = true;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.Replace('\\', '/').Contains(Folder)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                bool loop = c.name.EndsWith("_Loop") || c.name == "Sword_Idle";
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
