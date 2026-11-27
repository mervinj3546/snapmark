using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Media;

namespace Snapmark;

/// Window editor for an existing image (`Snapmark --open file.png`).
sealed class EditorForm : Form
{
    readonly CanvasControl canvas;
    readonly Dictionary<Tool, ToolStripButton> toolButtons = new();
    readonly ToolStripButton[] widthButtons;
    readonly ToolStripButton colorButton;
    static readonly float[] Widths = { 2.5f, 4, 7 };

    public EditorForm(Bitmap image)
    {
        Text = "Snapmark";
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        float scale = DeviceDpi / 96f;
        int icon = (int)Math.Round(16 * scale);

        canvas = new CanvasControl(image, scale);
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.ControlDark };
        scroller.Controls.Add(canvas);

        var bar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(icon, icon),
            Padding = new Padding(8, 4, 8, 4),
            RenderMode = ToolStripRenderMode.System,
        };
        var dark = Color.FromArgb(40, 40, 40);
        foreach (var (t, glyph, tip) in new[] { (Tool.Arrow, "arrow", "Arrow (A)"), (Tool.Line, "line", "Line (L)"),
                                                (Tool.Rect, "rect", "Rectangle (R)"), (Tool.Oval, "oval", "Oval (O)") })
        {
            var b = new ToolStripButton(null, Glyphs.Bitmap(glyph, icon, dark), (_, _) => SetTool(t)) { ToolTipText = tip };
            toolButtons[t] = b;
            bar.Items.Add(b);
        }
        bar.Items.Add(new ToolStripSeparator());
        colorButton = new ToolStripButton(null, Swatch(icon, canvas.Color), (_, _) => PickColor()) { ToolTipText = "Color" };
        bar.Items.Add(colorButton);
        bar.Items.Add(new ToolStripSeparator());
        widthButtons = new[] { "S", "M", "L" }.Select((label, i) =>
        {
            var b = new ToolStripButton(label, null, (_, _) => SetWidth(i)) { ToolTipText = "Line thickness" };
            bar.Items.Add(b);
            return b;
        }).ToArray();
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton(null, Glyphs.Bitmap("undo", icon, dark), (_, _) => canvas.Undo()) { ToolTipText = "Undo (Ctrl+Z)" });
        var copy = new ToolStripButton("Copy", Glyphs.Bitmap("copy", icon, Color.FromArgb(0, 103, 192)), (_, _) => CopyImage())
        {
            Alignment = ToolStripItemAlignment.Right,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = "Copy to clipboard and close (Ctrl+C or Enter)",
        };
        bar.Items.Add(copy);

        Controls.Add(scroller);
        Controls.Add(bar);

        SetTool(Tool.Arrow);
        SetWidth(1);

        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int barH = bar.PreferredSize.Height;
        int w = (int)Math.Min(Math.Max(image.Width + 20, 560 * scale), wa.Width * 0.9);
        int h = (int)Math.Min(image.Height + barH + 20, wa.Height * 0.9);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(w, h);
    }

    static Bitmap Swatch(int size, Color c)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Glyphs.RoundedRect(new RectangleF(1, 1, size - 2, size - 2), size / 6f);
        using var b = new SolidBrush(c);
        using var p = new Pen(Color.FromArgb(120, 0, 0, 0), 1);
        g.FillPath(b, path);
        g.DrawPath(p, path);
        return bmp;
    }

    void SetTool(Tool t)
    {
        canvas.Tool = t;
        foreach (var (k, b) in toolButtons) b.Checked = k == t;
    }

    void SetWidth(int i)
    {
        canvas.LineWidth = Widths[i];
        for (int j = 0; j < widthButtons.Length; j++) widthButtons[j].Checked = j == i;
    }

    void PickColor()
    {
        using var dlg = new ColorDialog { Color = canvas.Color, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        canvas.Color = dlg.Color;
        var old = colorButton.Image;
        colorButton.Image = Swatch(old?.Width ?? 16, dlg.Color);
        old?.Dispose();
    }

    void CopyImage()
    {
        using var bmp = canvas.Render();
        if (!ImageOutput.Copy(bmp)) { SystemSounds.Beep.Play(); return; }
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.R: SetTool(Tool.Rect); return true;
            case Keys.L: SetTool(Tool.Line); return true;
            case Keys.A: SetTool(Tool.Arrow); return true;
            case Keys.O: SetTool(Tool.Oval); return true;
            case Keys.Enter:
            case Keys.Control | Keys.C: CopyImage(); return true;
            case Keys.Control | Keys.Z: canvas.Undo(); return true;
            case Keys.Escape: Close(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        canvas.Image.Dispose();
    }
}

sealed class CanvasControl : Control
{
    public readonly Bitmap Image;
    readonly List<Shape> shapes = new();
    Shape? current;
    readonly float scale;
    public Tool Tool = Tool.Arrow;
    public Color Color = Color.FromArgb(255, 26, 26);
    public float LineWidth = 4;

    public CanvasControl(Bitmap image, float scale)
    {
        Image = image;
        this.scale = scale;
        Size = image.Size;
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(Image, new Rectangle(0, 0, Image.Width, Image.Height), new Rectangle(0, 0, Image.Width, Image.Height), GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in shapes) s.Draw(g);
        current?.Draw(g);
    }

    PointF Clamp(Point p) => new(Math.Clamp(p.X, 0, Width), Math.Clamp(p.Y, 0, Height));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button != MouseButtons.Left) return;
        current = new Shape(Tool, Clamp(e.Location), Color, LineWidth * scale, scale);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (current == null) return;
        var p = Clamp(e.Location);
        current.B = (ModifierKeys & Keys.Shift) != 0 ? current.Snapped(p) : p;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (current != null && current.Length > 3) shapes.Add(current);
        current = null;
        Invalidate();
    }

    public void Undo()
    {
        if (shapes.Count == 0) { SystemSounds.Beep.Play(); return; }
        shapes.RemoveAt(shapes.Count - 1);
        Invalidate();
    }

    public Bitmap Render()
    {
        var bmp = new Bitmap(Image.Width, Image.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(Image, new Rectangle(0, 0, Image.Width, Image.Height), new Rectangle(0, 0, Image.Width, Image.Height), GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in shapes) s.Draw(g);
        return bmp;
    }
}
