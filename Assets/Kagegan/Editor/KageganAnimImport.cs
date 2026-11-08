using UnityEditor;

namespace Kagegan.EditorTools
{
    /// <summary>
    /// Import settings for the humanoid animation packs (Quaternius UAL, KayKit; CC0) placed in Resources/Kagegan/Anim,
    /// following the author's Unity setup: Humanoid rig with its own avatar, Bake Axis Conversion,
    /// looping on every clip whose name ends in _Loop. Root motion is baked into the pose (the game moves fighters itself).
    /// </summary>
    public class KageganAnimImport : AssetPostprocessor
    {
        const string Folder = "/Kagegan/Resources/Kagegan/Anim/";

        public override uint GetVersion() => 5;

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
            // KayKit dodges/attacks travel up to ~1.8 m; the game moves fighters itself, so that travel must stay out of the pose
            bool travelToRoot = assetPath.Replace('\\', '/').Contains(Folder + "KK_");
            foreach (var c in clips)
            {
                bool loop = c.name.EndsWith("_Loop") || c.name == "Sword_Idle"
                            || c.name == "Running_Strafe_Left" || c.name == "Running_Strafe_Right" || c.name == "Walking_Backwards";
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = !travelToRoot; // unlocked = horizontal travel becomes root motion, which fighters ignore
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = !travelToRoot; // KayKit travels on the hips, so root motion is taken from the centre of mass
            }
            mi.clipAnimations = clips;
        }
    }
}
