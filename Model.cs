namespace XboxBattery;

/// <summary>电池种类。对应 GIP 的 batteryType 字段。</summary>
public enum BatteryKind
{
    Unknown = 0,
    Wired = 1,
    Alkaline = 2,
    RechargePack = 3
}

/// <summary>电量档位。Xbox 手柄固件只上报四档，无法得到真实百分比。</summary>
public enum BatteryLevel
{
    Unknown = -1,
    Low = 0,
    Medium = 1,
    High = 2,
    Full = 3
}

/// <summary>一个手柄的聚合读数。</summary>
public sealed class ControllerReading
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "Xbox 手柄";

    /// <summary>真实百分比，只有部分数据源能提供。</summary>
    public int? Percent { get; set; }

    public BatteryLevel Level { get; set; } = BatteryLevel.Unknown;
    public BatteryKind Kind { get; set; } = BatteryKind.Unknown;

    /// <summary>true=在充电 false=未充电 null=数据源未提供</summary>
    public bool? Charging { get; set; }

    public bool Wired { get; set; }
    public bool Connected { get; set; } = true;

    public string Connection { get; set; } = "未知";
    public string Source { get; set; } = "";

    /// <summary>原始读数细节，诊断用（如毫瓦时容量、BatteryReport 状态）。</summary>
    public string? RawDetail { get; set; }

    /// <summary>数据源刚连接、读数尚未稳定时为 true，界面应显示"读取中"而不是可能陈旧的档位。</summary>
    public bool WarmingUp { get; set; }

    public int? DisplayPercent =>
        Percent ?? (Level == BatteryLevel.Unknown ? null : LevelToPercent(Level));

    public static int LevelToPercent(BatteryLevel level) => level switch
    {
        BatteryLevel.Low => 15,
        BatteryLevel.Medium => 50,
        BatteryLevel.High => 75,
        BatteryLevel.Full => 100,
        _ => 0
    };

    public static string LevelName(BatteryLevel level) => level switch
    {
        BatteryLevel.Low => "低",
        BatteryLevel.Medium => "中",
        BatteryLevel.High => "高",
        BatteryLevel.Full => "满",
        _ => "未知"
    };

    public static string KindName(BatteryKind kind) => kind switch
    {
        BatteryKind.Wired => "有线供电",
        BatteryKind.Alkaline => "AA 干电池",
        BatteryKind.RechargePack => "充电电池组",
        _ => "未知"
    };

    /// <summary>主显示文本，严格区分"百分比"与"四档估算"。</summary>
    public string BatteryText
    {
        get
        {
            if (WarmingUp && Percent is null && Level == BatteryLevel.Unknown)
            {
                return "读取中…";
            }

            if (Percent is int exact)
            {
                return Charging == true ? $"充电中 {exact}%" : $"{exact}%";
            }

            if (Kind == BatteryKind.Wired && Level == BatteryLevel.Unknown)
            {
                return "有线供电 · 无电量数据";
            }

            if (Level != BatteryLevel.Unknown)
            {
                string approx = $"约 {LevelToPercent(Level)}%（{LevelName(Level)}）";
                return Charging == true ? $"充电中 {approx}" : approx;
            }

            return Kind == BatteryKind.Wired ? "有线供电 · 无电量数据" : "无电量数据";
        }
    }

    public string DetailText
    {
        get
        {
            bool looksFull = Level == BatteryLevel.Full || (Percent is int pct && pct >= 99);

            string charging = Charging switch
            {
                true => "充电中",
                false when Wired && looksFull => "已充满（连接电源）",
                false when Wired => "未充电（连接电源）",
                false => "未充电",
                _ when Wired && looksFull => "已充满（连接电源）",
                _ => "未知"
            };
            string precision = Percent is null
                ? "档位（手柄仅上报四档）"
                : Connection == "USB 有线"
                    ? "百分比（系统报告，有线时可能为名义值）"
                    : "百分比（系统报告）";
            string level = Level == BatteryLevel.Unknown ? "无" : $"{LevelName(Level)}（手柄上报）";

            string text = $"连接方式：{Connection}\r\n"
                        + $"电池类型：{KindName(Kind)}\r\n"
                        + $"充电状态：{charging}\r\n"
                        + $"电量档位：{level}\r\n"
                        + $"数据精度：{precision}\r\n"
                        + $"数据来源：{Source}";

            if (!string.IsNullOrEmpty(RawDetail))
            {
                text += $"\r\n原始读数：{RawDetail}";
            }

            return text;
        }
    }
}

/// <summary>一次采集的完整快照。</summary>
public sealed class ControllerSnapshot
{
    public List<ControllerReading> Controllers { get; init; } = new();
    public string GipState { get; init; } = "";

    /// <summary>GIP 0x03 原始字节，诊断用。</summary>
    public byte? GipRawByte { get; init; }

    public int GipStatusCount { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool HasController => Controllers.Count > 0;
}
