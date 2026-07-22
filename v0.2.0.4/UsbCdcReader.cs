using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace InzoneHeadsetBatteryTray;

public class UsbCdcReader : IBatteryReader
{
    private SafeFileHandle? _comHandle;
    private bool _initialized;
    private static string? _cachedComPort;

    private static byte Chk(byte type, byte cmd, byte sub) => (byte)(type + cmd + sub + 0x5A);

    private static byte[] MakeCmd(byte type, byte cmd, byte sub) =>
        [0x01, 0x00, 0xFC, 0x08, 0x96, 0xC3, type, cmd, sub, 0x01, 0x00, Chk(type, cmd, sub)];

    private static readonly byte[] BatteryQuery = MakeCmd(0x41, 0x04, 0x01);
    private static readonly byte[] Cmd01Status = MakeCmd(0x21, 0x01, 0x01);
    private static readonly byte[] Cmd02Init = MakeCmd(0x41, 0x02, 0x01);

    public string Method => "USB";

    public (int battery, bool charging) Read()
    {
        if (_comHandle == null || _comHandle.IsInvalid)
        {
            if (!OpenComPort()) return (-1, false);
        }

        if (!_initialized)
        {
            byte[][] init = [Cmd01Status, Cmd02Init];
            foreach (var cmd in init)
            {
                if (!WriteFile(_comHandle!, cmd, cmd.Length, out _, IntPtr.Zero))
                {
                    CleanupCom();
                    return (-1, false);
                }
                Thread.Sleep(100);
            }
            _initialized = true;
        }

        if (!WriteFile(_comHandle!, BatteryQuery, BatteryQuery.Length, out _, IntPtr.Zero))
        {
            CleanupCom();
            return (-1, false);
        }

        var pool = ArrayPool<byte>.Shared;
        byte[] buf = pool.Rent(64);
        try
        {
            for (int retry = 0; retry < 5; retry++)
            {
                Thread.Sleep(300);
                if (ReadFile(_comHandle!, buf, buf.Length, out int read, IntPtr.Zero) && read > 0)
                {
                    for (int off = 0; off <= read - 14; off++)
                    {
                        if (buf[off] == 0x04 && buf[off + 1] == 0xFF && buf[off + 2] == 0x0B)
                        {
                            int b = buf[off + 12];
                            bool chg = buf[off + 11] == 0x01;
                            return (b is >= 0 and <= 100 ? b : -1, chg);
                        }
                    }
                }
            }
            return (-1, false);
        }
        finally
        {
            pool.Return(buf);
        }
    }

    public static void ClearCache()
    {
        _cachedComPort = null;
    }

    private static string? FindInzoneComPort()
    {
        if (_cachedComPort != null)
        {
            if (VerifyPortOpen(_cachedComPort))
                return _cachedComPort;
            _cachedComPort = null;
        }

        var candidates = new List<string>();

        try
        {
            using var usbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbKey != null)
            {
                foreach (var vidPid in usbKey.GetSubKeyNames())
                {
                    if (!vidPid.StartsWith("VID_054C&PID_0E53", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var deviceKey = usbKey.OpenSubKey(vidPid);
                    if (deviceKey == null) continue;

                    foreach (var instance in deviceKey.GetSubKeyNames())
                        CollectPortNames(deviceKey, instance, candidates);
                }
            }
        }
        catch { }

        foreach (var port in candidates)
        {
            if (VerifyPortOpen(port))
            {
                _cachedComPort = port;
                return _cachedComPort;
            }
        }
        return null;
    }

    private static void CollectPortNames(RegistryKey parent, string subKeyName, List<string> results)
    {
        using var key = parent.OpenSubKey(subKeyName);
        if (key == null) return;

        using var devParams = key.OpenSubKey("Device Parameters");
        if (devParams != null)
        {
            var portName = devParams.GetValue("PortName") as string;
            if (!string.IsNullOrEmpty(portName) && !results.Contains(portName))
                results.Add(portName);
        }

        foreach (var sub in key.GetSubKeyNames())
        {
            if (sub.StartsWith("MI_", StringComparison.OrdinalIgnoreCase))
                CollectPortNames(key, sub, results);
        }
    }

    private static bool VerifyPortOpen(string port)
    {
        var handle = CreateFile($@"\\.\{port}", GenericRead | GenericWrite, 0,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return false;
        handle.Close();
        return true;
    }

    private bool OpenComPort()
    {
        var comPort = FindInzoneComPort();
        if (comPort == null) return false;

        foreach (var proc in Process.GetProcessesByName("INZONEHub"))
        {
            proc.Kill();
            proc.WaitForExit(1000);
        }
        Thread.Sleep(500);

        _comHandle = CreateFile($@"\\.\{comPort}", GenericRead | GenericWrite, 0,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (_comHandle.IsInvalid)
        {
            _comHandle = null;
            _cachedComPort = null;
            return false;
        }

        DCB dcb = new() { DCBlength = (uint)Marshal.SizeOf<DCB>() };
        if (!BuildCommDCB("9600,n,8,1", ref dcb))
        { CleanupCom(); return false; }

        dcb.Flags |= DcbBinary | DcbDtrControlEnable | DcbRtsControlEnable;
        dcb.Flags &= ~(uint)(1 << 5);
        dcb.Flags &= ~(uint)(1 << 6);

        if (!SetCommState(_comHandle, ref dcb))
        { CleanupCom(); return false; }

        COMMTIMEOUTS timeouts = new()
        {
            ReadIntervalTimeout = 50,
            ReadTotalTimeoutConstant = 2000,
            WriteTotalTimeoutConstant = 2000
        };
        SetCommTimeouts(_comHandle, ref timeouts);
        SetupComm(_comHandle, 4096, 4096);
        PurgeComm(_comHandle, PurgeRcClear | PurgeRcAbort | PurgeTxAbort | PurgeTxClear);
        EscapeCommFunction(_comHandle, SetDtr);
        EscapeCommFunction(_comHandle, SetRts);

        _initialized = false;
        return true;
    }

    private void CleanupCom()
    {
        if (_comHandle != null && !_comHandle.IsInvalid)
            _comHandle.Close();
        _comHandle = null;
        _initialized = false;
    }

    public void Dispose()
    {
        CleanupCom();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer,
        int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer,
        int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool BuildCommDCB(string lpDef, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetupComm(SafeFileHandle hFile, int dwInQueue, int dwOutQueue);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PurgeComm(SafeFileHandle hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EscapeCommFunction(SafeFileHandle hFile, uint dwFunc);

    [StructLayout(LayoutKind.Sequential)]
    private struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;

    private const uint DcbBinary = 0x00000001;
    private const uint DcbDtrControlEnable = 0x00000010;
    private const uint DcbRtsControlEnable = 0x00001000;
    private const uint PurgeRcClear = 0x0008;
    private const uint PurgeRcAbort = 0x0001;
    private const uint PurgeTxAbort = 0x0002;
    private const uint PurgeTxClear = 0x0004;
    private const uint SetDtr = 5;
    private const uint SetRts = 3;
}
