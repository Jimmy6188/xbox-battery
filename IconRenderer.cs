using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace XboxBattery;

/// <summary>运行时绘制托盘图标：电池轮廓 + 电量填充 + 充电闪电。</summary>
internal static class IconRenderer
{
    private static readonly ConcurrentDictionary<string, Icon> Cache = new();

    public static Icon Get(int? percent, bool charging, bool wired, bool connected)
    {
        string key = connected
            ? $"{Bucket(percent)}|{charging}|{wired}"
            : "off";

        return Cache.GetOrAdd(key, _ => Build(percent, charging, wired, connected));
    }

    private static string Bucket(int? percent) => percent is null ? "na" : (percent.Value / 5).ToString();

    private static Icon Build(int? percent, bool charging, bool wired, bool connected)
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color color = !connected
                ? Color.FromArgb(150, 150, 150)
                : charging
                    ? Color.FromArgb(0, 170, 255)
                    : percent is null
                        ? Color.FromArgb(185, 185, 185)
                        : percent >= 60
                            ? Color.FromArgb(72, 199, 96)
                            : percent >= 25
                                ? Color.FromArgb(240, 175, 60)
                                : Color.FromArgb(232, 74, 74);

            using var pen = new Pen(color, 2.2f);
            Rectangle body = new(4, 9, 22, 14);
            using (GraphicsPath path = RoundedRect(body, 3))
            {
                g.DrawPath(pen, path);
            }

            // 电池正极
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, 26, 13, 3, 6);
            }

            if (percent is int value)
            {
                int innerWidth = (int)Math.Round(18 * Math.Clamp(value, 0, 100) / 100.0);
                if (innerWidth > 0)
                {
                    using var brush = new SolidBrush(color);
                    g.FillRectangle(brush, 6, 11, innerWidth, 10);
                }
            }
            else if (wired)
            {
                using var brush = new SolidBrush(Color.FromArgb(130, color));
                g.FillRectangle(brush, 6, 11, 18, 10);
            }
            else if (connected)
            {
                using var brush = new SolidBrush(Color.FromArgb(70, color));
                g.FillRectangle(brush, 6, 11, 6, 10);
            }

            if (charging)
            {
                using var bolt = new SolidBrush(Color.White);
                g.FillPolygon(bolt, new[]
                {
                    new PointF(17, 5), new PointF(10, 17), new PointF(15, 17),
                    new PointF(13, 27), new PointF(22, 14), new PointF(16, 14)
                });
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
