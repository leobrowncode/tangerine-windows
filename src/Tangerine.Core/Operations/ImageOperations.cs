using ImageMagick;

namespace Tangerine.Core.Operations;

internal static class ImageIO
{
    public static readonly IReadOnlySet<FileKind> Kinds = new HashSet<FileKind> { FileKind.Image, FileKind.Vector };

    public static MagickImage ReadFirstFrame(string path)
    {
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        if (FileKinds.Of(path) == FileKind.Vector)
        {
            settings.Density = new Density(192);
            settings.BackgroundColor = MagickColors.Transparent;
        }
        var image = new MagickImage(path, settings);
        image.AutoOrient();
        return image;
    }

    public static MagickFormat FormatFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => MagickFormat.Jpeg,
        ".png" => MagickFormat.Png,
        ".webp" => MagickFormat.WebP,
        ".heic" or ".heif" => MagickFormat.Heic,
        ".tif" or ".tiff" => MagickFormat.Tiff,
        ".avif" => MagickFormat.Avif,
        ".bmp" => MagickFormat.Bmp,
        ".gif" => MagickFormat.Gif,
        ".ico" => MagickFormat.Ico,
        ".pdf" => MagickFormat.Pdf,
        _ => throw new NotSupportedException($"Unsupported image format {extension}"),
    };

    public static bool IsLossy(MagickFormat format) =>
        format is MagickFormat.Jpeg or MagickFormat.WebP or MagickFormat.Heic or MagickFormat.Avif;

    /// <summary>Applies format specific preparation and writes <paramref name="image"/>.</summary>
    public static void Write(MagickImage image, string output, MagickFormat format, int quality)
    {
        switch (format)
        {
            case MagickFormat.Jpeg:
            case MagickFormat.Bmp:
                if (image.HasAlpha)
                {
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaAction.Remove);
                    image.Alpha(AlphaAction.Off);
                }
                break;
            case MagickFormat.Ico:
                if (image.Width > 256 || image.Height > 256)
                    image.Resize(new MagickGeometry(256, 256));
                image.Settings.SetDefine(MagickFormat.Ico, "auto-resize", "256,128,64,48,32,16");
                break;
            case MagickFormat.Tiff:
                image.Settings.Compression = CompressionMethod.Zip;
                break;
        }

        if (IsLossy(format)) image.Quality = (uint)Math.Clamp(quality, 1, 100);
        image.Format = format;
        image.Write(output, format);
    }
}

public sealed class ImageConvertOperation(string extension, string label) : PerFileOperation
{
    public override string Id => $"image.convert{extension}";
    public override string Label => label;
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => ImageIO.Kinds;
    protected override string TargetExtension => extension;

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var output = context.Output.FileFor(input, extension);
            return [WriteGuarded(output, path =>
            {
                using var image = ImageIO.ReadFirstFrame(input);
                ImageIO.Write(image, path, ImageIO.FormatFor(extension), context.Settings.ImageQuality);
            })];
        }, cancellationToken);
}

/// <summary>Base for tools that load an image, transform it and save it in its own format.</summary>
public abstract class ImageTransformOperation : PerFileOperation
{
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Image };

    protected abstract string Suffix(OperationContext context);

    protected virtual int Quality(MagickImage image, OperationContext context) =>
        image.Quality is > 0 and <= 100 ? (int)image.Quality : context.Settings.ImageQuality;

    protected abstract void Transform(MagickImage image, OperationContext context);

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var ext = PathNames.Extension(input);
            var output = context.Output.FileFor(input, ext, Suffix(context));
            return [WriteGuarded(output, path =>
            {
                using var image = ImageIO.ReadFirstFrame(input);
                var quality = Quality(image, context);
                Transform(image, context);
                ImageIO.Write(image, path, ImageIO.FormatFor(ext), quality);
            })];
        }, cancellationToken);
}

