using System.Diagnostics;
using Microsoft.Diagnostics.Runtime;

namespace InzoneHeadsetBatteryTray;

public class ClrMdReader : IBatteryReader
{
    public string Method => "ClrMD";

    public (int battery, bool charging) Read()
    {
        var hubName = FindHubProcessName();
        var procs = Process.GetProcessesByName(hubName);
        if (procs.Length == 0) return (-1, false);
        using var proc = procs[0];

        try
        {
            using var dt = DataTarget.AttachToProcess(proc.Id, true);
            using var runtime = dt.ClrVersions[0].CreateRuntime();
            var heap = runtime.Heap;

            foreach (var obj in heap.EnumerateObjects())
            {
                if (obj.Type?.Name?.Contains("PcWidgetCommunication") == true)
                {
                    var headsetParam = obj.ReadObjectField("headsetParam");
                    if (headsetParam == 0) continue;

                    var part1 = runtime.Heap.GetObject(headsetParam).ReadObjectField("part1");
                    if (part1 == 0) continue;

                    var batteryInfo = runtime.Heap.GetObject(part1).ReadObjectField("batteryInfo");
                    if (batteryInfo == 0) continue;

                    var batteryByte = runtime.Heap.GetObject(batteryInfo).ReadField<byte>("remainingBattery");
                    return (batteryByte, false);
                }
            }
        }
        catch { }
        return (-1, false);
    }

    private static string FindHubProcessName()
    {
        foreach (var name in new[] { "INZONE Hub", "INZONEHub" })
        {
            if (Process.GetProcessesByName(name).Length > 0)
                return name;
        }
        return "INZONE Hub";
    }

    public void Dispose() { }
}
