using Tangerine.Core.Documents;
using Tangerine.Core.Sheets;
using Tangerine.Core.Subtitles;

namespace Tangerine.Core.Operations;

public sealed class DocumentConvertOperation(string extension, string label) : PerFileOperation
{
    public override string Id => $"document.convert{extension}";
    public override string Label => label;
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Document, FileKind.Pdf };
    protected override string TargetExtension => extension;

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var doc = DocReaders.Read(input);
            var output = context.Output.FileFor(input, extension);
            return [WriteGuarded(output, path => DocWriters.Write(doc, path))];
        }, cancellationToken);
}

public sealed class SpreadsheetConvertOperation(string extension, string label) : PerFileOperation
{
    public override string Id => $"sheet.convert{extension}";
    public override string Label => label;
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Spreadsheet };
    protected override string TargetExtension => extension;

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var sheets = Workbook.Read(input);
            if (Workbook.IsSingleSheetFormat(extension))
            {
                var nonEmpty = sheets.Where(s => s.Rows.Count > 0).ToList();
                if (nonEmpty.Count > 1)
                {
                    var outputs = new List<string>();
                    foreach (var sheet in nonEmpty)
                    {
                        var output = context.Output.FileFor(input, extension, $" - {Sanitize(sheet.Name)}");
                        outputs.Add(WriteGuarded(output, path => Workbook.Write([sheet], path)));
                    }
                    return outputs;
                }
                sheets = nonEmpty.Count == 1 ? nonEmpty : [new Sheet("Sheet1")];
            }
            var single = context.Output.FileFor(input, extension);
            return [WriteGuarded(single, path => Workbook.Write(sheets, path))];
        }, cancellationToken);

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public sealed class SubtitleConvertOperation(string extension, string label) : PerFileOperation
{
    public override string Id => $"subtitle.convert{extension}";
    public override string Label => label;
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Subtitle };
    protected override string TargetExtension => extension;

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var cues = SubtitleFile.Parse(File.ReadAllText(input));
            var output = context.Output.FileFor(input, extension);
            return [WriteGuarded(output, path => File.WriteAllText(path, extension == ".vtt" ? SubtitleFile.ToVtt(cues) : SubtitleFile.ToSrt(cues)))];
        }, cancellationToken);
}
