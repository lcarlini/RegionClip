using System.Runtime.InteropServices;

namespace RegionClip;

sealed class MainForm : Form
{
    readonly ActionButton _selectButton;
    readonly ActionButton _printButton;
    readonly ActionButton _closeButton;
    readonly Label _status;
    readonly ToolTip _tips = new();
    readonly System.Windows.Forms.Timer _statusTimer;
    Rectangle? _region;
    bool _busy;

    public MainForm()
    {
        Text = "RegionClip";
        var icon = Program.CreateIcon();
        if (icon is not null)
            Icon = icon;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(55, 58, 70);
        Padding = new Padding(1);
        ClientSize = new Size(380, 98);
        MinimumSize = new Size(320, 90);
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 20, 27),
        };
        root.MouseDown += StartDrag;
        root.Resize += (_, _) => LayoutBar();

        _selectButton = new ActionButton(ButtonIcon.Select, "Selecionar região", Color.FromArgb(37, 99, 235), Color.FromArgb(29, 78, 216))
        {
            TabIndex = 0,
        };
        _printButton = new ActionButton(ButtonIcon.Capture, "Tirar print", Color.FromArgb(5, 150, 105), Color.FromArgb(4, 120, 87))
        {
            Enabled = false,
            TabIndex = 1,
        };
        _closeButton = new ActionButton(ButtonIcon.Close, "Fechar", Color.FromArgb(185, 28, 28), Color.FromArgb(153, 27, 27))
        {
            TabIndex = 2,
        };
        _tips.SetToolTip(_selectButton, "Selecionar região");
        _tips.SetToolTip(_printButton, "Tirar print. Atalho: F8. Grave a tecla F8 no G5 do teclado.");
        _tips.SetToolTip(_closeButton, "Fechar");

        _selectButton.Click += async (_, _) => await SelectRegionAsync();
        _printButton.Click += async (_, _) => await CopyPrintAsync();
        _closeButton.Click += (_, _) => Close();

        _status = new Label
        {
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(161, 166, 179),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Nenhuma região selecionada  ·  arraste a barra para mover",
        };
        _status.MouseDown += StartDrag;

        root.Controls.Add(_selectButton);
        root.Controls.Add(_printButton);
        root.Controls.Add(_closeButton);
        root.Controls.Add(_status);
        Controls.Add(root);
        LayoutBar();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 2200 };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            ShowRegionStatus();
        };

    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToScreen();
        PlaceNearTop();
    }

    void FitToScreen()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        if (Width > area.Width - 24)
            Width = Math.Max(520, area.Width - 24);
        if (Height > area.Height / 3)
            Height = Math.Max(140, area.Height / 3);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var preference = 2;
        DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
        WindowTop.Keep(this);
        RegisterHotKey(Handle, CaptureHotkeyId, ModNoRepeat, (uint)Keys.F8);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, CaptureHotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == CaptureHotkeyId)
        {
            TriggerCapture();
            return;
        }

        base.WndProc(ref m);
    }

    void TriggerCapture()
    {
        if (_busy)
            return;

        if (_region is null)
        {
            _statusTimer.Stop();
            _status.ForeColor = Color.FromArgb(252, 165, 165);
            _status.Text = "Selecione uma região antes de tirar o print";
            _statusTimer.Start();
            return;
        }

        _ = CopyPrintAsync();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        WindowTop.Keep(this);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int dropShadow = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= dropShadow;
            parameters.ExStyle |= WindowTop.Style;
            return parameters;
        }
    }

    void LayoutBar()
    {
        if (_selectButton is null || _printButton is null || _closeButton is null || _status is null)
            return;

        var host = _selectButton.Parent;
        if (host is null || host.ClientSize.Width < 200)
            return;

        const int size = 40;
        const int side = 22;
        const int top = 12;
        var gap = Math.Max(36, (host.ClientSize.Width - (side * 2) - (size * 3)) / 2);
        var left = Math.Max(side, (host.ClientSize.Width - ((size * 3) + (gap * 2))) / 2);
        _selectButton.SetBounds(left, top, size, size);
        _printButton.SetBounds(left + size + gap, top, size, size);
        _closeButton.SetBounds(left + ((size + gap) * 2), top, size, size);
        _status.SetBounds(8, top + size + 4, host.ClientSize.Width - 16, 22);
    }

    async Task SelectRegionAsync()
    {
        if (_busy)
            return;

        _busy = true;
        SetActionsEnabled(false);
        Hide();
        try
        {
            await Task.Delay(80);
            var picked = RegionSelector.Pick(this);
            if (picked is Rectangle region)
            {
                _region = region;
                ShowRegionStatus();
                MoveAwayFrom(region);
            }
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.FromArgb(252, 165, 165);
            _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            Show();
            WindowTop.Keep(this);
            Activate();
            SetActionsEnabled(true);
        }
    }

    async Task CopyPrintAsync()
    {
        if (_busy || _region is not Rectangle region)
            return;

        _busy = true;
        SetActionsEnabled(false);
        Hide();
        try
        {
            await Task.Delay(120);
            using var bitmap = ScreenCapture.Capture(region);
            ScreenCapture.CopyToClipboard(bitmap);
            _status.ForeColor = Color.FromArgb(52, 211, 153);
            _status.Text = "Print copiado para a área de transferência";
            _statusTimer.Stop();
            _statusTimer.Start();
        }
        catch (Exception ex)
        {
            _statusTimer.Stop();
            _status.ForeColor = Color.FromArgb(252, 165, 165);
            _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            Show();
            WindowTop.Keep(this);
            Activate();
            SetActionsEnabled(true);
        }
    }

    void SetActionsEnabled(bool enabled)
    {
        _selectButton.Enabled = enabled;
        _closeButton.Enabled = enabled;
        _printButton.Enabled = enabled && _region is not null;
    }

    void ShowRegionStatus()
    {
        if (_region is not Rectangle region)
        {
            _status.ForeColor = Color.FromArgb(161, 166, 179);
            _status.Text = "Nenhuma região selecionada  ·  arraste a barra para mover";
            return;
        }

        _status.ForeColor = Color.FromArgb(191, 219, 254);
        _status.Text = $"Região pronta: {region.Width} × {region.Height}   ·   pode selecionar outra a qualquer momento";
    }

    void MoveAwayFrom(Rectangle region)
    {
        var bar = new Rectangle(Location, Size);
        if (!bar.IntersectsWith(region))
            return;

        var area = Screen.GetWorkingArea(region);
        Point[] candidates =
        [
            new Point(area.Left + 16, area.Top + 16),
            new Point(area.Right - Width - 16, area.Top + 16),
            new Point(area.Left + 16, area.Bottom - Height - 16),
            new Point(area.Right - Width - 16, area.Bottom - Height - 16),
        ];

        var best = candidates
            .OrderBy(point => IntersectionArea(new Rectangle(point, Size), region))
            .First();
        Location = best;
    }

    static int IntersectionArea(Rectangle left, Rectangle right)
    {
        var intersection = Rectangle.Intersect(left, right);
        if (intersection.Width < 1 || intersection.Height < 1)
            return 0;

        return intersection.Width * intersection.Height;
    }

    void PlaceNearTop()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + 20);
    }

    void StartDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int CaptureHotkeyId = 1;
    const int WmHotkey = 0x0312;
    const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int message, int wParam, int lParam);
}

