# Tangerine for Windows

The zero-click offline file converter, rebuilt for Windows 11.
This is a Windows take on [Tangerine for Mac](https://tangerineformac.com/).

**Drag a file, tap Shift, and drop.** A wheel of formats and tools opens around your cursor
while you're still dragging. Drop the file on what you need and the result is saved next to the original.
Everything runs locally and nothing is uploaded.

| Gesture | What happens |
| --- | --- |
| Drag a file, then tap **Shift** | **Conversion wheel**: formats on the inner ring, file tools on the outer ring |
| Drag anything, then tap **Ctrl** | **File actions wheel**: Share, Float, Copy, Move, Copy path, Terminal (works with folders) |
| Drop in the centre, press **Esc**, or tap the key again | Cancel |

You can change both keys (Shift, Ctrl, Alt, Caps Lock or off) in Settings.

## What it converts

| Kind | From | To | Tools |
| --- | --- | --- | --- |
| Images | JPG PNG WebP HEIC TIFF AVIF BMP GIF ICO SVG | JPG PNG WebP TIFF AVIF BMP ICO PDF | Compress, Resize, Crop square, Rotate, Strip metadata, Combine into PDF |
| Video | MP4 MOV MKV WebM AVI WMV M4V (and animated GIF) | MP4 MOV MKV WebM AVI WMV GIF, MP3 (extract audio) | Compress, Trim, Mute, Merge |
| Audio | MP3 M4A WAV FLAC OGG Opus AIFF WMA AAC | MP3 M4A WAV FLAC OGG Opus AIFF WMA | Compress, Trim, Normalize loudness, Merge |
| PDF | PDF | DOCX Markdown TXT HTML EPUB, JPG/PNG (all pages, 300 DPI) | Merge, Split into pages |
| Documents | DOCX Markdown TXT HTML RTF | DOCX Markdown TXT HTML EPUB, PDF* | |
| Slides | PPTX PPT | PDF*, JPG*, PNG* | |
| Spreadsheets | XLSX ODS CSV TSV | XLSX ODS CSV TSV, PDF* | |
| Subtitles | SRT VTT | SRT VTT | |
| Archives | ZIP TAR TAR.GZ GZ | ZIP TAR TAR.GZ | Extract |
| Anything | | | Compress to ZIP, GZIP |

\* Uses Microsoft Office if it's installed, otherwise LibreOffice. Without either, these options are hidden.

Audio and video use [FFmpeg](https://ffmpeg.org). If it's missing, Tangerine offers to install it with
`winget install Gyan.FFmpeg`. You can also drop `ffmpeg.exe` next to `Tangerine.exe` or point Settings at it.

HEIC files can be opened but not written: the bundled ImageMagick build doesn't include an HEVC encoder.

## How it works on Windows

- `GestureHook` installs low-level mouse and keyboard hooks on a dedicated thread. Once a mouse button is held
  and has moved past the system drag threshold, a tap of the trigger key opens the wheel. The key press
  is swallowed so Explorer doesn't switch the drag to "move".
- `WheelWindow` is a transparent, topmost, non-activating window that is also an OLE drop target. It appears
  centred on the cursor, nudges the in-progress drag so it notices the wheel immediately, reads the dragged
  paths, and offers only the operations that apply to them. It always reports a *copy* drop effect, so the
  drag source never deletes your original.
- Conversions run in the background with progress, Cancel, and Show/Open buttons in a small window in the
  bottom-right corner.
- Results are named `photo.png`, then `photo (1).png`, and so on, so nothing is ever overwritten.

## Project layout

```
src/Tangerine.Core     Cross-platform conversion engine (format catalog, operations, wheel geometry)
src/Tangerine.App      Windows 11 tray app (WPF): hooks, wheel, file actions, progress window, settings
tests/Tangerine.Core.Tests
```

Core libraries: Magick.NET (images), FFmpeg (audio/video), PdfPig and PDFtoImage/PDFium (PDF),
DocumentFormat.OpenXml, Markdig and ReverseMarkdown (documents), ClosedXML (spreadsheets).

## Build

Requires the .NET 10 SDK.

```powershell
dotnet test tests/Tangerine.Core.Tests
dotnet publish src/Tangerine.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The core and its tests also build and run on Linux and macOS. The app project builds anywhere
(`EnableWindowsTargeting`) but only runs on Windows 10 (19041) or later.

`Tangerine.exe --render-preview <folder>` renders the wheel, settings and progress windows to PNG and
exits. CI uses it as a UI smoke test.
