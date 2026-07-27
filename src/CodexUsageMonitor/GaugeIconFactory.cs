using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexUsageMonitor
{
    internal static class GaugeIconFactory
    {
        public static Icon Create(int? remainingPercent, Color? requestedAccent)
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.Clear(Color.Transparent);

                using (GraphicsPath tile = CreateRoundedRectangle(
                    new RectangleF(1.5f, 1.5f, 29, 29),
                    7))
                using (SolidBrush background =
                    new SolidBrush(Color.FromArgb(8, 17, 34)))
                using (Pen border =
                    new Pen(Color.FromArgb(210, 48, 213, 239), 1.5f))
                {
                    graphics.FillPath(background, tile);
                    graphics.DrawPath(border, tile);
                }

                int remaining = Math.Max(
                    0,
                    Math.Min(100, remainingPercent ?? 62));
                Color accent = remainingPercent.HasValue
                    ? requestedAccent ?? GetAccentColor(remaining)
                    : Color.FromArgb(126, 139, 151);
                float remainingSweep = Math.Max(
                    5,
                    (float)(remaining * 2.7));

                using (Pen used = new Pen(
                    remainingPercent.HasValue
                        ? Color.FromArgb(255, 50, 183)
                        : Color.FromArgb(69, 81, 101),
                    3.5f))
                {
                    used.StartCap = LineCap.Round;
                    used.EndCap = LineCap.Round;
                    graphics.DrawArc(used, 6.5f, 6.5f, 19, 19, 135, 270);
                }

                using (Pen progress = new Pen(accent, 3.5f))
                {
                    progress.StartCap = LineCap.Round;
                    progress.EndCap = LineCap.Round;
                    graphics.DrawArc(
                        progress,
                        6.5f,
                        6.5f,
                        19,
                        19,
                        135,
                        remainingSweep);
                }

                using (Pen prompt = new Pen(
                    Color.FromArgb(235, 246, 251, 255),
                    2.2f))
                using (Pen cursor = new Pen(
                    Color.FromArgb(255, 50, 183),
                    2.2f))
                {
                    prompt.StartCap = LineCap.Round;
                    prompt.EndCap = LineCap.Round;
                    cursor.StartCap = LineCap.Round;
                    cursor.EndCap = LineCap.Round;
                    graphics.DrawLines(
                        prompt,
                        new[]
                        {
                            new PointF(11.5f, 11.5f),
                            new PointF(15.8f, 15.8f),
                            new PointF(11.5f, 20.1f)
                        });
                    graphics.DrawLine(cursor, 17.2f, 19.5f, 21.3f, 19.5f);
                }
            }

            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (Icon temporary = Icon.FromHandle(handle))
                {
                    return (Icon)temporary.Clone();
                }
            }
            finally
            {
                DestroyIcon(handle);
                bitmap.Dispose();
            }
        }

        public static Color GetAccentColor(int remaining)
        {
            if (remaining <= 15)
            {
                return Color.FromArgb(255, 93, 93);
            }

            if (remaining <= 35)
            {
                return Color.FromArgb(255, 190, 92);
            }

            return Color.FromArgb(64, 212, 152);
        }

        private static GraphicsPath CreateRoundedRectangle(
            RectangleF rectangle,
            float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(
                rectangle.Left,
                rectangle.Top,
                diameter,
                diameter,
                180,
                90);
            path.AddArc(
                rectangle.Right - diameter,
                rectangle.Top,
                diameter,
                diameter,
                270,
                90);
            path.AddArc(
                rectangle.Right - diameter,
                rectangle.Bottom - diameter,
                diameter,
                diameter,
                0,
                90);
            path.AddArc(
                rectangle.Left,
                rectangle.Bottom - diameter,
                diameter,
                diameter,
                90,
                90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