enum ButtonIcon
{
    Select,
    Capture,
    Close,
}

sealed class ActionButton : Control
{
    readonly ButtonIcon _icon;
    readonly Color _fill;
    readonly Color _hoverFill;
    readonly Color _disabledFill = Color.FromArgb(42, 45, 54);
    bool _hover;
    bool _pressed;

    public ActionButton(ButtonIcon icon, string accessibleName, Color fill, Color hoverFill)
    {
        _icon = icon;
        _fill = fill;
        _hoverFill = hoverFill;
        AccessibleName = accessibleName;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        var fill = !Enabled ? _disabledFill : _pressed || _hover ? _hoverFill : _fill;
        var fore = Enabled ? Color.White : Color.FromArgb(130, 134, 145);
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));

        using var path = RoundRect(bounds, 10);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);

        var icon = Math.Max(14, (int)Math.Round(Math.Min(Width, Height) * 0.46));
        var iconBounds = new Rectangle(
            (Width - icon) / 2,
            (Height - icon) / 2,
            icon,
            icon);
        using var pen = new Pen(fore, Math.Max(1.6f, icon / 10f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        DrawIcon(graphics, pen, iconBounds);
    }

    void DrawIcon(Graphics graphics, Pen pen, Rectangle bounds)
    {
        switch (_icon)
        {
            case ButtonIcon.Select:
                DrawCrop(graphics, pen, bounds);
                break;
            case ButtonIcon.Capture:
                DrawCamera(graphics, pen, bounds);
                break;
            default:
                DrawClose(graphics, pen, bounds);
                break;
        }
    }

    static void DrawCrop(Graphics graphics, Pen pen, Rectangle bounds)
    {
        var length = Math.Max(4f, bounds.Width * 0.34f);
        var left = bounds.Left + 0.5f;
        var top = bounds.Top + 0.5f;
        var right = bounds.Right - 1.5f;
        var bottom = bounds.Bottom - 1.5f;
        graphics.DrawLines(pen, [new PointF(left, top + length), new PointF(left, top), new PointF(left + length, top)]);
        graphics.DrawLines(pen, [new PointF(right - length, top), new PointF(right, top), new PointF(right, top + length)]);
        graphics.DrawLines(pen, [new PointF(left, bottom - length), new PointF(left, bottom), new PointF(left + length, bottom)]);
        graphics.DrawLines(pen, [new PointF(right - length, bottom), new PointF(right, bottom), new PointF(right, bottom - length)]);
    }

    static void DrawCamera(Graphics graphics, Pen pen, Rectangle bounds)
    {
        float x = bounds.Left + 0.5f;
        float y = bounds.Top;
        float width = bounds.Width - 1.5f;
        float height = bounds.Height - 1.5f;
        var body = new RectangleF(x, y + (height * 0.26f), width, height * 0.74f);
        using var bodyPath = RoundRectF(body, width * 0.14f);
        graphics.DrawPath(pen, bodyPath);

        var lens = width * 0.34f;
        graphics.DrawEllipse(pen, x + ((width - lens) / 2f), y + (height * 0.46f), lens, lens);

        float bumpX = x + (width * 0.16f);
        float bumpTop = y + (height * 0.05f);
        float bumpRight = bumpX + (width * 0.36f);
        float bumpBottom = y + (height * 0.26f);
        graphics.DrawLines(pen,
        [
            new PointF(bumpX, bumpBottom),
            new PointF(bumpX, bumpTop),
            new PointF(bumpRight, bumpTop),
            new PointF(bumpRight, bumpBottom),
        ]);
    }

    static void DrawClose(Graphics graphics, Pen pen, Rectangle bounds)
    {
        var inset = Math.Max(1f, bounds.Width * 0.12f);
        var left = bounds.Left + inset;
        var top = bounds.Top + inset;
        var right = bounds.Right - inset - 1f;
        var bottom = bounds.Bottom - inset - 1f;
        graphics.DrawLine(pen, left, top, right, bottom);
        graphics.DrawLine(pen, right, top, left, bottom);
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundRectF(RectangleF bounds, float radius)
    {
        var diameter = radius * 2f;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
            return;

        _pressed = true;
        Focus();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var shouldClick = Enabled && _pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        _pressed = false;
        Invalidate();
        if (shouldClick)
            OnClick(EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space))
            return;

        e.Handled = true;
        OnClick(EventArgs.Empty);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
