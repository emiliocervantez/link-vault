using System.Drawing;
using System.Windows.Forms;

namespace LinkVault.Tray;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action showPopup, Action showSettings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show links", null, (_, _) => showPopup());
        menu.Items.Add("Settings...", null, (_, _) => showSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Text = "LinkVault",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) showPopup(); };
        _icon.DoubleClick += (_, _) => showSettings();
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && Icon.ExtractAssociatedIcon(exe) is { } icon) return icon;
        }
        catch (Exception) { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
