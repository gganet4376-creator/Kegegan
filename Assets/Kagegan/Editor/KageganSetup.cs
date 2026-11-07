using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kagegan.EditorTools
{
    /// <summary>
    /// One-time project setup, runs automatically when the project is first opened (and from the menu):
    ///  • creates Assets/Kagegan/Scenes/Main.unity and puts it in Build Settings (the game boots itself from code);
    ///  • creates a mobile URP asset (no HDR, no MSAA, no shadow maps) and assigns it — via reflection, so the project
    ///    still compiles and runs on the built-in pipeline if URP is missing;
    ///  • configures Android player settings for weak devices (IL2CPP, ARM64, OpenGL ES 3 only, landscape, Gamma).
    /// </summary>
    [InitializeOnLoad]
    public static class KageganSetup
    {
        const string ScenePath = "Assets/Kagegan/Scenes/Main.unity";
        const string SettingsDir = "Assets/Kagegan/Settings";
        const string Marker = "Kagegan.SetupDone.v1";

        static KageganSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                if (!File.Exists(ScenePath) || !EditorPrefs.GetBool(Marker + "." + Application.dataPath, false))
                {
                    RunSetup();
                    EditorPrefs.SetBool(Marker + "." + Application.dataPath, true);
                }
            };
        }

        [MenuItem("Kagegan/Setup Project (scene, URP, Android)")]
        public static void RunSetup()
        {
            EnsureScene();
            TrySetupURP();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("Kagegan: project setup complete. Press Play in Main.unity, or use Kagegan > Build Android APK.");
        }

        /// <summary>Pre-export hook for Unity Build Automation (cloud build):
        /// set "Pre-Export Method Name" to Kagegan.EditorTools.KageganSetup.PreExport</summary>
        public static void PreExport()
        {
            EnsureScene();
            TrySetupURP();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
        }

        public static void EnsureScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (!Application.isBatchMode && EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static void TrySetupURP()
        {
            try
            {
                const string asm = "Unity.RenderPipelines.Universal.Runtime";
                Type assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, " + asm);
                Type rendererType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, " + asm);
                Type baseRendererType = Type.GetType("UnityEngine.Rendering.Universal.ScriptableRendererData, " + asm);
                if (assetType == null || rendererType == null || baseRendererType == null)
                {
                    Debug.LogWarning("Kagegan: URP package not found — running on the built-in pipeline (shaders support both).");
                    return;
                }
                Directory.CreateDirectory(SettingsDir);
                string assetPath = SettingsDir + "/KageganURP.asset";
                string rendererPath = SettingsDir + "/KageganURP_Renderer.asset";
                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(assetPath);
                if (asset == null)
                {
                    var renderer = ScriptableObject.CreateInstance(rendererType);
                    AssetDatabase.CreateAsset(renderer, rendererPath);
                    var create = assetType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { baseRendererType }, null);
                    asset = (RenderPipelineAsset)create.Invoke(null, new object[] { renderer });
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                SetProp(asset, "supportsHDR", false);
                SetProp(asset, "msaaSampleCount", 1);
                SetProp(asset, "renderScale", 1f);
                SetProp(asset, "shadowDistance", 0f);
                SetProp(asset, "supportsMainLightShadows", false);
                SetProp(asset, "supportsCameraDepthTexture", false);
                SetProp(asset, "supportsCameraOpaqueTexture", false);
                EditorUtility.SetDirty(asset);
                GraphicsSettings.defaultRenderPipeline = asset;
                int current = QualitySettings.GetQualityLevel();
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = asset;
                }
                QualitySettings.SetQualityLevel(current, false);
                Debug.Log("Kagegan: URP asset assigned (" + assetPath + ").");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Kagegan: URP setup skipped (" + e.Message + "). The game still runs on the built-in pipeline.");
            }
        }

        static void SetProp(object target, string name, object value)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(target, Convert.ChangeType(value, p.PropertyType)); } catch { }
            }
        }

        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Kagegan";
            PlayerSettings.productName = "Kagegan Mobile";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.kagegan.mobile");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            // PowerVR GE8320 (POCO C61) has unreliable Vulkan drivers; GLES3 is the safe, fast choice.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            // Linux 64-bit test build (auto-test on a headless server with software GL): OpenGL Core first, windowed 1080p
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SplashScreen.show = false;
            SetActiveInputHandlingBoth();
        }

        /// <summary>The game uses the legacy Input Manager (touch, keys, gamepad buttons); make sure it is enabled.</summary>
        static void SetActiveInputHandlingBoth()
        {
            try
            {
                var getter = typeof(Unsupported).GetMethod("GetSerializedAssetInterfaceSingleton", BindingFlags.Public | BindingFlags.Static);
                var ps = getter != null ? getter.Invoke(null, new object[] { "PlayerSettings" }) as UnityEngine.Object : null;
                if (ps == null) return;
                var so = new SerializedObject(ps);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue == 1)
                {
                    prop.intValue = 2; // Both
                    so.ApplyModifiedProperties();
                    Debug.Log("Kagegan: Active Input Handling set to 'Both' (restart the editor if prompted).");
                }
            }
            catch { }
        }
    }
}
