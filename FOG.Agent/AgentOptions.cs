namespace FOG.Agent;

public sealed class AgentOptions
{
    public string RuntimeDirectory { get; } = "runtime";
    public string ManifestPath { get; } = "runtime.manifest.json";
    public string ProfilesPath { get; } = "profiles.json";

    public string ResolveDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FOG Prime");
    }

    public string ResolveFromBase(string path) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
