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

public sealed class ConversionService(string? ffmpegPath, IPdfRenderer? pdfRenderer = null)
{
    public bool HasFfmpeg => ffmpegPath is not null;

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
        if (ffmpegPath is null)
        {
            return new(file, ResultStatus.Failed, Message: FfmpegMissing);
        }

        var output = OutputPaths.NextTo(file, action.OutputExtensionFor(file), action.Suffix);
        try
        {
            await Ffmpeg.RunAsync(ffmpegPath, FfmpegArgs.Build(action, file, output), ct).ConfigureAwait(false);
            return new(file, ResultStatus.Done, output);
        }
        catch (Exception ex) when (ex is FfmpegException or OperationCanceledException or IOException)
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
        if (ffmpegPath is null)
        {
            return new(first, ResultStatus.Failed, Message: FfmpegMissing);
        }

        var name = images.Count == 1 ? Path.GetFileNameWithoutExtension(first) : "Combined";
        var output = OutputPaths.Unique(Path.GetDirectoryName(Path.GetFullPath(first)) ?? ".", name, ".pdf");
        var temp = Path.Combine(Path.GetTempPath(), "Halora-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var pages = new List<string>();
            for (var i = 0; i < images.Count; i++)
            {
                // A JPEG that PDF can show as-is goes in byte for byte: no second compression.
                if (Formats.SameFormat(images[i], ".jpg") && JpegInfo.Read(images[i]).CanEmbedAsIs)
                {
                    pages.Add(images[i]);
                    continue;
                }

                var page = Path.Combine(temp, $"{i:D4}.jpg");
                await Ffmpeg.RunAsync(ffmpegPath, FfmpegArgs.ImageToJpegForPdf(images[i], page), ct).ConfigureAwait(false);
                pages.Add(page);
            }

            PdfWriter.WriteJpegPages(pages, output);
            return new(first, ResultStatus.Done, output);
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or InvalidDataException)
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
