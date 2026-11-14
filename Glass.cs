using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Nook
{
    /// <summary>
    /// 抓取抽屉背后的桌面像素并做真实模糊。
    /// 不依赖系统 acrylic——自己算模糊半径和去饱和，玻璃质感完全可控。
    /// </summary>
    internal static class Glass
    {
        public static BitmapSource? Capture(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return null;

            try
            {
                using var full = new Bitmap(w, h, DPixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(full))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
                }

                // 缩小 → 模糊 → 放大，等价于一次大半径高斯，但快几十倍
                int sw = Math.Max(1, w / 6);
                int sh = Math.Max(1, h / 6);

                using var small = new Bitmap(sw, sh, DPixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, new Rectangle(0, 0, sw, sh));
                }

                // 全黑说明这次没抓到真实画面（窗口排除截屏时可能出现），交给上层保留旧图
                long sum = 0;
                int samples = 0;

                for (int sy = 0; sy < sh; sy += 3)
                    for (int sx = 0; sx < sw; sx += 3)
                    {
                        var c = small.GetPixel(sx, sy);
                        sum += c.R + c.G + c.B;
                        samples++;
                    }

                if (samples > 0 && sum / (3.0 * samples) < 8) return null;

                Blur(small, 7, 3);
                Desaturate(small, 0.18f);

                using var back = new Bitmap(w, h, DPixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(back))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(0, 0, w, h));
                }

                return ToSource(back);
            }
            catch
            {
                return null;
            }
        }

        // 三遍盒式模糊 ≈ 高斯
        private static unsafe void Blur(Bitmap bmp, int radius, int passes)
        {
            if (radius < 1) return;

            int w = bmp.Width, h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, DPixelFormat.Format32bppArgb);
            try
            {
                int count = w * h;
                byte* scan0 = (byte*)data.Scan0;
                int stride = data.Stride;

                byte[] r = new byte[count];
                byte[] g = new byte[count];
                byte[] b = new byte[count];
                byte[] a = new byte[count];

                for (int y = 0; y < h; y++)
                {
                    byte* row = scan0 + y * stride;
                    int o = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        byte* px = row + x * 4;
                        b[o + x] = px[0];
                        g[o + x] = px[1];
                        r[o + x] = px[2];
                        a[o + x] = px[3];
                    }
                }

                byte[] tmp = new byte[count];
                for (int p = 0; p < passes; p++)
                {
                    BoxH(r, tmp, w, h, radius);
                    BoxH(g, tmp, w, h, radius);
                    BoxH(b, tmp, w, h, radius);
                    BoxH(a, tmp, w, h, radius);

                    BoxV(r, tmp, w, h, radius);
                    BoxV(g, tmp, w, h, radius);
                    BoxV(b, tmp, w, h, radius);
                    BoxV(a, tmp, w, h, radius);
                }

                for (int y = 0; y < h; y++)
                {
                    byte* row = scan0 + y * stride;
                    int o = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        byte* px = row + x * 4;
                        px[0] = b[o + x];
                        px[1] = g[o + x];
                        px[2] = r[o + x];
                        px[3] = a[o + x];
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static void BoxH(byte[] src, byte[] dst, int w, int h, int radius)
        {
            int diameter = radius * 2 + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += src[row + Clamp(i, 0, w - 1)];

                for (int x = 0; x < w; x++)
                {
                    dst[row + x] = (byte)(sum / diameter);
                    sum -= src[row + Clamp(x - radius, 0, w - 1)];
                    sum += src[row + Clamp(x + radius + 1, 0, w - 1)];
                }
            }
        }

        private static void BoxV(byte[] src, byte[] dst, int w, int h, int radius)
        {
            int diameter = radius * 2 + 1;
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += src[Clamp(i, 0, h - 1) * w + x];

                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = (byte)(sum / diameter);
                    sum -= src[Clamp(y - radius, 0, h - 1) * w + x];
                    sum += src[Clamp(y + radius + 1, 0, h - 1) * w + x];
                }
            }
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // 玻璃背后的颜色要退一步，别跟前景抢戏
        private static unsafe void Desaturate(Bitmap bmp, float amount)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, DPixelFormat.Format32bppArgb);
            try
            {
                byte* scan0 = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int y = 0; y < bmp.Height; y++)
                {
                    byte* row = scan0 + y * stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        byte* px = row + x * 4;
                        float lum = px[0] * 0.114f + px[1] * 0.587f + px[2] * 0.299f;
                        px[0] = (byte)(px[0] + (lum - px[0]) * amount);
                        px[1] = (byte)(px[1] + (lum - px[1]) * amount);
                        px[2] = (byte)(px[2] + (lum - px[2]) * amount);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static BitmapSource ToSource(Bitmap bmp)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, DPixelFormat.Format32bppArgb);
            try
            {
                int len = data.Stride * bmp.Height;
                byte[] buffer = new byte[len];
                Marshal.Copy(data.Scan0, buffer, 0, len);

                BitmapSource src = BitmapSource.Create(
                    bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null, buffer, data.Stride);
                src.Freeze();
                return src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }
}
