# Codex Usage Monitor

Codex Usage Monitor 是一个轻量的 Windows x64 桌面悬浮窗，用于实时显示当前 Codex
rate-limit window 的剩余比例和重置时间。程序可以直接运行、按当前 Windows 用户安装，
也可以作为 Codex 本地插件启动、停止和查询状态。

> 本项目是采用 MIT License 的社区项目，不是 OpenAI 官方产品。Codex 名称和视觉元素
> 仅用于说明兼容对象，不代表 OpenAI 的认可、担保或官方支持。

## 功能概览

- 实时显示主要和次要用量窗口、剩余百分比及重置时间。
- 支持系统托盘、单实例、置顶、开机启动和三种主题。
- 支持屏幕上、下、左、右四边吸附，并自动缩成圆球式半圆。
- 半圆模式可双击展开，也可向屏幕内部拖离后继续移动。
- 使用本机 `codex app-server`，不要求用户填写 API key。
- 提供 Windows PowerShell 5.1 安装、升级、回滚和卸载脚本。
- 提供 Codex 本地 marketplace 和插件生命周期脚本。

显示的百分比是 Codex rate-limit window，不是账单、消费金额或账户余额。

## 下载与 SHA-256 校验

### 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11 x64 |
| Codex | 已安装 Codex Desktop，并在当前 Windows 用户下登录 |
| 管理员权限 | 不需要；默认按当前用户运行和安装 |
| 运行时 | .NET Framework 4.x（项目目标为 4.0；Windows 10/11 通常已包含兼容运行时） |
| Windows 32 位 | 不支持 |
| Windows on ARM | 可能通过 x64 模拟运行，但尚未正式验证 |

前往项目的 [GitHub Releases](../../releases/latest)，从同一个 Release 下载：

```text
CodexUsageMonitor.exe
SHA256SUMS.txt
```

把两个文件放在同一目录，在该目录打开 Windows PowerShell，执行：

```powershell
$record = Get-Content .\SHA256SUMS.txt |
    Where-Object {
        $_ -match '^[0-9A-Fa-f]{64}\s+\*?CodexUsageMonitor\.exe$'
    } |
    Select-Object -First 1

if (-not $record) {
    throw 'SHA256SUMS.txt 中没有有效的 CodexUsageMonitor.exe 记录。'
}

$expected = (($record -split '\s+')[0]).ToUpperInvariant()
$actual = (Get-FileHash .\CodexUsageMonitor.exe -Algorithm SHA256).Hash

if ($actual -ne $expected) {
    throw "SHA-256 校验失败。Expected: $expected; Actual: $actual"
}

"SHA-256 OK: $actual"
```

只有显示 `SHA-256 OK` 才继续运行。SHA-256 用于确认下载文件与 Release 发布的清单一致；
仍应确认下载页面属于可信仓库。

当前公开构建可能未进行代码签名，因此 Windows SmartScreen 可能显示警告。校验哈希后，
再根据自己的安全策略决定是否运行；程序本身不需要管理员权限。

## 直接运行

1. 安装并登录 Codex Desktop。
2. 按上一节校验 SHA-256。
3. 双击 `CodexUsageMonitor.exe`。
4. 程序启动后会进入系统托盘并显示悬浮窗。

EXE 是单文件程序，不依赖同目录 DLL。第二次启动只会唤醒已有窗口，不会创建重复监控
实例。关闭悬浮窗默认隐藏到托盘；要完全退出，请使用托盘菜单中的“退出”。

如果 Codex 没有安装、尚未登录或当前版本不兼容，悬浮窗会显示可恢复状态并自动重试，
不会修改 Codex 账号状态。

## 按当前用户安装

