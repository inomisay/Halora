using System.Globalization;
using System.Text;

namespace Halora.Core;

/// <summary>
/// Writes a minimal PDF where every page is one JPEG, embedded as-is (DCTDecode),
/// so there is no re-compression. Pages are sized to the image at 96 DPI.
/// </summary>
public static class PdfWriter
{
    public static void WriteJpegPages(IReadOnlyList<string> jpegPaths, string outputPath)
    {
        if (jpegPaths.Count == 0)
        {
            throw new ArgumentException("No pages to write.", nameof(jpegPaths));
        }

        var pages = jpegPaths.Select(p => (Data: File.ReadAllBytes(p), Info: JpegInfo.Read(p))).ToList();

        // Object numbers: 1 catalog, 2 page tree, then 3 per page (page, image, content).
        var pageObj = new int[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            pageObj[i] = 3 + (i * 3);
        }

        var objectCount = 2 + (pages.Count * 3);
        var offsets = new long[objectCount + 1];

        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
        Write(stream, "%PDF-1.4\n%âãÏÓ\n");

        offsets[1] = stream.Position;
        Write(stream, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = stream.Position;
        var kids = string.Join(" ", pageObj.Select(n => $"{n} 0 R"));
        Write(stream, $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var (data, info) = pages[i];
            var p = pageObj[i];
            var w = Num(info.Width * 72.0 / 96.0);
            var h = Num(info.Height * 72.0 / 96.0);
            var colorSpace = info.Components switch
            {
                1 => "/DeviceGray",
                4 => "/DeviceCMYK",
                _ => "/DeviceRGB",
            };

            offsets[p] = stream.Position;
            Write(stream,
                $"{p} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] " +
                $"/Resources << /XObject << /Im0 {p + 1} 0 R >> >> /Contents {p + 2} 0 R >>\nendobj\n");

            offsets[p + 1] = stream.Position;
            Write(stream,
                $"{p + 1} 0 obj\n<< /Type /XObject /Subtype /Image /Width {info.Width} /Height {info.Height} " +
                $"/ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode /Length {data.Length} >>\nstream\n");
            stream.Write(data);
            Write(stream, "\nendstream\nendobj\n");

            var content = $"q {w} 0 0 {h} 0 0 cm /Im0 Do Q";
            offsets[p + 2] = stream.Position;
            Write(stream, $"{p + 2} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        }

        var xref = stream.Position;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objectCount + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (var n = 1; n <= objectCount; n++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offsets[n]:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(stream, sb.ToString());
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Write(Stream stream, string text) => stream.Write(Encoding.Latin1.GetBytes(text));
}

/// <param name="Orientation">EXIF orientation, 1 when the file has none (no rotation).</param>
public readonly record struct JpegInfo(int Width, int Height, int Components, int Marker, int Orientation)
{
    /// <summary>
    /// True when a PDF reader shows this file exactly as an image viewer would: baseline or
    /// progressive Huffman coding, gray or RGB, and no EXIF rotation (PDF ignores EXIF).
    /// </summary>
    public bool CanEmbedAsIs => Marker is 0xC0 or 0xC1 or 0xC2 && Components is 1 or 3 && Orientation == 1;

    /// <summary>Reads the size from the JPEG's start-of-frame marker, and the EXIF orientation before it.</summary>
    public static JpegInfo Read(string path)
    {
        using var s = File.OpenRead(path);
        if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8)
        {
            throw new InvalidDataException("Not a JPEG file.");
        }

        var orientation = 1;
        while (true)
        {
            var b = s.ReadByte();
            while (b == 0xFF)
            {
                b = s.ReadByte();
            }

            if (b < 0)
            {
                throw new InvalidDataException("JPEG ended before its size was found.");
            }

            var marker = b;
            var length = (s.ReadByte() << 8) | s.ReadByte();

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                s.ReadByte(); // precision
                var height = (s.ReadByte() << 8) | s.ReadByte();
                var width = (s.ReadByte() << 8) | s.ReadByte();
                var components = s.ReadByte();
                return new JpegInfo(width, height, components, marker, orientation);
            }

            if (marker == 0xE1 && length > 2)
            {
                var segment = new byte[length - 2];
                s.ReadExactly(segment);
                orientation = ExifOrientation(segment) ?? orientation;
                continue;
            }

            s.Seek(length - 2, SeekOrigin.Current);
        }
    }

    /// <summary>Finds tag 0x0112 in the first IFD of an APP1 "Exif" segment.</summary>
    private static int? ExifOrientation(byte[] app1)
    {
        const int tiff = 6; // after "Exif\0\0"
        if (app1.Length < tiff + 8 || app1[0] != 'E' || app1[1] != 'x' || app1[2] != 'i' || app1[3] != 'f')
        {
            return null;
        }

        var little = app1[tiff] == 'I';
        int U16(int at) => little ? app1[at] | (app1[at + 1] << 8) : (app1[at] << 8) | app1[at + 1];
        int U32(int at) => little
            ? app1[at] | (app1[at + 1] << 8) | (app1[at + 2] << 16) | (app1[at + 3] << 24)
            : (app1[at] << 24) | (app1[at + 1] << 16) | (app1[at + 2] << 8) | app1[at + 3];

        var ifd = tiff + U32(tiff + 4);
        if (ifd < tiff || ifd + 2 > app1.Length)
        {
            return null;
        }

        var count = U16(ifd);
        for (var i = 0; i < count; i++)
        {
            var entry = ifd + 2 + (i * 12);
            if (entry + 12 > app1.Length)
            {
                return null;
            }

            if (U16(entry) == 0x0112)
            {
                return U16(entry + 8);
            }
        }

        return null;
    }
}
