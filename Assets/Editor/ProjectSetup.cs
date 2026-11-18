using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Reins.EditorTools
{
    /// <summary>
    /// Project plumbing: regenerates the (single-object) main scene and makes player builds.
    /// Callable from the Reins menu or headless via -executeMethod.
    /// </summary>
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Reins/Rebuild Main Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Game").AddComponent<GameManager>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        public static void Configure()
        {
            PlayerSettings.productName = "Reins";
            PlayerSettings.companyName = "Alexandre Rheault";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
        }

        [MenuItem("Reins/Build Windows Player")]
        public static void BuildWindows()
        {
            Configure();
            if (!File.Exists(ScenePath)) CreateScene();
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Build/Reins.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("Build result: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
