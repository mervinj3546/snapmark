using System.Drawing.Drawing2D;

namespace Snapmark;

enum Tool { Arrow, Line, Rect, Oval }

sealed class Shape
{
    public Tool Tool;
    public PointF A, B;
    public Color Color;
    /// Stroke width in pixels.
    public float Width;
    /// Display scale when drawn (keeps the minimum arrowhead size consistent across DPIs).
    public float Scale;

    public Shape(Tool tool, PointF a, Color color, float width, float scale)
    {
        Tool = tool; A = a; B = a; Color = color; Width = Math.Max(1, width); Scale = scale;
    }

    public float Length => MathF.Sqrt((B.X - A.X) * (B.X - A.X) + (B.Y - A.Y) * (B.Y - A.Y));

    /// Shift-drag: squares and circles for rectangles and ovals, 45° steps for lines and arrows.
    public PointF Snapped(PointF p)
    {
        if (Tool is Tool.Rect or Tool.Oval)
        {
            float d = Math.Max(Math.Abs(p.X - A.X), Math.Abs(p.Y - A.Y));
            return new PointF(A.X + (p.X >= A.X ? d : -d), A.Y + (p.Y >= A.Y ? d : -d));
        }
        float dx = p.X - A.X, dy = p.Y - A.Y;
        float step = MathF.PI / 4;
        float ang = MathF.Round(MathF.Atan2(dy, dx) / step) * step;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        return new PointF(A.X + MathF.Cos(ang) * len, A.Y + MathF.Sin(ang) * len);
    }

    float HeadLength(float len) => Math.Min(Math.Max(14 * Scale, Width * 4), len * 0.6f);

    /// Area the shape can paint into, for partial repaints.
    public Rectangle Bounds()
    {
        float pad = Math.Max(Width, HeadLength(Length) * 0.6f) + 4;
        float x0 = Math.Min(A.X, B.X) - pad, y0 = Math.Min(A.Y, B.Y) - pad;
        float x1 = Math.Max(A.X, B.X) + pad, y1 = Math.Max(A.Y, B.Y) + pad;
        return Rectangle.FromLTRB((int)MathF.Floor(x0), (int)MathF.Floor(y0), (int)MathF.Ceiling(x1), (int)MathF.Ceiling(y1));
    }

    public void Draw(Graphics g)
    {
        using var pen = new Pen(Color, Width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        var r = RectangleF.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y));
        switch (Tool)
        {
            case Tool.Rect:
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                break;
            case Tool.Oval:
                g.DrawEllipse(pen, r);
                break;
            case Tool.Line:
                g.DrawLine(pen, A, B);
                break;
            case Tool.Arrow:
            {
                float dx = B.X - A.X, dy = B.Y - A.Y;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len <= 0.5f) return;
                float ux = dx / len, uy = dy / len;
                // Fixed-size head: depends only on line width, capped so short arrows stay sane.
                float headLen = HeadLength(len);
                float half = headLen * 0.5f;
                var basePt = new PointF(B.X - ux * headLen, B.Y - uy * headLen);
                g.DrawLine(pen, A, new PointF(basePt.X + ux, basePt.Y + uy));
                PointF[] head =
                {
                    B,
                    new(basePt.X - uy * half, basePt.Y + ux * half),
                    new(basePt.X + uy * half, basePt.Y - ux * half),
                };
                using var brush = new SolidBrush(Color);
                using var thin = new Pen(Color, 1) { LineJoin = LineJoin.Round };
                g.FillPolygon(brush, head);
                g.DrawPolygon(thin, head);
                break;
            }
        }
    }
}
