// Installs the packed packages (artifacts/package/release) into tests/SmokeTest the way an app would, and
// checks that photocraft-cli is found and runs in every output layout:
//
//   1. dotnet build (framework-dependent)   -> runtimes/<rid>/native/photocraft-cli
//   2. dotnet publish -r <rid>              -> photocraft-cli next to the app
//   3. dotnet publish -r <rid> single-file  -> photocraft-cli next to the app, not bundled
//   4. PHOTOCRAFT_CLI_PATH override (good and bad path)
//   5. .NET Framework 4.8 build             -> runtimes/win-*/native/photocraft-cli.exe (run on Windows only)
//
// Usage: dotnet run eng/smoke-test.cs -- <rid> [package revision]
//   rid                this machine: linux-x64, linux-arm64, osx-arm64, osx-x64, win-x64, win-x86, win-arm64
//   package revision   1-99, default $PACKAGE_REVISION or 1
// The PhotoCraft version is $PHOTOCRAFT_VERSION, else the one eng/download-cli.cs downloaded (runtimes/.version).
#:package CliWrap
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: dotnet run eng/smoke-test.cs -- <rid> [package revision]");
    return 2;
}

var rid = args[0];
var revision = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("PACKAGE_REVISION") is { Length: > 0 } r ? r : "1";
var root = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, ".."));
var project = Path.Combine(root, "tests", "SmokeTest", "SmokeTest.csproj");
var output = Path.Combine(root, "artifacts", "smoke");
var isWindows = rid.StartsWith("win-", StringComparison.Ordinal);
var exe = isWindows ? "photocraft-cli.exe" : "photocraft-cli";
var app = isWindows ? "SmokeTest.exe" : "SmokeTest";
var packageRid = rid.StartsWith("osx-", StringComparison.Ordinal) ? "osx" : rid; // one universal macOS binary

