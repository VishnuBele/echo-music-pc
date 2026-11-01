using System.Net;
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
        timeout.CancelAfter(TimeSpan.FromSeconds(24));
        var releaseApi = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest";
        string? apiProblem = null;

        // GitHub's unauthenticated REST quota is shared by many users behind the same NAT/VPN.
        // When it is exhausted, do not report a false update failure: use the public Releases page.
        try
        {
            var fromApi = await TryReadApiAsync(http, releaseApi, timeout.Token);
            if (fromApi.Release is not null) return fromApi.Release;
            apiProblem = fromApi.Problem;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { apiProblem = ex.Message; }

        try
        {
            return await ReadLatestFromWebAsync(http, owner, repo, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (timeout.IsCancellationRequested)
                throw new TimeoutException("GitHub Releases did not respond in time. Check your connection and try again.", ex);
            throw new InvalidOperationException(
                $"Could not check GitHub Releases. The API reported: {ShortError(apiProblem)}. The public release page reported: {ShortError(ex.Message)}.", ex);
        }
    }

    private static async Task<(AppReleaseInfo? Release, string? Problem)> TryReadApiAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EchoMusicPC", DynamicConfigService.CurrentAppVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            if ((int)response.StatusCode == 403 &&
                (response.Headers.Contains("X-RateLimit-Remaining") || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)))
                reason = "GitHub's anonymous API rate limit is temporarily exhausted";
            if (response.StatusCode == HttpStatusCode.NotFound)
                reason = "GitHub has no public latest release for this repository";
            return (null, reason);
        }

        try
        {
            var root = JsonNode.Parse(body) ?? throw new InvalidDataException("GitHub returned an empty release response.");
            var tag = root["tag_name"]?.GetValue<string>()?.Trim();
            var releaseUrl = root["html_url"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(releaseUrl))
                return (null, "the GitHub API response did not contain a release tag and URL");
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
            return (new AppReleaseInfo(tag, ParseVersion(tag), releaseUrl, installer, root["body"]?.GetValue<string>()), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, "GitHub returned an unreadable release response: " + ex.Message);
        }
    }

    private static async Task<AppReleaseInfo> ReadLatestFromWebAsync(HttpClient http, string owner, string repo, CancellationToken ct)
    {
        string baseUrl = $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/releases/latest");
        request.Headers.UserAgent.ParseAdd($"Mozilla/5.0 (compatible; EchoMusicPC/{DynamicConfigService.CurrentAppVersion}; +https://github.com)");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"release page returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        string finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? baseUrl + "/releases/latest";
        string tag = ExtractTag(finalUrl);
        if (string.IsNullOrWhiteSpace(tag))
        {
            string html = await response.Content.ReadAsStringAsync(ct);
            var title = Regex.Match(html, @"<title>\s*Release\s+([^<·]+)", RegexOptions.IgnoreCase);
            if (title.Success) tag = WebUtility.HtmlDecode(title.Groups[1].Value).Trim();
        }
        if (string.IsNullOrWhiteSpace(tag))
            throw new InvalidDataException("GitHub's release page did not identify a latest release. The repository may not have a published release yet.");

        string releaseUrl = baseUrl + "/releases/tag/" + Uri.EscapeDataString(tag);
        string? installer = null;
        try
        {
            using var assetsRequest = new HttpRequestMessage(HttpMethod.Get,
                baseUrl + "/releases/expanded_assets/" + Uri.EscapeDataString(tag));
            assetsRequest.Headers.UserAgent.ParseAdd($"Mozilla/5.0 (compatible; EchoMusicPC/{DynamicConfigService.CurrentAppVersion}; +https://github.com)");
            using var assetsResponse = await http.SendAsync(assetsRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (assetsResponse.IsSuccessStatusCode)
            {
                string assetsHtml = await assetsResponse.Content.ReadAsStringAsync(ct);
                installer = ExtractInstallerUrl(assetsHtml, releaseUrl);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* The release page remains useful even if its optional asset fragment is unavailable. */ }

        return new AppReleaseInfo(tag, ParseVersion(tag), releaseUrl, installer, null);
    }

    private static string ExtractTag(string releaseUrl)
    {
        if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return "";
        var match = Regex.Match(uri.AbsolutePath, @"/releases/tag/([^/]+)$", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : "";
    }

    private static string? ExtractInstallerUrl(string html, string releaseUrl)
    {
        var links = new List<(string Name, string Url)>();
        foreach (Match match in Regex.Matches(html, @"\bhref\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase))
        {
            string href = WebUtility.HtmlDecode(match.Groups[1].Value);
            if (!href.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(new Uri(releaseUrl), href, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (!uri.AbsolutePath.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
            links.Add((Path.GetFileName(uri.AbsolutePath), uri.ToString()));
        }
        return links.OrderByDescending(x => x.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Url).FirstOrDefault();
    }

    private static string ShortError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "no details";
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 180 ? text : text[..177] + "…";
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
