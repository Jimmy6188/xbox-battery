using System.Runtime.InteropServices;
using System.Text;

namespace XboxBattery.Providers;

/// <summary>
/// 用 SetupAPI 判断"当前是否有 Xbox 手柄通过 USB 线连接"。
/// 有线连接时系统会枚举出 USB\VID_045E&amp;PID_0B1x 且描述含 "Xbox" 的复合设备；
/// 无线适配器虽然也是 VID_045E，但描述里含 Adapter / 适配器，需要排除。
/// </summary>
internal static class UsbPresenceProbe
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfAllClasses = 0x04;
    private const uint SpdrpDeviceDesc = 0x00;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(3);
    private static readonly object Sync = new();

    private static bool _cached;
    private static DateTime _cachedAt = DateTime.MinValue;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData,
        StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, uint property,
        out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    public static bool IsUsbControllerPresent()
    {
        lock (Sync)
        {
            if (DateTime.Now - _cachedAt < CacheDuration)
            {
                return _cached;
            }
        }

        bool result;
        try
        {
            result = Query();
        }
        catch
        {
            result = false;
        }

        lock (Sync)
        {
            _cached = result;
            _cachedAt = DateTime.Now;
        }

        return result;
    }

    private static bool Query()
    {
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            return false;
        }

        try
        {
            var data = new SpDevInfoData { cbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };

            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref data); index++)
            {
                var idBuilder = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref data, idBuilder, (uint)idBuilder.Capacity, out _))
                {
                    continue;
                }

                string instanceId = idBuilder.ToString();
                if (!instanceId.Contains("VID_045E", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string description = ReadDescription(set, ref data);
                if (description.Length == 0 || !description.Contains("xbox", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 无线适配器同样是 VID_045E，必须排除
                if (description.Contains("adapter", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("适配器", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return false;
    }

    private static string ReadDescription(IntPtr set, ref SpDevInfoData data)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, SpdrpDeviceDesc,
                out _, buffer, (uint)buffer.Length, out uint required))
        {
            if (required == 0)
            {
                return "";
            }
            buffer = new byte[required];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, SpdrpDeviceDesc,
                    out _, buffer, (uint)buffer.Length, out _))
            {
                return "";
            }
        }

        string text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return text;
    }
}
