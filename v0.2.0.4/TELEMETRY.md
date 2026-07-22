# 遥测接口文档

> v0.2.0.4 — INZONE-Headset-BatteryTray 电量数据 UDP 推送接口

---

## 概述

主程序每次轮询完成后，自动向本地 UDP 端口发送当前电量数据，供第三方软件、网页或监控工具消费。

---

## 协议

| 项目 | 值 |
|------|-----|
| 传输层 | UDP |
| 目标地址 | `127.0.0.1`（本地回环） |
| 默认端口 | **19091**（可通过 `ihbt.json` 配置） |
| 数据格式 | JSON (UTF-8) |
| 触发时机 | 每次耳机 + 鼠标读数完成（见 `OnBothResults`） |

**注意**: UDP 是无连接协议，若接收端未启动，数据直接丢弃，不影响主程序正常运行。

---

## 数据格式

### 字段说明

```json
{
  "headset": 70,
  "headsetCharging": false,
  "mouse": 71,
  "timestamp": "17:09:51"
}
```

| 字段 | 类型 | 范围 | 说明 |
|------|------|------|------|
| `headset` | int | `-1` 或 `0`-`100` | 耳机电量百分比，`-1` 表示未检测到 |
| `headsetCharging` | bool | `true`/`false` | 耳机是否充电中（仅 USB CDC 方案可检测，ClrMD 始终为 `false`） |
| `mouse` | int | `-1` 或 `0`-`100` | Rapoo VT3S 鼠标电量百分比，`-1` 表示未检测到 |
| `timestamp` | string | `"HH:mm:ss"` | 发送端本地时间 |

### 示例

正常状态:
```json
{"headset":85,"headsetCharging":false,"mouse":72,"timestamp":"14:01:28"}
```

充电中:
```json
{"headset":92,"headsetCharging":true,"mouse":-1,"timestamp":"14:05:33"}
```

无鼠标:
```json
{"headset":70,"headsetCharging":false,"mouse":-1,"timestamp":"14:10:01"}
```

---

## 接收端实现

### Python

```python
import json, socket
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("127.0.0.1", 19091))
while True:
    data, addr = sock.recvfrom(4096)
    payload = json.loads(data.decode())
    print(f"headset={payload['headset']}% | mouse={payload['mouse']}%")
```

### Node.js

```javascript
const dgram = require('dgram');
const server = dgram.createSocket('udp4');
server.bind(19091);
server.on('message', (msg) => {
    const data = JSON.parse(msg.toString());
    console.log(`headset=${data.headset}% mouse=${data.mouse}%`);
});
```

### C#

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
var udp = new UdpClient(19091);
var remote = new IPEndPoint(IPAddress.Any, 0);
while (true) {
    var bytes = udp.Receive(ref remote);
    Console.WriteLine(Encoding.UTF8.GetString(bytes));
}
```

### HTTP 监控页（内置）

主程序内置 HTTP 服务（默认 `http://127.0.0.1:19090`），无需额外部署：
- `GET /` — 实时监控网页，每 2s 自动刷新
- `GET /data` — JSON 格式的实时电量数据

---

## 配置

通过 `ihbt.json` 配置遥测与 HTTP 服务：

```json
{
  "SchemeIndex": 0,
  "RefreshIntervalIndex": 2,
  "TelemetryEnabled": true,
  "TelemetryPort": 19091,
  "HttpPort": 19090
}
```

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `TelemetryEnabled` | `true` | 是否启用 UDP 遥测推送 |
| `TelemetryPort` | `19091` | UDP 推送目标端口 |
| `HttpPort` | `19090` | HTTP 监控页服务端口 |

修改 `ihbt.json` 后重启程序生效。

---

## 代码参考

### TelemetrySender.cs

```csharp
public static class TelemetrySender
{
    public static void Init(bool enabled, int port) { ... }
    public static void Send(int headset, bool charging, int mouse) { ... }
}
```

- `Init()` — 在 `MainForm` 构造函数中调用，传入配置
- `Send()` — 每次 `OnBothResults()` 触发，自动输出日志

### 调用链路

```
MainForm 构造函数
  → TelemetrySender.Init(config.TelemetryEnabled, config.TelemetryPort)
  → HttpServer.Start(config.HttpPort)

OnBothResults()
  → TelemetrySender.Send(headset, charging, mouse)
  → HttpServer.UpdateData(headset, charging, mouse)
```

---

## 注意事项

1. **UDP 无可靠性保证**：丢包不重发。适合局域网/本机监控，不适合关键远程监控
2. **数据频率**：与轮询频率一致（5s/15s/25s/50s），可在右键菜单中调整
3. **防火墙**：若接收端在其他机器，需开放目标端口
4. **首次发送**：首次成功/失败各记一条日志，后续静默
5. **HTTP 端口冲突**：若 19090 被占用，修改 `HttpPort` 字段
