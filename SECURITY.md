# Security Policy

## Supported versions

安全修复只面向最新发布版本。旧版本应先升级再复现问题。

## Reporting a vulnerability

请优先使用 GitHub 的 **Private vulnerability reporting** 或私有 Security Advisory
报告漏洞。不要在公开 issue 中粘贴以下内容：

- Codex 或 OpenAI 的 cookie、OAuth token、API key
- `settings.json`、`last-error.txt` 的未经检查原文
- 用户目录、账号标识或内部安装路径
- 能直接复现凭据泄露的完整请求和响应

报告应包含受影响版本、Windows 版本、最小复现步骤、预期影响和已经采取的缓解措施。
维护者确认前，请不要公开利用细节。

## Security boundary

Codex Usage Monitor：

- 只应启动本机可信的 `codex.exe`。
- 通过本地子进程标准输入/输出调用 `codex app-server`。
- 只解析 rate-limit 数据，不需要 API key。
- 不应读取浏览器存储、复制登录资料或访问另一个 Windows 用户的目录。
- 不包含独立遥测、广告、远程更新器或第三方分析 SDK。
- 只在用户启用开机启动时写入当前用户的 `HKCU` Run 项。
- 启动项冲突默认拒绝；显式强制接管时保存旧命令，仅在仍持有该 Run 值时恢复，并在
  每次注册表变更前重新读取 ownership。
- 安装和删除拒绝 UNC、device namespace 与任一祖先 reparse point，并在变更前复检。
- 只有绑定当前规范化目录的有效格式版本 2 标记才授予安装 ownership。
- 卸载只删除 owned allowlist，且只在目录为空时删除目录本身，不递归清空安装根。

`CODEX_USAGE_MONITOR_CODEX_EXE` 可以覆盖自动定位结果。该环境变量等价于允许程序执行
指定文件，必须只指向来源可信、由用户验证过的 `codex.exe`。

## Release integrity

正式发布应：

1. 在干净 Windows x64 环境中从公开源码构建。
2. 生成并发布 `SHA256SUMS.txt`。
3. 排除 `bin/`、`obj/`、PDB、日志、设置和账号数据。
4. 对最终 staging 目录重新执行敏感信息扫描。
5. 验证插件清单和 marketplace。
6. 如有代码签名证书，对最终 EXE 签名并验证签名；证书和私钥不得进入仓库。

未签名 EXE 可能触发 SmartScreen。这是分发信誉风险，不代表程序需要管理员权限。
