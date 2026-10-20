using Tangerine.Core.Operations;

namespace Tangerine.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tangerine-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, string content)
    {
        var path = File(name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public static class Run
{
    public static readonly OperationCatalog Catalog = OperationCatalog.CreateDefault();

    public static async Task<IReadOnlyList<string>> Op(string id, IReadOnlyList<string> inputs, Dictionary<string, string>? parameters = null)
    {
        var op = Catalog.Find(id) ?? throw new ArgumentException($"No operation {id}");
        Assert.True(op.AppliesTo(inputs), $"{id} should apply to {string.Join(", ", inputs)}");
        var context = new OperationContext { Settings = new ConverterSettings(), Parameters = parameters ?? new Dictionary<string, string>() };
        var outputs = await op.RunAsync(inputs, context, CancellationToken.None);
        Assert.NotEmpty(outputs);
        foreach (var output in outputs) Assert.True(System.IO.File.Exists(output) || Directory.Exists(output), $"missing {output}");
        return outputs;
    }

    public static async Task<string> Single(string id, string input, Dictionary<string, string>? parameters = null) =>
        Assert.Single(await Op(id, [input], parameters));
}
