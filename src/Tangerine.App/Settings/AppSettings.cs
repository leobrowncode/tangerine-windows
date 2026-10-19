using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tangerine.Core;

namespace Tangerine.App.Settings;

public enum TriggerKey
{
    None,
    Shift,
    Control,
    Alt,
    CapsLock,
}

public sealed class AppSettings
{
    public TriggerKey ConvertKey { get; set; } = TriggerKey.Shift;
    public TriggerKey ActionsKey { get; set; } = TriggerKey.Control;

    public int ImageQuality { get; set; } = 85;
    public int CompressQuality { get; set; } = 70;
    public int PdfDpi { get; set; } = 300;

    public bool UseOutputFolder { get; set; }
    public string? OutputFolder { get; set; }

    public string? FfmpegPath { get; set; }

    public bool LaunchAtLogin { get; set; }
    public bool RevealResults { get; set; }
    public bool Paused { get; set; }
    public bool HasCompletedWelcome { get; set; }

    public ConverterSettings ToConverterSettings() => new()
    {
        ImageQuality = Math.Clamp(ImageQuality, 1, 100),
        CompressQuality = Math.Clamp(CompressQuality, 1, 100),
        PdfDpi = Math.Clamp(PdfDpi, 72, 600),
        OutputDirectory = UseOutputFolder && !string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : null,
        FfmpegPath = FfmpegPath,
    };

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tangerine");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file should not stop the app from starting.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
