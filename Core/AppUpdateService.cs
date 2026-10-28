using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EchoMusic.Core;

public sealed record AppReleaseInfo(string Tag, Version? ParsedVersion, string ReleaseUrl, string? InstallerUrl, string? Notes);

/// <summary>Checks GitHub Releases for the PC edition. It never downloads or installs silently.</summary>
public static class AppUpdateService
{
    public static async Task<AppReleaseInfo> CheckLatestAsync(HttpClient http, string repository, CancellationToken ct = default)
    {
        var (owner, repo) = ParseRepository(repository);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EchoMusicPC", DynamicConfigService.CurrentAppVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        response.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(body) ?? throw new InvalidDataException("GitHub returned an empty release response.");
        var tag = root["tag_name"]?.GetValue<string>()?.Trim();
        var releaseUrl = root["html_url"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(releaseUrl))
            throw new InvalidDataException("The latest release is missing its tag or release page URL.");
        string? installer = null;
        if (root["assets"] is JsonArray assets)
        {
            installer = assets.OfType<JsonObject>()
                .Select(x => (Name: x["name"]?.GetValue<string>() ?? "", Url: x["browser_download_url"]?.GetValue<string>()))
                .Where(x => x.Url is { Length: > 0 } && x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Url)
                .FirstOrDefault();
        }
        return new AppReleaseInfo(tag, ParseVersion(tag), releaseUrl, installer, root["body"]?.GetValue<string>());
    }

    public static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, @"\d+(?:\.\d+){1,3}");
        return match.Success && Version.TryParse(match.Value, out var parsed) ? parsed : null;
    }

    private static (string Owner, string Repo) ParseRepository(string value)
    {
        value = (value ?? "").Trim().TrimEnd('/');
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            value = uri.AbsolutePath.Trim('/');
        value = value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(x => x is "." or ".." || x.Contains(':')))
            throw new ArgumentException("Enter a GitHub repository as owner/repository or paste its github.com URL.", nameof(value));
        return (parts[0], parts[1]);
    }
}
