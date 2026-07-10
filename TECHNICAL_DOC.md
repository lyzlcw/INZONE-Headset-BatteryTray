# MixTray — 技术文档

> INZONE H9 耳机电量托盘显示 — 整合 ClrMD + USB CDC 双方案

---

## 概述

MixTray 是一个 Windows 系统托盘应用，实时显示 INZONE H9 耳机精确电量百分比。
整合了两种读取方案，启动时通过 GUI 选择，运行时可在右键菜单中动态切换。

| 方案 | 读取方式 | 依赖 | 充电检测 |
|------|---------|------|---------|
| **ClrMD** | 读 INZONE Hub 进程托管堆 | Hub 必须运行 | ❌ |
| **USB CDC** | COM4 直读设备协议 | 无（纯 Win32 API） | ✅ |

发布为单文件 exe，依赖 .NET 8 运行时，体积约 180KB。

---

## 项目结构

```
mix/
├── Program.cs              # 入口：选择对话框 → 启动托盘
├── SchemeSelectorDialog.cs # 启动 GUI 方案选择（RadioButton + 注释）
├── MainForm.cs             # 托盘主窗体：图标、菜单、定时器、充电闪烁
├── IBatteryReader.cs       # 读取器接口
├── ClrMdReader.cs          # 方案A：ClrMD 读 Hub 内存
├── UsbCdcReader.cs         # 方案B：USB CDC 直连 COM4
├── Config.cs               # JSON 配置持久化
├── MixTray.csproj          # .NET 8 WinForms + ClrMD
├── publish/                # 发布输出
├── TECHNICAL_DOC.md        # 本文档
├── PROTOCOL_DISCOVERY.md   # USB CDC 协议逆向
├── PROGRESS.md             # 项目进度与状态
└── AGENTS.md               # 会话交接文档
```

---

## 架构设计

### IBatteryReader 接口

```csharp
public interface IBatteryReader : IDisposable
{
    (int battery, bool charging) Read();
    string Method { get; }
}
```

- `Read()` 返回 `(电量百分比 0-100, 是否充电中)`，失败返回 `(-1, false)`
- `Method` 返回标识字符串（如 `"ClrMD"`、`"USB"`）
- 继承 `IDisposable`，切换方案时自动释放

### 方案A: ClrMdReader

使用 Microsoft.Diagnostics.Runtime (ClrMD) v3.1.512801 附加到 INZONE Hub 进程。

**对象链**:
```
INZONEHub 进程
→ PcWidgetCommunication 对象
→ headsetParam 字段
→ part1 字段
→ batteryInfo 字段
→ remainingBattery (byte, 0-100)
```

**关键细节**:
- 字段名是 `remainingBattery`（不是 `_remainingBattery`）
- `DataTarget.AttachToProcess(pid, suspend: true)` — 挂起 Hub 保证快照一致
- 枚举托管堆遍历所有对象，类型名含 `"PcWidgetCommunication"` 即目标
- Hub 进程名可能是 `"INZONE Hub"`（带空格）或 `"INZONEHub"`（无空格）
- 读取瞬间 Hub 暂停通常 <100ms
- 无充电状态信息（字节中无对应字段）

### 方案B: UsbCdcReader

通过 Win32 P/Invoke 直接操作 USB CDC 虚拟串口（COM 口自动检测）。

**设备信息**:
| 属性 | 值 |
|------|-----|
| VID/PID | 0x054C / 0x0E53 |
| 通信接口 | MI_06 — CDC-Data (bInterfaceClass=0x0a) |
| Windows 驱动 | usbser.sys → COM 口（自动检测） |
| BULK OUT 端点 | 0x08 |
| BULK IN 端点 | 0x88 |

