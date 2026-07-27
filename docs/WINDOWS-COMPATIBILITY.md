# Windows compatibility and troubleshooting

## 支持矩阵

| 环境 | 状态 | 说明 |
| --- | --- | --- |
| Windows 11 x64 | 目标平台 | 已在一台开发机完成构建、回归和本机功能测试 |
| Windows 10 x64 | 目标平台 | 代码路径兼容；尚未在第二台干净电脑实机验收 |
| Windows 8/8.1 x64 | 非目标 | Codex Desktop 支持范围可能不满足 |
| Windows 32 位 | 不支持 | EXE 的 PlatformTarget 为 x64 |
| Windows on ARM | 未正式验证 | 可尝试系统 x64 模拟 |
| Windows Server / Wine | 未正式验证 | 桌面合成、托盘和 Codex Desktop 可能不可用 |

程序目标 .NET Framework 4.0；Windows 10/11 内置的 .NET Framework 4.x 可兼容运行。
不需要在发布目录携带 .NET DLL。

目标平台不等于所有环境均已实测。正式公开发布前，建议在至少一台未安装开发工具的
Windows 10 或 Windows 11 x64 电脑完成直接运行、Codex 登录读取、托盘、四边吸附、
多显示器/DPI、开机启动、卸载和 SmartScreen 验收。

自定义安装目录只支持本机盘符路径。为避免安装或卸载穿过链接修改其他位置，UNC、
`\\?\` / `\\.\` device namespace、符号链接、junction、mount point 及其后代不会被
接受。路径检查覆盖目标自身和每一级已存在的祖先，并在每次文件写入或删除前重新执行。

仓库生命周期脚本以 Windows PowerShell 5.1 为最低版本。发布前应使用系统自带的
`powershell.exe` 做语法解析和安装安全回归，而不能只在 PowerShell 7 中测试。

## Codex 定位

定位顺序：

1. `CODEX_USAGE_MONITOR_CODEX_EXE`
2. 当前用户 LocalAppData 下的 Codex Desktop `bin` 目录
3. `PATH` 中的 `codex.exe`

正常用户无需设置环境变量。高级覆盖示例：

```powershell
$trustedCodex = (Get-Command codex.exe -ErrorAction Stop).Source
[Environment]::SetEnvironmentVariable(
    'CODEX_USAGE_MONITOR_CODEX_EXE',
    $trustedCodex,
    'User')
```

该变量指定的文件会被执行。只使用可信路径；不要把某台开发机的用户名、版本目录哈希
或绝对路径提交到源码和文档。

清除覆盖：

```powershell
[Environment]::SetEnvironmentVariable(
    'CODEX_USAGE_MONITOR_CODEX_EXE',
    $null,
    'User')
```

## 常见问题

### 显示找不到 Codex

1. 确认 Codex Desktop 已安装。
2. 打开 Codex Desktop，确认当前 Windows 用户已登录。
3. 移除失效的 `CODEX_USAGE_MONITOR_CODEX_EXE`。
4. 退出并重新启动 monitor。

### 百分比为空或显示 `--`

1. 确认 Codex Desktop 在线且账号可正常使用。
2. 等待一次刷新周期。
3. 使用插件状态脚本确认程序和路径：

```powershell
.\plugins\codex-usage-monitor\scripts\Get-CodexUsageMonitorStatus.ps1
```

`ProcessIds` 是数组。`RunningExecutablePath` 可用于识别实际运行的是 standalone 还是
plugin 内的 EXE。

### 关闭后仍在运行

窗口关闭默认是隐藏到托盘。使用托盘“退出”，或运行：

```powershell
.\plugins\codex-usage-monitor\scripts\Stop-CodexUsageMonitor.ps1
```

脚本发送安全退出信号并等待，不会默认强制终止进程。

### 吸附后无法移动

把鼠标按在半圆内部，向屏幕内部拖离边缘。双击半圆只展开、不脱离。关闭“侧边吸附”
会清除当前吸附状态并恢复展开窗口。如果显示器布局刚发生变化，先从托盘重新显示窗口。

### 紧凑数字显示不全

1.1 版半圆仅显示 `100`、`--` 或 `∞`，悬浮提示显示完整百分比。验收应覆盖这三种
宽度、左右/上下四种方向，以及 100%、125%、150%、200% 的 Windows 缩放比例。

### 毛玻璃文字不清晰或背景是黑块

毛玻璃能力受 Windows 版本、显卡驱动、远程桌面和“透明效果”设置影响。切换到
赛博朋克或极简主题可完全绕过系统 backdrop。此降级不影响用量读取。

### SmartScreen 警告

未签名的新项目缺少下载信誉时可能出现警告。核对：

```powershell
Get-FileHash .\release\CodexUsageMonitor.exe -Algorithm SHA256
Get-Content .\release\SHA256SUMS.txt
```

不要通过关闭系统安全功能来解决。正式分发建议使用受保护的代码签名证书。

## 重置界面状态

先安全退出程序，然后仅删除：

```text
%LOCALAPPDATA%\CodexUsageMonitor\settings.json
```

下次启动会恢复默认赛博朋克主题、置顶和侧边吸附。不要删除整个 Codex Desktop 配置
目录。

## 回滚

package installer 会在升级前保留最近一份 EXE：

```powershell
.\scripts\Rollback-CodexUsageMonitor.ps1
```

回滚只替换 monitor，不修改 Codex Desktop、账号或设置。使用 `-WhatIf` 可先查看
目标。
