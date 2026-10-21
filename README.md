<p align="center"><img src="docs/logo.png" width="96" alt="Halora logo"></p>

<h1 align="center">Halora</h1>

<p align="center">Drag a file, hold Shift, let go. Pick a format. Done.</p>

Halora is a small Windows tray app for converting files without opening anything. Start dragging a file from Explorer or the desktop, hold **Shift**, and let go anywhere. A ring of formats opens where you dropped it. Click one, and the converted file is saved next to the original.

Everything runs on your PC. Nothing is uploaded.

## What it converts

| You drop | You can get |
| --- | --- |
| Images (PNG, JPG, WebP, AVIF, BMP, TIFF, GIF, HEIC*) | PNG, JPG, WebP, AVIF, a smaller copy, a 50% copy, or one PDF with all the images |
| Videos (MP4, MOV, MKV, WebM, AVI, WMV…) | MP4, WebM, animated GIF, the sound as MP3, a smaller copy, a muted copy |
| Audio (MP3, WAV, FLAC, M4A, OGG, Opus…) | MP3, M4A, WAV, FLAC, Opus, a smaller copy |
| PDFs | Every page as PNG or JPG |

You can drop several files at once, even of different kinds. Files that don't fit the format you pick are skipped. Originals are never changed or overwritten: a second conversion becomes `photo (2).webp`.

\* HEIC needs FFmpeg 7.1 or newer.

## Install

1. Download `Halora-win-x64.zip` from [Releases](../../releases) and unzip it anywhere.
2. Install FFmpeg once. Open Terminal and run:
   ```
   winget install Gyan.FFmpeg
   ```
3. Run `Halora.exe`. It appears in the tray. Right-click the icon for **Start with Windows**.

Windows SmartScreen may warn the first time because the app isn't code-signed yet. Click **More info → Run anyway**.

## How it works

Windows has no event for "a file is being dragged somewhere", so Halora:

1. watches the mouse with a low-level hook on its own thread,
2. notices when the left button is held, the mouse moves, and Shift is down,
3. shows an invisible drop window that follows the cursor,
4. reads the file paths when you let go, and always answers **Copy** so Explorer never deletes the original. In Explorer, Shift + drag normally means *move*.

Conversions use [FFmpeg](https://ffmpeg.org). PDF pages are rendered with the PDF engine built into Windows. Images → PDF is written by Halora itself, embedding the JPEGs without re-compressing them.

```
src/Halora.Core    conversions, formats, file naming (plain .NET, tested on Linux too)
src/Halora.App     the Windows app: mouse hook, drop window, ring menu, tray
tests/Halora.Tests runs every conversion through real FFmpeg and checks the output
```

## Known limits

- Explorer also uses Shift + drag, to move files. While Halora is on, Shift + drag opens Halora instead. To move files, drag with the right mouse button and pick **Move here**, or switch Halora off from the tray. Releasing Shift before letting go also cancels Halora.
- Drags from apps running as administrator can't be seen by a normal app. This is a Windows rule.
- Folders and files dragged out of zip archives or Outlook aren't supported yet.

## Build from source

Needs the .NET 8 SDK on Windows.

```
dotnet run --project tests/Halora.Tests    # needs ffmpeg, ffprobe, qpdf and pdfinfo on PATH
dotnet run --project src/Halora.App
```

## License

MIT. FFmpeg is a separate program with its own license and isn't bundled.
