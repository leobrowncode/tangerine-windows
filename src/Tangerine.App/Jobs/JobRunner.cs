using Tangerine.App.Settings;
using Tangerine.Core;
using Tangerine.Core.Operations;

namespace Tangerine.App.Jobs;

internal sealed class JobRunner(JobsWindow hud, Func<AppSettings> settings)
{
    private readonly SemaphoreSlim _slots = new(2);

    public async Task RunAsync(Operation operation, IReadOnlyList<string> files, IReadOnlyDictionary<string, string> parameters)
    {
        var job = new JobViewModel(operation.Title, Describe(files));
        using var cts = new CancellationTokenSource();
        job.Cancel = cts.Cancel;
        hud.Add(job);

        var current = settings();
        var context = new OperationContext
        {
            Settings = current.ToConverterSettings(),
            Parameters = parameters,
            Progress = new Progress<double>(p => job.Progress = p),
        };

        try
        {
            await _slots.WaitAsync(cts.Token);
            try
            {
                var outputs = await Task.Run(() => operation.RunAsync(files, context, cts.Token), cts.Token);
                job.Complete(outputs);
                if (current.RevealResults) Shell.Reveal(outputs);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            job.Cancelled();
        }
        catch (MissingDependencyException ex) when (ex.Dependency == "FFmpeg")
        {
            job.Failed(ex.Message, ("Install FFmpeg", Shell.InstallFfmpeg));
        }
        catch (Exception ex)
        {
            job.Failed(Friendly(ex));
        }
    }

    public void Notify(string title, string detail, IReadOnlyList<string>? outputs = null, string? status = null)
    {
        var job = new JobViewModel(title, detail);
        hud.Add(job);
        job.Complete(outputs ?? [], status ?? "Done");
    }

    public void NotifyFailure(string title, string detail, string message)
    {
        var job = new JobViewModel(title, detail);
        hud.Add(job);
        job.Failed(message);
    }

    public static string Describe(IReadOnlyList<string> files) =>
        files.Count == 1 ? System.IO.Path.GetFileName(files[0].TrimEnd('\\')) : $"{files.Count} items";

    private static string Friendly(Exception ex) => ex switch
    {
        ImageMagick.MagickException => "The image could not be processed: " + ex.Message.Split(" @ ")[0],
        UnauthorizedAccessException => "Tangerine doesn't have permission to write there. Choose another output folder in Settings.",
        System.IO.IOException io => io.Message,
        AggregateException { InnerException: { } inner } => Friendly(inner),
        _ => ex.Message,
    };
}
