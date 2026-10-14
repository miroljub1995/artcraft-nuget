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
    [DataRow("win", Architecture.X64, "win-x64")]
    [DataRow("win", Architecture.X86, "win-x86")]
    [DataRow("win", Architecture.Arm64, "win-arm64")]
    [DataRow("linux", Architecture.X64, "linux-x64")]
    [DataRow("linux", Architecture.Arm64, "linux-arm64")]
    [DataRow("osx", Architecture.X64, "osx")]
    [DataRow("osx", Architecture.Arm64, "osx")]
    [DataRow("linux", Architecture.Arm, null)]
    [DataRow(null, Architecture.X64, null)]
    public void Each_platform_maps_to_its_package(string? os, Architecture architecture, string? expected) =>
        Assert.AreEqual(expected, PhotoCraftCli.GetRuntimeIdentifier(os, architecture));

    [TestMethod]
    public void Finds_the_binary_under_runtimes_in_framework_dependent_output()
    {
        var expected = CreateFile("runtimes", "osx", "native", FileName);

        Assert.AreEqual(expected, Locate("osx"));
    }

    [TestMethod]
    public void Finds_the_binary_in_the_app_directory_after_a_runtime_specific_publish()
    {
        var expected = CreateFile(FileName);

        Assert.AreEqual(expected, Locate("linux-x64"));
    }

    [TestMethod]
    public void Prefers_runtimes_to_the_app_directory()
    {
        CreateFile(FileName);
        var expected = CreateFile("runtimes", "win-arm64", "native", FileName);

        Assert.AreEqual(expected, Locate("win-arm64"));
    }

    [TestMethod]
    public void Never_uses_another_platforms_binary()
    {
        CreateFile("runtimes", "win-x64", "native", FileName);

        Assert.ThrowsExactly<FileNotFoundException>(() => Locate("win-arm64"));
    }

    [TestMethod]
    public void Searches_later_directories_when_the_first_has_no_binary()
    {
        var other = Path.Combine(_root, "plugin");
        var expected = CreateFile("plugin", "runtimes", "linux-x64", "native", FileName);

        Assert.AreEqual(expected, PhotoCraftCli.Locate(null, [_root, other], "linux-x64", FileName));
    }

    [TestMethod]
    public void Override_wins_over_the_packaged_binary()
    {
        CreateFile(FileName);
        var expected = CreateFile("custom", FileName);

        Assert.AreEqual(expected, PhotoCraftCli.Locate(expected, [_root], "linux-x64", FileName));
    }

    [TestMethod]
    public void Override_that_does_not_exist_throws_instead_of_falling_back()
    {
        CreateFile(FileName);
        var missing = Path.Combine(_root, "missing", FileName);

        var e = Assert.ThrowsExactly<FileNotFoundException>(() => PhotoCraftCli.Locate(missing, [_root], "linux-x64", FileName));
        Assert.Contains(PhotoCraftCli.PathEnvironmentVariable, e.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Missing_binary_lists_every_probed_path_and_the_package_to_install()
    {
        var e = Assert.ThrowsExactly<FileNotFoundException>(() => Locate("win-x64"));

        Assert.Contains("ArtCraft.PhotoCraft.Runtime.win-x64", e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, "runtimes", "win-x64", "native", FileName), e.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, FileName), e.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Unsupported_platform_throws_PlatformNotSupportedException() =>
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => Locate(null));

    private string Locate(string? runtimeIdentifier) =>
        PhotoCraftCli.Locate(null, [_root], runtimeIdentifier, FileName);

    private string CreateFile(params string[] relativePath)
    {
        var path = Path.Combine([_root, .. relativePath]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
