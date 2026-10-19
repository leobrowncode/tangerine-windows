using System.Runtime.InteropServices;
using Tangerine.Core;
using Tangerine.Core.Office;

namespace Tangerine.App.Office;

/// <summary>Exports DOCX/PPTX/XLSX to PDF through an installed Microsoft Office, via COM automation.</summary>
internal sealed class MicrosoftOfficeExporter : IPdfExporter
{
    private const int WdExportFormatPdf = 17;
    private const int PpSaveAsPdf = 32;
    private const int XlTypePdf = 0;
    private const int MsoTrue = -1, MsoFalse = 0;

    private readonly Type? _word = Type.GetTypeFromProgID("Word.Application");
    private readonly Type? _powerPoint = Type.GetTypeFromProgID("PowerPoint.Application");
    private readonly Type? _excel = Type.GetTypeFromProgID("Excel.Application");

    public string Name => "Microsoft Office";

    public static MicrosoftOfficeExporter? TryCreate()
    {
        var exporter = new MicrosoftOfficeExporter();
        return exporter._word is null && exporter._powerPoint is null && exporter._excel is null ? null : exporter;
    }

    public bool CanExport(string path) => PathNames.Extension(path) switch
    {
        ".docx" or ".rtf" => _word is not null,
        ".pptx" or ".ppt" => _powerPoint is not null,
        ".xlsx" or ".ods" => _excel is not null,
        _ => false,
    };

    public Task ExportPdfAsync(string input, string output, CancellationToken cancellationToken) =>
        RunSta(() =>
        {
            switch (PathNames.Extension(input))
            {
                case ".docx" or ".rtf": ExportWord(input, output); break;
                case ".pptx" or ".ppt": ExportPowerPoint(input, output); break;
                default: ExportExcel(input, output); break;
            }
        }, cancellationToken);

    private void ExportWord(string input, string output)
    {
        dynamic app = Activator.CreateInstance(_word!)!;
        try
        {
            app.DisplayAlerts = 0;
            dynamic doc = app.Documents.Open(input, false, true, false);
            try { doc.ExportAsFixedFormat(output, WdExportFormatPdf); }
            finally { doc.Close(0); Marshal.FinalReleaseComObject(doc); }
        }
        finally
        {
            if (app.Documents.Count == 0) app.Quit(0);
            Marshal.FinalReleaseComObject(app);
        }
    }

    private void ExportPowerPoint(string input, string output)
    {
        dynamic app = Activator.CreateInstance(_powerPoint!)!;
        try
        {
            dynamic presentation = app.Presentations.Open(input, MsoTrue, MsoFalse, MsoFalse);
            try { presentation.SaveAs(output, PpSaveAsPdf); }
            finally { presentation.Close(); Marshal.FinalReleaseComObject(presentation); }
        }
        finally
        {
            // PowerPoint is single-instance: only quit if the user has nothing else open.
            if (app.Presentations.Count == 0) app.Quit();
            Marshal.FinalReleaseComObject(app);
        }
    }

    private void ExportExcel(string input, string output)
    {
        dynamic app = Activator.CreateInstance(_excel!)!;
        try
        {
            app.DisplayAlerts = false;
            dynamic workbook = app.Workbooks.Open(input, 0, true);
            try { workbook.ExportAsFixedFormat(XlTypePdf, output); }
            finally { workbook.Close(false); Marshal.FinalReleaseComObject(workbook); }
        }
        finally
        {
            if (app.Workbooks.Count == 0) app.Quit();
            Marshal.FinalReleaseComObject(app);
        }
    }

    private static Task RunSta(Action action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                tcs.SetResult();
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (COMException ex)
            {
                tcs.SetException(new InvalidOperationException("Microsoft Office could not convert the file: " + ex.Message, ex));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }) { IsBackground = true, Name = "Office export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
