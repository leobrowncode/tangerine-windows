using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Tangerine.Core.Documents;

public static class DocWriters
{
    public static void Write(DocModel doc, string path)
    {
        switch (PathNames.Extension(path))
        {
            case ".md": File.WriteAllText(path, ToMarkdown(doc)); break;
            case ".txt": File.WriteAllText(path, ToPlainText(doc)); break;
            case ".html": File.WriteAllText(path, ToHtml(doc)); break;
            case ".docx": WriteDocx(doc, path); break;
            case ".epub": WriteEpub(doc, path); break;
            default: throw new NotSupportedException($"Cannot write {PathNames.Extension(path)} documents");
        }
    }

    #region Markdown & text

    public static string ToMarkdown(DocModel doc)
    {
        var sb = new StringBuilder();
        var orderedCounters = new Dictionary<int, int>();
        Block? previous = null;
        foreach (var block in doc.Blocks)
        {
            if (previous is not null && !(previous is ListItem && block is ListItem)) sb.Append('\n');
            if (block is not ListItem) orderedCounters.Clear();
            switch (block)
            {
                case Heading h:
                    sb.Append(new string('#', h.Level)).Append(' ').Append(MarkdownInlines(h.Content)).Append('\n');
                    break;
                case Paragraph p:
                    sb.Append(MarkdownInlines(p.Content)).Append('\n');
                    break;
                case ListItem li:
                    foreach (var deeper in orderedCounters.Keys.Where(k => k > li.Depth).ToList()) orderedCounters.Remove(deeper);
                    sb.Append(new string(' ', li.Depth * 4));
                    if (li.Ordered)
                    {
                        var n = orderedCounters.GetValueOrDefault(li.Depth) + 1;
                        orderedCounters[li.Depth] = n;
                        sb.Append(n).Append(". ");
                    }
                    else sb.Append("- ");
                    sb.Append(MarkdownInlines(li.Content)).Append('\n');
                    break;
                case Quote q:
                    sb.Append("> ").Append(MarkdownInlines(q.Content).Replace("\n", "\n> ")).Append('\n');
                    break;
                case CodeBlock code:
                    sb.Append("```\n").Append(code.Text).Append("\n```\n");
                    break;
                case Rule:
                    sb.Append("---\n");
                    break;
            }
            previous = block;
        }
        return sb.ToString();
    }

    private static string MarkdownInlines(IEnumerable<Inline> content)
    {
        var sb = new StringBuilder();
        foreach (var inline in content)
        {
            var text = inline.Code ? $"`{inline.Text}`" : EscapeMarkdown(inline.Text).Replace("\n", "  \n");
            if (text.Trim().Length > 0 && !inline.Code)
            {
                var lead = text[..(text.Length - text.TrimStart().Length)];
                var trail = text[text.TrimEnd().Length..];
                var core = text.Trim();
                if (inline.Bold && inline.Italic) core = $"***{core}***";
                else if (inline.Bold) core = $"**{core}**";
                else if (inline.Italic) core = $"*{core}*";
                text = lead + core + trail;
            }
            if (inline.Link is not null) text = $"[{text}]({inline.Link})";
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>') sb.Append('\\');
            sb.Append(c);
        }
        var escaped = sb.ToString();
        if (escaped.StartsWith('#') || escaped.StartsWith("- ") || escaped.StartsWith("+ ")) escaped = "\\" + escaped;
        return escaped;
    }

    public static string ToPlainText(DocModel doc)
    {
        var sb = new StringBuilder();
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Heading h: sb.Append(DocModel.PlainText(h.Content)).Append("\n\n"); break;
                case Paragraph p: sb.Append(DocModel.PlainText(p.Content)).Append("\n\n"); break;
                case ListItem li: sb.Append(new string(' ', li.Depth * 2)).Append("• ").Append(DocModel.PlainText(li.Content)).Append('\n'); break;
                case Quote q: sb.Append("    ").Append(DocModel.PlainText(q.Content)).Append("\n\n"); break;
                case CodeBlock c: sb.Append(c.Text).Append("\n\n"); break;
                case Rule: sb.Append("————————\n\n"); break;
            }
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    #endregion

    #region HTML / XHTML

    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    private const string Css = """
        body { font-family: "Segoe UI", system-ui, sans-serif; line-height: 1.6; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; color: #1f1f1f; }
        pre { background: #f4f4f4; padding: .75rem 1rem; border-radius: 6px; overflow-x: auto; }
        code { font-family: Consolas, "Cascadia Mono", monospace; }
        blockquote { border-left: 3px solid #ff8a1f; margin-left: 0; padding-left: 1rem; color: #555; }
        """;

