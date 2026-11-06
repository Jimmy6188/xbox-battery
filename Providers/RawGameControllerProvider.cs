using Windows.Devices.Power;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace XboxBattery.Providers;

/// <summary>
/// WinRT 官方接口，是唯一可能给出"百分比"的来源：
/// BatteryReport 提供剩余/满充毫瓦时，可直接算出百分比。
/// 有线连接是否返回有效报告取决于固件，需要实测。
/// </summary>
internal static class RawGameControllerProvider
{
    public static List<ControllerReading> Read()
    {
        var list = new List<ControllerReading>();

        IReadOnlyList<RawGameController> pads;
        try
        {
            pads = RawGameController.RawGameControllers;
        }
        catch
        {
            return list;
        }

        if (pads is null)
        {
            return list;
        }

        foreach (var pad in pads)
        {
            string id = "";
            string name = "Xbox 手柄";
            ushort vendorId = 0;

            try
            {
                id = pad.NonRoamableId ?? "";
                string display = pad.DisplayName;
                if (!string.IsNullOrWhiteSpace(display))
                {
                    name = display;
                }
                vendorId = pad.HardwareVendorId;
            }
            catch
            {
                // 单个设备读取失败不影响其它设备
            }

            var reading = new ControllerReading
            {
                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
                Name = name,
                Source = "RawGameController",
                Connection = ClassifyConnection(id),
                Connected = true
            };

            try
            {
                BatteryReport? report = pad.TryGetBatteryReport();
                if (report is null)
                {
                    reading.RawDetail = "BatteryReport = null";
                }
                else
                {
                    int? remaining = report.RemainingCapacityInMilliwattHours;
                    int? full = report.FullChargeCapacityInMilliwattHours;

                    if (remaining is int r && full is int f && f > 0)
                    {
                        int percent = (int)Math.Round(r * 100.0 / f);
                        reading.Percent = Math.Clamp(percent, 0, 100);
                    }

                    reading.RawDetail = $"剩余 {remaining?.ToString() ?? "null"} mWh / 满充 {full?.ToString() ?? "null"} mWh / 状态 {report.Status}";

                    BatteryStatus status = report.Status;
                    if (status == BatteryStatus.Charging)
                    {
                        reading.Charging = true;
                    }
                    else if (status is BatteryStatus.Discharging or BatteryStatus.Idle)
                    {
                        reading.Charging = false;
                    }

                    if (status == BatteryStatus.NotPresent)
                    {
                        reading.Kind = BatteryKind.Wired;
                        reading.Wired = true;
                    }
                }
            }
            catch
            {
                // 部分设备不支持 BatteryReport
            }

            if (vendorId != 0 && vendorId != 0x045E)
            {
                reading.Name = $"{name}（非微软设备）";
            }

            list.Add(reading);
        }

        return list;
    }

    private static string ClassifyConnection(string id)
    {
        if (id.Contains("BTH", StringComparison.OrdinalIgnoreCase))
        {
            return "蓝牙";
        }
        if (id.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB 有线";
        }
        if (id.Contains("HID", StringComparison.OrdinalIgnoreCase))
        {
            return "USB / 适配器";
        }
        return "未知";
    }
}
