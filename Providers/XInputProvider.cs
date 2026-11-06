using System.Runtime.InteropServices;

namespace XboxBattery.Providers;

/// <summary>
/// XInput 兜底数据源。无线适配器与部分 2.4G 接收器可用；USB 有线返回 BATTERY_TYPE_WIRED。
/// 精度只有四档。
/// </summary>
internal static class XInputProvider
{
    private const int ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(
        uint dwUserIndex, byte devType, ref XINPUT_BATTERY_INFORMATION pBatteryInformation);

    public static List<ControllerReading> Read()
    {
        var list = new List<ControllerReading>();

        for (uint index = 0; index < 4; index++)
        {
            var state = new XINPUT_STATE();
            if (XInputGetState(index, ref state) != ErrorSuccess)
            {
                continue;
            }

            var reading = new ControllerReading
            {
                Id = $"XInput_{index}",
                Name = $"Xbox 手柄 {index + 1}",
                Source = "XInput",
                Connection = "USB / 无线适配器",
                Connected = true
            };

            var info = new XINPUT_BATTERY_INFORMATION();
            if (XInputGetBatteryInformation(index, 0, ref info) == ErrorSuccess)
            {
                reading.Kind = info.BatteryType switch
                {
                    1 => BatteryKind.Wired,
                    2 => BatteryKind.Alkaline,
                    3 => BatteryKind.RechargePack,
                    _ => BatteryKind.Unknown
                };

                reading.Wired = info.BatteryType == 1;

                // XInput: 0=EMPTY 1=LOW 2=MEDIUM 3=FULL。
                // 有线模式下 0 既可能表示"空"也可能表示"不适用"，按未知处理，避免误报低电量。
                reading.Level = info.BatteryLevel switch
                {
                    0 => reading.Wired ? BatteryLevel.Unknown : BatteryLevel.Low,
                    1 => BatteryLevel.Low,
                    2 => BatteryLevel.Medium,
                    3 => BatteryLevel.Full,
                    _ => BatteryLevel.Unknown
                };
            }

            list.Add(reading);
        }

        return list;
    }
}
