using System.Diagnostics;
using Tangerine.Core.Operations;

namespace Tangerine.Core.Office;

/// <summary>Something that can render office documents (DOCX, PPTX, XLSX…) to PDF.</summary>
public interface IPdfExporter
{
    string Name { get; }
    bool CanExport(string path);
    Task ExportPdfAsync(string input, string output, CancellationToken cancellationToken);
}

/// <summary>Uses a local LibreOffice install in headless mode.</summary>
public sealed class LibreOfficeExporter(string soffice) : IPdfExporter
{
    public string Name => "LibreOffice";

    public static LibreOfficeExporter? TryCreate()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                candidates.Add(Path.Combine(Environment.GetFolderPath(root), "LibreOffice", "program", "soffice.exe"));
        }
        else
        {
            candidates.AddRange(["/usr/bin/soffice", "/usr/local/bin/soffice", "/usr/lib/libreoffice/program/soffice"]);
        }
        var found = candidates.FirstOrDefault(File.Exists);
        return found is null ? null : new LibreOfficeExporter(found);
    }

    public bool CanExport(string path) => PathNames.Extension(path) is ".docx" or ".rtf" or ".pptx" or ".ppt" or ".xlsx" or ".ods";

    public async Task ExportPdfAsync(string input, string output, CancellationToken cancellationToken)
    {
        var work = Path.Combine(Path.GetTempPath(), "Tangerine", "lo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var psi = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            // A private profile avoids clashing with a LibreOffice window the user already has open.
            foreach (var a in new[] { $"-env:UserInstallation=file:///{work.Replace('\\', '/')}/profile", "--headless", "--norestore", "--convert-to", "pdf", "--outdir", work, input })
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
            var produced = Path.Combine(work, Path.GetFileNameWithoutExtension(input) + ".pdf");
            if (process.ExitCode != 0 || !File.Exists(produced))
                throw new InvalidOperationException("LibreOffice could not convert the file. " + (await stderr).Trim());
            File.Move(produced, output);
        }
        finally
        {
            PerFileOperation.TryDelete(work);
        }
    }
}

public sealed class OfficePdfOperation(IReadOnlyList<IPdfExporter> exporters) : PerFileOperation
{
    public override string Id => "office.convert.pdf";
    public override string Label => "PDF";
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Document, FileKind.Slides, FileKind.Spreadsheet };
    protected override string TargetExtension => ".pdf";
    protected override bool AcceptsInput(string path) => base.AcceptsInput(path) && exporters.Any(e => e.CanExport(path));

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var exporter = exporters.First(e => e.CanExport(input));
        var output = context.Output.FileFor(input, ".pdf");
        return [await WriteGuarded(output, path => exporter.ExportPdfAsync(Path.GetFullPath(input), path, cancellationToken)).ConfigureAwait(false)];
    }
}

/// <summary>Exports slides to PDF and renders each page, so all engines give identical results.</summary>
public sealed class SlidesToImagesOperation(string extension, string label, IReadOnlyList<IPdfExporter> exporters) : PerFileOperation
{
    private readonly PdfToImagesOperation _render = new(extension, label);

    public override string Id => $"slides.render{extension}";
    public override string Label => label;
    public override string Title => $"Export slides as {label}";
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Slides };
    protected override bool AcceptsInput(string path) => base.AcceptsInput(path) && exporters.Any(e => e.CanExport(path));

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Path.GetTempPath(), "Tangerine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var pdf = Path.Combine(temp, PathNames.Stem(input) + ".pdf");
            await exporters.First(e => e.CanExport(input)).ExportPdfAsync(Path.GetFullPath(input), pdf, cancellationToken).ConfigureAwait(false);
            progress.Report(0.5);

            var outDir = context.Output.DirectoryFor(input);
            var renderContext = new OperationContext { Settings = new ConverterSettings
            {
                OutputDirectory = outDir,
                ImageQuality = context.Settings.ImageQuality,
                PdfDpi = Math.Min(context.Settings.PdfDpi, 200),
            } };
            return await _render.RunAsync([pdf], renderContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(temp);
        }
    }
}

public static class OfficeCatalog
{
    public static void Register(OperationCatalog catalog, IReadOnlyList<IPdfExporter> exporters)
    {
        if (exporters.Count == 0) return;
        catalog.Register(new OfficePdfOperation(exporters));
        catalog.Register(new SlidesToImagesOperation(".png", "PNG", exporters));
        catalog.Register(new SlidesToImagesOperation(".jpg", "JPG", exporters));
    }
}
