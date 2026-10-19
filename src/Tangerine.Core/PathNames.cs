namespace Tangerine.Core;

public static class PathNames
{
    private static readonly string[] CompoundExtensions = [".tar.gz"];

    /// <summary>Lower-case extension including the dot; understands compound extensions such as ".tar.gz".</summary>
    public static string Extension(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var compound in CompoundExtensions)
        {
            if (name.EndsWith(compound, StringComparison.OrdinalIgnoreCase) && name.Length > compound.Length)
                return compound;
        }
        return Path.GetExtension(name).ToLowerInvariant();
    }

    public static string Stem(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var ext = Extension(path);
        return ext.Length > 0 && name.Length > ext.Length ? name[..^ext.Length] : name;
    }

    /// <summary>Returns <paramref name="desired"/> or "name (n).ext" if it already exists.</summary>
    public static string Unique(string desired)
    {
        if (!File.Exists(desired) && !Directory.Exists(desired)) return desired;
        var dir = Path.GetDirectoryName(desired) ?? "";
        var ext = Directory.Exists(desired) ? "" : Extension(desired);
        var stem = ext.Length > 0 ? Path.GetFileName(desired)[..^ext.Length] : Path.GetFileName(desired);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}
