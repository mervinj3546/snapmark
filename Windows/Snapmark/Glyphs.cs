using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Snapmark;

/// Small line icons for the toolbars, drawn on a 16×16 grid.
static class Glyphs
{
    public static void Draw(Graphics g, string name, RectangleF box, Color color, float scale)
    {
        float s = scale;
        float ox = box.X + box.Width / 2 - 8 * s, oy = box.Y + box.Height / 2 - 8 * s;
        PointF P(float x, float y) => new(ox + x * s, oy + y * s);

        var oldSmooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        switch (name)
        {
            case "arrow":
                g.DrawLine(pen, P(3.5f, 12.5f), P(12.5f, 3.5f));
                g.DrawLines(pen, new[] { P(6f, 3.5f), P(12.5f, 3.5f), P(12.5f, 10f) });
                break;
            case "line":
                g.DrawLine(pen, P(3f, 13f), P(13f, 3f));
                break;
            case "rect":
                g.DrawRectangle(pen, ox + 1.5f * s, oy + 3.5f * s, 13 * s, 9 * s);
                break;
            case "oval":
                g.DrawEllipse(pen, ox + 2f * s, oy + 2f * s, 12 * s, 12 * s);
                break;
            case "weight":
                using (var p1 = new Pen(color, 1f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p1, P(2.5f, 3.5f), P(13.5f, 3.5f));
                using (var p2 = new Pen(color, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p2, P(2.5f, 8f), P(13.5f, 8f));
                using (var p3 = new Pen(color, 3.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p3, P(2.5f, 12.8f), P(13.5f, 12.8f));
                break;
            case "undo":
            {
                using var path = new GraphicsPath();
                path.AddLine(P(3f, 6f), P(10f, 6f));
                path.AddArc(ox + 6.5f * s, oy + 6f * s, 7 * s, 7 * s, 270, 180);
                path.AddLine(P(10f, 13f), P(6f, 13f));
                g.DrawPath(pen, path);
                g.DrawLines(pen, new[] { P(6f, 3f), P(3f, 6f), P(6f, 9f) });
                break;
            }
            case "copy":
                g.DrawLines(pen, new[] { P(5f, 4.5f), P(5f, 1.5f), P(14.5f, 1.5f), P(14.5f, 11.5f), P(11.5f, 11.5f) });
                g.DrawRectangle(pen, ox + 1.5f * s, oy + 4.5f * s, 10 * s, 10 * s);
                break;
            case "save":
                g.DrawLines(pen, new[] { P(2f, 9f), P(2f, 14f), P(14f, 14f), P(14f, 9f) });
                g.DrawLine(pen, P(8f, 1.5f), P(8f, 10.5f));
                g.DrawLines(pen, new[] { P(4.8f, 7.3f), P(8f, 10.5f), P(11.2f, 7.3f) });
                break;
            case "close":
                g.DrawLine(pen, P(4f, 4f), P(12f, 12f));
                g.DrawLine(pen, P(12f, 4f), P(4f, 12f));
                break;
        }
        g.SmoothingMode = oldSmooth;
    }

    public static Bitmap Bitmap(string name, int size, Color color)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Draw(g, name, new RectangleF(0, 0, size, size), color, size / 16f);
        return bmp;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
