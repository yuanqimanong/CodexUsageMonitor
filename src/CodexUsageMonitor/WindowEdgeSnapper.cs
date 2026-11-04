using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexUsageMonitor
{
    internal enum WindowDockEdge
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// Implements live, per-monitor edge snapping in the native window-move loop.
    /// All geometry in the hook stays in physical pixels to avoid mixed-DPI drift.
    /// </summary>
    internal sealed class WindowEdgeSnapper : IDisposable
    {
        private const int WindowMoving = 0x0216;
        private const int WindowExitSizeMove = 0x0232;
        private const uint MonitorDefaultToNearest = 2;
        private const uint SetWindowPosNoSize = 0x0001;
        private const uint SetWindowPosNoZOrder = 0x0004;
        private const uint SetWindowPosNoActivate = 0x0010;

        private readonly Window window;
        private Thickness visibleSurfaceMargin;
        private readonly double thresholdDips;
        private HwndSource source;
        private IntPtr handle;
        private bool disposed;
        private bool enabled;
        private bool suppressed;
        private double? dockAnchor;
        private IntPtr anchorMonitor;
        private WindowDockEdge? anchorEdge;

        public void ResetDockAnchor()
        {
            dockAnchor = null;
        }

        public double GetAvailableHeight()
        {
            NativeRect workArea;
            if (handle == IntPtr.Zero || !TryGetWorkArea(MonitorFromWindow(handle, MonitorDefaultToNearest), out workArea))
                return SystemParameters.WorkArea.Height;
            return (workArea.Bottom - workArea.Top) / GetDpiScale() + visibleSurfaceMargin.Top + visibleSurfaceMargin.Bottom;
        }

        public double? GetDockAnchorRatio()
        {
            NativeRect workArea;
            if (!dockAnchor.HasValue || !anchorEdge.HasValue || !TryGetWorkArea(anchorMonitor, out workArea)) return null;
            bool horizontal = anchorEdge == WindowDockEdge.Left || anchorEdge == WindowDockEdge.Right;
            double start = horizontal ? workArea.Top : workArea.Left;
            double length = horizontal ? workArea.Bottom - workArea.Top : workArea.Right - workArea.Left;
            return length > 0 ? (double?)Math.Max(0, Math.Min(1, (dockAnchor.Value - start) / length)) : null;
        }

        public void RestoreDockAnchor(WindowDockEdge edge, double? ratio)
        {
            if (!ratio.HasValue || double.IsNaN(ratio.Value) || double.IsInfinity(ratio.Value)) return;
            NativeRect workArea;
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (!TryGetWorkArea(monitor, out workArea)) return;
            bool horizontal = edge == WindowDockEdge.Left || edge == WindowDockEdge.Right;
            double start = horizontal ? workArea.Top : workArea.Left;
            double length = horizontal ? workArea.Bottom - workArea.Top : workArea.Right - workArea.Left;
            dockAnchor = start + Math.Max(0, Math.Min(1, ratio.Value)) * length;
            anchorMonitor = monitor;
            anchorEdge = edge;
        }

        public bool ResizeDocked(double widthDips, double heightDips, Thickness previousMargin,
            WindowDockEdge edge)
        {
            NativeRect before;
            NativeRect workArea;
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out before) || !TryGetWorkArea(monitor, out workArea))
                return false;

            double scale = GetDpiScale();
            PixelInsets afterInsets = GetPixelInsets(scale);
            bool horizontal = edge == WindowDockEdge.Left || edge == WindowDockEdge.Right;
            if (!dockAnchor.HasValue || anchorMonitor != monitor || anchorEdge != edge)
            {
                dockAnchor = horizontal
                    ? (before.Top + previousMargin.Top * scale + before.Bottom - previousMargin.Bottom * scale) / 2
                    : (before.Left + previousMargin.Left * scale + before.Right - previousMargin.Right * scale) / 2;
                anchorMonitor = monitor;
                anchorEdge = edge;
            }

            int width = Math.Min((int)Math.Round(widthDips * scale), workArea.Right - workArea.Left + afterInsets.Left + afterInsets.Right);
            int height = Math.Min((int)Math.Round(heightDips * scale), workArea.Bottom - workArea.Top + afterInsets.Top + afterInsets.Bottom);
            int visibleWidth = width - afterInsets.Left - afterInsets.Right;
            int visibleHeight = height - afterInsets.Top - afterInsets.Bottom;
            int left = horizontal
                ? (edge == WindowDockEdge.Left ? workArea.Left - afterInsets.Left : workArea.Right + afterInsets.Right - width)
                : (int)Math.Round(dockAnchor.Value - visibleWidth / 2.0) - afterInsets.Left;
            int top = horizontal
                ? (int)Math.Round(dockAnchor.Value - visibleHeight / 2.0) - afterInsets.Top
                : (edge == WindowDockEdge.Top ? workArea.Top - afterInsets.Top : workArea.Bottom + afterInsets.Bottom - height);
            left = Math.Max(workArea.Left - afterInsets.Left, Math.Min(left, workArea.Right + afterInsets.Right - width));
            top = Math.Max(workArea.Top - afterInsets.Top, Math.Min(top, workArea.Bottom + afterInsets.Bottom - height));
            NativeRect target = new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
            // Commit final geometry once. Resizing the HWND every frame causes
            // layout churn and can leave the compact surface stretched on interruption.
            ApplyBounds(target, scale);
            return true;
        }

        private void ApplyBounds(NativeRect rectangle, double scale)
        {
            window.Width = (rectangle.Right - rectangle.Left) / scale;
            window.Height = (rectangle.Bottom - rectangle.Top) / scale;
            SetWindowPos(handle, IntPtr.Zero, rectangle.Left, rectangle.Top,
                rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top,
                SetWindowPosNoZOrder | SetWindowPosNoActivate);
        }

        public WindowEdgeSnapper(
            Window window,
            Thickness visibleSurfaceMargin,
            double thresholdDips)
        {
            this.window = window;
            this.visibleSurfaceMargin = visibleSurfaceMargin;
            this.thresholdDips = thresholdDips;
            enabled = true;

            window.SourceInitialized += HandleSourceInitialized;
            window.Closed += HandleWindowClosed;
        }

        public event EventHandler MoveCompleted;

        public WindowDockEdge? LastHorizontalEdge { get; private set; }

        public WindowDockEdge? LastVerticalEdge { get; private set; }

        public WindowDockEdge? PrimaryDockEdge { get; private set; }

        public Thickness VisibleSurfaceMargin
        {
            get { return visibleSurfaceMargin; }
            set { visibleSurfaceMargin = value; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        public bool Suppressed
        {
            get { return suppressed; }
            set { suppressed = value; }
        }

        public bool SnapNow()
        {
            return SnapNow(false);
        }

        public bool SnapNow(bool preferCursorMonitor)
        {
            if (!enabled || suppressed || handle == IntPtr.Zero)
            {
                return false;
            }

            NativeRect rectangle;
            if (!GetWindowRect(handle, out rectangle) ||
                !ApplySnap(
                    ref rectangle,
                    preferCursorMonitor,
                    preferCursorMonitor))
            {
                return false;
            }

            return MoveWindow(rectangle);
        }

        public bool DetectDockEdges(double toleranceDips)
        {
            LastHorizontalEdge = null;
            LastVerticalEdge = null;
            PrimaryDockEdge = null;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            NativeRect rectangle;
            NativeRect workArea;
            if (!GetWindowRect(handle, out rectangle) ||
                !TryGetWorkArea(ref rectangle, false, out workArea))
            {
                return false;
            }

            double scale = GetDpiScale();
            PixelInsets insets = GetPixelInsets(scale);
            int tolerance = Math.Max(
                2,
                (int)Math.Round(toleranceDips * scale));
            int visibleLeft = rectangle.Left + insets.Left;
            int visibleRight = rectangle.Right - insets.Right;
            int visibleTop = rectangle.Top + insets.Top;
            int visibleBottom = rectangle.Bottom - insets.Bottom;
            int leftDistance = Math.Abs(visibleLeft - workArea.Left);
            int rightDistance = Math.Abs(visibleRight - workArea.Right);
            int topDistance = Math.Abs(visibleTop - workArea.Top);
            int bottomDistance = Math.Abs(visibleBottom - workArea.Bottom);

            if (Math.Min(leftDistance, rightDistance) <= tolerance)
            {
                LastHorizontalEdge = leftDistance <= rightDistance
                    ? WindowDockEdge.Left
                    : WindowDockEdge.Right;
            }

            if (Math.Min(topDistance, bottomDistance) <= tolerance)
            {
                LastVerticalEdge = topDistance <= bottomDistance
                    ? WindowDockEdge.Top
                    : WindowDockEdge.Bottom;
            }

            if (LastHorizontalEdge.HasValue &&
                LastVerticalEdge.HasValue)
            {
                PrimaryDockEdge =
                    Math.Min(leftDistance, rightDistance) <=
                    Math.Min(topDistance, bottomDistance)
                        ? LastHorizontalEdge
                        : LastVerticalEdge;
            }
            else
            {
                PrimaryDockEdge = LastHorizontalEdge ??
                    LastVerticalEdge;
            }

            return LastHorizontalEdge.HasValue ||
                LastVerticalEdge.HasValue;
        }

        public bool DockTo(WindowDockEdge edge)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            PrimaryDockEdge = edge;
            LastHorizontalEdge =
                edge == WindowDockEdge.Left ||
                edge == WindowDockEdge.Right
                    ? (WindowDockEdge?)edge
                    : null;
            LastVerticalEdge =
                edge == WindowDockEdge.Top ||
                edge == WindowDockEdge.Bottom
                    ? (WindowDockEdge?)edge
                    : null;

            NativeRect rectangle;
            if (!GetWindowRect(handle, out rectangle))
            {
                return false;
            }

            IntPtr monitor = MonitorFromWindow(
                handle,
                MonitorDefaultToNearest);
            NativeRect workArea;
            if (!TryGetWorkArea(monitor, out workArea))
            {
                return false;
            }

            PixelInsets insets = GetPixelInsets();
            int width = rectangle.Right - rectangle.Left;
            int height = rectangle.Bottom - rectangle.Top;
            int visibleWidth = Math.Max(
                1,
                width - insets.Left - insets.Right);
            int visibleHeight = Math.Max(
                1,
                height - insets.Top - insets.Bottom);

            switch (edge)
            {
                case WindowDockEdge.Left:
                    rectangle.Left = workArea.Left - insets.Left;
                    rectangle.Right = rectangle.Left + width;
                    break;
                case WindowDockEdge.Right:
                    rectangle.Right = workArea.Right + insets.Right;
                    rectangle.Left = rectangle.Right - width;
                    break;
                case WindowDockEdge.Top:
                    rectangle.Top = workArea.Top - insets.Top;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
                case WindowDockEdge.Bottom:
                    rectangle.Bottom = workArea.Bottom + insets.Bottom;
                    rectangle.Top = rectangle.Bottom - height;
                    break;
            }

            int minimumLeft = workArea.Left - insets.Left;
            int maximumLeft =
                workArea.Right - visibleWidth - insets.Left;
            int minimumTop = workArea.Top - insets.Top;
            int maximumTop =
                workArea.Bottom - visibleHeight - insets.Top;

            if (edge == WindowDockEdge.Left ||
                edge == WindowDockEdge.Right)
            {
                rectangle.Top = Math.Max(
                    minimumTop,
                    Math.Min(rectangle.Top, maximumTop));
                rectangle.Bottom = rectangle.Top + height;
            }
            else
            {
                rectangle.Left = Math.Max(
                    minimumLeft,
                    Math.Min(rectangle.Left, maximumLeft));
                rectangle.Right = rectangle.Left + width;
            }

            return MoveWindow(rectangle);
        }

        public void ClampToWorkArea()
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            NativeRect rectangle;
            NativeRect workArea;
            if (!GetWindowRect(handle, out rectangle) ||
                !TryGetWorkArea(ref rectangle, false, out workArea))
            {
                return;
            }

            PixelInsets insets = GetPixelInsets();
            int deltaX = 0;
            int deltaY = 0;

            int visibleLeft = rectangle.Left + insets.Left;
            int visibleRight = rectangle.Right - insets.Right;
            int visibleTop = rectangle.Top + insets.Top;
            int visibleBottom = rectangle.Bottom - insets.Bottom;

            if (visibleLeft < workArea.Left)
            {
                deltaX = workArea.Left - visibleLeft;
            }
            else if (visibleRight > workArea.Right)
            {
                deltaX = workArea.Right - visibleRight;
            }

            if (visibleTop < workArea.Top)
            {
                deltaY = workArea.Top - visibleTop;
            }
            else if (visibleBottom > workArea.Bottom)
            {
                deltaY = workArea.Bottom - visibleBottom;
            }

            if (deltaX != 0 || deltaY != 0)
            {
                Offset(ref rectangle, deltaX, deltaY);
                MoveWindow(rectangle);
            }
        }

        public void PlaceAtTopRight(double gapDips)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            NativeRect rectangle;
            NativeRect workArea;
            if (!GetWindowRect(handle, out rectangle) ||
                !TryGetWorkArea(ref rectangle, false, out workArea))
            {
                return;
            }

            double scale = GetDpiScale();
            PixelInsets insets = GetPixelInsets(scale);
            int gap = Math.Max(0, (int)Math.Round(gapDips * scale));
            int width = rectangle.Right - rectangle.Left;
            int height = rectangle.Bottom - rectangle.Top;
            int visibleWidth = Math.Max(1, width - insets.Left - insets.Right);

            rectangle.Left =
                workArea.Right - gap - visibleWidth - insets.Left;
            rectangle.Top = workArea.Top + gap - insets.Top;
            rectangle.Right = rectangle.Left + width;
            rectangle.Bottom = rectangle.Top + height;
            MoveWindow(rectangle);
        }

        public bool ResizePreservingEdgeOffsets(
            double widthDips,
            double heightDips,
            WindowDockEdge? horizontalEdge,
            WindowDockEdge? verticalEdge,
            Thickness previousMargin)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            NativeRect before;
            NativeRect workArea;
            if (!GetWindowRect(handle, out before) ||
                !TryGetWorkArea(ref before, false, out workArea))
            {
                return false;
            }

            Thickness targetMargin = visibleSurfaceMargin;
            visibleSurfaceMargin = previousMargin;
            PixelInsets beforeInsets = GetPixelInsets();
            visibleSurfaceMargin = targetMargin;
            int beforeVisibleLeft = before.Left + beforeInsets.Left;
            int beforeVisibleRight = before.Right - beforeInsets.Right;
            int beforeVisibleTop = before.Top + beforeInsets.Top;
            int beforeVisibleBottom = before.Bottom - beforeInsets.Bottom;
            int horizontalGap = 0;
            int verticalGap = 0;
            int visibleCenterX =
                beforeVisibleLeft +
                (beforeVisibleRight - beforeVisibleLeft) / 2;
            int visibleCenterY =
                beforeVisibleTop +
                (beforeVisibleBottom - beforeVisibleTop) / 2;

            if (horizontalEdge == WindowDockEdge.Left)
            {
                horizontalGap = beforeVisibleLeft - workArea.Left;
            }
            else if (horizontalEdge == WindowDockEdge.Right)
            {
                horizontalGap = workArea.Right - beforeVisibleRight;
            }

            if (verticalEdge == WindowDockEdge.Top)
            {
                verticalGap = beforeVisibleTop - workArea.Top;
            }
            else if (verticalEdge == WindowDockEdge.Bottom)
            {
                verticalGap = workArea.Bottom - beforeVisibleBottom;
            }

            window.Width = widthDips;
            window.Height = heightDips;
            window.UpdateLayout();

            NativeRect after;
            if (!GetWindowRect(handle, out after))
            {
                return false;
            }

            PixelInsets afterInsets = GetPixelInsets();
            int width = after.Right - after.Left;
            int height = after.Bottom - after.Top;
            int visibleWidth = Math.Max(
                1,
                width - afterInsets.Left - afterInsets.Right);
            int visibleHeight = Math.Max(
                1,
                height - afterInsets.Top - afterInsets.Bottom);

            if (horizontalEdge == WindowDockEdge.Left)
            {
                after.Left =
                    workArea.Left + horizontalGap - afterInsets.Left;
            }
            else if (horizontalEdge == WindowDockEdge.Right)
            {
                after.Left =
                    workArea.Right -
                    horizontalGap -
                    visibleWidth -
                    afterInsets.Left;
            }
            else
            {
                after.Left =
                    visibleCenterX -
                    visibleWidth / 2 -
                    afterInsets.Left;
            }

            if (verticalEdge == WindowDockEdge.Top)
            {
                after.Top =
                    workArea.Top + verticalGap - afterInsets.Top;
            }
            else if (verticalEdge == WindowDockEdge.Bottom)
            {
                after.Top =
                    workArea.Bottom -
                    verticalGap -
                    visibleHeight -
                    afterInsets.Top;
            }
            else
            {
                after.Top =
                    visibleCenterY -
                    visibleHeight / 2 -
                    afterInsets.Top;
            }

            int minimumLeft = workArea.Left - afterInsets.Left;
            int maximumLeft =
                workArea.Right - visibleWidth - afterInsets.Left;
            int minimumTop = workArea.Top - afterInsets.Top;
            int maximumTop =
                workArea.Bottom - visibleHeight - afterInsets.Top;
            after.Left = Math.Max(
                minimumLeft,
                Math.Min(after.Left, maximumLeft));
            after.Top = Math.Max(
                minimumTop,
                Math.Min(after.Top, maximumTop));
            after.Right = after.Left + width;
            after.Bottom = after.Top + height;
            return MoveWindow(after);
        }

        private void HandleSourceInitialized(object sender, EventArgs eventArgs)
        {
            handle = new WindowInteropHelper(window).Handle;
            source = HwndSource.FromHwnd(handle);
            if (source != null)
            {
                source.AddHook(WindowProcedure);
            }
        }

        private void HandleWindowClosed(object sender, EventArgs eventArgs)
        {
            Dispose();
        }

        private IntPtr WindowProcedure(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WindowMoving &&
                enabled &&
                !suppressed &&
                lParam != IntPtr.Zero)
            {
                NativeRect rectangle = (NativeRect)Marshal.PtrToStructure(
                    lParam,
                    typeof(NativeRect));
                if (ApplySnap(ref rectangle, true, true))
                {
                    Marshal.StructureToPtr(rectangle, lParam, false);
                    handled = true;
                    return new IntPtr(1);
                }
            }
            else if (message == WindowExitSizeMove)
            {
                EventHandler handler = MoveCompleted;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }

            return IntPtr.Zero;
        }

        private bool ApplySnap(
            ref NativeRect rectangle,
            bool preferCursorMonitor,
            bool allowCursorTrigger)
        {
            LastHorizontalEdge = null;
            LastVerticalEdge = null;
            PrimaryDockEdge = null;

            NativeRect workArea;
            NativeRect monitorArea;
            NativePoint cursor;
            bool hasCursor;
            if (!TryGetSnapAreas(
                ref rectangle,
                preferCursorMonitor,
                out workArea,
                out monitorArea,
                out cursor,
                out hasCursor))
            {
                return false;
            }

            double scale = GetDpiScale();
            PixelInsets insets = GetPixelInsets(scale);
            int threshold = Math.Max(
                12,
                (int)Math.Round(thresholdDips * scale));

            int visibleLeft = rectangle.Left + insets.Left;
            int visibleRight = rectangle.Right - insets.Right;
            int visibleTop = rectangle.Top + insets.Top;
            int visibleBottom = rectangle.Bottom - insets.Bottom;

            int leftDistance = Math.Abs(visibleLeft - workArea.Left);
            int rightDistance = Math.Abs(visibleRight - workArea.Right);
            int topDistance = Math.Abs(visibleTop - workArea.Top);
            int bottomDistance = Math.Abs(visibleBottom - workArea.Bottom);
            int deltaX = 0;
            int deltaY = 0;
            bool snappedX = false;
            bool snappedY = false;

            int cursorThreshold = Math.Max(
                12,
                (int)Math.Round(24 * scale));
            int cursorLeftDistance = int.MaxValue;
            int cursorRightDistance = int.MaxValue;
            int cursorTopDistance = int.MaxValue;
            int cursorBottomDistance = int.MaxValue;

            if (allowCursorTrigger && hasCursor)
            {
                cursorLeftDistance =
                    Math.Min(
                        Math.Abs(cursor.X - monitorArea.Left),
                        Math.Abs(cursor.X - workArea.Left));
                cursorRightDistance =
                    Math.Min(
                        Math.Abs(cursor.X - monitorArea.Right),
                        Math.Abs(cursor.X - workArea.Right));
                cursorTopDistance =
                    Math.Min(
                        Math.Abs(cursor.Y - monitorArea.Top),
                        Math.Abs(cursor.Y - workArea.Top));
                cursorBottomDistance =
                    Math.Min(
                        Math.Abs(cursor.Y - monitorArea.Bottom),
                        Math.Abs(cursor.Y - workArea.Bottom));
            }

            if (Math.Min(cursorLeftDistance, cursorRightDistance) <=
                cursorThreshold)
            {
                snappedX = true;
                if (cursorLeftDistance <= cursorRightDistance)
                {
                    LastHorizontalEdge = WindowDockEdge.Left;
                    deltaX = workArea.Left - visibleLeft;
                }
                else
                {
                    LastHorizontalEdge = WindowDockEdge.Right;
                    deltaX = workArea.Right - visibleRight;
                }
            }
            else if (Math.Min(leftDistance, rightDistance) <= threshold)
            {
                snappedX = true;
                if (leftDistance <= rightDistance)
                {
                    LastHorizontalEdge = WindowDockEdge.Left;
                    deltaX = workArea.Left - visibleLeft;
                }
                else
                {
                    LastHorizontalEdge = WindowDockEdge.Right;
                    deltaX = workArea.Right - visibleRight;
                }
            }

            if (Math.Min(cursorTopDistance, cursorBottomDistance) <=
                cursorThreshold)
            {
                snappedY = true;
                if (cursorTopDistance <= cursorBottomDistance)
                {
                    LastVerticalEdge = WindowDockEdge.Top;
                    deltaY = workArea.Top - visibleTop;
                }
                else
                {
                    LastVerticalEdge = WindowDockEdge.Bottom;
                    deltaY = workArea.Bottom - visibleBottom;
                }
            }
            else if (Math.Min(topDistance, bottomDistance) <= threshold)
            {
                snappedY = true;
                if (topDistance <= bottomDistance)
                {
                    LastVerticalEdge = WindowDockEdge.Top;
                    deltaY = workArea.Top - visibleTop;
                }
                else
                {
                    LastVerticalEdge = WindowDockEdge.Bottom;
                    deltaY = workArea.Bottom - visibleBottom;
                }
            }

            if (!snappedX && !snappedY)
            {
                return false;
            }

            if (LastHorizontalEdge.HasValue &&
                LastVerticalEdge.HasValue)
            {
                int horizontalScore = Math.Min(
                    Math.Min(cursorLeftDistance, cursorRightDistance),
                    Math.Min(leftDistance, rightDistance));
                int verticalScore = Math.Min(
                    Math.Min(cursorTopDistance, cursorBottomDistance),
                    Math.Min(topDistance, bottomDistance));
                PrimaryDockEdge = horizontalScore <= verticalScore
                    ? LastHorizontalEdge
                    : LastVerticalEdge;
                // Magnetize only the chosen edge. Locking both axes makes corners sticky.
                if (PrimaryDockEdge == LastHorizontalEdge)
                {
                    deltaY = 0;
                    LastVerticalEdge = null;
                }
                else
                {
                    deltaX = 0;
                    LastHorizontalEdge = null;
                }
            }
            else
            {
                PrimaryDockEdge = LastHorizontalEdge ??
                    LastVerticalEdge;
            }

            Offset(ref rectangle, deltaX, deltaY);
            return true;
        }

        private static bool TryGetSnapAreas(
            ref NativeRect rectangle,
            bool preferCursorMonitor,
            out NativeRect workArea,
            out NativeRect monitorArea,
            out NativePoint cursor,
            out bool hasCursor)
        {
            hasCursor = GetCursorPos(out cursor);
            IntPtr monitor = IntPtr.Zero;
            if (preferCursorMonitor && hasCursor)
            {
                monitor = MonitorFromPoint(
                    cursor,
                    MonitorDefaultToNearest);
            }

            if (monitor == IntPtr.Zero)
            {
                monitor = MonitorFromRect(
                    ref rectangle,
                    MonitorDefaultToNearest);
            }

            return TryGetMonitorAreas(
                monitor,
                out workArea,
                out monitorArea);
        }

        private bool TryGetWorkArea(
            ref NativeRect rectangle,
            bool preferCursorMonitor,
            out NativeRect workArea)
        {
            IntPtr monitor = IntPtr.Zero;
            NativePoint cursor;
            if (preferCursorMonitor && GetCursorPos(out cursor))
            {
                monitor = MonitorFromPoint(
                    cursor,
                    MonitorDefaultToNearest);
            }

            if (monitor == IntPtr.Zero)
            {
                monitor = MonitorFromRect(
                    ref rectangle,
                    MonitorDefaultToNearest);
            }
            return TryGetWorkArea(monitor, out workArea);
        }

        private static bool TryGetWorkArea(
            IntPtr monitor,
            out NativeRect workArea)
        {
            NativeRect monitorArea;
            return TryGetMonitorAreas(
                monitor,
                out workArea,
                out monitorArea);
        }

        private static bool TryGetMonitorAreas(
            IntPtr monitor,
            out NativeRect workArea,
            out NativeRect monitorArea)
        {
            MonitorInfo info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                workArea = new NativeRect();
                monitorArea = new NativeRect();
                return false;
            }

            workArea = info.WorkArea;
            monitorArea = info.MonitorArea;
            return true;
        }

        private PixelInsets GetPixelInsets()
        {
            return GetPixelInsets(GetDpiScale());
        }

        private PixelInsets GetPixelInsets(double scale)
        {
            return new PixelInsets
            {
                Left = Math.Max(
                    0,
                    (int)Math.Round(visibleSurfaceMargin.Left * scale)),
                Top = Math.Max(
                    0,
                    (int)Math.Round(visibleSurfaceMargin.Top * scale)),
                Right = Math.Max(
                    0,
                    (int)Math.Round(visibleSurfaceMargin.Right * scale)),
                Bottom = Math.Max(
                    0,
                    (int)Math.Round(visibleSurfaceMargin.Bottom * scale))
            };
        }

        private double GetDpiScale()
        {
            try
            {
                uint dpi = GetDpiForWindow(handle);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }

            if (source != null && source.CompositionTarget != null)
            {
                double scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale > 0)
                {
                    return scale;
                }
            }

            return 1.0;
        }

        private bool MoveWindow(NativeRect rectangle)
        {
            return SetWindowPos(
                handle,
                IntPtr.Zero,
                rectangle.Left,
                rectangle.Top,
                0,
                0,
                SetWindowPosNoSize |
                    SetWindowPosNoZOrder |
                    SetWindowPosNoActivate);
        }

        private static void Offset(
            ref NativeRect rectangle,
            int deltaX,
            int deltaY)
        {
            rectangle.Left += deltaX;
            rectangle.Right += deltaX;
            rectangle.Top += deltaY;
            rectangle.Bottom += deltaY;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            window.SourceInitialized -= HandleSourceInitialized;
            window.Closed -= HandleWindowClosed;
            if (source != null)
            {
                source.RemoveHook(WindowProcedure);
                source = null;
            }
            handle = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect MonitorArea;
            public NativeRect WorkArea;
            public uint Flags;
        }

        private struct PixelInsets
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(
            IntPtr window,
            out NativeRect rectangle);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(
            ref NativeRect rectangle,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr window,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
    }
}
