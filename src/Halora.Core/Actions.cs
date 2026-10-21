namespace Halora.Core;

public enum Engine
{
    /// <summary>One output file per input, made by FFmpeg.</summary>
    Ffmpeg,

    /// <summary>All selected images become pages of one PDF.</summary>
    CombinePdf,

    /// <summary>Each PDF page becomes an image. Needs a platform renderer (Windows.Data.Pdf on Windows).</summary>
    RenderPdf,
}

/// <param name="Id">Stable id, used by the FFmpeg argument builder.</param>
/// <param name="Label">Short text shown in the circle menu.</param>
/// <param name="Hint">One line shown when hovering the button.</param>
/// <param name="TargetExtension">Extension of the output, or null to keep the input's.</param>
/// <param name="Suffix">Added to the file name, e.g. " (compressed)". Empty for plain conversions.</param>
public sealed record ConversionAction(
    string Id,
    string Label,
    string Hint,
    MediaKind Kind,
    Engine Engine,
    string? TargetExtension,
    string Suffix,
    Func<string, bool> AppliesTo)
{
    public string OutputExtensionFor(string input) => TargetExtension ?? Path.GetExtension(input);
}

public static class ActionCatalog
{
    private static Func<string, bool> NotAlready(string ext) => path => !Formats.SameFormat(path, ext);

    private static Func<string, bool> OnlyFormats(params string[] exts) =>
        path => exts.Any(e => Formats.SameFormat(path, e));

    private static readonly Func<string, bool> Always = _ => true;

    public static IReadOnlyList<ConversionAction> All { get; } =
    [
        // Images
        new("img.png", "PNG", "Convert to PNG (keeps transparency)", MediaKind.Image, Engine.Ffmpeg, ".png", "", NotAlready(".png")),
        new("img.jpg", "JPG", "Convert to JPG (transparent areas become white)", MediaKind.Image, Engine.Ffmpeg, ".jpg", "", NotAlready(".jpg")),
        new("img.webp", "WebP", "Convert to WebP, smaller than JPG at the same quality", MediaKind.Image, Engine.Ffmpeg, ".webp", "", NotAlready(".webp")),
        new("img.avif", "AVIF", "Convert to AVIF, the smallest modern format", MediaKind.Image, Engine.Ffmpeg, ".avif", "", NotAlready(".avif")),
        new("img.compress", "Smaller", "Re-save JPG or WebP at lower quality", MediaKind.Image, Engine.Ffmpeg, null, " (compressed)", OnlyFormats(".jpg", ".webp")),
        new("img.half", "50%", "Halve the width and height", MediaKind.Image, Engine.Ffmpeg, null, " (50%)", OnlyFormats(".png", ".jpg", ".webp", ".bmp", ".tif", ".avif")),
        new("img.pdf", "PDF", "Put the images into one PDF, one per page", MediaKind.Image, Engine.CombinePdf, ".pdf", "", Always),

        // Video
        new("vid.mp4", "MP4", "Convert to MP4 (H.264), plays everywhere", MediaKind.Video, Engine.Ffmpeg, ".mp4", "", NotAlready(".mp4")),
        new("vid.webm", "WebM", "Convert to WebM (VP9) for the web", MediaKind.Video, Engine.Ffmpeg, ".webm", "", NotAlready(".webm")),
        new("vid.gif", "GIF", "Turn the video into a 480 px animated GIF", MediaKind.Video, Engine.Ffmpeg, ".gif", "", Always),
        new("vid.mp3", "Audio", "Extract the sound as MP3", MediaKind.Video, Engine.Ffmpeg, ".mp3", "", Always),
        new("vid.compress", "Smaller", "Re-encode at up to 1080p with a smaller file size", MediaKind.Video, Engine.Ffmpeg, ".mp4", " (compressed)", Always),
        new("vid.mute", "Mute", "Remove the sound, keep the picture untouched", MediaKind.Video, Engine.Ffmpeg, null, " (muted)", Always),

        // Audio
        new("aud.mp3", "MP3", "Convert to MP3 (high quality VBR)", MediaKind.Audio, Engine.Ffmpeg, ".mp3", "", NotAlready(".mp3")),
        new("aud.m4a", "M4A", "Convert to M4A (AAC), good for Apple devices", MediaKind.Audio, Engine.Ffmpeg, ".m4a", "", NotAlready(".m4a")),
        new("aud.wav", "WAV", "Convert to uncompressed WAV", MediaKind.Audio, Engine.Ffmpeg, ".wav", "", NotAlready(".wav")),
        new("aud.flac", "FLAC", "Convert to lossless FLAC", MediaKind.Audio, Engine.Ffmpeg, ".flac", "", NotAlready(".flac")),
        new("aud.opus", "Opus", "Convert to Opus, very small for voice", MediaKind.Audio, Engine.Ffmpeg, ".opus", "", NotAlready(".opus")),
        new("aud.compress", "Smaller", "Re-save as 128 kbps MP3", MediaKind.Audio, Engine.Ffmpeg, ".mp3", " (compressed)", Always),

        // PDF
        new("pdf.png", "PNG", "Save every page as a PNG image", MediaKind.Pdf, Engine.RenderPdf, ".png", "", Always),
        new("pdf.jpg", "JPG", "Save every page as a JPG image", MediaKind.Pdf, Engine.RenderPdf, ".jpg", "", Always),
    ];

    /// <summary>
    /// Actions that apply to at least one of the files. Files an action does not apply to
    /// are skipped when it runs, so a mixed selection still works.
    /// </summary>
    public static IReadOnlyList<ConversionAction> For(IReadOnlyCollection<string> paths)
    {
        var supported = paths.Where(p => Formats.KindOf(p) != MediaKind.Unknown).ToList();
        if (supported.Count == 0)
        {
            return [];
        }

        return All
            .Where(a => supported.Any(p => Formats.KindOf(p) == a.Kind && a.AppliesTo(p)))
            .ToList();
    }

    public static bool Accepts(ConversionAction action, string path) =>
        Formats.KindOf(path) == action.Kind && action.AppliesTo(path);
}
