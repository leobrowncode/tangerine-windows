namespace Tangerine.Core;

public enum FileKind
{
    Unknown,
    Image,
    Vector,
    Audio,
    Video,
    Pdf,
    Document,
    Slides,
    Spreadsheet,
    Subtitle,
    Archive,
}

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = FileKind.Image, [".jpeg"] = FileKind.Image, [".png"] = FileKind.Image,
        [".webp"] = FileKind.Image, [".heic"] = FileKind.Image, [".heif"] = FileKind.Image,
        [".tif"] = FileKind.Image, [".tiff"] = FileKind.Image, [".avif"] = FileKind.Image,
        [".bmp"] = FileKind.Image, [".gif"] = FileKind.Image, [".ico"] = FileKind.Image,
        [".svg"] = FileKind.Vector,

        [".mp3"] = FileKind.Audio, [".m4a"] = FileKind.Audio, [".wav"] = FileKind.Audio,
        [".flac"] = FileKind.Audio, [".ogg"] = FileKind.Audio, [".oga"] = FileKind.Audio,
        [".opus"] = FileKind.Audio, [".aiff"] = FileKind.Audio, [".aif"] = FileKind.Audio,
        [".wma"] = FileKind.Audio, [".aac"] = FileKind.Audio,

        [".mp4"] = FileKind.Video, [".m4v"] = FileKind.Video, [".mov"] = FileKind.Video,
        [".mkv"] = FileKind.Video, [".webm"] = FileKind.Video, [".avi"] = FileKind.Video,
        [".wmv"] = FileKind.Video,

        [".pdf"] = FileKind.Pdf,

        [".docx"] = FileKind.Document, [".md"] = FileKind.Document, [".markdown"] = FileKind.Document,
        [".txt"] = FileKind.Document, [".rtf"] = FileKind.Document, [".html"] = FileKind.Document,
        [".htm"] = FileKind.Document,

        [".pptx"] = FileKind.Slides, [".ppt"] = FileKind.Slides,

        [".xlsx"] = FileKind.Spreadsheet, [".csv"] = FileKind.Spreadsheet,
        [".tsv"] = FileKind.Spreadsheet, [".ods"] = FileKind.Spreadsheet,

        [".srt"] = FileKind.Subtitle, [".vtt"] = FileKind.Subtitle,

        [".zip"] = FileKind.Archive, [".tar"] = FileKind.Archive, [".gz"] = FileKind.Archive,
        [".tgz"] = FileKind.Archive, [".tar.gz"] = FileKind.Archive,
    };

    public static FileKind Of(string path)
    {
        if (Directory.Exists(path)) return FileKind.Unknown;
        return ByExtension.TryGetValue(PathNames.Extension(path), out var kind) ? kind : FileKind.Unknown;
    }
}
