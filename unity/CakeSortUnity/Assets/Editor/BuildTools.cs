using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CakeSort.EditorTools
{
    public static class BuildTools
    {
        static readonly string[] Scenes = { "Assets/Scenes/Game.unity" };

        [MenuItem("Cake Sort/Reimport Art")]
        public static void ReimportArt()
        {
            AssetDatabase.ImportAsset("Assets/Resources/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }

        /// <summary>macOS player used for automated smoke tests (see README).</summary>
        public static void BuildMac()
        {
            ReimportArt();
            ProjectSetup.Apply();
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 1170;
            PlayerSettings.resizableWindow = true;
            Build(BuildTarget.StandaloneOSX, "Builds/Mac/CakeSort.app");
        }

        [MenuItem("Cake Sort/Build iOS Xcode Project")]
        public static void BuildIOS()
        {
            ReimportArt();
            ProjectSetup.Apply();
            Build(BuildTarget.iOS, "Builds/iOS");
        }

        static void Build(BuildTarget target, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                target = target,
                locationPathName = path,
                options = BuildOptions.None
            });
            var summary = report.summary;
            Debug.Log($"[CakeSort] Build {target}: {summary.result}, {summary.totalErrors} errors, {summary.totalSize / (1024 * 1024)} MB -> {path}");
            if (Application.isBatchMode) EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