**串口配置（严格顺序）**:
```csharp
// 1. 配置 DCB
BuildCommDCB("9600,n,8,1", ref dcb);
dcb.Flags |= DcbBinary | DcbDtrControlEnable | DcbRtsControlEnable;
dcb.Flags &= ~(1 << 5); // fOutxCtsFlow=0
dcb.Flags &= ~(1 << 6); // fOutxDsrFlow=0
SetCommState(handle, ref dcb);

// 2. 超时
SetCommTimeouts(handle, timeouts);  // ReadInterval=50ms, TotalTimeout=2000ms

// 3. 缓冲区
SetupComm(handle, 4096, 4096);

// 4. 清空
PurgeComm(handle, PURGE_RXCLEAR | PURGE_RXABORT | PURGE_TXABORT | PURGE_TXCLEAR);

// 5. 断言 DTR/RTS（必须！）
EscapeCommFunction(handle, SETDTR);
EscapeCommFunction(handle, SETRTS);
```

**重要**: `System.IO.Ports.SerialPort` **不兼容**（内部用 `FILE_FLAG_OVERLAPPED` + 默认流控）。

**协议格式**:

OUT 请求（12 字节）:
```
01 00 fc 08 96 c3 TT CMD SUB 01 00 CHK
            |    |   |
          类型 命令 子类型
```
- `TT` = `41`(数据命令) / `21`(状态查询)
- `CMD` = 命令类型
- `SUB` = 子类型/序列号
- `CHK` = 校验字节（算法未逆向，不影响读取）

IN 响应（变长）:
```
04 ff LEN [payload...] CHK
  |    |
 头  长度
```

**初始化序列（必须！否则写超时 err=121）**:
```
Step 1: CMD=0x01 状态查询
  → OUT: 01 00 fc 08 96 c3 21 01 01 01 00 7D
  → 间隔 250ms
  → IN:  04 ff 0a 00 96 c3 12 01 SS 01 00 01 CHK

Step 2: CMD=0x02 配对/初始化
  → OUT: 01 00 fc 08 96 c3 41 02 01 01 00 9E
  → 间隔 250ms
  → IN:  04 ff 0f 00 96 c3 14 02 10 01 00 00 ff 00 00 ff 00 CHK

Step 3: CMD=0x04 电量查询
  → OUT: 01 00 fc 08 96 c3 41 04 01 01 00 A0
  → 间隔 300ms
  → IN:  04 ff 0b 00 96 c3 14 04 SS 01 00 CC BATT CHK
                                              ^^       ^^^^
                                          充电标志  电量 0-100
```

**电量响应解析**（14 字节）:
| 偏移 | 字段 | 含义 |
|------|------|------|
| [0-2] | Header | `04 ff 0b` |
| [3-6] | Magic | `00 96 c3 14` |
| [7] | Type | `04`=电量报告 |
| [8] | SS | 状态标志 |
| [9] | Seq | 序列号 |
| [10] | Pad | `00` |
| [11] | **CC** | **充电标志**: `00`=未充电, `01`=充电中 |
| [12] | **BATT** | **电量百分比**: `0`-`100` |
| [13] | CHK | 校验字节 |

**多包响应**: 设备 BULK IN 可能包含多个响应包拼接（例如 45B = 3 个包）。
解析时找 `04 ff <len>` 头分割。

**设备主动推送**: 初始化后 ~1.7s 自动发送电量报告，此后每 ~60s 自动更新。
只监控电量的话可以只初始化不查询，等推送。

**Hub 共存策略**:
- Hub 以独占模式打开 COM4（dwShareMode=0），无法共享
- 进入 USB 方案时自动 `Process.Kill("INZONEHub")`，等待 500ms 后打开
- **保持句柄打开**，在循环中重复 query/read（多次 open/close 会损坏设备状态 err=121）
- 方案切换回 ClrMD 或退出时不重启 Hub（需手动重启）

**COM 口自动检测**:
- 扫描注册表 `HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_054C&PID_0E53` 下所有设备实例
- 递归查找每个实例的 `Device Parameters\PortName`
- 收集所有候选端口，逐个 `CreateFile` 验证是否可打开
- 跳过打开失败的端口（过滤 phantom 设备），仅返回真正可用的 COM 口
- 支持任意 USB 端口插拔，不受原 COM4 限制
- 端口名缓存 + 失败自动清缓存重扫

**断链自动重扫**:
- `ReadBattery()` 读到 `-1`（无设备）时，自动将 `_pollTimer` 间隔切到 **2 秒**
- 读到有效电量后，恢复用户设定的频率（5/15/25/50 秒）
- 右键菜单「Scan COM Port」手动清缓存 + 强制重扫

