using System.Windows;
using System.Windows.Threading;
using Tangerine.App.Actions;
using Tangerine.App.Input;
using Tangerine.App.Jobs;
using Tangerine.App.Native;
using Tangerine.App.Office;
using Tangerine.App.Settings;
using Tangerine.App.Wheel;
using Tangerine.Core.Office;
using Tangerine.Core.Operations;

namespace Tangerine.App;

public partial class App : Application
{
    private const string InstanceName = @"Local\Tangerine.ForWindows";
    private const int MaxSegmentsPerRing = 12;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private AppSettings _settings = new();
    private OperationCatalog _catalog = null!;
    private string _officeStatus = "";
    private GestureHook _hook = null!;
    private WheelWindow _wheel = null!;
    private JobsWindow _hud = null!;
    private JobRunner _runner = null!;
    private FileActions _actions = null!;
    private TrayIcon _tray = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _dismissAfterDrag;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--render-preview", var previewDir, ..])
        {
            InitializeServices();
            Shutdown(Preview.Render(previewDir, _wheel, _hud, () => new SettingsWindow(_settings, _officeStatus)));
            return;
        }

        _instanceMutex = new Mutex(true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            // Ask the running copy to show its settings, then exit.
            try { EventWaitHandle.OpenExisting(InstanceName + ".Show").Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowSettings);
        }) { IsBackground = true, Name = "Tangerine single instance" }.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            _runner?.NotifyFailure("Something went wrong", "Tangerine", args.Exception.Message);
        };

        _settings = SettingsStore.Load();
        InitializeServices();

        _hook = new GestureHook(Dispatcher);
        _hook.Triggered += OnTriggered;
        _hook.DragEnded += OnDragEnded;
        _hook.EscapePressed += () => _wheel.Dismiss();
        _hook.Start();

        _tray = new TrayIcon(ShowSettings, SetPaused, Quit);
        ApplySettings();

        if (!_settings.HasCompletedWelcome || !e.Args.Contains("--background")) ShowSettings();
    }

    private void InitializeServices()
    {
        _catalog = OperationCatalog.CreateDefault();
        var exporters = new List<IPdfExporter>();
        if (MicrosoftOfficeExporter.TryCreate() is { } office) exporters.Add(office);
        if (LibreOfficeExporter.TryCreate() is { } libre) exporters.Add(libre);
        OfficeCatalog.Register(_catalog, exporters);
        _officeStatus = exporters.Count == 0
            ? "Install Microsoft Office or LibreOffice to convert DOCX, PPTX and XLSX to PDF and slides to images."
            : "Using " + string.Join(" and ", exporters.Select(x => x.Name));

        _hud = new JobsWindow();
        _runner = new JobRunner(_hud, () => _settings);
        _actions = new FileActions(_runner);

        _wheel = new WheelWindow { ItemsProvider = BuildItems };
        _wheel.ItemDropped += OnItemDropped;
        _wheel.Dismissed += () =>
        {
            if (_hook is not null) _hook.WheelVisible = false;
        };
    }

    private void ApplySettings()
    {
        _hook.Configure(_settings.ConvertKey, _settings.ActionsKey);
        _hook.Enabled = !_settings.Paused;
        _tray.SetState(_settings.Paused, $"Tangerine: drag a file, tap {KeyName(_settings.ConvertKey)}");
        try
        {
            Settings.Startup.Apply(_settings.LaunchAtLogin);
        }
        catch (UnauthorizedAccessException)
        {
            // Group policy can lock the Run key; the setting just won't take effect.
        }
    }

    private static string KeyName(TriggerKey key) => key switch
    {
        TriggerKey.Control => "Ctrl",
        TriggerKey.CapsLock => "Caps Lock",
        _ => key.ToString(),
    };

    private void OnTriggered(WheelMode mode, NativeMethods.POINT point)
    {
        _dismissAfterDrag?.Stop();
        if (_wheel.IsOpen && _wheel.Mode == mode)
        {
            _wheel.Dismiss();
            return;
        }
        NativeMethods.GetCursorPos(out var cursor);
        _hook.WheelVisible = true;
        _wheel.ShowAt(mode, cursor.X == 0 && cursor.Y == 0 ? point : cursor);
    }

    private void OnDragEnded()
    {
        if (!_wheel.IsOpen) return;
        // The drop callback may arrive just after the button-up we observe in the hook; give it a moment.
        _dismissAfterDrag ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Normal, (_, _) =>
        {
            _dismissAfterDrag!.Stop();
            _wheel.Dismiss();
        }, Dispatcher);
        _dismissAfterDrag.Stop();
        _dismissAfterDrag.Start();
    }

    private IReadOnlyList<WheelItem> BuildItems(WheelMode mode, IReadOnlyList<string> files)
    {
        if (mode == WheelMode.Actions) return _actions.Items;

        var operations = _catalog.For(files);
        var conversions = operations.Where(o => o.Group == OperationGroup.Convert).Take(MaxSegmentsPerRing).ToList();
        var tools = operations.Where(o => o.Group == OperationGroup.Tool).Take(MaxSegmentsPerRing).ToList();
        var toolRing = conversions.Count > 0 ? 1 : 0;

        return conversions.Select(o => Item(o, 0)).Concat(tools.Select(o => Item(o, toolRing))).ToList();

        WheelItem Item(Operation op, int ring) => new()
        {
            Label = op.Label,
            Title = op.Title,
            Ring = ring,
            Invoke = f => RunOperation(op, f),
        };
    }

    private async Task RunOperation(Operation operation, IReadOnlyList<string> files)
    {
        IReadOnlyDictionary<string, string> parameters = new Dictionary<string, string>();
        if (operation.Parameters.Count > 0)
        {
            var answer = ParametersWindow.Ask(operation, JobRunner.Describe(files));
            if (answer is null) return;
            parameters = answer;
        }
        await _runner.RunAsync(operation, files, parameters);
    }

    private async void OnItemDropped(WheelItem item, IReadOnlyList<string> files)
    {
        try
        {
            await item.Invoke(files);
        }
        catch (Exception ex)
        {
            _runner.NotifyFailure(item.Title, JobRunner.Describe(files), ex.Message);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            NativeMethods.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle);
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _officeStatus);
        _settingsWindow.Saved += saved =>
        {
            _settings = saved;
            SettingsStore.Save(_settings);
            ApplySettings();
        };
        _settingsWindow.QuitRequested += Quit;
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (!_settings.HasCompletedWelcome)
            {
                _settings.HasCompletedWelcome = true;
                SettingsStore.Save(_settings);
            }
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SetPaused(bool paused)
    {
        if (_settings.Paused == paused) return;
        _settings.Paused = paused;
        SettingsStore.Save(_settings);
        ApplySettings();
    }

    private void Quit()
    {
        _settingsWindow?.Close();
        _hook.Dispose();
        _tray.Dispose();
        _instanceMutex?.ReleaseMutex();
        Shutdown();
    }
}
