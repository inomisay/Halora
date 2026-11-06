namespace Halora.Core;

public sealed class MagickException(string message) : ToolException(message);

/// <summary>
/// ImageMagick, used for images when it is installed. Unlike FFmpeg (a video tool), it keeps
/// the color profile and EXIF data, keeps 16-bit depth, writes lossless WebP from lossless
/// sources, and keeps full color resolution in JPG and AVIF.
/// </summary>
public static class Magick
{
    public const string InstallCommand = "winget install ImageMagick.ImageMagick";

    /// <summary>Returns the path to magick, or null when ImageMagick 7 is not installed.</summary>
    public static string? Locate() => ExternalTool.Locate("magick", InstallFolders());

    public static Task RunAsync(string magickPath, IReadOnlyList<string> args, CancellationToken ct) =>
        ExternalTool.RunAsync(magickPath, args, stderr => new MagickException(Explain(stderr)), ct);

    /// <summary>Turns ImageMagick's error output into one readable line.</summary>
    public static string Explain(string stderr)
    {
        if (stderr.Contains("no decode delegate", StringComparison.OrdinalIgnoreCase))
        {
            return "ImageMagick can't read this kind of image.";
        }

        if (stderr.Contains("no encode delegate", StringComparison.OrdinalIgnoreCase))
        {
            return "ImageMagick can't write this format on this PC.";
        }

        var last = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrEmpty(last))
        {
            return "ImageMagick stopped with an unknown error.";
        }

        // "magick: improper image header `x.png' @ error/png.c/ReadPNGImage/4092." -> "improper image header `x.png'"
        if (last.StartsWith("magick", StringComparison.OrdinalIgnoreCase) && last.IndexOf(": ", StringComparison.Ordinal) is > 0 and var colon)
        {
            last = last[(colon + 2)..];
        }

        var at = last.LastIndexOf(" @ ", StringComparison.Ordinal);
        return at > 0 ? last[..at] : last;
    }

    /// <summary>The ImageMagick installer adds itself to the machine PATH, but also look in its usual folders.</summary>
    private static IEnumerable<string> InstallFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        try
        {
            return Directory.GetDirectories(programFiles, "ImageMagick-7*").OrderDescending();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>
/// Builds ImageMagick argument lists for the image actions. Inputs are read as "file[0]"
/// (only the first frame, and brackets in the file name are never taken as a frame range);
/// outputs carry an explicit format prefix, so the file name alone never decides the format.
/// </summary>
public static class MagickArgs
{
    // Paints transparent areas white, since JPG and PDF pages can't be see-through.
    private static readonly string[] FlattenOnWhite = ["-background", "white", "-alpha", "remove", "-alpha", "off"];

    // 4:4:4 keeps full color resolution, so small colored text and sharp edges stay crisp.
    private static readonly string[] BestJpeg = ["-quality", "95", "-sampling-factor", "4:4:4"];

    private static readonly string[] LosslessWebp = ["-define", "webp:lossless=true", "-define", "webp:method=6"];

    private static readonly string[] BestLossyWebp = ["-quality", "92", "-define", "webp:method=6", "-define", "webp:use-sharp-yuv=true"];

    private static readonly string[] BestAvif = ["-quality", "90", "-define", "heic:chroma=444", "-define", "heic:speed=6"];

    public static bool Handles(ConversionAction action) =>
        action.Kind == MediaKind.Image && action.Engine == Engine.Ffmpeg;

    public static List<string> Build(ConversionAction action, string input, string output)
    {
        var outExt = Path.GetExtension(output).ToLowerInvariant();
        var animatedGifToWebp = action.Id == "img.webp" && Formats.SameFormat(input, ".gif");

        // Turn the pixels upright by the EXIF orientation, so every viewer shows them the same way.
        var args = animatedGifToWebp
            ? new List<string> { "-quiet", Path.GetFullPath(input), "-coalesce" }
            : ["-quiet", Path.GetFullPath(input) + "[0]", "-auto-orient"];

        switch (action.Id)
        {
            case "img.png":
                break;

            case "img.jpg":
                args.AddRange([.. FlattenOnWhite, .. BestJpeg]);
                break;

            case "img.webp":
                // From PNG, GIF, BMP or TIFF: every pixel stays exactly the same.
                args.AddRange(Formats.IsLossless(input) ? LosslessWebp : BestLossyWebp);
                break;

            case "img.avif":
                args.AddRange(BestAvif);
                break;

            case "img.compress":
                args.AddRange(outExt == ".webp" ? ["-quality", "60"] : ["-quality", "75"]);
                break;

            case "img.half":
                args.AddRange(["-filter", "Lanczos", "-resize", "50%"]);
                args.AddRange(outExt switch
                {
                    ".jpg" or ".jpeg" => BestJpeg,
                    ".webp" => BestLossyWebp,
                    ".avif" => BestAvif,
                    _ => [],
                });
                break;

            default:
                throw new ArgumentException($"'{action.Id}' is not an image action.", nameof(action));
        }

        args.Add(Prefixed(output));
        return args;
    }

    /// <summary>Exact RGB pixels for a lossless PDF page: upright, flattened on white, 8-bit PPM.</summary>
    public static List<string> ImageToPpmForPdf(string input, string output) =>
        ["-quiet", Path.GetFullPath(input) + "[0]", "-auto-orient", .. FlattenOnWhite,
         "-colorspace", "sRGB", "-type", "TrueColor", "-depth", "8", "-strip", "PPM:" + Path.GetFullPath(output)];

    /// <summary>A JPEG page for a PDF: upright, flattened on white, high quality.</summary>
    public static List<string> ImageToJpegForPdf(string input, string output) =>
        ["-quiet", Path.GetFullPath(input) + "[0]", "-auto-orient", .. FlattenOnWhite,
         "-colorspace", "sRGB", "-type", "TrueColor", .. BestJpeg, "-strip", "JPEG:" + Path.GetFullPath(output)];

    private static string Prefixed(string output)
    {
        var format = Path.GetExtension(output).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "JPEG",
            ".png" => "PNG",
            ".webp" => "WEBP",
            ".avif" => "AVIF",
            ".bmp" => "BMP",
            ".tif" or ".tiff" => "TIFF",
            ".gif" => "GIF",
            var other => throw new ArgumentException($"ImageMagick output '{other}' isn't supported.", nameof(output)),
        };

        return format + ":" + Path.GetFullPath(output);
    }
}
