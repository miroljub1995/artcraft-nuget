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
/// The application directory is <see cref="AppContext.BaseDirectory"/>, then the directory of this assembly
/// when it differs (for example when it is loaded as a plugin).
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
    /// On Linux and macOS the file is also given execute permission if it lacks it (NuGet doesn't keep file
    /// permissions). That is best effort: on a read-only file system the build-time fix from the runtime
    /// package has to have run.
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
        var path = Locate(
            Environment.GetEnvironmentVariable(PathEnvironmentVariable),
            GetSearchDirectories(),
            GetRuntimeIdentifier(os, RuntimeInformation.OSArchitecture),
            os == "win" ? "photocraft-cli.exe" : "photocraft-cli");
        if (os != "win")
        {
            EnsureExecutable(path);
        }

        return path;
    }

    internal static string Locate(
        string? overridePath,
        IEnumerable<string> searchDirectories,
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

        var probed = new List<string>();
        foreach (var directory in searchDirectories)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(directory, "runtimes", runtimeIdentifier, "native", fileName),
                Path.Combine(directory, fileName),
            })
            {
                probed.Add(candidate);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(
            $"{fileName} was not found. Reference the {RuntimePackage} package, or {RuntimePackage}.{runtimeIdentifier} " +
            $"for this platform only, or set {PathEnvironmentVariable}. Looked in:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", probed),
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

    private static IEnumerable<string> GetSearchDirectories()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return baseDirectory;

        // Empty in a single-file app, where AppContext.BaseDirectory already covers it.
#pragma warning disable IL3000
        var assemblyLocation = typeof(PhotoCraftCli).Assembly.Location;
#pragma warning restore IL3000
        var assemblyDirectory = string.IsNullOrEmpty(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation);
        if (assemblyDirectory is not null &&
            !string.Equals(
                Path.GetFullPath(assemblyDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
        {
            yield return assemblyDirectory;
        }
    }

    private static void EnsureExecutable(string path)
    {
        try
        {
#if NET
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var mode = File.GetUnixFileMode(path);
            if ((mode & UnixFileMode.UserExecute) != 0)
            {
                return;
            }

            // Grant execute wherever read is granted: 0644 becomes 0755.
            if ((mode & UnixFileMode.UserRead) != 0) { mode |= UnixFileMode.UserExecute; }
            if ((mode & UnixFileMode.GroupRead) != 0) { mode |= UnixFileMode.GroupExecute; }
            if ((mode & UnixFileMode.OtherRead) != 0) { mode |= UnixFileMode.OtherExecute; }
            File.SetUnixFileMode(path, mode);
#else
            const int X_OK = 1;
            if (NativeMethods.access(path, X_OK) != 0)
            {
                _ = NativeMethods.chmod(path, 0x1ED); // 0755
            }
#endif
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Best effort; starting the process reports the real problem if it is still not executable.
        }
    }

#if !NET
    private static class NativeMethods
    {
        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern int access(string path, int mode);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern int chmod(string path, int mode);
    }
#endif
}
