using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxBattery.Providers;

internal sealed class GipDevice
{
    public ulong DeviceId { get; init; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public BatteryLevel Level { get; set; } = BatteryLevel.Unknown;
    public BatteryKind Kind { get; set; } = BatteryKind.Unknown;
    public bool Connected { get; set; }

    /// <summary>收到的 0x03 状态消息数量。刚连接时手柄可能先发一条初始值，需要过滤。</summary>
    public int StatusCount { get; set; }

    public byte LastRawByte { get; set; }

    public DateTime LastSeen { get; set; } = DateTime.Now;
}

/// <summary>
/// 直读 Windows 的 GIP 接口（\\.\XboxGIP）。
/// 该接口由 xboxgip.sys 提供，同时覆盖 USB 有线与无线手柄，
/// 周期性下发 0x03 Device Status（含 batteryLevel / batteryType）。
/// 协议已于 2024-09 由微软发布为开放标准 MS-GIPUSB。
/// </summary>
internal sealed class GipProvider : IDisposable
{
    private const uint GenericReadWrite = 0xC0000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint IoctlAddReenumerateCallerContext = 0x40001CD0;
    private const int ErrorNoMoreItems = 259;

    private const int HeaderSize = 20;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    private readonly ConcurrentDictionary<ulong, GipDevice> _devices = new();
    private readonly object _sync = new();

    private Thread? _thread;
    private volatile bool _running;
    private volatile string _state = "未启动";
    private SafeFileHandle? _handle;

    public string State => _state;

    public void Start()
    {
        lock (_sync)
        {
            if (_running)
            {
                return;
            }
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "GIP reader" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _handle?.Close();
        }
        catch
        {
            // 忽略关闭异常
        }

        try
        {
            _thread?.Join(1500);
        }
        catch
        {
            // 忽略线程等待异常
        }
    }

    public IReadOnlyList<GipDevice> Snapshot()
    {
        DateTime now = DateTime.Now;
        foreach (var pair in _devices.ToArray())
        {
            if (now - pair.Value.LastSeen > TimeSpan.FromSeconds(20))
            {
                _devices.TryRemove(pair.Key, out _);
            }
        }

        return _devices.Values.ToList();
    }

    private void Loop()
    {
        while (_running)
        {
            SafeFileHandle? handle = null;
            try
            {
                handle = CreateFileW(
                    @"\\.\XboxGIP", GenericReadWrite, FileShareReadWrite,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    _state = error switch
                    {
                        2 => "无 GIP 设备",
                        5 => "访问被拒绝",
                        _ => $"打开失败（{error}）"
                    };
                    handle.Dispose();
                    handle = null;
                    Thread.Sleep(1200);
                    continue;
                }

                _handle = handle;
                _state = "已连接";

                try
                {
                    DeviceIoControl(handle, IoctlAddReenumerateCallerContext,
                        IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                }
                catch
                {
                    // 该 IOCTL 只影响设备重新枚举，失败不影响后续读取
                }

                var buffer = new byte[2048];
                while (_running && !handle.IsInvalid)
                {
                    bool ok = ReadFile(handle, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero);
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != ErrorNoMoreItems)
                        {
                            break;
                        }
                        Thread.Sleep(50);
                        continue;
                    }

                    if (read > 0)
                    {
                        Parse(buffer, (int)read);
                    }
                    else
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            catch
            {
                // 任何异常都退化为重新打开
            }
            finally
            {
                try
                {
                    handle?.Dispose();
                }
                catch
                {
                    // 忽略
                }

                _handle = null;
                _state = _running ? "无 GIP 设备" : "已停止";
            }

            if (_running)
            {
                Thread.Sleep(800);
            }
        }
    }

    private void Parse(byte[] buffer, int length)
    {
        int offset = 0;

        while (offset + HeaderSize <= length)
        {
            ulong deviceId = BitConverter.ToUInt64(buffer, offset);
            byte command = buffer[offset + 8];
            uint payloadLength = BitConverter.ToUInt32(buffer, offset + 12);

            // 单条消息上限保护，防止长度字段异常时死循环
            if (payloadLength > 1024)
            {
                break;
            }

            int total = HeaderSize + (int)payloadLength;
            if (offset + total > length)
            {
                break;
            }

            GipDevice device = _devices.GetOrAdd(deviceId, id => new GipDevice { DeviceId = id });

            switch (command)
            {
                case 0x02 when payloadLength >= 12:
                    // Device Arrival: header + deviceId(8) + vid(2) + pid(2) + firmware(8) + ...
                    device.VendorId = BitConverter.ToUInt16(buffer, offset + 28);
                    device.ProductId = BitConverter.ToUInt16(buffer, offset + 30);
                    device.LastSeen = DateTime.Now;
                    break;

                case 0x03 when payloadLength >= 1:
                {
                    byte raw = buffer[offset + HeaderSize];

                    // bit0-1: batteryLevel 0=Low 1=Medium 2=High 3=Full
                    device.Level = (raw & 0x03) switch
                    {
                        0 => BatteryLevel.Low,
                        1 => BatteryLevel.Medium,
                        2 => BatteryLevel.High,
                        _ => BatteryLevel.Full
                    };

                    // bit2-3: batteryType 0=Wired 1=Standard AA 2=Rechargeable pack
                    device.Kind = ((raw >> 2) & 0x03) switch
                    {
                        0 => BatteryKind.Wired,
                        1 => BatteryKind.Alkaline,
                        2 => BatteryKind.RechargePack,
                        _ => BatteryKind.Unknown
                    };

                    device.Connected = (raw & 0x80) != 0;
                    device.LastRawByte = raw;
                    device.StatusCount++;
                    device.LastSeen = DateTime.Now;
                    break;
                }
            }

            offset += total;
        }
    }

    public void Dispose() => Stop();
}
