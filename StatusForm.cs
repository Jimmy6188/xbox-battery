using System.Drawing;
using System.Windows.Forms;

namespace XboxBattery;

/// <summary>点击托盘图标后弹出的小面板。</summary>
internal sealed class StatusForm : Form
{
    private readonly Label _title;
    private readonly Label _battery;
    private readonly Label _details;
    private readonly Label _footer;
    private readonly Func<Task> _refresh;

    public StatusForm(Func<Task> refresh)
    {
        _refresh = refresh;

        Text = "Xbox 手柄电量";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 300);
        BackColor = Color.FromArgb(30, 30, 32);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        _title = new Label
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(328, 22),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 235, 235)
        };

        _battery = new Label
        {
            AutoSize = false,
            Location = new Point(16, 40),
            Size = new Size(328, 44),
            Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 220, 140)
        };

        _details = new Label
        {
            AutoSize = false,
            Location = new Point(16, 92),
            Size = new Size(328, 160),
            ForeColor = Color.FromArgb(205, 205, 210)
        };

        _footer = new Label
        {
            AutoSize = false,
            Location = new Point(16, 258),
            Size = new Size(240, 20),
            ForeColor = Color.FromArgb(140, 140, 148)
        };

        var refreshButton = new Button
        {
            Text = "刷新",
            Location = new Point(268, 256),
            Size = new Size(76, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 56),
            ForeColor = Color.White
        };
        refreshButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 88);
        refreshButton.Click += async (_, _) =>
        {
            refreshButton.Enabled = false;
            try
            {
                await _refresh();
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        };

        Controls.Add(_title);
        Controls.Add(_battery);
        Controls.Add(_details);
        Controls.Add(_footer);
        Controls.Add(refreshButton);
    }

    public void UpdateSnapshot(ControllerSnapshot snapshot)
    {
        if (!snapshot.HasController)
        {
            _title.Text = "未检测到 Xbox 手柄";
            _battery.Text = "—";
            _battery.ForeColor = Color.FromArgb(160, 160, 165);
            _details.Text = "请用 USB 线连接手柄，或通过蓝牙 / 无线适配器配对。\r\n\r\n"
                          + $"GIP 接口：{snapshot.GipState}";
            _footer.Text = $"更新于 {snapshot.Timestamp:HH:mm:ss}";
            return;
        }

        if (snapshot.Controllers.Count == 1)
        {
            ControllerReading controller = snapshot.Controllers[0];
            _title.Text = controller.Name;
            _battery.Text = controller.BatteryText;
            _battery.ForeColor = ColorFor(controller);
            _details.Text = controller.DetailText;
        }
        else
        {
            _title.Text = $"检测到 {snapshot.Controllers.Count} 个手柄";
            _battery.Text = "";
            var lines = snapshot.Controllers
                .Select((c, i) => $"{i + 1}. {c.Name}：{c.BatteryText}（{c.Connection}）");
            _details.Text = string.Join("\r\n", lines);
        }

        _footer.Text = $"更新于 {snapshot.Timestamp:HH:mm:ss}";
    }

    public void ShowNearTray()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
        Show();
        BringToFront();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private static Color ColorFor(ControllerReading controller) => controller switch
    {
        { Charging: true } => Color.FromArgb(90, 190, 250),
        { Percent: int p } when p >= 60 => Color.FromArgb(120, 220, 140),
        { Percent: int p } when p >= 25 => Color.FromArgb(240, 190, 100),
        { Percent: int p } => Color.FromArgb(240, 120, 120),
        { Level: BatteryLevel.Full } => Color.FromArgb(120, 220, 140),
        { Level: BatteryLevel.High } => Color.FromArgb(170, 220, 130),
        { Level: BatteryLevel.Medium } => Color.FromArgb(240, 190, 100),
        { Level: BatteryLevel.Low } => Color.FromArgb(240, 120, 120),
        _ => Color.FromArgb(170, 170, 175)
    };
}
