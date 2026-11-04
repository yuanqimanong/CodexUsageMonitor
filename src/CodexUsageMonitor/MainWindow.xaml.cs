using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor
{
    public partial class MainWindow : Window
    {
        private const double FullWindowWidth = 392;
        private const double FullWindowHeight = 286;
        private const double ChatWindowWidth = 520;
        private const double FullSurfaceMargin = 14;
        private const double FullCornerRadius = 14;
        private const double CompactSurfaceMargin = 6;
        private const double CompactDiameter = 52;
        private const double CompactThickness = 30;
        private const double CompactCornerRadius = 26;

        private readonly UsageMonitorService service;
        private readonly SettingsStore settingsStore;
        private readonly MonitorSettings settings;
        private readonly DispatcherTimer countdownTimer;
        private readonly DispatcherTimer collapseTimer;
        private readonly DispatcherTimer expandTimer;
        private readonly Forms.NotifyIcon trayIcon;
        private readonly Forms.ToolStripMenuItem showMenuItem;
        private readonly Forms.ToolStripMenuItem topMostMenuItem;
        private readonly Forms.ToolStripMenuItem edgeSnapMenuItem;
        private readonly Forms.ToolStripMenuItem startupMenuItem;
        private readonly Forms.ToolStripMenuItem minimalThemeMenuItem;
        private readonly Forms.ToolStripMenuItem glassThemeMenuItem;
        private readonly Forms.ToolStripMenuItem cyberpunkThemeMenuItem;
        private readonly WindowEdgeSnapper edgeSnapper;
        private readonly object snapshotDispatchSync = new object();

        private UsageSnapshot snapshot;
        private UsageSnapshot pendingSnapshot;
        private MonitorStatus monitorStatus;
        private Icon currentTrayIcon;
        private MonitorTheme currentTheme;
        private bool snapshotDispatchPending;
        private bool initialized;
        private bool exiting;
        private bool compactMode;
        private bool isDragging;
        private bool isProgrammaticLayout;
        private bool chatMode;
        private bool keepExpanded;
        private bool waitForHoverExit;
        private System.Windows.Point? dragStart;
        private WindowDockEdge? dockedHorizontalEdge;
        private WindowDockEdge? dockedVerticalEdge;

        public MainWindow()
        {
            InitializeComponent();

            settingsStore = new SettingsStore();
            settings = settingsStore.Load();
            Topmost = settings.TopMost;
            currentTheme = ThemeManager.Parse(settings.Theme);
            ThemeManager.Apply(this, currentTheme);

            edgeSnapper = new WindowEdgeSnapper(
                this,
                WindowSurface.Margin,
                24);
            edgeSnapper.Enabled = settings.EdgeSnapEnabled != false;
            edgeSnapper.MoveCompleted += EdgeSnapper_MoveCompleted;

            service = new UsageMonitorService();
            service.SnapshotChanged += Service_SnapshotChanged;
            service.StatusChanged += Service_StatusChanged;

            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromSeconds(1);
            countdownTimer.Tick += CountdownTimer_Tick;

            collapseTimer = new DispatcherTimer();
            collapseTimer.Interval = TimeSpan.FromMilliseconds(900);
            collapseTimer.Tick += CollapseTimer_Tick;
            expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            expandTimer.Tick += delegate
            {
                expandTimer.Stop();
                if (compactMode && IsMouseOver && !isDragging && !dragStart.HasValue &&
                    !waitForHoverExit && !IsContextMenuOpen)
                    ExpandDockedPanel();
            };
            ChatView.Configure(settings.CodexDataDirectory);
            ChatView.PreferencesChanged += delegate { SaveSettings(); };
            ChatView.ContentHeightChanged += delegate
            {
                if (chatMode && !compactMode && !isDragging && Math.Abs(Height - ExpandedHeight) > 0.5)
                    ApplyWindowSize(ExpandedWidth, ExpandedHeight, WindowSurface.Margin);
            };

            trayIcon = new Forms.NotifyIcon();
            trayIcon.Text = BuildInfo.ProductName;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { Dispatcher.BeginInvoke(
                new Action(ShowFromTray)); };

            Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
            showMenuItem = new Forms.ToolStripMenuItem("显示悬浮窗");
            showMenuItem.Click += delegate { Dispatcher.BeginInvoke(
                new Action(ToggleWindowVisibilityFromTray)); };
            trayMenu.Items.Add(showMenuItem);

            Forms.ToolStripMenuItem refreshMenuItem =
                new Forms.ToolStripMenuItem("立即刷新");
            refreshMenuItem.Click += delegate { service.Refresh(); };
            trayMenu.Items.Add(refreshMenuItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());

            topMostMenuItem = new Forms.ToolStripMenuItem("始终置顶");
            topMostMenuItem.Checked = settings.TopMost;
            topMostMenuItem.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(ToggleTopMost));
            };
            trayMenu.Items.Add(topMostMenuItem);

            edgeSnapMenuItem = new Forms.ToolStripMenuItem("屏幕边缘吸附");
            edgeSnapMenuItem.Checked = settings.EdgeSnapEnabled != false;
            edgeSnapMenuItem.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(ToggleEdgeSnap));
            };
            trayMenu.Items.Add(edgeSnapMenuItem);

            Forms.ToolStripMenuItem dockMenuItem =
                new Forms.ToolStripMenuItem("立即吸附到");
            dockMenuItem.DropDownItems.Add(
                CreateDockMenuItem("左侧", WindowDockEdge.Left));
            dockMenuItem.DropDownItems.Add(
                CreateDockMenuItem("右侧", WindowDockEdge.Right));
            dockMenuItem.DropDownItems.Add(
                CreateDockMenuItem("顶部", WindowDockEdge.Top));
            dockMenuItem.DropDownItems.Add(
                CreateDockMenuItem("底部", WindowDockEdge.Bottom));
            trayMenu.Items.Add(dockMenuItem);

            Forms.ToolStripMenuItem themeMenu =
                new Forms.ToolStripMenuItem("皮肤");
            minimalThemeMenuItem = CreateThemeMenuItem(
                "黑白极简",
                MonitorTheme.Minimal);
            glassThemeMenuItem = CreateThemeMenuItem(
                "雾灰",
                MonitorTheme.Glass);
            cyberpunkThemeMenuItem = CreateThemeMenuItem(
                "赛博朋克科技",
                MonitorTheme.Cyberpunk);
            themeMenu.DropDownItems.Add(minimalThemeMenuItem);
            themeMenu.DropDownItems.Add(glassThemeMenuItem);
            themeMenu.DropDownItems.Add(cyberpunkThemeMenuItem);
            trayMenu.Items.Add(themeMenu);

            startupMenuItem = new Forms.ToolStripMenuItem("开机启动");
            startupMenuItem.Checked = StartupManager.IsEnabled();
            startupMenuItem.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(ToggleStartup));
            };
            trayMenu.Items.Add(startupMenuItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());

            Forms.ToolStripMenuItem exitMenuItem =
                new Forms.ToolStripMenuItem("退出");
            exitMenuItem.Click += delegate
            {
                Dispatcher.BeginInvoke(
                    new Action(delegate
                    {
                        App application = Application.Current as App;
                        if (application != null)
                        {
                            application.ExitApplication();
                        }
                    }));
            };
            trayMenu.Items.Add(exitMenuItem);
            trayIcon.ContextMenuStrip = trayMenu;

            UpdateThemeMenuChecks();
            UpdateTrayIcon(null);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            LocationChanged += MainWindow_LocationChanged;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            Deactivated += delegate { CancelPendingDrag(); };
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SetActiveView(settings.ChatView);
        }

        public void StartHidden()
        {
            ShowActivated = false;
            Opacity = 0;
            Show();
            Hide();
            Opacity = 1;
            ShowActivated = true;
        }

        public void ShowFromTray()
        {
            if (exiting)
            {
                return;
            }

            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Topmost = settings.TopMost;
            if (compactMode) ExpandDockedPanel();
            Activate();
            SyncVisibilityMenuText();
        }

        public void PrepareForExit()
        {
            if (exiting)
            {
                return;
            }

            lock (snapshotDispatchSync)
            {
                exiting = true;
                pendingSnapshot = null;
                snapshotDispatchPending = false;
            }

            SaveSettings();
            CancelPendingDrag();
            countdownTimer.Stop();
            collapseTimer.Stop();
            expandTimer.Stop();
            ChatView.Dispose();
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            edgeSnapper.MoveCompleted -= EdgeSnapper_MoveCompleted;
            edgeSnapper.Dispose();

            service.SnapshotChanged -= Service_SnapshotChanged;
            service.StatusChanged -= Service_StatusChanged;
            service.Dispose();

            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (currentTrayIcon != null)
            {
                currentTrayIcon.Dispose();
                currentTrayIcon = null;
            }

            Close();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs eventArgs)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            RestorePosition();
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(delegate
                {
                    edgeSnapper.ClampToWorkArea();
                    WindowDockEdge savedEdge;
                    if (edgeSnapper.Enabled && Enum.TryParse(settings.DockEdge, out savedEdge) &&
                        Enum.IsDefined(typeof(WindowDockEdge), savedEdge))
                    {
                        DockToEdge(savedEdge, settings.DockAnchorRatio);
                    }
                    else if (edgeSnapper.Enabled && edgeSnapper.SnapNow())
                    {
                        CaptureDockEdges();
                        EnterCompactMode();
                    }
                    SaveSettings();
                }));
            ApplyTheme(currentTheme, false);
            SyncVisibilityMenuText();
            countdownTimer.Start();
            service.Start();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs eventArgs)
        {
            if (!exiting)
            {
                eventArgs.Cancel = true;
                HideToTray();
            }
        }

        private void MainWindow_LocationChanged(object sender, EventArgs eventArgs)
        {
            if (initialized &&
                !isProgrammaticLayout &&
                IsVisible &&
                IsFinite(Left) &&
                IsFinite(Top))
            {
                settings.HasWindowPosition = true;
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
            }
        }

        private void MainWindow_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs eventArgs)
        {
            SyncVisibilityMenuText();
        }

        private void EdgeSnapper_MoveCompleted(object sender, EventArgs eventArgs)
        {
            SaveSettings();
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs eventArgs)
        {
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    if (exiting) return;
                    edgeSnapper.ResetDockAnchor();
                    if (chatMode && !compactMode)
                    {
                        ApplyWindowSize(ExpandedWidth, ExpandedHeight, WindowSurface.Margin);
                    }
                    else if (HasDockEdges)
                    {
                        DockToStoredEdges();
                    }
                    else
                    {
                        edgeSnapper.ClampToWorkArea();
                    }
                }));
        }

        private void Service_SnapshotChanged(
            object sender,
            UsageSnapshotEventArgs eventArgs)
        {
            UsageSnapshot latest = eventArgs == null
                ? null
                : eventArgs.Snapshot;
            if (latest == null)
            {
                return;
            }

            lock (snapshotDispatchSync)
            {
                if (exiting)
                {
                    return;
                }

                pendingSnapshot = latest;
                if (snapshotDispatchPending)
                {
                    return;
                }

                snapshotDispatchPending = true;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                ClearPendingSnapshot();
                return;
            }

            try
            {
                Dispatcher.BeginInvoke(new Action(ApplyPendingSnapshot));
            }
            catch (InvalidOperationException)
            {
                ClearPendingSnapshot();
            }
        }

        private void ApplyPendingSnapshot()
        {
            UsageSnapshot latest;
            lock (snapshotDispatchSync)
            {
                latest = pendingSnapshot;
                pendingSnapshot = null;
                snapshotDispatchPending = false;
            }

            if (exiting || latest == null)
            {
                return;
            }

            snapshot = latest;
            RenderSnapshot();
        }

        private void ClearPendingSnapshot()
        {
            lock (snapshotDispatchSync)
            {
                pendingSnapshot = null;
                snapshotDispatchPending = false;
            }
        }

        private void Service_StatusChanged(
            object sender,
            MonitorStatusEventArgs eventArgs)
        {
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    monitorStatus = eventArgs.Status;
                    RenderStatus();
                }));
        }

        private void CountdownTimer_Tick(object sender, EventArgs eventArgs)
        {
            if (snapshot != null)
            {
                RenderWindowDetails();
            }
        }

        private void RenderSnapshot()
        {
            RateWindow constrained = snapshot.MostConstrainedWindow;
            int? displayedRemaining = constrained != null
                ? (int?)constrained.RemainingPercent
                : snapshot.UnlimitedCredits == true
                    ? (int?)100
                    : null;

            if (snapshot.UnlimitedCredits == true && constrained == null)
            {
                RemainingText.Text = "∞";
            }
            else
            {
                RemainingText.Text = displayedRemaining.HasValue
                    ? displayedRemaining.Value + "%"
                    : "--%";
            }
            MiniRemainingText.Text =
                snapshot.UnlimitedCredits == true && constrained == null
                    ? "∞"
                    : displayedRemaining.HasValue
                        ? displayedRemaining.Value.ToString()
                        : "--";
            CompactPanel.ToolTip =
                "剩余用量 " + RemainingText.Text +
                "；双击展开；向屏幕内拖动可脱离吸附";

            UsageProgress.Value = displayedRemaining ?? 0;

            System.Drawing.Color accent = displayedRemaining.HasValue
                ? GetUsageColor(displayedRemaining.Value)
                : System.Drawing.Color.FromArgb(113, 128, 141);
            SolidColorBrush accentBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(accent.R, accent.G, accent.B));
            UsageProgress.Foreground = accentBrush;
            SetStatusBrush(accentBrush);

            PlanText.Text = string.IsNullOrWhiteSpace(snapshot.PlanType)
                ? string.Empty
                : "· " + snapshot.PlanType.ToUpperInvariant();
            FetchedText.Text = snapshot.FetchedAt.ToLocalTime().ToString("HH:mm:ss") +
                " 更新";

            RenderWindowDetails();
            RenderFooter();
            UpdateTrayIcon(displayedRemaining);
            RenderStatus();
        }

        private void RenderWindowDetails()
        {
            if (snapshot == null || snapshot.Windows.Count == 0)
            {
                return;
            }

            List<RateWindow> ordered = snapshot.Windows
                .Where(window => window.Kind == RateWindowKind.RateLimit)
                .OrderBy(window => window.RemainingPercent)
                .ThenBy(window => window.WindowDurationMinutes ?? long.MaxValue)
                .ToList();

            if (ordered.Count == 0)
            {
                PrimaryWindowText.Text = snapshot.UnlimitedCredits == true
                    ? "Codex 未返回速率窗口 · Credits 无限"
                    : "Codex 未返回速率窗口";

                RateWindow spending = snapshot.Windows
                    .Where(window => window.Kind == RateWindowKind.SpendingLimit)
                    .OrderBy(window => window.RemainingPercent)
                    .FirstOrDefault();
                SecondaryWindowText.Text = spending != null
                    ? BuildWindowLine(spending, false)
                    : "由 Codex app-server 实时同步";
                SecondaryWindowText.Visibility = Visibility.Visible;
                return;
            }

            bool showBucket = ordered
                .Select(window => window.BucketId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;

            PrimaryWindowText.Text = BuildWindowLine(ordered[0], showBucket);
            if (ordered.Count > 1)
            {
                SecondaryWindowText.Text = BuildWindowLine(ordered[1], showBucket);
                SecondaryWindowText.Visibility = Visibility.Visible;
            }
            else
            {
                SecondaryWindowText.Text = "由 Codex app-server 实时同步";
                SecondaryWindowText.Visibility = Visibility.Visible;
            }
        }

        private static string BuildWindowLine(RateWindow window, bool showBucket)
        {
            string prefix = showBucket
                ? string.Format("{0} · ", window.BucketName)
                : string.Empty;
            return string.Format(
                "{0}{1}：{2}% 剩余 · {3}",
                prefix,
                window.WindowName,
                window.RemainingPercent,
                FormatReset(window.ResetsAt));
        }

        private static string FormatReset(DateTimeOffset? resetsAt)
        {
            if (!resetsAt.HasValue)
            {
                return "重置时间未知";
            }

            TimeSpan remaining = resetsAt.Value.ToLocalTime() - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                return "等待刷新";
            }

            if (remaining.TotalDays >= 1)
            {
                return string.Format(
                    "{0}天 {1}小时后重置",
                    (int)remaining.TotalDays,
                    remaining.Hours);
            }

            if (remaining.TotalHours >= 1)
            {
                return string.Format(
                    "{0}小时 {1}分后重置",
                    (int)remaining.TotalHours,
                    remaining.Minutes);
            }

            return string.Format(
                "{0}分 {1}秒后重置",
                Math.Max(0, remaining.Minutes),
                Math.Max(0, remaining.Seconds));
        }

        private void RenderFooter()
        {
            List<string> parts = new List<string>();
            parts.Add("Codex Desktop");

            if (snapshot.UnlimitedCredits == true)
            {
                parts.Add("Credits 无限");
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.CreditsBalance))
            {
                parts.Add("Credits " + snapshot.CreditsBalance);
            }

            int rateWindowCount = snapshot.Windows.Count(
                window => window.Kind == RateWindowKind.RateLimit);
            if (rateWindowCount > 2)
            {
                parts.Add("共 " + rateWindowCount + " 个窗口");
            }
            else
            {
                parts.Add("每分钟兜底刷新");
            }

            FooterText.Text = string.Join(" · ", parts.ToArray());
        }

        private void RenderStatus()
        {
            if (monitorStatus == null)
            {
                return;
            }

            ConnectionText.Text = monitorStatus.IsStale
                ? "缓存 · " + monitorStatus.Message
                : monitorStatus.Message;

            if (monitorStatus.IsConnected && snapshot != null)
            {
                RateWindow constrained = snapshot.MostConstrainedWindow;
                if (constrained != null)
                {
                    System.Drawing.Color accent =
                        GetUsageColor(
                            constrained.RemainingPercent);
                    SetStatusBrush(new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            accent.R,
                            accent.G,
                            accent.B)));
                }
            }
            else
            {
                SolidColorBrush idle =
                    TryFindResource("StatusIdleBrush") as SolidColorBrush;
                SetStatusBrush(idle ?? new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(113, 128, 141)));
            }

            if (monitorStatus.IsStale && snapshot != null)
            {
                FetchedText.Text = snapshot.FetchedAt.ToLocalTime().ToString("HH:mm:ss") +
                    " 最后更新";
            }
        }

        private void SetStatusBrush(System.Windows.Media.Brush brush)
        {
            StatusDot.Fill = brush;
            MiniStatusDot.Fill = brush;
        }

        private void UpdateTrayIcon(int? remaining)
        {
            System.Drawing.Color? accent = remaining.HasValue
                ? (System.Drawing.Color?)GetUsageColor(remaining.Value)
                : null;
            Icon replacement = GaugeIconFactory.Create(remaining, accent);
            Icon previous = currentTrayIcon;
            currentTrayIcon = replacement;
            trayIcon.Icon = replacement;
            if (previous != null)
            {
                previous.Dispose();
            }

            try
            {
                trayIcon.Text = remaining.HasValue
                    ? string.Format("Codex 剩余 {0}%", remaining.Value)
                    : BuildInfo.ProductName;
            }
            catch (ArgumentException)
            {
                trayIcon.Text = BuildInfo.ProductName;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (chatMode)
            {
                ChatView.RefreshCatalog();
                return;
            }
            ConnectionText.Text = "正在刷新…";
            service.Refresh();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            ApplyTheme(ThemeManager.Next(currentTheme), true);
        }

        private void HideButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            HideToTray();
        }

        private void HideToTray()
        {
            CancelPendingDrag();
            expandTimer.Stop();
            collapseTimer.Stop();
            SaveSettings();
            Hide();
            SyncVisibilityMenuText();
        }

        private void ToggleWindowVisibilityFromTray()
        {
            if (IsVisible)
            {
                HideToTray();
            }
            else
            {
                ShowFromTray();
            }
        }

        private void SyncVisibilityMenuText()
        {
            showMenuItem.Text = IsVisible
                ? "隐藏悬浮窗"
                : "显示悬浮窗";
        }

        private void ToggleTopMost()
        {
            settings.TopMost = !settings.TopMost;
            Topmost = settings.TopMost;
            topMostMenuItem.Checked = settings.TopMost;
            SaveSettings();
        }

        private void ToggleEdgeSnap()
        {
            bool enabled = settings.EdgeSnapEnabled != false;
            settings.EdgeSnapEnabled = !enabled;
            edgeSnapMenuItem.Checked = !enabled;
            edgeSnapper.Enabled = !enabled;
            if (enabled)
            {
                ClearDockEdges();
                ShowExpandedPanel();
                edgeSnapper.ClampToWorkArea();
            }
            else if (edgeSnapper.SnapNow())
            {
                CaptureDockEdges();
                EnterCompactMode();
            }
            SaveSettings();
        }

        private void ToggleStartup()
        {
            try
            {
                bool enabled = !StartupManager.IsEnabled();
                StartupManager.SetEnabled(enabled);
                startupMenuItem.Checked = enabled;
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    BuildInfo.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                startupMenuItem.Checked = StartupManager.IsEnabled();
            }
        }

        private Forms.ToolStripMenuItem CreateThemeMenuItem(
            string label,
            MonitorTheme theme)
        {
            Forms.ToolStripMenuItem item =
                new Forms.ToolStripMenuItem(label);
            item.Tag = theme;
            item.Click += delegate
            {
                Dispatcher.BeginInvoke(
                    new Action(delegate
                    {
                        ApplyTheme((MonitorTheme)item.Tag, true);
                    }));
            };
            return item;
        }

        private Forms.ToolStripMenuItem CreateDockMenuItem(
            string label,
            WindowDockEdge edge)
        {
            Forms.ToolStripMenuItem item =
                new Forms.ToolStripMenuItem(label);
            item.Click += delegate
            {
                Dispatcher.BeginInvoke(
                    new Action(delegate
                    {
                        DockToEdge(edge);
                    }));
            };
            return item;
        }

        private void ApplyTheme(MonitorTheme theme, bool save)
        {
            currentTheme = theme;
            settings.Theme = theme.ToString();
            ThemeManager.Apply(this, theme);
            UpdateThemeMenuChecks();
            ThemeButton.ToolTip = "当前皮肤：" +
                ThemeManager.GetDisplayName(theme) + "；点击切换";

            if (snapshot != null)
            {
                RenderSnapshot();
            }
            else
            {
                UpdateTrayIcon(null);
                RenderStatus();
            }

            if (save)
            {
                SaveSettings();
            }
        }

        private void UpdateThemeMenuChecks()
        {
            minimalThemeMenuItem.Checked =
                currentTheme == MonitorTheme.Minimal;
            glassThemeMenuItem.Checked =
                currentTheme == MonitorTheme.Glass;
            cyberpunkThemeMenuItem.Checked =
                currentTheme == MonitorTheme.Cyberpunk;
        }

        private System.Drawing.Color GetUsageColor(int remaining)
        {
            string resourceKey;
            if (remaining <= 15)
            {
                resourceKey = "CriticalBrush";
            }
            else if (remaining <= 35)
            {
                resourceKey = "WarningBrush";
            }
            else
            {
                resourceKey = "HealthyBrush";
            }

            SolidColorBrush brush =
                TryFindResource(resourceKey) as SolidColorBrush;
            if (brush == null)
            {
                return GaugeIconFactory.GetAccentColor(remaining);
            }

            return System.Drawing.Color.FromArgb(
                brush.Color.A,
                brush.Color.R,
                brush.Color.G,
                brush.Color.B);
        }

        private bool HasDockEdges
        {
            get
            {
                return dockedHorizontalEdge.HasValue ||
                    dockedVerticalEdge.HasValue;
            }
        }

        private double ExpandedWidth { get { return chatMode ? ChatWindowWidth : FullWindowWidth; } }
        private double ExpandedHeight
        {
            get
            {
                if (!chatMode) return FullWindowHeight;
                double height = ChatView.PreferredHeight + FullPanel.RowDefinitions[0].Height.Value +
                    SurfaceBorder.BorderThickness.Top + SurfaceBorder.BorderThickness.Bottom + FullSurfaceMargin * 2;
                return Math.Min(height, edgeSnapper.GetAvailableHeight());
            }
        }

        private void QuotaTab_Click(object sender, RoutedEventArgs args) { SetActiveView(false); }
        private void ChatTab_Click(object sender, RoutedEventArgs args) { SetActiveView(true); }

        private void SetActiveView(bool showChat)
        {
            chatMode = showChat;
            QuotaPanel.Visibility = showChat ? Visibility.Collapsed : Visibility.Visible;
            ChatView.Visibility = showChat ? Visibility.Visible : Visibility.Collapsed;
            RefreshButton.ToolTip = showChat ? "刷新运行中的会话" : "刷新额度";
            QuotaTab.SetResourceReference(Button.BackgroundProperty, showChat ? "SurfaceBrush" : "ChromePressedBrush");
            ChatTab.SetResourceReference(Button.BackgroundProperty, showChat ? "ChromePressedBrush" : "SurfaceBrush");
            if (!compactMode) ShowExpandedPanel();
            if (initialized) SaveSettings();
        }

        private void KeepOpenButton_Click(object sender, RoutedEventArgs args)
        {
            keepExpanded = !keepExpanded;
            KeepOpenButton.Content = keepExpanded ? "◆" : "◇";
            KeepOpenButton.ToolTip = keepExpanded ? "允许自动收起" : "保持展开";
            if (keepExpanded) collapseTimer.Stop();
            else ScheduleCompactMode();
        }

        private void WindowSurface_MouseEnter(
            object sender,
            MouseEventArgs eventArgs)
        {
            collapseTimer.Stop();
            if (compactMode && !isDragging && !dragStart.HasValue && !waitForHoverExit) expandTimer.Start();
        }

        private void WindowSurface_MouseLeave(
            object sender,
            MouseEventArgs eventArgs)
        {
            waitForHoverExit = false;
            expandTimer.Stop();
            ScheduleCompactMode();
        }

        private void CollapseButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            if (!HasDockEdges)
            {
                return;
            }

            keepExpanded = false;
            KeepOpenButton.Content = "◇";
            KeepOpenButton.ToolTip = "保持展开";
            EnterCompactMode();
            SaveSettings();
        }

        private void CollapseTimer_Tick(object sender, EventArgs eventArgs)
        {
            collapseTimer.Stop();
            if (!HasDockEdges ||
                compactMode ||
                isDragging ||
                keepExpanded ||
                dragStart.HasValue ||
                ChatView.IsInteracting ||
                IsMouseOver ||
                IsContextMenuOpen)
            {
                ScheduleCompactMode();
                return;
            }

            EnterCompactMode();
            SaveSettings();
        }

        private void ScheduleCompactMode()
        {
            collapseTimer.Stop();
            if (IsVisible && HasDockEdges && !compactMode && !isDragging && !dragStart.HasValue && !keepExpanded)
            {
                collapseTimer.Start();
            }
        }

        private void CaptureDockEdges()
        {
            edgeSnapper.ResetDockAnchor();
            WindowDockEdge? primaryEdge =
                edgeSnapper.PrimaryDockEdge ??
                edgeSnapper.LastHorizontalEdge ??
                edgeSnapper.LastVerticalEdge;

            dockedHorizontalEdge = null;
            dockedVerticalEdge = null;
            if (primaryEdge == WindowDockEdge.Left ||
                primaryEdge == WindowDockEdge.Right)
            {
                dockedHorizontalEdge = primaryEdge;
            }
            else if (primaryEdge == WindowDockEdge.Top ||
                primaryEdge == WindowDockEdge.Bottom)
            {
                dockedVerticalEdge = primaryEdge;
            }
        }

        private void ClearDockEdges()
        {
            edgeSnapper.ResetDockAnchor();
            dockedHorizontalEdge = null;
            dockedVerticalEdge = null;
            collapseTimer.Stop();
            expandTimer.Stop();
        }

        private void EnterCompactMode()
        {
            if (!HasDockEdges)
            {
                return;
            }

            collapseTimer.Stop();
            expandTimer.Stop();
            Thickness previousMargin = WindowSurface.Margin;
            compactMode = true;
            // A drop/collapse under the pointer must stay compact until a fresh hover.
            waitForHoverExit = IsMouseOver;
            FullPanel.BeginAnimation(OpacityProperty, null);
            FullPanel.Visibility = Visibility.Collapsed;
            CompactPanel.Visibility = Visibility.Visible;
            CollapseButton.Visibility = Visibility.Collapsed;

            double compactWidth;
            double compactHeight;
            ConfigureCompactSurface(GetStoredDockEdge(), out compactWidth, out compactHeight);
            ApplyWindowSize(compactWidth, compactHeight, previousMargin);
            if (!IsMouseOver) waitForHoverExit = false;
        }

        private void ExpandDockedPanel()
        {
            if (!HasDockEdges)
            {
                return;
            }

            ShowExpandedPanel();
        }

        private void ShowExpandedPanel()
        {
            bool wasCompact = compactMode;
            waitForHoverExit = false;
            Thickness previousMargin = WindowSurface.Margin;
            compactMode = false;
            CompactPanel.Visibility = Visibility.Collapsed;
            FullPanel.Visibility = Visibility.Visible;
            CollapseButton.Visibility = HasDockEdges
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConfigureFullSurface();
            ApplyWindowSize(ExpandedWidth, ExpandedHeight, previousMargin);
            if (wasCompact && initialized && IsVisible && SystemParameters.ClientAreaAnimation)
            {
                FullPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1,
                    TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
            }
            ScheduleCompactMode();
        }

        private void ShowExpandedPanelDetached(
            WindowDockEdge? originalHorizontalEdge,
            WindowDockEdge? originalVerticalEdge)
        {
            Thickness previousMargin = WindowSurface.Margin;
            compactMode = false;
            CompactPanel.Visibility = Visibility.Collapsed;
            FullPanel.Visibility = Visibility.Visible;
            CollapseButton.Visibility = Visibility.Collapsed;
            ConfigureFullSurface();
            isProgrammaticLayout = true;
            try
            {
                if (!edgeSnapper.ResizePreservingEdgeOffsets(
                    ExpandedWidth,
                    ExpandedHeight,
                    originalHorizontalEdge,
                    originalVerticalEdge,
                    previousMargin))
                {
                    Width = ExpandedWidth;
                    Height = ExpandedHeight;
                    UpdateLayout();
                }
            }
            finally
            {
                isProgrammaticLayout = false;
            }
        }

        private void ApplyWindowSize(double width, double height, Thickness previousMargin)
        {
            isProgrammaticLayout = true;
            try
            {
                if (HasDockEdges && edgeSnapper.ResizeDocked(width, height, previousMargin,
                    GetStoredDockEdge()))
                {
                    return;
                }
                Width = width;
                Height = height;
                UpdateLayout();
                if (initialized) edgeSnapper.ClampToWorkArea();
            }
            finally
            {
                isProgrammaticLayout = false;
            }
        }

        private void ConfigureFullSurface()
        {
            WindowSurface.Margin = new Thickness(FullSurfaceMargin);
            edgeSnapper.VisibleSurfaceMargin = WindowSurface.Margin;
            SurfaceBorder.CornerRadius =
                new CornerRadius(FullCornerRadius);
            SurfaceBorder.Effect = SurfaceShadowEffect;
        }

        private void ConfigureCompactSurface(
            WindowDockEdge edge,
            out double width,
            out double height)
        {
            WindowSurface.Margin = new Thickness(CompactSurfaceMargin);
            edgeSnapper.VisibleSurfaceMargin = WindowSurface.Margin;
            SurfaceBorder.Effect = null;

            double outerMargin = CompactSurfaceMargin * 2;
            if (edge == WindowDockEdge.Left ||
                edge == WindowDockEdge.Right)
            {
                width = CompactThickness + outerMargin;
                height = CompactDiameter + outerMargin;
            }
            else
            {
                width = CompactDiameter + outerMargin;
                height = CompactThickness + outerMargin;
            }

            MiniStatusDot.Width = 4;
            MiniStatusDot.Height = 4;
            switch (edge)
            {
                case WindowDockEdge.Left:
                    SurfaceBorder.CornerRadius =
                        new CornerRadius(
                            0,
                            CompactCornerRadius,
                            CompactCornerRadius,
                            0);
                    MiniStatusDot.HorizontalAlignment =
                        HorizontalAlignment.Right;
                    MiniStatusDot.VerticalAlignment =
                        VerticalAlignment.Top;
                    MiniStatusDot.Margin = new Thickness(0, 5, 5, 0);
                    break;
                case WindowDockEdge.Right:
                    SurfaceBorder.CornerRadius =
                        new CornerRadius(
                            CompactCornerRadius,
                            0,
                            0,
                            CompactCornerRadius);
                    MiniStatusDot.HorizontalAlignment =
                        HorizontalAlignment.Left;
                    MiniStatusDot.VerticalAlignment =
                        VerticalAlignment.Top;
                    MiniStatusDot.Margin = new Thickness(5, 5, 0, 0);
                    break;
                case WindowDockEdge.Top:
                    SurfaceBorder.CornerRadius =
                        new CornerRadius(
                            0,
                            0,
                            CompactCornerRadius,
                            CompactCornerRadius);
                    MiniStatusDot.HorizontalAlignment =
                        HorizontalAlignment.Right;
                    MiniStatusDot.VerticalAlignment =
                        VerticalAlignment.Bottom;
                    MiniStatusDot.Margin = new Thickness(0, 0, 5, 5);
                    break;
                default:
                    SurfaceBorder.CornerRadius =
                        new CornerRadius(
                            CompactCornerRadius,
                            CompactCornerRadius,
                            0,
                            0);
                    MiniStatusDot.HorizontalAlignment =
                        HorizontalAlignment.Right;
                    MiniStatusDot.VerticalAlignment =
                        VerticalAlignment.Top;
                    MiniStatusDot.Margin = new Thickness(0, 5, 5, 0);
                    break;
            }
        }

        private bool IsContextMenuOpen
        {
            get { return WindowSurface.ContextMenu != null && WindowSurface.ContextMenu.IsOpen; }
        }

        private WindowDockEdge GetStoredDockEdge()
        {
            if (dockedHorizontalEdge.HasValue)
            {
                return dockedHorizontalEdge.Value;
            }
            if (dockedVerticalEdge.HasValue)
            {
                return dockedVerticalEdge.Value;
            }
            return WindowDockEdge.Right;
        }

        private void DockToStoredEdges()
        {
            if (dockedHorizontalEdge.HasValue)
            {
                edgeSnapper.DockTo(dockedHorizontalEdge.Value);
            }
            if (dockedVerticalEdge.HasValue)
            {
                edgeSnapper.DockTo(dockedVerticalEdge.Value);
            }
        }

        private void WindowSurface_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs eventArgs)
        {
            DependencyObject source = eventArgs.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is ButtonBase || source is TextBoxBase || source is Selector ||
                    source is ScrollBar || source is ChatPanel)
                {
                    return;
                }
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }

            if (compactMode && eventArgs.ClickCount >= 2)
            {
                ExpandDockedPanel();
                SaveSettings();
                eventArgs.Handled = true;
                return;
            }

            dragStart = eventArgs.GetPosition(this);
            expandTimer.Stop();
            collapseTimer.Stop();
            // Capture on the same element that receives move/up events.
            if (!DragSurface.CaptureMouse()) dragStart = null;
            eventArgs.Handled = true;
        }

        private void CancelPendingDrag()
        {
            dragStart = null;
            if (DragSurface.IsMouseCaptured) DragSurface.ReleaseMouseCapture();
        }

        private void WindowSurface_LostMouseCapture(object sender, MouseEventArgs args)
        {
            if (DragSurface.IsMouseCaptured) return;
            dragStart = null;
            if (!isDragging) ScheduleCompactMode();
        }

        private void WindowSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs args)
        {
            if (!dragStart.HasValue) return;
            CancelPendingDrag();
            if (compactMode && IsMouseOver && !waitForHoverExit) expandTimer.Start();
            else ScheduleCompactMode();
            args.Handled = true;
        }

        private void WindowSurface_MouseMove(object sender, MouseEventArgs eventArgs)
        {
            if (!dragStart.HasValue || isDragging) return;
            if (eventArgs.LeftButton != MouseButtonState.Pressed)
            {
                CancelPendingDrag();
                return;
            }
            System.Windows.Point current = eventArgs.GetPosition(this);
            if (Math.Abs(current.X - dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            Vector initialMovement = current - dragStart.Value;
            isDragging = true;
            CancelPendingDrag();
            FullPanel.BeginAnimation(OpacityProperty, null);
            expandTimer.Stop();
            collapseTimer.Stop();
            eventArgs.Handled = true;

            bool wasDocked = HasDockEdges;
            bool wasCompact = compactMode;
            WindowDockEdge? originalHorizontalEdge =
                dockedHorizontalEdge;
            WindowDockEdge? originalVerticalEdge =
                dockedVerticalEdge;
            edgeSnapper.Suppressed = wasDocked;
            try
            {
                // Preserve motion already received before entering the native move loop.
                Left += initialMovement.X;
                Top += initialMovement.Y;
                if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            }
            finally
            {
                edgeSnapper.Suppressed = false;
                isDragging = false;
            }

            bool snapped = false;
            if (edgeSnapper.Enabled)
            {
                snapped = wasDocked
                    ? edgeSnapper.DetectDockEdges(10)
                    : edgeSnapper.DetectDockEdges(10) ||
                        edgeSnapper.SnapNow(true);
            }

            if (snapped)
            {
                CaptureDockEdges();
                FetchedText.Text =
                    "已吸附到屏幕" + GetLastSnapEdgeName();
                EnterCompactMode();
            }
            else
            {
                bool detached = wasDocked;
                ClearDockEdges();
                if (detached && wasCompact)
                {
                    ShowExpandedPanelDetached(
                        originalHorizontalEdge,
                        originalVerticalEdge);
                }
                else
                {
                    ShowExpandedPanel();
                }
                edgeSnapper.ClampToWorkArea();
                if (detached)
                {
                    FetchedText.Text = "已脱离屏幕边缘";
                }
            }

            SaveSettings();
        }

        private void DockLeftMenuItem_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DockToEdge(WindowDockEdge.Left);
        }

        private void DockRightMenuItem_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DockToEdge(WindowDockEdge.Right);
        }

        private void DockTopMenuItem_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DockToEdge(WindowDockEdge.Top);
        }

        private void DockBottomMenuItem_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DockToEdge(WindowDockEdge.Bottom);
        }

        private void DockToEdge(WindowDockEdge edge, double? anchorRatio = null)
        {
            edgeSnapper.ResetDockAnchor();
            if (!edgeSnapper.DockTo(edge))
            {
                return;
            }

            edgeSnapper.Enabled = true;
            settings.EdgeSnapEnabled = true;
            edgeSnapMenuItem.Checked = true;
            edgeSnapper.RestoreDockAnchor(edge, anchorRatio);

            dockedHorizontalEdge =
                edge == WindowDockEdge.Left ||
                edge == WindowDockEdge.Right
                    ? (WindowDockEdge?)edge
                    : null;
            dockedVerticalEdge =
                edge == WindowDockEdge.Top ||
                edge == WindowDockEdge.Bottom
                    ? (WindowDockEdge?)edge
                    : null;
            FetchedText.Text = "已吸附到屏幕" + GetDockEdgeName(edge);
            EnterCompactMode();
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(SaveSettings));
        }

        private static string GetDockEdgeName(WindowDockEdge edge)
        {
            switch (edge)
            {
                case WindowDockEdge.Left:
                    return "左侧";
                case WindowDockEdge.Right:
                    return "右侧";
                case WindowDockEdge.Top:
                    return "顶部";
                default:
                    return "底部";
            }
        }

        private string GetLastSnapEdgeName()
        {
            if (dockedHorizontalEdge.HasValue)
            {
                return GetDockEdgeName(dockedHorizontalEdge.Value);
            }

            if (dockedVerticalEdge.HasValue)
            {
                return GetDockEdgeName(dockedVerticalEdge.Value);
            }

            return "边缘";
        }

        private void RestorePosition()
        {
            if (settings.HasWindowPosition &&
                IsFinite(settings.WindowLeft) &&
                IsFinite(settings.WindowTop))
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
                edgeSnapper.ClampToWorkArea();
            }
            else
            {
                settings.HasWindowPosition = false;
                edgeSnapper.PlaceAtTopRight(18);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value > -1000000 &&
                value < 1000000;
        }

        private void SaveSettings()
        {
            if (initialized && IsFinite(Left) && IsFinite(Top))
            {
                settings.HasWindowPosition = true;
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
            }
            settings.TopMost = Topmost;
            settings.Theme = currentTheme.ToString();
            settings.EdgeSnapEnabled = edgeSnapper.Enabled;
            settings.ChatView = chatMode;
            settings.CodexDataDirectory = ChatView.DataDirectory;
            settings.DockEdge = HasDockEdges ? GetStoredDockEdge().ToString() : null;
            settings.DockAnchorRatio = HasDockEdges ? edgeSnapper.GetDockAnchorRatio() : null;
            settingsStore.Save(settings);
        }
    }
}
