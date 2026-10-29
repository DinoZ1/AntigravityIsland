using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using AntigravityIsland.Native;

namespace AntigravityIsland.Services
{
    public static class ScreenCapture
    {
        public static string LatestScreenshotPath { get; private set; }

        public static string Capture(bool activeWindowOnly)
        {
            NativeWin32.InitDpiAwareness();

            Rectangle bounds;
            if (activeWindowOnly)
            {
                IntPtr fg = NativeWin32.GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    NativeWin32.RECT rect = NativeWin32.GetExtendedFrameBounds(fg);
                    if (rect.Width > 50 && rect.Height > 50)
                    {
                        bounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                    }
                    else
                    {
                        bounds = GetPrimaryScreenBounds();
                    }
                }
                else
                {
                    bounds = GetPrimaryScreenBounds();
                }
            }
            else
            {
                bounds = GetPrimaryScreenBounds();
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = new Rectangle(0, 0, 1920, 1080);
            }

            string tempDir = Path.GetTempPath();
            string fileName = string.Format("antigravity-screen-{0:yyyyMMdd-HHmmss}.png", DateTime.Now);
            string fullPath = Path.Combine(tempDir, fileName);

            using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                // Salva como PNG otimizado
                bmp.Save(fullPath, ImageFormat.Png);
            }

            LatestScreenshotPath = fullPath;
            return fullPath;
        }

        private static Rectangle GetPrimaryScreenBounds()
        {
            int w = (int)SystemParameters.PrimaryScreenWidth;
            int h = (int)SystemParameters.PrimaryScreenHeight;
            return new Rectangle(0, 0, w, h);
        }

        public static string GetLatestAsBase64()
        {
            if (string.IsNullOrEmpty(LatestScreenshotPath) || !File.Exists(LatestScreenshotPath))
                return null;

            byte[] bytes = File.ReadAllBytes(LatestScreenshotPath);
            return Convert.ToBase64String(bytes);
        }
    }
}
