using System.Runtime.InteropServices;
using System.Text;

namespace XboxBattery;

/// <summary>
/// 命令行诊断模式：XboxBattery.exe --probe
/// 采集一次并打印每个数据源的真实返回，同时写入 probe-output.txt，
/// 便于确认 USB 有线 / 蓝牙模式下到底能读到什么。
/// </summary>
internal static class ProbeRunner
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void Run()
    {
        AttachConsole(AttachParentProcess);

        var output = new StringBuilder();
        void Line(string text = "")
        {
            output.AppendLine(text);
            Console.WriteLine(text);
        }

        Line("=== Xbox 手柄电量诊断 ===");
        Line($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"系统：{Environment.OSVersion.VersionString}");
        Line();

        using var monitor = new BatteryMonitor();
        monitor.Start();

        Line("等待 GIP 状态消息（12 秒）…");
        Thread.Sleep(12000);

        // 采两次：第一次让稳定门限建立基线，第二次取稳定值
        monitor.Collect();
        Thread.Sleep(2500);
        ControllerSnapshot snapshot = monitor.Collect();

        Line($"GIP 接口状态：{snapshot.GipState}");
        Line(snapshot.GipRawByte is byte raw
            ? $"GIP 原始状态字节：0x{raw:X2}（状态消息数 {snapshot.GipStatusCount}）"
            : "GIP 原始状态字节：无");
        Line($"检测到手柄数量：{snapshot.Controllers.Count}");
        Line();

        if (snapshot.Controllers.Count == 0)
        {
            Line("未检测到手柄。请确认：");
            Line("  1. 手柄已开机（Xbox 键常亮）");
            Line("  2. USB 线已插好，或蓝牙已配对连接");
            Line("  3. 设备管理器中没有黄色感叹号");
        }
        else
        {
            int index = 0;
            foreach (ControllerReading controller in snapshot.Controllers)
            {
                index++;
                Line($"[{index}] {controller.Name}");
                Line($"    显示文本：{controller.BatteryText}");
                Line($"    百分比字段：{(controller.Percent is int p ? p + "%" : "无")}");
                Line($"    档位字段：{ControllerReading.LevelName(controller.Level)}");
                Line($"    电池类型：{ControllerReading.KindName(controller.Kind)}");
                Line($"    充电中：{controller.Charging switch { true => "是", false => "否", _ => "未知" }}");
                Line($"    连接方式：{controller.Connection}");
                Line($"    数据来源：{controller.Source}");
                if (!string.IsNullOrEmpty(controller.RawDetail))
                {
                    Line($"    原始读数：{controller.RawDetail}");
                }
                Line($"    设备 Id：{controller.Id}");
                Line();
            }
        }

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "probe-output.txt");
            File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"结果已写入：{path}");
        }
        catch
        {
            // 输出文件写入失败不影响诊断结果
        }
    }
}
