using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Tangerine.App;

internal static class Shell
{
    public static void Open(string path) =>
        TryStart(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Opens Explorer with the given items selected.</summary>
    public static void Reveal(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        var first = paths[0].TrimEnd('\\');
        TryStart(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", first }, UseShellExecute = false });
    }

    public static void OpenTerminal(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", folder }, UseShellExecute = false });
            return;
        }
        catch (Win32Exception)
        {
            // Windows Terminal is not installed; fall back to PowerShell.
        }
        TryStart(new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoExit", "-NoLogo" }, WorkingDirectory = folder, UseShellExecute = true });
    }

    public static void InstallFfmpeg() =>
        TryStart(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/k", "winget install --id Gyan.FFmpeg -e --accept-source-agreements && echo. && echo FFmpeg installed. Close this window and try again." },
            UseShellExecute = true,
        });

    /// <summary>Runs a clipboard action, retrying when another app briefly holds the clipboard open.</summary>
    public static void WithClipboard(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(60);
            }
        }
    }

    public static string FolderOf(string path) =>
        Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;

    private static void TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Win32Exception)
        {
            // Nothing sensible to do if Explorer or the associated app cannot be launched.
        }
    }
}
