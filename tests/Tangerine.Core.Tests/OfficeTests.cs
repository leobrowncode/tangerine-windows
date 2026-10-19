using Tangerine.Core.Office;
using Tangerine.Core.Operations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Tangerine.Core.Tests;

public class OfficeTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class FakeExporter : IPdfExporter
    {
        public string Name => "Fake";
        public bool CanExport(string path) => PathNames.Extension(path) is ".pptx" or ".docx";

        public Task ExportPdfAsync(string input, string output, CancellationToken cancellationToken)
        {
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            for (var i = 0; i < 3; i++) builder.AddPage(PageSize.A4).AddText($"Slide {i + 1}", 30, new PdfPoint(60, 700), font);
            File.WriteAllBytes(output, builder.Build());
            return Task.CompletedTask;
        }
    }

    private static OperationCatalog Catalog()
    {
        var catalog = OperationCatalog.CreateDefault();
        OfficeCatalog.Register(catalog, [new FakeExporter()]);
        return catalog;
    }

    [Fact]
    public void Office_operations_only_appear_when_exporter_supports_file()
    {
        var catalog = Catalog();
        var pptx = _dir.Write("deck.pptx", "x");
        var csv = _dir.Write("data.csv", "a");
        Assert.Contains(catalog.For([pptx]), o => o.Id == "office.convert.pdf");
        Assert.Contains(catalog.For([pptx]), o => o.Id == "slides.render.png");
        Assert.DoesNotContain(catalog.For([csv]), o => o.Id == "office.convert.pdf");
        Assert.DoesNotContain(OperationCatalog.CreateDefault().For([pptx]), o => o.Id == "office.convert.pdf");
    }

    [Fact]
    public async Task Slides_render_to_images_next_to_original()
    {
        var pptx = _dir.Write("deck.pptx", "x");
        var op = Catalog().Find("slides.render.png")!;
        var outputs = await op.RunAsync([pptx], new OperationContext { Settings = new ConverterSettings() }, CancellationToken.None);
        var folder = Assert.Single(outputs);
        Assert.Equal(_dir.File("deck (pages)"), folder);
        Assert.Equal(3, Directory.GetFiles(folder, "*.png").Length);
    }

    [Fact]
    public async Task Docx_exports_to_pdf()
    {
        var docx = _dir.Write("letter.docx", "x");
        var op = Catalog().Find("office.convert.pdf")!;
        var output = Assert.Single(await op.RunAsync([docx], new OperationContext { Settings = new ConverterSettings() }, CancellationToken.None));
        Assert.Equal(_dir.File("letter.pdf"), output);
    }
}
