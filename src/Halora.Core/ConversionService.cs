namespace Halora.Core;

/// <summary>Turns each page of a PDF into an image. Implemented per platform.</summary>
public interface IPdfRenderer
{
    /// <param name="imageExtension">".png" or ".jpg".</param>
    /// <returns>The image files written, in page order.</returns>
    Task<IReadOnlyList<string>> RenderPagesAsync(string pdfPath, string outputFolder, string imageExtension, CancellationToken ct);
}

public enum ResultStatus
{
    Done,
    Skipped,
    Failed,
}

public sealed record ConversionResult(string Source, ResultStatus Status, string? Output = null, string? Message = null);

public sealed record ConversionProgress(int Completed, int Total, string CurrentFile);

/// <param name="ffmpegPath">FFmpeg, for video, audio, and images when ImageMagick is missing.</param>
/// <param name="magickPath">ImageMagick, preferred for images because it keeps quality and metadata.</param>
public sealed class ConversionService(string? ffmpegPath, IPdfRenderer? pdfRenderer = null, string? magickPath = null)
{
    public bool HasFfmpeg => ffmpegPath is not null;

    public bool HasMagick => magickPath is not null;

    /// <summary>Whether the tools this action needs are installed.</summary>
    public bool CanRun(ConversionAction action) => action.Engine switch
    {
        Engine.RenderPdf => true,
        _ when action.Kind == MediaKind.Image => HasMagick || HasFfmpeg,
        _ => HasFfmpeg,
    };

    public async Task<IReadOnlyList<ConversionResult>> RunAsync(
        ConversionAction action,
        IReadOnlyList<string> files,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<ConversionResult>();
        var accepted = new List<string>();

        foreach (var file in files)
        {
            if (ActionCatalog.Accepts(action, file))
            {
                accepted.Add(file);
            }
            else
            {
                results.Add(new(file, ResultStatus.Skipped, Message: $"{action.Label} doesn't apply to this file."));
            }
        }

        if (accepted.Count == 0)
        {
            return results;
        }

        if (action.Engine == Engine.CombinePdf)
        {
            progress?.Report(new(0, 1, Path.GetFileName(accepted[0])));
            results.Add(await CombineAsync(accepted, ct).ConfigureAwait(false));
            progress?.Report(new(1, 1, ""));
            return results;
        }

        for (var i = 0; i < accepted.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = accepted[i];
            progress?.Report(new(i, accepted.Count, Path.GetFileName(file)));

            results.Add(action.Engine switch
            {
                Engine.Ffmpeg => await ConvertOneAsync(action, file, ct).ConfigureAwait(false),
                Engine.RenderPdf => await RenderPdfAsync(action, file, ct).ConfigureAwait(false),
                _ => new(file, ResultStatus.Failed, Message: "Unknown action."),
            });
        }

        progress?.Report(new(accepted.Count, accepted.Count, ""));
        return results;
    }

    private async Task<ConversionResult> ConvertOneAsync(ConversionAction action, string file, CancellationToken ct)
    {
        var useMagick = magickPath is not null && MagickArgs.Handles(action);
        if (!useMagick && ffmpegPath is null)
        {
            return new(file, ResultStatus.Failed, Message: FfmpegMissing);
        }

        var output = OutputPaths.NextTo(file, action.OutputExtensionFor(file), action.Suffix);
        try
        {
            if (useMagick)
            {
                try
                {
                    await Magick.RunAsync(magickPath!, MagickArgs.Build(action, file, output), ct).ConfigureAwait(false);
                    return new(file, ResultStatus.Done, output);
                }
                catch (MagickException) when (ffmpegPath is not null)
                {
                    // Some ImageMagick builds can't read every format (HEIC, for one). FFmpeg may.
                    TryDelete(output);
                }
            }

            await Ffmpeg.RunAsync(ffmpegPath!, FfmpegArgs.Build(action, file, output), ct).ConfigureAwait(false);
            return new(file, ResultStatus.Done, output);
        }
        catch (Exception ex) when (ex is ToolException or OperationCanceledException or IOException)
        {
            TryDelete(output);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new(file, ResultStatus.Failed, Message: ex.Message);
        }
    }

