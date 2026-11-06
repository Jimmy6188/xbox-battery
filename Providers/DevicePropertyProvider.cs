using Windows.Devices.Enumeration;

namespace XboxBattery.Providers;

/// <summary>
/// 读取 Windows 设备属性中的电量信息（蓝牙设备在「设置 → 蓝牙和设备」里显示的百分比
/// 就来自这组属性）。只对部分设备有效，查询较慢，因此做 60 秒缓存。
/// </summary>
internal static class DevicePropertyProvider
{
    private const string BatteryPercentKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string BatteryStatusKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 13";
    private const string ConnectedKey = "System.Devices.Connected";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private static readonly object Sync = new();

    private static List<ControllerReading> _cache = new();
    private static DateTime _cachedAt = DateTime.MinValue;

    public static IReadOnlyList<ControllerReading> Read()
    {
        lock (Sync)
        {
            if (DateTime.Now - _cachedAt < CacheDuration)
            {
                return _cache;
            }
        }

        List<ControllerReading> result;
        try
        {
            result = Query();
        }
        catch
        {
            result = new List<ControllerReading>();
        }

        lock (Sync)
        {
            _cache = result;
            _cachedAt = DateTime.Now;
        }

        return result;
    }

    private static List<ControllerReading> Query()
    {
        var result = new List<ControllerReading>();
        var properties = new List<string> { BatteryPercentKey, BatteryStatusKey, ConnectedKey };

        DeviceInformationCollection found = DeviceInformation
            .FindAllAsync("", properties, DeviceInformationKind.Device)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (found is null)
        {
            return result;
        }

        foreach (DeviceInformation info in found)
        {
            string name = info.Name ?? "";
            string id = info.Id ?? "";
            if (name.Length == 0)
            {
                continue;
            }

            string probe = (name + " " + id).ToLowerInvariant();
            bool looksLikeController =
                probe.Contains("xbox") ||
                probe.Contains("xinput") ||
                probe.Contains("xusb") ||
                probe.Contains("game controller") ||
                probe.Contains("游戏控制器");
            if (!looksLikeController)
            {
                continue;
            }

            int? percent = null;
            bool charging = false;
            bool chargingKnown = false;
            bool connected = false;

            try
            {
                var bag = info.Properties;
                if (bag is not null)
                {
                    if (bag.TryGetValue(BatteryPercentKey, out object? percentValue))
                    {
                        percent = ToInt(percentValue);
                    }

                    if (bag.TryGetValue(BatteryStatusKey, out object? statusValue))
                    {
                        string status = statusValue?.ToString() ?? "";
                        charging = status.Contains("Charging", StringComparison.OrdinalIgnoreCase);
                        chargingKnown = status.Length > 0;
                    }

                    if (bag.TryGetValue(ConnectedKey, out object? connectedValue) && connectedValue is bool flag)
                    {
                        connected = flag;
                    }
                }
            }
            catch
            {
                // 属性缺失时跳过
            }

            // 已配对但未连接的设备会保留上次的百分比缓存，必须过滤，否则会显示假数据
            if (!connected)
            {
                continue;
            }

            if (percent is null or < 0 or > 100)
            {
                continue;
            }

            result.Add(new ControllerReading
            {
                Id = id,
                Name = name,
                Percent = percent,
                Charging = chargingKnown ? charging : null,
                Source = "设备属性",
                Connection = id.Contains("BTH", StringComparison.OrdinalIgnoreCase) ? "蓝牙" : "USB / 适配器",
                Connected = true
            });
        }

        return result;
    }

    private static int? ToInt(object? value) => value switch
    {
        null => null,
        byte b => b,
        sbyte sb => sb,
        short s => s,
        ushort us => us,
        int i => i,
        uint ui => (int)ui,
        long l => (int)l,
        ulong ul => (int)ul,
        string text when int.TryParse(text, out int parsed) => parsed,
        _ => null
    };
}
