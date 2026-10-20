using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using W = DocumentFormat.OpenXml.Wordprocessing;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Tangerine.Core.Documents;

public static class DocReaders
{
    public static DocModel Read(string path)
    {
        var doc = PathNames.Extension(path) switch
        {
            ".md" or ".markdown" => FromMarkdown(File.ReadAllText(path)),
            ".txt" => FromPlainText(File.ReadAllText(path)),
            ".html" or ".htm" => FromHtml(File.ReadAllText(path)),
            ".rtf" => FromPlainText(RtfText.ToPlainText(File.ReadAllText(path, Encoding.Latin1))),
            ".docx" => FromDocx(path),
            ".pdf" => FromPdf(path),
            var ext => throw new NotSupportedException($"Cannot read {ext} documents"),
        };
        if (doc.Title == "Untitled") doc.Title = PathNames.Stem(path);
        return doc;
    }

    public static DocModel FromPlainText(string text)
    {
        var doc = new DocModel();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var chunk in normalized.Split("\n\n"))
        {
            var para = chunk.Trim('\n');
            if (para.Trim().Length == 0) continue;
            doc.Blocks.Add(new Paragraph([new Inline(para)]));
        }
        return doc;
    }

    public static DocModel FromHtml(string html)
    {
        var config = new ReverseMarkdown.Config { GithubFlavored = true };
        config.Tags.Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass;
        config.Formatting.RemoveComments = true;
        config.Links.SmartHref = true;
        var converter = new ReverseMarkdown.Converter(config);
        var doc = FromMarkdown(converter.Convert(html));
        var title = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (title.Success && title.Groups[1].Value.Trim().Length > 0) doc.Title = System.Net.WebUtility.HtmlDecode(title.Groups[1].Value.Trim());
        return doc;
    }

    #region Markdown

    public static DocModel FromMarkdown(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseAutoLinks().Build();
        var parsed = Markdown.Parse(markdown, pipeline);
        var doc = new DocModel();
        foreach (var block in parsed) AddMarkdownBlock(doc, block, listDepth: -1, ordered: false);
        var firstHeading = doc.Blocks.OfType<Heading>().FirstOrDefault();
        if (firstHeading is not null) doc.Title = DocModel.PlainText(firstHeading.Content);
        return doc;
    }

    private static void AddMarkdownBlock(DocModel doc, Markdig.Syntax.Block block, int listDepth, bool ordered)
    {
        switch (block)
        {
            case HeadingBlock h:
                doc.Blocks.Add(new Heading(h.Level, MarkdownInlines(h.Inline)));
                break;
            case ParagraphBlock p:
                doc.Blocks.Add(new Paragraph(MarkdownInlines(p.Inline)));
                break;
            case Markdig.Syntax.CodeBlock code:
                doc.Blocks.Add(new CodeBlock(code.Lines.ToString().TrimEnd('\n', '\r')));
                break;
            case ThematicBreakBlock:
                doc.Blocks.Add(new Rule());
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                    if (child is ParagraphBlock qp) doc.Blocks.Add(new Quote(MarkdownInlines(qp.Inline)));
                    else AddMarkdownBlock(doc, child, listDepth, ordered);
                break;
            case ListBlock list:
                var itemDepth = listDepth + 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var first = true;
                    foreach (var child in item)
                    {
                        if (child is ParagraphBlock ip)
                            doc.Blocks.Add(first
                                ? new ListItem(list.IsOrdered, itemDepth, MarkdownInlines(ip.Inline))
                                : new Paragraph(MarkdownInlines(ip.Inline)));
                        else
                            AddMarkdownBlock(doc, child, child is ListBlock ? itemDepth : -1, list.IsOrdered);
                        first = false;
                    }
                }
                break;
            case Table table:
                foreach (var row in table.OfType<TableRow>())
                {
                    var cells = row.OfType<TableCell>()
                        .Select(c => string.Join(" ", c.OfType<ParagraphBlock>().Select(p => DocModel.PlainText(MarkdownInlines(p.Inline)))))
                        .ToList();
                    doc.Blocks.Add(new Paragraph([new Inline(string.Join(" | ", cells), Bold: row.IsHeader)]));
                }
                break;
            case ContainerBlock container:
                foreach (var child in container) AddMarkdownBlock(doc, child, listDepth, ordered);
                break;
        }
    }

    private static List<Inline> MarkdownInlines(ContainerInline? container)
    {
        var result = new List<Inline>();
        if (container is not null) CollectInlines(container, result, bold: false, italic: false, link: null);
        return Merge(result);
    }

    private static void CollectInlines(ContainerInline container, List<Inline> into, bool bold, bool italic, string? link)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    into.Add(new Inline(literal.Content.ToString(), bold, italic, Link: link));
                    break;
                case CodeInline code:
                    into.Add(new Inline(code.Content, bold, italic, Code: true, Link: link));
                    break;
                case LineBreakInline br:
                    into.Add(new Inline(br.IsHard ? "\n" : " ", bold, italic, Link: link));
                    break;
                case HtmlEntityInline entity:
                    into.Add(new Inline(entity.Transcoded.ToString(), bold, italic, Link: link));
                    break;
                case AutolinkInline auto:
                    into.Add(new Inline(auto.Url, bold, italic, Link: auto.IsEmail ? "mailto:" + auto.Url : auto.Url));
                    break;
                case EmphasisInline em:
                    var strong = em.DelimiterCount >= 2;
                    CollectInlines(em, into, bold || strong, italic || !strong, link);
                    break;
                case LinkInline { IsImage: true } image:
                    into.Add(new Inline($"[{FirstText(image) ?? "image"}]", bold, italic, Link: image.Url));
                    break;
                case LinkInline l:
                    CollectInlines(l, into, bold, italic, l.Url);
                    break;
                case ContainerInline other:
                    CollectInlines(other, into, bold, italic, link);
                    break;
            }
        }
    }

    private static string? FirstText(ContainerInline container) =>
        container.Descendants<LiteralInline>().Select(l => l.Content.ToString()).FirstOrDefault();

    internal static List<Inline> Merge(List<Inline> inlines)
    {
        var merged = new List<Inline>();
        foreach (var inline in inlines)
        {
            if (inline.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1] with { Text = "" } == inline with { Text = "" })
                merged[^1] = merged[^1] with { Text = merged[^1].Text + inline.Text };
            else
                merged.Add(inline);
        }
        return merged;
    }

    #endregion

    #region DOCX

    public static DocModel FromDocx(string path)
    {
        using var word = WordprocessingDocument.Open(path, false);
        var main = word.MainDocumentPart ?? throw new InvalidDataException("The document has no body.");
        var body = main.Document?.Body ?? throw new InvalidDataException("The document has no body.");
        var doc = new DocModel();
        var title = word.PackageProperties.Title;
        if (!string.IsNullOrWhiteSpace(title)) doc.Title = title;

        var styleNames = main.StyleDefinitionsPart?.Styles?.Elements<W.Style>()
            .Where(s => s.StyleId?.Value is not null)
            .ToDictionary(s => s.StyleId!.Value!, s => s.StyleName?.Val?.Value ?? s.StyleId!.Value!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in body.ChildElements)
        {
            if (element is W.Paragraph p) AddDocxParagraph(doc, main, p, styleNames);
            else if (element is W.Table table)
            {
                foreach (var row in table.Elements<W.TableRow>())
                {
                    var cells = row.Elements<W.TableCell>().Select(c => string.Join(" ", c.Elements<W.Paragraph>().Select(cp => cp.InnerText)));
                    doc.Blocks.Add(new Paragraph([new Inline(string.Join(" | ", cells))]));
                }
            }
        }
        return doc;
    }

    private static void AddDocxParagraph(DocModel doc, MainDocumentPart main, W.Paragraph p, IReadOnlyDictionary<string, string> styleNames)
    {
        var inlines = new List<Inline>();
        foreach (var child in p.ChildElements)
        {
            if (child is W.Run run) AddRun(inlines, run, null);
            else if (child is W.Hyperlink hyperlink)
            {
                string? url = null;
                if (hyperlink.Id?.Value is { } relId)
                    url = main.HyperlinkRelationships.FirstOrDefault(r => r.Id == relId)?.Uri.ToString();
                foreach (var run2 in hyperlink.Elements<W.Run>()) AddRun(inlines, run2, url);
            }
        }
        var content = Merge(inlines);

        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var styleName = styleNames.TryGetValue(styleId, out var n) ? n : styleId;
        var numbering = p.ParagraphProperties?.NumberingProperties;

        if (DocModel.PlainText(content).Trim().Length == 0) return;

        var headingLevel = HeadingLevel(styleName) ?? HeadingLevel(styleId);
        if (headingLevel is { } level)
        {
            doc.Blocks.Add(new Heading(level, content));
            if (level == 1 && doc.Title == "Untitled") doc.Title = DocModel.PlainText(content);
        }
        else if (numbering is not null || styleName.Contains("List", StringComparison.OrdinalIgnoreCase))
        {
            var depth = numbering?.NumberingLevelReference?.Val?.Value ?? 0;
            var numId = numbering?.NumberingId?.Val?.Value;
            doc.Blocks.Add(new ListItem(IsOrderedNumbering(main, numId, depth), depth, content));
        }
        else if (styleName.Contains("Quote", StringComparison.OrdinalIgnoreCase))
            doc.Blocks.Add(new Quote(content));
        else if (styleName.Contains("Code", StringComparison.OrdinalIgnoreCase))
            doc.Blocks.Add(new CodeBlock(DocModel.PlainText(content)));
        else
            doc.Blocks.Add(new Paragraph(content));
    }

    private static int? HeadingLevel(string style)
    {
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;
        var s = style.Replace(" ", "");
        if (s.StartsWith("heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.AsSpan(7), out var level))
            return Math.Clamp(level, 1, 6);
        return null;
    }

    private static bool IsOrderedNumbering(MainDocumentPart main, int? numId, int level)
    {
        var numbering = main.NumberingDefinitionsPart?.Numbering;
        if (numbering is null || numId is null) return false;
        var instance = numbering.Elements<W.NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        var abstractId = instance?.AbstractNumId?.Val?.Value;
        var abstractNum = numbering.Elements<W.AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
        var lvl = abstractNum?.Elements<W.Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        var format = lvl?.NumberingFormat?.Val?.Value;
        return format is not null && format != W.NumberFormatValues.Bullet && format != W.NumberFormatValues.None;
    }

    private static void AddRun(List<Inline> into, W.Run run, string? link)
    {
        var props = run.RunProperties;
        var bold = props?.Bold is { } b && (b.Val is null || b.Val.Value);
        var italic = props?.Italic is { } i && (i.Val is null || i.Val.Value);
        var code = props?.RunFonts?.Ascii?.Value is { } font && (font.Contains("Consolas") || font.Contains("Courier") || font.Contains("Mono"));
        foreach (var child in run.ChildElements)
        {
            var text = child switch
            {
                W.Text t => t.Text,
                W.TabChar => "\t",
                W.Break or W.CarriageReturn => "\n",
                _ => null,
            };
            if (text is not null) into.Add(new Inline(text, bold, italic, code, link));
        }
    }

    #endregion

    #region PDF

    public static DocModel FromPdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var doc = new DocModel();
        if (!string.IsNullOrWhiteSpace(pdf.Information.Title)) doc.Title = pdf.Information.Title!;

        var blocks = new List<(string Text, double Size)>();
        var letterSizes = new List<double>();
        foreach (var page in pdf.GetPages())
        {
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
            if (words.Count == 0) continue;
            var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
            var ordered = UnsupervisedReadingOrderDetector.Instance.Get(textBlocks);
            foreach (var block in ordered)
            {
                var text = string.Join(" ", block.TextLines.Select(l => l.Text.Trim())).Trim();
                text = DehyphenateRegex.Replace(text, "$1$2");
                if (text.Length == 0) continue;
                var letters = block.TextLines.SelectMany(l => l.Words).SelectMany(w => w.Letters).ToList();
                var size = letters.Count == 0 ? 0 : letters.Average(l => l.PointSize);
                letterSizes.AddRange(letters.Select(l => l.PointSize));
                blocks.Add((text, size));
            }
        }

        letterSizes.Sort();
        var median = letterSizes.Count == 0 ? 0 : letterSizes[letterSizes.Count / 2];
        foreach (var (text, size) in blocks)
        {
            var isShort = text.Length < 120;
            if (median > 0 && isShort && size >= median * 1.6) doc.Blocks.Add(new Heading(1, [new Inline(text)]));
            else if (median > 0 && isShort && size >= median * 1.25) doc.Blocks.Add(new Heading(2, [new Inline(text)]));
            else if (BulletRegex.Match(text) is { Success: true } m) doc.Blocks.Add(new ListItem(false, 0, [new Inline(text[m.Length..])]));
            else doc.Blocks.Add(new Paragraph([new Inline(text)]));
        }
        return doc;
    }

    private static readonly System.Text.RegularExpressions.Regex DehyphenateRegex = new(@"(\p{L})- (\p{Ll})");
    private static readonly System.Text.RegularExpressions.Regex BulletRegex = new(@"^[•▪◦‣●○■\-–]\s+");

    #endregion
}
