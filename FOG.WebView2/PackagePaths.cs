using System.IO;

namespace FOG.WebView2;

internal static class PackagePaths
{
    internal static string BackendDirectory(string baseDirectory) =>
        Path.GetFullPath(Path.Combine(baseDirectory, "app"));

    internal static string Agent(string baseDirectory) =>
        Path.Combine(BackendDirectory(baseDirectory), "FOG.Agent.exe");

    internal static string Engine(string baseDirectory) =>
        Path.Combine(BackendDirectory(baseDirectory), "runtime", "FOG.Engine.exe");

    internal static string Manifest(string baseDirectory) =>
        Path.Combine(BackendDirectory(baseDirectory), "runtime.manifest.json");
}
