using ImageMagick;
using ImageMagick.Drawing;

namespace Tangerine.Core.Tests;

public class ImageTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string MakePng(string name = "photo.png", uint width = 64, uint height = 48)
    {
        using var image = new MagickImage(MagickColors.Orange, width, height);
        image.Draw(new Drawables().FillColor(MagickColors.Transparent).StrokeColor(MagickColors.Black).Rectangle(4, 4, 20, 20));
        image.Alpha(AlphaAction.Set);
        var path = _dir.File(name);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    [Theory]
    [InlineData(".jpg", MagickFormat.Jpeg)]
    [InlineData(".webp", MagickFormat.WebP)]
    [InlineData(".tiff", MagickFormat.Tiff)]
    [InlineData(".bmp", MagickFormat.Bmp)]
    [InlineData(".avif", MagickFormat.Avif)]
    [InlineData(".ico", MagickFormat.Ico)]
    public async Task Png_converts_to(string ext, MagickFormat expected)
    {
        var output = await Run.Single($"image.convert{ext}", MakePng());
        Assert.EndsWith("photo" + ext, output);
        var info = new MagickImageInfo(output);
        Assert.Equal(expected, info.Format);
    }

    [Fact]
    public async Task Png_converts_to_pdf()
    {
        var output = await Run.Single("image.convert.pdf", MakePng());
        Assert.StartsWith("%PDF", File.ReadAllText(output)[..4]);
    }

    [Fact]
    public async Task Svg_rasterises_to_png()
    {
        var svg = _dir.Write("logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20"><rect width="40" height="20" fill="#ff8a1f"/></svg>""");
        var output = await Run.Single("image.convert.png", svg);
        using var image = new MagickImage(output);
        Assert.True(image.Width >= 40);
    }

    [Fact]
    public async Task Resize_scales_by_percentage()
    {
        var output = await Run.Single("image.resize", MakePng(), new() { ["scale"] = "50" });
        using var image = new MagickImage(output);
        Assert.Equal((32u, 24u), (image.Width, image.Height));
        Assert.EndsWith("photo (50%).png", output);
    }

    [Fact]
    public async Task Crop_square_uses_shortest_side()
    {
        var output = await Run.Single("image.crop-square", MakePng());
        using var image = new MagickImage(output);
        Assert.Equal((48u, 48u), (image.Width, image.Height));
    }

    [Fact]
    public async Task Rotate_swaps_dimensions()
    {
        var output = await Run.Single("image.rotate", MakePng());
        using var image = new MagickImage(output);
        Assert.Equal((48u, 64u), (image.Width, image.Height));
    }

    [Fact]
    public async Task Compress_writes_new_file_next_to_original()
    {
        var input = MakePng(width: 400, height: 300);
        var output = await Run.Single("image.compress", input);
        Assert.EndsWith("photo (compressed).png", output);
        Assert.True(File.Exists(input));
    }

    [Fact]
    public async Task Combine_images_into_pdf()
    {
        var a = MakePng("a.png");
        var b = MakePng("b.png");
        var output = Assert.Single(await Run.Op("image.combine-pdf", [a, b]));
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
        Assert.Equal(2, pdf.NumberOfPages);
    }

    [Fact]
    public async Task Converting_twice_does_not_overwrite()
    {
        var input = MakePng();
        var first = await Run.Single("image.convert.jpg", input);
        var second = await Run.Single("image.convert.jpg", input);
        Assert.NotEqual(first, second);
        Assert.EndsWith("photo (1).jpg", second);
    }
}
