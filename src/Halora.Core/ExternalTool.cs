using System.Diagnostics;
using System.Text;

namespace Halora.Core;

/// <summary>A command-line tool (FFmpeg, ImageMagick) stopped with an error.</summary>
public class ToolException(string message) : Exception(message);

/// <summary>Finds and runs the command-line tools Halora converts with.</summary>
public static class ExternalTool
{
    /// <summary>
    /// Looks in the app's own "tools" folder first, then next to the app, then on PATH,
    /// then in any extra folders given. Returns null when the tool is not installed.
    /// </summary>
    public static string? Locate(string name, IEnumerable<string>? extraDirs = null)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var baseDir = AppContext.BaseDirectory;

        string[] local = [Path.Combine(baseDir, "tools", exe), Path.Combine(baseDir, exe)];
        foreach (var candidate in local)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Also read the saved user and machine PATH. After "winget install", the running
        // process still has its old PATH, and this way nobody has to restart Halora.
        var path = string.Join(Path.PathSeparator,
            Environment.GetEnvironmentVariable("PATH") ?? "",
            OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "" : "",
            OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "" : "");

        if (OperatingSystem.IsWindows())
        {
            // winget puts its command links here.
            var links = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links");
            path += Path.PathSeparator + links;
        }

        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(extraDirs ?? []);
        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), exe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry; skip it.
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the tool with arguments passed one by one (never through a shell, so any file
    /// name is safe). Throws the exception <paramref name="fail"/> builds from the error output
    /// when the tool exits with a non-zero code. Cancelling kills the tool.
    /// </summary>
    public static async Task RunAsync(string exePath, IReadOnlyList<string> args, Func<string, ToolException> fail, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = info };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            string text;
            lock (stderr)
            {
                text = stderr.ToString();
            }

            throw fail(text);
        }
    }
}
