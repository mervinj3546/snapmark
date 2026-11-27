using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Media;

namespace Snapmark;

/// Full-screen capture overlay: freezes and dims every monitor, select an area, annotate in place, copy.
sealed class OverlayForm : Form
{
    static readonly (string Name, Color Color)[] Swatches =
    {
        ("Red", Color.FromArgb(255, 26, 26)), ("Orange", Color.FromArgb(255, 149, 0)),
        ("Yellow", Color.FromArgb(255, 204, 0)), ("Green", Color.FromArgb(52, 199, 89)),
        ("Blue", Color.FromArgb(0, 122, 255)), ("Purple", Color.FromArgb(175, 82, 222)),
        ("Black", Color.Black), ("White", Color.White),
    };

    enum Kind { Tool, Color, WidthIcon, WidthField, Undo, Copy, Save, Close }

    sealed class Item
    {
        public Kind Kind; public Tool Tool; public string Tip = ""; public string Glyph = "";
        public Rectangle Bounds;
    }

    enum DragMode { None, Select, Draw, Resize }

    readonly Bitmap shot, dimmed;
    readonly Rectangle virt;
    readonly BufferedGraphicsContext bufCtx = new();
    readonly System.Windows.Forms.Timer tipTimer = new() { Interval = 600 };
    static readonly Graphics Measure = Graphics.FromImage(new Bitmap(1, 1));

    // Capture state
    Rectangle? sel;
    readonly List<Shape> shapes = new();
    Shape? current;
    Tool tool = Tool.Arrow;
    Color color = Swatches[0].Color;
    float lineWidth = 2;
    float ui = 1;

    DragMode drag;
    Point dragStart;
    bool rl, rr, rt, rb;
    Rectangle resizeStart;
    Rectangle lastShapeBounds;

    // Panels
    readonly List<Item> toolItems = new(), actionItems = new();
    bool panels;
    Rectangle toolPanel, actionBar, monitor;
    bool colorMenu;
    Rectangle colorMenuRect;
    int colorHover = -1;
    bool editingWidth, widthReplace;
    string widthText = "";
    Item? hover;
    bool tipVisible;
    Rectangle tipRect;
    Point mouse;

    Rectangle lastVisual;
    Font? font;
    float fontScale;

