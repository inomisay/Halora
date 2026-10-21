using System.Diagnostics;
using System.Text;

namespace Halora.Core;

public sealed class FfmpegException(string message) : Exception(message);

public static class Ffmpeg
{
    /// <summary>
    /// Looks in the app's own "tools" folder first, then next to the app, then on PATH.
    /// Returns null when FFmpeg is not installed.
    /// </summary>
    public static string? Locate()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
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

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
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

    public static async Task RunAsync(string ffmpegPath, IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpegPath)
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

            throw new FfmpegException(Explain(text));
        }
    }

    /// <summary>Turns FFmpeg's error output into one readable line.</summary>
    public static string Explain(string stderr)
    {
        if (stderr.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
        {
            return "This file has no sound to extract.";
        }

        if (stderr.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
        {
            return "Your FFmpeg build is missing an encoder. Install the full build: winget install Gyan.FFmpeg";
        }

        if (stderr.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
        {
            return "FFmpeg couldn't read this file. It may be damaged, or this FFmpeg version doesn't support it.";
        }

        var last = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrEmpty(last) ? "FFmpeg stopped with an unknown error." : last;
    }
}
