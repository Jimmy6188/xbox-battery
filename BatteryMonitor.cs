using XboxBattery.Providers;

namespace XboxBattery;

/// <summary>
/// 多源聚合：RawGameController（百分比） → 设备属性（百分比）
/// → GIP（四档 + 电池类型）→ XInput（四档兜底）。
/// 原则：能拿到百分比就显示百分比，只有四档就标"约"，都没有就明确写"无电量数据"。
/// </summary>
internal sealed class BatteryMonitor : IDisposable
{
    private readonly GipProvider _gip = new();
    private readonly Dictionary<string, (BatteryLevel Level, int Count)> _stability = new();
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _gip.Start();
    }

    public void Stop()
    {
        _gip.Stop();
    }

    public ControllerSnapshot Collect()
    {
        List<ControllerReading> raw = RawGameControllerProvider.Read();
        List<ControllerReading> xinput = XInputProvider.Read();
        IReadOnlyList<ControllerReading> properties = DevicePropertyProvider.Read();
        IReadOnlyList<GipDevice> gip = _gip.Snapshot();

        var controllers = new List<ControllerReading>();

        if (raw.Count > 0)
        {
            controllers.AddRange(raw);
        }
        else if (xinput.Count > 0)
        {
            controllers.AddRange(xinput);
        }
        else if (gip.Count > 0)
        {
            controllers.AddRange(gip.Select(ToReading));
        }

        // 注意：设备属性不作为独立来源。
        // 已配对但未连接的蓝牙设备会保留上次电量缓存，单独使用会显示假数据。
        // 它只用于给已确认在线的手柄补充百分比。

        // 用设备属性补百分比（按名称或 Id 粗略匹配）
        foreach (ControllerReading controller in controllers)
        {
            ControllerReading? match = properties.FirstOrDefault(p => IsSameDevice(p, controller));
            if (match?.Percent is int percent)
            {
                controller.Percent ??= percent;
                if (match.Charging is bool charging)
                {
                    controller.Charging ??= charging;
                }
            }
        }

        // GIP 是手柄自身的状态上报，优先级高于 XInput 的粗粒度缓存。
        // 但刚连接时手柄可能先发一条初始状态，StatusCount < 2 时不采信，避免误报低电量。
        GipDevice? gipDevice = gip
            .Where(d => d.StatusCount >= 2 && DateTime.Now - d.LastSeen < TimeSpan.FromSeconds(10))
            .OrderByDescending(d => d.LastSeen)
            .FirstOrDefault();

        if (controllers.Count == 1 && gipDevice is not null)
        {
            ControllerReading controller = controllers[0];

            if (gipDevice.Level != BatteryLevel.Unknown)
            {
                controller.Level = gipDevice.Level;
            }

            if (gipDevice.Kind != BatteryKind.Unknown)
            {
                controller.Kind = gipDevice.Kind;
            }

            if (!controller.Source.Contains("GIP", StringComparison.Ordinal))
            {
                controller.Source = $"GIP + {controller.Source}";
            }
        }

        // 连接方式以 PnP 在线状态为准。
        // 注意：XInput 的 BatteryType 表示电池类型（ALKALINE/NIMH），不是连接方式，
        // 实测有线连接时它也会报 NIMH，因此不能用它判断。
        bool usbPresent = UsbPresenceProbe.IsUsbControllerPresent();
        bool bluetoothPresent = properties.Any(p => p.Connection == "蓝牙");

        foreach (ControllerReading controller in controllers)
        {
            if (usbPresent)
            {
                controller.Connection = "USB 有线";
                controller.Wired = true;
            }
            else if (bluetoothPresent || controller.Id.Contains("BTH", StringComparison.OrdinalIgnoreCase))
            {
                controller.Connection = "蓝牙";
                controller.Wired = false;
            }
            else if (controller.Connection is "未知" or "" or "USB / 无线适配器")
            {
                controller.Connection = "无线（适配器）";
                controller.Wired = false;
            }
        }

        // 稳定门限：同一档位连续出现两次才采信。
        // 手柄刚连接时数据源可能先返回一个陈旧值（实测会出现"低"但实际满电）。
        foreach (ControllerReading controller in controllers)
        {
            ControllerReading reading = controller;
            BatteryLevel rawLevel = reading.Level;
            BatteryLevel stable = Stabilize(reading.Id, rawLevel);

            if (stable == BatteryLevel.Unknown && rawLevel != BatteryLevel.Unknown)
            {
                reading.WarmingUp = true;
                reading.Level = BatteryLevel.Unknown;
            }
        }

        return new ControllerSnapshot
        {
            Controllers = controllers,
            GipState = _gip.State,
            GipRawByte = gipDevice?.LastRawByte,
            GipStatusCount = gipDevice?.StatusCount ?? 0
        };
    }

    private BatteryLevel Stabilize(string id, BatteryLevel level)
    {
        if (level == BatteryLevel.Unknown)
        {
            _stability.Remove(id);
            return BatteryLevel.Unknown;
        }

        if (!_stability.TryGetValue(id, out (BatteryLevel Level, int Count) state) || state.Level != level)
        {
            _stability[id] = (level, 1);
            return BatteryLevel.Unknown;
        }

        state.Count++;
        _stability[id] = state;
        return state.Count >= 2 ? level : BatteryLevel.Unknown;
    }

    private static ControllerReading ToReading(GipDevice device) => new()
    {
        Id = $"GIP_{device.DeviceId:X16}",
        Name = device.ProductId != 0
            ? $"Xbox 手柄（{device.VendorId:X4}:{device.ProductId:X4}）"
            : "Xbox 手柄",
        Level = device.Level,
        Kind = device.Kind,
        Wired = device.Kind == BatteryKind.Wired,
        Connected = device.Connected,
        Connection = device.Kind == BatteryKind.Wired ? "USB 有线" : "无线",
        Source = "GIP"
    };

    private static bool IsSameDevice(ControllerReading a, ControllerReading b)
    {
        if (a.Id.Length > 0 && b.Id.Length > 0 && string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(a.Name)
            && !string.IsNullOrWhiteSpace(b.Name)
            && a.Name.Contains(b.Name, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Stop();
}