try
{
    var version = Environment.GetEnvironmentVariable("PHOTOCRAFT_VERSION") is { Length: > 0 } v
        ? v
        : File.ReadAllText(Path.Combine(root, "runtimes", ".version")).Trim();
    var packageVersion = await GetPackageVersionAsync(version, revision);

    // Always test the freshly packed packages, never an older copy from the cache.
    if (Directory.Exists(output))
    {
        Directory.Delete(output, recursive: true);
    }

    Environment.SetEnvironmentVariable("PhotoCraftPackageVersion", packageVersion); // read by tests/SmokeTest

    Step($"1. framework-dependent build: runtimes/{packageRid}/native/{exe}");
    var build = Path.Combine(output, "build");
    await RunAsync("dotnet", "build", project, "-c", "Release", "-f", "net10.0", "-o", build);
    ExpectExecutable(Path.Combine(build, "runtimes", packageRid, "native", exe));
    await RunAsync("dotnet", Path.Combine(build, "SmokeTest.dll"), version);

    Step($"2. publish -r {rid}: {exe} next to the app");
    var publish = Path.Combine(output, "publish-rid");
    await RunAsync("dotnet", "publish", project, "-c", "Release", "-f", "net10.0", "-r", rid, "--self-contained", "false", "-o", publish);
    ExpectExecutable(Path.Combine(publish, exe));
    Expect(!Directory.Exists(Path.Combine(publish, "runtimes")), $"publish -r {rid} still has a runtimes/ folder");
    await RunAsync(Path.Combine(publish, app), version);

    Step($"3. single-file self-contained publish: {exe} next to the app, not bundled");
    var singleFile = Path.Combine(output, "single-file");
    await RunAsync("dotnet", "publish", project, "-c", "Release", "-f", "net10.0", "-r", rid, "--self-contained",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-o", singleFile);
    ExpectExecutable(Path.Combine(singleFile, exe));
    await RunAsync(Path.Combine(singleFile, app), version);

    Step($"4. {exe} from PHOTOCRAFT_CLI_PATH");
    var overridePath = Path.Combine(output, "override", exe);
    Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
    File.Copy(Path.Combine(publish, exe), overridePath);
    var overridden = await Cli.Wrap("dotnet")
        .WithArguments([Path.Combine(build, "SmokeTest.dll"), version])
        .WithEnvironmentVariables(e => e.Set("PHOTOCRAFT_CLI_PATH", overridePath))
        .ExecuteBufferedAsync();
    Console.Write(overridden.StandardOutput);
    Expect(overridden.StandardOutput.Contains(overridePath, StringComparison.Ordinal), "PHOTOCRAFT_CLI_PATH was ignored");
    var missing = await Cli.Wrap("dotnet")
        .WithArguments([Path.Combine(build, "SmokeTest.dll"), version])
        .WithEnvironmentVariables(e => e.Set("PHOTOCRAFT_CLI_PATH", Path.Combine(output, "override", "missing")))
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();
    Expect(missing.ExitCode != 0, "a PHOTOCRAFT_CLI_PATH that doesn't exist was accepted");

    Step("5. .NET Framework 4.8 build: runtimes/win-*/native/photocraft-cli.exe");
    var netfx = Path.Combine(output, "netfx");
    await RunAsync("dotnet", "build", project, "-c", "Release", "-f", "net48", "-o", netfx);
    foreach (var windowsRid in new[] { "win-x64", "win-x86", "win-arm64" })
    {
        Expect(File.Exists(Path.Combine(netfx, "runtimes", windowsRid, "native", "photocraft-cli.exe")),
            $"net48 output lacks runtimes/{windowsRid}/native/photocraft-cli.exe");
    }

    Expect(!Directory.Exists(Path.Combine(netfx, "runtimes", "linux-x64")),
        "net48 output has the Linux binary; the meta-package should only bring Windows ones");
    if (isWindows)
    {
        await RunAsync(Path.Combine(netfx, "SmokeTest.exe"), version);
    }
    else
    {
        Console.WriteLine("(not run: .NET Framework needs Windows)");
    }

    Step($"all smoke tests passed on {rid} (PhotoCraft {version}, packages {packageVersion})");
    return 0;
}
catch (Exception e) when (e is SmokeTestException or CommandExecutionException or IOException)
{
    Console.Error.WriteLine($"FAIL: {e.Message}");
    return 1;
}

// The package version the build gives this upstream version and revision, e.g. 0.8.2 revision 1 -> 0.8.201.
async Task<string> GetPackageVersionAsync(string version, string revision)
{
    var result = await Cli.Wrap("dotnet")
        .WithArguments(["msbuild", Path.Combine(root, "src", "ArtCraft.PhotoCraft", "ArtCraft.PhotoCraft.csproj"),
            "-getProperty:Version", "-getProperty:_PhotoCraftVersionIsValid",
            $"-p:PhotoCraftVersion={version}", $"-p:PackageRevision={revision}"])
        .ExecuteBufferedAsync();
    var properties = JsonDocument.Parse(result.StandardOutput).RootElement.GetProperty("Properties");
    Expect(properties.GetProperty("_PhotoCraftVersionIsValid").GetString() == "true",
        $"'{version}' revision '{revision}' is not a valid PhotoCraft version and package revision (1-99)");
    return properties.GetProperty("Version").GetString()!;
}

static async Task RunAsync(string target, params string[] arguments) =>
    await Cli.Wrap(target)
        .WithArguments(arguments)
        .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
        .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
        .ExecuteAsync();

static void Step(string title) => Console.WriteLine($"{Environment.NewLine}==> {title}");

static void Expect(bool condition, string failure)
{
    if (!condition)
    {
        throw new SmokeTestException(failure);
    }
}

// On Linux and macOS the file must be executable by everyone (755), not only its owner: apps often run as
// another user than the one who built them, e.g. `app` in the .NET container images.
void ExpectExecutable(string path)
{
    Expect(File.Exists(path), $"{path} is missing");
    if (!OperatingSystem.IsWindows())
    {
        const UnixFileMode everyoneExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        Expect((File.GetUnixFileMode(path) & everyoneExecute) == everyoneExecute,
            $"{path} is not executable by everyone (the buildTransitive chmod didn't run)");
    }
}

sealed class SmokeTestException(string message) : Exception(message);
