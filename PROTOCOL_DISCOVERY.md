# INZONE H9 USB 协议逆向分析

> 设备: Sony INZONE H9 WH-G900N (VID=0x054C, PID=0x0E53)
> 通信: CDC Data BULK，非 HID

---

## 1. 通信接口

### 复合设备结构

USB 无线适配器包含 10 个接口（MI_00 ~ MI_09），电量通信走 **MI_06**：

| 接口 | 类 | 用途 |
|------|----|------|
| MI_04 | CDC Communications (0x02) | 控制通道、INT 端点 |
| **MI_06** | **CDC-Data (0x0a)** | **BULK 数据通道（电量）** |
| 其余 | HID / 大容量存储 | 音频、固件等 |

### 端点

| 方向 | 端点 | 类型 | 用途 |
|------|------|------|------|
| 主机→设备 | 0x08 | BULK | CDC Data OUT（发送命令） |
| 设备→主机 | 0x88 | BULK | CDC Data IN（接收响应） |
| 设备→主机 | 0x82 | INT (MI_04) | 状态通知 |

### Windows 驱动映射

- usbser.sys 将 MI_06 映射为 COM4（虚拟串口）
- Win32 `CreateFile("\\.\COM4")` 打开，配置 9600 8N1

---

## 2. 串口配置（Win32 API）

`System.IO.Ports.SerialPort` **不兼容**。必须用原始 Win32 API：

```csharp
// 打开
CreateFile(@"\\.\COM4", GENERIC_READ | GENERIC_WRITE, 0,
    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

// 配置 DCB
BuildCommDCB("9600,n,8,1", ref dcb);
dcb.Flags |= DcbBinary | DcbDtrControlEnable | DcbRtsControlEnable;
dcb.Flags &= ~(uint)(1 << 5); // fOutxCtsFlow = 0
dcb.Flags &= ~(uint)(1 << 6); // fOutxDsrFlow = 0
SetCommState(handle, ref dcb);

// 超时
COMMTIMEOUTS { ReadIntervalTimeout=50, ReadTotalTimeoutConstant=2000, WriteTotalTimeoutConstant=2000 }
SetCommTimeouts(handle, ref timeouts);

// 缓冲区
SetupComm(handle, 4096, 4096);

// 清空
PurgeComm(handle, PURGE_RXCLEAR | PURGE_RXABORT | PURGE_TXABORT | PURGE_TXCLEAR);

// 断言 DTR/RTS（必须！否则写超时 err=121）
EscapeCommFunction(handle, SETDTR);
EscapeCommFunction(handle, SETRTS);
```

---

## 3. 报文格式

### OUT 请求（主机 → 设备）

通用 12 字节格式：

```
01 00 fc 08 96 c3 TT CMD SUB 01 00 CHK
```

| 偏移 | 长度 | 字段 | 说明 |
|------|------|------|------|
| [0-5] | 6 | 魔数 | `01 00 fc 08 96 c3` |
| [6] | 1 | 类型 | `41`=数据命令, `21`=状态查询 |
| [7] | 1 | **CMD** | 命令类型 |
| [8] | 1 | **SUB** | 子类型/序列号 |
| [9-10] | 2 | 填充 | `01 00` |
| [11] | 1 | **CHK** | 校验字节（算法未逆向） |

### IN 响应（设备 → 主机）

通用格式：

```
04 ff LEN [payload...] CHK
```

| 偏移 | 长度 | 字段 | 说明 |
|------|------|------|------|
| [0] | 1 | 头 | `04` |
| [1] | 1 | 头 | `ff` |
| [2] | 1 | **LEN** | 后续 payload 长度 |
| [3..-2] | LEN | Payload | 响应数据 |
| [-1] | 1 | CHK | 校验字节 |

---

## 4. 命令类型

| CMD | SUB | 功能 | 响应头 |
|-----|-----|------|--------|
| 0x01 | 01 | 状态查询 | `04 ff 0a` (14B) |
| 0x02 | 01-03 | 配对/初始化 | `04 ff 0f` (17B) + `04 ff 02` (7B) |
| 0x03 | 01-02 | 音频参数同步 | `04 ff 11` (20B) |
| **0x04** | **01-02** | **电量查询** | **`04 ff 0b` (14B)** |
| 0x06 | 01-02 | 空间音频? | `04 ff 12` (21B) |
| 0x07 | 01-02 | 均衡器? | `04 ff 13` (22B) |
| 0x08 | 01-02 | 音效设置? | `04 ff 10` (19B) |
| 0xfd | - | 未知（4字节） | 无? |
| 0xf3 | - | 未知（28字节） | 无? |

---

## 5. 电量数据

### 电量响应结构（14 字节）

```
04 ff 0b 00 96 c3 14 04 SS 01 00 CC BATT CHK
```

| 偏移 | 字段 | 含义 | 取值 |
|------|------|------|------|
| [0-2] | Header | 固定头 | `04 ff 0b` |
| [3-6] | Magic | 固定 | `00 96 c3 14` |
| [7] | Type | 报告子类型 | `04`=状态/电量报告 |
| [8] | SS | 状态标志 | `10`=常规, `a0`=充电态 |
| [9] | Seq | 序列号 | `01`, `02`... |
| [10] | Padding | 固定 | `00` |
| [11] | **CC** | **充电标志** | `00`=未充电, `01`=充电中 |
| [12] | **BATT** | **电量百分比** | `0x00`-`0x64` (0-100) |
| [13] | CHK | 校验字节 | |

