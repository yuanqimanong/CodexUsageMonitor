# Privacy

## 结论

Codex Usage Monitor 不需要用户填写账号、密码或 API key。它复用当前 Windows 用户已
登录的 Codex Desktop，通过本地 `codex app-server` 只获取界面需要的 rate-limit
window。

## 处理的数据

| 数据 | 用途 | 持久化 |
| --- | --- | --- |
| rate-limit 已用比例 | 绘制百分比和图标 | 否，仅内存 |
| rate-limit 重置时间 | 显示倒计时/重置时间 | 否，仅内存 |
| 窗口位置、主题、置顶、吸附开关 | 恢复 UI | 是，`settings.json` |
| 未处理异常文本 | 本地排障 | 仅异常时写入 `last-error.txt` |
| EXE 版本和进程 ID | 本地状态检查 | 否 |
| 开机启动命令 | 当前用户登录时启动 | 是，HKCU Run 项 |
| 安装标记与可选旧启动命令 | 安全升级、卸载和恢复被显式替换的 Run 值 | 是，程序安装目录 |

## 不应收集的数据

- OpenAI/Codex 密码、cookie、OAuth token、API key
- Authorization header 或浏览器存储
- 邮箱、账号 ID、付款信息或账单内容
- 聊天内容、提示词、源码内容或工作区文件
- 另一个 Windows 用户的资料
- 原始 app-server 响应日志

如果未来功能必须新增上述数据类型，应先更新架构和本隐私说明，并重新进行安全审查；
当前版本没有这类授权。

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
