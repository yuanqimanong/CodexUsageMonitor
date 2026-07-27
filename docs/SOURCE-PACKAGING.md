# Source packaging allowlist

GitHub-ready 源码应使用 allowlist 复制，不能直接递归复制开发目录。

## GitHub code 与 GitHub Release 的边界

GitHub code 仓库只保存可审查、可复现构建的源码和文本元数据。允许进入仓库的内容包括：

- `src/` 下的 C#、XAML、项目文件、构建脚本和主题资源。
- MIT `LICENSE`、`README.md`、`SECURITY.md`、`CONTRIBUTING.md` 和 `docs/`。
- 图标源文件 `*.png`、`*.ico` 及图标生成脚本。
- `.codex-plugin/plugin.json`、插件 manifest、skill 文档和生命周期脚本。

GitHub Release 保存由干净源码构建并经过验证的可执行发布物。`*.exe`、`*.zip`、`*.pdb`、
`bin/`、`obj/`、release staging 目录以及插件 assets 中的 EXE 均不得提交到 GitHub code
仓库。发布插件时，在仓库外的临时 staging 目录中组装
`plugins/codex-usage-monitor/assets/CodexUsageMonitor.exe`，校验后再生成 ZIP 或上传 Release
资产；不要把该 EXE 复制回源码树。

## Include

```text
LICENSE
README.md
SECURITY.md
CONTRIBUTING.md
docs/
src/CodexUsageMonitor/
  App.xaml
  App.xaml.cs
  AppServerClient.cs
  app.manifest
  BuildInfo.cs
  build.ps1
  CodexLocator.cs
  CodexUsageMonitor.csproj
  GaugeIconFactory.cs
  JsonValue.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  Models.cs
  MonitorSettings.cs
  RateLimitParser.cs
  ThemeManager.cs
  UsageMonitorService.cs
  WindowBackdrop.cs
  WindowEdgeSnapper.cs
  Assets/
    Generate-Icon.ps1
    CodexUsageMonitor.ico
    CodexUsageMonitor-*.png
    CodexUsageMonitor.png
  Themes/
    Cyberpunk.xaml
    Glass.xaml
    Minimal.xaml
plugins/codex-usage-monitor/
  .codex-plugin/plugin.json
  manifest files
  skills/
  scripts/
  assets/
    icon.png
    logo.png
```

## Exclude

```text
bin/
obj/
release/*
!release/README.md
release-staging/
staging/
dist/
out/
publish/
artifacts/
packages/
*.exe
*.zip
*.pdb
*.dll
*.msi
*.msix
*.nupkg
*.cache
*.user
*.suo
*.log
*.dmp
logs/
settings.json
*.settings.json
*.local.json
last-error.txt
.env
.env.*
secrets/
credentials/
secrets.json
credentials.json
token.json
tokens.json
*.secrets.json
*.credentials.json
*.token.json
*.tokens.json
*.pfx
*.p12
*.key
*.snk
```

同时排除研究阶段的 app-server schema dump、probe 脚本、捕获流量、临时报告和真实账号
输出。这些内容不是运行或构建依赖。

`.gitignore` 只阻止未跟踪文件被新增，不能自动移除已经纳入索引的文件。提交前必须用
`git ls-files` 检查已跟踪清单；发现违规路径时，应先从 Git 索引中移除，再重新验证。

在仓库根目录使用 Windows PowerShell 执行：

```powershell
$forbidden = git ls-files | Where-Object {
    $_ -match '(?i)\.(exe|zip|pdb|dll|msi|msix|nupkg)$' -or
    $_ -match '(?i)^release/(?!README\.md$)' -or
    $_ -match '(?i)(^|/)(bin|obj|release-staging|staging|dist|out|publish|artifacts|packages|logs)(/|$)' -or
    $_ -match '(?i)(^|/)(settings\.json|.*\.settings\.json|.*\.local\.json|last-error\.txt|\.env(\..*)?)$' -or
    $_ -match '(?i)(^|/)(secrets|credentials)(/|$)' -or
    $_ -match '(?i)(^|/)(secrets|credentials|token|tokens)(\.[^/]*)?\.json$' -or
    $_ -match '(?i)\.(log|dmp|pfx|p12|key|snk)$'
}
if ($forbidden) {
    $forbidden
    throw 'Git index contains forbidden release, local-state, or credential files.'
}
```

命令无输出且不抛出异常才算通过。`release/README.md` 是唯一允许跟踪的 release
目录文件；PNG/ICO 是明确允许的图标资产，不属于禁止的可执行发布物。

## Portability gate

复制前确认：

- `build.ps1` 通过 `vswhere`、常见 MSBuild 安装位置或 `PATH` 发现工具，不固定到单一
  Visual Studio 年份。
- `CodexLocator` 使用系统目录 API、当前用户 LocalAppData 和 `PATH`，不含用户名、
  盘符或安装版本哈希。
- Release 配置为 x64、optimized、`DebugType=none`。
- 所有 XAML、C#、JSON、YAML 和 Markdown 使用一致的 UTF-8 编码，无乱码。
- 最终源码能在另一台 Windows 10/11 x64 机器从干净目录构建。
- 构建输出和 Release staging 位于源码仓库外，构建结束后不会污染 Git 工作树。
