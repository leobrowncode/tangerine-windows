using System.IO.Compression;
using Tangerine.Core.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Tangerine.Core.Tests;

public class DocumentTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private const string SampleMarkdown = """
        # Release notes

        Tangerine is **fast** and *local*. See [the site](https://example.com).

        - First item
        - Second item
            - Nested item

        1. One
        2. Two

        > Quoted text

        ```
        code();
        ```
        """;

    private string MakePdf(string name = "paper.pdf", int pages = 2)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 1; i <= pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText($"Chapter {i}", 24, new PdfPoint(50, 760), font);
            page.AddText($"This is the body text of page {i} in the sample.", 11, new PdfPoint(50, 700), font);
            page.AddText("A second line of the same paragraph follows.", 11, new PdfPoint(50, 686), font);
        }
        var path = _dir.File(name);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public void Markdown_is_parsed_into_blocks()
    {
        var doc = DocReaders.FromMarkdown(SampleMarkdown);
        Assert.Equal("Release notes", doc.Title);
        Assert.IsType<Heading>(doc.Blocks[0]);
        var para = Assert.IsType<Paragraph>(doc.Blocks[1]);
        Assert.Contains(para.Content, i => i.Bold && i.Text == "fast");
        Assert.Contains(para.Content, i => i.Italic && i.Text == "local");
        Assert.Contains(para.Content, i => i.Link == "https://example.com");
        var items = doc.Blocks.OfType<ListItem>().ToList();
        Assert.Equal(5, items.Count);
        Assert.Equal(1, items[2].Depth);
        Assert.True(items[3].Ordered);
        Assert.Single(doc.Blocks.OfType<Quote>());
        Assert.Equal("code();", Assert.Single(doc.Blocks.OfType<CodeBlock>()).Text);
    }

    [Fact]
    public async Task Markdown_round_trips_through_docx()
    {
        var md = _dir.Write("notes.md", SampleMarkdown);
        var docx = await Run.Single("document.convert.docx", md);
        var back = await Run.Single("document.convert.md", docx);
        var text = File.ReadAllText(back);
        Assert.Contains("# Release notes", text);
        Assert.Contains("**fast**", text);
        Assert.Contains("*local*", text);
        Assert.Contains("[the site](https://example.com/)", text);
        Assert.Contains("- First item", text);
        Assert.Contains("    - Nested item", text);
        Assert.Contains("1. One", text);
        Assert.Contains("2. Two", text);
    }

    [Fact]
    public async Task Markdown_to_html_is_well_formed()
    {
        var md = _dir.Write("notes.md", SampleMarkdown);
        var html = File.ReadAllText(await Run.Single("document.convert.html", md));
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<strong>fast</strong>", html);
        Assert.Contains("<ul>", html);
        System.Xml.Linq.XDocument.Parse(html[html.IndexOf('<', 1)..]);
    }

    [Fact]
    public async Task Markdown_to_epub_has_valid_structure()
    {
        var md = _dir.Write("book.md", "# One\n\nFirst chapter.\n\n# Two\n\nSecond chapter.\n");
        var epub = await Run.Single("document.convert.epub", md);
        using var zip = ZipFile.OpenRead(epub);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
        Assert.NotNull(zip.GetEntry("META-INF/container.xml"));
        Assert.NotNull(zip.GetEntry("OEBPS/content.opf"));
        Assert.NotNull(zip.GetEntry("OEBPS/chapter-2.xhtml"));
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".opf")))
        {
            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd().Replace("<!DOCTYPE html>", "");
            System.Xml.Linq.XDocument.Parse(content);
        }
    }

    [Fact]
    public async Task Text_converts_to_markdown_escaping_specials()
    {
        var txt = _dir.Write("plain.txt", "First paragraph with *stars*.\n\nSecond paragraph.\n");
        var md = File.ReadAllText(await Run.Single("document.convert.md", txt));
        Assert.Contains(@"\*stars\*", md);
        Assert.Contains("Second paragraph.", md);
    }

    [Fact]
    public async Task Rtf_converts_to_text()
    {
        var rtf = _dir.Write("letter.rtf", @"{\rtf1\ansi\ansicpg1252{\fonttbl{\f0 Calibri;}}{\colortbl;\red0\green0\blue0;}\f0 Hello \b world\b0 .\par Caf\'e9 \u8364? price\par}");
        var text = File.ReadAllText(await Run.Single("document.convert.txt", rtf));
        Assert.Contains("Hello world.", text);
        Assert.Contains("Café € price", text);
        Assert.DoesNotContain("Calibri", text);
    }

    [Fact]
    public async Task Html_converts_to_markdown()
    {
        var html = _dir.Write("page.html", "<html><head><title>Page</title></head><body><h2>Hi</h2><p>Some <b>bold</b> text.</p><ul><li>a</li><li>b</li></ul></body></html>");
        var md = File.ReadAllText(await Run.Single("document.convert.md", html));
        Assert.Contains("## Hi", md);
        Assert.Contains("**bold**", md);
        Assert.Contains("- a", md);
    }

    [Fact]
    public async Task Pdf_text_is_extracted_with_headings()
    {
        var md = File.ReadAllText(await Run.Single("document.convert.md", MakePdf()));
        Assert.Contains("# Chapter 1", md);
        Assert.Contains("body text of page 2", md);
    }

    [Fact]
    public async Task Pdf_converts_to_docx()
    {
        var docx = await Run.Single("document.convert.docx", MakePdf());
        var doc = DocReaders.FromDocx(docx);
        Assert.Contains(doc.Blocks.OfType<Heading>(), h => DocModel.PlainText(h.Content) == "Chapter 2");
    }

    [Fact]
    public async Task Pdf_pages_render_to_png_folder()
    {
        var folder = await Run.Single("pdf.render.png", MakePdf());
        var files = Directory.GetFiles(folder).Order().ToList();
        Assert.Equal(2, files.Count);
        Assert.EndsWith("paper - page 01.png", files[0]);
        using var image = new ImageMagick.MagickImage(files[0]);
        Assert.InRange(image.Width, 2400u, 2500u);
    }

    [Fact]
    public async Task Single_page_pdf_renders_to_one_jpg()
    {
        var jpg = await Run.Single("pdf.render.jpg", MakePdf(pages: 1));
        Assert.EndsWith("paper.jpg", jpg);
    }

    [Fact]
    public async Task Pdfs_merge_and_split()
    {
        var merged = Assert.Single(await Run.Op("pdf.merge", [MakePdf("a.pdf", 2), MakePdf("b.pdf", 3)]));
        using (var pdf = PdfDocument.Open(merged)) Assert.Equal(5, pdf.NumberOfPages);

        var folder = await Run.Single("pdf.split", merged);
        Assert.Equal(5, Directory.GetFiles(folder, "*.pdf").Length);
    }
}
