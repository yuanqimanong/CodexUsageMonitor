# Architecture

## 目标

- 在 Windows 桌面持续显示 Codex rate-limit window 的已用比例和重置时间。
- 保持单文件、低依赖、按当前用户运行。
- 复用 Codex Desktop 当前登录会话，不创建第二套凭据。
- 提供托盘、主题、置顶、侧边吸附、紧凑模式和开机启动。
- 允许 Codex 本地插件安全地启动、停止和检查状态。

## 非目标

- 不显示账单、余额、API 费用或消费金额。
- 不绕过 Codex 的限流、认证或客户端权限。
- 不抓取浏览器 cookie、Local Storage 或其他 Windows 用户的配置。
- 不提供远程控制、远程更新、遥测或云端账号系统。
- 不支持 Windows 32 位。

## 核心模块

| 模块 | 职责 |
| --- | --- |
| `App` | 单实例、显示/退出信号、后台启动和未处理异常日志 |
| `MainWindow` | WPF UI、托盘菜单、主题、方向化半圆状态和设置保存 |
| `UsageMonitorService` | 启动轮询、维护快照、重连和状态通知 |
| `AppServerClient` | 启动本地 `codex app-server`，执行初始化和 JSON-RPC 调用 |
| `CodexLocator` | 便携定位 `codex.exe`，支持可信环境变量覆盖 |
| `RateLimitParser` | 把 app-server 响应收敛为显示模型 |
| `ChatPanel` | 运行会话的单列卡片、速度与 Token 明细，可见时每 3 秒读取 |
| `LocalThreadCatalog` | 通过系统 winsqlite3 只读查询现存主会话，过滤删除、归档及后台会话 |
| `LocalSessionReader` | 活跃会话 JSONL 的尾部读取、增量跟随和缓存淘汰 |
| `SessionMetricsParser` | 新旧 Token 记录去重、调用耗时和工具间隔、缺失字段处理 |
| `SettingsStore` | 在当前用户的 LocalAppData 保存界面设置 |
| `StartupManager` | 管理当前用户的 `HKCU` Run 项 |
| `WindowEdgeSnapper` | 多显示器工作区、主边选择、边缘吸附、恢复和拖动状态 |
| `WindowBackdrop` / themes | Windows 背景效果和资源字典切换 |
| plugin scripts | 通过相对路径执行 bundled EXE，并输出稳定的状态对象 |

## 数据流

```text
Codex Desktop current-user installation
              │ portable executable discovery
              ▼
       trusted codex.exe
              │ child process: app-server
              ▼
        AppServerClient
              │ account/rateLimits/read
              ▼
        RateLimitParser
              │ in-memory display model
              ▼
     UsageMonitorService ─────► MainWindow / tray icon
```

显示模型只保存在内存。程序不会把原始 app-server 响应写入文件。

## 状态与持久化

界面状态：

```text
Expanded ⇄ Compact (edge snap)
Visible ⇄ Hidden (tray)
Running → Exit (tray or --shutdown)
```

持久化文件：

