# AirPods Tray

A lightweight Windows tray app for reconnecting to a selected, already-paired Bluetooth audio device.

**Windows 蓝牙耳机一键连接工具 · 0.2.0-beta**

从已配对的蓝牙音频设备中选定目标，之后左键单击托盘图标即可请求连接。支持设备选择与记忆；名称不再固定为 AirPods 4。

## 下载与运行

**[下载 Windows x64 运行包](https://github.com/caishaoweimeheat/airpods-tray/releases/download/v0.2.0-beta/AirPodsTray-0.2.0-beta-win-x64.zip)** · [查看版本说明](https://github.com/caishaoweimeheat/airpods-tray/releases/tag/v0.2.0-beta) · [SHA-256 校验文件](https://github.com/caishaoweimeheat/airpods-tray/releases/download/v0.2.0-beta/SHA256SUMS.txt)

当前版本为 **0.2.0-beta 公开测试版**。下载运行包，完整解压后运行 `AirPods4Tray.exe`，无需自行编译。Release 中的 `Source code` 是开发用源码；普通用户请选择上面的 Windows 运行包。

1. 首次运行，在弹出的窗口中选择耳机，点击 **设为目标耳机**。
2. 将右下角 `^` 中的耳机图标拖到任务栏常显区域。
3. 左键单击图标连接；右键 **选择目标耳机…** 可随时更换。
4. 需要自动启动时，右键勾选 **开机启动**。

保存选择不会立即连接。首次启动在列表中识别到唯一的 AirPods 4 时会预选它，仍需用户点击确认。

## 功能

- 列出支持 Windows 蓝牙音频重连接口的已配对设备。
- 记住所选设备，退出程序或重启后继续使用。
- 以设备容器 ID 识别目标，改名后仍可匹配；缺少容器 ID 时使用精确端点 ID。
- 按设备标识区分同名耳机。目标缺失时提示检查，不自动连接其他同名设备。
- 托盘颜色：蓝色未连接、黄色连接中、绿色播放端点已连接。
- 单实例、状态刷新、普通用户开机启动、蓝牙/声音设置入口和本地日志。
- 已连接时直接提示，无需重复连接。

## 系统与兼容性

- 64 位 Windows 10/11，.NET Framework 4.5 或以上；开发验证环境为 Windows 11 x64 / .NET Framework 4.8。
- 使用 `KSPROPSETID_BtAudio / KSPROPERTY_ONESHOT_RECONNECT`；列表仅显示提供该接口的播放设备。
- 不依赖额外 Python、第三方蓝牙工具或管理员权限。
- 已在 Windows 11 + AirPods 4 上验证设备枚举、接口支持、选择保存和已连接状态处理。
- **尚未完成主动断开后的硬件重连实测，也未验证其他耳机型号。此版本为测试版。**
- Bluetooth LE Audio 专用设备或不提供上述接口的驱动可能不出现在列表中。
- 只连接已配对的设备，不执行首次配对。

## 已知行为

- 不更改默认声音输出。已连接却没有声音时，可从右键菜单打开声音设置，手动选择目标耳机。
- 耳机连接手机、盒子关闭、没电或超出范围时，连接可能失败。
- 请求被驱动接受后，等待约 25 秒确认播放端点状态。驱动调用自身耗时另计。
- 没有后台持续抢占连接；只有用户点击时才发起连接请求。
- 重新配对或某些驱动更新可能改变设备标识，此时重新选择目标耳机。
- 蓝牙关闭时，请开启蓝牙后点击“刷新列表”。

## 配置与升级

配置和日志位于当前用户的 `%LOCALAPPDATA%\AirPodsTray\`，分别为 `settings.xml` 和 `AirPodsTray.log`。

从旧版升级：先在旧托盘图标菜单中选择“退出”，再启动新版。首次升级需要选择一次设备。程序移动目录后，请在新版中重新勾选“开机启动”，使快捷方式指向新位置。

卸载：取消“开机启动”、退出程序后删除程序目录。若需清除历史选择，可再删除上述配置目录。

## 编译与检查

在 Windows PowerShell 中进入源码目录：

```powershell
.\Build.ps1
.\Build.ps1 -Test
.\Package.ps1 -SkipBuild
```

程序输出到 `bin\AirPods4Tray.exe`；发布压缩包输出到 `dist\`。

`-Test` 运行身份匹配、同名设备隔离、配置保存/损坏、启动快捷方式及选择窗口检查。它只在测试目录保存配置和测试快捷方式，不更改用户的开机启动设置，也不主动断开蓝牙设备。

诊断命令：

```powershell
.\bin\AirPods4Tray.exe --diagnose "C:\完整路径\diagnostic.txt"
```

完整验证范围见 [TESTING.md](TESTING.md)，版本变化见 [CHANGELOG.md](CHANGELOG.md)，版本发布与维护方法见 [PUBLISHING.md](PUBLISHING.md)。

## 反馈问题

请在 [Issues](https://github.com/caishaoweimeheat/airpods-tray/issues) 中提供 Windows 版本、耳机型号、复现步骤和错误码。兼容性反馈请说明设备是否出现在列表、是否成功连接、是否能播放声音。分享日志前请删除个人设备标识。

## 接口资料

- [Bluetooth reconnect property](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect)
- [Using IKsControl](https://learn.microsoft.com/en-us/windows/win32/coreaudio/using-the-ikscontrol-interface-to-access-audio-properties)
- [Audio endpoint container IDs](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-endpoint-container-id)

## License

[MIT](LICENSE). This is an independent utility, not an Apple or Microsoft product.
