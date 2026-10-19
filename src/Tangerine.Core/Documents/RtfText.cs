using System.Text;

namespace Tangerine.Core.Documents;

/// <summary>Extracts plain text (with paragraph breaks) from RTF.</summary>
public static class RtfText
{
    private static readonly HashSet<string> SkippedDestinations =
    [
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "header", "footer", "headerl", "headerr", "footerl",
        "footerr", "object", "listtable", "listoverridetable", "rsidtbl", "generator", "xmlnstbl", "themedata",
        "colorschememapping", "latentstyles", "datastore", "fldinst", "filetbl", "revtbl", "pgdsctbl",
    ];

    static RtfText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string ToPlainText(string rtf)
    {
        var output = new StringBuilder();
        var stack = new Stack<(bool Skip, int UnicodeSkip)>();
        var skip = false;
        var unicodeSkip = 1;
        var pendingSkip = 0;
        var ansi = Encoding.GetEncoding(1252);
        var bytes = new List<byte>();

        void FlushBytes()
        {
            if (bytes.Count == 0) return;
            if (!skip) output.Append(ansi.GetString(bytes.ToArray()));
            bytes.Clear();
        }

        var i = 0;
        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '{')
            {
                FlushBytes();
                stack.Push((skip, unicodeSkip));
                i++;
            }
            else if (c == '}')
            {
                FlushBytes();
                if (stack.Count > 0) (skip, unicodeSkip) = stack.Pop();
                i++;
            }
            else if (c == '\\')
            {
                i++;
                if (i >= rtf.Length) break;
                var next = rtf[i];
                if (next == '\'' && i + 2 < rtf.Length)
                {
                    var hex = rtf.Substring(i + 1, 2);
                    i += 3;
                    if (pendingSkip > 0) { pendingSkip--; continue; }
                    if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var b)) bytes.Add(b);
                    continue;
                }

                FlushBytes();
                if (!char.IsAsciiLetter(next))
                {
                    i++;
                    if (next == '*') skip = true;
                    else if (!skip && next is '\\' or '{' or '}') output.Append(next);
                    else if (!skip && next == '~') output.Append('\u00A0');
                    else if (!skip && next is '\n' or '\r') output.Append('\n');
                    continue;
                }

                var start = i;
                while (i < rtf.Length && char.IsAsciiLetter(rtf[i])) i++;
                var word = rtf[start..i];
                int? param = null;
                var paramStart = i;
                if (i < rtf.Length && (rtf[i] == '-' || char.IsAsciiDigit(rtf[i])))
                {
                    i++;
                    while (i < rtf.Length && char.IsAsciiDigit(rtf[i])) i++;
                    if (int.TryParse(rtf.AsSpan(paramStart, i - paramStart), out var p)) param = p;
                }
                if (i < rtf.Length && rtf[i] == ' ') i++;

                if (SkippedDestinations.Contains(word)) { skip = true; continue; }
                if (skip) continue;

                switch (word)
                {
                    case "par" or "sect" or "page":
                        output.Append("\n\n");
                        break;
                    case "line":
                        output.Append('\n');
                        break;
                    case "tab":
                        output.Append('\t');
                        break;
                    case "cell":
                        output.Append(" | ");
                        break;
                    case "row":
                        output.Append("\n\n");
                        break;
                    case "emdash": output.Append('—'); break;
                    case "endash": output.Append('–'); break;
                    case "bullet": output.Append('•'); break;
                    case "lquote": output.Append('‘'); break;
                    case "rquote": output.Append('’'); break;
                    case "ldblquote": output.Append('“'); break;
                    case "rdblquote": output.Append('”'); break;
                    case "uc" when param is not null:
                        unicodeSkip = param.Value;
                        break;
                    case "u" when param is not null:
                        var code = param.Value < 0 ? param.Value + 65536 : param.Value;
                        output.Append((char)code);
                        pendingSkip = unicodeSkip;
                        break;
                    case "ansicpg" when param is not null:
                        try { ansi = Encoding.GetEncoding(param.Value); } catch (ArgumentException) { } catch (NotSupportedException) { }
                        break;
                }
            }
            else if (c is '\r' or '\n')
            {
                i++;
            }
            else
            {
                i++;
                if (pendingSkip > 0) { pendingSkip--; continue; }
                if (!skip)
                {
                    FlushBytes();
                    output.Append(c);
                }
            }
        }
        FlushBytes();

        var text = output.ToString();
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text.Trim();
    }
}
