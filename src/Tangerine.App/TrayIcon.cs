using System.Windows;
using Forms = System.Windows.Forms;

namespace Tangerine.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;

    public TrayIcon(Action showSettings, Action<bool> setPaused, Action quit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Tangerine.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Tangerine for Windows") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        _pause = new Forms.ToolStripMenuItem("Pause wheel") { CheckOnClick = true };
        _pause.CheckedChanged += (_, _) => setPaused(_pause.Checked);
        menu.Items.Add(_pause);
        menu.Items.Add("Settings…", null, (_, _) => showSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Tangerine: drag a file, tap Shift",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) showSettings();
        };
    }

    public void SetState(bool paused, string tooltip)
    {
        _pause.Checked = paused;
        _icon.Text = paused ? "Tangerine (paused)" : tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
