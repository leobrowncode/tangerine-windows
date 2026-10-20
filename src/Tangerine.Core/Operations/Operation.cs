namespace Tangerine.Core.Operations;

public enum OperationGroup
{
    /// <summary>Format conversions (inner ring of the wheel).</summary>
    Convert,
    /// <summary>File tools such as compress, trim, merge (outer ring of the wheel).</summary>
    Tool,
}

/// <summary>A value the user must supply before the operation runs, e.g. trim start/end.</summary>
public sealed record ParameterSpec(string Key, string Label, string DefaultValue, string? Hint = null);

public sealed class OperationContext
{
    public required ConverterSettings Settings { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public IProgress<double>? Progress { get; init; }

    public OutputLocator Output => new(Settings);

    public string Parameter(string key, string fallback) =>
        Parameters.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;
}

public abstract class Operation
{
    public abstract string Id { get; }

    /// <summary>Short label shown in the wheel segment, e.g. "PNG" or "Compress".</summary>
    public abstract string Label { get; }

    /// <summary>Longer description shown in the wheel centre, e.g. "Convert to PNG".</summary>
    public virtual string Title => Group == OperationGroup.Convert ? $"Convert to {Label}" : Label;

    public abstract OperationGroup Group { get; }

    public virtual IReadOnlyList<ParameterSpec> Parameters => [];

    public abstract bool AppliesTo(IReadOnlyList<string> inputs);

    /// <returns>Paths of the files or folders that were created.</returns>
    public abstract Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs, OperationContext context, CancellationToken cancellationToken);

    public override string ToString() => Id;
}

/// <summary>An operation that turns each input file into one output independently.</summary>
public abstract class PerFileOperation : Operation
{
    protected abstract IReadOnlySet<FileKind> InputKinds { get; }

    /// <summary>Extension (with dot) of the produced file, or null when the operation keeps the input extension.</summary>
    protected virtual string? TargetExtension => null;

    protected virtual bool AcceptsInput(string path) =>
        InputKinds.Contains(FileKinds.Of(path)) &&
        (TargetExtension is null || !SameFormat(PathNames.Extension(path), TargetExtension));

    public override bool AppliesTo(IReadOnlyList<string> inputs) =>
        inputs.Count > 0 && inputs.All(p => File.Exists(p) && AcceptsInput(p));

    public override async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = i;
            var fileProgress = new Progress<double>(p => context.Progress?.Report((index + Math.Clamp(p, 0, 1)) / inputs.Count));
            var result = await RunOneAsync(inputs[i], context, fileProgress, cancellationToken).ConfigureAwait(false);
            outputs.AddRange(result);
            context.Progress?.Report((double)(i + 1) / inputs.Count);
        }
        return outputs;
    }

    protected abstract Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken);

    protected static bool SameFormat(string a, string b) => Normalize(a) == Normalize(b);

    private static string Normalize(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpeg" => ".jpg",
        ".tif" => ".tiff",
        ".heif" => ".heic",
        ".aif" => ".aiff",
        ".htm" => ".html",
        ".markdown" => ".md",
        ".tgz" => ".tar.gz",
        ".oga" => ".ogg",
        var e => e,
    };

    /// <summary>Runs <paramref name="write"/> and removes the partial output if it throws.</summary>
    protected static async Task<string> WriteGuarded(string output, Func<string, Task> write)
    {
        try
        {
            await write(output).ConfigureAwait(false);
            return output;
        }
        catch
        {
            TryDelete(output);
            throw;
        }
    }

    protected static string WriteGuarded(string output, Action<string> write)
    {
        try
        {
            write(output);
            return output;
        }
        catch
        {
            TryDelete(output);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
