// .BAK - radial wheel + toast overlay windows
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BakApp
{
    // Borderless, click-through-where-transparent, non-activating window drawn with per-pixel alpha.
    internal class OverlayWindow : Form
    {
        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(10, 10);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; // LAYERED | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void EnsureHandle()
        {
            if (!IsHandleCreated) CreateHandle();
        }

        public void Present(Bitmap bmp, int x, int y, byte alpha)
        {
            EnsureHandle();
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                oldObj = Native.SelectObject(memDc, hBmp);
                Native.SIZE size = new Native.SIZE(); size.cx = bmp.Width; size.cy = bmp.Height;
                Native.POINT src = new Native.POINT();
                Native.POINT dst = new Native.POINT(); dst.x = x; dst.y = y;
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = alpha; bf.AlphaFormat = 1;
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, 2);
            }
            finally
            {
                if (oldObj != IntPtr.Zero) Native.SelectObject(memDc, oldObj);
                if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected static float Ease(float t) { float u = 1f - Math.Max(0f, Math.Min(1f, t)); return 1f - u * u * u; }
    }

    // ------------------------------------------------------------------ wheel
    internal enum OptKind { Rename, Custom, Manage, Backup, Restore }

    internal sealed class WheelOption
    {
        public OptKind Kind;
        public string Label;
        public string Ext;
        public bool Suggested;
        public string Hint;
        public float T; // hover animation 0..1
    }

    internal sealed class WheelContext
    {
        public string SourceLabel = "";
        public string Caption = "";
        public string BaseName;    // set only for a single file; used for the live preview
        public string Status;
        public int StatusLevel;    // 0 none, 1 ok, 2 warning
        public string BackupName;  // preview of the ".bak" copy name (single file only)
        public string RestoreName; // preview of the restored name (single file only)
    }

    internal sealed class WheelWindow : OverlayWindow
    {
        enum St { Hidden, Opening, Open, Closing }

        public event Action<WheelOption> Chosen;
        public event Action Dismissed;
        public Func<bool> HoldHeld;   // is the trigger button still physically held?

        List<WheelOption> opts = new List<WheelOption>();
        WheelContext ctx = new WheelContext();
        System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        Stopwatch sw = new Stopwatch();
        St st = St.Hidden;
        Point center;
        int size;
        float s = 1f;
        int hover = -1;
        bool dirty, anim = true, leftWas, holdMode, pendingRelease;
        Color accent = Color.FromArgb(76, 141, 255);
        long lastActive, closeStart;
        Point lastPos;

        public bool IsOpen { get { return st == St.Opening || st == St.Open; } }
        public bool HoldMode { get { return holdMode; } }
        public void NotifyRelease() { pendingRelease = true; }

        public WheelWindow()
        {
            tick.Interval = 15;
            tick.Tick += OnTick;
        }

        public void Open(List<WheelOption> options, WheelContext c, Point cursor, bool hold, Color accentColor, bool animations)
        {
            opts = options; ctx = c; accent = accentColor; anim = animations;
            holdMode = hold; pendingRelease = false;
            s = Ui.Scale;
            size = (int)Math.Ceiling(360 * s);
            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int half = size / 2;
            int cx = Math.Max(wa.Left + half, Math.Min(wa.Right - half, cursor.X));
            int cy = Math.Max(wa.Top + half, Math.Min(wa.Bottom - half, cursor.Y));
            center = new Point(cx, cy);
            if (Math.Abs(cx - cursor.X) > 3 || Math.Abs(cy - cursor.Y) > 3) Cursor.Position = center;
            foreach (WheelOption o in opts) o.T = 0;
            hover = -1;
            st = St.Opening;
            sw.Restart();
            lastActive = 0;
            lastPos = Cursor.Position;
            leftWas = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
            dirty = true;
            EnsureHandle();
            Render();
            if (!Visible) Show();
            tick.Start();
        }

        public void Dismiss()
        {
            if (!IsOpen) return;
            if (!anim) { HideNow(); return; }
            st = St.Closing;
            closeStart = sw.ElapsedMilliseconds;
            dirty = true;
        }

        // idx: -1 = Esc, 0..7 = digit shortcut
        public void HandleKey(int idx)
        {
            if (!IsOpen) return;
            if (idx < 0) { Dismiss(); return; }
            if (idx < opts.Count) Choose(idx);
        }

        void HideNow()
        {
            bool wasOpen = st != St.Hidden;
            st = St.Hidden;
            tick.Stop();
            if (Visible) Hide();
            if (wasOpen && Dismissed != null) Dismissed();
        }

        void Choose(int index)
        {
            WheelOption o = opts[index];
            HideNow();
            Action<WheelOption> cb = Chosen;
            if (cb != null) cb(o);
        }

        void OnTick(object sender, EventArgs e)
        {
            if (st == St.Hidden) return;
            try { Step(); }
            catch (Exception ex) { Log.Write("wheel tick: " + ex); HideNow(); }
        }

        void Step()
        {
            long ms = sw.ElapsedMilliseconds;
            Point p = Cursor.Position;
            if (p != lastPos) { lastPos = p; lastActive = ms; }

            if (st == St.Opening) { if (!anim || ms >= 150) st = St.Open; dirty = true; }
            if (st == St.Closing)
            {
                if (!anim || ms - closeStart >= 100) { HideNow(); return; }
                Render();
                return;
            }

            if (holdMode && (pendingRelease || (HoldHeld != null && !HoldHeld())))
            {
                bool far = true;
                holdMode = false; pendingRelease = false;
                int rh = HitSector(p, far);
                if (rh >= 0) { Choose(rh); return; }
            }

            int h = HitSector(p, holdMode);
            if (h != hover) { hover = h; dirty = true; }
            for (int i = 0; i < opts.Count; i++)
            {
                float target = i == hover ? 1f : 0f;
                float nt = anim ? opts[i].T + (target - opts[i].T) * 0.38f : target;
                if (Math.Abs(nt - target) < 0.02f) nt = target;
                if (nt != opts[i].T) { opts[i].T = nt; dirty = true; }
            }

            bool ld = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
            if (ld && !leftWas)
            {
                leftWas = true;
                int ch = HitSector(p, false);
                if (ch >= 0) { Choose(ch); return; }
                if (!holdMode) { Dismiss(); return; }
            }
            leftWas = ld;

            if (!holdMode && ms - lastActive > 9000) { Dismiss(); return; }
            if (dirty) Render();
        }

        int HitSector(Point p, bool far)
        {
            int n = opts.Count;
            if (n == 0) return -1;
            double dx = p.X - center.X, dy = p.Y - center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 62 * s) return -1;
            if (!far && dist > 150 * s) return -1;
            double step = 360.0 / n;
            double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double a = (ang + 90 + step / 2) % 360;
            if (a < 0) a += 360;
            int idx = (int)(a / step);
            if (idx >= n) idx = n - 1;
            return idx;
        }

        // ---------------------------------------------------------- drawing
        static Color Mix(Color a, Color b, float t) { return Ui.Mix(a, b, t); }

        void Render()
        {
            dirty = false;
            long ms = sw.ElapsedMilliseconds;
            float k = 1f; int a = 255;
            if (anim)
            {
                if (st == St.Opening) { float e = Ease(ms / 150f); k = 0.88f + 0.12f * e; a = (int)(255 * e); }
                else if (st == St.Closing) { float e = 1f - Math.Min(1f, (ms - closeStart) / 100f); k = 0.97f + 0.03f * e; a = (int)(255 * e); }
            }
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    float c = size / 2f;
                    g.TranslateTransform(c, c);
                    g.ScaleTransform(k, k);
                    g.TranslateTransform(-c, -c);
                    DrawAll(g, c);
                }
                Present(bmp, center.X - size / 2, center.Y - size / 2, (byte)Math.Max(0, Math.Min(255, a)));
            }
        }

        static GraphicsPath SectorPath(float c, float ri, float ro, float a0, float sweep)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(c - ro, c - ro, 2 * ro, 2 * ro, a0, sweep);
            p.AddArc(c - ri, c - ri, 2 * ri, 2 * ri, a0 + sweep, -sweep);
            p.CloseFigure();
            return p;
        }

        void DrawAll(Graphics g, float c)
        {
            float discR = 150 * s, ringO = 140 * s, ringI = 70 * s, centerR = 60 * s, grow = 7 * s, ins = 2.5f * s;

            // soft shadow
            float sr = discR + 26 * s;
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(c - sr, c - sr + 6 * s, 2 * sr, 2 * sr);
                using (PathGradientBrush pg = new PathGradientBrush(gp))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Positions = new float[] { 0f, 1f - discR / sr, 1f };
                    cb.Colors = new Color[] { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(105, 0, 0, 0), Color.FromArgb(105, 0, 0, 0) };
                    pg.InterpolationColors = cb;
                    pg.CenterPoint = new PointF(c, c + 6 * s);
                    g.FillPath(pg, gp);
                }
            }

            // glass disc
            RectangleF dr = new RectangleF(c - discR, c - discR, discR * 2, discR * 2);
            using (LinearGradientBrush lg = new LinearGradientBrush(dr, Color.FromArgb(240, 40, 44, 56), Color.FromArgb(244, 17, 19, 26), 90f))
                g.FillEllipse(lg, dr);
            using (Pen bp = new Pen(Color.FromArgb(58, 255, 255, 255), Math.Max(1f, 1.2f * s)))
                g.DrawEllipse(bp, dr);

            int n = opts.Count;
            float step = 360f / n;
            float ga = 3.1f;
            Color normal = Color.FromArgb(255, 54, 60, 75);
            Color action = Color.FromArgb(255, 42, 47, 60);
            for (int i = 0; i < n; i++)
            {
                WheelOption o = opts[i];
                float t = o.T;
                float mid = -90f + step * i;
                float a0 = mid - step / 2f + ga;
                float sweep = step - 2 * ga;
                float ro = ringO + grow * t - ins;
                float ri = ringI + ins;
                bool isText = o.Kind == OptKind.Rename || o.Kind == OptKind.Backup || o.Kind == OptKind.Restore;
                Color baseC = o.Kind == OptKind.Rename ? (o.Suggested ? Mix(normal, accent, 0.40f) : normal)
                            : (o.Kind == OptKind.Restore ? Mix(normal, accent, 0.40f) : action);
                Color col = Mix(baseC, accent, t);
                using (GraphicsPath sp = SectorPath(c, ri, ro, a0, sweep))
                using (SolidBrush sb = new SolidBrush(col))
                using (Pen pen = new Pen(col, ins * 2))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.FillPath(sb, sp);
                    g.DrawPath(pen, sp);
                }

                float lr = (ringI + ringO) / 2f + grow * t * 0.5f;
                double rad = mid * Math.PI / 180.0;
                float lx = c + (float)Math.Cos(rad) * lr, ly = c + (float)Math.Sin(rad) * lr;
                float chord = (float)(2 * lr * Math.Sin(Math.Min(step, 120f) * Math.PI / 360.0)) - 8 * s;
                float maxW = Math.Min(66 * s, chord);
                Color white = Color.FromArgb(255, 255, 255, 255);
                if (isText)
                {
                    Ui.Txt(g, o.Label, 15 * s, FontStyle.Bold, white, new RectangleF(lx - maxW / 2, ly - 12 * s, maxW, 24 * s), true, "Segoe UI", Al.Center);
                    if (o.Suggested)
                        Ui.Txt(g, "\u2605", 10 * s, FontStyle.Regular, Mix(accent, Color.White, 0.55f), new RectangleF(lx - 8 * s, ly - 24 * s, 16 * s, 12 * s), false, "Segoe UI Symbol", Al.Center);
                    if (i < 8)
                        Ui.Txt(g, (i + 1).ToString(), 9 * s, FontStyle.Regular, Color.FromArgb(120, 255, 255, 255), new RectangleF(lx - 8 * s, ly + 12 * s, 16 * s, 12 * s), false, "Segoe UI", Al.Center);
                }
                else
                {
                    string glyph = o.Kind == OptKind.Custom ? "+" : (o.Kind == OptKind.Manage ? "\u2699" : "\u2726");
                    Ui.Txt(g, glyph, 26 * s, FontStyle.Regular, Color.FromArgb(235, 255, 255, 255), new RectangleF(lx - 20 * s, ly - 18 * s, 40 * s, 36 * s), false, "Segoe UI Symbol", Al.Center);
                }
            }

            // center
            bool hv = hover >= 0 && hover < n;
            WheelOption ho = hv ? opts[hover] : null;
            RectangleF cr = new RectangleF(c - centerR, c - centerR, centerR * 2, centerR * 2);
            using (SolidBrush cbr = new SolidBrush(Color.FromArgb(252, 14, 16, 22))) g.FillEllipse(cbr, cr);
            using (Pen cp = new Pen(hv ? accent : Color.FromArgb(70, 255, 255, 255), 1.6f * s)) g.DrawEllipse(cp, cr);

            float tw = centerR * 2 - 16 * s;
            string line1; float px1; Color col1 = Color.White;
            if (ho != null && (ho.Kind == OptKind.Rename || ho.Kind == OptKind.Restore)) { line1 = (ho.Kind == OptKind.Restore ? "\u21A9 " : "\u2192 ") + ho.Label; px1 = 22 * s; col1 = Mix(accent, Color.White, 0.5f); }
            else if (ho != null) { line1 = ho.Hint; px1 = 15 * s; }
            else { line1 = ctx.SourceLabel; px1 = 24 * s; }
            Ui.Txt(g, line1, px1, FontStyle.Bold, col1, new RectangleF(c - tw / 2, c - 28 * s, tw, 30 * s), true, "Segoe UI", Al.Center);

            string line2 = ctx.Caption;
            if (ho != null && ctx.BaseName != null)
            {
                if (ho.Kind == OptKind.Rename) line2 = ctx.BaseName + ho.Label;
                else if (ho.Kind == OptKind.Restore && ctx.RestoreName != null) line2 = ctx.RestoreName;
                else if (ho.Kind == OptKind.Backup && ctx.BackupName != null) line2 = ctx.BackupName;
            }
            Ui.Txt(g, line2, 11.5f * s, FontStyle.Regular, Color.FromArgb(200, 200, 208, 222), new RectangleF(c - tw / 2, c + 3 * s, tw, 18 * s), false, "Segoe UI", Al.Center);

            if (ho == null && !string.IsNullOrEmpty(ctx.Status))
            {
                Color sc = ctx.StatusLevel == 2 ? Ui.Warn : (ctx.StatusLevel == 1 ? Ui.Ok : Color.FromArgb(170, 255, 255, 255));
                Ui.Txt(g, ctx.Status, 10.5f * s, FontStyle.Regular, sc, new RectangleF(c - tw / 2, c + 23 * s, tw, 16 * s), false, "Segoe UI", Al.Center);
            }
            else if (ho != null && ho.Kind == OptKind.Restore)
            {
                Ui.Txt(g, ho.Hint ?? "", 10.5f * s, FontStyle.Regular, Ui.Ok, new RectangleF(c - tw / 2, c + 23 * s, tw, 16 * s), false, "Segoe UI", Al.Center);
            }
            else if (ho != null && ho.Kind == OptKind.Rename && ho.Suggested)
            {
                Ui.Txt(g, "\u2605 " + (ho.Hint ?? ""), 10.5f * s, FontStyle.Regular, Ui.Warn, new RectangleF(c - tw / 2, c + 23 * s, tw, 16 * s), false, "Segoe UI", Al.Center);
            }
        }
    }

    // ------------------------------------------------------------------ toast
    internal sealed class ToastWindow : OverlayWindow
    {
        System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        Stopwatch sw = new Stopwatch();
        string title = "", sub = "", action;
        bool error, hoverCard, hoverAction, dirty;
        Action onAction;
        Color accent;
        float s = 1f;
        int w, h, pad, baseX, baseY, lifeMs;
        long extra;
        RectangleF actionRect;

        public ToastWindow()
        {
            tick.Interval = 16;
            tick.Tick += delegate { try { Step(); } catch (Exception ex) { Log.Write("toast: " + ex.Message); HideNow(); } };
        }

        public void ShowToast(string titleText, string subText, string actionText, Action actionCb, Color accentColor, bool isError, bool animations)
        {
            title = titleText; sub = subText; action = actionText; onAction = actionCb; accent = accentColor; error = isError;
            s = Ui.Scale;
            pad = (int)(26 * s);
            w = (int)(396 * s) + 2 * pad;
            h = (int)(84 * s) + 2 * pad;
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            baseX = wa.Left + (wa.Width - w) / 2;
            baseY = wa.Bottom - h + pad - (int)(18 * s);
            lifeMs = actionText != null ? 6500 : (isError ? 5500 : 3200);
            extra = 0; hoverCard = false; hoverAction = false;
            sw.Restart();
            EnsureHandle();
            Render(0f);
            if (!Visible) Show();
            tick.Start();
        }

        public void HideNow()
        {
            tick.Stop();
            if (Visible) Hide();
        }

        void Step()
        {
            long ms = sw.ElapsedMilliseconds;
            if (hoverCard) extra += 16;
            long end = lifeMs + extra;
            if (ms >= end) { HideNow(); return; }
            bool animating = ms < 200 || ms > end - 240;
            if (animating || dirty) Render(ms);
        }

        void Render(float ms)
        {
            dirty = false;
            long end = lifeMs + extra;
            float inT = Ease(ms / 200f);
            float outT = ms > end - 240 ? Math.Max(0f, (end - ms) / 240f) : 1f;
            float vis = Math.Min(inT, outT);
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    Draw(g);
                }
                Present(bmp, baseX, baseY + (int)((1f - inT) * 16 * s), (byte)(255 * vis));
            }
        }

        void Draw(Graphics g)
        {
            RectangleF card = new RectangleF(pad, pad, w - 2 * pad, h - 2 * pad);
            float rad = 18 * s;
            for (int i = 10; i >= 1; i--)
            {
                RectangleF r = card; r.Inflate(i * 1.6f * s, i * 1.6f * s); r.Offset(0, 3 * s);
                using (GraphicsPath p = Ui.Round(r, rad + i * 1.6f * s))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(6, 0, 0, 0))) g.FillPath(b, p);
            }
            using (GraphicsPath p = Ui.Round(card, rad))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 27, 30, 38))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(52, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
            Color ic = error ? Ui.Danger : accent;
            float cy = card.Top + card.Height / 2;
            bool ltr = !Loc.Rtl;
            float cx = ltr ? card.Left + 36 * s : card.Right - 36 * s;
            RectangleF circle = new RectangleF(cx - 17 * s, cy - 17 * s, 34 * s, 34 * s);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(48, ic))) g.FillEllipse(b, circle);
            Ui.Txt(g, error ? "!" : "\u2713", 19 * s, FontStyle.Bold, ic, circle, false, "Segoe UI Symbol", Al.Center);

            float textLeft, textRight;
            if (ltr) { textLeft = card.Left + 66 * s; textRight = card.Right - (action != null ? 104 * s : 18 * s); }
            else { textRight = card.Right - 66 * s; textLeft = card.Left + (action != null ? 104 * s : 18 * s); }
            Ui.Txt(g, title, 15 * s, FontStyle.Bold, Color.White, new RectangleF(textLeft, card.Top + 14 * s, textRight - textLeft, 26 * s), false, "Segoe UI", Al.End);
            Ui.Txt(g, sub, 12.5f * s, FontStyle.Regular, Color.FromArgb(185, 205, 210, 222), new RectangleF(textLeft, card.Top + 42 * s, textRight - textLeft, 22 * s), false, "Segoe UI", Al.End);

            if (action != null)
            {
                float ax = ltr ? card.Right - 14 * s - 78 * s : card.Left + 14 * s;
                actionRect = new RectangleF(ax, cy - 17 * s, 78 * s, 34 * s);
                using (GraphicsPath p = Ui.Round(actionRect, 10 * s))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(hoverAction ? 70 : 34, accent))) g.FillPath(b, p);
                Ui.Txt(g, action, 14 * s, FontStyle.Bold, Ui.Mix(accent, Color.White, 0.35f), actionRect, false, "Segoe UI", Al.Center);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool ha = action != null && actionRect.Contains(e.Location);
            if (!hoverCard || ha != hoverAction) { hoverCard = true; hoverAction = ha; dirty = true; }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverCard = false; hoverAction = false; dirty = true;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Action cb = null;
            if (action != null && actionRect.Contains(e.Location)) cb = onAction;
            HideNow();
            if (cb != null) cb();
            base.OnMouseUp(e);
        }
    }
}
