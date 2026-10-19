using PDFtoImage;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Tangerine.Core.Operations;

public sealed class PdfToImagesOperation(string extension, string label) : PerFileOperation
{
    public override string Id => $"pdf.render{extension}";
    public override string Label => label;
    public override string Title => $"Export pages as {label}";
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Pdf };

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var bytes = File.ReadAllBytes(input);
            var pageCount = Conversion.GetPageCount(bytes);
            var options = new RenderOptions(Dpi: context.Settings.PdfDpi, WithAnnotations: true, WithFormFill: true, BackgroundColor: SkiaSharp.SKColors.White);

            if (pageCount == 1)
            {
                var output = context.Output.FileFor(input, extension);
                return [WriteGuarded(output, path => Render(path, bytes, 0, options, context.Settings.ImageQuality))];
            }

            var folder = context.Output.FolderFor(input, " (pages)");
            try
            {
                var digits = Math.Max(2, pageCount.ToString().Length);
                for (var page = 0; page < pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = Path.Combine(folder, $"{PathNames.Stem(input)} - page {(page + 1).ToString().PadLeft(digits, '0')}{extension}");
                    Render(file, bytes, page, options, context.Settings.ImageQuality);
                    progress.Report((page + 1.0) / pageCount);
                }
                return [folder];
            }
            catch
            {
                TryDelete(folder);
                throw;
            }
        }, cancellationToken);

    private void Render(string path, byte[] pdf, int page, RenderOptions options, int quality)
    {
        if (extension == ".png")
        {
            Conversion.SavePng(path, pdf, page, null, options);
            return;
        }
        using var bitmap = Conversion.ToImage(pdf, page, null, options);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}

public sealed class PdfMergeOperation : Operation
{
    public override string Id => "pdf.merge";
    public override string Label => "Merge";
    public override string Title => "Merge PDFs";
    public override OperationGroup Group => OperationGroup.Tool;

    public override bool AppliesTo(IReadOnlyList<string> inputs) =>
        inputs.Count >= 2 && inputs.All(p => File.Exists(p) && FileKinds.Of(p) == FileKind.Pdf);

    public override Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs, OperationContext context, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var output = context.Output.FileFor(inputs[0], ".pdf", " (merged)");
            var merged = PdfMerger.Merge(inputs.ToArray());
            File.WriteAllBytes(output, merged);
            return [output];
        }, cancellationToken);
}

public sealed class PdfSplitOperation : PerFileOperation
{
    public override string Id => "pdf.split";
    public override string Label => "Split";
    public override string Title => "Split into single pages";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Pdf };

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            using var document = PdfDocument.Open(input);
            var folder = context.Output.FolderFor(input, " (split)");
            try
            {
                var digits = Math.Max(2, document.NumberOfPages.ToString().Length);
                for (var page = 1; page <= document.NumberOfPages; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var builder = new PdfDocumentBuilder();
                    builder.AddPage(document, page);
                    File.WriteAllBytes(Path.Combine(folder, $"{PathNames.Stem(input)} - page {page.ToString().PadLeft(digits, '0')}.pdf"), builder.Build());
                    progress.Report((double)page / document.NumberOfPages);
                }
                return [folder];
            }
            catch
            {
                TryDelete(folder);
                throw;
            }
        }, cancellationToken);
}