public sealed class ImageCompressOperation : ImageTransformOperation
{
    public override string Id => "image.compress";
    public override string Label => "Compress";
    public override string Title => "Compress image";

    protected override bool AcceptsInput(string path) =>
        base.AcceptsInput(path) && PathNames.Extension(path) is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif" or ".avif" or ".tif" or ".tiff";

    protected override string Suffix(OperationContext context) => " (compressed)";

    protected override int Quality(MagickImage image, OperationContext context) => context.Settings.CompressQuality;

    protected override void Transform(MagickImage image, OperationContext context)
    {
        image.Strip();
        if (image.Format == MagickFormat.Png)
        {
            image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
            image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        }
    }
}

public sealed class ImageResizeOperation : ImageTransformOperation
{
    public override string Id => "image.resize";
    public override string Label => "Resize";
    public override string Title => "Resize image";
    public override IReadOnlyList<ParameterSpec> Parameters => [new("scale", "Scale (%)", "50", "Percentage of the original width and height")];

    private static double Scale(OperationContext context) =>
        double.TryParse(context.Parameter("scale", "50").TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0 && s <= 1000
            ? s
            : throw new ArgumentException("Scale must be a percentage between 1 and 1000.");

    protected override string Suffix(OperationContext context) => $" ({Scale(context):0.#}%)";

    protected override void Transform(MagickImage image, OperationContext context) =>
        image.Resize(new Percentage(Scale(context)));
}

public sealed class ImageCropSquareOperation : ImageTransformOperation
{
    public override string Id => "image.crop-square";
    public override string Label => "Crop square";
    public override string Title => "Crop to a centred square";
    protected override string Suffix(OperationContext context) => " (square)";

    protected override void Transform(MagickImage image, OperationContext context)
    {
        var side = Math.Min(image.Width, image.Height);
        image.Crop(new MagickGeometry(side, side), Gravity.Center);
        image.ResetPage();
    }
}

public sealed class ImageRotateOperation : ImageTransformOperation
{
    public override string Id => "image.rotate";
    public override string Label => "Rotate";
    public override string Title => "Rotate 90° clockwise";
    protected override string Suffix(OperationContext context) => " (rotated)";
    protected override void Transform(MagickImage image, OperationContext context) => image.Rotate(90);
}

public sealed class ImageStripMetadataOperation : ImageTransformOperation
{
    public override string Id => "image.strip";
    public override string Label => "Strip metadata";
    public override string Title => "Remove location & camera metadata";
    protected override string Suffix(OperationContext context) => " (clean)";
    protected override void Transform(MagickImage image, OperationContext context) => image.Strip();
}

public sealed class ImagesToPdfOperation : Operation
{
    public override string Id => "image.combine-pdf";
    public override string Label => "Combine PDF";
    public override string Title => "Combine images into one PDF";
    public override OperationGroup Group => OperationGroup.Tool;

    public override bool AppliesTo(IReadOnlyList<string> inputs) =>
        inputs.Count >= 2 && inputs.All(p => File.Exists(p) && ImageIO.Kinds.Contains(FileKinds.Of(p)));

    public override Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs, OperationContext context, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var output = context.Output.FileFor(inputs[0], ".pdf", " (combined)");
            try
            {
                using var collection = new MagickImageCollection();
                for (var i = 0; i < inputs.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var image = ImageIO.ReadFirstFrame(inputs[i]);
                    if (image.HasAlpha)
                    {
                        image.BackgroundColor = MagickColors.White;
                        image.Alpha(AlphaAction.Remove);
                    }
                    image.Quality = (uint)context.Settings.ImageQuality;
                    collection.Add(image);
                    context.Progress?.Report((i + 1.0) / (inputs.Count + 1));
                }
                collection.Write(output, MagickFormat.Pdf);
                return [output];
            }
            catch
            {
                PerFileOperation.TryDelete(output);
                throw;
            }
        }, cancellationToken);
}
