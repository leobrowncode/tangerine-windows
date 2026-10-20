using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Tangerine.App.Jobs;

public enum JobState
{
    Running,
    Done,
    Failed,
    Cancelled,
}

public sealed class JobViewModel : INotifyPropertyChanged
{
    private double _progress;
    private bool _hasProgress;
    private JobState _state = JobState.Running;
    private string _status = "Working…";
    private IReadOnlyList<string> _outputs = [];

    public JobViewModel(string title, string detail)
    {
        Title = title;
        Detail = detail;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }
    public string Detail { get; }
    public Action? Cancel { get; set; }

    /// <summary>Optional extra button, e.g. "Install FFmpeg" after a missing dependency error.</summary>
    public (string Label, Action Run)? Fix { get; private set; }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0, 1);
            _hasProgress = true;
            OnChanged();
            OnChanged(nameof(IsIndeterminate));
            OnChanged(nameof(Percent));
        }
    }

    public bool IsIndeterminate => State == JobState.Running && !_hasProgress;
    public double Percent => _progress * 100;

    public JobState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnChanged();
            OnChanged(nameof(IsRunning));
            OnChanged(nameof(IsIndeterminate));
            OnChanged(nameof(CanReveal));
            OnChanged(nameof(ProgressVisibility));
            OnChanged(nameof(CancelVisibility));
            OnChanged(nameof(RevealVisibility));
            OnChanged(nameof(FixVisibility));
            OnChanged(nameof(StateGlyph));
        }
    }

    public bool IsRunning => State == JobState.Running;
    public bool CanReveal => State == JobState.Done && Outputs.Count > 0;

    public Visibility ProgressVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CancelVisibility => IsRunning && Cancel is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RevealVisibility => CanReveal ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FixVisibility => State == JobState.Failed && Fix is not null ? Visibility.Visible : Visibility.Collapsed;
    public string FixLabel => Fix?.Label ?? "";

    public string StateGlyph => State switch
    {
        JobState.Done => "\uE73E",
        JobState.Failed => "\uE783",
        JobState.Cancelled => "\uE711",
        _ => "\uE895",
    };

    public string Status
    {
        get => _status;
        private set { _status = value; OnChanged(); }
    }

    public IReadOnlyList<string> Outputs
    {
        get => _outputs;
        private set { _outputs = value; OnChanged(); OnChanged(nameof(CanReveal)); OnChanged(nameof(RevealVisibility)); }
    }

    public DateTime FinishedAt { get; private set; }

    public void Complete(IReadOnlyList<string> outputs, string? status = null)
    {
        Outputs = outputs;
        Status = status ?? (outputs.Count switch
        {
            0 => "Done",
            1 => "Saved " + System.IO.Path.GetFileName(outputs[0].TrimEnd('\\')),
            _ => $"Saved {outputs.Count} items",
        });
        FinishedAt = DateTime.Now;
        State = JobState.Done;
    }

    public void Failed(string message, (string Label, Action Run)? fix = null)
    {
        Fix = fix;
        OnChanged(nameof(FixLabel));
        Status = message;
        FinishedAt = DateTime.Now;
        State = JobState.Failed;
    }

    public void Cancelled()
    {
        Status = "Cancelled";
        FinishedAt = DateTime.Now;
        State = JobState.Cancelled;
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
