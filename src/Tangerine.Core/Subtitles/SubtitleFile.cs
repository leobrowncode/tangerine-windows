using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tangerine.Core.Subtitles;

public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

public static partial class SubtitleFile
{
    public static List<Cue> Parse(string content)
    {
        var cues = new List<Cue>();
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        foreach (var block in BlankLines().Split(normalized))
        {
            var lines = block.Split('\n').ToList();
            var timingIndex = lines.FindIndex(l => l.Contains("-->"));
            if (timingIndex < 0) continue;
            var match = Timing().Match(lines[timingIndex]);
            if (!match.Success) continue;
            var text = string.Join("\n", lines.Skip(timingIndex + 1)).Trim('\n');
            cues.Add(new Cue(ParseTimestamp(match.Groups[1].Value), ParseTimestamp(match.Groups[2].Value), text));
        }
        return cues;
    }

    public static string ToSrt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in cues)
        {
            sb.Append(index++).Append('\n')
              .Append(Format(cue.Start, ',')).Append(" --> ").Append(Format(cue.End, ',')).Append('\n')
              .Append(StripVttMarkup(cue.Text)).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            sb.Append(Format(cue.Start, '.')).Append(" --> ").Append(Format(cue.End, '.')).Append('\n')
              .Append(SrtFontTags().Replace(cue.Text, "")).Append("\n\n");
        }
        return sb.ToString();
    }

    private static TimeSpan ParseTimestamp(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        var seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        var minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        var hours = parts.Length > 2 ? int.Parse(parts[^3], CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
    }

    private static string Format(TimeSpan t, char separator) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}";

    private static string StripVttMarkup(string text) => VttTags().Replace(text, "");

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"((?:\d+:)?\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[.,]\d{1,3})")]
    private static partial Regex Timing();

    [GeneratedRegex(@"</?(?:c|v|lang|ruby|rt)(?:[.\s][^>]*)?>|<\d{2}:[\d:.]+>")]
    private static partial Regex VttTags();

    [GeneratedRegex(@"</?font[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex SrtFontTags();
}
