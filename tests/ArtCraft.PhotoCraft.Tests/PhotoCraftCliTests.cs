using System.Runtime.InteropServices;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ArtCraft.PhotoCraft.Tests;

[TestClass]
public sealed class PhotoCraftCliTests : IDisposable
{
    private const string FileName = "photocraft-cli";

    private readonly string _root = Directory.CreateTempSubdirectory("photocraft-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    [DataRow("win", Architecture.Arm64, new[] { "win-arm64", "win-x64", "win-x86" })]
    [DataRow("win", Architecture.X64, new[] { "win-x64", "win-x86" })]
    [DataRow("win", Architecture.X86, new[] { "win-x86" })]
    [DataRow("osx", Architecture.Arm64, new[] { "osx-arm64", "osx" })]
    [DataRow("osx", Architecture.X64, new[] { "osx-x64", "osx" })]
    [DataRow("linux", Architecture.X64, new[] { "linux-x64" })]
    [DataRow("linux", Architecture.Arm64, new[] { "linux-arm64" })]
    [DataRow("linux", Architecture.Arm, new string[0])]
    [DataRow(null, Architecture.X64, new string[0])]
    public void RuntimeIdentifiers_are_listed_best_first(string? os, Architecture architecture, string[] expected) =>
        CollectionAssert.AreEqual(expected, PhotoCraftCli.GetRuntimeIdentifiers(os, architecture).ToArray());

    [TestMethod]
    public void Finds_the_binary_under_runtimes_in_framework_dependent_output()
    {
        var expected = CreateFile("runtimes", "osx", "native", FileName);

        Assert.AreEqual(expected, Locate(["osx-arm64", "osx"]));
    }

    [TestMethod]
    public void Finds_the_binary_in_the_app_directory_after_a_runtime_specific_publish()
    {
        var expected = CreateFile(FileName);

        Assert.AreEqual(expected, Locate(["linux-x64"]));
    }

    [TestMethod]
    public void Prefers_the_exact_runtime_identifier_to_the_app_directory()
    {
        CreateFile(FileName);
        var expected = CreateFile("runtimes", "win-arm64", "native", FileName);
        CreateFile("runtimes", "win-x64", "native", FileName);

        Assert.AreEqual(expected, Locate(["win-arm64", "win-x64", "win-x86"]));
    }

    [TestMethod]
    public void Prefers_the_app_directory_to_a_compatible_runtime_identifier()
    {
        var expected = CreateFile(FileName);
        CreateFile("runtimes", "win-x64", "native", FileName);

        Assert.AreEqual(expected, Locate(["win-arm64", "win-x64"]));
    }

    [TestMethod]
    public void Falls_back_to_a_compatible_runtime_identifier()
    {
        var expected = CreateFile("runtimes", "win-x86", "native", FileName);

        Assert.AreEqual(expected, Locate(["win-arm64", "win-x64", "win-x86"]));
    }

    [TestMethod]
    public void Searches_later_directories_when_the_first_has_no_binary()
    {
        var other = Path.Combine(_root, "plugin");
        var expected = CreateFile("plugin", "runtimes", "linux-x64", "native", FileName);

        Assert.AreEqual(expected, PhotoCraftCli.Locate(null, [_root, other], ["linux-x64"], FileName));
    }

    [TestMethod]
    public void Override_wins_over_the_packaged_binary()
    {
        CreateFile(FileName);
        var expected = CreateFile("custom", FileName);

        Assert.AreEqual(expected, PhotoCraftCli.Locate(expected, [_root], ["linux-x64"], FileName));
    }

    [TestMethod]
    public void Override_that_does_not_exist_throws_instead_of_falling_back()
    {
        CreateFile(FileName);
        var missing = Path.Combine(_root, "missing", FileName);

        var e = Assert.ThrowsExactly<FileNotFoundException>(() => PhotoCraftCli.Locate(missing, [_root], ["linux-x64"], FileName));
        Assert.Contains(PhotoCraftCli.PathEnvironmentVariable, e.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Missing_binary_lists_every_probed_path_and_the_package_to_install()
    {
        var e = Assert.ThrowsExactly<FileNotFoundException>(() => Locate(["win-x64", "win-x86"]));

        Assert.Contains("ArtCraft.PhotoCraft.Runtime.win-x64", e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, "runtimes", "win-x64", "native", FileName), e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, FileName), e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, "runtimes", "win-x86", "native", FileName), e.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Unsupported_platform_throws_PlatformNotSupportedException() =>
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => Locate([]));

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
