using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Tangerine.App.Native;

namespace Tangerine.App;

/// <summary>
/// An invisible 1×1 topmost window at the cursor. System dialogs and the share sheet need a visible,
/// foreground owner; without one they can open behind Explorer after a drop.
/// </summary>
internal sealed class AnchorWindow : Window
{
    public AnchorWindow()
    {
        Width = 1;
        Height = 1;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Title = "Tangerine";
        Handle = new WindowInteropHelper(this).EnsureHandle();
    }

    public IntPtr Handle { get; }

    /// <summary>Shows the anchor at the cursor and makes it the foreground window.</summary>
    public void Present()
    {
        NativeMethods.GetCursorPos(out var cursor);
        Show();
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, cursor.X, cursor.Y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.ForceForeground(Handle);
    }
}
