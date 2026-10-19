using Tangerine.Core.Operations;

namespace Tangerine.Core.Tests;

public class CatalogTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private IReadOnlyList<string> IdsFor(params string[] names)
    {
        var paths = names.Select(n => _dir.Write(n, "x")).ToList();
        return Run.Catalog.For(paths).Select(o => o.Id).ToList();
    }

    [Fact]
    public void Png_offers_other_image_formats_but_not_itself()
    {
        var ids = IdsFor("photo.png");
        Assert.Contains("image.convert.jpg", ids);
        Assert.Contains("image.convert.webp", ids);
        Assert.Contains("image.convert.pdf", ids);
        Assert.DoesNotContain("image.convert.png", ids);
        Assert.Contains("image.compress", ids);
        Assert.Contains("any.zip", ids);
        Assert.DoesNotContain("image.combine-pdf", ids);
    }

    [Fact]
    public void Jpeg_extension_is_treated_as_jpg()
    {
        var ids = IdsFor("photo.jpeg");
        Assert.DoesNotContain("image.convert.jpg", ids);
        Assert.Contains("image.convert.png", ids);
    }

    [Fact]
    public void Multiple_images_offer_combine()
    {
        var ids = IdsFor("a.png", "b.jpg");
        Assert.Contains("image.combine-pdf", ids);
        Assert.Contains("image.convert.webp", ids);
    }

    [Fact]
    public void Mixed_kinds_only_offer_generic_tools()
    {
        var ids = IdsFor("a.png", "b.mp3");
        Assert.All(ids, id => Assert.StartsWith("any.", id));
        Assert.Contains("any.zip", ids);
    }

    [Fact]
    public void Video_offers_audio_extraction_and_gif()
    {
        var ids = IdsFor("clip.mov");
        Assert.Contains("media.convert.mp3.Video", ids);
        Assert.Contains("media.convert.gif.Video", ids);
        Assert.DoesNotContain("media.convert.mov.Video", ids);
        Assert.Contains("media.trim", ids);
        Assert.Contains("video.mute", ids);
    }

    [Fact]
    public void Pdf_offers_documents_and_images()
    {
        var ids = IdsFor("paper.pdf");
        Assert.Contains("document.convert.docx", ids);
        Assert.Contains("document.convert.md", ids);
        Assert.Contains("pdf.render.png", ids);
        Assert.Contains("pdf.split", ids);
        Assert.DoesNotContain("pdf.merge", ids);
        Assert.Contains("pdf.merge", IdsFor("a.pdf", "b.pdf"));
    }

    [Fact]
    public void Compound_archive_extensions_are_recognised()
    {
        var ids = IdsFor("backup.tar.gz");
        Assert.Contains("archive.convert.zip", ids);
        Assert.Contains("archive.extract", ids);
        Assert.DoesNotContain("archive.convert.tar.gz", ids);
        Assert.DoesNotContain("any.gzip", ids);
    }

    [Fact]
    public void Unknown_files_only_offer_archiving()
    {
        var ids = IdsFor("thing.xyz");
        Assert.Equal(["any.zip", "any.gzip"], ids);
    }

    [Fact]
    public void Folders_can_be_zipped()
    {
        var folder = Path.Combine(_dir.Path, "folder");
        Directory.CreateDirectory(folder);
        Assert.Equal(["any.zip"], Run.Catalog.For([folder]).Select(o => o.Id));
    }

    [Fact]
    public void Operation_ids_are_unique_and_labels_present()
    {
        var all = Run.Catalog.All;
        Assert.Equal(all.Count, all.Select(o => o.Id).Distinct().Count());
        Assert.All(all, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    [Theory]
    [InlineData("photo.png", "photo", ".png")]
    [InlineData("backup.tar.gz", "backup", ".tar.gz")]
    [InlineData("Song.MP3", "Song", ".mp3")]
    [InlineData("noext", "noext", "")]
    public void Path_names_split_stem_and_extension(string name, string stem, string ext)
    {
        Assert.Equal(stem, PathNames.Stem(name));
        Assert.Equal(ext, PathNames.Extension(name));
    }

    [Fact]
    public void Unique_appends_counter()
    {
        var first = _dir.Write("a.txt", "1");
        Assert.Equal(_dir.File("a (1).txt"), PathNames.Unique(first));
        _dir.Write("a (1).txt", "2");
        Assert.Equal(_dir.File("a (2).txt"), PathNames.Unique(first));
    }

    [Fact]
    public void Output_directory_setting_is_respected()
    {
        var input = _dir.Write("doc.txt", "hi");
        var outDir = Path.Combine(_dir.Path, "out");
        var locator = new OutputLocator(new ConverterSettings { OutputDirectory = outDir });
        Assert.Equal(Path.Combine(outDir, "doc.md"), locator.FileFor(input, ".md"));
        Assert.True(Directory.Exists(outDir));
    }
}
