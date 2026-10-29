using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halora.Core;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Halora.App;

/// <summary>Renders PDF pages with the PDF engine built into Windows 10 and 11.</summary>
internal sealed class WindowsPdfRenderer : IPdfRenderer
{
    private const double Scale = 300.0 / 96.0; // page sizes are in 1/96 inch; this renders at 300 DPI, print quality

    public async Task<IReadOnlyList<string>> RenderPagesAsync(string pdfPath, string outputFolder, string imageExtension, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath).AsTask(ct);
        var document = await PdfDocument.LoadFromFileAsync(file).AsTask(ct);
        if (document.IsPasswordProtected)
        {
            throw new InvalidOperationException("This PDF is password protected.");
        }

        var folder = await StorageFolder.GetFolderFromPathAsync(outputFolder).AsTask(ct);
        var jpeg = imageExtension.Equals(".jpg", StringComparison.OrdinalIgnoreCase);
        var digits = Math.Max(3, document.PageCount.ToString().Length);
        var written = new List<string>();

        for (uint i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = document.GetPage(i);

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * Scale)),
            };
            if (jpeg)
            {
                options.BitmapEncoderId = BitmapEncoder.JpegEncoderId;
            }

            var name = $"page-{(i + 1).ToString().PadLeft(digits, '0')}{imageExtension}";
            var outFile = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName).AsTask(ct);
            using (var stream = await outFile.OpenAsync(FileAccessMode.ReadWrite).AsTask(ct))
            {
                await page.RenderToStreamAsync(stream, options).AsTask(ct);
            }

            written.Add(outFile.Path);
        }

        return written;
    }
}
