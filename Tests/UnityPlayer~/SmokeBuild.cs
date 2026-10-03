using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmokeBuild
{
    public static void Run()
    {
        string backend = Argument("-smokeBackend") ?? "Editor";
        string output = Argument("-smokeOutput") ?? "Build";
        Directory.CreateDirectory(output);
        ProbeLinuxPluginExtension(output);
        SmokeChecks.Run();
        File.WriteAllText(Path.Combine(output, "editor.txt"), "PASS: Editor Mono smoke");
        if (backend == "Editor") return;

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
            backend == "IL2CPP" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.High);
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
        PlayerSettings.allowUnsafeCode = true;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Smoke.unity" },
            target = BuildTarget.StandaloneWindows64,
            locationPathName = Path.Combine(output, "Smoke.exe"),
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception(backend + " player build failed: " + report.summary.result);
        File.WriteAllText(Path.Combine(output, "build.txt"), "PASS: " + backend + " player build");
        Debug.Log("FFmpeg.AutoGen " + backend + " player build passed");
    }

    private static void ProbeLinuxPluginExtension(string output)
    {
        // Verify importer selection only; this temporary native library is never built.
        const string probe = "Assets/libavutil.so.61";
        try
        {
            string source = Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines/LinuxStandaloneSupport/Variations/linux64_player_development_mono/libdecor-cairo.so");
            string format = "ELF";
            if (!File.Exists(source))
            {
                source = "Assets/Plugins/x86_64/avutil-61.dll";
                format = "PE fallback";
            }
            File.Copy(source, probe, true);
            AssetDatabase.ImportAsset(probe, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(probe);
            File.WriteAllText(Path.Combine(output, "linux-importer.txt"),
                format + ": " + (importer == null ? "null" : importer.GetType().Name));
        }
        finally { AssetDatabase.DeleteAsset(probe); }
    }

    private static string Argument(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (arguments[i] == name) return arguments[i + 1];
        return null;
    }
}
