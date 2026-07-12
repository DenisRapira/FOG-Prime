namespace FOG.Agent;

public sealed class AgentOptions
{
    public string RuntimeDirectory { get; set; } = "runtime";
    public string ManifestPath { get; set; } = "runtime.manifest.json";
    public string ProfilesPath { get; set; } = "profiles.json";
    public string DataDirectory { get; set; } = "";

    public string ResolveDataDirectory()
    {
        return string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FOG Prime")
            : Path.GetFullPath(DataDirectory);
    }

    public string ResolveFromBase(string path) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
