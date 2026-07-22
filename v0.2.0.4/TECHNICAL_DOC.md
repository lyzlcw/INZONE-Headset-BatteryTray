# INZONE-Headset-BatteryTray — 技术文档

> v0.2.0.4 — Win32 托盘应用，无 System.Drawing 依赖

---

## 概述

纯 Win32 API 实现（无 WinForms/WPF），所有图标绘制使用 GDI（`CreateDIBSection`/`CreateFontW`/`DrawTextW`）。

| 设备 | 方案 | 读取方式 | 充电检测 |
|------|------|---------|---------|
| INZONE H9 | ClrMD | 读 Hub 进程托管堆 | ❌ |
| INZONE H9 | USB CDC | COM 口串口协议 | ✅ byte[11] |
| Rapoo VT3S | HID | Win32 HID P/Invoke | ❌ |

---

## 项目结构

```
INZONE-Headset-BatteryTray/
├── Program.cs                 # 入口：检测 Hub 进程 → 启动托盘
├── MainForm.cs                # Win32 托盘：消息循环、图标、菜单、双Reader并行读
├── IBatteryReader.cs          # 读取器接口
├── ClrMdReader.cs             # 方案A：ClrMD 读 Hub 内存
├── UsbCdcReader.cs            # 方案B：USB CDC 串口协议
├── RapooHidReader.cs          # 鼠标：Win32 HID P/Invoke
├── Config.cs                  # JSON 配置持久化 (ihbt.json)
├── TelemetrySender.cs         # UDP 遥测推送
├── HttpServer.cs              # 内置 HTTP 监控页服务
├── publish/                   # 发布输出
├── experiments/               # 实验功能
│   ├── gdi-test/              # GDI 图标绘制测试
│   ├── udp-sender/            # UDP 遥测原型
│   ├── volume-reader/         # 音量协议探索
│   ├── memory/                # 内存优化实验
│   └── optimization/          # 性能优化实验
└── *.md                       # 文档
```

---

## 架构设计

### 消息循环

```
Program.Main()
  → SetProcessDpiAwareness(PerMonitorV2) (DPI 感知)
  → Config.Load()
  → MainForm.Run()
  → RegisterClassW + CreateWindowExW (隐藏窗口)
  → Shell_NotifyIconW NIM_ADD (托盘图标)
  → TelemetrySender.Init(config)
  → HttpServer.Start(config.HttpPort)
  → SwitchReader (创建 IBatteryReader)
  → new RapooHidReader
  → SetTimer (PollTimer)
  → GetMessageW 消息循环
```

### 双 Reader 并行读

```csharp
_pendingResults = 2;
Task.Run(() =>
{
    var (hb, hc) = _headsetReader.Read();
    PostMessageW(WmHeadsetResult, hb, hc ? 1 : 0);
    var (mb, _) = _mouseReader.Read();
    PostMessageW(WmMouseResult, mb, 0);
});
```

结果通过 `PostMessageW` 回到主线程，两个结果都到齐后触发 `OnBothResults()`。

### OnBothResults 调用链

```
OnBothResults()
  → Log (环形日志)
  → TelemetrySender.Send (UDP 推送)
  → HttpServer.UpdateData (HTTP 服务更新)
  → CheckLowBattery (低电量通知)
  → UpdatePollInterval (断链自动 2s)
  → UpdateTray (tooltip + 图标)
  → 充电闪烁 Timer 管理
```

---

## ClrMdReader

使用 `Microsoft.Diagnostics.Runtime` v3.1.512801 附加到 INZONE Hub 进程。

**对象链**: `PcWidgetCommunication → headsetParam → part1 → batteryInfo → remainingBattery`

- Hub 进程名: `"INZONE Hub"` 或 `"INZONEHub"`
- `DataTarget.AttachToProcess(pid, suspend: true)`
- 无充电检测

---

## UsbCdcReader

Win32 串口通信（`CreateFile` + `BuildCommDCB` 9600 8N1, DTR/RTS 断言）。

