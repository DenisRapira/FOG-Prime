using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FOG.WebView2;

public sealed class UpdateChecker : IDisposable
{
    private static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/DenisRapira/FOG-Prime/releases/latest");
    private const int MaximumResponseBytes = 128 * 1024;
    private readonly HttpClient _httpClient;

    public UpdateChecker() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

    internal UpdateChecker(HttpMessageHandler handler)
    {
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
    }

    public async Task<ReleaseUpdate?> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        cancellationToken = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("FOG-Prime-Update-Check");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("Release response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (body.Length + count > MaximumResponseBytes)
                throw new InvalidDataException("Release response is too large.");
            body.Write(buffer, 0, count);
        }
        var release = JsonSerializer.Deserialize<GitHubRelease>(
            body.GetBuffer().AsSpan(0, checked((int)body.Length)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (release is null ||
            release.Draft ||
            release.Prerelease ||
            string.IsNullOrWhiteSpace(release.TagName) ||
            !IsNewerVersion(release.TagName, currentVersion) ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releaseUri) ||
            !IsTrustedReleaseUri(releaseUri))
        {
            return null;
        }

        return new ReleaseUpdate(release.TagName.TrimStart('v', 'V'), releaseUri);
    }

    public static bool IsNewerVersion(string candidate, string current)
    {
        return TryParseVersion(candidate, out var candidateVersion) &&
            TryParseVersion(current, out var currentVersion) &&
            candidateVersion > currentVersion;
    }

    public static bool IsTrustedReleaseUri(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps &&
            uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(
                "/DenisRapira/FOG-Prime/releases/",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var metadata = normalized.IndexOfAny(['-', '+']);
        if (metadata >= 0)
        {
            normalized = normalized[..metadata];
        }

        return Version.TryParse(normalized, out version!);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        bool Draft,
        bool Prerelease);
}

public sealed record ReleaseUpdate(string Version, Uri PageUri);
