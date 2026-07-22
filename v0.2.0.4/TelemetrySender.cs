using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace InzoneHeadsetBatteryTray;

public static class TelemetrySender
{
    private static UdpClient? _client;
    private static IPEndPoint? _endpoint;
    private static bool _enabled;
    private static bool _logged;

    public static void Init(bool enabled, int port)
    {
        _enabled = enabled;
        if (!enabled) return;
        _client = new UdpClient(AddressFamily.InterNetwork);
        _endpoint = new IPEndPoint(IPAddress.Loopback, port);
        _logged = false;
    }

    public static void Send(int headsetBattery, bool headsetCharging, int mouseBattery)
    {
        if (!_enabled || _client == null || _endpoint == null) return;
        try
        {
            var data = new
            {
                headset = headsetBattery,
                headsetCharging,
                mouse = mouseBattery,
                timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            _client.Send(bytes, bytes.Length, _endpoint);
            if (!_logged) { _logged = true; MainForm.Log($"Telemetry sent: {json}"); }
        }
        catch (Exception ex) when (!_logged) { _logged = true; MainForm.Log($"Telemetry failed: {ex.Message}"); }
        catch { }
    }
}