- `%LOCALAPPDATA%\CodexUsageMonitor\settings.json`
- `%LOCALAPPDATA%\CodexUsageMonitor\last-error.txt`（仅未处理异常）
- `%LOCALAPPDATA%\CodexUsageMonitor\rollback\`（package installer）
- `<install directory>\.codex-usage-monitor-install.json`（安装所有权与可选启动项恢复信息）

可持久化的设置包括窗口位置、置顶、主题、吸附边缘、当前页面、沿边位置和数据目录。认证材料和 rate-limit 原始
响应不应写入设置。

会话页以只读 SQLite 查询结果为唯一清单，不扫描遗留日志发现会话。名称优先使用 `name`，空名称或旧版数据库回退到 `title`。
候选会话取数据库 `updated_at` 和文件修改时间中较新的活动时间，避免 Windows 延迟刷新文件时间造成漏显。运行状态由 JSONL 事件判断。每个候选日志首次
读取末尾 1 MiB，此后增量跟随，单轮最多读取 8 MiB，单行最多 2 MiB。半行和拆分的
UTF-8 字符等待后续追加，截断或替换后重新读取尾部。已完成、中断、删除、归档、后台
子会话和连续 5 分钟无更新的会话不显示。长时间静默的工具或思考可能暂时被隐藏。
卡片通过 ObservableCollection 原位更新，避免每轮重建控件导致滚动跳动。
窗口按最多 5 张卡片计算高度，超过 5 条时滚动。屏幕工作区不足时限制窗口高度并允许滚动，空列表使用紧凑提示区。
速度使用本地观测时间，在工具开始前结束计时，全部工具返回后开启下一段。
隐藏、收起和切回额度页后暂停新的读取任务，缓存只保留清单中近期更新的会话。

吸附布局使用物理像素和当前显示器工作区，展开、收起共享稳定的沿边锚点。
悬停 180 ms 展开，离开 900 ms 后收起。窗口尺寸一次更新到位，展开内容使用 120 ms 淡入，遵循系统动画设置。
拖动吸附后立即收起。若鼠标仍在半圆内，等待移出后再允许悬停展开，避免松手后马上反弹。
拖拽事件与鼠标捕获归属同一个 Grid。空白区域移动超过系统拖动阈值后进入原生窗口移动循环，点击控件正常交互。松开、失去捕获、窗口失活或隐藏时清理待拖动状态。

## 异常处理

- 找不到 Codex：显示可恢复错误，等待用户安装、登录或设置可信覆盖路径。
- app-server 超时或退出：停止当前连接并由 service 进入重试。
- UI 未处理异常：尽力写入 `last-error.txt`，然后安全释放子进程和托盘资源。
- 第二实例：向已有实例发送显示信号后退出。
- `--shutdown`：向已有实例发送退出信号；生命周期脚本等待其自然退出，不强杀。
- 吸附或显示器变化：把窗口限制在当前工作区，并保存可恢复位置。

## 安全边界

可信：

- 当前发布包中的 EXE、脚本和静态资源
- 用户明确安装并信任的 Codex Desktop / `codex.exe`
- 当前 Windows 用户自己的 LocalAppData 和 HKCU

不可信或需要验证：

- 下载文件和 release hash
- `PATH` 中同名可执行文件
- `CODEX_USAGE_MONITOR_CODEX_EXE` 指定的路径
- app-server 输出长度、结构和字段值
- 用户准备公开分享的错误日志

程序以 `asInvoker` 运行，不需要提权。安装和开机启动均为 per-user 范围。
文件变更只接受不含 reparse point 的本机盘符路径。卸载使用 owned allowlist，并且只在
目录为空时删除目录本身；安装标记不是递归删除任意目录的授权。格式版本 2 的标记绑定
规范化安装目录并验证固定字段，损坏或路径不匹配的标记不授予修改权限。文件写入和删除
前会重新检查完整祖先链；HKCU Run 值也会在实际变更前重新读取。

## 部署

Standalone：

```text
%LOCALAPPDATA%\Programs\CodexUsageMonitor\CodexUsageMonitor.exe
```

Codex plugin：

```text
plugins\codex-usage-monitor\
  .codex-plugin\plugin.json
  assets\CodexUsageMonitor.exe
  scripts\
  skills\
```

两种部署包含相同的 Release EXE。PDB、源码构建目录和账号状态不进入部署包。

## 验收标准

- 干净 Windows 10/11 x64 当前用户无需管理员权限即可运行。
- 自动定位 Codex Desktop 时不依赖开发机用户名、盘符或安装版本哈希。
- Codex 已登录时能显示至少一个受支持的 rate-limit window。
- 未安装、未登录、超时和 app-server 退出时可恢复且不泄露凭据。
- 左右边使用 `30×52` DIP 纵向半圆，上下边使用 `52×30` DIP 横向半圆。
- 角落只保留一个主边，方向菜单切换时不继承旧的正交边状态。
- 半圆可双击展开，也可向屏幕内部拖离后恢复完整面板并继续移动。
- `100`、`--`、`∞` 等半圆数字完整显示，悬浮提示保留完整百分比。
- 安装、升级、回滚和卸载不会删除不属于本项目的文件或注册表值。
- 最终发布目录通过敏感信息复扫，且 EXE SHA-256 与清单一致。
