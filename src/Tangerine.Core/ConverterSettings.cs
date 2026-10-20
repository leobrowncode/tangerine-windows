namespace Tangerine.Core;

public sealed class ConverterSettings
{
    /// <summary>JPEG/WebP/HEIC/AVIF quality, 1-100.</summary>
    public int ImageQuality { get; set; } = 85;

    /// <summary>Quality used by the image "Compress" tool, 1-100.</summary>
    public int CompressQuality { get; set; } = 70;

    /// <summary>Folder for results. Null or empty writes next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Explicit ffmpeg.exe path; when empty the app folder and PATH are searched.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Resolution used when rendering PDF pages to images.</summary>
    public int PdfDpi { get; set; } = 300;
}

public sealed class OutputLocator(ConverterSettings settings)
{
    public string DirectoryFor(string input)
    {
        var dir = string.IsNullOrWhiteSpace(settings.OutputDirectory)
            ? Path.GetDirectoryName(Path.GetFullPath(input))!
            : settings.OutputDirectory!;
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A unique file path for <paramref name="input"/> converted to <paramref name="extension"/>.</summary>
    public string FileFor(string input, string extension, string suffix = "") =>
        PathNames.Unique(Path.Combine(DirectoryFor(input), PathNames.Stem(input) + suffix + extension));

    public string FolderFor(string input, string suffix = "")
    {
        var path = PathNames.Unique(Path.Combine(DirectoryFor(input), PathNames.Stem(input) + suffix));
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class MissingDependencyException(string dependency, string hint)
    : Exception($"{dependency} is required for this conversion. {hint}")
{
    public string Dependency { get; } = dependency;
}