    public OverlayForm(Bitmap raw, Rectangle virtualScreen)
    {
        virt = virtualScreen;
        int w = raw.Width, h = raw.Height;
        shot = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(shot))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(raw, new Rectangle(0, 0, w, h), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
        }
        dimmed = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(dimmed))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(shot, new Rectangle(0, 0, w, h), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            using var dim = new SolidBrush(Color.FromArgb(115, 0, 0, 0)); // 45% black
            g.FillRectangle(dim, 0, 0, w, h);
        }

        Text = "Snapmark";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        Bounds = virt;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        bufCtx.MaximumBuffer = new Size(w + 1, h + 1);

        foreach (var (t, glyph, tip) in new[] { (Tool.Arrow, "arrow", "Arrow (A)"), (Tool.Line, "line", "Line (L)"),
                                                (Tool.Rect, "rect", "Rectangle (R)"), (Tool.Oval, "oval", "Oval (O)") })
            toolItems.Add(new Item { Kind = Kind.Tool, Tool = t, Glyph = glyph, Tip = tip });
        toolItems.Add(new Item { Kind = Kind.Color, Tip = "Color" });
        toolItems.Add(new Item { Kind = Kind.WidthIcon, Glyph = "weight", Tip = "Line thickness in px (W)" });
        toolItems.Add(new Item { Kind = Kind.WidthField, Tip = "Line thickness in px (1–50). Type a value, then press Enter. Scroll to adjust." });
        toolItems.Add(new Item { Kind = Kind.Undo, Glyph = "undo", Tip = "Undo (Ctrl+Z)" });
        actionItems.Add(new Item { Kind = Kind.Copy, Glyph = "copy", Tip = "Copy and close (Ctrl+C or Enter)" });
        actionItems.Add(new Item { Kind = Kind.Save, Glyph = "save", Tip = "Save to Desktop (Ctrl+S)" });
        actionItems.Add(new Item { Kind = Kind.Close, Glyph = "close", Tip = "Close (Esc)" });

        tipTimer.Tick += (_, _) =>
        {
            tipTimer.Stop();
            if (hover == null || drag != DragMode.None) return;
            tipVisible = true;
            tipRect = TipRect(hover.Tip);
            Changed();
        };
    }

    // MARK: Window

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DisableTransitions(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = virt;
        Activate();
        Native.SetForegroundWindow(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        // The overlay spans monitors at 1:1 physical pixels; never let Windows rescale it.
        if (m.Msg == Native.WM_DPICHANGED) return;
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        tipTimer.Dispose();
        bufCtx.Dispose();
        shot.Dispose();
        dimmed.Dispose();
        font?.Dispose();
    }

    // MARK: Geometry helpers

    int P(float v) => (int)MathF.Round(v * ui);

    Rectangle MonitorAt(Point client)
    {
        var b = Screen.FromPoint(new Point(client.X + virt.X, client.Y + virt.Y)).Bounds;
        b.Offset(-virt.X, -virt.Y);
        return b;
    }

    float ScaleAt(Point client) => Native.ScaleAt(new Point(client.X + virt.X, client.Y + virt.Y));

    Point Clamp(Point p) => new(Math.Clamp(p.X, 0, ClientSize.Width), Math.Clamp(p.Y, 0, ClientSize.Height));

    Font UiFont()
    {
        if (font == null || fontScale != ui)
        {
            font?.Dispose();
            font = new Font("Segoe UI Semibold", 12 * ui, GraphicsUnit.Pixel);
            fontScale = ui;
        }
        return font;
    }

    static Rectangle Union(Rectangle a, Rectangle b) => a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);

    /// 8 handles with the edges each one moves (left, right, top, bottom).
    static List<(Point P, bool L, bool R, bool T, bool B)> Handles(Rectangle s)
    {
        var xs = new[] { (s.Left, true, false), (s.Left + s.Width / 2, false, false), (s.Right, false, true) };
        var ys = new[] { (s.Top, true, false), (s.Top + s.Height / 2, false, false), (s.Bottom, false, true) };
        var list = new List<(Point, bool, bool, bool, bool)>();
        foreach (var x in xs)
            foreach (var y in ys)
                if (x.Item2 || x.Item3 || y.Item2 || y.Item3)
                    list.Add((new Point(x.Item1, y.Item1), x.Item2, x.Item3, y.Item2, y.Item3));
        return list;
    }

    string SizeText(Rectangle s) => $"{s.Width}×{s.Height}";

    Rectangle LabelRect(Rectangle s)
    {
        var ts = Measure.MeasureString(SizeText(s), UiFont(), PointF.Empty, StringFormat.GenericTypographic);
        int w = (int)MathF.Ceiling(ts.Width) + P(10), h = (int)MathF.Ceiling(ts.Height) + P(5);
        var r = new Rectangle(s.Left, s.Top - h - P(6), w, h);
        if (r.Top < MonitorAt(s.Location).Top + 2) r.Location = new Point(s.Left + P(4), s.Top + P(4));
        return r;
    }

    Rectangle TipRect(string text)
    {
        var ts = Measure.MeasureString(text, UiFont(), PointF.Empty, StringFormat.GenericTypographic);
        int w = (int)MathF.Ceiling(ts.Width) + P(14), h = (int)MathF.Ceiling(ts.Height) + P(8);
        var mon = MonitorAt(mouse);
        int x = Math.Min(mouse.X + P(12), mon.Right - w - 2);
        int y = mouse.Y + P(22);
        if (y + h > mon.Bottom - 2) y = mouse.Y - h - P(10);
        return new Rectangle(Math.Max(mon.Left + 2, x), y, w, h);
    }

    /// Everything currently drawn on top of the dimmed screenshot.
    Rectangle VisualBounds()
    {
        if (sel is not Rectangle s) return Rectangle.Empty;
        var r = Rectangle.Inflate(s, P(6) + 2, P(6) + 2);
        r = Union(r, Rectangle.Inflate(LabelRect(s), 2, 2));
        if (panels) { r = Union(r, toolPanel); r = Union(r, actionBar); }
        if (colorMenu) r = Union(r, colorMenuRect);
        if (tipVisible) r = Union(r, tipRect);
        return Rectangle.Inflate(r, 2, 2);
    }

    void Changed()
    {
        var nb = VisualBounds();
        var dirty = Union(lastVisual, nb);
        if (!dirty.IsEmpty) Invalidate(dirty);
        lastVisual = nb;
    }

    // MARK: Panels

    void LayoutPanels()
    {
        colorMenu = false;
        if (sel is not Rectangle s) { panels = false; return; }
        var center = new Point(s.Left + s.Width / 2, s.Top + s.Height / 2);
        ui = ScaleAt(center);
        monitor = MonitorAt(center);
        var mon = monitor;
        int pad = P(4), sp = P(2), iw = P(30), ih = P(28), fh = P(22), gap = P(8), m = P(4);

        // Tools: right of the selection, top-aligned. Left side or inside when no room.
        int y0 = pad;
        foreach (var it in toolItems)
        {
            int h = it.Kind == Kind.WidthField ? fh : ih;
            it.Bounds = new Rectangle(pad, y0, iw, h);
            y0 += h + sp;
        }
        int tw = iw + 2 * pad, th = y0 - sp + pad;
        int x = s.Right + gap;
        if (x + tw > mon.Right - m) x = s.Left - gap - tw;
        if (x < mon.Left + m) x = s.Right - gap - tw;
        int y = Math.Min(Math.Max(mon.Top + m, s.Top), mon.Bottom - th - m);
        toolPanel = new Rectangle(x, y, tw, th);
        foreach (var it in toolItems) it.Bounds.Offset(x, y);

        // Actions: below the selection, right-aligned. Above or inside when no room.
        int aw = actionItems.Count * iw + (actionItems.Count - 1) * sp + 2 * pad, ah = ih + 2 * pad;
        int ay = s.Bottom + gap;
        if (ay + ah > mon.Bottom - m) ay = s.Top - gap - ah;
        if (ay < mon.Top + m) ay = s.Bottom - gap - ah;
        int ax = Math.Min(Math.Max(mon.Left + m, s.Right - aw), mon.Right - aw - m);
        actionBar = new Rectangle(ax, ay, aw, ah);
        for (int i = 0; i < actionItems.Count; i++)
            actionItems[i].Bounds = new Rectangle(ax + pad + i * (iw + sp), ay + pad, iw, ih);

        panels = true;
    }

    Item? ItemAt(Point p)
    {
        if (!panels) return null;
        return toolItems.Concat(actionItems).FirstOrDefault(i => i.Bounds.Contains(p));
    }

    Item Find(Kind k) => toolItems.First(i => i.Kind == k);

    void OpenColorMenu()
    {
        var anchor = Find(Kind.Color).Bounds;
        int pad = P(4), rowH = P(26), w = P(120), h = Swatches.Length * rowH + 2 * pad;
        int x = toolPanel.Right + P(4);
        if (x + w > monitor.Right - 2) x = toolPanel.Left - P(4) - w;
        int y = Math.Min(anchor.Top, monitor.Bottom - h - 2);
        colorMenuRect = new Rectangle(x, y, w, h);
        colorHover = -1;
        colorMenu = true;
    }

    Rectangle ColorRow(int i)
    {
        int pad = P(4), rowH = P(26);
        return new Rectangle(colorMenuRect.X + pad, colorMenuRect.Y + pad + i * rowH, colorMenuRect.Width - 2 * pad, rowH);
    }

    int ColorIndexAt(Point p)
    {
        if (!colorMenu) return -1;
        for (int i = 0; i < Swatches.Length; i++) if (ColorRow(i).Contains(p)) return i;
        return -1;
    }

    static string Fmt(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    void BeginWidthEdit()
    {
        if (sel == null) return;
        if (!panels) LayoutPanels();
        editingWidth = true;
        widthReplace = true;
        widthText = Fmt(lineWidth);
        Changed();
    }

    void CommitWidth()
    {
        if (!editingWidth) return;
        editingWidth = false;
        if (float.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 1)
            lineWidth = MathF.Round(Math.Min(v, 50), 1);
        Changed();
    }

    void CancelWidth() { editingWidth = false; Changed(); }

    void HideTip()
    {
        tipTimer.Stop();
        tipVisible = false;
    }

    // MARK: Drawing

    protected override void OnPaint(PaintEventArgs e)
    {
        var clip = Rectangle.Intersect(e.ClipRectangle, ClientRectangle);
        if (clip.IsEmpty) return;
        using var buf = bufCtx.Allocate(e.Graphics, clip);
        var g = buf.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);

        if (sel is Rectangle s)
        {
            var inner = Rectangle.Intersect(s, clip);
            if (!inner.IsEmpty) g.DrawImage(shot, inner, inner, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            if (!inner.IsEmpty)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.SetClip(inner);
                foreach (var sh in shapes) sh.Draw(g);
                current?.Draw(g);
                g.SetClip(clip);
            }
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Dashed border
            using (var white = new Pen(Color.White, 1))
                g.DrawRectangle(white, s.X - 1, s.Y - 1, s.Width + 1, s.Height + 1);
            using (var dash = new Pen(Color.Black, 1) { DashPattern = new[] { 4f, 4f } })
                g.DrawRectangle(dash, s.X - 1, s.Y - 1, s.Width + 1, s.Height + 1);

            // Resize handles
            int hs = Math.Max(7, P(7)) | 1;
            using (var hb = new Pen(Color.FromArgb(153, 0, 0, 0), 1))
            {
                foreach (var h in Handles(s))
                {
                    var r = new Rectangle(h.P.X - hs / 2, h.P.Y - hs / 2, hs, hs);
                    g.FillRectangle(Brushes.White, r);
                    g.DrawRectangle(hb, r);
                }
            }

            // Pixel size label
            var lr = LabelRect(s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Color.FromArgb(191, 0, 0, 0)))
            using (var path = Glyphs.RoundedRect(lr, P(4)))
                g.FillPath(bg, path);
            g.DrawString(SizeText(s), UiFont(), Brushes.White, lr.X + P(5), lr.Y + P(2.5f), StringFormat.GenericTypographic);

            if (panels) DrawPanels(g);
            if (colorMenu) DrawColorMenu(g);
            if (tipVisible) DrawTip(g);
        }
        buf.Render(e.Graphics);
    }

    void DrawPanelBackground(Graphics g, Rectangle r)
    {
        using var path = Glyphs.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), P(8));
        using var fill = new SolidBrush(Color.FromArgb(240, 34, 34, 37));
        using var edge = new Pen(Color.FromArgb(50, 255, 255, 255), 1);
        g.FillPath(fill, path);
        g.DrawPath(edge, path);
    }

    void DrawPanels(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawPanelBackground(g, toolPanel);
        DrawPanelBackground(g, actionBar);
        var blue = Color.FromArgb(10, 132, 255);

        foreach (var it in toolItems.Concat(actionItems))
        {
            var b = it.Bounds;
            bool active = it.Kind == Kind.Tool && it.Tool == tool;
            if (it.Kind != Kind.WidthField && (active || hover == it))
            {
                using var hl = new SolidBrush(Color.FromArgb(active ? 56 : 30, 255, 255, 255));
                using var path = Glyphs.RoundedRect(b, P(5));
                g.FillPath(hl, path);
            }
            switch (it.Kind)
            {
                case Kind.Color:
                {
                    int sq = P(14);
                    var r = new RectangleF(b.X + (b.Width - sq) / 2f, b.Y + (b.Height - sq) / 2f, sq, sq);
                    using var path = Glyphs.RoundedRect(r, P(2));
                    using var fill = new SolidBrush(color);
                    using var edge = new Pen(Color.FromArgb(217, 255, 255, 255), 1);
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                    break;
                }
                case Kind.WidthField:
                {
                    using (var path = Glyphs.RoundedRect(b, P(4)))
                    using (var bg = new SolidBrush(Color.FromArgb(editingWidth ? 60 : 31, 255, 255, 255)))
                        g.FillPath(bg, path);
                    var text = editingWidth ? widthText : Fmt(lineWidth);
                    var f = UiFont();
                    var ts = g.MeasureString(text.Length == 0 ? "0" : text, f, PointF.Empty, StringFormat.GenericTypographic);
                    if (text.Length == 0) ts.Width = 0;
                    float tx = b.X + (b.Width - ts.Width) / 2, ty = b.Y + (b.Height - ts.Height) / 2;
                    if (editingWidth && widthReplace && text.Length > 0)
                    {
                        using var selBrush = new SolidBrush(Color.FromArgb(200, 10, 132, 255));
                        g.FillRectangle(selBrush, tx - 1, ty, ts.Width + 2, ts.Height);
                    }
                    g.DrawString(text, f, Brushes.White, tx, ty, StringFormat.GenericTypographic);
                    if (editingWidth && !widthReplace)
                        using (var caret = new Pen(Color.White, Math.Max(1, ui)))
                            g.DrawLine(caret, tx + ts.Width + 1, ty, tx + ts.Width + 1, ty + ts.Height);
                    break;
                }
                default:
                    Glyphs.Draw(g, it.Glyph, b, it.Kind == Kind.Copy ? blue : Color.White, ui);
                    break;
            }
        }
    }

    void DrawColorMenu(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawPanelBackground(g, colorMenuRect);
        var f = UiFont();
        for (int i = 0; i < Swatches.Length; i++)
        {
            var r = ColorRow(i);
            bool selected = Swatches[i].Color.ToArgb() == color.ToArgb();
            if (i == colorHover || selected)
            {
                using var hl = new SolidBrush(i == colorHover ? Color.FromArgb(200, 10, 132, 255) : Color.FromArgb(30, 255, 255, 255));
                using var hp = Glyphs.RoundedRect(r, P(4));
                g.FillPath(hl, hp);
            }
            int sq = P(14);
            var sw = new RectangleF(r.X + P(8), r.Y + (r.Height - sq) / 2f, sq, sq);
            using (var path = Glyphs.RoundedRect(sw, P(2)))
            using (var fill = new SolidBrush(Swatches[i].Color))
            using (var edge = new Pen(Color.FromArgb(150, 128, 128, 128), 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            var ts = g.MeasureString(Swatches[i].Name, f, PointF.Empty, StringFormat.GenericTypographic);
            g.DrawString(Swatches[i].Name, f, Brushes.White, sw.Right + P(8), r.Y + (r.Height - ts.Height) / 2, StringFormat.GenericTypographic);
        }
    }

    void DrawTip(Graphics g)
    {
        if (hover == null) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Glyphs.RoundedRect(tipRect, P(4)))
        using (var bg = new SolidBrush(Color.FromArgb(235, 20, 20, 22)))
        using (var edge = new Pen(Color.FromArgb(60, 255, 255, 255), 1))
        {
            g.FillPath(bg, path);
            g.DrawPath(edge, path);
        }
        g.DrawString(hover.Tip, UiFont(), Brushes.White, tipRect.X + P(7), tipRect.Y + P(4), StringFormat.GenericTypographic);
    }

    // MARK: Mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        HideTip();
        var p = Clamp(e.Location);

        if (colorMenu)
        {
            int i = ColorIndexAt(p);
            if (i >= 0) color = Swatches[i].Color;
            colorMenu = false;
            Changed();
            if (i >= 0 || toolPanel.Contains(p)) return;
        }

        var item = ItemAt(p);
        if (editingWidth && item?.Kind != Kind.WidthField) CommitWidth();

        if (item != null) { ClickItem(item); return; }
        if (panels && (toolPanel.Contains(p) || actionBar.Contains(p))) return;

        if (sel is Rectangle s)
        {
            int grab = Math.Max(7, P(7));
            foreach (var h in Handles(s))
            {
                if (Math.Abs(h.P.X - p.X) <= grab && Math.Abs(h.P.Y - p.Y) <= grab)
                {
                    (rl, rr, rt, rb) = (h.L, h.R, h.T, h.B);
                    resizeStart = s;
                    drag = DragMode.Resize;
                    panels = false;
                    Changed();
                    return;
                }
            }
            if (s.Contains(p))
            {
                current = new Shape(tool, p, color, lineWidth * ui, ui);
                lastShapeBounds = current.Bounds();
                drag = DragMode.Draw;
                Changed();
                return;
            }
        }

        // Start a new selection (clears the old one and its annotations).
        sel = null;
        shapes.Clear();
        current = null;
        panels = false;
        editingWidth = false;
        ui = ScaleAt(p);
        dragStart = p;
        drag = DragMode.Select;
        Changed();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = Clamp(e.Location);
        mouse = p;
        switch (drag)
        {
            case DragMode.Select:
                sel = Rectangle.FromLTRB(Math.Min(dragStart.X, p.X), Math.Min(dragStart.Y, p.Y),
                                         Math.Max(dragStart.X, p.X), Math.Max(dragStart.Y, p.Y));
                Changed();
                return;
            case DragMode.Draw:
                if (current == null || sel is not Rectangle s) return;
                var q = new PointF(Math.Clamp(p.X, s.Left, s.Right), Math.Clamp(p.Y, s.Top, s.Bottom));
                current.B = (ModifierKeys & Keys.Shift) != 0 ? current.Snapped(q) : q;
                var nb = current.Bounds();
                Invalidate(Rectangle.Intersect(Union(lastShapeBounds, nb), Rectangle.Inflate(s, 1, 1)));
                lastShapeBounds = nb;
                return;
            case DragMode.Resize:
            {
                var st = resizeStart;
                int minX = rl ? p.X : st.Left, maxX = rr ? p.X : st.Right;
                int minY = rt ? p.Y : st.Top, maxY = rb ? p.Y : st.Bottom;
                sel = Rectangle.FromLTRB(Math.Min(minX, maxX), Math.Min(minY, maxY), Math.Max(minX, maxX), Math.Max(minY, maxY));
                Changed();
                return;
            }
        }

        // Hover: highlight, tooltip, cursor.
        if (colorMenu)
        {
            int ci = ColorIndexAt(p);
            if (ci != colorHover) { colorHover = ci; Changed(); }
        }
        var item = ItemAt(p);
        if (item != hover)
        {
            hover = item;
            if (tipVisible) tipVisible = false;
            tipTimer.Stop();
            if (item != null) tipTimer.Start();
            Changed();
        }
        Cursor = CursorAt(p);
    }

    Cursor CursorAt(Point p)
    {
        if (colorMenu && colorMenuRect.Contains(p)) return Cursors.Default;
        if (panels && (toolPanel.Contains(p) || actionBar.Contains(p)))
            return ItemAt(p)?.Kind == Kind.WidthField ? Cursors.IBeam : Cursors.Default;
        if (sel is Rectangle s)
        {
            int grab = Math.Max(7, P(7));
            foreach (var h in Handles(s))
            {
                if (Math.Abs(h.P.X - p.X) > grab || Math.Abs(h.P.Y - p.Y) > grab) continue;
                if ((h.L && h.T) || (h.R && h.B)) return Cursors.SizeNWSE;
                if ((h.R && h.T) || (h.L && h.B)) return Cursors.SizeNESW;
                return h.L || h.R ? Cursors.SizeWE : Cursors.SizeNS;
            }
        }
        return Cursors.Cross;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        switch (drag)
        {
            case DragMode.Select:
            case DragMode.Resize:
                if (sel is Rectangle s && (s.Width < 4 || s.Height < 4)) sel = null;
                LayoutPanels();
                break;
            case DragMode.Draw:
                if (current != null && current.Length > 3) shapes.Add(current);
                current = null;
                break;
        }
        drag = DragMode.None;
        Changed();
        Invalidate(lastShapeBounds);
        Cursor = CursorAt(Clamp(e.Location));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!panels || !toolPanel.Contains(e.Location)) return;
        lineWidth = Math.Clamp(MathF.Round(lineWidth) + Math.Sign(e.Delta), 1, 50);
        if (editingWidth) { widthText = Fmt(lineWidth); widthReplace = true; }
        Changed();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != null) { hover = null; HideTip(); Changed(); }
    }

    void ClickItem(Item it)
    {
        switch (it.Kind)
        {
            case Kind.Tool: tool = it.Tool; break;
            case Kind.Color: if (colorMenu) colorMenu = false; else OpenColorMenu(); break;
            case Kind.WidthIcon:
            case Kind.WidthField: BeginWidthEdit(); return;
            case Kind.Undo: Undo(); break;
            case Kind.Copy: CopyImage(); return;
            case Kind.Save: SaveImage(); return;
            case Kind.Close: Close(); return;
        }
        Changed();
    }

    // MARK: Keys and actions

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;

        if (editingWidth)
        {
            if (mods == Keys.None || mods == Keys.Shift)
            {
                char? ch = key switch
                {
                    >= Keys.D0 and <= Keys.D9 when mods == Keys.None => (char)('0' + (key - Keys.D0)),
                    >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
                    Keys.OemPeriod or Keys.Decimal => '.',
                    _ => null,
                };
                if (ch != null)
                {
                    if (widthReplace) { widthText = ""; widthReplace = false; }
                    if (widthText.Length < 4 && !(ch == '.' && widthText.Contains('.'))) widthText += ch;
                    Changed();
                    return true;
                }
            }
            switch (key)
            {
                case Keys.Back:
                    widthText = widthReplace ? "" : widthText[..Math.Max(0, widthText.Length - 1)];
                    widthReplace = false;
                    Changed();
                    return true;
                case Keys.Enter:
                case Keys.Tab: CommitWidth(); return true;
                case Keys.Escape: CancelWidth(); return true;
                case Keys.Up: lineWidth = Math.Min(50, MathF.Round(lineWidth) + 1); widthText = Fmt(lineWidth); widthReplace = true; Changed(); return true;
                case Keys.Down: lineWidth = Math.Max(1, MathF.Round(lineWidth) - 1); widthText = Fmt(lineWidth); widthReplace = true; Changed(); return true;
            }
            if (mods == Keys.None) return true; // swallow other plain keys while typing
            CommitWidth();
        }

        if (mods == Keys.Control)
        {
            switch (key)
            {
                case Keys.C: CopyImage(); return true;
                case Keys.Z: Undo(); return true;
                case Keys.S: SaveImage(); return true;
            }
        }
        if (mods == Keys.None)
        {
            switch (key)
            {
                case Keys.Escape:
                    if (colorMenu) { colorMenu = false; Changed(); }
                    else Close();
                    return true;
                case Keys.A: SetTool(Tool.Arrow); return true;
                case Keys.L: SetTool(Tool.Line); return true;
                case Keys.R: SetTool(Tool.Rect); return true;
                case Keys.O: SetTool(Tool.Oval); return true;
                case Keys.W: BeginWidthEdit(); return true;
                case Keys.Enter: CopyImage(); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void SetTool(Tool t) { tool = t; Changed(); }

    void Undo()
    {
        if (shapes.Count == 0) { SystemSounds.Beep.Play(); return; }
        var b = shapes[^1].Bounds();
        shapes.RemoveAt(shapes.Count - 1);
        Invalidate(b);
        Changed();
    }

    /// Crops the frozen screen to the selection at full pixel resolution and draws the shapes on top.
    Bitmap? RenderSelection()
    {
        if (sel is not Rectangle s || s.Width < 1 || s.Height < 1) return null;
        var bmp = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(shot, new Rectangle(0, 0, s.Width, s.Height), s, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(-s.X, -s.Y);
        foreach (var sh in shapes) sh.Draw(g);
        return bmp;
    }

    void CopyImage()
    {
        if (editingWidth) CommitWidth();
        using var bmp = RenderSelection();
        if (bmp == null || !ImageOutput.Copy(bmp)) { SystemSounds.Beep.Play(); return; }
        Close();
    }

    void SaveImage()
    {
        if (editingWidth) CommitWidth();
        using var bmp = RenderSelection();
        if (bmp == null || ImageOutput.SaveToDesktop(bmp) == null) { SystemSounds.Beep.Play(); return; }
        Close();
    }
}