### 降噪响应（16 字节）

```
04 ff 0d 00 96 c3 14 41 a0 01 00 NC 03 ff 00 CHK
                                         ^^ NC off/on
```

### 状态响应（14 字节）

```
04 ff 0a 00 96 c3 12 01 SS 01 00 01 CHK
```

---

## 6. 初始化序列（关键！）

**必须先发初始化命令，否则 BULK OUT 写超时（err=121）。**

```
Step 1: CMD=0x01 状态查询
  → OUT: 0100fc0896c321010101007d
  → 等待 250ms
  → IN:  04 ff 0a 00 96 c3 12 01 SS 01 00 01 CHK

Step 2: CMD=0x02 配对/初始化
  → OUT: 0100fc0896c341020101009e
  → 等待 250ms
  → IN:  04 ff 0f 00 96 c3 14 02 10 01 00 00 ff 00 00 ff 00 CHK

Step 3: CMD=0x04 电量查询
  → OUT: 0100fc0896c34104010100a0
  → 等待 300ms
  → IN:  04 ff 0b 00 96 c3 14 04 SS 01 00 CC BATT CHK
```

三条响应包连续到达，共用同一个 BULK IN 传输（总长 45 字节）。

---

## 7. Hub 完整 OUT 序列（供参考）

Hub 启动时发送 18 个 OUT 包，循环重复：

```
 1. 0100fc0896c321010101007d        CMD=0x01 SUB=01
 2. 0100fc0896c341020101009e        CMD=0x02 SUB=01
 3. 0100fc0896c34104010100a0        CMD=0x04 SUB=01  电量查询
 4. 0100fd00                        其他（4B）
 5. 01f3ff180080...                 其他（28B）
 6. 0100fc0896c341030101009f        CMD=0x03 SUB=01
 7. 0100fc0896c34106010100a2        CMD=0x06 SUB=01
 8. 0100fc0896c34107010100a3        CMD=0x07 SUB=01
 9. 0100fc0896c34108010100a4        CMD=0x08 SUB=01
10. 0100fc0896c341020102009f        CMD=0x02 SUB=02
11. 0100fc0896c34102010300a0        CMD=0x02 SUB=03
12. 0100fc0896c34104010200a1        CMD=0x04 SUB=02  电量查询
13. 0100fd00
14. 01f3ff180080...
15. 0100fc0896c34103010200a0        CMD=0x03 SUB=02
16. 0100fc0896c34106010200a3        CMD=0x06 SUB=02
17. 0100fc0896c34107010200a4        CMD=0x07 SUB=02
18. 0100fc0896c34108010200a5        CMD=0x08 SUB=02
```

---

## 8. 多包响应处理

BULK IN 数据可能包含多个响应包拼接。例如 45 字节包含 3 个包：

```
[CMD=0x01 响应] 04 ff 0a 00 96 c3 12 01 10 01 00 01 7e
[CMD=0x02 响应] 04 ff 0f 00 96 c3 14 02 10 01 00 00 ff 00 00 ff 00 7e
[CMD=0x04 响应] 04 ff 0b 00 96 c3 14 04 10 01 00 00 46 c8
                                             ^^       ^^
                                          充电=00  电量=70
```

解析方法：循环寻找 `04 ff` 头，根据 byte[2]（LEN）计算包尾。

---

## 9. 设备主动推送

初始化后约 **1.7 秒**，设备自动发送电量报告。此后每约 **60 秒** 自动更新。
不发送查询也可收到更新——初始化完成后只读 BULK IN 即可。

---

## 10. 句柄复用策略

**多次 kill/open/close COM4 会损坏设备状态**（err=121），必须：
1. 只打开一次 COM4
2. 保持句柄
3. 循环中重复 query/read
4. 退出时 close
5. 恢复方式：重启 INZONE Hub

---

## 11. 实测验证数据

| 场景 | 时间 | 值 |
|------|------|-----|
| 常规状态 | 18:21:40 | 电量=70%, 充电=0x00 |
| 常规状态 | 18:22:41 | 电量=70%, 充电=0x00 |
| 常规状态 | 18:23:41 | 电量=70%, 充电=0x00 |
| 插入充电线 | Frame 165 | 电量=100%, 充电=0x01 |
| 拔出充电线 | Frame 167 | 电量=100%, 充电=0x00 |
| 耳机开机 | Frame 173 | 状态=0xa0 |
| 降噪关 | Frame 221 | NC=0x00 |
| 降噪开 | Frame 223 | NC=0x01 |

---

## 12. 结论

| 问题 | 答案 |
|------|------|
| 协议类型 | CDC Data BULK（非 HID）|
| 通信方式 | COM4（usbser.sys 虚拟串口）|
| 波特率 | 9600 8N1 |
| DTR/RTS | 必须断言 |
| 初始化 | CMD=0x01 + CMD=0x02 必须先发 |
| 电量位置 | 响应 byte[12], 0-100 |
| 充电位置 | 响应 byte[11], 0x00/0x01 |
| 句柄策略 | 保持打开，不复用 |
| 校验算法 | 未逆向（读取电量无需校验） |
| 设备推送 | 初始化后 ~1.7s 自动上报 |
