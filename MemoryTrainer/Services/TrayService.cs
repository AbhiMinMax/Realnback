using System.Windows.Forms;

namespace MemoryTrainer.Services;

public class TrayService : IDisposable
{
    private readonly SessionEngine _engine;
    private readonly System.Windows.Window _mainWindow;
    private readonly NotifyIcon _notifyIcon;

    public TrayService(SessionEngine engine, System.Windows.Window mainWindow)
    {
        _engine = engine;
        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "MemoryTrainer"
        };

        try
        {
            _notifyIcon.Icon = new System.Drawing.Icon(
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "tray_icon.ico"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayService] Could not load tray icon: {ex.Message}");
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                _notifyIcon.ContextMenuStrip = BuildContextMenu();
        };

        _mainWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            _mainWindow.Hide();
        };

        _engine.SessionStateChanged += UpdateTrayText;
    }

    private void UpdateTrayText()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_engine.HasActiveSession)
                _notifyIcon.Text = _engine.IsPaused
                    ? "MemoryTrainer — Paused"
                    : $"MemoryTrainer — {_engine.Runners.Count} cycle(s) running";
            else
                _notifyIcon.Text = "MemoryTrainer — No active session";
        });
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        string statusText = _engine.HasActiveSession
            ? (_engine.IsPaused ? "Paused" : $"Active — {_engine.Runners.Count} cycles running")
            : "No active session";
        menu.Items.Add(new ToolStripMenuItem(statusText) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        if (_engine.HasActiveSession)
        {
            if (_engine.IsPaused)
            {
                var resume = menu.Items.Add("Resume");
                resume.Click += async (_, _) => await _engine.ResumeAsync();
            }
            else
            {
                var pause = menu.Items.Add("Pause");
                pause.Click += async (_, _) => await _engine.PauseAsync();
            }

            var stop = menu.Items.Add("Stop Session");
            stop.Click += async (_, _) =>
            {
                var result = System.Windows.MessageBox.Show(
                    "Stop session? All scores are saved.", "Stop Session",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (result == System.Windows.MessageBoxResult.Yes)
                    await _engine.StopAsync();
            };
        }
        else
        {
            menu.Items.Add(new ToolStripMenuItem("Pause") { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem("Stop Session") { Enabled = false });
        }

        menu.Items.Add(new ToolStripSeparator());
        var open = menu.Items.Add("Open App");
        open.Click += (_, _) => ShowMainWindow();

        var quit = menu.Items.Add("Quit");
        quit.Click += async (_, _) =>
        {
            if (_engine.HasActiveSession)
                await _engine.StopAsync();
            _notifyIcon.Visible = false;
            System.Windows.Application.Current?.Shutdown();
        };

        return menu;
    }

    private void ShowMainWindow()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.Activate();
            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                _mainWindow.WindowState = System.Windows.WindowState.Normal;
        });
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
