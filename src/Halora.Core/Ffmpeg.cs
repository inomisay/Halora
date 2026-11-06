namespace Halora.Core;

public sealed class FfmpegException(string message) : ToolException(message);

public static class Ffmpeg
{
    /// <summary>Returns the path to ffmpeg, or null when FFmpeg is not installed.</summary>
    public static string? Locate() => ExternalTool.Locate("ffmpeg");

    public static Task RunAsync(string ffmpegPath, IReadOnlyList<string> args, CancellationToken ct) =>
        ExternalTool.RunAsync(ffmpegPath, args, stderr => new FfmpegException(Explain(stderr)), ct);

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
