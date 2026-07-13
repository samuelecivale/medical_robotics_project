using System;
using System.IO;
using UnityEditor;

public static class RCMDiagnosticPlayerBuilder
{
    public static void BuildMacPlayer()
    {
        string output = Environment.GetEnvironmentVariable("RCM_DIAGNOSTIC_BUILD_PATH");
        if (string.IsNullOrEmpty(output))
            output = Path.Combine(Path.GetTempPath(), "MedicalRoboticsDiagnostic.app");
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        string[] scenes = Array.ConvertAll(
            Array.FindAll(EditorBuildSettings.scenes, scene => scene.enabled),
            scene => scene.path);
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development
        };
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("Diagnostic player build failed: " + report.summary.result);
    }
}
