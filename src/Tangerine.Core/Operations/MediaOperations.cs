using Tangerine.Core.Media;

namespace Tangerine.Core.Operations;

internal static class MediaArgs
{
    public static readonly IReadOnlySet<FileKind> Audio = new HashSet<FileKind> { FileKind.Audio };
    public static readonly IReadOnlySet<FileKind> Video = new HashSet<FileKind> { FileKind.Video };
    public static readonly IReadOnlySet<FileKind> AudioOrVideo = new HashSet<FileKind> { FileKind.Audio, FileKind.Video };

    public static bool IsAudioExtension(string ext) => FileKinds.Of("x" + ext) == FileKind.Audio;

    /// <summary>Encoder arguments for writing a file with extension <paramref name="ext"/>.</summary>
    public static string[] EncodeFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
        ".m4a" or ".aac" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
        ".wav" => ["-vn", "-c:a", "pcm_s16le"],
        ".flac" => ["-vn", "-c:a", "flac"],
        ".ogg" or ".oga" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
        ".opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
        ".aiff" or ".aif" => ["-vn", "-c:a", "pcm_s16be"],
        ".wma" => ["-vn", "-c:a", "wmav2", "-b:a", "192k"],
        ".mp4" or ".m4v" or ".mov" => ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"],
        ".mkv" => ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:a", "aac", "-b:a", "160k"],
        ".webm" => ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4",
            "-c:a", "libopus", "-b:a", "128k"],
        ".avi" => ["-c:v", "mpeg4", "-q:v", "3", "-c:a", "libmp3lame", "-q:a", "3"],
        ".wmv" => ["-c:v", "wmv2", "-b:v", "4M", "-c:a", "wmav2", "-b:a", "192k"],
        ".gif" => ["-an", "-vf", "fps=12,scale='min(720,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5",
            "-loop", "0"],
        _ => throw new NotSupportedException($"Unsupported media format {ext}"),
    };
}

public sealed class MediaConvertOperation : PerFileOperation
{
    private readonly string _extension;
    private readonly IReadOnlySet<FileKind> _kinds;
    private readonly Func<string, bool>? _accept;

    public MediaConvertOperation(string extension, string label, IReadOnlySet<FileKind> kinds, Func<string, bool>? accept = null)
    {
        _extension = extension;
        Label = label;
        _kinds = kinds;
        _accept = accept;
    }

    public override string Id => $"media.convert{_extension}.{string.Join("+", _kinds.Order())}";
    public override string Label { get; }
    public override string Title => MediaArgs.IsAudioExtension(_extension) && _kinds.Contains(FileKind.Video) ? $"Extract audio as {Label}" : $"Convert to {Label}";
    public override OperationGroup Group => OperationGroup.Convert;
    protected override IReadOnlySet<FileKind> InputKinds => _kinds;
    protected override string TargetExtension => _extension;
    protected override bool AcceptsInput(string path) => base.AcceptsInput(path) && (_accept?.Invoke(path) ?? true);

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var output = context.Output.FileFor(input, _extension);
        var args = new List<string> { "-i", input };
        args.AddRange(MediaArgs.EncodeFor(_extension));
        args.Add(output);
        return [await WriteGuarded(output, _ => Ffmpeg.RunAsync(context.Settings, args, null, progress, cancellationToken)).ConfigureAwait(false)];
    }
}

public sealed class MediaTrimOperation : PerFileOperation
{
    public override string Id => "media.trim";
    public override string Label => "Trim";
    public override string Title => "Trim to a time range";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => MediaArgs.AudioOrVideo;

    public override IReadOnlyList<ParameterSpec> Parameters =>
    [
        new("start", "Start", "0:00", "Seconds or h:mm:ss"),
        new("end", "End", "", "Leave empty to keep until the end"),
    ];

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var start = Ffmpeg.ParseTime(context.Parameter("start", "0"));
        var endText = context.Parameter("end", "");
        double? end = endText.Length == 0 ? null : Ffmpeg.ParseTime(endText);
        if (end <= start) throw new ArgumentException("The end time must be after the start time.");

        var ext = PathNames.Extension(input);
        var output = context.Output.FileFor(input, ext, " (trimmed)");
        var args = new List<string> { "-i", input, "-ss", Ffmpeg.FormatTime(start) };
        if (end is not null) args.AddRange(["-to", Ffmpeg.FormatTime(end.Value)]);
        args.AddRange(MediaArgs.EncodeFor(ext));
        args.Add(output);
        return [await WriteGuarded(output, _ => Ffmpeg.RunAsync(context.Settings, args, end - start, progress, cancellationToken)).ConfigureAwait(false)];
    }
}

public sealed class MediaCompressOperation : PerFileOperation
{
    public override string Id => "media.compress";
    public override string Label => "Compress";
    public override string Title => "Compress (smaller file)";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => MediaArgs.AudioOrVideo;

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var isVideo = FileKinds.Of(input) == FileKind.Video;
        var output = context.Output.FileFor(input, isVideo ? ".mp4" : ".mp3", " (compressed)");
        string[] encode = isVideo
            ? ["-c:v", "libx264", "-preset", "slow", "-crf", "28", "-pix_fmt", "yuv420p",
               "-vf", "scale='min(1920,iw)':-2", "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart"]
            : ["-vn", "-c:a", "libmp3lame", "-b:a", "96k"];
        var args = new List<string> { "-i", input };
        args.AddRange(encode);
        args.Add(output);
        return [await WriteGuarded(output, _ => Ffmpeg.RunAsync(context.Settings, args, null, progress, cancellationToken)).ConfigureAwait(false)];
    }
}

