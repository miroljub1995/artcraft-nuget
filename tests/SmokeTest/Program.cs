using System.Diagnostics;
using ArtCraft.PhotoCraft;

// Usage: SmokeTest <expected version>
// Finds photocraft-cli, runs `photocraft-cli --version` and checks the output.
var expectedVersion = args.Length > 0 ? args[0] : throw new ArgumentException("usage: SmokeTest <expected version>");

var path = PhotoCraftCli.GetPath();
Console.WriteLine($"photocraft-cli: {path}");
if (!Path.IsPathRooted(path))
{
    throw new InvalidOperationException($"'{path}' is not an absolute path.");
}

var startInfo = new ProcessStartInfo(path, "--version")
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
};
using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
var output = process.StandardOutput.ReadToEnd();
var error = process.StandardError.ReadToEnd();
process.WaitForExit();
Console.Write(output);
Console.Error.Write(error);

if (process.ExitCode != 0 || !output.StartsWith($"photocraft-cli {expectedVersion} ", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"FAIL: exit code {process.ExitCode}, expected output 'photocraft-cli {expectedVersion} ...'.");
    return 1;
}

Console.WriteLine("OK");
return 0;
