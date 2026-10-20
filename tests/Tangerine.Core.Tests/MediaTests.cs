using System.Diagnostics;
using Tangerine.Core.Media;
using Tangerine.Core.Operations;

namespace Tangerine.Core.Tests;

public class MediaTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly ConverterSettings Settings = new();
    private static readonly bool HasFfmpeg = Ffmpeg.TryLocate(Settings) is not null && Ffmpeg.TryLocate(Settings, "ffprobe") is not null;

    public void Dispose() => _dir.Dispose();

    private string Generate(string name, params string[] args)
    {
        var path = _dir.File(name);
        var psi = new ProcessStartInfo(Ffmpeg.Locate(Settings)) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args).Append(path)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
        return path;
    }

    private string Tone(string name = "tone.wav", double seconds = 2) =>
        Generate(name, "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}");

    private string Clip(string name = "clip.mp4", double seconds = 2, string size = "320x240") =>
        Generate(name, "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size={size}:rate=25", "-f", "lavfi", "-i", $"sine=duration={seconds}",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest");

    private static Task<MediaInfo> Probe(string path) => Ffmpeg.ProbeAsync(Settings, path, CancellationToken.None);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03.5", 3723.5)]
    public void Parses_times(string text, double seconds) => Assert.Equal(seconds, Ffmpeg.ParseTime(text));

    [Fact]
    public void Rejects_bad_times() => Assert.Throws<FormatException>(() => Ffmpeg.ParseTime("abc"));

    [SkippableTheory]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".flac")]
    [InlineData(".ogg")]
    [InlineData(".opus")]
    [InlineData(".aiff")]
    [InlineData(".wma")]
    public async Task Audio_converts(string ext)
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var output = await Run.Single($"media.convert{ext}.Audio", Tone());
        var info = await Probe(output);
        Assert.True(info.HasAudio);
        Assert.InRange(info.DurationSeconds ?? 0, 1.8, 2.3);
    }

    [SkippableTheory]
    [InlineData(".mov")]
    [InlineData(".mkv")]
    [InlineData(".webm")]
    [InlineData(".avi")]
    [InlineData(".wmv")]
    [InlineData(".gif")]
    public async Task Video_converts(string ext)
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var output = await Run.Single($"media.convert{ext}.Video", Clip(seconds: 1));
        Assert.True((await Probe(output)).HasVideo);
    }

    [SkippableFact]
    public async Task Video_extracts_mp3_and_reports_progress()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var input = Clip();
        var reports = new List<double>();
        var op = Run.Catalog.Find("media.convert.mp3.Video")!;
        var outputs = await op.RunAsync([input], new OperationContext { Settings = Settings, Progress = new SyncProgress(reports.Add) }, CancellationToken.None);
        var info = await Probe(Assert.Single(outputs));
        Assert.False(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.Equal(1, reports[^1]);
    }

    [SkippableFact]
    public async Task Animated_gif_converts_to_mp4()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var gif = await Run.Single("media.convert.gif.Video", Clip(seconds: 1));
        var mp4 = await Run.Single("media.convert.mp4.Image", gif);
        Assert.True((await Probe(mp4)).HasVideo);
    }

    [SkippableFact]
    public async Task Trim_cuts_range()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var output = await Run.Single("media.trim", Clip(seconds: 4), new() { ["start"] = "1", ["end"] = "0:02.5" });
        Assert.InRange((await Probe(output)).DurationSeconds ?? 0, 1.3, 1.7);
        Assert.EndsWith("clip (trimmed).mp4", output);
    }

    [SkippableFact]
    public async Task Trim_rejects_inverted_range()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        await Assert.ThrowsAsync<ArgumentException>(() => Run.Single("media.trim", Tone(), new() { ["start"] = "2", ["end"] = "1" }));
    }

    [SkippableFact]
    public async Task Mute_removes_audio()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var info = await Probe(await Run.Single("video.mute", Clip(seconds: 1)));
        Assert.True(info.HasVideo);
        Assert.False(info.HasAudio);
    }

    [SkippableFact]
    public async Task Compress_video_produces_mp4()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var output = await Run.Single("media.compress", Clip(seconds: 1));
        Assert.EndsWith("clip (compressed).mp4", output);
    }

    [SkippableFact]
    public async Task Merge_videos_with_different_sizes()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var a = Clip("a.mp4", 1, "320x240");
        var b = Clip("b.mp4", 1, "640x360");
        var output = Assert.Single(await Run.Op("media.merge", [a, b]));
        var info = await Probe(output);
        Assert.Equal((320, 240), (info.Width, info.Height));
        Assert.InRange(info.DurationSeconds ?? 0, 1.8, 2.3);
        Assert.True(info.HasAudio);
    }

    [SkippableFact]
    public async Task Merge_audio_files()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var output = Assert.Single(await Run.Op("media.merge", [Tone("a.wav", 1), Tone("b.wav", 1)]));
        Assert.InRange((await Probe(output)).DurationSeconds ?? 0, 1.9, 2.1);
    }

    [SkippableFact]
    public async Task Failed_conversion_leaves_no_partial_file()
    {
        Skip.IfNot(HasFfmpeg, "ffmpeg not installed");
        var bogus = _dir.Write("broken.mp3", "not really audio");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run.Single("media.convert.wav.Audio", bogus));
        Assert.False(File.Exists(_dir.File("broken.wav")));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

[CollectionDefinition(nameof(EnvironmentMutating), DisableParallelization = true)]
public class EnvironmentMutating;

[Collection(nameof(EnvironmentMutating))]
public class MissingFfmpegTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Missing_ffmpeg_raises_helpful_error()
    {
        var input = _dir.Write("song.mp3", "x");
        var op = Run.Catalog.Find("media.convert.wav.Audio")!;
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            var ex = await Assert.ThrowsAsync<MissingDependencyException>(() =>
                op.RunAsync([input], new OperationContext { Settings = new ConverterSettings() }, CancellationToken.None));
            Assert.Contains("winget", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }
}
