using System.Diagnostics;

namespace NexusDisplay;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppLog.Initialize();
        AppLog.Info("APP", $"start v{BuildInfo.DisplayVersion}");
        ApplicationConfiguration.Initialize();

        if (args.Contains("--install-startup", StringComparer.OrdinalIgnoreCase))
        {
            ShowStartupResult(StartupManager.SetEnabled(true), true);
            return;
        }

        if (args.Contains("--remove-startup", StringComparer.OrdinalIgnoreCase))
        {
            ShowStartupResult(StartupManager.SetEnabled(false), false);
            return;
        }

        bool background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, @"Global\NexusDisplayAgent", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (!background)
                MessageBox.Show("NEXUS 已在后台运行。", "NEXUS Display", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var context = new TrayApplicationContext(background);
        Application.Run(context);
    }

    private static void ShowStartupResult(bool success, bool enabled)
    {
        string action = enabled ? "启用" : "关闭";
        MessageBox.Show(success ? $"已{action}开机自启。" : $"无法{action}开机自启，请查看日志。",
            "NEXUS Display", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }
}

internal sealed class TrayApplicationContext : ApplicationContext, IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _portItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly TelemetryWorker _worker;
    private readonly SynchronizationContext _ui;
    private string _lastStatus = "正在启动";
    private bool _disposed;

    public TrayApplicationContext(bool background)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = SynchronizationContext.Current!;
        _worker = new TelemetryWorker();

        _statusItem = new ToolStripMenuItem("状态：正在启动") { Enabled = false };
        _portItem = new ToolStripMenuItem("串口：自动检测") { Enabled = false };
        _startupItem = new ToolStripMenuItem("开机自启") { Checked = StartupManager.IsEnabled(), CheckOnClick = false };
        _startupItem.Click += (_, _) => ToggleStartup();

        var versionItem = new ToolStripMenuItem($"版本：v{BuildInfo.DisplayVersion}") { Enabled = false };
        _updateItem = new ToolStripMenuItem("检查更新…");
        _updateItem.Click += async (_, _) => await CheckForUpdatesAsync();

        var reconnectItem = new ToolStripMenuItem("立即重连");
        reconnectItem.Click += (_, _) => _worker.RequestReconnect();
        var logsItem = new ToolStripMenuItem("打开日志目录");
        logsItem.Click += (_, _) => OpenLogFolder();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] {
            _statusItem, _portItem, versionItem, new ToolStripSeparator(), _startupItem,
            reconnectItem, _updateItem, logsItem, new ToolStripSeparator(), exitItem
        });

        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Information.Clone();
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "NEXUS Display - 正在启动",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowCurrentStatus();

        _worker.StatusChanged += status => _ui.Post(_ => ApplyStatus(status), null);
        _worker.Start();

        if (!background)
        {
            _tray.BalloonTipTitle = "NEXUS Display 已启动";
            _tray.BalloonTipText = "正在自动连接 ESP32-P4；右键托盘图标可管理开机自启。";
            _tray.ShowBalloonTip(3500);
        }
    }

    private void ApplyStatus(AgentStatus status)
    {
        _lastStatus = status.Message;
        _statusItem.Text = $"状态：{status.Message}";
        _portItem.Text = status.Port is null ? "串口：自动检测" : $"串口：{status.Port}";
        string tooltip = status.Connected ? $"NEXUS Display - 已连接 {status.Port}" : $"NEXUS Display - {status.Message}";
        _tray.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private void ShowCurrentStatus()
    {
        _tray.BalloonTipTitle = "NEXUS Display";
        _tray.BalloonTipText = _lastStatus;
        _tray.ShowBalloonTip(2500);
    }

    private void ToggleStartup()
    {
        bool target = !_startupItem.Checked;
        bool success = StartupManager.SetEnabled(target);
        _startupItem.Checked = success ? target : StartupManager.IsEnabled();
        _tray.BalloonTipTitle = "开机自启";
        _tray.BalloonTipText = success ? (target ? "已启用" : "已关闭") : "操作失败，请查看日志";
        _tray.ShowBalloonTip(2000);
    }

    private async Task CheckForUpdatesAsync()
    {
        _updateItem.Enabled = false;
        _updateItem.Text = "正在检查更新…";
        try
        {
            if (await UpdateManager.CheckAndPrepareAsync())
                ExitThread();
        }
        finally
        {
            if (!_disposed)
            {
                _updateItem.Text = "检查更新…";
                _updateItem.Enabled = true;
            }
        }
    }

    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(AppLog.DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", AppLog.DirectoryPath) { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        Dispose();
        base.ExitThreadCore();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppLog.Info("APP", "stop");
        _worker.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _appIcon.Dispose();
        base.Dispose();
    }
}
