using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Design
{
    public static readonly Color Background = Color.FromArgb(17, 18, 23), Sidebar = Color.FromArgb(23, 24, 31);
    public static readonly Color Surface = Color.FromArgb(28, 30, 38), Border = Color.FromArgb(47, 50, 62);
    public static readonly Color White = Color.FromArgb(241, 242, 246), Muted = Color.FromArgb(152, 158, 176);
    public static readonly Color Accent = Color.FromArgb(230, 66, 77), Green = Color.FromArgb(103, 211, 161);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Box(Graphics g, RectangleF r, Color color, float radius, Color? border = null)
    {
        using (var path = Round(r, radius))
        {
            using (var b = new SolidBrush(color)) g.FillPath(b, path);
            if (border.HasValue) using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
        }
    }

    public static void Text(Graphics g, string text, float size, Color color, RectangleF r, bool bold = false, StringAlignment align = StringAlignment.Near)
    {
        using (var font = new Font("Leelawadee UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
        using (var format = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter })
            g.DrawString(text, font, brush, r, format);
    }
}

class ModernButton : Control
{
    public bool Primary;
    bool hover, pressed;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = Design.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        Color fill = !Enabled ? Color.FromArgb(43, 45, 55) : Primary ? (pressed ? Color.FromArgb(190, 47, 59) : hover ? Color.FromArgb(244, 84, 95) : Design.Accent) : (hover ? Color.FromArgb(45, 48, 61) : Design.Surface);
        Design.Box(g, new RectangleF(1, 1, Width - 2, Height - 2), fill, 10 * Height / 56f, Primary ? (Color?)null : Design.Border);
        using (var f = new Font("Leelawadee UI", (Primary ? 17 : 14) * Height / (Primary ? 56f : 40f), Primary ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(Enabled ? Design.White : Design.Muted))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(Text, f, b, new RectangleF(7, 0, Width - 14, Height), format);
        if (Focused)
            using (var p = new Pen(Color.FromArgb(200, 205, 224), 2))
            using (var path = Design.Round(new RectangleF(4, 4, Width - 8, Height - 8), 8))
                g.DrawPath(p, path);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { OnClick(EventArgs.Empty); e.SuppressKeyPress = true; } base.OnKeyDown(e); }
}

class InstallerForm : Form
{
    readonly ModernButton install = new ModernButton { Primary = true, Text = "ติดตั้งม็อดภาษาไทย  →" };
    readonly ModernButton scan = new ModernButton { Text = "ค้นหาใหม่" };
    readonly ModernButton donate = new ModernButton { Text = "♡  โดเนทให้กำลังใจ" };
    readonly ModernButton next = new ModernButton { Text = "คลังเกมถัดไป  →" };
    readonly Image logo;
    List<Game> games = new List<Game>();
    int selected, progress;
    bool busy, scanning, complete, error;
    string status = "กำลังค้นหาเกมจาก Steam…";

    float UiScale { get { return Math.Min(ClientSize.Width / 1020f, ClientSize.Height / 680f); } }
    Game Current { get { return games.Count > selected ? games[selected] : null; } }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public InstallerForm()
    {
        Text = "Spider-F  |  ม่านลวงบ่วงแค้น (Undertone / 底色) — 2.0.0 [ไทย + ฟอนต์ Windows]";
        ClientSize = new Size(1020, 680);
        MinimumSize = new Size(1036, 719);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Design.Background;
        ForeColor = Design.White;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        using (var s = Engine.Resource("app.ico")) Icon = new Icon(s);
        using (var s = Engine.Resource("logo.png")) logo = new Bitmap(Image.FromStream(s));
        Controls.AddRange(new Control[] { install, scan, donate, next });
        install.TabIndex = 0; scan.TabIndex = 1; next.TabIndex = 2; donate.TabIndex = 3;
        install.Enabled = false; next.Visible = false;
        install.Click += async (a, b) => await RunInstall();
        scan.Click += async (a, b) => await ScanAsync();
        next.Click += (a, b) => { selected = (selected + 1) % games.Count; complete = false; error = false; progress = 0; install.Enabled = true; install.Text = "ติดตั้งม็อดภาษาไทย  →"; status = "ปิดเกมก่อนเริ่ม แล้วกดติดตั้งม็อดด้านล่าง"; Invalidate(); };
        donate.Click += (a, b) => { try { Process.Start(new ProcessStartInfo(Engine.Donate) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(this, e.Message, "Spider-F"); } };
        Shown += async (a, b) => await ScanAsync();
        Resize += (a, b) => Arrange();
        FormClosing += (a, b) => { if (busy) b.Cancel = true; };
        Arrange();
        AccessibleName = "Spider-F ตัวติดตั้งม็อดภาษาไทย ม่านลวงบ่วงแค้น (Undertone)";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0)
                DwmSetWindowAttribute(Handle, 19, ref dark, 4);
            int rounded = 2;
            DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
        }
        catch { }
    }

    void Arrange()
    {
        float s = UiScale;
        install.Bounds = Rect(326, 530, 646, 56, s);
        scan.Bounds = Rect(826, 228, 146, 40, s);
        donate.Bounds = Rect(28, 538, 222, 46, s);
        next.Bounds = Rect(326, 390, 170, 34, s);
        donate.BackColor = Design.Sidebar;
    }

    static Rectangle Rect(int x, int y, int w, int h, float s)
    {
        return new Rectangle((int)(x * s), (int)(y * s), (int)(w * s), (int)(h * s));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PaintSurface(e.Graphics);
    }

    void PaintSurface(Graphics graphics)
    {
        var state = graphics.Save();
        graphics.ScaleTransform(UiScale, UiScale);
        var g = graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var brush = new SolidBrush(Design.Sidebar)) g.FillRectangle(brush, 0, 0, 282, 680);
        using (var pen = new Pen(Design.Border)) g.DrawLine(pen, 282, 0, 282, 680);
        Design.Text(g, "THAI LOCALIZATION", 12, Design.Muted, new RectangleF(36, 36, 220, 24), true);
        var saved = g.Save();
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(38, 112, 206, 206);
            g.SetClip(clip);
            g.DrawImage(logo, new Rectangle(38, 112, 206, 206));
        }
        g.Restore(saved);
        Design.Text(g, "Spider-F", 34, Design.White, new RectangleF(36, 348, 220, 54), true);
        Design.Text(g, "เรื่องราวเดิม ในภาษาของคุณ", 15, Design.Muted, new RectangleF(36, 402, 218, 52));
        Design.Text(g, "แปลและเรียบเรียงภาษาไทย", 13, Design.Muted, new RectangleF(36, 604, 215, 24));
        Design.Text(g, "ม็อดภาษาไทย • รุ่น 2.0.0", 16, Design.White, new RectangleF(36, 630, 215, 30));

        Design.Text(g, "UNDERTONE  •  ม่านลวงบ่วงแค้น", 13, Design.Muted, new RectangleF(326, 34, 646, 24), true);
        Design.Text(g, "ม็อดภาษาไทย", 36, Design.White, new RectangleF(323, 66, 649, 60), true);
        Design.Text(g, "ม่านลวงบ่วงแค้น (Undertone / 底色 Full)", 18, Design.Muted, new RectangleF(326, 128, 646, 32));
        Design.Box(g, new RectangleF(326, 176, 274, 34), Color.FromArgb(49, 40, 29), 8);
        Design.Text(g, "ซับไทย + UI ภาษาไทยครบ 100%", 15, Color.FromArgb(242, 198, 112), new RectangleF(339, 181, 254, 27), true);
        Design.Text(g, "อัปเดต 18 พ.ย. 2026", 12, Design.Muted, new RectangleF(616, 184, 300, 22), true);
        Design.Text(g, "ตำแหน่งติดตั้ง (ตรวจไฟล์เกมจาก Steam เท่านั้น)", 20, Design.White, new RectangleF(326, 232, 480, 34), true);
        Design.Box(g, new RectangleF(326, 282, 646, 100), Design.Surface, 12, Design.Border);
        Color indicator = Current != null ? Design.Green : scanning ? Color.FromArgb(242, 198, 112) : Design.Muted;
        using (var brush = new SolidBrush(indicator)) g.FillEllipse(brush, 346, 304, 7, 7);
        string located = Current != null ? "พบเกมที่ติดตั้งผ่าน Steam" : "ค้นหาจากคลังเกม Steam เท่านั้น";
        Design.Text(g, located, 14, indicator, new RectangleF(363, 296, 580, 27), true);
        Design.Text(g, Current != null ? Current.Root : (scanning ? "กำลังอ่านรายการคลังเกม…" : "ยังไม่พบตัวเกมในคลัง Steam"), 16, Design.White, new RectangleF(345, 330, 606, 45));
        if (games.Count > 1) Design.Text(g, (selected + 1) + " / " + games.Count + " คลังเกม", 13, Design.Muted, new RectangleF(512, 397, 420, 26));
        string heading = busy ? "กำลังติดตั้ง" : complete ? "ติดตั้งสำเร็จ" : error ? "ติดตั้งไม่สำเร็จ" : scanning ? "กำลังค้นหา" : Current != null ? "พร้อมติดตั้ง" : "ไม่พบเกมใน Steam";
        Design.Text(g, heading, 19, error ? Color.FromArgb(255, 135, 143) : complete ? Design.Green : Design.White, new RectangleF(326, 435, 568, 34), true);
        Design.Text(g, status, 14, Design.Muted, new RectangleF(326, 472, 646, 42));
        if (busy || complete)
        {
            Design.Text(g, progress + "%", 15, Design.Muted, new RectangleF(896, 439, 76, 26), false, StringAlignment.Far);
            Design.Box(g, new RectangleF(326, 514, 646, 5), Design.Surface, 2);
            if (progress > 0) Design.Box(g, new RectangleF(326, 514, Math.Max(5, 646 * progress / 100f), 5), complete ? Design.Green : Design.Accent, 2);
        }
        Design.Text(g, "หลังติดตั้ง สามารถเลือกภาษา [ไทย] และเลือกฟอนต์ Windows ได้ในหน้าตั้งค่า (หรือกด F9)", 14, Design.Muted, new RectangleF(326, 607, 646, 48));
        graphics.Restore(state);
    }

    void SetGames(List<Game> found)
    {
        games = found; selected = 0; next.Visible = games.Count > 1; install.Enabled = Current != null;
        install.Text = "ติดตั้งม็อดภาษาไทย  →";
        status = Current != null ? "ปิดเกมก่อนเริ่ม แล้วกดติดตั้งม็อดด้านล่าง" : "ติดตั้งเกมผ่าน Steam แล้วกดค้นหาใหม่";
        AccessibleDescription = status + (Current != null ? " " + Current.Root : "");
        Invalidate();
    }

    async Task ScanAsync()
    {
        if (busy || scanning) return;
        scanning = true; complete = false; error = false; status = "กำลังอ่านรายการคลังเกม Steam…"; scan.Enabled = false; install.Enabled = false; next.Enabled = false; Invalidate();
        try { var found = await Task.Run(() => Engine.Discover()); if (!IsDisposed) SetGames(found); }
        catch (Exception e) { if (!IsDisposed) { SetGames(new List<Game>()); status = "ค้นหาไม่สำเร็จ: " + e.Message; } }
        finally { if (!IsDisposed) { scanning = false; scan.Enabled = true; next.Enabled = true; Invalidate(); } }
    }

    async Task RunInstall()
    {
        var game = Current; if (game == null || busy) return;
        busy = true; complete = false; error = false; progress = 0; install.Enabled = false; scan.Enabled = false; next.Enabled = false;
        install.Text = "กำลังติดตั้ง…"; status = "กำลังเตรียมไฟล์และตรวจเกมจาก Steam"; Invalidate();
        try
        {
            await Task.Run(() => Engine.Install(game, (n, s) => Invoke(new Action(() => { progress = n; status = s; Invalidate(); }))));
            complete = true; status = "ติดตั้งเรียบร้อย — เข้าเกมผ่าน Steam และปรับขนาดซับได้ในตั้งค่า"; install.Text = "ติดตั้งเรียบร้อยแล้ว  ✓";
        }
        catch (Exception e)
        {
            error = true; status = e.Message; install.Text = "ลองติดตั้งอีกครั้ง  →";
            MessageBox.Show(this, e.Message, "Spider-F — ติดตั้งไม่สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { busy = false; scan.Enabled = true; next.Enabled = true; install.Enabled = !complete && Current != null; Invalidate(); }
    }

    public void RenderPreview(string path, string mode)
    {
        SetGames(Engine.Discover());
        if (mode == "missing") SetGames(new List<Game>());
        if (mode == "installing") { busy = true; progress = 64; status = "กำลังประกอบไฟล์เกมและฝังม็อดภาษาไทย (64%)..."; install.Text = "กำลังติดตั้ง…"; install.Enabled = false; scan.Enabled = false; }
        if (mode == "complete") { complete = true; progress = 100; status = "ติดตั้งเรียบร้อย — เข้าเกมผ่าน Steam และปรับขนาดซับได้ในตั้งค่า"; install.Text = "ติดตั้งเรียบร้อยแล้ว  ✓"; install.Enabled = false; }
        var handle = Handle; Arrange(); foreach (Control c in Controls) { var childHandle = c.Handle; }
        using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(BackColor); PaintSurface(g);
            foreach (Control c in Controls)
            {
                if (c == next && games.Count <= 1) continue;
                using (var b = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                    g.DrawImageUnscaled(b, c.Location);
                }
            }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && logo != null) logo.Dispose();
        base.Dispose(disposing);
    }
}
