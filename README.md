# Xbox 手柄电量（Windows 托盘小工具）

在 Windows 上查看 Xbox 手柄的电量与连接状态。常驻系统托盘，鼠标悬停看电量，左键点开详情面板，电量过低时弹提醒。

## 运行

直接双击 `publish\XboxBattery.exe`（需要 .NET 8 桌面运行时，本机已安装）。

- 左键点击托盘图标：打开详情面板
- 右键点击托盘图标：显示详情 / 立即刷新 / 开机自动启动 / 退出
- 命令行诊断：`XboxBattery.exe --probe`，结果打印到控制台并写入同目录 `probe-output.txt`

## 关于"精确电量"的重要说明

**Xbox 官方手柄的固件只上报四档电量**（低 / 中 / 高 / 满），协议里就只有 2 个 bit。任何软件都不可能给出真实的 1% 精度，这是硬件层面的限制。

本工具因此按数据源能力分级显示：

| 拿到什么 | 怎么显示 |
|---|---|
| 系统百分比（Windows BatteryReport） | `100%` |
| 手柄四档档位 | `约 75%（高）`，并注明"手柄仅上报四档" |
| 只有有线供电、无电量数据 | `有线供电 · 无电量数据` |
| 刚连接、读数未稳定 | `读取中…` |

有线 USB 连接时，Windows 报告的百分比**可能是名义值**（常见为固定的 100%），详情面板里会同时列出"手柄上报的档位"和"原始毫瓦时读数"，便于判断。

## 数据源与优先级

1. **RawGameController**（WinRT 官方接口）— 唯一可能给出百分比的来源，读 BatteryReport 的剩余/满充毫瓦时
2. **设备属性** — Windows「设置 → 蓝牙和设备」里那个百分比，只用于补充**已连接**的手柄；已配对未连接的设备会保留缓存值，已按 `System.Devices.Connected` 过滤
3. **GIP**（`\\.\XboxGIP`）— 直读手柄自身的状态消息（四档 + 电池类型），有线/无线都走这条路；协议已于 2024-09 由微软发布为开放标准 MS-GIPUSB
4. **XInput** — 四档兜底

连接方式的判断不看 XInput 的 BatteryType（它表示电池类型，实测有线时也报 NIMH），而是用 SetupAPI 检查是否存在在线的 USB Xbox 手柄设备。

## 设计上的防错处理

- **稳定门限**：同一档位连续出现两次才采信。手柄刚连接时数据源可能先给一个陈旧值（实测出现过"低"但实际满电）
- **GIP 采信门槛**：至少收到 2 条状态消息才采用，避免初始状态误报
- **充电状态三态**：只有数据源明确说充电才显示"充电中"，否则显示"未知"，不猜
- **低电量提醒**：需要读数为稳定值，且同一档位只提醒一次

## 开发

```text
# 构建
dotnet build -c Release

# 发布单文件 exe
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

- 需要 .NET 8 SDK
- NuGet 走 `NuGet.config` 里配置的华为云镜像
- 目标框架 `net8.0-windows10.0.19041.0`（WinRT 投影需要）

## 文件说明

| 文件 | 作用 |
|---|---|
| `Program.cs` | 入口，单实例 + `--probe` 分支 |
| `Model.cs` | 读数模型与显示策略（分级、精度标注） |
| `BatteryMonitor.cs` | 多源聚合、稳定门限、连接方式判定 |
| `Providers/RawGameControllerProvider.cs` | WinRT 百分比路径 |
| `Providers/DevicePropertyProvider.cs` | 设备属性百分比（60 秒缓存） |
| `Providers/GipProvider.cs` | `\\.\XboxGIP` 后台读取线程 |
| `Providers/XInputProvider.cs` | XInput 四档兜底 |
| `Providers/UsbPresenceProbe.cs` | SetupAPI 判断是否 USB 有线连接 |
| `TrayApp.cs` / `StatusForm.cs` / `IconRenderer.cs` | 托盘、面板、图标绘制 |
| `AutoStart.cs` | 开机自启（HKCU Run 项） |
| `ProbeRunner.cs` | `--probe` 诊断 |

## 已知限制

- 四档是精度天花板，无法突破
- GIP 状态消息是周期性下发（实测约 5–6 秒一条），偶发长时间不来；此时退回其它数据源
- 蓝牙模式下 Windows 自身的电量读数经常不准（Game Bar 显示"电量严重不足"而实际满电的情况大量存在），这是微软实现问题
- 无线适配器与蓝牙在部分情况下都表现为 XInput 设备，若无法确认会标为"无线（适配器）"
