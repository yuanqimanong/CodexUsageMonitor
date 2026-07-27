using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexUsageMonitor
{
    internal static class WindowBackdrop
    {
        public static void Apply(Window window, bool enableGlass)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            // AllowsTransparency=true creates a layered HWND. Applying Acrylic to
            // that HWND paints the full rectangular bounds and produces a black
            // frame around transparent WPF content. Use the theme's alpha fallback
            // instead; it preserves real see-through pixels and rounded corners.
            long extendedStyle = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
            bool isLayered = (extendedStyle & LayeredWindowStyle) != 0;
            if (enableGlass && isLayered)
            {
                enableGlass = false;
            }

            AccentPolicy accent = new AccentPolicy
            {
                AccentState = enableGlass
                    ? AccentState.EnableAcrylicBlurBehind
                    : AccentState.Disabled,
                AccentFlags = enableGlass ? 2 : 0,
                GradientColor = enableGlass
                    ? BuildAbgr(82, 24, 32, 40)
                    : 0,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);
                WindowCompositionAttributeData data =
                    new WindowCompositionAttributeData
                    {
                        Attribute = WindowCompositionAttribute.AccentPolicy,
                        Data = pointer,
                        SizeOfData = size
                    };
                SetWindowCompositionAttribute(handle, ref data);
            }
            catch (Exception)
            {
                // The semi-transparent theme remains a safe visual fallback.
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static int BuildAbgr(byte alpha, byte red, byte green, byte blue)
        {
            return (alpha << 24) | (blue << 16) | (green << 8) | red;
        }

        private enum AccentState
        {
            Disabled = 0,
            EnableGradient = 1,
            EnableTransparentGradient = 2,
            EnableBlurBehind = 3,
            EnableAcrylicBlurBehind = 4
        }

        private enum WindowCompositionAttribute
        {
            AccentPolicy = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(
            IntPtr window,
            ref WindowCompositionAttributeData data);

        private const int ExtendedStyleIndex = -20;
        private const long LayeredWindowStyle = 0x00080000L;

        private static IntPtr GetWindowLongPtr(IntPtr window, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(window, index)
                : new IntPtr(GetWindowLong32(window, index));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    }
}
