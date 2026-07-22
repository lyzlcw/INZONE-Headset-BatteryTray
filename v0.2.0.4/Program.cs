using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InzoneHeadsetBatteryTray;

static class Program
{
    [DllImport("shcore.dll")]
    static extern int SetProcessDpiAwareness(int value);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static void Main()
    {
        SetProcessDpiAwareness(2); // PerMonitorV2 (Win 8.1+)
        SetProcessDPIAware(); // fallback for older OS

        var config = Config.Load();
        config.SchemeIndex = DetectScheme();
        config.Save();
        var mainForm = new MainForm(config);
        mainForm.Run();
    }

    static int DetectScheme()
    {
        foreach (var name in new[] { "INZONE Hub", "INZONEHub" })
        {
            if (Process.GetProcessesByName(name).Length > 0)
                return 0;
        }
        return 1;
    }
}
