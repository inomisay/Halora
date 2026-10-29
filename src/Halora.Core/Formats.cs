namespace Halora.Core;

public enum MediaKind
{
    Unknown,
    Image,
    Audio,
    Video,
    Pdf,
}

public static class Formats
{
    private static readonly Dictionary<string, MediaKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = MediaKind.Image,
        [".jpg"] = MediaKind.Image,
        [".jpeg"] = MediaKind.Image,
        [".webp"] = MediaKind.Image,
        [".bmp"] = MediaKind.Image,
        [".gif"] = MediaKind.Image,
        [".tif"] = MediaKind.Image,
        [".tiff"] = MediaKind.Image,
        [".avif"] = MediaKind.Image,
        [".heic"] = MediaKind.Image,
        [".heif"] = MediaKind.Image,
        [".ico"] = MediaKind.Image,

        [".mp3"] = MediaKind.Audio,
        [".wav"] = MediaKind.Audio,
        [".flac"] = MediaKind.Audio,
        [".m4a"] = MediaKind.Audio,
        [".aac"] = MediaKind.Audio,
        [".ogg"] = MediaKind.Audio,
        [".opus"] = MediaKind.Audio,
        [".wma"] = MediaKind.Audio,
        [".aiff"] = MediaKind.Audio,
        [".aif"] = MediaKind.Audio,

        [".mp4"] = MediaKind.Video,
        [".mov"] = MediaKind.Video,
        [".mkv"] = MediaKind.Video,
        [".webm"] = MediaKind.Video,
        [".avi"] = MediaKind.Video,
        [".m4v"] = MediaKind.Video,
        [".wmv"] = MediaKind.Video,
        [".flv"] = MediaKind.Video,
        [".mts"] = MediaKind.Video,
        [".3gp"] = MediaKind.Video,

        [".pdf"] = MediaKind.Pdf,
    };

    public static MediaKind KindOf(string path) =>
        Kinds.TryGetValue(Path.GetExtension(path), out var kind) ? kind : MediaKind.Unknown;

    /// <summary>Formats that store every pixel exactly, so a copy of them should too.</summary>
    public static bool IsLossless(string path) =>
        new[] { ".png", ".bmp", ".tif", ".gif", ".ico" }.Any(ext => SameFormat(path, ext));

    /// <summary>Treats .jpg/.jpeg and .tif/.tiff as the same format.</summary>
    public static bool SameFormat(string path, string extension)
    {
        static string Normalize(string ext) => ext.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".tiff" => ".tif",
            ".aif" => ".aiff",
            var other => other,
        };

        return Normalize(Path.GetExtension(path)) == Normalize(extension);
    }
}
