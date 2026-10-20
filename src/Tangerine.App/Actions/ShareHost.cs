using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Tangerine.App.Actions;

/// <summary>Shows the Windows share sheet for files from a desktop (non-packaged) app.</summary>
internal sealed class ShareHost
{
    private AnchorWindow? _anchor;
    private IReadOnlyList<IStorageItem> _items = [];
    private string _title = "";

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

        if (_anchor is null)
        {
            _anchor = new AnchorWindow();
            DataTransferManagerInterop.GetForWindow(_anchor.Handle).DataRequested += OnDataRequested;
        }
        _anchor.Present();
        DataTransferManagerInterop.ShowShareUIForWindow(_anchor.Handle);
    }

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var data = args.Request.Data;
        data.Properties.Title = _title;
        data.Properties.Description = "Shared from Tangerine";
        data.SetStorageItems(_items, readOnly: true);
    }
}
