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
        string platform = Argument("-smokePlatform") ?? "Windows";
        string architecture = Argument("-smokeArchitecture") ?? "x64";
        string compilerConfiguration = Argument("-smokeCompilerConfiguration") ?? "Release";
        string output = Argument("-smokeOutput") ?? "Build";
        Directory.CreateDirectory(output);
        ConfigurePlugins();
        ProbeLinuxPluginExtension(output);
        SmokeChecks.Run();
        File.WriteAllText(Path.Combine(output, "editor.txt"), "PASS: Editor Mono smoke; pointer size=" + IntPtr.Size);
        if (backend == "Editor") return;

        var namedTarget = platform == "Android" ? NamedBuildTarget.Android : NamedBuildTarget.Standalone;
        var buildTarget = platform == "Android" ? BuildTarget.Android :
            architecture == "x86" ? BuildTarget.StandaloneWindows : BuildTarget.StandaloneWindows64;
        PlayerSettings.SetScriptingBackend(namedTarget,
            backend == "IL2CPP" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(namedTarget, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.High);
        PlayerSettings.SetIl2CppCompilerConfiguration(namedTarget,
            (Il2CppCompilerConfiguration)Enum.Parse(typeof(Il2CppCompilerConfiguration), compilerConfiguration));
        PlayerSettings.allowUnsafeCode = true;
        if (platform == "Android")
        {
            PlayerSettings.Android.targetArchitectures = architecture == "armv7" ? AndroidArchitecture.ARMv7 : AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.SetApplicationIdentifier(namedTarget, "com.teammajdata.ffmpegsmoke");
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Smoke.unity" },
            target = buildTarget,
            locationPathName = Path.Combine(output, platform == "Android" ? "Smoke.apk" : "Smoke.exe"),
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception(backend + " player build failed: " + report.summary.result);
        File.WriteAllText(Path.Combine(output, "build.txt"), "PASS: " + platform + " " + architecture + " " + backend +
            " player build; C++ configuration=" + compilerConfiguration);
        Debug.Log("FFmpeg.AutoGen " + platform + " " + architecture + " " + backend + " player build passed");
    }

    private static void ConfigurePlugins()
    {
        foreach (var path in Directory.GetFiles("Assets/Plugins", "*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".dll") && !path.EndsWith(".so")) continue;
            string asset = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(asset) as PluginImporter;
            if (importer == null) throw new Exception("No PluginImporter for " + asset);
            bool editor = asset.Contains("/Windows/x86_64/");
            bool x86 = asset.Contains("/Windows/x86/");
            bool android = asset.Contains("/Android/");
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(editor);
            importer.SetEditorData("CPU", "x86_64");
            importer.SetEditorData("OS", "Windows");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, editor);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, x86);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, android);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            importer.SetPlatformData(BuildTarget.StandaloneWindows, "CPU", "x86");
            if (android)
                importer.SetPlatformData(BuildTarget.Android, "CPU", asset.Contains("/armeabi-v7a/") ? "ARMv7" : "ARM64");
            importer.SaveAndReimport();
        }
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
                source = "Assets/Plugins/Windows/x86_64/avutil-61.dll";
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
