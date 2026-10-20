using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace Tangerine.Core.Sheets;

public sealed class Sheet(string name)
{
    public string Name { get; set; } = name;
    public List<List<string>> Rows { get; } = [];
}

public static class Workbook
{
    public static List<Sheet> Read(string path) => PathNames.Extension(path) switch
    {
        ".csv" => [ReadDelimited(path, DetectCsvDelimiter(path))],
        ".tsv" => [ReadDelimited(path, '\t')],
        ".xlsx" => ReadXlsx(path),
        ".ods" => ReadOds(path),
        var ext => throw new NotSupportedException($"Cannot read {ext} spreadsheets"),
    };

    public static void Write(IReadOnlyList<Sheet> sheets, string path)
    {
        switch (PathNames.Extension(path))
        {
            case ".csv": WriteDelimited(sheets[0], path, ','); break;
            case ".tsv": WriteDelimited(sheets[0], path, '\t'); break;
            case ".xlsx": WriteXlsx(sheets, path); break;
            case ".ods": WriteOds(sheets, path); break;
            default: throw new NotSupportedException($"Cannot write {PathNames.Extension(path)} spreadsheets");
        }
    }

    public static bool IsSingleSheetFormat(string extension) => extension is ".csv" or ".tsv";

    #region Delimited

    private static char DetectCsvDelimiter(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var firstLine = reader.ReadLine() ?? "";
        return firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
    }

    public static Sheet ReadDelimited(string path, char delimiter)
    {
        var text = File.ReadAllText(path);
        var sheet = new Sheet(PathNames.Stem(path));
        sheet.Rows.AddRange(ParseDelimited(text, delimiter));
        return sheet;
    }

