using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AccentHold.Core;
using Microsoft.Win32;

namespace AccentHold.UI;

// Notification-area icon with the enable/startup/quit menu.
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(HoldController controller, Settings settings, Action quit)
    {
        var enabled = new ToolStripMenuItem("Enabled") { Checked = true, CheckOnClick = true };
        enabled.CheckedChanged += (_, _) => controller.Enabled = enabled.Checked;

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartupManager.IsEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => StartupManager.SetEnabled(startup.Checked);

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => settings.OpenInEditor();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => quit();

        var menu = new ContextMenuStrip();
        menu.Items.Add(enabled);
        menu.Items.Add(startup);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _icon = new NotifyIcon
        {
            Text = "AccentHold — hold a letter to accent it",
            ContextMenuStrip = menu,
            Icon = DrawIcon(),
            Visible = true,
        };
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var old = _icon.Icon;
        _icon.Icon = DrawIcon();
        old?.Dispose();
    }

    private static Icon DrawIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        using var font = new Font("Segoe UI Semibold", 22f, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
        var brush = IsTaskbarLight() ? Brushes.Black : Brushes.White;
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("é", font, brush, new RectangleF(0, 0, 32, 32), fmt);
        var handle = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(handle);
            return (Icon)tmp.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    private static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
