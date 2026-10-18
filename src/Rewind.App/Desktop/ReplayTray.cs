using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Rewind.App.Desktop;

internal sealed class ReplayTray : IDisposable
{
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem toggle;
    private readonly Forms.ToolStripMenuItem save;
    private bool disposed;
    public event Action? OpenRequested;
    public event Action? ToggleRequested;
    public event Action? SaveRequested;
    public event Action? FolderRequested;
    public event Action? ExitRequested;
    public bool Visible => !disposed && icon.Visible;

    public ReplayTray()
    {
        menu.Items.Add("Open Rewind", null, (_, _) => OpenRequested?.Invoke());
        toggle = (Forms.ToolStripMenuItem)menu.Items.Add("Start buffering", null, (_, _) => ToggleRequested?.Invoke());
        save = (Forms.ToolStripMenuItem)menu.Items.Add("Save replay", null, (_, _) => SaveRequested?.Invoke());
        menu.Items.Add("Open clips folder", null, (_, _) => FolderRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        icon = new Forms.NotifyIcon { Icon = Drawing.SystemIcons.Application, Text = "Rewind · Not recording", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void Update(bool recording, bool canToggle, bool canSave, bool saving)
    {
        if (disposed) return;
        toggle.Text = recording ? "Stop buffering" : "Start buffering";
        toggle.Enabled = canToggle;
        save.Text = saving ? "Saving…" : "Save replay";
        save.Enabled = canSave;
        icon.Text = saving ? "Rewind · Saving replay" : recording ? "Rewind · Buffering" : "Rewind · Not recording";
    }

    public void Notify(string title, string message, bool error = false)
    {
        if (!disposed) icon.ShowBalloonTip(5000, title, message, error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    internal void ClickForDiagnostics(int index) => ((Forms.ToolStripMenuItem)menu.Items[index]).PerformClick();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
    }
}
