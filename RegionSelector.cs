namespace RegionClip;

static class RegionSelector
{
    public static Rectangle? Pick(IWin32Window owner)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
            return null;

        var session = new SelectionSession();
        var overlays = screens
            .Select((screen, index) => new OverlayForm(screen, session, host: index == 0))
            .ToList();
        session.Attach(overlays);

        try
        {
            foreach (var overlay in overlays)
                overlay.Prepare();

            overlays[0].ShowDialog(owner);
            return session.Result;
        }
        finally
        {
            foreach (var overlay in overlays)
            {
                if (!overlay.IsDisposed)
                    overlay.Dispose();
            }
        }
    }
}

sealed class SelectionSession
{
    readonly List<OverlayForm> _overlays = [];
    bool _completed;

    public bool Dragging { get; private set; }
    public Rectangle? Result { get; private set; }
    public Point? Start { get; private set; }
    public Point? Current { get; private set; }

    public Rectangle? CurrentRect
    {
        get
        {
            if (Start is not Point start || Current is not Point current)
                return null;

            return Rectangle.FromLTRB(
                Math.Min(start.X, current.X),
                Math.Min(start.Y, current.Y),
                Math.Max(start.X, current.X),
                Math.Max(start.Y, current.Y));
        }
    }

    public void Attach(IEnumerable<OverlayForm> overlays) => _overlays.AddRange(overlays);

    public IReadOnlyList<OverlayForm> Overlays => _overlays;

    public void Begin(Point screenPoint)
    {
        if (_completed)
            return;

        Dragging = true;
        Start = screenPoint;
        Current = screenPoint;
        InvalidateAll();
    }

    public void Move(Point screenPoint)
    {
        if (!Dragging || _completed)
            return;

        Current = screenPoint;
        InvalidateAll();
    }

    public void End()
    {
        if (_completed)
            return;

        Dragging = false;
        var rect = CurrentRect;
        if (rect is { Width: >= 4, Height: >= 4 })
            Complete(rect);
        else
            Complete(null);
    }

    public void Cancel()
    {
        if (_completed)
            return;

        Dragging = false;
        Complete(null);
    }

    void Complete(Rectangle? result)
    {
        if (_completed)
            return;

        _completed = true;
        Result = result;

        void CloseAll()
        {
            foreach (var overlay in _overlays.ToArray())
            {
                if (!overlay.IsDisposed)
                    overlay.Close();
            }
        }

        var host = _overlays.FirstOrDefault(overlay => !overlay.IsDisposed && overlay.IsHandleCreated);
        if (host is null)
            CloseAll();
        else
            host.BeginInvoke(CloseAll);
    }

    void InvalidateAll()
    {
        foreach (var overlay in _overlays)
        {
            if (!overlay.IsDisposed)
                overlay.Invalidate();
        }
    }
}

sealed class OverlayForm : Form
{
    readonly Screen _screen;
    readonly SelectionSession _session;
    readonly bool _host;
    Bitmap? _screenshot;
    Bitmap? _dimmed;

    public OverlayForm(Screen screen, SelectionSession session, bool host)
    {
        _screen = screen;
        _session = session;
        _host = host;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = screen.Bounds;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Text = "Selecionar região";
    }

    public void Prepare()
    {
        try
        {
            var bounds = _screen.Bounds;
            _screenshot = ScreenCapture.Capture(bounds);
            _dimmed = new Bitmap(_screenshot);
            using var graphics = Graphics.FromImage(_dimmed);
            using var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            graphics.FillRectangle(shade, 0, 0, _dimmed.Width, _dimmed.Height);
        }
        catch
        {
            _screenshot?.Dispose();
            _screenshot = null;
            _dimmed?.Dispose();
            _dimmed = null;
            Opacity = 0.45;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _screen.Bounds;
        TopMost = true;
        WindowTop.Keep(this);
        if (_host)
        {
            foreach (var overlay in _session.Overlays)
            {
                if (!ReferenceEquals(overlay, this) && !overlay.Visible)
                    overlay.Show(this);
            }
        }

        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _session.Cancel();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;

        if (_dimmed is not null)
            graphics.DrawImage(_dimmed, ClientRectangle);
        else
            graphics.Clear(Color.FromArgb(160, 0, 0, 0));

        var selection = _session.CurrentRect;
        if (selection is Rectangle rect && rect.Width > 0 && rect.Height > 0)
            PaintSelection(graphics, rect);
        else if (_screen.Bounds.Contains(Cursor.Position))
            PaintHint(graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            _session.Cancel();
            return;
        }

        if (e.Button != MouseButtons.Left || _session.Dragging)
            return;

        Capture = true;
        _session.Begin(Cursor.Position);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_session.Dragging && Capture)
            _session.Move(Cursor.Position);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !Capture)
            return;

        Capture = false;
        _session.End();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_session.Dragging && !Capture)
            _session.End();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            _session.Cancel();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _screenshot?.Dispose();
        _dimmed?.Dispose();
        _screenshot = null;
        _dimmed = null;
        base.OnFormClosed(e);
    }

    void PaintSelection(Graphics graphics, Rectangle screenRect)
    {
        var topLeft = PointToClient(screenRect.Location);
        var bottomRight = PointToClient(new Point(screenRect.Right, screenRect.Bottom));
        var local = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        var visible = Rectangle.Intersect(local, ClientRectangle);
        if (visible.Width < 1 || visible.Height < 1)
            return;

        if (_screenshot is not null)
        {
            graphics.SetClip(visible);
            graphics.DrawImage(_screenshot, ClientRectangle);
            graphics.ResetClip();
        }

        using var pen = new Pen(Color.FromArgb(96, 165, 250), 2);
        graphics.DrawRectangle(pen, visible.X, visible.Y, Math.Max(1, visible.Width - 1), Math.Max(1, visible.Height - 1));

        var label = $"{screenRect.Width} × {screenRect.Height}";
        using var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
        var textSize = TextRenderer.MeasureText(label, font);
        var labelRect = new Rectangle(visible.X, visible.Bottom + 8, textSize.Width + 16, textSize.Height + 8);
        if (labelRect.Bottom > ClientSize.Height - 8)
            labelRect.Y = Math.Max(8, visible.Y - labelRect.Height - 8);
        if (labelRect.Right > ClientSize.Width - 8)
            labelRect.X = Math.Max(8, ClientSize.Width - labelRect.Width - 8);

        using var background = new SolidBrush(Color.FromArgb(230, 15, 23, 42));
        graphics.FillRectangle(background, labelRect);
        TextRenderer.DrawText(
            graphics,
            label,
            font,
            labelRect,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    void PaintHint(Graphics graphics)
    {
        const string hint = "Arraste para selecionar a região    ·    Esc cancela";
        using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Point);
        var size = TextRenderer.MeasureText(hint, font);
        var rect = new Rectangle(
            Math.Max(16, (ClientSize.Width - size.Width - 28) / 2),
            28,
            size.Width + 28,
            size.Height + 16);

        using var background = new SolidBrush(Color.FromArgb(210, 15, 23, 42));
        graphics.FillRectangle(background, rect);
        TextRenderer.DrawText(
            graphics,
            hint,
            font,
            rect,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int composited = 0x02000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= composited | WindowTop.Style;
            return parameters;
        }
    }
}