public sealed class VideoMuteOperation : PerFileOperation
{
    public override string Id => "video.mute";
    public override string Label => "Mute";
    public override string Title => "Remove the audio track";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => MediaArgs.Video;

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var output = context.Output.FileFor(input, PathNames.Extension(input), " (muted)");
        string[] args = ["-i", input, "-map", "0:v", "-c", "copy", "-an", output];
        return [await WriteGuarded(output, _ => Ffmpeg.RunAsync(context.Settings, args, null, progress, cancellationToken)).ConfigureAwait(false)];
    }
}

public sealed class AudioNormalizeOperation : PerFileOperation
{
    public override string Id => "audio.normalize";
    public override string Label => "Normalize";
    public override string Title => "Normalize loudness";
    public override OperationGroup Group => OperationGroup.Tool;
    protected override IReadOnlySet<FileKind> InputKinds => MediaArgs.Audio;

    protected override async Task<IReadOnlyList<string>> RunOneAsync(string input, OperationContext context, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var ext = PathNames.Extension(input);
        var output = context.Output.FileFor(input, ext, " (normalized)");
        var args = new List<string> { "-i", input, "-af", "loudnorm=I=-16:TP=-1.5:LRA=11", "-ar", "48000" };
        args.AddRange(MediaArgs.EncodeFor(ext));
        args.Add(output);
        return [await WriteGuarded(output, _ => Ffmpeg.RunAsync(context.Settings, args, null, progress, cancellationToken)).ConfigureAwait(false)];
    }
}

public sealed class MediaMergeOperation : Operation
{
    public override string Id => "media.merge";
    public override string Label => "Merge";
    public override string Title => "Merge into one file";
    public override OperationGroup Group => OperationGroup.Tool;

    public override bool AppliesTo(IReadOnlyList<string> inputs)
    {
        if (inputs.Count < 2 || !inputs.All(File.Exists)) return false;
        var kind = FileKinds.Of(inputs[0]);
        return kind is FileKind.Audio or FileKind.Video && inputs.All(p => FileKinds.Of(p) == kind);
    }

    public override async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs, OperationContext context, CancellationToken cancellationToken)
    {
        var isVideo = FileKinds.Of(inputs[0]) == FileKind.Video;
        var ext = PathNames.Extension(inputs[0]);
        var output = context.Output.FileFor(inputs[0], ext, " (merged)");

        var infos = new List<MediaInfo>();
        foreach (var input in inputs) infos.Add(await Ffmpeg.ProbeAsync(context.Settings, input, cancellationToken).ConfigureAwait(false));
        var total = infos.Sum(i => i.DurationSeconds ?? 0);
        var withAudio = infos.All(i => i.HasAudio);

        var args = new List<string>();
        foreach (var input in inputs) args.AddRange(["-i", input]);

        var filter = new System.Text.StringBuilder();
        var concatInputs = new System.Text.StringBuilder();
        if (isVideo)
        {
            var w = Even(infos[0].Width > 0 ? infos[0].Width : 1280);
            var h = Even(infos[0].Height > 0 ? infos[0].Height : 720);
            for (var i = 0; i < inputs.Count; i++)
            {
                filter.Append($"[{i}:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];");
                if (withAudio) filter.Append($"[{i}:a]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
                concatInputs.Append($"[v{i}]");
                if (withAudio) concatInputs.Append($"[a{i}]");
            }
            filter.Append($"{concatInputs}concat=n={inputs.Count}:v=1:a={(withAudio ? 1 : 0)}[v]{(withAudio ? "[a]" : "")}");
            args.AddRange(["-filter_complex", filter.ToString(), "-map", "[v]"]);
            if (withAudio) args.AddRange(["-map", "[a]"]);
            var encode = MediaArgs.EncodeFor(ext).ToList();
            var vf = encode.IndexOf("-vf");
            if (vf >= 0) encode.RemoveRange(vf, 2);
            if (!withAudio) encode.Add("-an");
            args.AddRange(encode);
        }
        else
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                filter.Append($"[{i}:a]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
                concatInputs.Append($"[a{i}]");
            }
            filter.Append($"{concatInputs}concat=n={inputs.Count}:v=0:a=1[a]");
            args.AddRange(["-filter_complex", filter.ToString(), "-map", "[a]"]);
            args.AddRange(MediaArgs.EncodeFor(ext));
        }
        args.Add(output);

        try
        {
            await Ffmpeg.RunAsync(context.Settings, args, total > 0 ? total : null, context.Progress, cancellationToken).ConfigureAwait(false);
            return [output];
        }
        catch
        {
            PerFileOperation.TryDelete(output);
            throw;
        }
    }

    private static int Even(int n) => n % 2 == 0 ? n : n - 1;
}
