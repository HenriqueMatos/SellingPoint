using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SellingPoint.App;

public sealed record ReleaseInfo(Version Version, string Notes, string DownloadUrl, long SizeBytes)
{
    public string SizeText => $"{SizeBytes / 1024.0 / 1024.0:0.0} MB";
}

/// <summary>
/// Asks GitHub what the latest published version is.
///
/// Deliberately only ever reports; it never acts on what it finds. A till that
/// updates itself in the middle of an event, with a queue at the counter, is a
/// far worse outcome than one running a version behind.
/// </summary>
public sealed class UpdateChecker(HttpClient http, string repository)
{
    /// <summary>The version this copy is, read from the assembly rather than written twice.</summary>
    public static Version Current =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v
            ? new Version(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build)
            : new Version(0, 0, 0);

    public async Task<ReleaseInfo?> LatestAsync(CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");

        // GitHub rejects requests without one.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SellingPoint", Current.ToString()));

        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) return null;

        return Parse(await response.Content.ReadAsStringAsync(token));
    }

    /// <summary>
    /// Split out from the request so the shape of GitHub's answer can be tested
    /// without a network.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!TryParseVersion(tag, out var version)) return null;

            // The .exe is the only asset worth offering; a source zip is not an update.
            if (!root.TryGetProperty("assets", out var assets)) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(url)) continue;

                var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                return new ReleaseInfo(version, notes.Trim(), url, size);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Accepts "v1.2.3" and "1.2.3" alike, since tags are written both ways.</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var trimmed = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(trimmed, out var parsed)) return false;

        version = new Version(parsed.Major, parsed.Minor, parsed.Build < 0 ? 0 : parsed.Build);
        return true;
    }

    public static bool IsNewer(Version candidate, Version current) => candidate > current;
}
