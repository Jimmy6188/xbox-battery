using System.Diagnostics;
using System.Windows.Forms;

namespace XboxBattery;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly BatteryMonitor _monitor = new();

    private StatusForm? _form;
    private ControllerSnapshot _snapshot = new();
    private bool _refreshing;
    private int _lastLowNotification = -1;

    public TrayApp()
    {
        _monitor.Start();

        _menu = new ContextMenuStrip();

        var detailsItem = new ToolStripMenuItem("显示详情");
        detailsItem.Click += (_, _) => ShowDetails();

        var autoStartItem = new ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled()
        };
        autoStartItem.Click += (_, _) =>
        {
            try
            {
                AutoStart.Set(autoStartItem.Checked);
            }
            catch (Exception ex)
            {
                ShowError($"设置开机自启失败：{ex.Message}");
            }
        };

        var refreshItem = new ToolStripMenuItem("立即刷新");
        refreshItem.Click += async (_, _) => await RefreshAsync(force: true);

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();

        _menu.Items.Add(detailsItem);
        _menu.Items.Add(refreshItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(autoStartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = IconRenderer.Get(null, false, false, false),
            Visible = true,
            Text = "Xbox 手柄电量：未检测到",
            ContextMenuStrip = _menu
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowDetails();
            }
        };

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += async (_, _) => await RefreshAsync(force: false);
        _timer.Start();

        _ = RefreshAsync(force: true);
    }

    private async Task RefreshAsync(bool force)
    {
        if (_refreshing && !force)
        {
            return;
        }

        _refreshing = true;
        try
        {
            ControllerSnapshot snapshot = await Task.Run(() => _monitor.Collect());
            _snapshot = snapshot;
            Apply(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(ControllerSnapshot snapshot)
    {
        if (!snapshot.HasController)
        {
            _tray.Icon = IconRenderer.Get(null, false, false, false);
            _tray.Text = "Xbox 手柄电量：未检测到";
        }
        else
        {
            ControllerReading primary = snapshot.Controllers[0];
            _tray.Icon = IconRenderer.Get(primary.DisplayPercent, primary.Charging == true, primary.Wired, true);
            _tray.Text = Truncate($"{primary.Name}：{primary.BatteryText}", 63);
            NotifyIfLow(primary);
        }

        _form?.UpdateSnapshot(snapshot);
    }

    private void NotifyIfLow(ControllerReading controller)
    {
        if (controller.Charging == true)
        {
            _lastLowNotification = -1;
            return;
        }

        int? percent = controller.DisplayPercent;
        if (percent is not int value || value > 20)
        {
            if (percent is int high && high > 25)
            {
                _lastLowNotification = -1;
            }
            return;
        }

        int bucket = value / 5;
        if (bucket == _lastLowNotification)
        {
            return;
        }

        _lastLowNotification = bucket;
        _tray.BalloonTipTitle = "Xbox 手柄电量偏低";
        _tray.BalloonTipText = $"{controller.Name}：{controller.BatteryText}";
        _tray.BalloonTipIcon = ToolTipIcon.Warning;
        _tray.ShowBalloonTip(5000);
    }

    private void ShowDetails()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new StatusForm(() => RefreshAsync(force: true));
        }

        _form.UpdateSnapshot(_snapshot);
        _form.ShowNearTray();
    }

    private void ShowError(string message)
    {
        _tray.BalloonTipTitle = "Xbox 手柄电量";
        _tray.BalloonTipText = message;
        _tray.BalloonTipIcon = ToolTipIcon.Error;
        _tray.ShowBalloonTip(4000);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private void Shutdown()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _form?.Dispose();
        _monitor.Dispose();
        ExitThread();
    }
}
