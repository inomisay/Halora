// A small self-contained test runner (no test framework packages needed).
// Unit checks run everywhere. Conversion checks need ffmpeg and ffprobe on PATH.
// Run: dotnet run --project tests/Halora.Tests
using System.Diagnostics;
using Halora.Core;

var failures = 0;
var passed = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok)
    {
        passed++;
        Console.WriteLine($"  ok   {name}");
    }
    else
    {
        failures++;
        Console.WriteLine($"  FAIL {name} {detail}");
    }
}

string Run(string exe, params string[] args)
{
    var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in args)
    {
        info.ArgumentList.Add(a);
    }

    using var p = Process.Start(info)!;
    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0)
    {
        throw new Exception($"{exe} failed: {output}");
    }

    return output.Trim();
}

string Probe(string file, string entries) =>
    Run("ffprobe", "-v", "error", "-show_entries", entries, "-of", "default=nw=1:nk=1", file);

var root = Path.Combine(Path.GetTempPath(), "halora-tests-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);
Console.WriteLine($"Working in {root}");

// ---------- Unit checks ----------
Console.WriteLine("Formats and catalog");
Check("jpeg is image", Formats.KindOf("a.JPEG") == MediaKind.Image);
Check("mkv is video", Formats.KindOf("a.mkv") == MediaKind.Video);
Check("txt is unknown", Formats.KindOf("a.txt") == MediaKind.Unknown);
Check("jpg == jpeg", Formats.SameFormat("x.jpeg", ".jpg"));

var pngActions = ActionCatalog.For(["a.png"]).Select(a => a.Id).ToList();
Check("png gets no 'to PNG'", !pngActions.Contains("img.png"));
Check("png gets JPG/WebP/PDF", pngActions.Contains("img.jpg") && pngActions.Contains("img.webp") && pngActions.Contains("img.pdf"));
Check("png gets no compress", !pngActions.Contains("img.compress"));
Check("unknown files get nothing", ActionCatalog.For(["a.txt", "b.docx"]).Count == 0);
var mixed = ActionCatalog.For(["a.png", "b.mp3"]).Select(a => a.Kind).Distinct().ToList();
Check("mixed selection offers both kinds", mixed.Contains(MediaKind.Image) && mixed.Contains(MediaKind.Audio));
Check("pdf actions use renderer", ActionCatalog.For(["a.pdf"]).All(a => a.Engine == Engine.RenderPdf));

Console.WriteLine("Output paths");
var existing = Path.Combine(root, "photo.webp");
File.WriteAllText(existing, "x");
var next = OutputPaths.NextTo(Path.Combine(root, "photo.png"), ".webp");
Check("never overwrites", next == Path.Combine(root, "photo (2).webp"), next);
Check("suffix", OutputPaths.NextTo(Path.Combine(root, "clip.mp4"), ".mp4", " (muted)") == Path.Combine(root, "clip (muted).mp4"));
File.Delete(existing);

Console.WriteLine("Error messages");
Check("no audio explained", Ffmpeg.Explain("Stream map '0:a:0' matches no streams.").Contains("no sound"));
Check("last line fallback", Ffmpeg.Explain("line one\nreal problem\n") == "real problem");

Console.WriteLine("Argument safety");
var weird = Path.Combine(root, "my \"odd\" file, v2.png");
var built = FfmpegArgs.Build(ActionCatalog.All.First(a => a.Id == "img.webp"), weird, "out.webp");
Check("path passed as one argument", built.Contains(weird));
Check("never overwrites (-n)", built.Contains("-n") && !built.Contains("-y"));

// ---------- Conversion checks (real FFmpeg) ----------
var ffmpeg = Ffmpeg.Locate();
if (ffmpeg is null)
{
    Console.WriteLine("ffmpeg not found; skipping conversion checks");
}
else
{
    Console.WriteLine($"Conversions with {ffmpeg}");

    string Fixture(string name, params string[] ffArgs)
    {
        var path = Path.Combine(root, name);
        Run("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", .. ffArgs, path]);
        return path;
    }

    // Odd sizes on purpose: they break naive yuv420p encoders.
    var png = Fixture("alpha shot.png", "-f", "lavfi", "-i", "color=c=0x3366ff@0.5:s=101x75,format=rgba", "-frames:v", "1");
    var jpg = Fixture("photo.jpg", "-f", "lavfi", "-i", "testsrc2=s=203x151", "-frames:v", "1");
    var gif = Fixture("anim.gif", "-f", "lavfi", "-i", "testsrc2=s=120x90:d=1:r=10");
    var mp4 = Fixture("clip.mp4", "-f", "lavfi", "-i", "testsrc2=s=321x241:d=2", "-f", "lavfi", "-i", "sine=f=440:d=2",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-vf", "scale=320:240", "-c:a", "aac", "-shortest");
    var silent = Fixture("silent.mov", "-f", "lavfi", "-i", "testsrc2=s=160x120:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p");
    var wav = Fixture("voice note.wav", "-f", "lavfi", "-i", "sine=f=330:d=2");

    var service = new ConversionService(ffmpeg);

    async Task<ConversionResult> Do(string id, params string[] files)
    {
        var action = ActionCatalog.All.First(a => a.Id == id);
        var results = await service.RunAsync(action, files);
        return results.First(r => r.Status != ResultStatus.Skipped);
    }

    async Task Expect(string id, string file, string codec)
    {
        var r = await Do(id, file);
        var ok = r.Status == ResultStatus.Done && File.Exists(r.Output);
        var detail = r.Message ?? "";
        if (ok && codec.Length > 0)
        {
            var stream = codec is "mp3" or "aac" or "flac" or "opus" or "pcm_s16le" ? "a:0" : "v:0";
            var actual = Run("ffprobe", "-v", "error", "-select_streams", stream, "-show_entries", "stream=codec_name", "-of", "default=nw=1:nk=1", r.Output!);
            ok = actual == codec;
            detail = $"codec {actual}";
        }

        Check($"{id} on {Path.GetFileName(file)} -> {Path.GetFileName(r.Output ?? "?")}", ok, detail);
    }

    await Expect("img.jpg", png, "mjpeg");
    await Expect("img.webp", png, "webp");
    await Expect("img.avif", png, "av1");
    await Expect("img.png", jpg, "png");
    await Expect("img.compress", jpg, "mjpeg");
    await Expect("img.half", jpg, "mjpeg");
    await Expect("img.png", gif, "png");
    await Expect("vid.mp4", silent, "h264");
    await Expect("vid.webm", mp4, "vp9");
    await Expect("vid.gif", mp4, "gif");
    await Expect("vid.mp3", mp4, "mp3");
    await Expect("vid.compress", mp4, "h264");
    await Expect("vid.mute", mp4, "h264");
    await Expect("aud.mp3", wav, "mp3");
    await Expect("aud.m4a", wav, "aac");
    await Expect("aud.flac", wav, "flac");
    await Expect("aud.opus", wav, "opus");
    await Expect("aud.compress", wav, "mp3");

    // JPG flattening: the half-transparent blue must sit on white, not black.
    var flat = Path.Combine(root, "alpha shot.jpg");
    var raw = Path.Combine(root, "pixel.rgb");
    Run("ffmpeg", "-v", "error", "-i", flat, "-vf", "format=rgb24,crop=2:2:50:36", "-f", "rawvideo", raw);
    var pixel = File.ReadAllBytes(raw);
    Check("jpg flattens on white (not black)", pixel.Length >= 3 && pixel[0] > 100, $"red {(pixel.Length > 0 ? pixel[0] : -1)}");

    var muted = Path.Combine(root, "clip (muted).mp4");
    Check("muted video has no audio", Probe(muted, "stream=codec_type").Trim() == "video");

    var half = Probe(Path.Combine(root, "photo (50%).jpg"), "stream=width,height").Replace("\n", "x");
    Check("50% resize", half == "101x75", half);

    var noSound = await Do("vid.mp3", silent);
    Check("no-audio video fails politely", noSound.Status == ResultStatus.Failed && noSound.Message!.Contains("no sound"), noSound.Message ?? "");
    Check("failed output removed", !File.Exists(Path.Combine(root, "silent.mp3")));

    var again = await Do("img.jpg", png);
    Check("second run gets a new name", again.Output == Path.Combine(root, "alpha shot (2).jpg"), again.Output ?? "");

    var pdf = await Do("img.pdf", png, jpg, gif);
    Check("combine to pdf", pdf.Status == ResultStatus.Done && pdf.Output!.EndsWith("Combined.pdf"), pdf.Message ?? "");
    if (pdf.Status == ResultStatus.Done)
    {
        Run("qpdf", "--check", pdf.Output!);
        var info = Run("pdfinfo", pdf.Output!);
        Check("pdf has 3 pages and passes qpdf --check", System.Text.RegularExpressions.Regex.IsMatch(info, @"Pages:\s+3\b"), info);
        Run("pdftoppm", "-r", "20", "-png", pdf.Output!, Path.Combine(root, "render"));
        Check("pdf pages render", Directory.GetFiles(root, "render*.png").Length == 3);
    }

    var skipped = await service.RunAsync(ActionCatalog.All.First(a => a.Id == "aud.mp3"), [png, wav]);
    Check("mixed run skips non-audio", skipped.Count(r => r.Status == ResultStatus.Skipped) == 1 && skipped.Count(r => r.Status == ResultStatus.Done) == 1);

    var noFfmpeg = await new ConversionService(null).RunAsync(ActionCatalog.All.First(a => a.Id == "aud.mp3"), [wav]);
    Check("missing ffmpeg explained", noFfmpeg[0].Message == ConversionService.FfmpegMissing);

    var noRenderer = await service.RunAsync(ActionCatalog.All.First(a => a.Id == "pdf.png"), [pdf.Output!]);
    Check("pdf render without renderer fails politely", noRenderer[0].Status == ResultStatus.Failed);

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    var big = Fixture("long.mp4", "-f", "lavfi", "-i", "testsrc2=s=1280x720:d=30", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p");
    var cancelled = false;
    try
    {
        await service.RunAsync(ActionCatalog.All.First(a => a.Id == "vid.webm"), [big], ct: cts.Token);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }

    Check("cancel stops and cleans up", cancelled && !File.Exists(Path.Combine(root, "long.webm")));
}

Directory.Delete(root, recursive: true);
Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
