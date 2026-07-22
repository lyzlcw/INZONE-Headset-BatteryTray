using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace InzoneHeadsetBatteryTray;

public class RapooHidReader : IBatteryReader
{
    private const int RapooVid = 0x24AE;
    private const ushort TargetUsagePage = 0xFF00;
    private const ushort TargetUsage = 0x0002;
    private const int BatteryIndex = 8;

    private SafeFileHandle? _devHandle;
    private SafeFileHandle? _readEvent;
    private SafeFileHandle? _writeEvent;
    private string? _cachedPath;
    private int _writeLen = 19;
    private int _readLen = 64;

    private const int MaxLogSize = 64 * 1024;
    private static string LogPath => Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "raphid.log");

    private static FileStream? _logStream;
    private static long _logPosition;
    private static readonly object LogLock = new();

    private static void InitLog()
    {
        if (_logStream != null) return;
        lock (LogLock)
        {
            if (_logStream != null) return;
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _logStream = new FileStream(LogPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                _logPosition = _logStream.Length;
                if (_logPosition > MaxLogSize)
                { _logPosition = 0; _logStream.SetLength(MaxLogSize); _logStream.Seek(0, SeekOrigin.Begin); }
            }
            catch { }
        }
    }

    private void Log(string msg)
    {
        InitLog();
        if (_logStream == null) return;
        lock (LogLock)
        {
            try
            {
                string text = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                if (_logPosition + bytes.Length > MaxLogSize)
                { _logPosition = 0; _logStream.Seek(0, SeekOrigin.Begin); }
                _logStream.Write(bytes, 0, bytes.Length);
                _logPosition += bytes.Length;
                _logStream.Flush();
            }
            catch { }
        }
    }

    public string Method => "HID";

    public (int battery, bool charging) Read()
    {
        if (_devHandle == null || _devHandle.IsInvalid)
        {
            if (!FindAndOpen()) return (-1, false);
        }

        byte[] inBuf = new byte[_readLen];

        // 1) quick read — maybe periodic report already queued
        int read = ReadOverlapped(inBuf, 200);
        if (read > 0) { int b = ParseBattery(inBuf, read); if (b >= 0) return (b, false); }

        // 2) active write + read
        byte[] outBuf = new byte[_writeLen];
        outBuf[0] = 0x07;
        outBuf[1] = 0x01;
        if (WriteOverlapped(outBuf, 500))
        {
            Thread.Sleep(100);
            read = ReadOverlapped(inBuf, 300);
            if (read > 0) { int b = ParseBattery(inBuf, read); if (b >= 0) return (b, false); }
        }

        // 3) passive: wait for next periodic report
        read = ReadOverlapped(inBuf, 3500);
        if (read > 0) { int b = ParseBattery(inBuf, read); if (b >= 0) return (b, false); }

        CloseDevice();
        return (-1, false);
    }

    private static int ParseBattery(byte[] buf, int len)
    {
        if (len <= BatteryIndex) return -1;
        int batt = buf[BatteryIndex];
        return batt >= 0 && batt <= 100 ? batt : -1;
    }

    private bool WriteOverlapped(byte[] buf, int timeoutMs)
    {
        EnsureEvent(ref _writeEvent);
        var over = default(NATIVE_OVERLAPPED);
        over.hEvent = _writeEvent!.DangerousGetHandle();

        if (WriteFile(_devHandle!, buf, buf.Length, out int written, ref over))
            return written == buf.Length;

        if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
            return false;

        if (WaitForSingleObject(over.hEvent, (uint)timeoutMs) == WAIT_OBJECT_0)
        {
            GetOverlappedResult(_devHandle!, ref over, out written, false);
            return written == buf.Length;
        }

        CancelIo(_devHandle!);
        return false;
    }

    private int ReadOverlapped(byte[] buf, int timeoutMs)
    {
        EnsureEvent(ref _readEvent);
        var over = default(NATIVE_OVERLAPPED);
        over.hEvent = _readEvent!.DangerousGetHandle();

        if (ReadFile(_devHandle!, buf, buf.Length, out int read, ref over))
            return read;

        if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
            return -1;

        if (WaitForSingleObject(over.hEvent, (uint)timeoutMs) == WAIT_OBJECT_0)
        {
            GetOverlappedResult(_devHandle!, ref over, out read, false);
            return read;
        }

        CancelIo(_devHandle!);
        return -1;
    }

    private static void EnsureEvent(ref SafeFileHandle? evt)
    {
        if (evt == null || evt.IsInvalid)
            evt = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        else
            ResetEvent(evt);
    }

    private bool FindAndOpen()
    {
        CloseDevice();

        if (_cachedPath != null)
        {
            Log($"Trying cached path: {_cachedPath}");
            _devHandle = CreateFile(_cachedPath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (!_devHandle.IsInvalid)
            {
                Log("Cached path OK");
                return true;
            }
            Log($"Cached path failed, err={Marshal.GetLastWin32Error()}");
            _cachedPath = null;
        }

        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr devInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devInfoSet == (IntPtr)(-1)) return false;

        try
        {
            var ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            for (uint i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, i, ref ifData))
                    break;

                SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifData, IntPtr.Zero, 0,
                    out uint reqSize, IntPtr.Zero);
                if (reqSize == 0) continue;

                IntPtr detailBuf = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifData, detailBuf,
                        reqSize, out _, IntPtr.Zero))
                        continue;

                    string? devPath = Marshal.PtrToStringUni(detailBuf + 4);
                    if (devPath == null) continue;

                    using SafeFileHandle probe = CreateFile(devPath,
                        GenericRead | GenericWrite,
                        FileShareRead | FileShareWrite,
                        IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (probe.IsInvalid) continue;

                    var attrs = new HIDD_ATTRIBUTES();
                    attrs.Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>();
                    if (!HidD_GetAttributes(probe, ref attrs)) continue;
                    if (attrs.VendorID != RapooVid) continue;

                    if (!HidD_GetPreparsedData(probe, out IntPtr prep)) continue;
                    var caps = default(HIDP_CAPS);
                    HidP_GetCaps(prep, ref caps);
                    HidD_FreePreparsedData(prep);
                    if (caps.UsagePage != TargetUsagePage || caps.Usage != TargetUsage)
                        continue;

                    _writeLen = caps.OutputReportByteLength > 0 ? caps.OutputReportByteLength : 19;
                    _readLen = caps.InputReportByteLength > 0 ? caps.InputReportByteLength : 64;
                    _cachedPath = devPath;
                    _devHandle = CreateFile(devPath,
                        GenericRead | GenericWrite,
                        FileShareRead | FileShareWrite,
                        IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                    Log("Rapoo VT3S found and opened");
                    return !_devHandle.IsInvalid;
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuf);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return false;
    }

    private void CloseDevice()
    {
        if (_devHandle != null && !_devHandle.IsInvalid)
        {
            CancelIo(_devHandle);
            _devHandle.Close();
        }
        _devHandle = null;
    }

    public void Dispose()
    {
        CloseDevice();
        _readEvent?.Close();
        _writeEvent?.Close();
    }

    // --- P/Invoke ---

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS caps);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator,
        IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
        IntPtr deviceInfoData, ref Guid classGuid, uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer,
        int nNumberOfBytesToRead, out int lpNumberOfBytesRead, ref NATIVE_OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer,
        int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, ref NATIVE_OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIo(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile,
        ref NATIVE_OVERLAPPED lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateEvent(IntPtr lpEventAttributes,
        bool bManualReset, bool bInitialState, IntPtr lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(SafeFileHandle hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint DIGCF_PRESENT = 2;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint ERROR_IO_PENDING = 997;
    private const uint WAIT_OBJECT_0 = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }
}
