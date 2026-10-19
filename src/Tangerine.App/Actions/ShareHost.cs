using System.IO;
using System.Windows;
using System.Windows.Interop;
using Tangerine.App.Native;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Tangerine.App.Actions;

/// <summary>Shows the Windows share sheet for files from a desktop (non-packaged) app.</summary>
internal sealed class ShareHost
{
    private Window? _anchor;
    private IntPtr _hwnd;
    private IReadOnlyList<IStorageItem> _items = [];
    private string _title = "";
    private DataTransferManager? _manager;

    public async Task ShareAsync(IReadOnlyList<string> paths)
    {
        var items = new List<IStorageItem>();
        foreach (var path in paths)
        {
            items.Add(Directory.Exists(path)
                ? await StorageFolder.GetFolderFromPathAsync(path.TrimEnd('\\'))
                : await StorageFile.GetFileFromPathAsync(path));
        }
        _items = items;
        _title = paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : $"{paths.Count} items";

        EnsureAnchor();
        DataTransferManagerInterop.ShowShareUIForWindow(_hwnd);
    }

    private void EnsureAnchor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        if (_anchor is null)
        {
            // The share sheet must be owned by a visible window; a 1×1 transparent window near the cursor serves as anchor.
            _anchor = new Window
            {
                Width = 1, Height = 1, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent, ShowInTaskbar = false, Topmost = true,
                ShowActivated = true, ResizeMode = ResizeMode.NoResize, Title = "Tangerine share",
            };
            _hwnd = new WindowInteropHelper(_anchor).EnsureHandle();
            _manager = DataTransferManagerInterop.GetForWindow(_hwnd);
            _manager.DataRequested += OnDataRequested;
        }
        _anchor.Show();
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, cursor.X, cursor.Y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        _anchor.Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var data = args.Request.Data;
        data.Properties.Title = _title;
        data.Properties.Description = "Shared from Tangerine";
        data.SetStorageItems(_items, readOnly: true);
    }
}
