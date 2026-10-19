namespace Tangerine.App.Wheel;

public sealed class WheelItem
{
    public required string Label { get; init; }
    public required string Title { get; init; }

    /// <summary>Optional Segoe Fluent Icons glyph shown above the label.</summary>
    public string? Glyph { get; init; }

    /// <summary>0 = inner ring, 1 = outer ring.</summary>
    public int Ring { get; init; }

    public required Func<IReadOnlyList<string>, Task> Invoke { get; init; }
}
