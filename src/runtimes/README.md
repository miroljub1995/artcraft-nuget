# PhotoCraft CLI runtime packages

These packages carry the [PhotoCraft](https://github.com/storytold/photocraft) command-line tool, `photocraft-cli`,
taken unmodified from the official GitHub release and checked against the SHA-256 in its `SHA256SUMS.txt`.

> **Unofficial.** This packaging isn't made or endorsed by the PhotoCraft authors. Report problems with the
> tool itself upstream, and problems with the packages at
> [miroljub1995/artcraft-nuget](https://github.com/miroljub1995/artcraft-nuget/issues).

| Package | Contents |
|---|---|
| `ArtCraft.PhotoCraft.Runtime` | Depends on all of the packages below (only the Windows ones on .NET Framework) |
| `ArtCraft.PhotoCraft.Runtime.linux-x64` | `runtimes/linux-x64/native/photocraft-cli` (needs glibc 2.35+) |
| `ArtCraft.PhotoCraft.Runtime.linux-arm64` | `runtimes/linux-arm64/native/photocraft-cli` (needs glibc 2.35+) |
| `ArtCraft.PhotoCraft.Runtime.osx` | `runtimes/osx/native/photocraft-cli` (universal: Apple silicon and Intel, macOS 11+) |
| `ArtCraft.PhotoCraft.Runtime.win-x64` | `runtimes/win-x64/native/photocraft-cli.exe` |
| `ArtCraft.PhotoCraft.Runtime.win-x86` | `runtimes/win-x86/native/photocraft-cli.exe` |
| `ArtCraft.PhotoCraft.Runtime.win-arm64` | `runtimes/win-arm64/native/photocraft-cli.exe` |

Find the executable with the `ArtCraft.PhotoCraft` package:

```csharp
string cli = ArtCraft.PhotoCraft.PhotoCraftCli.GetPath();
```

The binaries are licensed `MIT OR Apache-2.0` and embed fonts under the SIL Open Font License 1.1; the licence
texts are in each package's `licenses/` folder.
