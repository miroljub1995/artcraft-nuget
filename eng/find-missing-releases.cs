// Prints, as a JSON array on the last line of stdout, the upstream PhotoCraft releases to publish now, e.g.
// ["0.9.0","0.8.3"]: stable releases (not drafts, not pre-releases) of at least <min version>, published upstream
// at least <min age hours> ago, with no ArtCraft.PhotoCraft.Runtime package on nuget.org yet. Progress goes to
// stderr.
//
// Usage: dotnet run eng/find-missing-releases.cs -- <min version> <min age hours>
// Uses GITHUB_TOKEN or GH_TOKEN when set (GitHub's anonymous rate limit is 60 requests an hour), and
// GITHUB_API_URL (set by GitHub Actions) for the API address.
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args is not [var minVersionArgument, var minAgeArgument]
    || !TryParseRelease(minVersionArgument, out var minVersion)
    || !int.TryParse(minAgeArgument, out var minAgeHours))
{
    Console.Error.WriteLine("usage: dotnet run eng/find-missing-releases.cs -- <min version> <min age hours>   (e.g. 0.8.2 24)");
    return 2;
}

const string Upstream = "storytold/photocraft";
const string MarkerPackage = "artcraft.photocraft.runtime"; // the meta-package, pushed last

try
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("artcraft-nuget", "1.0"));

    var published = await GetPublishedAsync(http);
    var cutoff = DateTimeOffset.UtcNow.AddHours(-minAgeHours);
    var missing = new List<string>();
    foreach (var release in await GetReleasesAsync(http))
    {
        if (release.Draft || release.Prerelease)
        {
            continue;
        }

        if (!TryParseRelease(release.Tag.TrimStart('v'), out var version))
        {
            Console.Error.WriteLine($"{release.Tag}: skipped, not a plain X.Y.Z version");
        }
        else if (version < minVersion)
        {
            continue;
        }
        else if (published.Contains(version))
        {
            Console.Error.WriteLine($"{version}: already on nuget.org");
        }
        else if (release.PublishedAt > cutoff)
        {
            Console.Error.WriteLine($"{version}: released {release.PublishedAt:u}, waiting until it is {minAgeHours}h old");
        }
        else
        {
            Console.Error.WriteLine($"{version}: to publish");
            missing.Add(version.ToString());
        }
    }

    // Versions are only digits and dots, so they need no JSON escaping.
    Console.WriteLine($"[{string.Join(",", missing.Select(v => $"\"{v}\""))}]");
    return 0;
}
catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static bool TryParseRelease(string text, out Version version) =>
    Version.TryParse(text, out version!) && Regex.IsMatch(text, @"^\d+\.\d+\.\d+$");

// Upstream versions that have packages: the patch number carries a two-digit packaging revision, so package
// versions 0.8.201 and 0.8.202 are both upstream 0.8.2.
static async Task<HashSet<Version>> GetPublishedAsync(HttpClient http)
{
    using var response = await http.GetAsync($"https://api.nuget.org/v3-flatcontainer/{MarkerPackage}/index.json");
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return []; // nothing published yet
    }

    response.EnsureSuccessStatusCode();
    using var index = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return index.RootElement.GetProperty("versions").EnumerateArray()
        .Select(v => Version.TryParse(v.GetString(), out var package) ? package : null)
        .OfType<Version>()
        .Select(package => new Version(package.Major, package.Minor, package.Build / 100))
        .ToHashSet();
}

static async Task<List<Release>> GetReleasesAsync(HttpClient http)
{
    var api = (Environment.GetEnvironmentVariable("GITHUB_API_URL") is { Length: > 0 } url ? url : "https://api.github.com").TrimEnd('/');
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } t ? t : Environment.GetEnvironmentVariable("GH_TOKEN");
    var releases = new List<Release>();
    for (var page = 1; ; page++)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{api}/repos/{Upstream}/releases?per_page=100&page={page}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.EnumerateArray().ToList();
        releases.AddRange(items.Select(r => new Release(
            r.GetProperty("tag_name").GetString()!,
            r.GetProperty("draft").GetBoolean(),
            r.GetProperty("prerelease").GetBoolean(),
            r.GetProperty("published_at").ValueKind == JsonValueKind.Null ? DateTimeOffset.MaxValue : r.GetProperty("published_at").GetDateTimeOffset())));
        if (items.Count < 100)
        {
            return releases;
        }
    }
}

sealed record Release(string Tag, bool Draft, bool Prerelease, DateTimeOffset PublishedAt);
