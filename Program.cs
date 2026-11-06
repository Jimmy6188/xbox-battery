using System.Threading;

namespace XboxBattery;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase)))
        {
            ProbeRunner.Run();
            return;
        }

        _mutex = new Mutex(true, "XboxBattery.SingleInstance.v1", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
        }
        catch
        {
            // WinRT 初始化失败时仍可运行，XInput / GIP 路径不受影响
        }

        Application.Run(new TrayApp());
    }
}
