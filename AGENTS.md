# MixTray — 会话交接文档

> 此文档供下一轮 AI 会话快速恢复上下文使用。

---

## 当前状态

MixTray 已整合完成：启动 GUI 选方案 → 托盘显示电量，右键可切换方案和频率。

**所在目录**: `F:\workplace\opencodework\new\mix\`
**执行文件**: `publish/MixTray.exe`（~180KB）
**构建方式**: `dotnet publish -c Release -r win-x64 --self-contained false -o publish`

---

## 核心文件速查

| 文件 | 行数 | 核心逻辑 |
|------|------|---------|
| `MainForm.cs` | 263 | 托盘图标、充电闪烁、右键菜单、方案/频率切换 |
| `UsbCdcReader.cs` | 198 | COM4 串口通信、协议命令、响应解析 |
| `ClrMdReader.cs` | 56 | ClrMD 附加 Hub 进程、堆遍历读内存 |
| `SchemeSelectorDialog.cs` | 106 | 启动方案选择 GUI |
| `Config.cs` | 41 | JSON 配置读写 |
| `Program.cs` | 22 | 入口：弹框 → 启动托盘 |
| `IBatteryReader.cs` | 7 | 接口定义 |

---

## 关键技术细节

### USB CDC 协议（UsbCdcReader.cs）

- 12 字节 OUT 命令: `01 00 fc 08 96 c3 41 CMD SUB 01 00 CHK`
- 必须先发初始化: CMD=0x01 + CMD=0x02，然后才能 CMD=0x04 查电量
- 电量响应: `04 ff 0b ... CC BATT CHK` — byte[11]=充电, byte[12]=电量
- 串口: Win32 CreateFile("\\.\COMx"), 9600 8N1, DTR/RTS 必须断言
- COM 口**自动检测**: 扫描注册表 `HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_054C&PID_0E53` 收集所有 PortName 候选，逐个 `CreateFile` 验证可打开，过滤 phantom 设备。支持任意 USB 端口
- **断链自动 2s 重扫**: `ReadBattery()` 读到 `-1` 时自动切 `_pollTimer` 到 2s，连上后恢复
- **手动 Scan COM Port**: 右键菜单项，清缓存强制重扫
- `System.IO.Ports.SerialPort` **不兼容**
- 句柄必须保持打开，关闭后重新打开会损坏设备状态

### ClrMD 方案（ClrMdReader.cs）

- Hub 进程名: `"INZONE Hub"`（带空格）或 `"INZONEHub"`（无空格）
- 对象链: `PcWidgetCommunication → headsetParam → part1 → batteryInfo → remainingBattery`
- 字段名是 `remainingBattery`（不是 `_remainingBattery`）
- NuGet: `Microsoft.Diagnostics.Runtime` v3.1.512801
- ClrMD 模式无充电检测

### 图标绘制（MainForm.cs）

- 32×32 Bitmap, Segoe UI Bold
- 1-2位 16pt, 3位(100) 10pt
- 颜色: <50%=红#C62828, 50-69%=琥珀#A06300, ≥70%=绿#1B5E20, 无信号=灰#757575
- 充电: 600ms 亮/暗闪烁（RGB+80）

---

## 已知问题 & 常见坑

1. **COM 口自动检测**: 扫描注册表 `Enum\USB\VID_054C&PID_0E53` 收集所有 PortName，逐一验证可打开。右键菜单「Scan COM Port」可手动重扫。2s 自动扫描在断开时启用。
2. **Hub 进程名**: 两种写法都有，`FindHubProcessName()` 会尝试 `"INZONE Hub"` 和 `"INZONEHub"`
3. **退出不重启 Hub**: 用户已知，当前行为是手动重启
4. **ClrMD 无充电**: 字节中没有充电字段
5. **校验字节**: 算法未知，硬编码固定值，不影响读取
6. **100 字号**: 当前 10pt，如果高 DPI 下溢出需要再调小（改 MainForm.cs `fontSize`）

---

## 可能的下一步（按用户未提但合理推测）

| 任务 | 改动位置 | 难度 |
|------|---------|------|
| 退出后重启 Hub | `UsbCdcReader.Dispose()` + `Process.Start` | 低 |
| 开机自启 | 注册表 Run 键或任务计划 | 低 |
| 降噪状态显示 | 解析 `04 ff 0d` 响应 byte[11] | 低 |
| 托盘图标显示充电⚡符号 | `MakeIcon()` 加判定 | 低 |
| 校验字节算法逆向 | 多组样本推导 XOR/CRC | 中 |
| 多耳机 + 自动发现 | 枚举 COM 口 + 协议探测 | 中 |
| 高 DPI 适配 | App.Manifest + DPI-aware 字号 | 低 |

---

## 配置格式

`mix_config.json`（与 exe 同目录）：
```json
{ "SchemeIndex": 0, "RefreshIntervalIndex": 2 }
```
- `SchemeIndex`: 0=ClrMD, 1=USB
- `RefreshIntervalIndex`: 0=5s, 1=15s, 2=25s, 3=50s

---

## 日志

`MixTray.log`（与 exe 同目录），追加写入。
