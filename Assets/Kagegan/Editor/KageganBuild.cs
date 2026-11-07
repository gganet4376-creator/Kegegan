using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kagegan.EditorTools
{
    /// <summary>Builds the Android APK. Menu: Kagegan > Build Android APK.
    /// Command line / CI: -executeMethod Kagegan.EditorTools.KageganBuild.BuildAndroidCI [-kageganOutput path.apk]</summary>
    public static class KageganBuild
    {
        public const string DefaultOutput = "Builds/Android/Kagegan.apk";

        [MenuItem("Kagegan/Build Android APK")]
        public static void BuildAndroidMenu()
        {
            var report = BuildAndroid(DefaultOutput, false);
            if (report != null && report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.GetFullPath(DefaultOutput));
        }

        [MenuItem("Kagegan/Build && Run on connected device")]
        public static void BuildAndRun() { BuildAndroid(DefaultOutput, true); }

        public static void BuildAndroidCI()
        {
            string output = DefaultOutput;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-kageganOutput") output = args[i + 1];
            var report = BuildAndroid(output, false);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport BuildAndroid(string output, bool run)
        {
            KageganSetup.EnsureScene();
            KageganSetup.TrySetupURP();
            KageganSetup.ConfigurePlayer();
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Kagegan/Scenes/Main.unity" },
                locationPathName = output,
                target = BuildTarget.Android,
                options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"Kagegan build: {s.result}, size {s.totalSize / (1024f * 1024f):0.0} MB, errors {s.totalErrors}, time {s.totalTime}, output {output}");
            return report;
        }
    }
}
