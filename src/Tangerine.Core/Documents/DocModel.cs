namespace Tangerine.Core.Documents;

/// <summary>A run of text. "\n" inside <see cref="Text"/> is a hard line break.</summary>
public sealed record Inline(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

public abstract record Block;

public sealed record Heading(int Level, IReadOnlyList<Inline> Content) : Block;

public sealed record Paragraph(IReadOnlyList<Inline> Content) : Block;

public sealed record ListItem(bool Ordered, int Depth, IReadOnlyList<Inline> Content) : Block;

public sealed record Quote(IReadOnlyList<Inline> Content) : Block;

public sealed record CodeBlock(string Text) : Block;

public sealed record Rule : Block;

public sealed class DocModel
{
    public string Title { get; set; } = "Untitled";
    public List<Block> Blocks { get; } = [];

    public static string PlainText(IEnumerable<Inline> content) => string.Concat(content.Select(i => i.Text));
}
