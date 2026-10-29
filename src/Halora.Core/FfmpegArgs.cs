namespace Halora.Core;

/// <summary>
/// Builds FFmpeg argument lists. Arguments go through ProcessStartInfo.ArgumentList,
/// never a shell, so file names with spaces or quotes are safe. Commas inside filter
/// expressions are escaped with a backslash because FFmpeg itself splits filters on them.
/// </summary>
public static class FfmpegArgs
{
    // Paints the image over a white canvas so transparent areas don't turn black in JPG.
    // Full-resolution color (4:4:4) keeps small colored text and sharp edges crisp.
    private const string FlattenOnWhite =
        "[0:v]format=rgba,split[a][b];[a]drawbox=c=white@1:t=fill[bg];[bg][b]overlay=format=auto,format=yuvj444p";

    private const string GifFilter =
        @"fps=12,scale=min(480\,iw):-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=4";

    public static List<string> Build(ConversionAction action, string input, string output)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-n", "-i", input };
        var isGif = Formats.SameFormat(input, ".gif");
        var outExt = Path.GetExtension(output).ToLowerInvariant();

        switch (action.Id)
        {
            case "img.png":
                args.AddRange(["-frames:v", "1"]);
                break;

            case "img.jpg":
                args.AddRange(["-frames:v", "1", "-filter_complex", FlattenOnWhite, "-q:v", "2"]);
                break;

            case "img.webp":
                args.AddRange(isGif
                    ? ["-c:v", "libwebp", "-quality", "85", "-compression_level", "6", "-loop", "0"]
                    : ["-frames:v", "1", "-c:v", "libwebp", "-quality", "85", "-compression_level", "6"]);
                break;

            case "img.avif":
                args.AddRange(["-frames:v", "1", "-c:v", "libaom-av1", "-still-picture", "1", "-crf", "30", "-cpu-used", "6", "-pix_fmt", "yuv420p"]);
                break;

            case "img.compress":
                args.AddRange(["-frames:v", "1"]);
                args.AddRange(outExt == ".webp" ? ["-c:v", "libwebp", "-quality", "60"] : ["-q:v", "7"]);
                break;

            case "img.half":
                args.AddRange(["-frames:v", "1", "-vf", @"scale=max(1\,trunc(iw/2)):max(1\,trunc(ih/2)):flags=lanczos"]);
                args.AddRange(QualityFor(outExt));
                break;

            case "vid.mp4":
                args.AddRange(["-map", "0:v:0", "-map", "0:a:0?",
                    "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
                    "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                    "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"]);
                break;

            case "vid.webm":
                args.AddRange(["-map", "0:v:0", "-map", "0:a:0?",
                    "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4",
                    "-c:a", "libopus", "-b:a", "128k"]);
                break;

            case "vid.gif":
                args.AddRange(["-an", "-vf", GifFilter, "-loop", "0"]);
                break;

            case "vid.mp3":
                args.AddRange(["-vn", "-map", "0:a:0", "-c:a", "libmp3lame", "-q:a", "2"]);
                break;

            case "vid.compress":
                args.AddRange(["-map", "0:v:0", "-map", "0:a:0?",
                    "-c:v", "libx264", "-preset", "slow", "-crf", "28", "-pix_fmt", "yuv420p",
                    "-vf", @"scale=-2:trunc(min(1080\,ih)/2)*2:flags=lanczos",
                    "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]);
                break;

            case "vid.mute":
                args.AddRange(["-map", "0:v", "-c", "copy", "-an"]);
                break;

            case "aud.mp3":
                args.AddRange([.. AudioOnly, "-c:a", "libmp3lame", "-q:a", "2"]);
                break;

            case "aud.m4a":
                args.AddRange([.. AudioOnly, "-c:a", "aac", "-b:a", "192k"]);
                break;

            case "aud.wav":
                args.AddRange([.. AudioOnly, "-c:a", "pcm_s16le"]);
                break;

            case "aud.flac":
                args.AddRange([.. AudioOnly, "-c:a", "flac"]);
                break;

            case "aud.opus":
                args.AddRange([.. AudioOnly, "-c:a", "libopus", "-b:a", "96k"]);
                break;

            case "aud.compress":
                args.AddRange([.. AudioOnly, "-c:a", "libmp3lame", "-b:a", "128k"]);
                break;

            default:
                throw new ArgumentException($"'{action.Id}' is not an FFmpeg action.", nameof(action));
        }

        args.Add(output);
        return args;
    }

    /// <summary>JPEG pages for a PDF: flattened on white, high quality.</summary>
    public static List<string> ImageToJpegForPdf(string input, string output) =>
        ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", input,
         "-frames:v", "1", "-filter_complex", FlattenOnWhite, "-q:v", "2", output];

    private static readonly string[] AudioOnly = ["-vn", "-map", "0:a:0", "-map_metadata", "0"];

    private static string[] QualityFor(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => ["-q:v", "2"],
        ".webp" => ["-c:v", "libwebp", "-quality", "90", "-compression_level", "6"],
        ".avif" => ["-c:v", "libaom-av1", "-still-picture", "1", "-crf", "28", "-cpu-used", "6", "-pix_fmt", "yuv420p"],
        _ => [],
    };
}