    private async Task<ConversionResult> CombineAsync(IReadOnlyList<string> images, CancellationToken ct)
    {
        var first = images[0];
        if (ffmpegPath is null && magickPath is null)
        {
            return new(first, ResultStatus.Failed, Message: FfmpegMissing);
        }

        var name = images.Count == 1 ? Path.GetFileNameWithoutExtension(first) : "Combined";
        var output = OutputPaths.Unique(Path.GetDirectoryName(Path.GetFullPath(first)) ?? ".", name, ".pdf");
        var temp = Path.Combine(Path.GetTempPath(), "Halora-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var pages = new List<PdfImage>();
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];

                // A JPEG that PDF can show as-is goes in byte for byte: no second compression.
                if (Formats.SameFormat(image, ".jpg") && JpegInfo.Read(image).CanEmbedAsIs)
                {
                    pages.Add(PdfImage.FromJpeg(image));
                    continue;
                }

                // Screenshots, drawings and other lossless images stay pixel-exact.
                if (Formats.IsLossless(image))
                {
                    var pixels = Path.Combine(temp, $"{i:D4}.ppm");
                    await PageAsync(MagickArgs.ImageToPpmForPdf(image, pixels), FfmpegArgs.ImageToPpmForPdf(image, pixels), ct).ConfigureAwait(false);
                    pages.Add(PdfImage.FromPpm(pixels));
                    File.Delete(pixels);
                    continue;
                }

                // Already-lossy photos (WebP, AVIF, HEIC, rotated JPG): one high-quality JPEG pass.
                var page = Path.Combine(temp, $"{i:D4}.jpg");
                await PageAsync(MagickArgs.ImageToJpegForPdf(image, page), FfmpegArgs.ImageToJpegForPdf(image, page), ct).ConfigureAwait(false);
                pages.Add(PdfImage.FromJpeg(page));
            }

            PdfWriter.WritePages(pages, output);
            return new(first, ResultStatus.Done, output);
        }
        catch (Exception ex) when (ex is ToolException or IOException or InvalidDataException)
        {
            TryDelete(output);
            return new(first, ResultStatus.Failed, Message: ex.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // Temp files are cleaned up by Windows eventually.
            }
        }
    }

    /// <summary>Makes one PDF page image with ImageMagick, or FFmpeg when ImageMagick is missing or can't read it.</summary>
    private async Task PageAsync(List<string> magickArgs, List<string> ffmpegArgs, CancellationToken ct)
    {
        if (magickPath is not null)
        {
            try
            {
                await Magick.RunAsync(magickPath, magickArgs, ct).ConfigureAwait(false);
                return;
            }
            catch (MagickException) when (ffmpegPath is not null)
            {
            }
        }

        await Ffmpeg.RunAsync(ffmpegPath!, ffmpegArgs, ct).ConfigureAwait(false);
    }

    private async Task<ConversionResult> RenderPdfAsync(ConversionAction action, string file, CancellationToken ct)
    {
        if (pdfRenderer is null)
        {
            return new(file, ResultStatus.Failed, Message: "PDF pages can only be rendered on Windows.");
        }

        var folder = OutputPaths.NewFolderNextTo(file, " pages");
        try
        {
            var written = await pdfRenderer.RenderPagesAsync(file, folder, action.TargetExtension ?? ".png", ct).ConfigureAwait(false);
            return new(file, ResultStatus.Done, folder, $"{written.Count} page(s)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(file, ResultStatus.Failed, Message: ex.Message);
        }
    }

    public const string FfmpegMissing =
        "FFmpeg isn't installed. Open a terminal and run: winget install Gyan.FFmpeg";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
