# INZONE-Headset-BatteryTray

Windows 系统托盘应用，实时显示 INZONE H9 耳机精确电量与 Rapoo VT3S 鼠标电量。

## 功能

- **双方案读耳机**：ClrMD（读 INZONE Hub 内存）或 USB CDC（直连 COM 口），启动时自动检测
- **HID 读鼠标**：Rapoo VT3S 通过 Win32 HID 读取电量（byte[8]=0-100），混合主动写入+被动等待
- **托盘图标**：32×32 纯 GDI 彩色电量数字，红/琥珀/绿三档，充电时 600ms 闪烁
- **Tooltip 悬浮**：显示耳机和鼠标电量：`INZONE H9: 85% CHG | Rapoo VT3S: 72%`
- **HTTP 监控页**：内置 HTTP 服务，浏览器打开即可查看实时电量
- **UDP 遥测推送**：可配置端口/UDP 推送电量 JSON，供第三方软件消费
- **低电量通知**：电量 <20% 时弹出 Windows 气球通知
- **环形日志**：traylog.log + raphid.log 保持在 64KB 以内，自动从头覆盖
- **开机自启**：右键菜单开关，写入 `HKCU\...\Run`
- **右键菜单**：方案切换、刷新频率(5/15/25/50s)、Refresh Now、Scan COM Port、开机自启、Exit
- **配置持久化**：全部配置保存到 `ihbt.json`

## 快速开始

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\INZONE-Headset-BatteryTray.exe
# 浏览器 http://127.0.0.1:19090
```

## 文档

| 文档 | 说明 |
|------|------|
| `TELEMETRY.md` | 遥测接口文档 — UDP 数据格式与接收端实现 |
| `TECHNICAL_DOC.md` | 完整技术架构与实现细节 |
| `PROTOCOL_DISCOVERY.md` | USB CDC 协议逆向 + Rapoo HID 协议 |
| `PROGRESS.md` | 项目进度、已/待完成、已知问题 |
| `AGENTS.md` | AI 会话交接文档 |
| `experiments/README.md` | 实验功能说明 |
