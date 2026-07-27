# Release payload

此目录只保留发布说明，不作为本地构建输出目录。源码仓库不承载发布 EXE 或 ZIP；可下载产物由 GitHub Release 提供。

运行仓库根目录下的：

```powershell
.\scripts\Build-ReleaseArtifacts.ps1
```

脚本会从 `.NET Framework 4.0` 源码构建 Windows x64 Release，并在系统临时目录或 `-OutputDirectory` 指定的仓库外本地目录中创建唯一发布目录。成功输出固定为：

```text
CodexUsageMonitor.exe
SHA256SUMS.txt
CodexUsageMonitor-windows-x64.zip
codex-usage-monitor-plugin.zip
```

不要把生成的 EXE、ZIP、PDB、`bin`/`obj`、设置、日志、账号数据、证书或私钥复制回本目录。完整构建、哈希校验、Authenticode 验签、tag 自动发布和 `gh release create` 命令见 [`../docs/RELEASE-PROCESS.md`](../docs/RELEASE-PROCESS.md)。
