using System.Formats.Tar;
using System.IO.Compression;

namespace Tangerine.Core.Operations;

internal static class Archives
{
    public static bool IsContainer(string path) => PathNames.Extension(path) is ".zip" or ".tar" or ".tar.gz" or ".tgz";

    public static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        switch (PathNames.Extension(archive))
        {
            case ".zip":
                ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: false);
                break;
            case ".tar":
                TarFile.ExtractToDirectory(archive, destination, overwriteFiles: false);
                break;
            case ".tar.gz" or ".tgz":
                using (var file = File.OpenRead(archive))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                    TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: false);
                break;
            case ".gz":
                using (var file = File.OpenRead(archive))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var output = File.Create(Path.Combine(destination, PathNames.Stem(archive))))
                    gzip.CopyTo(output);
                break;
            default:
                throw new NotSupportedException($"Cannot extract {PathNames.Extension(archive)} files");
        }
    }

    /// <summary>Packs the contents of <paramref name="sourceDirectory"/> into <paramref name="output"/>.</summary>
    public static void PackDirectory(string sourceDirectory, string output)
    {
        switch (PathNames.Extension(output))
        {
            case ".zip":
                ZipFile.CreateFromDirectory(sourceDirectory, output, CompressionLevel.Optimal, includeBaseDirectory: false);
                break;
            case ".tar":
                TarFile.CreateFromDirectory(sourceDirectory, output, includeBaseDirectory: false);
                break;
            case ".tar.gz":
                using (var file = File.Create(output))
                using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                    TarFile.CreateFromDirectory(sourceDirectory, gzip, includeBaseDirectory: false);
                break;
            default:
                throw new NotSupportedException($"Cannot create {PathNames.Extension(output)} archives");
        }
    }

    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Tangerine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public sealed class ArchiveConvertOperation(string extension, string label) : PerFileOperation
{
    public override string Id => $"archive.convert{extension}";
    public override string Label => label;
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Archive };
    protected override string TargetExtension => extension;
    protected override bool AcceptsInput(string path) => base.AcceptsInput(path) && Archives.IsContainer(path);

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var temp = Archives.TempDirectory();
            try
            {
                Archives.Extract(input, temp);
                progress.Report(0.5);
                var output = context.Output.FileFor(input, extension);
                return [WriteGuarded(output, path => Archives.PackDirectory(temp, path))];
            }
            finally
            {
                TryDelete(temp);
            }
        }, cancellationToken);
}

public sealed class ArchiveExtractOperation : PerFileOperation
{
    public override string Id => "archive.extract";
    public override string Label => "Extract";
    public override string Title => "Extract here";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind> { FileKind.Archive };

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            if (PathNames.Extension(input) == ".gz")
            {
                var inner = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, PathNames.Stem(input));
                var output = context.Output.FileFor(inner, PathNames.Extension(inner));
                return [WriteGuarded(output, path =>
                {
                    using var file = File.OpenRead(input);
                    using var gzip = new GZipStream(file, CompressionMode.Decompress);
                    using var target = File.Create(path);
                    gzip.CopyTo(target);
                })];
            }
            var folder = context.Output.FolderFor(input);
            try
            {
                Archives.Extract(input, folder);
                return [folder];
            }
            catch
            {
                TryDelete(folder);
                throw;
            }
        }, cancellationToken);
}

/// <summary>Packs every dropped file and folder into one ZIP.</summary>
public sealed class ZipOperation : Operation
{
    public override string Id => "any.zip";
    public override string Label => "ZIP";
    public override string Title => "Compress to ZIP";
    public override OperationGroup Group => OperationGroup.Tool;

    public override bool AppliesTo(IReadOnlyList<string> inputs) =>
        inputs.Count > 0 && inputs.All(p => File.Exists(p) || Directory.Exists(p)) &&
        !(inputs.Count == 1 && PathNames.Extension(inputs[0]) == ".zip");

    public override Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs, OperationContext context, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var first = inputs[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var output = inputs.Count == 1
                ? PathNames.Unique(Path.Combine(context.Output.DirectoryFor(first), (Directory.Exists(first) ? Path.GetFileName(first) : PathNames.Stem(first)) + ".zip"))
                : PathNames.Unique(Path.Combine(context.Output.DirectoryFor(first), "Archive.zip"));
            try
            {
                using var stream = File.Create(output);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < inputs.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = inputs[i].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (Directory.Exists(path))
                    {
                        var root = Path.GetFileName(path);
                        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        {
                            var relative = Path.Combine(root, Path.GetRelativePath(path, file)).Replace('\\', '/');
                            if (names.Add(relative)) zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                        }
                    }
                    else
                    {
                        var name = Path.GetFileName(path);
                        for (var n = 1; !names.Add(name); n++) name = $"{PathNames.Stem(path)} ({n}){PathNames.Extension(path)}";
                        zip.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                    }
                    context.Progress?.Report((i + 1.0) / inputs.Count);
                }
                return [output];
            }
            catch
            {
                PerFileOperation.TryDelete(output);
                throw;
            }
        }, cancellationToken);
}

public sealed class GzipOperation : PerFileOperation
{
    public override string Id => "any.gzip";
    public override string Label => "GZIP";
    public override string Title => "Compress with GZIP";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => new HashSet<FileKind>();
    protected override bool AcceptsInput(string path) => FileKinds.Of(path) != FileKind.Archive;

    protected override Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var output = PathNames.Unique(Path.Combine(context.Output.DirectoryFor(input), Path.GetFileName(input) + ".gz"));
            return [WriteGuarded(output, path =>
            {
                using var source = File.OpenRead(input);
                using var target = File.Create(path);
                using var gzip = new GZipStream(target, CompressionLevel.Optimal);
                source.CopyTo(gzip);
            })];
        }, cancellationToken);
}