GitHub Release 中的 `CodexUsageMonitor-windows-x64.zip` 只包含 EXE 和
`SHA256SUMS.txt`。安装脚本位于源码仓库的 `scripts\`；下载或克隆源码后，在仓库
根目录打开 Windows PowerShell，并把 Release 中已校验的 EXE 路径传给安装脚本：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

$downloadedExe = Join-Path `
    $env:USERPROFILE `
    'Downloads\CodexUsageMonitor.exe'

.\scripts\Install-CodexUsageMonitor.ps1 `
    -SourceExecutable $downloadedExe `
    -WhatIf
.\scripts\Install-CodexUsageMonitor.ps1 `
    -SourceExecutable $downloadedExe `
    -EnableStartup `
    -Start
```

如果浏览器下载目录不同，请修改 `$downloadedExe`。安装脚本会自动读取 EXE 同目录的
`SHA256SUMS.txt`；清单存在时，哈希不一致会立即停止安装。

默认安装位置：

```text
%LOCALAPPDATA%\Programs\CodexUsageMonitor
```

安装不会请求管理员权限，也不会复制 Codex 登录资料。升级前会保存上一版 EXE，便于
回滚：

```powershell
.\scripts\Rollback-CodexUsageMonitor.ps1
```

卸载程序但保留界面设置和回滚数据：

```powershell
.\scripts\Uninstall-CodexUsageMonitor.ps1
```

同时删除本项目的设置、错误日志和回滚数据：

```powershell
.\scripts\Uninstall-CodexUsageMonitor.ps1 -RemoveSettings
```

安装脚本只接受安全的本机目录，拒绝 UNC、device namespace、符号链接、junction、
mount point 及带这些 reparse point 的祖先目录。非空且没有有效项目标记的目录不会被
接管；卸载只删除本项目拥有的 allowlist 文件，不递归清空安装目录。

`-EnableStartup` 默认拒绝覆盖指向其他程序的同名 HKCU Run 值。只有确认需要接管时才
使用 `-ForceStartupReplace`；卸载时也只会在启动项仍属于本次安装的情况下恢复旧值。

## 使用 Codex 本地插件

仓库根目录是一个本地 Codex marketplace，`.agents` 是必需的隐藏目录。由于 Git
仓库不提交 EXE，使用源码 checkout 安装插件前，需要先从 GitHub Release 下载并校验
`CodexUsageMonitor.exe`。以下命令假定已把校验通过的 EXE 放入本地 `release\`
staging；该文件只用于本地装配，不提交到 Git：

```powershell
Copy-Item `
    .\release\CodexUsageMonitor.exe `
    .\plugins\codex-usage-monitor\assets\CodexUsageMonitor.exe `
    -Force
```

如果使用 GitHub Release 提供的完整插件发布包，上述文件应已装配完成，无需再次复制。
安装 marketplace 和插件：

```powershell
codex plugin marketplace add (Resolve-Path '.').Path
codex plugin add codex-usage-monitor@codex-usage-monitor-local
```

安装或更新后，请新建一个 Codex 任务，让 Codex 加载最新版 skill 和脚本。之后可以在
Codex 中要求：

- 启动 Codex Usage Monitor
- 停止 Codex Usage Monitor
- 查看运行状态
- 启用或禁用开机启动

插件使用本地脚本控制随包 EXE，不增加账号、网络服务或新的认证流程。插件安装与独立
程序安装互不替代；如果两者都已准备，只需要运行其中一份 EXE，应用会保持单实例。
插件卸载请使用 Codex Desktop 的插件管理界面。

## 四边半圆吸附操作

启用托盘菜单中的“侧边吸附”后，把完整悬浮窗拖到当前屏幕工作区边缘：

| 吸附边缘 | 紧凑形态 |
| --- | --- |
| 左边 | 纵向半圆，圆弧和数字朝屏幕内侧 |
| 右边 | 纵向半圆，圆弧和数字朝屏幕内侧 |
| 上边 | 横向半圆，圆弧和数字朝屏幕内侧 |
| 下边 | 横向半圆，圆弧和数字朝屏幕内侧 |

操作规则：

