using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using CodexUsageMonitor;

internal static class WindowInteractionTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [STAThread]
    private static int Main()
    {
        MainWindow window = null;
        try
        {
            App app = new App();
            app.InitializeComponent();
            window = new MainWindow();
            // Keep persistence inside the test output, including cleanup on failure.
            object store = Field(window, "settingsStore").GetValue(window);
            store.GetType().GetField("settingsPath", PrivateInstance).SetValue(store,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window-test.settings.json"));

            ((Button)window.FindName("QuotaTab")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CheckCompactGeometry(window);
            CheckThemesAndChrome(window);
            CheckCaptureCleanup(window);
            CheckCardScrolling(window);
            CheckDockActions(window);
            CheckMonitorDpi(window);
            Console.WriteLine("PASS " + checks + " window regression checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (window != null) window.PrepareForExit();
        }
    }

    private static void CheckCompactGeometry(MainWindow window)
    {
        Type edgeType = window.GetType().Assembly.GetType("CodexUsageMonitor.WindowDockEdge");
        Grid surface = (Grid)window.FindName("WindowSurface");
        Border border = (Border)window.FindName("SurfaceBorder");
        string[] edges = { "Left", "Right", "Top", "Bottom" };
        CornerRadius[] corners =
        {
            new CornerRadius(0, 26, 26, 0), new CornerRadius(26, 0, 0, 26),
            new CornerRadius(0, 0, 26, 26), new CornerRadius(26, 26, 0, 0)
        };
        for (int i = 0; i < edges.Length; i++)
        {
            string edge = edges[i];
            bool horizontal = edge == "Left" || edge == "Right";
            object value = Enum.Parse(edgeType, edge);
            Field(window, "dockedHorizontalEdge").SetValue(window, horizontal ? value : null);
            Field(window, "dockedVerticalEdge").SetValue(window, horizontal ? null : value);
            for (int repeat = 0; repeat < 20; repeat++)
            {
                Call(window, "EnterCompactMode");
                double width = window.Width - surface.Margin.Left - surface.Margin.Right;
                double height = window.Height - surface.Margin.Top - surface.Margin.Bottom;
                Require(width == (horizontal ? 30 : 52) && height == (horizontal ? 52 : 30),
                    "Original semicircle geometry on " + edge);
                Require(border.CornerRadius == corners[i], "Straight edge faces screen edge on " + edge);
                Call(window, "ShowExpandedPanel");
                Require(window.Width == 520 && window.Height == 326, "Expanded quota geometry on " + edge);
            }
            Pass("stable compact/expanded geometry on " + edge);
        }
    }

    private static void CheckThemesAndChrome(MainWindow window)
    {
        Button theme = (Button)window.FindName("ThemeButton");
        Border border = (Border)window.FindName("SurfaceBorder");
        foreach (string tab in new[] { "QuotaTab", "ChatTab" })
        {
            ((Button)window.FindName(tab)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Color initial = ((SolidColorBrush)border.Background).Color;
            Color previous = initial;
            for (int i = 0; i < 3; i++)
            {
                theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Color current = ((SolidColorBrush)border.Background).Color;
                Require(current != previous && current.A == 255, "Theme changes to an opaque surface");
                Require(theme.ToolTip.ToString().Contains("点击切换"), "Theme tooltip refreshes");
                previous = current;
            }
            Require(previous == initial, "Three themes cycle back to the starting theme");
            Require(theme.Parent == ((Button)window.FindName("KeepOpenButton")).Parent,
                "Shared header controls");
            Pass("all three themes through " + tab);
        }
        Grid surface = (Grid)window.FindName("WindowSurface");
        Color before = ((SolidColorBrush)border.Background).Color;
        ((MenuItem)surface.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(((SolidColorBrush)border.Background).Color != before, "Compact context menu switches theme");
        Pass("context menu theme switching");
    }

    private static void CheckCaptureCleanup(MainWindow window)
    {
        Grid dragSurface = (Grid)window.FindName("DragSurface");
        Field(window, "dragStart").SetValue(window, new Point(10, 10));
        dragSurface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = Mouse.LostMouseCaptureEvent
        });
        Require(Field(window, "dragStart").GetValue(window) == null, "Capture loss clears pending drag");
        Pass("capture loss cancels pending drag");

        Field(window, "dragStart").SetValue(window, new Point(10, 10));
        dragSurface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });
        Require(Field(window, "dragStart").GetValue(window) == null, "Mouse release clears pending drag");
        Pass("mouse release cancels pending drag");
    }

    private static void CheckDockActions(MainWindow window)
    {
        new WindowInteropHelper(window).EnsureHandle();
        Grid surface = (Grid)window.FindName("WindowSurface");
        for (int i = 2; i < 6; i++)
        {
            Call(window, "ShowExpandedPanel");
            ((MenuItem)surface.ContextMenu.Items[i]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require((bool)Field(window, "compactMode").GetValue(window), "Docking immediately collapses the panel");
            Require(window.Width <= 64 && window.Height <= 64, "Docked window uses compact bounds");
        }
        Pass("all four dock actions immediately collapse");

        Field(window, "waitForHoverExit").SetValue(window, true);
        ((Grid)window.FindName("DragSurface")).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = Mouse.MouseLeaveEvent
        });
        Require(!(bool)Field(window, "waitForHoverExit").GetValue(window), "Leaving enables the next hover");
        Pass("fresh hover enabled after pointer exit");
    }

    private static void CheckCardScrolling(MainWindow window)
    {
        ((Button)window.FindName("ChatTab")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Call(window, "ShowExpandedPanel");
        bool hasHandle = new WindowInteropHelper(window).Handle != IntPtr.Zero;
        ChatPanel panel = (ChatPanel)window.FindName("ChatView");
        IList cards = (IList)Field(panel, "cards").GetValue(panel);
        Type cardType = typeof(ChatPanel).Assembly.GetType("CodexUsageMonitor.SessionCard");
        ScrollViewer scroller = (ScrollViewer)panel.FindName("CardScroller");
        double previousHeight = 0;
        double fiveCardHeight = 0;
        foreach (int count in new[] { 1, 2, 3, 4, 5, 6, 0, 3 })
        {
            cards.Clear();
            for (int i = 0; i < count; i++)
            {
                object card = Activator.CreateInstance(cardType, true);
                cardType.GetProperty("Title").SetValue(card, "Synthetic session " + i, null);
                cardType.GetProperty("Detail").SetValue(card, "test-model · running", null);
                foreach (string property in new[] { "Speed", "Input", "Cached", "Output", "Reasoning" })
                    cardType.GetProperty(property).SetValue(card, "123.4", null);
                cards.Add(card);
            }
            ((FrameworkElement)panel.FindName("EmptyState")).Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Call(panel, "UpdateContentHeight");
            if (hasHandle)
                ArrangeRoot(window, (Grid)window.FindName("DragSurface"));
            else
            {
                panel.Measure(new Size(490, panel.PreferredHeight));
                panel.Arrange(new Rect(0, 0, 490, panel.PreferredHeight));
                panel.UpdateLayout();
            }
            Require(count > 5 ? scroller.ScrollableHeight > 0.5 : scroller.ScrollableHeight <= 0.5,
                "Scroll threshold failed with " + count + " cards; extent=" + scroller.ExtentHeight +
                ", viewport=" + scroller.ViewportHeight + ", panel=" + panel.ActualHeight + ", preferred=" + panel.PreferredHeight);
            if (count <= 5 && count > 0 && fiveCardHeight == 0)
                Require(panel.PreferredHeight > previousHeight, "Card list did not grow with its content");
            if (count == 5) fiveCardHeight = panel.PreferredHeight;
            if (count == 6) Require(panel.PreferredHeight == fiveCardHeight, "Sixth card enlarged the window");
            previousHeight = panel.PreferredHeight;
        }
        // On a short display, all cards remain reachable even when fewer than five fit.
        if (hasHandle)
        {
            window.Height = 250;
            ArrangeRoot(window, (Grid)window.FindName("DragSurface"));
        }
        else
        {
            panel.Measure(new Size(490, 180));
            panel.Arrange(new Rect(0, 0, 490, 180));
            panel.UpdateLayout();
        }
        Require(scroller.ScrollableHeight > 0.5, "Short displays clipped cards instead of allowing scrolling");
        Call(window, "ShowExpandedPanel");
        Pass("adaptive card height and scrolling after five cards");
    }

    private static void CheckMonitorDpi(MainWindow window)
    {
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        object snapper = Field(window, "edgeSnapper").GetValue(window);
        Type edgeType = window.GetType().Assembly.GetType("CodexUsageMonitor.WindowDockEdge");
        Grid root = (Grid)window.FindName("DragSurface");
        Border border = (Border)window.FindName("SurfaceBorder");
        // Only move our hidden HWND; never capture input or alter the user's window.
        for (int trip = 0; trip < 2; trip++)
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
        {
            Call(window, "ClearDockEdges");
            var area = screen.WorkingArea;
            SetWindowPos(handle, IntPtr.Zero, area.Left + 100, area.Top + 100, 0, 0, 0x15);
            Pump();
            Matrix transform = HwndSource.FromHwnd(handle).CompositionTarget.TransformToDevice;
            double scale = GetDpiForWindow(handle) / 96.0;
            Require(Math.Abs(transform.M11 - scale) < 0.001 && Math.Abs(transform.M22 - scale) < 0.001,
                "WPF must follow monitor DPI on " + screen.DeviceName + "; native=" + scale + ", WPF=" + transform.M11);
            CheckCardScrolling(window);

            CheckDropOverflow(window, screen, scale);

            foreach (string edge in new[] { "Left", "Right", "Top", "Bottom" })
            {
                bool horizontal = edge == "Left" || edge == "Right";
                object value = Enum.Parse(edgeType, edge);
                Field(window, "dockedHorizontalEdge").SetValue(window, horizontal ? value : null);
                Field(window, "dockedVerticalEdge").SetValue(window, horizontal ? null : value);
                snapper.GetType().GetMethod("ResetDockAnchor").Invoke(snapper, null);
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    Call(window, "EnterCompactMode");
                    Pump();
                    CheckNativeSize(window, handle, scale, horizontal ? 42 : 64, horizontal ? 64 : 42);
                    ArrangeRoot(window, root);
                    Require(Math.Abs(border.ActualWidth - (horizontal ? 30 : 52)) < 1 &&
                        Math.Abs(border.ActualHeight - (horizontal ? 52 : 30)) < 1,
                        "Semicircle stretched on " + screen.DeviceName + " " + edge);
                    NativeRect rect;
                    GetWindowRect(handle, out rect);
                    double gap = edge == "Left" ? rect.Left + 6 * scale - area.Left :
                        edge == "Right" ? rect.Right - 6 * scale - area.Right :
                        edge == "Top" ? rect.Top + 6 * scale - area.Top : rect.Bottom - 6 * scale - area.Bottom;
                    Require(Math.Abs(gap) <= 1, "Visible compact edge left the work area");

                    foreach (string tab in new[] { "QuotaTab", "ChatTab" })
                    {
                        ((Button)window.FindName(tab)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Call(window, "ShowExpandedPanel");
                        Pump();
                        double available = (double)snapper.GetType().GetMethod("GetAvailableHeight").Invoke(snapper, null);
                        Thickness surfaceMargin = ((Grid)window.FindName("WindowSurface")).Margin;
                        double chromeHeight = ((Grid)window.FindName("FullPanel")).RowDefinitions[0].Height.Value +
                            surfaceMargin.Top + surfaceMargin.Bottom + border.BorderThickness.Top + border.BorderThickness.Bottom;
                        double height = tab == "QuotaTab" ? 326 : Math.Min(
                            ((ChatPanel)window.FindName("ChatView")).PreferredHeight + chromeHeight, available);
                        CheckNativeSize(window, handle, scale, 520, height);
                        ArrangeRoot(window, root);
                        Require(Math.Abs(border.ActualWidth - 492) <= 1, "Tabs must have equal visible width");
                        Require(root.ActualWidth >= border.ActualWidth, "Content exceeded native window");
                    }
                }
            }
            Pass("mixed-DPI round trip " + (trip + 1) + " on " + screen.DeviceName + " at " + (scale * 100) + "%");
        }
        if (Forms.Screen.AllScreens.Length < 2)
            Console.WriteLine("NOTE Only one display is connected; cross-monitor coverage requires a mixed-DPI desktop.");
    }

    private static void CheckDropOverflow(MainWindow window, Forms.Screen screen, double scale)
    {
        object snapper = Field(window, "edgeSnapper").GetValue(window);
        MethodInfo apply = snapper.GetType().GetMethod("ApplySnap", PrivateInstance);
        Type rectType = apply.GetParameters()[0].ParameterType.GetElementType();
        var area = screen.WorkingArea;
        int margin = (int)Math.Round(14 * scale);
        int width = (int)Math.Round(520 * scale);
        int height = (int)Math.Round(326 * scale);
        foreach (string edge in new[] { "Left", "Right", "Top", "Bottom" })
        foreach (int gapDips in new[] { -96, -32, -8, 0, 16, 40 })
        foreach (bool release in new[] { false, true })
        {
            int gap = (int)Math.Round(gapDips * scale);
            int left = area.Left + (area.Width - width) / 2;
            int top = area.Top + (area.Height - height) / 2;
            if (edge == "Left") left = area.Left + gap - margin;
            if (edge == "Right") left = area.Right - gap + margin - width;
            if (edge == "Top") top = area.Top + gap - margin;
            if (edge == "Bottom") top = area.Bottom - gap + margin - height;
            object rect = Activator.CreateInstance(rectType);
            rectType.GetField("Left").SetValue(rect, left);
            rectType.GetField("Right").SetValue(rect, left + width);
            rectType.GetField("Top").SetValue(rect, top);
            rectType.GetField("Bottom").SetValue(rect, top + height);
            // Supply geometry without moving the user's pointer. Live movement
            // must remain non-sticky; release accepts an overshot visible edge.
            object[] arguments = { rect, false, false, release };
            bool snapped = (bool)apply.Invoke(snapper, arguments);
            bool expected = release ? gapDips <= 24 : Math.Abs(gapDips) <= 24;
            Require(snapped == expected, "Drop tolerance failed on " + edge + ", gap=" + gapDips + ", release=" + release);
            object primary = snapper.GetType().GetProperty("PrimaryDockEdge").GetValue(snapper, null);
            if (!expected)
            {
                Require(primary == null, "Dragging inward retained a stale dock edge");
                continue;
            }
            Require(primary != null && primary.ToString() == edge, "Drop picked the wrong edge");
            object target = arguments[0];
            int coordinate = (int)rectType.GetField(edge).GetValue(target);
            int expectedCoordinate = edge == "Left" ? area.Left - margin : edge == "Right" ? area.Right + margin :
                edge == "Top" ? area.Top - margin : area.Bottom + margin;
            Require(coordinate == expectedCoordinate, "Drop did not align the visible surface");
        }
        Pass("release accepts edge overflow while inward movement detaches on " + screen.DeviceName);
    }

    private static void CheckNativeSize(MainWindow window, IntPtr handle, double scale, double width, double height)
    {
        NativeRect rect;
        Require(GetWindowRect(handle, out rect), "Native window bounds unavailable");
        Require(Math.Abs(rect.Right - rect.Left - width * scale) <= 1 &&
            Math.Abs(rect.Bottom - rect.Top - height * scale) <= 1,
            "Native size does not match content: " + (rect.Right - rect.Left) + "x" + (rect.Bottom - rect.Top) +
            ", expected=" + width * scale + "x" + height * scale);
        Require(Math.Abs(window.Width - width) <= 1 / scale && Math.Abs(window.Height - height) <= 1 / scale,
            "DPI transition changed logical window dimensions");
    }

    private static void ArrangeRoot(MainWindow window, Grid root)
    {
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        root.UpdateLayout();
    }

    private static void Pump()
    {
        DispatcherFrame frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    private static FieldInfo Field(object target, string name)
    {
        return target.GetType().GetField(name, PrivateInstance);
    }

    private static void Call(object target, string name)
    {
        target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pass(string message)
    {
        checks++;
        Console.WriteLine("PASS " + message);
    }
}
