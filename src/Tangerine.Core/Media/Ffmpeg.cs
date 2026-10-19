using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tangerine.Core.Media;

public sealed record MediaInfo(double? DurationSeconds, bool HasVideo, bool HasAudio, int Width, int Height);

public static partial class Ffmpeg
{
    public const string InstallHint = "Install it with \"winget install Gyan.FFmpeg\", place ffmpeg.exe next to Tangerine, or set its location in Settings.";

    public static string? TryLocate(ConverterSettings settings, string tool = "ffmpeg")
    {
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath))
        {
            var configured = Environment.ExpandEnvironmentVariables(settings.FfmpegPath.Trim('"', ' '));
            if (Directory.Exists(configured))
            {
                candidates.Add(Path.Combine(configured, exe));
                candidates.Add(Path.Combine(configured, "bin", exe));
            }
            else
            {
                candidates.Add(Path.Combine(Path.GetDirectoryName(configured) ?? "", exe));
            }
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, exe));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", exe));

        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", exe));
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim('"'), exe));

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string Locate(ConverterSettings settings, string tool = "ffmpeg") =>
        TryLocate(settings, tool) ?? throw new MissingDependencyException("FFmpeg", InstallHint);

    public static async Task<MediaInfo> ProbeAsync(ConverterSettings settings, string input, CancellationToken cancellationToken)
    {
        var ffprobe = Locate(settings, "ffprobe");
        var psi = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "json", input })
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not read {Path.GetFileName(input)}: {(await stderr).Trim()}");

        using var json = JsonDocument.Parse(await stdout);
        double? duration = null;
        if (json.RootElement.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var d) &&
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            duration = seconds;

        bool hasVideo = false, hasAudio = false;
        int width = 0, height = 0;
        if (json.RootElement.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "audio") hasAudio = true;
                if (type == "video" && !hasVideo)
                {
                    hasVideo = true;
                    width = s.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    height = s.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                }
            }
        }
        return new MediaInfo(duration, hasVideo, hasAudio, width, height);
    }

    /// <summary>Runs ffmpeg with <paramref name="arguments"/>, reporting progress from 0 to 1.</summary>
    public static async Task RunAsync(ConverterSettings settings, IEnumerable<string> arguments, double? totalSeconds, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var ffmpeg = Locate(settings);
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-y", "-progress", "pipe:1", "-nostats" }) psi.ArgumentList.Add(a);
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var errorTail = new StringBuilder();
        var duration = totalSeconds;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (errorTail)
            {
                errorTail.AppendLine(e.Data);
                if (errorTail.Length > 8000) errorTail.Remove(0, errorTail.Length - 8000);
            }
            if (duration is null && DurationRegex().Match(e.Data) is { Success: true } m)
                duration = ParseTime(m.Groups[1].Value);
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null || duration is not > 0) return;
            if (e.Data.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(e.Data.AsSpan(12), out var us))
                progress?.Report(Math.Clamp(us / 1_000_000.0 / duration.Value, 0, 1));
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string tail;
            lock (errorTail) tail = errorTail.ToString();
            var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            throw new InvalidOperationException("FFmpeg failed: " + string.Join(" ", lines.TakeLast(3)));
        }
        progress?.Report(1);
    }

    /// <summary>Parses "90", "1:30", "01:02:03.5" into seconds.</summary>
    public static double ParseTime(string value)
    {
        var parts = value.Trim().Split(':');
        if (parts.Length is 0 or > 3) throw new FormatException($"Invalid time \"{value}\". Use seconds or hh:mm:ss.");
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0)
                throw new FormatException($"Invalid time \"{value}\". Use seconds or hh:mm:ss.");
            total = total * 60 + n;
        }
        return total;
    }

    public static string FormatTime(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"Duration:\s*(\d+:\d+:\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();
}
