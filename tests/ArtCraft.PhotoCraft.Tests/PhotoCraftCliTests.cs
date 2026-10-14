using System.Runtime.InteropServices;

namespace ArtCraft.PhotoCraft.Tests;

public sealed class PhotoCraftCliTests : IDisposable
{
    private const string FileName = "photocraft-cli";

    private readonly string _root = Directory.CreateTempSubdirectory("photocraft-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("win", Architecture.Arm64, new[] { "win-arm64", "win-x64", "win-x86" })]
    [InlineData("win", Architecture.X64, new[] { "win-x64", "win-x86" })]
    [InlineData("win", Architecture.X86, new[] { "win-x86" })]
    [InlineData("osx", Architecture.Arm64, new[] { "osx-arm64", "osx" })]
    [InlineData("osx", Architecture.X64, new[] { "osx-x64", "osx" })]
    [InlineData("linux", Architecture.X64, new[] { "linux-x64" })]
    [InlineData("linux", Architecture.Arm64, new[] { "linux-arm64" })]
    [InlineData("linux", Architecture.Arm, new string[0])]
    [InlineData(null, Architecture.X64, new string[0])]
    public void RuntimeIdentifiers_are_listed_best_first(string? os, Architecture architecture, string[] expected) =>
        Assert.Equal(expected, PhotoCraftCli.GetRuntimeIdentifiers(os, architecture));

    [Fact]
    public void Finds_the_binary_under_runtimes_in_framework_dependent_output()
    {
        var expected = CreateFile("runtimes", "osx", "native", FileName);

        Assert.Equal(expected, Locate(["osx-arm64", "osx"]));
    }

    [Fact]
    public void Finds_the_binary_in_the_app_directory_after_a_runtime_specific_publish()
    {
        var expected = CreateFile(FileName);

        Assert.Equal(expected, Locate(["linux-x64"]));
    }

    [Fact]
    public void Prefers_the_exact_runtime_identifier_to_the_app_directory()
    {
        CreateFile(FileName);
        var expected = CreateFile("runtimes", "win-arm64", "native", FileName);
        CreateFile("runtimes", "win-x64", "native", FileName);

        Assert.Equal(expected, Locate(["win-arm64", "win-x64", "win-x86"]));
    }

    [Fact]
    public void Prefers_the_app_directory_to_a_compatible_runtime_identifier()
    {
        var expected = CreateFile(FileName);
        CreateFile("runtimes", "win-x64", "native", FileName);

        Assert.Equal(expected, Locate(["win-arm64", "win-x64"]));
    }

    [Fact]
    public void Falls_back_to_a_compatible_runtime_identifier()
    {
        var expected = CreateFile("runtimes", "win-x86", "native", FileName);

        Assert.Equal(expected, Locate(["win-arm64", "win-x64", "win-x86"]));
    }

    [Fact]
    public void Searches_later_directories_when_the_first_has_no_binary()
    {
        var other = Path.Combine(_root, "plugin");
        var expected = CreateFile("plugin", "runtimes", "linux-x64", "native", FileName);

        Assert.Equal(expected, PhotoCraftCli.Locate(null, [_root, other], ["linux-x64"], FileName));
    }

    [Fact]
    public void Override_wins_over_the_packaged_binary()
    {
        CreateFile(FileName);
        var expected = CreateFile("custom", FileName);

        Assert.Equal(expected, PhotoCraftCli.Locate(expected, [_root], ["linux-x64"], FileName));
    }

    [Fact]
    public void Override_that_does_not_exist_throws_instead_of_falling_back()
    {
        CreateFile(FileName);
        var missing = Path.Combine(_root, "missing", FileName);

        var e = Assert.Throws<FileNotFoundException>(() => PhotoCraftCli.Locate(missing, [_root], ["linux-x64"], FileName));
        Assert.Contains(PhotoCraftCli.PathEnvironmentVariable, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_binary_lists_every_probed_path_and_the_package_to_install()
    {
        var e = Assert.Throws<FileNotFoundException>(() => Locate(["win-x64", "win-x86"]));

        Assert.Contains("ArtCraft.PhotoCraft.Runtime.win-x64", e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, "runtimes", "win-x64", "native", FileName), e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, FileName), e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, "runtimes", "win-x86", "native", FileName), e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_platform_throws_PlatformNotSupportedException() =>
        Assert.Throws<PlatformNotSupportedException>(() => Locate([]));

    private string Locate(string[] runtimeIdentifiers) =>
        PhotoCraftCli.Locate(null, [_root], runtimeIdentifiers, FileName);

    private string CreateFile(params string[] relativePath)
    {
        var path = Path.Combine([_root, .. relativePath]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