    public static string ToHtml(DocModel doc)
    {
        var html = BuildXhtml(doc.Title, doc.Blocks);
        return "<!DOCTYPE html>\n" + html.ToString();
    }

    private static XElement BuildXhtml(string title, IEnumerable<Block> blocks)
    {
        var body = new XElement(Xhtml + "body");
        var listStack = new Stack<XElement>();

        foreach (var block in blocks)
        {
            if (block is ListItem li)
            {
                var tag = li.Ordered ? "ol" : "ul";
                while (listStack.Count > li.Depth + 1) listStack.Pop();
                if (listStack.Count == li.Depth + 1 && listStack.Peek().Name.LocalName != tag) listStack.Pop();
                while (listStack.Count < li.Depth + 1)
                {
                    var list = new XElement(Xhtml + tag);
                    if (listStack.Count == 0) body.Add(list);
                    else
                    {
                        var parentItem = listStack.Peek().Elements().LastOrDefault();
                        if (parentItem is null) { parentItem = new XElement(Xhtml + "li"); listStack.Peek().Add(parentItem); }
                        parentItem.Add(list);
                    }
                    listStack.Push(list);
                }
                listStack.Peek().Add(new XElement(Xhtml + "li", HtmlInlines(li.Content)));
                continue;
            }

            listStack.Clear();
            body.Add(block switch
            {
                Heading h => new XElement(Xhtml + $"h{Math.Clamp(h.Level, 1, 6)}", HtmlInlines(h.Content)),
                Paragraph p => new XElement(Xhtml + "p", HtmlInlines(p.Content)),
                Quote q => new XElement(Xhtml + "blockquote", new XElement(Xhtml + "p", HtmlInlines(q.Content))),
                CodeBlock c => new XElement(Xhtml + "pre", new XElement(Xhtml + "code", c.Text)),
                Rule => new XElement(Xhtml + "hr"),
                _ => null,
            });
        }

        return new XElement(Xhtml + "html",
            new XAttribute(XNamespace.Xmlns + "epub", "http://www.idpf.org/2007/ops"),
            new XElement(Xhtml + "head",
                new XElement(Xhtml + "meta", new XAttribute("charset", "utf-8")),
                new XElement(Xhtml + "title", title),
                new XElement(Xhtml + "style", Css)),
            body);
    }

