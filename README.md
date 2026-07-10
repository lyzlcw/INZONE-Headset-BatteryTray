MixTray — INZONE H9 电量托盘

Windows 系统托盘应用，实时显示 INZONE H9 耳机精确电量百分比。

功能
双方案: ClrMD（读 Hub 内存）或 USB CDC（直连 COM 口），启动时选择，运行时可切换
托盘图标**: 32×32 彩色电量数字，红/琥珀/绿三档，充电时闪烁
COM 口自动检测: 扫描注册表，支持任意 USB 口插拔
右键菜单: 方案切换、刷新频率(5/15/25/50秒)、刷新、扫描 COM 口
配置持久化: 方案和频率自动保存到 `mix_config.json`

快速开始
```powershell
# 需要 .NET 8 SDK
cd new\mix
dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\MixTray.exe
```

文档
`TECHNICAL_DOC.md` — 完整技术文档
`PROTOCOL_DISCOVERY.md` — USB CDC 协议逆向分析
