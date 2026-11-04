# Privacy

## 结论

Codex Usage Monitor 不需要用户填写账号、密码或 API key。它复用当前 Windows 用户已
登录的 Codex Desktop，通过本地 `codex app-server` 获取界面需要的 rate-limit
window。聊天页以只读方式读取当前用户选择的 Codex 数据目录，在本机统计速度和 Token 用量。

## 处理的数据

| 数据 | 用途 | 持久化 |
| --- | --- | --- |
| rate-limit 已用比例 | 绘制百分比和图标 | 否，仅内存 |
| rate-limit 重置时间 | 显示倒计时/重置时间 | 否，仅内存 |
| 现存会话名称、日志路径、模型、事件时间、Token 数字 | 运行状态、速度和用量统计 | 否，仅内存 |
| 窗口位置、主题、置顶、吸附开关、沿边位置、页面、数据目录 | 恢复 UI | 是，`settings.json` |
| 未处理异常文本 | 本地排障 | 仅异常时写入 `last-error.txt` |
| EXE 版本和进程 ID | 本地状态检查 | 否 |
| 开机启动命令 | 当前用户登录时启动 | 是，HKCU Run 项 |
| 安装标记与可选旧启动命令 | 安全升级、卸载和恢复被显式替换的 Run 值 | 是，程序安装目录 |

## 不应收集的数据

- OpenAI/Codex 密码、cookie、OAuth token、API key
- Authorization header 或浏览器存储
- 邮箱、账号 ID、付款信息或账单内容
- 聊天正文、提示词、工具参数和结果的持久副本
- 另一个 Windows 用户的资料
- 原始 app-server 响应日志

会话页通过 Windows 自带的 SQLite 以只读方式打开 `state_*.sqlite`，查询现存主会话的
编号、名称、模型、日志路径、更新时间和归档标记。只跟随其中近期更新的 `sessions` JSONL 日志，
不从遗留日志重建已删除会话。数据库读取使用正常 WAL 视图，可能由 SQLite 协调共享内存锁。
解析会经过包含消息和工具内容的行，临时对象处理后丢弃，仅保留统计所需的时间、计数和工具编号。
数据目录来自手动选择、`CODEX_HOME` 或当前用户的 `.codex`。
会话页不可见时暂停新的读取。真实名称会显示在面板中，公开截图前请检查。

## 数据位置

当前用户本地目录：

```text
%LOCALAPPDATA%\CodexUsageMonitor\settings.json
%LOCALAPPDATA%\CodexUsageMonitor\last-error.txt
%LOCALAPPDATA%\CodexUsageMonitor\rollback\
```

当前用户注册表：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexUsageMonitor
```

开机启动默认不强制开启。禁用或卸载时，仅当该值仍指向本项目安装路径才会移除。
如果用户显式使用 `-ForceStartupReplace`，安装标记会保存原 Run 命令，并在安全卸载时
恢复；普通安装不会保存其他程序的命令。恢复字段只在格式版本 2、安装路径和其他固定
字段全部有效时使用；实际恢复前还会再次确认当前 Run 值仍由本次安装持有。

## 网络边界

应用代码自身不实现独立 HTTP 客户端、遥测、广告或自动更新服务。它启动本地
`codex app-server` 子进程；该子进程会按照 Codex Desktop 的正常认证和服务流程与
OpenAI 通信。用户仍受其 Codex 产品条款和隐私政策约束。

## 日志风险

`last-error.txt` 可能包含异常堆栈、文件名或本机路径。它不应主动记录凭据，但公开
提交前仍需人工检查并移除：

- Windows 用户名和用户目录
- 内部项目路径
- 账号标识
- token、cookie、header 和原始响应

## 移除数据

保留设置卸载：

```powershell
.\scripts\Uninstall-CodexUsageMonitor.ps1
```

删除程序及本项目的设置、日志、回滚副本：

```powershell
.\scripts\Uninstall-CodexUsageMonitor.ps1 -RemoveSettings
```

`-RemoveSettings` 不会删除 Codex Desktop 的登录状态、配置或聊天数据。
