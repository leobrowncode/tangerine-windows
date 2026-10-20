using System.Collections.Specialized;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tangerine.App.Jobs;
using Tangerine.App.Wheel;
using Tangerine.Core;

namespace Tangerine.App.Actions;

/// <summary>The Control-key wheel: actions that work on any file or folder and do not convert anything.</summary>
internal sealed class FileActions(JobRunner runner)
{
    private readonly ShareHost _share = new();

    public IReadOnlyList<WheelItem> Items =>
    [
        new() { Label = "Share", Title = "Share with Nearby Sharing or an app", Glyph = "\uE72D", Invoke = Share },
        new() { Label = "Float", Title = "Keep on screen to drag later", Glyph = "\uE718", Invoke = Float },
        new() { Label = "Copy", Title = "Copy to clipboard", Glyph = "\uE8C8", Invoke = Copy },
        new() { Label = "Move", Title = "Move to a folder…", Glyph = "\uE8DE", Invoke = Move },
        new() { Label = "Copy path", Title = "Copy as path", Glyph = "\uE71B", Invoke = CopyPath },
        new() { Label = "Terminal", Title = "Open Terminal here", Glyph = "\uE756", Invoke = Terminal },
    ];

    private async Task Share(IReadOnlyList<string> files)
    {
        try
        {
            await _share.ShareAsync(files);
        }
        catch (Exception ex)
        {
            runner.NotifyFailure("Share", JobRunner.Describe(files), "Sharing isn't available: " + ex.Message);
        }
    }

    private Task Float(IReadOnlyList<string> files)
    {
        new FloatWindow(files).Show();
        return Task.CompletedTask;
    }

    private Task Copy(IReadOnlyList<string> files)
    {
        var list = new StringCollection();
        list.AddRange(files.ToArray());
        Shell.WithClipboard(() =>
        {
            var data = new DataObject();
            data.SetFileDropList(list);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
            Clipboard.SetDataObject(data, copy: true);
        });
        runner.Notify("Copied to clipboard", JobRunner.Describe(files), status: "Paste anywhere with Ctrl+V");
        return Task.CompletedTask;
    }

    private Task CopyPath(IReadOnlyList<string> files)
    {
        var text = string.Join(Environment.NewLine, files.Select(f => $"\"{f}\""));
        Shell.WithClipboard(() => Clipboard.SetText(text));
        runner.Notify("Path copied", JobRunner.Describe(files), status: files.Count == 1 ? files[0] : $"{files.Count} paths");
        return Task.CompletedTask;
    }

    private Task Terminal(IReadOnlyList<string> files)
    {
        Shell.OpenTerminal(Shell.FolderOf(files[0]));
        return Task.CompletedTask;
    }

    private async Task Move(IReadOnlyList<string> files)
    {
        var dialog = new OpenFolderDialog { Title = "Move to…", InitialDirectory = Shell.FolderOf(files[0]) };
        var owner = new AnchorWindow();
        bool? chosen;
        try
        {
            owner.Present();
            chosen = dialog.ShowDialog(owner);
        }
        finally
        {
            owner.Close();
        }
        if (chosen != true) return;
        var destination = dialog.FolderName;

        var moved = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                foreach (var source in files)
                {
                    var trimmed = source.TrimEnd('\\');
                    var target = PathNames.Unique(Path.Combine(destination, Path.GetFileName(trimmed)));
                    if (string.Equals(Path.GetDirectoryName(trimmed), destination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.Exists(trimmed)) MoveDirectory(trimmed, target);
                    else File.Move(trimmed, target);
                    moved.Add(target);
                }
            });
            runner.Notify("Moved", JobRunner.Describe(files), moved, $"Moved to {Path.GetFileName(destination.TrimEnd('\\'))}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            runner.NotifyFailure("Move", JobRunner.Describe(files), ex.Message);
        }
    }

    private static void MoveDirectory(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException) when (!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
        {
            // Directory.Move cannot cross volumes; copy then delete instead.
            CopyDirectory(source, target);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
