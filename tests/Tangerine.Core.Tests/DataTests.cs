using System.Formats.Tar;
using System.IO.Compression;
using Tangerine.Core.Sheets;
using Tangerine.Core.Subtitles;

namespace Tangerine.Core.Tests;

public class DataTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private const string Csv = "name,amount,note\r\nAlice,12.5,\"Hello, world\"\r\nBob,007,\"say \"\"hi\"\"\"\r\n";

    [Fact]
    public void Csv_parser_handles_quotes_and_newlines()
    {
        var rows = Workbook.ParseDelimited("a,\"b\nc\",\"d\"\"e\"\n1,2,3", ',');
        Assert.Equal(["a", "b\nc", "d\"e"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public async Task Csv_round_trips_through_xlsx()
    {
        var csv = _dir.Write("data.csv", Csv);
        var xlsx = await Run.Single("sheet.convert.xlsx", csv);

        using (var book = new ClosedXML.Excel.XLWorkbook(xlsx))
        {
            var ws = book.Worksheet(1);
            Assert.Equal(12.5, ws.Cell(2, 2).GetDouble());
            Assert.Equal("007", ws.Cell(3, 2).GetString());
        }

        File.Delete(csv);
        var back = await Run.Single("sheet.convert.csv", xlsx);
        var rows = Workbook.Read(back)[0].Rows;
        Assert.Equal(["Alice", "12.5", "Hello, world"], rows[1]);
        Assert.Equal(["Bob", "007", "say \"hi\""], rows[2]);
    }

    [Fact]
    public async Task Csv_round_trips_through_ods_and_tsv()
    {
        var csv = _dir.Write("data.csv", Csv);
        var ods = await Run.Single("sheet.convert.ods", csv);
        var tsv = await Run.Single("sheet.convert.tsv", ods);
        var rows = Workbook.Read(tsv)[0].Rows;
        Assert.Equal(3, rows.Count);
        Assert.Equal(["Alice", "12.5", "Hello, world"], rows[1]);
        Assert.Contains("\t", File.ReadAllText(tsv));
    }

    [Fact]
    public async Task Multi_sheet_workbook_exports_one_csv_per_sheet()
    {
        var xlsx = _dir.File("book.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            book.AddWorksheet("Jan").Cell(1, 1).Value = "a";
            book.AddWorksheet("Feb").Cell(1, 1).Value = "b";
            book.SaveAs(xlsx);
        }
        var outputs = await Run.Op("sheet.convert.csv", [xlsx]);
        Assert.Equal(2, outputs.Count);
        Assert.EndsWith("book - Jan.csv", outputs[0]);
    }

    [Fact]
    public async Task Srt_round_trips_through_vtt()
    {
        var srt = _dir.Write("movie.srt", "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello <i>there</i>\r\n\r\n2\r\n00:01:00,000 --> 01:00:00,001\r\nTwo\r\nlines\r\n");
        var vtt = await Run.Single("subtitle.convert.vtt", srt);
        var vttText = File.ReadAllText(vtt);
        Assert.StartsWith("WEBVTT", vttText);
        Assert.Contains("00:00:01.000 --> 00:00:02.500", vttText);

        File.Delete(srt);
        var back = SubtitleFile.Parse(File.ReadAllText(await Run.Single("subtitle.convert.srt", vtt)));
        Assert.Equal(2, back.Count);
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(1), back[1].End);
        Assert.Equal("Two\nlines", back[1].Text);
        Assert.Equal("Hello <i>there</i>", back[0].Text);
    }

    [Fact]
    public void Vtt_with_short_timestamps_and_settings_parses()
    {
        var cues = SubtitleFile.Parse("WEBVTT\n\nNOTE comment\n\nintro\n01:02.500 --> 01:04.000 align:start\n<v Bob>Hi</v>\n");
        var cue = Assert.Single(cues);
        Assert.Equal(TimeSpan.FromSeconds(62.5), cue.Start);
        Assert.Contains("Hi", SubtitleFile.ToSrt(cues));
        Assert.DoesNotContain("<v", SubtitleFile.ToSrt(cues));
    }

    [Fact]
    public async Task Folder_zips_then_converts_to_targz_and_extracts()
    {
        var folder = Path.Combine(_dir.Path, "project");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "a.txt"), "A");
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "B");

        var zip = await Run.Single("any.zip", folder);
        Assert.EndsWith("project.zip", zip);
        using (var archive = ZipFile.OpenRead(zip))
            Assert.Contains(archive.Entries, e => e.FullName == "project/sub/b.txt");

        var tgz = await Run.Single("archive.convert.tar.gz", zip);
        Assert.EndsWith("project.tar.gz", tgz);

        var extracted = await Run.Single("archive.extract", tgz);
        Assert.Equal("B", File.ReadAllText(Path.Combine(extracted, "project", "sub", "b.txt")));
    }

    [Fact]
    public async Task Multiple_files_zip_into_archive()
    {
        var a = _dir.Write("a.txt", "A");
        var b = _dir.Write("b.txt", "B");
        var zip = Assert.Single(await Run.Op("any.zip", [a, b]));
        Assert.EndsWith("Archive.zip", zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(2, archive.Entries.Count);
    }

    [Fact]
    public async Task Gzip_and_extract_restore_contents()
    {
        var input = _dir.Write("log.txt", new string('x', 10_000));
        var gz = await Run.Single("any.gzip", input);
        Assert.EndsWith("log.txt.gz", gz);
        Assert.True(new FileInfo(gz).Length < 1000);
        var restored = await Run.Single("archive.extract", gz);
        Assert.EndsWith("log (1).txt", restored);
        Assert.Equal(10_000, File.ReadAllText(restored).Length);
    }

    [Fact]
    public async Task Tar_converts_to_zip()
    {
        var src = Path.Combine(_dir.Path, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "x.txt"), "X");
        var tar = _dir.File("bundle.tar");
        TarFile.CreateFromDirectory(src, tar, includeBaseDirectory: false);

        var zip = await Run.Single("archive.convert.zip", tar);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "x.txt");
    }
}
