using System.Runtime.InteropServices;

namespace ArtCraft.PhotoCraft;

/// <summary>
/// Locates the PhotoCraft command-line tool (<c>photocraft-cli</c>) that the
/// <c>ArtCraft.PhotoCraft.Runtime</c> packages put next to the application.
/// </summary>
/// <remarks>
/// The executable is looked for in these places, first match wins:
/// <list type="number">
/// <item>The path in the <see cref="PathEnvironmentVariable"/> environment variable, when set.</item>
/// <item><c>runtimes/&lt;rid&gt;/native/</c> under the application directory: a framework-dependent build or
/// publish without a runtime identifier keeps every platform's binary there.</item>
/// <item>The application directory itself: a publish for one runtime identifier (<c>-r linux-x64</c>)
/// copies the binary there.</item>
/// </list>
/// The application directory is <see cref="AppContext.BaseDirectory"/>, which is also the executable's directory
/// in a single-file app.
/// </remarks>
public static class PhotoCraftCli
{
    /// <summary>
    /// The environment variable that overrides the lookup with the full path of a <c>photocraft-cli</c>
    /// executable, for example one installed in a container image.
    /// </summary>
    public const string PathEnvironmentVariable = "PHOTOCRAFT_CLI_PATH";

    private const string RuntimePackage = "ArtCraft.PhotoCraft.Runtime";

    // PublicationOnly: a failed lookup isn't cached, so the next call retries.
    private static readonly Lazy<string> s_path = new(Locate, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// Gets the absolute path of the <c>photocraft-cli</c> executable for the current operating system and
    /// architecture.
    /// </summary>
    /// <remarks>
    /// The file is only looked up, never changed. On Linux and macOS the runtime package's build targets give it
    /// execute permission when the app is built or published.
    /// </remarks>
    /// <returns>The absolute path of the executable.</returns>
    /// <exception cref="FileNotFoundException">
    /// The executable was not found, or <see cref="PathEnvironmentVariable"/> names a file that doesn't exist.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// PhotoCraft has no build for this operating system and architecture, and
    /// <see cref="PathEnvironmentVariable"/> is not set.
    /// </exception>
    public static string GetPath() => s_path.Value;

    private static string Locate()
    {
        var os = GetOperatingSystem();
        return Locate(
            Environment.GetEnvironmentVariable(PathEnvironmentVariable),
            AppContext.BaseDirectory,
            GetRuntimeIdentifier(os, RuntimeInformation.OSArchitecture),
            os == "win" ? "photocraft-cli.exe" : "photocraft-cli");
    }

    internal static string Locate(
        string? overridePath,
        string appDirectory,
        string? runtimeIdentifier,
        string fileName)
    {
        if (!string.IsNullOrEmpty(overridePath))
        {
            // An explicit setting that's wrong is an error, not a reason to fall back to something else.
            var fullPath = Path.GetFullPath(overridePath);
            return File.Exists(fullPath)
                ? fullPath
                : throw new FileNotFoundException(
                    $"{PathEnvironmentVariable} is set to '{fullPath}', but that file doesn't exist.", fullPath);
        }

        if (runtimeIdentifier is null)
        {
            throw new PlatformNotSupportedException(
                $"PhotoCraft has no build for {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}). " +
                $"Install photocraft-cli yourself and set {PathEnvironmentVariable} to its path.");
        }

        string[] candidates =
        [
            Path.Combine(appDirectory, "runtimes", runtimeIdentifier, "native", fileName),
            Path.Combine(appDirectory, fileName),
        ];
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"{fileName} was not found. Reference the {RuntimePackage} package, or {RuntimePackage}.{runtimeIdentifier} " +
            $"for this platform only, or set {PathEnvironmentVariable}. Looked in:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", candidates),
            fileName);
    }

    /// <summary>
    /// The runtime identifier of the package that has the binary for <paramref name="os"/>/<paramref name="architecture"/>,
    /// or <see langword="null"/> when PhotoCraft has no build for it. The CLI runs in its own process, so this is the
    /// operating system's architecture, not this process's.
    /// </summary>
    internal static string? GetRuntimeIdentifier(string? os, Architecture architecture) => (os, architecture) switch
    {
        ("win", Architecture.X64) => "win-x64",
        ("win", Architecture.X86) => "win-x86",
        ("win", Architecture.Arm64) => "win-arm64",
        ("linux", Architecture.X64) => "linux-x64",
        ("linux", Architecture.Arm64) => "linux-arm64",
        // One universal binary for Apple silicon and Intel.
        ("osx", Architecture.X64 or Architecture.Arm64) => "osx",
        _ => null,
    };

    private static string? GetOperatingSystem() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        null;
}
