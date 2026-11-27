using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Snapmark;

/// The Snapmark icon (viewfinder corners + red pointer), same design as Icon/make_icon.swift.
static class AppIcon
{
    public static Bitmap Render(int px)
    {
        var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // Design space is 1024 with y pointing down.
        float s = px / 1024f;
        g.ScaleTransform(s, s);

        // Small sizes get thicker strokes so the marks stay readable.
        bool small = px <= 32, medium = px <= 64;
        float bracketW = small ? 84 : (medium ? 56 : 44);
        float shaftW = small ? 92 : (medium ? 66 : 56);

        using (var basePath = Glyphs.RoundedRect(new RectangleF(100, 100, 824, 824), 186))
        {
            using var fill = new SolidBrush(Color.FromArgb(250, 250, 250));
            g.FillPath(fill, basePath);
            using var edge = new Pen(Color.FromArgb(small ? 46 : 26, 0, 0, 0), small ? 8 : 3);
            g.DrawPath(edge, basePath);
        }

        // Corner brackets
        using (var pen = new Pen(Color.FromArgb(29, 29, 31), bracketW) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            float[][] corners =
            {
                new float[] { 250, 390, 250, 290, 250, 250, 290, 250, 390, 250 },
                new float[] { 634, 250, 734, 250, 774, 250, 774, 290, 774, 390 },
                new float[] { 774, 634, 774, 734, 774, 774, 734, 774, 634, 774 },
                new float[] { 390, 774, 290, 774, 250, 774, 250, 734, 250, 634 },
            };
            foreach (var c in corners)
            {
                using var path = new GraphicsPath();
                var p0 = new PointF(c[0], c[1]); var p1 = new PointF(c[2], c[3]);
                var ctl = new PointF(c[4], c[5]); var p3 = new PointF(c[6], c[7]); var p4 = new PointF(c[8], c[9]);
                path.AddLine(p0, p1);
                // Quadratic curve as a cubic.
                var c1 = new PointF(p1.X + 2f / 3 * (ctl.X - p1.X), p1.Y + 2f / 3 * (ctl.Y - p1.Y));
                var c2 = new PointF(p3.X + 2f / 3 * (ctl.X - p3.X), p3.Y + 2f / 3 * (ctl.Y - p3.Y));
                path.AddBezier(p1, c1, c2, p3);
                path.AddLine(p3, p4);
                g.DrawPath(pen, path);
            }
        }

        // Red pointer arrow
        var red = Color.FromArgb(255, 59, 48);
        using (var shaft = new Pen(red, shaftW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(shaft, 680, 680, 459, 459);
        PointF[] head = { new(360, 360), new(519, 399), new(399, 519) };
        using (var b = new SolidBrush(red)) g.FillPolygon(b, head);
        using (var hp = new Pen(red, 18) { LineJoin = LineJoin.Round }) g.DrawPolygon(hp, head);

        return bmp;
    }

    /// Writes a multi-size .ico (PNG-compressed entries).
    public static void WriteIco(string path, int[] sizes)
    {
        var pngs = sizes.Select(sz =>
        {
            using var bmp = Render(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    /// A tray icon at the system's small-icon size for the current DPI.
    public static Icon TrayIcon()
    {
        int size = Math.Max(16, (int)Math.Round(16 * Native.ScaleAt(Point.Empty)));
        using var bmp = Render(size);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