---

## UI 流程

```
启动 → Config.Load()
     → SchemeSelectorDialog（模态选择）
         → RadioButton 二选一 + 灰色注释
         → 确定 → Config.Save()
     → MainForm（SetVisibleCore 隐藏窗口）
         → SwitchReader() 创建 IBatteryReader
         → ReadBattery() 立即读取
         → PollTimer 按频率定时读取
```

### SchemeSelectorDialog

- 520×290 固定对话框，居中
- RadioButton 直接挂 Form（同一容器，原生互斥）
- 手动 Click 事件确保互斥
- 分隔线视觉分区 + 灰色小字注释
- 确定 → `SelectedScheme` 属性 → 关闭

### MainForm

**托盘图标** (32×32 Bitmap):
- `Segoe UI Bold` 居中绘制数字
- 1~2 位用 16pt，3 位（100）用 10pt
- `--` 表示无信号（灰色）

**颜色方案**:
| 条件 | 背景色 | 前景色 | 对比度 |
|------|--------|--------|--------|
| 无信号 | `#757575` (灰) | White | 低 |
| <50% | `#C62828` (深红) | White | ~6.8:1 |
| 50-69% | `#A06300` (深琥珀) | `#1A0F00` (深棕) | ~7.2:1 |
| ≥70% | `#1B5E20` (深绿) | White | ~9.0:1 |

**充电闪烁**: 600ms Timer 交替正常背景 / RGB+80 亮色背景（仅 USB 方案可检测充电）。

**右键菜单**:
- `Refresh Now` — 立即读取
- `Scan COM Port` — 清空 USB 端口缓存，强制重新扫描注册表并重连
- `方案切换 ▸` — 两个子项带 ✓ 标记，点击即时切换
- `刷新频率 ▸` — 5秒 / 15秒 / 25秒 / 50秒
- `Exit` — 清理资源退出

---

## 配置持久化

- 文件：`mix_config.json`（与 exe 同目录）
- 格式：`System.Text.Json`
- 字段：
  - `SchemeIndex`: `0`=ClrMD, `1`=USB
  - `RefreshIntervalIndex`: `0`=5s, `1`=15s, `2`=25s, `3`=50s
- 切换方案或修改频率时自动保存

---

## 日志

- 文件：`MixTray.log`（与 exe 同目录，追加写入）
- 格式：`[yyyy-MM-dd HH:mm:ss] 消息`
- 线程安全：`lock (LogLock)`
- 内容：启动信息、方案切换、电量读数、频率变更、错误

---

## 构建与发布

```powershell
cd new\mix

# 框架依赖版（~180KB，需系统安装 .NET 8 运行时）
dotnet publish -c Release -r win-x64 --self-contained false -o publish

# 独立版（~155MB，无需 .NET 运行时，打包所有依赖）
dotnet publish -c Release -r win-x64 --self-contained true -o MixTrayNONET8version
```

框架依赖版输出 `publish/MixTray.exe`，独立版输出 `MixTrayNONET8version/MixTray.exe`。

---

## 已尝试但失败的技术路径

| 方案 | 结果 |
|------|------|
| HID Feature 0xA0 | 只返回 0-4 等级，非百分比 |
| HID Feature 0xA1 | Sony 自定义加密 `eadid:ENC0003`，无解密密钥 |
| HID SetupAPI 枚举 | Nefarius HidHide 拦截 (gle=1784) |
| FwUpdate_Monitor.dll 逆向 | 纯固件更新库，无电池函数 |
| `System.IO.Ports.SerialPort` | `FILE_FLAG_OVERLAPPED` + 流控不兼容 |
| GraphicsPath 圆角图标 | 缓存 / Dispose 问题，改用 FillRectangle |

---

## 已知问题

1. 退出 USB 方案后不自动重启 INZONE Hub
2. ClrMD 方案无充电检测
3. 多次 open/close COM4 会损坏设备状态（需重启 Hub 恢复）
4. 校验字节算法未逆向（当前硬编码固定值，不影响读取）
5. 无开机自启功能
6. 高 DPI 下三位数（100）字号可能仍需微调
