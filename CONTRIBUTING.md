# Contributing

## 开发环境

- Windows 10/11 x64
- PowerShell 5.1+
- Visual Studio Build Tools 2019/2022
- MSBuild、.NET Framework 4.0 targeting pack 和桌面构建工具
- 已安装并登录的 Codex Desktop（仅集成测试需要）

## 提交流程

1. 从新分支修改源码。
2. 执行 Release 构建。
3. 在至少一台非开发机的 Windows x64 环境做便携性验证。
4. 验证悬浮窗、托盘、侧边吸附、紧凑模式、退出和开机启动。
5. 验证 Codex 不可用、未登录、输出异常和超时等失败路径。
6. 检查最终差异和发布目录中的敏感信息。

## 隐私要求

禁止提交：

- 任何真实账号 ID、邮箱、cookie、OAuth token、API key 或 Authorization header
- 本机用户目录、版本哈希目录或开发者专用绝对路径
- `%LOCALAPPDATA%\CodexUsageMonitor` 下的设置和日志
- 捕获自真实账号的原始 app-server 请求/响应
- `bin/`、`obj/`、PDB、崩溃转储、签名私钥或证书

测试数据必须是最小化、虚构且可公开的数据。记录 app-server 输出时，只保留测试必需
字段并做白名单化。

## 代码规则

- 保持 .NET Framework 4.0、Windows 10/11 x64 和 C# 7.3 兼容。
- 保持 Windows x64 单文件 Release 输出。
- 新路径必须通过系统目录 API、环境变量或相对路径解析。
- 不要绑定 Windows 用户名、Codex 安装版本目录或开发机盘符。
- 更新状态脚本时，保持 `ProcessIds` 为数组，避免机器可读接口漂移。
- 进程、注册表和文件删除必须限定到当前项目的明确目标。
- 安装路径变更必须保持祖先 reparse-point 检查；卸载不得恢复为递归删除安装根。
- 新的 HKCU Run 写入不得覆盖不同目标，除非显式强制并提供可验证的恢复语义。
- 文件与 Run 值必须在实际写入或删除前复检，不能只依赖早期预检结果。
- 生命周期脚本必须在 Windows PowerShell 5.1 下解析和回归，不得使用仅 PowerShell 7
  提供的参数。
- 新增本地持久化字段时同步更新 `docs/PRIVACY.md`。

## 发布前

逐项完成 [发布检查清单](docs/RELEASE-CHECKLIST.md)。不要把本地构建目录直接复制到
GitHub-ready 目录；只复制源文件和经过校验的 Release EXE。