- 吸附成功后自动缩成圆球式半圆，只显示剩余数字。
- 鼠标悬停可查看完整百分比和窗口信息。
- 双击半圆可恢复完整面板。
- 按住半圆向屏幕内部拖离边缘，可恢复完整面板并继续移动。
- 拖到角落时只选择一个最近的主边；距离相同时优先左右边，避免同时锁住两个方向。
- 切换显示器、分辨率、DPI 或任务栏工作区后，窗口会重新限制在可见区域。
- 不需要半圆模式时，可在托盘菜单中关闭“侧边吸附”。

若半圆数字显示不全，请先确认使用的是最新 GitHub Release；项目的验收目标覆盖
`100`、`--` 和 `∞` 在常见 100% 至 200% Windows 缩放下完整显示。

## 隐私、数据与兼容性

程序通过本机 `codex app-server` 读取当前 Codex Desktop 会话可见的 rate-limit 数据。
应用自身：

- 不要求或保存 API key。
- 不读取浏览器 cookie、OAuth token 或浏览器存储。
- 不访问另一个 Windows 用户的目录。
- 不包含独立遥测、广告、第三方分析 SDK 或自动更新器。
- 不把原始账号响应、子进程 stderr 或环境信息写入发布包。

本地可能产生的数据：

```text
%LOCALAPPDATA%\CodexUsageMonitor\settings.json
%LOCALAPPDATA%\CodexUsageMonitor\last-error.txt
%LOCALAPPDATA%\CodexUsageMonitor\rollback\
%LOCALAPPDATA%\Programs\CodexUsageMonitor\.codex-usage-monitor-install.json
```

`settings.json` 只保存界面偏好。`last-error.txt` 仅在未处理异常时生成，可能包含本机
路径或异常文本，公开提交前必须先检查。安装标记保存产品 ownership、哈希和必要的启动
项恢复信息，不保存 Codex 凭据或账号数据。完整说明见
[隐私说明](docs/PRIVACY.md) 和 [安全策略](SECURITY.md)。

程序优先从当前用户的 Codex Desktop 安装目录定位 `codex.exe`，然后尝试 `PATH`。
环境变量 `CODEX_USAGE_MONITOR_CODEX_EXE` 是显式可信覆盖，相当于允许程序执行指定
文件，只应指向用户验证过的 `codex.exe`。

当前项目以 Windows 10/11 x64 为目标，并已完成 Windows 11 x64 开发机上的构建和
本机回归；尚未在第二台干净 Windows 10/11 电脑完成实机验收。Windows 8/8.1、
Windows Server、Wine 和 ARM 原生运行不是当前承诺范围，实际兼容性还受 Codex
Desktop 自身支持范围影响。

## 从源码构建

准备环境：

- Windows x64
- Windows PowerShell 5.1 或更高版本
- Visual Studio 2019/2022 或对应 Build Tools
- `.NET desktop build tools`
- MSBuild，可通过 `PATH` 或 Visual Studio Installer 的 `vswhere.exe` 定位
- 首次构建可访问 `nuget.org`，用于还原官方的 .NET Framework 4.0 reference assemblies

执行 Release 构建：

```powershell
.\src\CodexUsageMonitor\build.ps1 -Configuration Release
```

输出位置：

```text
src\CodexUsageMonitor\bin\x64\Release\CodexUsageMonitor.exe
```

项目目标框架为 .NET Framework 4.0。构建时会还原
`Microsoft.NETFramework.ReferenceAssemblies.net40` 1.0.3；它只提供编译期引用程序集，
不会复制到发布目录，也不会给 EXE 增加运行依赖。构建完成后可计算哈希：

```powershell
Get-FileHash `
    .\src\CodexUsageMonitor\bin\x64\Release\CodexUsageMonitor.exe `
    -Algorithm SHA256
```

`bin/`、`obj/` 和构建 EXE 都是本地生成物，不应加入 Git。Release 配置不需要 PDB 才
能运行；PDB 可能包含源码文件名、行号和构建路径，默认不公开。

## 仓库结构与文档

```text
.agents/plugins/marketplace.json        本地 marketplace 定义
docs/                                   架构、隐私、兼容和发布文档
plugins/codex-usage-monitor/             Codex 插件源码与公共图片
release/                                 本地发布 staging；Git 中只保留说明文件
scripts/                                 安装、回滚和卸载脚本
src/CodexUsageMonitor/                   可复现构建源码
```

