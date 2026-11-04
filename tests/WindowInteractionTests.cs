using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
                Require(window.Width == 392 && window.Height == 286, "Expanded quota geometry on " + edge);
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
            panel.Measure(new Size(490, panel.PreferredHeight));
            panel.Arrange(new Rect(0, 0, 490, panel.PreferredHeight));
            panel.UpdateLayout();
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
        panel.Measure(new Size(490, 180));
        panel.Arrange(new Rect(0, 0, 490, 180));
        panel.UpdateLayout();
        Require(scroller.ScrollableHeight > 0.5, "Short displays clipped cards instead of allowing scrolling");
        Pass("adaptive card height and scrolling after five cards");
    }

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