    public static List<List<string>> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"' && field.Length == 0) { inQuotes = true; fieldStarted = true; }
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); fieldStarted = true; }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
                fieldStarted = false;
            }
            else { field.Append(c); fieldStarted = true; }
        }
        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    public static void WriteDelimited(Sheet sheet, string path, char delimiter)
    {
        var sb = new StringBuilder();
        foreach (var row in sheet.Rows)
        {
            sb.AppendJoin(delimiter, row.Select(cell => Quote(cell, delimiter)));
            sb.Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: delimiter == ','));
    }

    private static string Quote(string cell, char delimiter) =>
        cell.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 || cell.StartsWith(' ') || cell.EndsWith(' ')
            ? "\"" + cell.Replace("\"", "\"\"") + "\""
            : cell;

    #endregion

    #region XLSX

    private static List<Sheet> ReadXlsx(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var book = new XLWorkbook(stream);
        var sheets = new List<Sheet>();
        foreach (var ws in book.Worksheets)
        {
            var sheet = new Sheet(ws.Name);
            var used = ws.RangeUsed();
            if (used is not null)
            {
                var lastCol = used.LastColumn().ColumnNumber();
                for (var r = 1; r <= used.LastRow().RowNumber(); r++)
                {
                    var row = new List<string>();
                    for (var c = 1; c <= lastCol; c++) row.Add(ws.Cell(r, c).GetFormattedString());
                    sheet.Rows.Add(row);
                }
            }
            sheets.Add(sheet);
        }
        return sheets;
    }

    private static void WriteXlsx(IReadOnlyList<Sheet> sheets, string path)
    {
        using var book = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var ws = book.Worksheets.Add(SafeSheetName(sheet.Name, used));
            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                for (var c = 0; c < sheet.Rows[r].Count; c++)
                {
                    var value = sheet.Rows[r][c];
                    var cell = ws.Cell(r + 1, c + 1);
                    if (TryNumber(value, out var number)) cell.Value = number;
                    else cell.Value = value;
                }
            }
            if (sheet.Rows.Count > 0) ws.Columns(1, Math.Max(1, sheet.Rows.Max(r => r.Count))).AdjustToContents(1, Math.Min(sheet.Rows.Count, 200));
        }
        if (sheets.Count == 0) book.Worksheets.Add("Sheet1");
        book.SaveAs(path);
    }

    private static string SafeSheetName(string name, HashSet<string> used)
    {
        var cleaned = new string(name.Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).ToArray()).Trim('\'');
        if (cleaned.Length == 0) cleaned = "Sheet";
        if (cleaned.Length > 31) cleaned = cleaned[..31];
        var candidate = cleaned;
        for (var i = 2; !used.Add(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = (cleaned.Length + suffix.Length > 31 ? cleaned[..(31 - suffix.Length)] : cleaned) + suffix;
        }
        return candidate;
    }

    internal static bool TryNumber(string value, out double number)
    {
        number = 0;
        if (value.Length == 0 || value.Length > 15 || value != value.Trim()) return false;
        if (value.Length > 1 && value[0] == '0' && char.IsAsciiDigit(value[1])) return false;
        return double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number);
    }

    #endregion

    #region ODS

    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    private static List<Sheet> ReadOds(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException("Not an OpenDocument spreadsheet.");
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        var sheets = new List<Sheet>();

        foreach (var table in xml.Descendants(Table + "table"))
        {
            var sheet = new Sheet((string?)table.Attribute(Table + "name") ?? $"Sheet{sheets.Count + 1}");
            foreach (var rowEl in table.Descendants(Table + "table-row"))
            {
                var rowRepeat = Math.Min((int?)rowEl.Attribute(Table + "number-rows-repeated") ?? 1, 10000);
                var row = new List<string>();
                foreach (var cellEl in rowEl.Elements().Where(e => e.Name == Table + "table-cell" || e.Name == Table + "covered-table-cell"))
                {
                    var repeat = Math.Min((int?)cellEl.Attribute(Table + "number-columns-repeated") ?? 1, 1024);
                    var value = string.Join("\n", cellEl.Elements(Text + "p").Select(p => p.Value));
                    for (var i = 0; i < repeat; i++) row.Add(value);
                }
                while (row.Count > 0 && row[^1].Length == 0) row.RemoveAt(row.Count - 1);
                for (var i = 0; i < rowRepeat; i++) sheet.Rows.Add([.. row]);
            }
            while (sheet.Rows.Count > 0 && sheet.Rows[^1].Count == 0) sheet.Rows.RemoveAt(sheet.Rows.Count - 1);
            sheets.Add(sheet);
        }
        return sheets;
    }

    private static void WriteOds(IReadOnlyList<Sheet> sheets, string path)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimetype.Open(), new UTF8Encoding(false))) w.Write("application/vnd.oasis.opendocument.spreadsheet");

        XNamespace manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
        var manifestXml = new XElement(manifest + "manifest", new XAttribute(XNamespace.Xmlns + "manifest", manifest), new XAttribute(manifest + "version", "1.2"),
            new XElement(manifest + "file-entry", new XAttribute(manifest + "full-path", "/"), new XAttribute(manifest + "media-type", "application/vnd.oasis.opendocument.spreadsheet")),
            new XElement(manifest + "file-entry", new XAttribute(manifest + "full-path", "content.xml"), new XAttribute(manifest + "media-type", "text/xml")));
        WriteXml(zip, "META-INF/manifest.xml", manifestXml);

        var spreadsheet = new XElement(Office + "spreadsheet");
        foreach (var sheet in sheets)
        {
            var table = new XElement(Table + "table", new XAttribute(Table + "name", sheet.Name));
            foreach (var row in sheet.Rows)
            {
                var rowEl = new XElement(Table + "table-row");
                foreach (var value in row)
                {
                    var cell = new XElement(Table + "table-cell");
                    if (TryNumber(value, out var n))
                    {
                        cell.Add(new XAttribute(Office + "value-type", "float"), new XAttribute(Office + "value", n.ToString("R", CultureInfo.InvariantCulture)));
                    }
                    else if (value.Length > 0)
                    {
                        cell.Add(new XAttribute(Office + "value-type", "string"));
                    }
                    foreach (var line in value.Length == 0 ? [] : value.Split('\n')) cell.Add(new XElement(Text + "p", line));
                    rowEl.Add(cell);
                }
                table.Add(rowEl);
            }
            spreadsheet.Add(table);
        }

        var content = new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office),
            new XAttribute(XNamespace.Xmlns + "table", Table),
            new XAttribute(XNamespace.Xmlns + "text", Text),
            new XAttribute(Office + "version", "1.2"),
            new XElement(Office + "body", spreadsheet));
        WriteXml(zip, "content.xml", content);
    }

    private static void WriteXml(ZipArchive zip, string name, XElement root)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(writer);
    }

    #endregion
}
