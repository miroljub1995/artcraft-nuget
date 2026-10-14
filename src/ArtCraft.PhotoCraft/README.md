# ArtCraft.PhotoCraft

Finds the [PhotoCraft](https://github.com/storytold/photocraft) command-line tool, `photocraft-cli`, that the
`ArtCraft.PhotoCraft.Runtime` packages ship with your application.

> **Unofficial.** This packaging isn't made or endorsed by the PhotoCraft authors.

```shell
dotnet add package ArtCraft.PhotoCraft
dotnet add package ArtCraft.PhotoCraft.Runtime            # every platform
# or only what you deploy to, e.g.
dotnet add package ArtCraft.PhotoCraft.Runtime.linux-x64
```

```csharp
using System.Diagnostics;
using ArtCraft.PhotoCraft;

var startInfo = new ProcessStartInfo(PhotoCraftCli.GetPath())
{
    ArgumentList = { "convert", "in.psd", "out.png" },
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
using var process = Process.Start(startInfo)!;
```

`PhotoCraftCli.GetPath()` returns the absolute path of the executable for the current operating system and
architecture. It looks in `runtimes/<rid>/native/` (framework-dependent output) and in the application
directory (output published for one runtime identifier). Set the `PHOTOCRAFT_CLI_PATH` environment variable to
use another copy instead.

On Linux the CLI needs glibc 2.35 or newer (Ubuntu 22.04+, Debian 12+). Alpine/musl is not supported.
