# artcraft-nuget

NuGet packages that ship the [PhotoCraft](https://github.com/storytold/photocraft) command-line tool
(`photocraft-cli`) with .NET applications, plus a small library that finds it at run time.

> **Unofficial.** This is independent packaging of the upstream release binaries; it isn't made or endorsed
> by the PhotoCraft authors.

## Packages

| Package | What it is |
|---|---|
| `ArtCraft.PhotoCraft` | `PhotoCraftCli.GetPath()`: the absolute path of `photocraft-cli` for the current platform (netstandard2.0, net8.0) |
| `ArtCraft.PhotoCraft.Runtime` | Meta-package: depends on every package below (on .NET Framework, only the Windows ones) |
| `ArtCraft.PhotoCraft.Runtime.linux-x64` / `.linux-arm64` | `runtimes/<rid>/native/photocraft-cli`, glibc 2.35+ |
| `ArtCraft.PhotoCraft.Runtime.osx` | `runtimes/osx/native/photocraft-cli`, universal (Apple silicon + Intel), macOS 11+ |
| `ArtCraft.PhotoCraft.Runtime.win-x64` / `.win-x86` / `.win-arm64` | `runtimes/<rid>/native/photocraft-cli.exe` |

Package versions follow the upstream version (`0.8.2`). A packaging-only fix of the same binaries is released as
`0.8.2.1`, `0.8.2.2`, ...

### Why one package per platform

The CLI is 60–130 MB per platform, about 450 MB in total. nuget.org rejects packages over 250 MB, and one
package would make every app download every platform. Like `SkiaSharp.NativeAssets.*` or `NetVips.Native.*`,
each platform gets its own package, and `ArtCraft.PhotoCraft.Runtime` brings them all in for apps that don't care.

## Using it

```shell
dotnet add package ArtCraft.PhotoCraft
dotnet add package ArtCraft.PhotoCraft.Runtime              # all platforms
dotnet add package ArtCraft.PhotoCraft.Runtime.linux-x64    # or only the ones you deploy to
```

```csharp
using System.Diagnostics;
using ArtCraft.PhotoCraft;

var startInfo = new ProcessStartInfo(PhotoCraftCli.GetPath())
{
    ArgumentList = { "convert", "in.psd", "out.png" },
    RedirectStandardError = true,
};
using var process = Process.Start(startInfo)!;
await process.WaitForExitAsync(cancellationToken);
```

`GetPath()` finds the binary wherever .NET puts it:

| Output | Location |
|---|---|
| `dotnet build` / `dotnet publish` without `-r` | `runtimes/<rid>/native/photocraft-cli` |
| `dotnet publish -r <rid>` (also self-contained, single-file) | next to the app |
| .NET Framework 4.6.2+ | `runtimes/win-<arch>/native/photocraft-cli.exe` (copied by the package's build targets) |
| `PHOTOCRAFT_CLI_PATH` set | that file, e.g. a copy installed in a container image |

It picks the binary for the app's own architecture, the same one `dotnet publish -r <rid>` would ship: an x64
app on an Arm machine runs the x64 CLI under emulation. It throws `FileNotFoundException` naming the package
to install and every path it tried, or `PlatformNotSupportedException` on a platform PhotoCraft doesn't build
for.

### Things to know

- **Linux needs glibc 2.35 or newer.** Use Ubuntu 22.04+/Debian 12+ based images, e.g.
  `mcr.microsoft.com/dotnet/aspnet:10.0-noble`. Alpine (musl) has no PhotoCraft build and won't work.
- **Execute permission.** NuGet doesn't keep file permissions, so the runtime packages run `chmod +x` on the
  binary after build and publish. `GetPath()` never changes files, so the app directory can be read-only.
  Publishing on Windows for Linux can't set the permission: run `chmod +x photocraft-cli` in your Dockerfile
  (or publish on Linux).
- **It's a separate process.** In a server, give each call a timeout and cancellation, and limit how many run
  at once.

## Building

Needs the .NET 10 SDK. The runtime projects download the upstream release archives on first build, verify
them against the SHA-256 digests pinned in [`eng/PhotoCraft.props`](eng/PhotoCraft.props), and cache them in
`artifacts/download/`.

```shell
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release            # -> artifacts/package/release/
eng/smoke-test.sh linux-x64       # installs the packed packages into tests/SmokeTest and runs the CLI
```

### Updating to a new PhotoCraft release

```shell
eng/update-photocraft.sh 0.9.0    # sets the version and pins the new digests from the release's SHA256SUMS.txt
```

The `Update PhotoCraft` workflow does this weekly and opens a pull request. After merging, push a `v0.9.0` tag:
CI builds, smoke-tests on Linux, macOS and Windows (x64 and arm64), and publishes to nuget.org.

### Publishing setup (once)

Publishing uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so
no API key is stored in GitHub:

1. On nuget.org, add a trusted publishing policy for repository `miroljub1995/artcraft-nuget`, workflow
   `ci.yml`, environment `nuget`.
2. In the repository settings, create the `nuget` environment (optionally with required reviewers) and add the
   secret `NUGET_USER` with your nuget.org user name.

## Licence

The packaging code is [MIT](LICENSE). The redistributed PhotoCraft binaries are `MIT OR Apache-2.0` and embed
fonts under the SIL Open Font License 1.1; each runtime package carries those licence texts in `licenses/`.