| 属性 | 值 |
|------|-----|
| VID/PID | 0x054C / 0x0E53 |
| 接口 | MI_06 CDC-Data |
| 协议 | 12B OUT → 变长 IN |
| 电量 | byte[12], 0-100 |
| 充电 | byte[11], 0x00/0x01 |

**约束**: `System.IO.Ports.SerialPort` 不兼容；句柄保持打开。

COM 口自动检测: 注册表枚举 → `CreateFile` 验证。

---

## RapooHidReader

Win32 HID，VID=0x24AE, PID=0x1411, UsagePage=0xFF00, Usage=0x0002, byte[8]=电量。

**三步读**: 200ms quick → 写入(0x07,0x01)+100ms+300ms → 被动 3.5s

`SetupDiGetClassDevs` 枚举 + 设备路径缓存。

---

## 图标绘制 (GDI, 无 System.Drawing)

**颜色方案**:

| 电量 | 背景色 | 前景色 |
|------|--------|--------|
| 无信号 | `#757575` | White |
| <50% | `#C62828` | White |
| 50-69% | `#A06300` | `#1A0F00` |
| ≥70% | `#1B5E20` | White |

- 1~2 位 16pt，3 位 10pt，`Segoe UI Bold`
- 充电: 600ms Timer 交替 RGB+80
- 实现: `CreateDIBSection`(32bpp DIB) → `FillRect` → `CreateFontW` → `DrawTextW` → `CreateIconIndirect`
- DPI: `SetProcessDpiAwareness(PerMonitorV2)`，字号上限 24px(2位)/16px(3位)

---

## HttpServer (内置 HTTP)

`TcpListener` 实现，不依赖任何 HTTP 框架。

| 路由 | 说明 |
|------|------|
| `GET /` | 监控网页，每 2s fetch `/data` 自动刷新 |
| `GET /data` | JSON: `{"headset":70,"headsetCharging":false,"mouse":54,"timestamp":"..."}` |

端口通过 `ihbt.json` 的 `HttpPort` 字段配置（默认 19090）。

---

## TelemetrySender (UDP 推送)

| 字段 | 默认 | 配置位置 |
|------|------|---------|
| 启用 | `true` | `ihbt.json` → `TelemetryEnabled` |
| 端口 | 19091 | `ihbt.json` → `TelemetryPort` |
| 目标 | 127.0.0.1 | 固定（仅本地回环） |

`Init()` 在 `MainForm` 构造函数中调用，传入配置值。

---

## 日志系统 (环形缓冲区)

- `traylog.log` (主日志) / `raphid.log` (Rapoo 独立日志)
- 各限 64KB，`FileStream` + `_logPosition`，超限 `Seek(0)` 从头覆盖
- 无 `.old` 文件，不生成垃圾

---

## 低电量通知

`CheckLowBattery()` 在 `OnBothResults()` 中调用：
- 耳机/鼠标电量 < 20% → `Shell_NotifyIconW(NIF_INFO)` 气球通知
- 恢复到 ≥ 25% 后重置通知门限，避免重复

---

## 开机自启

右键菜单切换，操作 `HKCU\Software\Microsoft\CurrentVersion\Run` 键。

---

## 配置 (ihbt.json)

| 字段 | 默认 | 说明 |
|------|------|------|
| `SchemeIndex` | 0 | 0=ClrMD, 1=USB |
| `RefreshIntervalIndex` | 2 | 0=5s, 1=15s, 2=25s, 3=50s |
| `TelemetryEnabled` | true | UDP 遥测开关 |
| `TelemetryPort` | 19091 | UDP 推送端口 |
| `HttpPort` | 19090 | HTTP 监控页端口 |

---

## 已知问题

1. 退出 USB 方案后不自动重启 INZONE Hub
2. ClrMD 方案无充电检测
3. Rapoo VT3S 主动写入成功率约 50%
4. 校验字节算法已逆向: `CHK = type + cmd + sub + 0x5A`
