# artcraft-nuget

NuGet packages that ship the [PhotoCraft](https://github.com/storytold/photocraft) command-line tool
(`photocraft-cli`) with .NET applications, plus a small library that finds it at run time.

> **Unofficial.** This is independent packaging of the upstream release binaries; it isn't made or endorsed
> by the PhotoCraft authors.

## Packages

| Package | What it is |
|---|---|
| `ArtCraft.PhotoCraft` | `PhotoCraftCli.GetPath()`: the absolute path of `photocraft-cli` for the current platform (netstandard2.0: .NET Framework 4.6.2+, .NET 5+) |
| `ArtCraft.PhotoCraft.Runtime` | Meta-package: depends on every package below (on .NET Framework, only the Windows ones) |
| `ArtCraft.PhotoCraft.Runtime.linux-x64` / `.linux-arm64` | `runtimes/<rid>/native/photocraft-cli`, glibc 2.35+ |
| `ArtCraft.PhotoCraft.Runtime.osx` | `runtimes/osx/native/photocraft-cli`, universal (Apple silicon + Intel), macOS 11+ |
| `ArtCraft.PhotoCraft.Runtime.win-x64` / `.win-x86` / `.win-arm64` | `runtimes/<rid>/native/photocraft-cli.exe` |

### Versions

A package version is the upstream version with a two-digit packaging revision (01–99) appended to the patch
number. Every package of a release has the same version.

| Upstream PhotoCraft | Packages |
|---|---|
| 0.8.2 | `0.8.201`; a packaging-only fix of the same binaries is `0.8.202`, `0.8.203`, ... |
| 0.8.12 | `0.8.1201`, ... |
| 0.9.0 | `0.9.1`, ... (the leading zeros of `001` are dropped) |

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

Needs the .NET 10 SDK. First download the CLI of the upstream release to package:

```shell
dotnet run eng/download-cli.cs -- 0.8.2
```

[`eng/download-cli.cs`](eng/download-cli.cs) fetches the release's archives (cached in `artifacts/download/`),
checks each against the release's `SHA256SUMS.txt`, and writes the CLI of every platform to `runtimes/` (not in
git), laid out as it ends up in an app's build output:

```text
runtimes/.version                         0.8.2, read by the build as the version to package
runtimes/<rid>/native/photocraft-cli      packed by src/runtimes/ArtCraft.PhotoCraft.Runtime.<rid>
runtimes/<rid>/licenses/                  licence files of that build
```

Then:

```shell
dotnet test -c Release                                # needs no download
dotnet pack -c Release                                # -> artifacts/package/release/*.0.8.201.nupkg
dotnet pack -c Release -p:PackageRevision=2           # -> *.0.8.202.nupkg
dotnet run eng/smoke-test.cs -- linux-x64            # installs the packed packages into tests/SmokeTest and runs the CLI
```

## Releasing

Nothing to do by hand: the `Release` workflow runs every day and publishes each upstream release that isn't on
nuget.org yet. It takes stable releases (no drafts or pre-releases) from 0.8.2 on, once they are at least
24 hours old, so upstream has time to pull a bad release first. Each version is built and smoke-tested on
Linux, macOS and Windows (x64 and arm64) before anything is pushed. If anything fails, nothing is pushed
that day and the next run tries again.

The minimum version and the delay are `MIN_VERSION` and `MIN_AGE_HOURS` in
[`.github/workflows/release.yml`](.github/workflows/release.yml).

To republish a version with a packaging fix, run the `Release` workflow by hand with the upstream version and
the next revision, e.g. `0.8.2` and `2` for `0.8.202`. nuget.org doesn't allow replacing a published version.

The workflow trusts the hashes in each upstream release's `SHA256SUMS.txt`. Upstream signs nothing, so this
catches corrupted downloads but not a tampered release; the 24-hour delay is the safeguard. Each run logs the
SHA-256 of every archive it packaged.

### Publishing setup (once)

Publishing uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so
no API key is stored in GitHub:

1. On nuget.org, add a trusted publishing policy for repository `miroljub1995/artcraft-nuget`, workflow
   `release.yml`, environment `nuget`.
2. In the repository settings, create the `nuget` environment and add the secret `NUGET_USER` with your
   nuget.org user name. Don't add required reviewers to it unless you want to approve every daily publish.

## Licence

The packaging code is [MIT](LICENSE). The redistributed PhotoCraft binaries are `MIT OR Apache-2.0` and embed
fonts under the SIL Open Font License 1.1; each runtime package carries those licence texts in `licenses/`.
