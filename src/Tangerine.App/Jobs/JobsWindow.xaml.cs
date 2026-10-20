using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Tangerine.App.Native;

namespace Tangerine.App.Jobs;

/// <summary>Stack of job cards in the bottom-right corner, similar to a notification.</summary>
public partial class JobsWindow : Window
{
    private static readonly TimeSpan KeepFinished = TimeSpan.FromSeconds(7);
    private readonly ObservableCollection<JobViewModel> _jobs = [];
    private readonly DispatcherTimer _cleanup;

    public JobsWindow()
    {
        InitializeComponent();
        Jobs.ItemsSource = _jobs;
        NativeMethods.MakeToolWindow(new WindowInteropHelper(this).EnsureHandle(), noActivate: true);
        SizeChanged += (_, _) => Reposition();
        _cleanup = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Cleanup(), Dispatcher);
    }

    public void Add(JobViewModel job)
    {
        _jobs.Add(job);
        while (_jobs.Count > 5 && _jobs.FirstOrDefault(j => !j.IsRunning) is { } old) _jobs.Remove(old);
        if (!IsVisible) Show();
        Reposition();
        _cleanup.Start();
    }

    private void Cleanup()
    {
        if (IsMouseOver) return;
        foreach (var job in _jobs.Where(j => !j.IsRunning && DateTime.Now - j.FinishedAt > (j.State == JobState.Failed ? KeepFinished * 2 : KeepFinished)).ToList())
            _jobs.Remove(job);
        if (_jobs.Count == 0)
        {
            _cleanup.Stop();
            Hide();
        }
    }

    private void Reposition()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 4;
        Top = work.Bottom - ActualHeight - 4;
    }

    private static JobViewModel Job(object sender) => (JobViewModel)((FrameworkElement)sender).DataContext;

    private void OnDismiss(object sender, RoutedEventArgs e)
    {
        var job = Job(sender);
        if (job.IsRunning) job.Cancel?.Invoke();
        else _jobs.Remove(job);
        if (_jobs.Count == 0) Hide();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Job(sender).Cancel?.Invoke();

    private void OnFix(object sender, RoutedEventArgs e) => Job(sender).Fix?.Run();

    private void OnReveal(object sender, RoutedEventArgs e) => Shell.Reveal(Job(sender).Outputs);

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var outputs = Job(sender).Outputs;
        if (outputs.Count > 0) Shell.Open(outputs[0]);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Cleanup();
    }
}