主要文档：

- [架构说明](docs/ARCHITECTURE.md)
- [隐私说明](docs/PRIVACY.md)
- [Windows 兼容与排障](docs/WINDOWS-COMPATIBILITY.md)
- [源码打包 allowlist](docs/SOURCE-PACKAGING.md)
- [发布检查清单](docs/RELEASE-CHECKLIST.md)
- [版本记录](CHANGELOG.md)
- [安全策略](SECURITY.md)
- [贡献指南](CONTRIBUTING.md)

## 开源许可与项目定位

源码按 [MIT License](LICENSE) 发布。贡献和安全问题请分别参阅
[贡献指南](CONTRIBUTING.md) 与 [安全策略](SECURITY.md)。

这是非官方社区工具。项目不得暗示自己是 OpenAI 或 Codex Desktop 的官方组成部分，
也不得把 OpenAI、Codex 名称或图标用于误导性的背书、签名或发布者身份声明。

## 打包 EXE 与发布流程

本仓库采用“源码进 Git，二进制进 GitHub Release”的发布策略。

**Git 仓库不得提交 `.exe`、`.zip`、`.pdb`、`bin/`、`obj/` 或其他构建产物。**
`release/` 和插件 `assets/` 中的 EXE 只在本地发布 staging 或 CI Release 作业中临时
装配。最终构建产物只上传到 GitHub Release，不进入源码提交或 Git 历史。

### 维护者发布步骤

1. 从干净的源码 checkout 构建 Windows x64 Release。
2. 在源码目录之外创建新的 allowlist staging，禁止递归复制整个开发工作区。
3. 把同一份最终 EXE 分别装配为 standalone 文件和插件资产；两份 SHA-256 必须一致。
4. 排除 PDB、`bin/obj`、日志、设置、转储、探针、账号数据、证书和开发机路径。
5. 如果使用代码签名，先完成签名并验证，再对最终 EXE 计算 SHA-256。
6. 生成 `SHA256SUMS.txt`，运行安装、回滚、卸载、插件 validator 和敏感信息检查。
7. 从已验证 staging 生成可选的 Windows x64 ZIP；不要从工作区直接压缩。
8. 创建带版本标签的 GitHub Release，并上传发布资产。

建议的 GitHub Release 资产：

```text
CodexUsageMonitor.exe
SHA256SUMS.txt
CodexUsageMonitor-windows-x64.zip        独立 EXE 与其校验清单
codex-usage-monitor-plugin.zip           已装配 EXE 的完整 Codex 插件
```

PDB 不是运行依赖，默认既不进入 ZIP，也不上传 GitHub Release。如果未来确需公开调试
符号，必须先检查源码路径和本机信息，并作为独立 symbols 资产发布。

在仓库根目录可执行完整本地打包：

```powershell
# 只预览目标，不构建、不写文件
.\scripts\Build-ReleaseArtifacts.ps1 -WhatIf

# 构建并在仓库外的系统临时目录生成四个发布资产
$release = .\scripts\Build-ReleaseArtifacts.ps1
$release.OutputDirectory
$release.ArtifactFiles

# 查看最终 SHA-256 清单
Get-Content `
    (Join-Path $release.OutputDirectory 'SHA256SUMS.txt')
```

脚本会重新构建 `Release|x64`，在仓库外创建唯一 staging，验证插件清单、ZIP 内容和
EXE 一致性，删除临时 staging 后只留下上表四个文件。推送版本标签后，
`.github/workflows/release.yml` 会在 GitHub 的 Windows runner 上重复同一流程并创建
Release：

```powershell
git tag -a v1.1.0 -m 'Codex Usage Monitor v1.1.0'
git push origin v1.1.0
```

发布前必须完成 [发布检查清单](docs/RELEASE-CHECKLIST.md)，并确认 Git 暂存区和提交中
没有任何 EXE、ZIP、PDB、本机路径、账号数据或凭据。
