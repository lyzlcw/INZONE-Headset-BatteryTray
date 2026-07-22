using System.Net;
using System.Net.Sockets;
using System.Text;

namespace InzoneHeadsetBatteryTray;

public static class HttpServer
{
    private static volatile int _headset = -1;
    private static volatile bool _charging;
    private static volatile int _mouse = -1;
    private static string _timestamp = "";
    private static readonly object _lock = new();

    public static void Start(int port = 19090)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    using var client = listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    byte[] buf = new byte[4096];
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read == 0) continue;
                    string req = Encoding.UTF8.GetString(buf, 0, read);
                    string firstLine = req.Split('\r')[0];
                    string path = firstLine.Split(' ').Length > 1 ? firstLine.Split(' ')[1] : "/";

                    if (path == "/data")
                        ServeJson(stream);
                    else
                        ServeHtml(stream);
                }
            }
            catch { }
        });
    }

    public static void UpdateData(int headset, bool charging, int mouse)
    {
        lock (_lock)
        {
            _headset = headset;
            _charging = charging;
            _mouse = mouse;
            _timestamp = DateTime.Now.ToString("HH:mm:ss");
        }
    }

    private static void ServeHtml(NetworkStream stream)
    {
        string html = GetHtml();
        byte[] body = Encoding.UTF8.GetBytes(html);
        string header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        stream.Write(Encoding.UTF8.GetBytes(header));
        stream.Write(body);
    }

    private static void ServeJson(NetworkStream stream)
    {
        int h, m; bool chg; string ts;
        lock (_lock) { h = _headset; chg = _charging; m = _mouse; ts = _timestamp; }
        string chgStr = chg ? "true" : "false";
        string json = $"{{\"headset\":{h},\"headsetCharging\":{chgStr},\"mouse\":{m},\"timestamp\":\"{ts}\"}}";
        byte[] body = Encoding.UTF8.GetBytes(json);
        string header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        stream.Write(Encoding.UTF8.GetBytes(header));
        stream.Write(body);
    }

    private static string GetHtml()
    {
        return "<!DOCTYPE html>\n<html lang=\"zh\">\n<head><meta charset=\"utf-8\"><title>Battery Monitor</title>\n<style>\n" +
"*{margin:0;padding:0;box-sizing:border-box}\n" +
"body{font-family:-apple-system,sans-serif;text-align:center;padding:60px 20px;background:#1a1a2e;color:#eee}\n" +
"h1{font-size:24px;margin-bottom:40px;color:#e94560}\n" +
".card{display:inline-block;background:#16213e;border-radius:12px;padding:30px 40px;margin:0 12px 20px;min-width:180px}\n" +
".label{font-size:14px;color:#888;margin-bottom:8px}\n" +
".value{font-size:48px;font-weight:bold;color:#0f3460}\n" +
".value.ok{color:#1b5e20}\n.value.warn{color:#a06300}\n.value.bad{color:#c62828}\n.value.na{color:#555}\n" +
"#ts{color:#555;margin-top:30px;font-size:13px}\n" +
"</style></head><body>\n" +
"<h1>Battery Monitor</h1>\n" +
"<div class=\"card\"><div class=\"label\">INZONE H9</div><div class=\"value ok\" id=\"h\">--</div></div>\n" +
"<div class=\"card\"><div class=\"label\">Rapoo VT3S</div><div class=\"value ok\" id=\"m\">--</div></div>\n" +
"<div id=\"ts\"></div>\n" +
"<script>\n" +
"function cls(e,c){e.className='value';if(c>=0&&c<70)e.className+=' '+(c<50?'bad':'warn');else if(c>=70)e.className+=' ok';else e.className+=' na'}\n" +
"setInterval(function(){\n" +
"fetch('/data').then(r=>r.json()).then(d=>{\n" +
"var h=document.getElementById('h'),m=document.getElementById('m');\n" +
"h.textContent=d.headset>=0?d.headset+'%':'N/A';\n" +
"m.textContent=d.mouse>=0?d.mouse+'%':'N/A';\n" +
"cls(h,d.headset);cls(m,d.mouse);\n" +
"document.getElementById('ts').textContent='Updated: '+d.timestamp;\n" +
"}).catch(function(){});\n" +
"},2000);\n" +
"</script></body></html>";
    }
}
