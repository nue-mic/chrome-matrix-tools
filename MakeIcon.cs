// ===== 应用图标生成器 =====
// 用 Windows 自带的 .NET Framework + GDI(System.Drawing) 程序化绘制图标，
// 输出多尺寸高清 app.ico（层叠浏览器窗口 · 蓝紫渐变），零外部素材依赖。
// 由 build.bat 自动编译运行；想换图标删掉 app.ico 重新编译即可。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class MakeIcon
{
    private static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var imgs = new List<KeyValuePair<int, byte[]>>();
        foreach (int s in sizes)
        {
            using (Bitmap bmp = Draw(s))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png); // 每个尺寸单独绘制并以 PNG 压缩存入 ICO
                imgs.Add(new KeyValuePair<int, byte[]>(s, ms.ToArray()));
            }
        }
        File.WriteAllBytes(outPath, BuildIco(imgs));
        Console.WriteLine("已生成图标: " + outPath + " (" + sizes.Length + " 种尺寸)");
    }

    // 绘制单个尺寸的位图：蓝紫渐变圆角底 + 三层错位的浏览器窗口卡片
    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float f = size;

            // 1) 背景：圆角矩形 + 蓝紫对角渐变 + 顶部高光
            float m = f * 0.04f;
            var bg = new RectangleF(m, m, f - 2 * m, f - 2 * m);
            float br = f * 0.22f;
            using (GraphicsPath bgPath = Round(bg, br))
            using (var grad = new LinearGradientBrush(
                bg, Color.FromArgb(255, 88, 123, 255), Color.FromArgb(255, 139, 61, 240), 45f))
            {
                g.FillPath(grad, bgPath);
            }
            using (GraphicsPath clip = Round(bg, br))
            {
                Region old = g.Clip;
                g.SetClip(clip);
                using (var gloss = new LinearGradientBrush(
                    new RectangleF(bg.X, bg.Y, bg.Width, bg.Height * 0.55f),
                    Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    g.FillRectangle(gloss, bg.X, bg.Y, bg.Width, bg.Height * 0.55f);
                }
                g.Clip = old;
            }

            // 2) 三层浏览器窗口卡片（从后到前，越靠前越实、越完整）
            bool tiny = size < 32; // 小尺寸只画两层，避免糊成一团
            float cw = f * 0.50f, ch = f * 0.42f;
            if (!tiny)
                Card(g, f * 0.34f, f * 0.15f, cw, ch, Color.FromArgb(150, 255, 255, 255), false, size);
            Card(g, f * 0.27f, f * 0.27f, cw, ch, Color.FromArgb(205, 255, 255, 255), false, size);
            Card(g, f * 0.17f, f * 0.40f, cw, ch, Color.FromArgb(255, 255, 255, 255), true, size);
        }
        return bmp;
    }

    // 绘制单个浏览器窗口卡片：阴影 + 卡片体 + 标题栏 + 红黄绿圆点 + (前卡)内容线
    private static void Card(Graphics g, float x, float y, float w, float h, Color body, bool detailed, int size)
    {
        float r = h * 0.16f;
        var rect = new RectangleF(x, y, w, h);

        // 阴影
        using (GraphicsPath sp = Round(new RectangleF(x, y + h * 0.05f, w, h), r))
        using (var sb = new SolidBrush(Color.FromArgb(70, 20, 24, 60)))
            g.FillPath(sb, sp);

        // 卡片体
        using (GraphicsPath cp = Round(rect, r))
        using (var cb = new SolidBrush(body))
            g.FillPath(cb, cp);

        // 标题栏（裁剪到圆角内，保证上方圆角）
        float th = h * 0.26f;
        using (GraphicsPath cp = Round(rect, r))
        {
            Region old = g.Clip;
            g.SetClip(cp);
            using (var tb = new SolidBrush(Color.FromArgb(body.A, 238, 240, 248)))
                g.FillRectangle(tb, x, y, w, th);
            g.Clip = old;
        }

        // 红黄绿三个圆点
        float dotR = Math.Max(1f, h * 0.055f);
        float cy = y + th * 0.5f;
        float startX = x + w * 0.10f;
        float gap = Math.Max(dotR * 2.6f, w * 0.085f);
        Color[] dots =
        {
            Color.FromArgb(body.A, 255, 95, 87),
            Color.FromArgb(body.A, 254, 188, 46),
            Color.FromArgb(body.A, 40, 200, 64),
        };
        for (int i = 0; i < 3; i++)
            using (var db = new SolidBrush(dots[i]))
                g.FillEllipse(db, startX + i * gap - dotR, cy - dotR, dotR * 2, dotR * 2);

        // 内容线（仅最前面的卡片、且尺寸够大时）
        if (detailed && size >= 48)
        {
            float lh = h * 0.07f;
            float lx = x + w * 0.10f;
            float ly = y + th + h * 0.14f;
            float[] lw = { w * 0.70f, w * 0.55f, w * 0.62f };
            using (var lb = new SolidBrush(Color.FromArgb(255, 205, 212, 230)))
                for (int i = 0; i < lw.Length; i++)
                    using (GraphicsPath lp = Round(new RectangleF(lx, ly + i * (lh + h * 0.10f), lw[i], lh), lh * 0.5f))
                        g.FillPath(lb, lp);
        }
    }

    private static GraphicsPath Round(RectangleF r, float rad)
    {
        rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
        var p = new GraphicsPath();
        if (rad <= 0)
        {
            p.AddRectangle(r);
            p.CloseFigure();
            return p;
        }
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // 把多张 PNG 拼装成一个多尺寸 .ico 文件
    private static byte[] BuildIco(List<KeyValuePair<int, byte[]>> imgs)
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write((short)0);            // reserved
            bw.Write((short)1);            // type = icon
            bw.Write((short)imgs.Count);   // 图像数量
            int offset = 6 + imgs.Count * 16;
            foreach (var kv in imgs)
            {
                int s = kv.Key;
                byte[] d = kv.Value;
                bw.Write((byte)(s >= 256 ? 0 : s)); // width，0 表示 256
                bw.Write((byte)(s >= 256 ? 0 : s)); // height
                bw.Write((byte)0);  // 调色板
                bw.Write((byte)0);  // reserved
                bw.Write((short)1); // 颜色平面
                bw.Write((short)32); // 位深
                bw.Write(d.Length);  // 数据字节数
                bw.Write(offset);    // 数据偏移
                offset += d.Length;
            }
            foreach (var kv in imgs) bw.Write(kv.Value);
            bw.Flush();
            return ms.ToArray();
        }
    }
}
