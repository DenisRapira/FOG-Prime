using FOG.WebView2;

internal static class PackagePathTests
{
    internal static void ResolvesPrivateBackend()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FOG layout with spaces"));
        var backend = Path.Combine(root, "app");
        if (PackagePaths.BackendDirectory(root) != backend ||
            PackagePaths.Agent(root) != Path.Combine(backend, "FOG.Agent.exe") ||
            PackagePaths.Engine(root) != Path.Combine(backend, "runtime", "FOG.Engine.exe") ||
            PackagePaths.Manifest(root) != Path.Combine(backend, "runtime.manifest.json"))
            throw new InvalidOperationException("Private backend layout is inconsistent.");
        if (PackagePaths.Agent(root + Path.DirectorySeparatorChar) != PackagePaths.Agent(root))
            throw new InvalidOperationException("Trailing directory separator changed the Agent path.");
    }
}
