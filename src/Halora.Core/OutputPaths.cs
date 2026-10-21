namespace Halora.Core;

public static class OutputPaths
{
    /// <summary>
    /// Next to the source, with the new extension. Never returns a path that exists:
    /// "photo.webp", then "photo (2).webp", "photo (3).webp" and so on.
    /// </summary>
    public static string NextTo(string source, string extension, string suffix = "")
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(source) + suffix;
        return Unique(folder, name, extension);
    }

    public static string Unique(string folder, string name, string extension)
    {
        var candidate = Path.Combine(folder, name + extension);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
        {
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        }

        return candidate;
    }

    /// <summary>A new, empty folder for multi-file output such as PDF pages.</summary>
    public static string NewFolderNextTo(string source, string suffix)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".";
        var path = Unique(folder, Path.GetFileNameWithoutExtension(source) + suffix, "");
        Directory.CreateDirectory(path);
        return path;
    }
}
