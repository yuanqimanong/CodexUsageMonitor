# Codex Usage Monitor

一个放在屏幕边缘的 Codex 小面板。平时收成贴边半圆，鼠标移上去就展开。你可以看剩余额度，也可以同时查看几个正在运行的会话及其 Token 速度。

适用于 **Windows 10 / 11 x64**。电脑上需要已经安装并使用过 Codex，查看额度时还需要登录 Codex Desktop。

## 能看什么

- **额度** — 剩余百分比、限额窗口和重置倒计时。
- **会话卡片** — 每个运行中的会话占一张卡，名称、模型、状态和 Token/s 直接显示。5 条以内随数量调整高度，超过 5 条时上下滚动。屏幕高度不足时也可滚动查看。
- **Token** — 每张卡平铺输入、缓存输入、输出和思考输出的累计数字。
- **桌面操作** — 四边吸附、悬停展开、保持展开、系统托盘、置顶和开机启动。
- **三种外观** — 黑白极简、雾灰、赛博朋克，均采用不透明底色。

## 下载并启动

1. 到 [下载页](https://github.com/yuanqimanong/CodexUsageMonitor/releases/latest)，下载 `CodexUsageMonitor-windows-x64.zip` 并完整解压。
2. 核对包内 `SHA256SUMS.txt` 中的校验值，确认下载完整。
3. 双击 `CodexUsageMonitor.exe`，面板会出现在屏幕边缘。
4. 鼠标移到半圆上，点击“会话”，正在运行的会话会自动出现。

程序无需管理员权限、Python 或 API Key。Windows 10 / 11 通常已包含所需的 .NET Framework 4.x。Windows on ARM 的 x64 模拟运行尚未验证。

<details>
<summary>如何核对下载文件</summary>

在解压后的文件夹打开 PowerShell，运行以下命令。将结果与 `SHA256SUMS.txt` 中 `CodexUsageMonitor.exe` 对应的值比较，一致后再运行。

```powershell
Get-FileHash .\CodexUsageMonitor.exe -Algorithm SHA256
```

部分发布版本没有代码签名，Windows SmartScreen 可能显示提示。请确认下载来源和校验值，再决定是否运行。

</details>

## 悬浮窗怎么用

| 操作 | 效果 |
| --- | --- |
| 拖动面板顶部到屏幕边缘，松开鼠标 | 立即吸附并缩成贴边半圆 |
| 鼠标停在半圆上 | 短暂停留后展开，也可双击展开 |
| 鼠标移出面板 | 稍候自动收起 |
| 点击顶部的 `◇` | 保持展开，再点一次恢复自动收起 |
| 点击 `◈`，或右键半圆选择“切换皮肤” | 依次切换三种外观 |
| 点击 `◁` | 立即收起到边缘 |
| 拖住半圆向屏幕内移动 | 脱离吸附，恢复完整面板 |
| 点击 `×` | 隐藏到系统托盘 |
| 双击托盘图标 | 显示面板 |
| 右键托盘图标 | 刷新、置顶、切换主题、开关吸附或开机启动，也可退出程序 |

面板会记住上次的页面、位置和主题。完全退出请使用托盘菜单中的“退出”。

额度和会话页宽度相同，切换时只调整高度。拖到不同缩放比例的显示器后，面板和文字会一起调整大小，收起后仍是贴边半圆。

## 会话卡片怎么看

每张卡对应一条正在运行的主会话。名称与 Codex 侧栏一致，重命名后会随刷新更新。顶部还显示模型、运行状态，以及最近一次模型调用的 **Token/s**。下方直接显示四类 Token 的会话累计值。

| 数字 | 含义 |
| --- | --- |
| 输入 | 发给模型的输入 Token，含缓存输入 |
| 缓存输入 | 输入中命中缓存的部分 |
| 输出 | 模型生成的 Token，含思考输出 |
| 思考输出 | 输出中用于推理的部分 |

`k` 表示千，`M` 表示百万，鼠标移到数字上可看完整值。合计只需把输入和输出相加。套餐剩余额度在“额度”页查看。

**速度随已完成的模型调用更新。** 显示值是输出 Token 数除以本地日志中可识别的响应耗时，包含等待和思考，扣除可识别的工具执行时间。新会话尚未完成首次调用，或日志缺少时间边界时，显示 `—`。

面板每 3 秒刷新一次，会话完成或中断后自动移除卡片。列表依据 Codex 当前会话清单，过滤已删除、已归档和后台审查会话。连续 5 分钟没有新记录的会话也会隐藏，长时间思考或运行工具的会话可能暂时消失，后续写入新记录时恢复。

收起、隐藏或切回额度页后暂停读取会话数据。打开面板时只读近期日志，再跟随新增记录更新。

## 没有出现会话怎么办

默认读取环境变量 `CODEX_HOME`，未设置时使用当前用户的 `.codex` 目录。也可以点击“数据目录”，选择包含 `state_*.sqlite` 和 `sessions` 的 Codex 数据目录。

| 现象 | 可以这样处理 |
| --- | --- |
| 显示“还没有正在运行的会话” | 在本机 Codex 中发一条消息，等待几秒 |
| 提示读不到会话清单 | 检查数据目录及权限，再点击 `↻` |
| 速度或某类 Token 显示 `—` | 等待一次模型调用完成，旧日志可能缺少对应字段 |
| 额度一直在连接 | 检查 Codex Desktop 是否已安装并登录，稍后刷新 |
| 面板收得太快 | 点击 `◇` 保持展开，或在托盘菜单关闭“屏幕边缘吸附” |

目前读取本机 JSONL 日志。仅保存在云端的会话和压缩日志暂不支持。

## 作为 Codex 插件使用

从下载页获取 `codex-usage-monitor-plugin.zip`，完整解压。在包含隐藏目录 `.agents` 的解压目录中打开 PowerShell，运行以下命令。

```powershell
codex plugin marketplace add (Resolve-Path '.').Path
codex plugin add codex-usage-monitor@codex-usage-monitor-local
```

安装或更新后，新建一个 Codex 聊天。你可以直接说“启动 Codex Usage Monitor”“查看运行状态”“停止 Codex Usage Monitor”或“启用开机启动”。

运行中的会话显示在悬浮窗的“会话”页。插件负责启动和管理这个桌面面板。卸载插件可使用 Codex 的插件管理界面。

## 更新、回退和卸载

**直接运行版**可以先从托盘退出，再用新版 EXE 替换旧文件。保留旧版 EXE，遇到问题时退出新版并换回即可。界面设置保存在 `%LOCALAPPDATA%\CodexUsageMonitor\settings.json`。

如果使用仓库附带的安装脚本，请在仓库目录中运行以下命令，并将路径换成已下载、校验过的 EXE。

```powershell
.\scripts\Install-CodexUsageMonitor.ps1 -SourceExecutable 'C:\Downloads\CodexUsageMonitor.exe' -Start
```

脚本默认安装到 `%LOCALAPPDATA%\Programs\CodexUsageMonitor`，升级时保存上一版。需要回退或卸载时，使用对应命令。

```powershell
# 回退到上一版并启动
.\scripts\Rollback-CodexUsageMonitor.ps1 -Start

# 卸载，保留设置
.\scripts\Uninstall-CodexUsageMonitor.ps1

# 卸载并删除本工具的设置、错误日志和回滚副本
.\scripts\Uninstall-CodexUsageMonitor.ps1 -RemoveSettings
```

## 数据与隐私

会话统计在本机完成。程序以只读方式打开 Codex 会话数据库，读取当前会话的名称、日志路径和模型，并跟随活跃会话的 JSONL 日志。解析会经过包含消息和工具内容的行，这些内容处理后即丢弃。统计只保存在内存中，界面偏好会写入本机设置文件。

额度通过本机 `codex app-server` 获取，由 Codex 按正常流程连接服务。会话统计本身无需联网。分享截图前，请留意其中的真实聊天名称。异常日志可能包含本机路径，提交问题前请检查。[隐私说明](docs/PRIVACY.md)

## 致谢与许可

会话面板的功能和统计呈现参考了 [Codex Pulse](https://github.com/Yamazzzx/codex-speed-panel) 与 [Agent Speed](https://github.com/zoefix/agent-speed)。

本项目由社区维护，采用 MIT License，与 OpenAI 无隶属关系。[许可证](LICENSE)
