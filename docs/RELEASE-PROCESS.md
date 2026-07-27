# Release process

本流程从源码重新构建 Windows x64 Release，并把所有最终产物写到仓库之外。源码仓库不承载发布 EXE 或 ZIP；GitHub Release 承载可下载资产。

## 1. 准备环境

- Windows 10/11 x64。
- Windows PowerShell 5.1 或更高版本。
- Visual Studio Build Tools 2019 或更高版本。
- 安装 `.NET desktop build tools`。
- 首次构建可访问 `nuget.org`，用于还原官方的 .NET Framework 4.0 reference assemblies。
- 发布到 GitHub 时安装并登录 GitHub CLI：`gh auth status`。

发布前先确认工作区内容正确。打包脚本调用现有的
`src/CodexUsageMonitor/build.ps1`，先还原固定版本
`Microsoft.NETFramework.ReferenceAssemblies.net40`，再执行 `.NET Framework 4.0`、
`Release`、`x64` 构建。该包仅参与编译，不进入最终发布目录。MSBuild 的 `bin`/`obj`
中间目录已被 Git 忽略；最终 EXE、ZIP 与哈希清单只写入仓库外目录。

## 2. 预览与构建

先预览。`-WhatIf` 不构建，也不创建目录：

```powershell
.\scripts\Build-ReleaseArtifacts.ps1 -WhatIf
```

默认构建到系统临时目录下的唯一子目录：

```powershell
$release = .\scripts\Build-ReleaseArtifacts.ps1
$artifactDirectory = $release.OutputDirectory
$artifactDirectory
```

也可以指定仓库外的本地目录。脚本仍会在该目录下创建唯一的发布子目录，避免覆盖旧版本：

```powershell
$releaseBase = Join-Path `
    $env:USERPROFILE `
    'CodexUsageMonitor-Releases'
$release = .\scripts\Build-ReleaseArtifacts.ps1 `
    -OutputDirectory $releaseBase
$artifactDirectory = $release.OutputDirectory
```

脚本拒绝以下目标：

- 仓库内部目录；
- UNC 或设备路径；
- 穿过 junction、symbolic link 等 reparse point 的目录；
- 已存在的同名发布目录。

成功后，输出目录只包含：

```text
CodexUsageMonitor.exe
SHA256SUMS.txt
CodexUsageMonitor-windows-x64.zip
codex-usage-monitor-plugin.zip
```

`CodexUsageMonitor-windows-x64.zip` 只包含 EXE 和一份针对该 EXE 的 `SHA256SUMS.txt`。`codex-usage-monitor-plugin.zip` 的压缩包根目录就是插件根目录，包含 `.codex-plugin/plugin.json`、脚本、技能、公开图片资源和新构建的 `assets/CodexUsageMonitor.exe`。两个压缩包都不包含 PDB。

## 3. 哈希校验

外部 `SHA256SUMS.txt` 覆盖 EXE 和两个 ZIP。可用以下 PowerShell 5.1 代码逐项验证：

```powershell
Push-Location $artifactDirectory
try {
    foreach ($line in Get-Content -LiteralPath '.\SHA256SUMS.txt') {
        if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') {
            throw "Invalid checksum line: $line"
        }

        $expected = $Matches[1]
        $fileName = $Matches[2]
        $actual = (Get-FileHash `
            -LiteralPath $fileName `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "SHA-256 mismatch: $fileName"
        }
    }
    'All SHA-256 checks passed.'
}
finally {
    Pop-Location
}
```

## 4. Authenticode 验签

正式公开发布前，建议使用可信代码签名证书。签名步骤必须位于“编译完成”和“复制、压缩、生成哈希”之间，使 EXE、插件副本、压缩包和清单都对应同一个签名版本。当前本地脚本不会访问证书或自动签名；未配置项目级签名时，它生成的是未签名测试产物。正式签名发布应在受保护的 CI 签名阶段完成，再执行同等打包与哈希步骤。

检查签名：

```powershell
$signature = Get-AuthenticodeSignature `
    -LiteralPath (Join-Path $artifactDirectory 'CodexUsageMonitor.exe')
$signature | Format-List Status, StatusMessage, SignerCertificate
if ($signature.Status -ne 'Valid') {
    throw "Release EXE is not validly signed: $($signature.Status)"
}
```

如果项目尚未配置代码签名证书，`NotSigned` 是预期结果，但应在 GitHub Release 说明中明确标注。不要把 `.pfx`、私钥、证书密码或签名日志放入仓库或发布压缩包。

## 5. 发布到 GitHub

推荐流程是创建并推送版本 tag，由仓库的 tag Release workflow 构建并上传资产：

```powershell
git tag -a v1.1.0 -m 'Codex Usage Monitor v1.1.0'
git push origin v1.1.0
```

如需手动发布，先在仓库外创建版本说明文件。以下命令把四个最终产物上传到新 Release：

```powershell
$tag = 'v1.1.0'
$notes = Join-Path $env:TEMP 'CodexUsageMonitor-release-notes.md'

gh release create $tag `
    (Join-Path $artifactDirectory 'CodexUsageMonitor.exe') `
    (Join-Path $artifactDirectory 'SHA256SUMS.txt') `
    (Join-Path $artifactDirectory 'CodexUsageMonitor-windows-x64.zip') `
    (Join-Path $artifactDirectory 'codex-usage-monitor-plugin.zip') `
    --title "Codex Usage Monitor $tag" `
    --notes-file $notes
```

上传后，从 GitHub Release 页面重新下载全部附件，再执行第 3 节的哈希校验。确认哈希、签名、文件数量与插件 ZIP 结构后再公开 Release。

## 6. 失败与回滚

每次运行都创建唯一输出目录，不覆盖旧产物。构建失败时：

1. 不要发布该目录中的任何部分产物。
2. 修复问题后重新运行脚本，使用新的输出目录。
3. 确认不再需要失败目录后，仅删除脚本返回或错误信息中明确显示的那个唯一目录。

GitHub Release 发布错误时，可先转为草稿或删除错误 Release；已存在的本地旧发布目录不会被本脚本修改。
