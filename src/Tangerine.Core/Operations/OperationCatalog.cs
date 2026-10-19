namespace Tangerine.Core.Operations;

/// <summary>All operations the wheel can offer, in display order.</summary>
public sealed class OperationCatalog
{
    private readonly List<Operation> _operations = [];

    public IReadOnlyList<Operation> All => _operations;

    public void Register(Operation operation)
    {
        if (_operations.Any(o => o.Id == operation.Id))
            throw new InvalidOperationException($"Operation {operation.Id} is already registered.");
        _operations.Add(operation);
    }

    public IReadOnlyList<Operation> For(IReadOnlyList<string> inputs) =>
        inputs.Count == 0 ? [] : _operations.Where(o => o.AppliesTo(inputs)).ToList();

    public Operation? Find(string id) => _operations.FirstOrDefault(o => o.Id == id);

    public static OperationCatalog CreateDefault()
    {
        var catalog = new OperationCatalog();

        foreach (var (ext, label) in new[]
                 {
                     (".jpg", "JPG"), (".png", "PNG"), (".webp", "WebP"), (".tiff", "TIFF"),
                     (".avif", "AVIF"), (".bmp", "BMP"), (".ico", "ICO"), (".pdf", "PDF"),
                 })
            catalog.Register(new ImageConvertOperation(ext, label));
        catalog.Register(new MediaConvertOperation(".mp4", "MP4", new HashSet<FileKind> { FileKind.Image }, p => PathNames.Extension(p) == ".gif"));

        foreach (var (ext, label) in new[]
                 {
                     (".mp4", "MP4"), (".mov", "MOV"), (".mkv", "MKV"), (".webm", "WebM"), (".avi", "AVI"),
                     (".wmv", "WMV"), (".gif", "GIF"), (".mp3", "MP3"),
                 })
            catalog.Register(new MediaConvertOperation(ext, label, MediaArgs.Video));

        foreach (var (ext, label) in new[]
                 {
                     (".mp3", "MP3"), (".m4a", "M4A"), (".wav", "WAV"), (".flac", "FLAC"), (".ogg", "OGG"),
                     (".opus", "Opus"), (".aiff", "AIFF"), (".wma", "WMA"),
                 })
            catalog.Register(new MediaConvertOperation(ext, label, MediaArgs.Audio));

        foreach (var (ext, label) in new[] { (".docx", "DOCX"), (".md", "Markdown"), (".txt", "TXT"), (".html", "HTML"), (".epub", "EPUB") })
            catalog.Register(new DocumentConvertOperation(ext, label));
        catalog.Register(new PdfToImagesOperation(".jpg", "JPG"));
        catalog.Register(new PdfToImagesOperation(".png", "PNG"));

        foreach (var (ext, label) in new[] { (".xlsx", "XLSX"), (".ods", "ODS"), (".csv", "CSV"), (".tsv", "TSV") })
            catalog.Register(new SpreadsheetConvertOperation(ext, label));

        catalog.Register(new SubtitleConvertOperation(".srt", "SRT"));
        catalog.Register(new SubtitleConvertOperation(".vtt", "VTT"));

        foreach (var (ext, label) in new[] { (".zip", "ZIP"), (".tar", "TAR"), (".tar.gz", "TAR.GZ") })
            catalog.Register(new ArchiveConvertOperation(ext, label));

        catalog.Register(new ImageCompressOperation());
        catalog.Register(new MediaCompressOperation());
        catalog.Register(new ImageResizeOperation());
        catalog.Register(new ImageCropSquareOperation());
        catalog.Register(new ImageRotateOperation());
        catalog.Register(new ImageStripMetadataOperation());
        catalog.Register(new ImagesToPdfOperation());
        catalog.Register(new MediaTrimOperation());
        catalog.Register(new VideoMuteOperation());
        catalog.Register(new AudioNormalizeOperation());
        catalog.Register(new MediaMergeOperation());
        catalog.Register(new PdfMergeOperation());
        catalog.Register(new PdfSplitOperation());
        catalog.Register(new ArchiveExtractOperation());
        catalog.Register(new ZipOperation());
        catalog.Register(new GzipOperation());

        return catalog;
    }
}
