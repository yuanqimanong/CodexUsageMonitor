# Release checklist

## 1. 源码与隐私

- [ ] 最终目录不包含真实用户名、邮箱、账号 ID、token、cookie、API key。
- [ ] 不包含开发机绝对路径、Codex 安装版本哈希或临时目录。
- [ ] 不包含真实账号捕获的请求/响应或研究探针。
- [ ] `bin/`、`obj/`、PDB、日志、设置、转储、证书和签名私钥均已排除。
- [ ] GitHub code 仓库不跟踪任何 `*.exe`、`*.zip`、`*.pdb` 或 release staging 内容。
- [ ] `release/CodexUsageMonitor.exe` 和 `plugins/codex-usage-monitor/assets/CodexUsageMonitor.exe` 均未被重新纳入源码仓库。
- [ ] `CODEX_USAGE_MONITOR_CODEX_EXE` 只出现在通用定位代码和文档，不包含实际机器值。
- [ ] 对 GitHub-ready 目录重新执行全文敏感信息扫描。
- [ ] `git ls-files` 的禁止项检查无输出；确认 `.gitignore` 之外没有已跟踪的历史二进制。

## 2. 可复现构建

- [ ] 从干净源码执行 x64 Release 构建。
- [ ] 构建脚本不依赖用户名、盘符或单一 Visual Studio 版本路径。
- [ ] Release 目录只有运行所需文件。
- [ ] 在另一台 Windows 10/11 x64 电脑验证，不依赖开发机缓存。
- [ ] EXE 以 `asInvoker` 运行，不请求管理员权限。

## 3. 功能

- [ ] Codex Desktop 已安装并登录时能读取 rate-limit。
- [ ] Codex 未安装、未登录、离线、超时和子进程退出时有可恢复提示。
- [ ] 第二次启动只显示现有实例。
- [ ] 正常关闭隐藏到托盘；托盘退出和 `--shutdown` 能释放子进程。
- [ ] 开机启动启用/禁用只修改当前用户的匹配 Run 项。
- [ ] 三种主题均可切换；毛玻璃不可用时不出现不可读黑块。
- [ ] 左右纵向半圆、上下横向半圆均可吸附、双击展开、拖离和继续移动。
- [ ] 角落只选择一个主边；菜单跨方向切换不残留旧的正交边。
- [ ] `100`、`--`、`∞` 在 100% 至 200% 缩放下完整显示。
- [ ] 多显示器插拔、分辨率变化和任务栏工作区变化后窗口仍可见。

## 4. 安装与回滚（仓库外 Release staging）

- [ ] 在仓库外发布目录直接运行 `CodexUsageMonitor.exe` 成功。
- [ ] `Install-CodexUsageMonitor.ps1 -WhatIf` 目标正确。
- [ ] 默认 per-user 安装不需要管理员权限。
- [ ] 升级前生成有效回滚副本和 metadata。
- [ ] Rollback 能恢复上一 EXE，且保留回滚前的当前 EXE。
- [ ] Uninstall 默认保留设置，`-RemoveSettings` 只删除本项目 LocalAppData。
- [ ] 不同路径的同名开机启动项不会被误删。
- [ ] 非空且没有有效安装标记的自定义目录会被拒绝。
- [ ] 安装与卸载拒绝 UNC、device namespace 和任一祖先 reparse point。
- [ ] 卸载只删除 owned allowlist；无法识别的文件和非空目录会被保留。
- [ ] 启动项冲突默认拒绝；显式强制替换后可在卸载时恢复原值。
- [ ] 损坏、路径不匹配、旧格式或恢复字段异常的安装标记不会授予目录 ownership。
- [ ] Run 值在实际写入/删除前复检；确认后出现的冲突不会被覆盖。
- [ ] 顶层和插件生命周期脚本通过 Windows PowerShell 5.1 语法及安全回归。

## 5. 插件

- [ ] `.codex-plugin/plugin.json` 为有效 UTF-8 JSON，无占位符和乱码。
- [ ] 插件目录名、manifest 名和 marketplace 名均为 `codex-usage-monitor`。
- [ ] marketplace 包含 `policy.installation`、`policy.authentication` 和 `category`。
- [ ] Git 跟踪的插件目录只包含 manifest、skill、scripts 和 PNG/ICO 图标，不包含 EXE。
- [ ] 在仓库外组装 Release 插件包时，临时 `assets/CodexUsageMonitor.exe` 与 standalone Release EXE 的 SHA-256 相同。
- [ ] `assets/icon.png` 和 `assets/logo.png` 存在。
- [ ] skill 文档使用 `ProcessIds`，与状态脚本一致。
- [ ] plugin validator 和 skill quick validator 均通过。
- [ ] 安装/更新插件后在新 Codex 任务中验证。

## 6. GitHub Release 发布物

- [ ] GitHub code 仓库只包含源码、MIT `LICENSE`、文档、PNG/ICO 图标、插件 manifest/skill/scripts。
- [ ] 所有 EXE、ZIP、PDB、`bin/`、`obj/` 和 staging 输出均位于源码仓库外。
- [ ] 在仓库外发布目录生成 `SHA256SUMS.txt`。
- [ ] SHA-256 使用最终复制后的 EXE 计算。
- [ ] 如使用代码签名，签名发生在计算最终 SHA-256 之前。
- [ ] 验证数字签名；私钥与证书不进入仓库。
- [ ] ZIP 或 GitHub Release 资产从同一份已验证 staging 目录产生。
- [ ] GitHub Release 上传完成后，源码工作树仍干净，`git ls-files` 禁止项检查仍通过。
- [ ] README、LICENSE、SECURITY、CONTRIBUTING 和隐私文档齐全。

## PDB policy

PDB 用于调试符号解析，不是程序运行依赖。它可能包含源码文件名、行号、编译器信息和
构建路径，因此默认不公开，也不“打包进 EXE”。如果未来需要公开 symbols，应单独
构建、审查路径信息，并作为独立调试资产发布。