    private static IEnumerable<object> HtmlInlines(IEnumerable<Inline> content)
    {
        foreach (var inline in content)
        {
            var parts = new List<object>();
            var lines = inline.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) parts.Add(new XElement(Xhtml + "br"));
                if (lines[i].Length > 0) parts.Add(new XText(lines[i]));
            }
            object node = parts.Count == 1 ? parts[0] : parts;
            if (inline.Code) node = new XElement(Xhtml + "code", node);
            if (inline.Italic) node = new XElement(Xhtml + "em", node);
            if (inline.Bold) node = new XElement(Xhtml + "strong", node);
            if (inline.Link is not null) node = new XElement(Xhtml + "a", new XAttribute("href", inline.Link), node);
            yield return node;
        }
    }

    #endregion

    #region DOCX

    public static void WriteDocx(DocModel doc, string path)
    {
        using var word = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = word.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        AddStyles(main);
        AddNumbering(main);
        word.PackageProperties.Title = doc.Title;

        var body = main.Document.Body!;
        var orderedListInstance = 1;
        Block? previous = null;
        foreach (var block in doc.Blocks)
        {
            if (block is ListItem { Ordered: true } && previous is not ListItem { Ordered: true }) orderedListInstance = RestartOrdered(main, orderedListInstance);
            body.Append(block switch
            {
                Heading h => StyledParagraph($"Heading{Math.Clamp(h.Level, 1, 6)}", h.Content, main),
                Paragraph p => StyledParagraph(null, p.Content, main),
                Quote q => StyledParagraph("Quote", q.Content, main),
                CodeBlock c => StyledParagraph("Code", [new Inline(c.Text, Code: true)], main),
                ListItem li => ListParagraph(li, main, li.Ordered ? orderedListInstance : 1),
                Rule => new W.Paragraph(new W.ParagraphProperties(new W.ParagraphBorders(
                    new W.BottomBorder { Val = W.BorderValues.Single, Size = 6, Space = 1, Color = "BFBFBF" }))),
                _ => new W.Paragraph(),
            });
            previous = block;
        }
        body.Append(new W.SectionProperties(
            new W.PageSize { Width = 11906, Height = 16838 },
            new W.PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 708, Footer = 708, Gutter = 0 }));
        main.Document.Save();
    }

    private static W.Paragraph StyledParagraph(string? style, IEnumerable<Inline> content, MainDocumentPart main)
    {
        var p = new W.Paragraph();
        if (style is not null) p.Append(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }));
        AppendRuns(p, content, main);
        return p;
    }

    private static W.Paragraph ListParagraph(ListItem li, MainDocumentPart main, int numId)
    {
        var p = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = "ListParagraph" },
            new W.NumberingProperties(new W.NumberingLevelReference { Val = Math.Clamp(li.Depth, 0, 8) }, new W.NumberingId { Val = numId })));
        AppendRuns(p, li.Content, main);
        return p;
    }

    private static void AppendRuns(OpenXmlCompositeElement parent, IEnumerable<Inline> content, MainDocumentPart main)
    {
        foreach (var inline in content)
        {
            var run = new W.Run();
            var props = new W.RunProperties();
            if (inline.Bold) props.Append(new W.Bold());
            if (inline.Italic) props.Append(new W.Italic());
            if (inline.Code) props.Append(new W.RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" });
            if (inline.Link is not null)
            {
                props.Append(new W.Color { Val = "0563C1" });
                props.Append(new W.Underline { Val = W.UnderlineValues.Single });
            }
            if (props.HasChildren) run.Append(props);

            var lines = inline.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) run.Append(new W.Break());
                run.Append(new W.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }

            if (inline.Link is not null && Uri.TryCreate(inline.Link, UriKind.Absolute, out var uri))
            {
                var rel = main.AddHyperlinkRelationship(uri, true);
                parent.Append(new W.Hyperlink(run) { Id = rel.Id });
            }
            else parent.Append(run);
        }
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new W.Styles(
            new W.DocDefaults(
                new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                    new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                    new W.FontSize { Val = "22" })),
                new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                    new W.SpacingBetweenLines { After = "160", Line = "259", LineRule = W.LineSpacingRuleValues.Auto }))),
            new W.Style(new W.StyleName { Val = "Normal" }, new W.PrimaryStyle()) { Type = W.StyleValues.Paragraph, StyleId = "Normal", Default = true });

        int[] sizes = [40, 32, 28, 24, 22, 22];
        for (var level = 1; level <= 6; level++)
        {
            styles.Append(new W.Style(
                new W.StyleName { Val = $"heading {level}" },
                new W.BasedOn { Val = "Normal" },
                new W.NextParagraphStyle { Val = "Normal" },
                new W.PrimaryStyle(),
                new W.StyleParagraphProperties(
                    new W.KeepNext(),
                    new W.SpacingBetweenLines { Before = level == 1 ? "360" : "240", After = "120" },
                    new W.OutlineLevel { Val = level - 1 }),
                new W.StyleRunProperties(new W.Bold(), new W.FontSize { Val = sizes[level - 1].ToString() }, new W.Color { Val = "1F1F1F" }))
            { Type = W.StyleValues.Paragraph, StyleId = $"Heading{level}" });
        }

        styles.Append(new W.Style(
            new W.StyleName { Val = "List Paragraph" }, new W.BasedOn { Val = "Normal" },
            new W.StyleParagraphProperties(new W.SpacingBetweenLines { After = "60" }, new W.ContextualSpacing()))
        { Type = W.StyleValues.Paragraph, StyleId = "ListParagraph" });

        styles.Append(new W.Style(
            new W.StyleName { Val = "Quote" }, new W.BasedOn { Val = "Normal" },
            new W.StyleParagraphProperties(new W.Indentation { Left = "720" }),
            new W.StyleRunProperties(new W.Italic(), new W.Color { Val = "595959" }))
        { Type = W.StyleValues.Paragraph, StyleId = "Quote" });

        styles.Append(new W.Style(
            new W.StyleName { Val = "Code" }, new W.BasedOn { Val = "Normal" },
            new W.StyleParagraphProperties(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = "F2F2F2" }, new W.SpacingBetweenLines { After = "120", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.StyleRunProperties(new W.RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" }, new W.FontSize { Val = "20" }))
        { Type = W.StyleValues.Paragraph, StyleId = "Code" });

        stylesPart.Styles = styles;
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new W.Numbering();
        numbering.Append(AbstractList(0, ordered: false));
        numbering.Append(AbstractList(1, ordered: true));
        numbering.Append(new W.NumberingInstance(new W.AbstractNumId { Val = 0 }) { NumberID = 1 });
        part.Numbering = numbering;
    }

    /// <summary>Each ordered list gets its own numbering instance so numbering restarts at 1.</summary>
    private static int RestartOrdered(MainDocumentPart main, int lastInstance)
    {
        var numbering = main.NumberingDefinitionsPart!.Numbering!;
        var id = lastInstance + 1;
        numbering.Append(new W.NumberingInstance(
            new W.AbstractNumId { Val = 1 },
            new W.LevelOverride(new W.StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 }) { NumberID = id });
        return id;
    }

    private static W.AbstractNum AbstractList(int id, bool ordered)
    {
        var abstractNum = new W.AbstractNum(new W.MultiLevelType { Val = W.MultiLevelValues.HybridMultilevel }) { AbstractNumberId = id };
        string[] bullets = ["•", "◦", "▪"];
        W.NumberFormatValues[] formats = [W.NumberFormatValues.Decimal, W.NumberFormatValues.LowerLetter, W.NumberFormatValues.LowerRoman];
        for (var level = 0; level < 9; level++)
        {
            abstractNum.Append(new W.Level(
                new W.StartNumberingValue { Val = 1 },
                new W.NumberingFormat { Val = ordered ? formats[level % 3] : W.NumberFormatValues.Bullet },
                new W.LevelText { Val = ordered ? $"%{level + 1}." : bullets[level % 3] },
                new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                new W.PreviousParagraphProperties(new W.Indentation { Left = (720 * (level + 1)).ToString(), Hanging = "360" }))
            { LevelIndex = level });
        }
        return abstractNum;
    }

    #endregion

    #region EPUB

    public static void WriteEpub(DocModel doc, string path)
    {
        var chapters = SplitChapters(doc);
        var id = "urn:uuid:" + Guid.NewGuid();

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimetype.Open(), new UTF8Encoding(false))) w.Write("application/epub+zip");

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var manifest = new XElement(opf + "manifest",
            new XElement(opf + "item", new XAttribute("id", "nav"), new XAttribute("href", "nav.xhtml"),
                new XAttribute("media-type", "application/xhtml+xml"), new XAttribute("properties", "nav")));
        var spine = new XElement(opf + "spine");
        var navList = new XElement(Xhtml + "ol");

        for (var i = 0; i < chapters.Count; i++)
        {
            var file = $"chapter-{i + 1}.xhtml";
            var xhtml = BuildXhtml(chapters[i].Title, chapters[i].Blocks);
            WriteEntry(zip, "OEBPS/" + file, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" + xhtml);
            manifest.Add(new XElement(opf + "item", new XAttribute("id", $"c{i + 1}"), new XAttribute("href", file), new XAttribute("media-type", "application/xhtml+xml")));
            spine.Add(new XElement(opf + "itemref", new XAttribute("idref", $"c{i + 1}")));
            navList.Add(new XElement(Xhtml + "li", new XElement(Xhtml + "a", new XAttribute("href", file), chapters[i].Title)));
        }

        var package = new XElement(opf + "package",
            new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "bookid"),
            new XElement(opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", dc),
                new XElement(dc + "identifier", new XAttribute("id", "bookid"), id),
                new XElement(dc + "title", doc.Title),
                new XElement(dc + "language", "en"),
                new XElement(opf + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))),
            manifest, spine);
        WriteEntry(zip, "OEBPS/content.opf", "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + package);

        XNamespace epub = "http://www.idpf.org/2007/ops";
        var nav = new XElement(Xhtml + "html", new XAttribute(XNamespace.Xmlns + "epub", epub),
            new XElement(Xhtml + "head", new XElement(Xhtml + "title", doc.Title)),
            new XElement(Xhtml + "body",
                new XElement(Xhtml + "nav", new XAttribute(epub + "type", "toc"), new XAttribute("id", "toc"),
                    new XElement(Xhtml + "h1", "Contents"), navList)));
        WriteEntry(zip, "OEBPS/nav.xhtml", "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" + nav);
    }

    private static List<(string Title, List<Block> Blocks)> SplitChapters(DocModel doc)
    {
        var chapters = new List<(string Title, List<Block> Blocks)>();
        var minLevel = doc.Blocks.OfType<Heading>().Select(h => h.Level).DefaultIfEmpty(1).Min();
        foreach (var block in doc.Blocks)
        {
            if (block is Heading h && h.Level == minLevel && (chapters.Count == 0 || chapters[^1].Blocks.Count > 0))
                chapters.Add((DocModel.PlainText(h.Content), []));
            if (chapters.Count == 0) chapters.Add((doc.Title, []));
            chapters[^1].Blocks.Add(block);
        }
        if (chapters.Count == 0) chapters.Add((doc.Title, []));
        return chapters;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    #endregion
}
