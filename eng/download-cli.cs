// Downloads the PhotoCraft CLI of one upstream release for every platform, laid out the way it lands in the
// build output of an app that references ArtCraft.PhotoCraft.Runtime:
//
//   runtimes/<rid>/native/photocraft-cli[.exe]   packed by src/runtimes/ArtCraft.PhotoCraft.Runtime.<rid>
//   runtimes/<rid>/licenses/                     the licence files shipped with that build
//   runtimes/.version                            the upstream version, read by the build as PhotoCraftVersion
//
// Each archive is checked against the release's SHA256SUMS.txt before anything is extracted. Archives are
// cached in artifacts/download/<version>/.
//
// Usage: dotnet run eng/download-cli.cs -- <version>      e.g. dotnet run eng/download-cli.cs -- 0.8.2
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

if (args is not [var versionArgument] || !Regex.IsMatch(versionArgument.TrimStart('v'), @"^\d+\.\d+\.\d+$"))
{
    Console.Error.WriteLine("usage: dotnet run eng/download-cli.cs -- <version>   (a stable release, e.g. 0.8.2)");
    return 2;
}

var version = versionArgument.TrimStart('v');
var root = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, ".."));
var runtimes = Path.Combine(root, "runtimes");
var cache = Path.Combine(root, "artifacts", "download", version);
var releaseUrl = $"https://github.com/storytold/photocraft/releases/download/v{version}/";

// Runtime identifier, release asset, path of the CLI inside it.
(string Rid, string Archive, string Executable)[] assets =
[
    ("linux-x64", $"photocraft-{version}-linux-x86_64.tar.gz", $"photocraft-{version}-linux-x86_64/bin/photocraft-cli"),
    ("linux-arm64", $"photocraft-{version}-linux-aarch64.tar.gz", $"photocraft-{version}-linux-aarch64/bin/photocraft-cli"),
    ("osx", $"photocraft-cli-{version}-macos-universal.zip", $"photocraft-cli-{version}-macos-universal/photocraft-cli"),
    ("win-x64", $"photocraft-{version}-windows-x64-portable.zip", $"photocraft-{version}-windows-x64-portable/photocraft-cli.exe"),
    ("win-x86", $"photocraft-{version}-windows-x86-portable.zip", $"photocraft-{version}-windows-x86-portable/photocraft-cli.exe"),
    ("win-arm64", $"photocraft-{version}-windows-arm64-portable.zip", $"photocraft-{version}-windows-arm64-portable/photocraft-cli.exe"),
];

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    Directory.CreateDirectory(cache);

    var sums = ParseSums(await File.ReadAllLinesAsync(await DownloadAsync(http, "SHA256SUMS.txt")));

    // A run that fails halfway leaves no runtimes/.version behind, so the build won't pack a partial set.
    if (Directory.Exists(runtimes))
    {
        Directory.Delete(runtimes, recursive: true);
    }

    await Task.WhenAll(assets.Select(async asset =>
    {
        var archive = await DownloadAsync(http, asset.Archive);
        var sha256 = await VerifyAsync(archive, sums);
        var native = Path.Combine(runtimes, asset.Rid, "native");
        var licenses = Path.Combine(runtimes, asset.Rid, "licenses");
        Directory.CreateDirectory(native);
        Directory.CreateDirectory(licenses);
        var executable = Path.Combine(native, Path.GetFileName(asset.Executable));
        Extract(archive, asset.Executable, executable, licenses);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, (UnixFileMode)0b111_101_101); // 755
        }

        Console.WriteLine($"{asset.Rid,-12} {asset.Archive}  sha256 {sha256}");
    }));

    await File.WriteAllTextAsync(Path.Combine(runtimes, ".version"), version);
    Console.WriteLine($"PhotoCraft {version} -> {runtimes}");
    return 0;
}
catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

// Downloads a release asset into the cache, unless it is already there.
async Task<string> DownloadAsync(HttpClient http, string name)
{
    var path = Path.Combine(cache, name);
    if (File.Exists(path))
    {
        return path;
    }

    using var response = await http.GetAsync(releaseUrl + name, HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException($"{releaseUrl}{name}: HTTP {(int)response.StatusCode}");
    }

    var partial = path + ".partial";
    await using (var file = File.Create(partial))
    {
        await response.Content.CopyToAsync(file);
    }

    File.Move(partial, path, overwrite: true);
    return path;
}

// SHA256SUMS.txt lines are "<sha256>  <file>", or "<sha256> *<file>" in binary mode.
static Dictionary<string, string> ParseSums(string[] lines) =>
    lines.Select(line => line.Split(' ', 2, StringSplitOptions.TrimEntries))
        .Where(parts => parts.Length == 2)
        .ToDictionary(parts => parts[1].TrimStart('*'), parts => parts[0].ToLowerInvariant());

static async Task<string> VerifyAsync(string archive, Dictionary<string, string> sums)
{
    var name = Path.GetFileName(archive);
    if (!sums.TryGetValue(name, out var expected))
    {
        throw new InvalidDataException($"SHA256SUMS.txt has no entry for {name}.");
    }

    string actual;
    await using (var stream = File.OpenRead(archive))
    {
        actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }

    if (actual != expected)
    {
        File.Delete(archive); // the next run downloads it again
        throw new InvalidDataException($"{name} has SHA-256 {actual}, but SHA256SUMS.txt says {expected}. The file was deleted.");
    }

    return actual;
}

// Copies the CLI and the licence files (LICENSE-*, OFL-*.txt) out of the archive, without unpacking the rest
// (it also holds the ~90 MB GUI app).
static void Extract(string archive, string executableEntry, string executable, string licenses)
{
    static bool IsLicense(string name) =>
        name.StartsWith("LICENSE-", StringComparison.Ordinal) ||
        (name.StartsWith("OFL-", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.Ordinal));

    var foundExecutable = false;
    var licenseCount = 0;
    if (archive.EndsWith(".zip", StringComparison.Ordinal))
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == executableEntry)
            {
                entry.ExtractToFile(executable, overwrite: true);
                foundExecutable = true;
            }
            else if (IsLicense(entry.Name))
            {
                entry.ExtractToFile(Path.Combine(licenses, entry.Name), overwrite: true);
                licenseCount++;
            }
        }
    }
    else
    {
        using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            if (name == executableEntry)
            {
                entry.ExtractToFile(executable, overwrite: true);
                foundExecutable = true;
            }
            else if (IsLicense(Path.GetFileName(name)))
            {
                entry.ExtractToFile(Path.Combine(licenses, Path.GetFileName(name)), overwrite: true);
                licenseCount++;
            }
        }
    }

    if (!foundExecutable)
    {
        throw new InvalidDataException($"{Path.GetFileName(archive)} has no {executableEntry}; the upstream layout changed, update eng/download-cli.cs.");
    }

    if (licenseCount == 0)
    {
        throw new InvalidDataException($"{Path.GetFileName(archive)} has no licence files.");
    }
}
