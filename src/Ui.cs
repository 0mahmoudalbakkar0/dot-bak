// .BAK - theme, custom controls, dialogs and the settings window
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BakApp
{
    // Visual alignment, written for the right-to-left (Arabic) layout:
    // End = right edge, Start = left edge. In English everything is mirrored automatically (see Ui.Txt).
    internal enum Al { Center, End, Start }

    internal static class Ui
    {
        public static float Scale = 1f;
        public static bool Dark = true;
        public static Color Accent = Color.FromArgb(76, 141, 255);
        public static Color Bg, Side, Card, Input, TextC, Muted, Border;
        public static readonly Color Danger = Color.FromArgb(255, 107, 107);
        public static readonly Color Warn = Color.FromArgb(255, 193, 77);
        public static readonly Color Ok = Color.FromArgb(82, 209, 140);

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float S(float v) { return v * Scale; }
        public static int Unscale(int px) { return (int)Math.Round(px / Scale); }

        public static void Init()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; } catch { Scale = 1f; }
            Refresh();
        }

        public static void Refresh()
        {
            Dark = SystemDark();
            Accent = ComputeAccent(Store.Config.Accent);
            if (Dark)
            {
                Bg = Color.FromArgb(0x14, 0x16, 0x1B); Side = Color.FromArgb(0x1A, 0x1D, 0x24); Card = Color.FromArgb(0x22, 0x26, 0x30);
                Input = Color.FromArgb(0x2A, 0x2F, 0x3B); TextC = Color.FromArgb(0xEE, 0xF0, 0xF4); Muted = Color.FromArgb(0x9A, 0xA3, 0xB2); Border = Color.FromArgb(0x34, 0x3A, 0x48);
            }
            else
            {
                Bg = Color.FromArgb(0xF3, 0xF4, 0xF7); Side = Color.FromArgb(0xE9, 0xEB, 0xF0); Card = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Input = Color.FromArgb(0xF0, 0xF2, 0xF6); TextC = Color.FromArgb(0x1B, 0x1E, 0x25); Muted = Color.FromArgb(0x63, 0x6B, 0x7A); Border = Color.FromArgb(0xDD, 0xE0, 0xE7);
            }
        }

        static bool SystemDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return Convert.ToInt32(v) == 0;
            }
            catch { return true; }
        }

        public static Color SystemAccent()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v != null)
                {
                    int c = Convert.ToInt32(v);
                    return Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                }
            }
            catch { }
            return Color.FromArgb(76, 141, 255);
        }

        static Color ComputeAccent(string s)
        {
            Color c;
            if (!string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#')
            {
                try { c = Color.FromArgb(Convert.ToInt32(s.Substring(1, 2), 16), Convert.ToInt32(s.Substring(3, 2), 16), Convert.ToInt32(s.Substring(5, 2), 16)); }
                catch { c = SystemAccent(); }
            }
            else c = SystemAccent();
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (lum < 90) c = Mix(c, Color.White, 0.38f);
            return c;
        }

        // Text colour that stays readable on top of the accent colour.
        public static Color OnAccent()
        {
            double lum = 0.299 * Accent.R + 0.587 * Accent.G + 0.114 * Accent.B;
            return lum > 150 ? Color.FromArgb(20, 22, 28) : Color.White;
        }

        public static Font F(float pt, FontStyle st) { return new Font("Segoe UI", pt, st, GraphicsUnit.Point); }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static bool HasArabic(string s)
        {
            if (s == null) return false;
            foreach (char c in s) if ((c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F)) return true;
            return false;
        }

        // Keeps a Latin fragment (like ".jpeg") intact inside an Arabic sentence. Not needed in English.
        public static string Ltr(string s) { return Loc.Rtl ? "‪" + s + "‬" : s; }

        static Al Flip(Al al)
        {
            if (Loc.Rtl) return al;
            if (al == Al.End) return Al.Start;
            if (al == Al.Start) return Al.End;
            return al;
        }

        public static void Txt(Graphics g, string text, float px, FontStyle style, Color col, RectangleF r, bool fit, string family, Al al)
        {
            if (string.IsNullOrEmpty(text)) return;
            al = Flip(al);
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                bool ar = HasArabic(text);
                if (ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                if (al == Al.Center) sf.Alignment = StringAlignment.Center;
                else if (al == Al.End) sf.Alignment = ar ? StringAlignment.Near : StringAlignment.Far;
                else sf.Alignment = ar ? StringAlignment.Far : StringAlignment.Near;
                float size = px;
                Font f = new Font(family, size, style, GraphicsUnit.Pixel);
                if (fit)
                {
                    while (size > 8 && g.MeasureString(text, f, 10000, sf).Width > r.Width)
                    {
                        f.Dispose();
                        size -= 0.75f;
                        f = new Font(family, size, style, GraphicsUnit.Pixel);
                    }
                }
                using (f)
                using (SolidBrush b = new SolidBrush(col))
                    g.DrawString(text, f, b, r, sf);
            }
        }

        public static void Para(Graphics g, string text, float px, FontStyle style, Color col, RectangleF r, Al al)
        {
            if (string.IsNullOrEmpty(text)) return;
            al = Flip(al);
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Near;
                bool ar = HasArabic(text);
                if (ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                if (al == Al.Center) sf.Alignment = StringAlignment.Center;
                else if (al == Al.End) sf.Alignment = ar ? StringAlignment.Near : StringAlignment.Far;
                else sf.Alignment = ar ? StringAlignment.Far : StringAlignment.Near;
                using (Font f = new Font("Segoe UI", px, style, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(col))
                    g.DrawString(text, f, b, r, sf);
            }
        }

        public static int ParaHeight(string text, float px, int width)
        {
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                if (HasArabic(text)) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                return (int)Math.Ceiling(g.MeasureString(text ?? "", f, width, sf).Height) + 4;
            }
        }

        public static void ForceFront(Form f)
        {
            try
            {
                Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                Native.SetForegroundWindow(f.Handle);
                f.Activate();
            }
            catch { }
        }

        public static void DarkTitle(Form f)
        {
            try
            {
                int v = Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(f.Handle, 20, ref v, 4);
                Native.DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            }
            catch { }
        }

        public static void DrawLogo(Graphics g, RectangleF r, Color accent)
        {
            float c = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, R = Math.Min(r.Width, r.Height) / 2f;
            float ro = R * 0.96f, ri = R * 0.40f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = -90f + 60f * i + 4f;
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddArc(c - ro, cy - ro, 2 * ro, 2 * ro, a0, 52f);
                    p.AddArc(c - ri, cy - ri, 2 * ri, 2 * ri, a0 + 52f, -52f);
                    p.CloseFigure();
                    Color col = i == 0 ? accent : Color.FromArgb(i == 1 || i == 5 ? 215 : 170, Mix(accent, Color.White, 0.25f));
                    using (SolidBrush b = new SolidBrush(col)) g.FillPath(b, p);
                }
            }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("open url: " + ex.Message); }
        }
    }

    // ------------------------------------------------------------------ keyboard shortcuts
    internal static class Hotkeys
    {
        public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4;

        public static string KeyName(uint vk)
        {
            Keys k = (Keys)vk;
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (int)(k - Keys.D0))).ToString();
            switch (k)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Back: return "Backspace";
                case Keys.Return: return "Enter";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
            }
            return k.ToString();
        }

        public static string Format(uint mods, uint vk)
        {
            List<string> parts = new List<string>();
            if ((mods & ModCtrl) != 0) parts.Add("Ctrl");
            if ((mods & ModAlt) != 0) parts.Add("Alt");
            if ((mods & ModShift) != 0) parts.Add("Shift");
            if (vk != 0) parts.Add(KeyName(vk));
            return string.Join(" + ", parts.ToArray());
        }

        // Returns null when the combination may be used, otherwise the key of the error text.
        public static string Validate(uint mods, uint vk)
        {
            if (vk == 0 || (mods & ~(ModAlt | ModCtrl | ModShift)) != 0) return "hk.err.nomod";
            if (mods == 0) return "hk.err.nomod";
            bool ctrl = (mods & ModCtrl) != 0, alt = (mods & ModAlt) != 0, shift = (mods & ModShift) != 0;
            if (!alt && !(ctrl && shift)) return "hk.err.risky";   // plain Ctrl+key / Shift+key would break normal typing and shortcuts
            Keys k = (Keys)vk;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return "hk.err.nomod";
            if (alt && (k == Keys.F4 || k == Keys.Space || k == Keys.Tab || k == Keys.Escape)) return "hk.err.reserved";
            return null;
        }
    }

    // ------------------------------------------------------------------ controls
    internal class UiControl : Control
    {
        public UiControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }
        protected Color ParentBg { get { return Parent != null ? Parent.BackColor : Ui.Bg; } }
    }

    internal sealed class Lbl : UiControl
    {
        public float Px = 14;
        public FontStyle Style = FontStyle.Regular;
        public int Role;          // 0 text, 1 muted, 2 accent, 3 danger, 4 warning, 5 ok
        public Al Align = Al.End;
        public bool Wrap;

        public override string Text
        {
            get { return base.Text; }
            set { base.Text = value; Invalidate(); }
        }

        public void Set(string text, int role)
        {
            Role = role;
            Text = text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBg);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color c = Ui.TextC;
            if (Role == 1) c = Ui.Muted; else if (Role == 2) c = Ui.Accent; else if (Role == 3) c = Ui.Danger; else if (Role == 4) c = Ui.Warn; else if (Role == 5) c = Ui.Ok;
            RectangleF r = new RectangleF(0, 0, Width, Height);
            if (Wrap) Ui.Para(g, Text, Px * Ui.Scale, Style, c, r, Align);
            else Ui.Txt(g, Text, Px * Ui.Scale, Style, c, r, false, "Segoe UI", Align);
        }
    }

    internal enum BtnKind { Primary, Secondary, Danger, Ghost, Nav }

    internal sealed class UiButton : UiControl
    {
        public BtnKind Kind = BtnKind.Secondary;
        public bool Selected;
        public string Glyph;
        bool hover, down;

        public UiButton() { Cursor = Cursors.Hand; Height = Ui.S(38); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color bg = ParentBg;
            g.Clear(bg);
            Color fill = bg, fg = Ui.TextC, line = Color.Transparent;
            bool live = Cursor == Cursors.Hand;   // a "display only" chip uses the normal cursor and does not react to the mouse
            switch (Kind)
            {
                case BtnKind.Primary:
                    fill = Ui.Accent;
                    fg = Ui.OnAccent();
                    if (down) fill = Ui.Mix(fill, Color.Black, 0.15f); else if (hover) fill = Ui.Mix(fill, Color.White, 0.12f);
                    break;
                case BtnKind.Secondary:
                    fill = hover && live ? Ui.Mix(Ui.Input, Ui.TextC, 0.08f) : Ui.Input; line = Ui.Border; break;
                case BtnKind.Danger:
                    fill = Ui.Mix(bg, Ui.Danger, hover ? 0.28f : 0.18f); fg = Ui.Danger; break;
                case BtnKind.Ghost:
                    fill = hover ? Ui.Mix(bg, Ui.TextC, 0.07f) : bg; fg = hover ? Ui.TextC : Ui.Muted; break;
                case BtnKind.Nav:
                    fill = Selected ? Ui.Mix(bg, Ui.Accent, 0.22f) : (hover ? Ui.Mix(bg, Ui.TextC, 0.06f) : bg);
                    fg = Selected ? Ui.TextC : Ui.Muted; break;
            }
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Round(r, Ui.S(9f)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (line != Color.Transparent) using (Pen pen = new Pen(line)) g.DrawPath(pen, p);
            }
            float px = 14f * Ui.Scale;
            if (Kind == BtnKind.Nav)
            {
                bool rtl = Loc.Rtl;
                if (Selected)
                    using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, rtl ? Width - Ui.S(3) : 0, Height * 0.25f, Ui.S(3), Height * 0.5f);
                RectangleF glyphR = rtl ? new RectangleF(Width - Ui.S(46), 0, Ui.S(32), Height) : new RectangleF(Ui.S(14), 0, Ui.S(32), Height);
                RectangleF textR = rtl ? new RectangleF(Ui.S(8), 0, Width - Ui.S(58), Height) : new RectangleF(Ui.S(50), 0, Width - Ui.S(58), Height);
                Ui.Txt(g, Glyph, 18f * Ui.Scale, FontStyle.Regular, fg, glyphR, false, "Segoe UI Symbol", Al.Center);
                Ui.Txt(g, Text, px, Selected ? FontStyle.Bold : FontStyle.Regular, fg, textR, false, "Segoe UI", Al.End);
            }
            else
                Ui.Txt(g, Text, px, FontStyle.Bold, fg, new RectangleF(0, 0, Width, Height), true, "Segoe UI", Al.Center);
        }
    }

    internal sealed class Toggle : UiControl
    {
        bool on;
        public event EventHandler Changed;
        public bool Checked { get { return on; } set { if (on != value) { on = value; Invalidate(); } } }

        public Toggle() { Cursor = Cursors.Hand; Size = new Size(Ui.S(46), Ui.S(26)); }

        protected override void OnClick(EventArgs e)
        {
            on = !on;
            Invalidate();
            base.OnClick(e);
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color track = on ? Ui.Accent : Ui.Mix(Ui.Input, Ui.Muted, 0.45f);
            using (GraphicsPath p = Ui.Round(r, r.Height / 2f))
            using (SolidBrush b = new SolidBrush(track)) g.FillPath(b, p);
            float d = Height - Ui.S(7);
            float x = on ? Width - d - Ui.S(4) : Ui.S(4);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, x, (Height - d) / 2f, d, d);
        }
    }

    internal sealed class Slider : UiControl
    {
        public int Min = 250, Max = 900, Step = 50;
        int val = 450;
        bool drag;
        public event EventHandler Changed;
        public int Value { get { return val; } set { val = Math.Max(Min, Math.Min(Max, value)); Invalidate(); } }

        public Slider() { Cursor = Cursors.Hand; }

        void SetFromX(int x)
        {
            float pad = Ui.S(10f);
            float t = (x - pad) / Math.Max(1f, Width - 2 * pad);
            t = Math.Max(0f, Math.Min(1f, t));
            int v = Min + (int)Math.Round((Max - Min) * t / Step) * Step;
            v = Math.Max(Min, Math.Min(Max, v));
            if (v != val) { val = v; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg);
            float pad = Ui.S(10f), cy = Height / 2f, th = Ui.S(5f);
            float t = (val - Min) / (float)(Max - Min);
            float tx = pad + (Width - 2 * pad) * t;
            using (SolidBrush b = new SolidBrush(Ui.Mix(Ui.Input, Ui.Muted, 0.35f))) g.FillRectangle(b, pad, cy - th / 2, Width - 2 * pad, th);
            using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, pad, cy - th / 2, tx - pad, th);
            float d = Ui.S(18f);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, tx - d / 2, cy - d / 2, d, d);
            using (Pen pen = new Pen(Ui.Accent, Ui.S(2f))) g.DrawEllipse(pen, tx - d / 2, cy - d / 2, d, d);
        }
    }

    // Segmented choice (2-3 options side by side).
    internal sealed class Seg : UiControl
    {
        public string[] Items = new string[0];
        int idx;
        public event EventHandler Changed;
        public int Index { get { return idx; } set { if (idx != value) { idx = value; Invalidate(); } } }

        public Seg() { Cursor = Cursors.Hand; Height = Ui.S(40); }

        int VisualIndex(int i) { return Loc.Rtl ? Items.Length - 1 - i : i; }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Items.Length > 0)
            {
                float w = Width / (float)Items.Length;
                int vi = Math.Max(0, Math.Min(Items.Length - 1, (int)(e.X / w)));
                int i = Loc.Rtl ? Items.Length - 1 - vi : vi;
                if (i != idx)
                {
                    idx = i;
                    Invalidate();
                    if (Changed != null) Changed(this, EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(ParentBg);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Round(r, Ui.S(10f)))
            {
                using (SolidBrush b = new SolidBrush(Ui.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
            }
            if (Items.Length == 0) return;
            float w = (Width - Ui.S(8f)) / Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF cell = new RectangleF(Ui.S(4f) + VisualIndex(i) * w, Ui.S(4f), w, Height - Ui.S(8f));
                bool on = i == idx;
                if (on)
                    using (GraphicsPath p = Ui.Round(cell, Ui.S(8f)))
                    using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
                Ui.Txt(g, Items[i], 14f * Ui.Scale, on ? FontStyle.Bold : FontStyle.Regular, on ? Ui.OnAccent() : Ui.Muted, cell, true, "Segoe UI", Al.Center);
            }
        }
    }

    internal sealed class Swatches : UiControl
    {
        static readonly string[] Values = { "system", "#4C8DFF", "#8B5CF6", "#EC4899", "#F59E0B", "#10B981", "#EF4444" };
        string val = "system";
        public event EventHandler Changed;
        public string Value { get { return val; } set { val = value; Invalidate(); } }

        public Swatches() { Cursor = Cursors.Hand; }

        float D { get { return Ui.S(26f); } }
        float Gap { get { return Ui.S(12f); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = (int)((e.X - Ui.S(3f)) / (D + Gap));
            if (i >= 0 && i < Values.Length && (e.X - Ui.S(3f)) - i * (D + Gap) <= D && Values[i] != val)
            {
                val = Values[i];
                Invalidate();
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg);
            float y = (Height - D) / 2f;
            for (int i = 0; i < Values.Length; i++)
            {
                float x = i * (D + Gap) + Ui.S(3f);
                Color c = i == 0 ? Ui.SystemAccent() : ColorTranslator.FromHtml(Values[i]);
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, x, y, D, D);
                if (i == 0) using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.FillEllipse(b, x + D / 2 - Ui.S(3f), y + D / 2 - Ui.S(3f), Ui.S(6f), Ui.S(6f));
                if (Values[i] == val) using (Pen pen = new Pen(Ui.TextC, Ui.S(2f))) g.DrawEllipse(pen, x - Ui.S(3f), y - Ui.S(3f), D + Ui.S(6f), D + Ui.S(6f));
            }
        }
    }

    internal sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Round(r, Ui.S(14f)))
            {
                using (SolidBrush b = new SolidBrush(Ui.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
            }
        }
    }

    internal sealed class InputBox : Control
    {
        public TextBox Box = new TextBox();
        bool focus;

        public InputBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(38);
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Ui.F(10.5f, FontStyle.Regular);
            Box.BackColor = Ui.Input;
            Box.ForeColor = Ui.TextC;
            Box.RightToLeft = RightToLeft.No;
            Controls.Add(Box);
            Box.GotFocus += delegate { focus = true; Invalidate(); };
            Box.LostFocus += delegate { focus = false; Invalidate(); };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Ui.S(12);
            int bh = Box.PreferredHeight;
            Box.SetBounds(pad, Math.Max(0, (Height - bh) / 2), Math.Max(10, Width - 2 * pad), bh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Round(r, Ui.S(9f)))
            {
                using (SolidBrush b = new SolidBrush(Ui.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(focus ? Ui.Accent : Ui.Border, focus ? 1.6f : 1f)) g.DrawPath(pen, p);
            }
        }
    }

    internal sealed class ListItem
    {
        public string Key;
        public string Text;
        public string Sub;
        public override string ToString() { return Text; }
    }

    internal sealed class ThemedList : ListBox
    {
        public ThemedList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            ItemHeight = Ui.S(38);
            BackColor = Ui.Card;
            DoubleBuffered = true;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            RectangleF r = new RectangleF(e.Bounds.X + Ui.S(4), e.Bounds.Y + Ui.S(2), e.Bounds.Width - Ui.S(8), e.Bounds.Height - Ui.S(4));
            if (sel)
                using (GraphicsPath p = Ui.Round(r, Ui.S(8f)))
                using (SolidBrush b = new SolidBrush(Ui.Mix(BackColor, Ui.Accent, 0.30f)))
                    g.FillPath(b, p);
            ListItem it = Items[e.Index] as ListItem;
            string text = it != null ? it.Text : Convert.ToString(Items[e.Index]);
            string sub = it != null ? it.Sub : null;
            float pad = Ui.S(12f), subW = Ui.S(90f);
            bool rtl = Loc.Rtl;
            RectangleF textR = rtl ? new RectangleF(r.X + pad + subW, r.Y, r.Width - 2 * pad - subW, r.Height)
                                   : new RectangleF(r.X + pad, r.Y, r.Width - 2 * pad - subW, r.Height);
            RectangleF subR = rtl ? new RectangleF(r.X + pad, r.Y, subW, r.Height)
                                  : new RectangleF(r.Right - pad - subW, r.Y, subW, r.Height);
            Ui.Txt(g, text, Ui.S(15f), FontStyle.Regular, Ui.TextC, textR, false, "Segoe UI", Al.End);
            if (!string.IsNullOrEmpty(sub))
                Ui.Txt(g, sub, Ui.S(12f), FontStyle.Regular, Ui.Muted, subR, false, "Segoe UI", Al.Start);
        }
    }

    // ------------------------------------------------------------------ dialogs
    internal sealed class DialogForm : Form
    {
        public bool Accepted;
        public string Value = "";
        public bool Checked;
        readonly InputBox input;
        readonly Toggle chk;
        readonly Lbl err;
        readonly Func<string, string> validate;

        public DialogForm(string title, string message, string yes, string no, bool danger, bool prompt, string initial, string checkText, Func<string, string> validator)
        {
            validate = validator;
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Ui.Bg;
            KeyPreview = true;
            Font = Ui.F(10, FontStyle.Regular);
            bool rtl = Loc.Rtl;

            int P = Ui.S(24), Wd = Ui.S(460), cw = Wd - 2 * P;
            int y = P;
            int mh = Ui.ParaHeight(message, 14f * Ui.Scale, cw);
            Lbl m = new Lbl(); m.Px = 14; m.Wrap = true; m.Text = message; m.Bounds = new Rectangle(P, y, cw, mh);
            Controls.Add(m);
            y += mh + Ui.S(14);

            if (prompt)
            {
                input = new InputBox();
                input.Bounds = new Rectangle(P, y, cw, Ui.S(38));
                input.Box.Text = initial ?? "";
                Controls.Add(input);
                y += Ui.S(38) + Ui.S(6);
                err = new Lbl(); err.Px = 12; err.Role = 3; err.Bounds = new Rectangle(P, y, cw, Ui.S(20));
                Controls.Add(err);
                y += Ui.S(24);
            }
            if (checkText != null)
            {
                chk = new Toggle(); chk.Checked = true;
                chk.Location = new Point(rtl ? Wd - P - chk.Width : P, y);
                Controls.Add(chk);
                Lbl cl = new Lbl(); cl.Px = 13.5f; cl.Text = checkText;
                int lw = cw - chk.Width - Ui.S(10);
                cl.Bounds = rtl ? new Rectangle(P, y, lw, chk.Height) : new Rectangle(P + chk.Width + Ui.S(10), y, lw, chk.Height);
                Controls.Add(cl);
                y += chk.Height + Ui.S(16);
            }
            y += Ui.S(4);
            int bw = Ui.S(130), nw = Ui.S(110), gap = Ui.S(10);
            UiButton by = new UiButton(); by.Text = yes; by.Kind = danger ? BtnKind.Danger : BtnKind.Primary;
            by.Bounds = rtl ? new Rectangle(P, y, bw, Ui.S(40)) : new Rectangle(Wd - P - bw, y, bw, Ui.S(40));
            by.Click += delegate { TryAccept(); };
            Controls.Add(by);
            if (no != null)
            {
                UiButton bn = new UiButton(); bn.Text = no; bn.Kind = BtnKind.Secondary;
                bn.Bounds = rtl ? new Rectangle(P + bw + gap, y, nw, Ui.S(40)) : new Rectangle(Wd - P - bw - gap - nw, y, nw, Ui.S(40));
                bn.Click += delegate { Close(); };
                Controls.Add(bn);
            }
            ClientSize = new Size(Wd, y + Ui.S(40) + P);
            HandleCreated += delegate { Ui.DarkTitle(this); };
            Shown += delegate { Ui.ForceFront(this); if (input != null) input.Box.Focus(); };
        }

        void TryAccept()
        {
            if (input != null)
            {
                string v = input.Box.Text;
                if (validate != null)
                {
                    string msg = validate(v);
                    if (msg != null) { err.Text = msg; return; }
                }
                Value = v;
                Checked = chk != null && chk.Checked;
            }
            Accepted = true;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TryAccept(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            base.OnKeyDown(e);
        }
    }

    internal static class Dialogs
    {
        public static bool Confirm(string title, string message, string yes, string no, bool danger)
        {
            using (DialogForm f = new DialogForm(title, message, yes, no, danger, false, null, null, null))
            {
                f.ShowDialog();
                return f.Accepted;
            }
        }

        public static string Prompt(string title, string message, string initial, string checkText, Func<string, string> validate, out bool check)
        {
            using (DialogForm f = new DialogForm(title, message, Loc.T("btn.ok"), Loc.T("btn.cancel"), false, true, initial, checkText, validate))
            {
                f.ShowDialog();
                check = f.Checked;
                return f.Accepted ? f.Value : null;
            }
        }
    }

    // Captures a key combination: press it, see it, confirm.
    internal sealed class ShortcutDialog : Form
    {
        public bool Accepted;
        public uint Mods, Vk;
        readonly Lbl chip;
        readonly Lbl err;

        public ShortcutDialog(string title, uint mods, uint vk)
        {
            Mods = mods; Vk = vk;
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Ui.Bg;
            Font = Ui.F(10, FontStyle.Regular);
            bool rtl = Loc.Rtl;

            int P = Ui.S(24), Wd = Ui.S(440), cw = Wd - 2 * P;
            int y = P;
            string msg = Loc.T("hk.dlg.msg");
            int mh = Ui.ParaHeight(msg, 14f * Ui.Scale, cw);
            Lbl m = new Lbl(); m.Px = 14; m.Wrap = true; m.Text = msg; m.Bounds = new Rectangle(P, y, cw, mh);
            Controls.Add(m);
            y += mh + Ui.S(14);

            Card box = new Card(); box.Bounds = new Rectangle(P, y, cw, Ui.S(64));
            Controls.Add(box);
            chip = new Lbl(); chip.Px = 26; chip.Style = FontStyle.Bold; chip.Role = 2; chip.Align = Al.Center;
            chip.Bounds = new Rectangle(Ui.S(8), Ui.S(8), cw - Ui.S(16), Ui.S(48));
            chip.Text = Hotkeys.Format(mods, vk);
            box.Controls.Add(chip);
            y += Ui.S(64) + Ui.S(8);

            err = new Lbl(); err.Px = 12; err.Role = 3; err.Bounds = new Rectangle(P, y, cw, Ui.S(20));
            Controls.Add(err);
            y += Ui.S(30);

            int bw = Ui.S(130), nw = Ui.S(110), gap = Ui.S(10);
            UiButton ok = new UiButton(); ok.Text = Loc.T("btn.ok"); ok.Kind = BtnKind.Primary;
            ok.Bounds = rtl ? new Rectangle(P, y, bw, Ui.S(40)) : new Rectangle(Wd - P - bw, y, bw, Ui.S(40));
            ok.Click += delegate { TryAccept(); };
            Controls.Add(ok);
            UiButton cancel = new UiButton(); cancel.Text = Loc.T("btn.cancel"); cancel.Kind = BtnKind.Secondary;
            cancel.Bounds = rtl ? new Rectangle(P + bw + gap, y, nw, Ui.S(40)) : new Rectangle(Wd - P - bw - gap - nw, y, nw, Ui.S(40));
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            ClientSize = new Size(Wd, y + Ui.S(40) + P);
            HandleCreated += delegate { Ui.DarkTitle(this); };
            Shown += delegate { Ui.ForceFront(this); };
        }

        void TryAccept()
        {
            string e = Hotkeys.Validate(Mods, Vk);
            if (e != null) { err.Text = Loc.T(e); return; }
            Accepted = true;
            Close();
        }

        // Every key goes through here first, so Tab, Enter and Alt combinations can be captured too.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Hotkeys.ModCtrl;
            if ((keyData & Keys.Alt) != 0) mods |= Hotkeys.ModAlt;
            if ((keyData & Keys.Shift) != 0) mods |= Hotkeys.ModShift;

            if (key == Keys.Escape && mods == 0) { Close(); return true; }
            err.Text = "";
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin || key == Keys.None)
            {
                chip.Text = Hotkeys.Format(mods, 0) + (mods != 0 ? " + …" : "");
                return true;
            }
            Mods = mods; Vk = (uint)key;
            chip.Text = Hotkeys.Format(mods, (uint)key);
            string v = Hotkeys.Validate(mods, (uint)key);
            if (v != null) err.Text = Loc.T(v);
            return true;
        }
    }

    // ------------------------------------------------------------------ settings window
    internal sealed class SettingsForm : Form
    {
        const int PageCount = 6;
        const int PgExt = 0, PgGeneral = 1, PgControls = 2, PgLook = 3, PgHistory = 4, PgAbout = 5;

        readonly TrayApp app;
        readonly Panel[] pages = new Panel[PageCount];
        readonly UiButton[] navs = new UiButton[PageCount];
        ThemedList srcList, tgtList, histList;
        Lbl tgtHeader, errLbl, histEmpty, holdValue, statusLbl, hkErr;
        InputBox tgtInput;
        UiButton chipOpen, chipUndo;
        Lbl[] aboutLines = new Lbl[4];
        bool loading;

        // Positions are written for the right-to-left layout and mirrored here for English.
        static Rectangle R(Control parent, int x, int y, int w, int h)
        {
            int pw = Ui.Unscale(parent.ClientSize.Width);
            int xx = Loc.Rtl ? x : pw - x - w;
            return new Rectangle(Ui.S(xx), Ui.S(y), Ui.S(w), Ui.S(h));
        }

        static Lbl L(Control p, string text, int x, int y, int w, int h, float px, FontStyle st, int role, Al al)
        {
            Lbl l = new Lbl(); l.Text = text; l.Px = px; l.Style = st; l.Role = role; l.Align = al; l.Bounds = R(p, x, y, w, h);
            p.Controls.Add(l);
            return l;
        }

        static UiButton B(Control p, string text, int x, int y, int w, int h, BtnKind k, EventHandler click)
        {
            UiButton b = new UiButton(); b.Text = text; b.Kind = k; b.Bounds = R(p, x, y, w, h);
            if (click != null) b.Click += click;
            p.Controls.Add(b);
            return b;
        }

        public SettingsForm(TrayApp trayApp)
        {
            app = trayApp;
            Text = AppInfo.Name + " — " + Loc.T("settings.title");
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(860), Ui.S(640));
            BackColor = Ui.Bg;
            Font = Ui.F(10, FontStyle.Regular);
            if (app.AppIcon != null) Icon = app.AppIcon;
            HandleCreated += delegate { Ui.DarkTitle(this); };

            for (int i = 0; i < PageCount; i++)
            {
                Panel pg = new Panel(); pg.Bounds = R(this, 0, 0, 650, 580); pg.BackColor = Ui.Bg; pg.Visible = i == 0;
                pages[i] = pg;
                Controls.Add(pg);
            }
            BuildSidebar();
            BuildBar();
            BuildExtensionsPage(pages[PgExt]);
            BuildGeneralPage(pages[PgGeneral]);
            BuildControlsPage(pages[PgControls]);
            BuildLookPage(pages[PgLook]);
            BuildHistoryPage(pages[PgHistory]);
            BuildAboutPage(pages[PgAbout]);
            RefreshSources(null);
            ShowPage(0, null);
        }

        // ---- shared
        void Changed()
        {
            Store.Save();
            statusLbl.Set(Loc.T("save.auto"), 1);
        }

        void SaveNow()
        {
            Store.Save();
            Store.SaveHistory();
            Startup.Set(Store.Config.AutoStart);
            statusLbl.Set(Loc.F("save.done", DateTime.Now.ToString("HH:mm:ss")), 5);
            app.Notify(Loc.T("save.toast.title"), Loc.T("save.toast.sub"));
        }

        void BuildSidebar()
        {
            Panel side = new Panel(); side.Bounds = R(this, 650, 0, 210, 640); side.BackColor = Ui.Side;
            side.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int lx = Loc.Rtl ? 152 : 14;
                Ui.DrawLogo(e.Graphics, new RectangleF(Ui.S(lx), Ui.S(24), Ui.S(44), Ui.S(44)), Ui.Accent);
            };
            Controls.Add(side);
            L(side, AppInfo.Name, 8, 22, 138, 28, 20, FontStyle.Bold, 0, Al.End);
            L(side, Loc.T("app.subtitle"), 8, 48, 138, 20, 12, FontStyle.Regular, 1, Al.End);
            string[] names = { Loc.T("nav.ext"), Loc.T("nav.general"), Loc.T("nav.controls"), Loc.T("nav.look"), Loc.T("nav.history"), Loc.T("nav.about") };
            string[] glyphs = { "⇄", "⚙", "⌨", "◑", "↶", "ⓘ" };
            for (int i = 0; i < PageCount; i++)
            {
                int idx = i;
                UiButton b = B(side, names[i], 12, 100 + i * 52, 186, 44, BtnKind.Nav, delegate { ShowPage(idx, null); });
                b.Glyph = glyphs[i];
                navs[i] = b;
            }
            L(side, "v" + AppInfo.Version, 12, 604, 186, 20, 11.5f, FontStyle.Regular, 1, Al.End);
        }

        void BuildBar()
        {
            Panel bar = new Panel(); bar.Bounds = R(this, 0, 580, 650, 60); bar.BackColor = Ui.Bg;
            bar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Ui.Border)) e.Graphics.DrawLine(pen, Ui.S(20), 0, bar.Width - Ui.S(20), 0);
            };
            Controls.Add(bar);
            B(bar, Loc.T("save.btn"), 28, 10, 170, 42, BtnKind.Primary, delegate { SaveNow(); });
            statusLbl = L(bar, Loc.T("save.auto"), 214, 18, 408, 26, 13, FontStyle.Regular, 1, Al.End);
        }

        public void ShowPage(int i, string sourceKey)
        {
            if (i < 0 || i >= PageCount) i = 0;
            for (int k = 0; k < PageCount; k++)
            {
                pages[k].Visible = k == i;
                navs[k].Selected = k == i;
                navs[k].Invalidate();
            }
            if (i == PgExt && sourceKey != null && Store.Config.Profiles.ContainsKey(sourceKey)) RefreshSources(sourceKey);
            if (i == PgHistory) RefreshHistory();
            if (i == PgAbout) RefreshAbout();
        }

        // ---- page: extensions
        void BuildExtensionsPage(Panel p)
        {
            L(p, Loc.T("ext.title"), 28, 22, 594, 36, 26, FontStyle.Bold, 0, Al.End);
            L(p, Loc.T("ext.desc"), 28, 60, 594, 22, 13.5f, FontStyle.Regular, 1, Al.End);

            Card cs = new Card(); cs.Bounds = R(p, 392, 100, 230, 380); p.Controls.Add(cs);
            L(cs, Loc.T("ext.src"), 16, 12, 198, 22, 13, FontStyle.Bold, 1, Al.End);
            srcList = new ThemedList(); srcList.Bounds = R(cs, 8, 40, 214, 276); cs.Controls.Add(srcList);
            srcList.SelectedIndexChanged += delegate { if (!loading) RefreshTargets(null); };
            B(cs, Loc.T("ext.src.add"), 84, 328, 132, 38, BtnKind.Secondary, delegate { AddSource(); });
            B(cs, Loc.T("btn.delete"), 14, 328, 64, 38, BtnKind.Danger, delegate { DelSource(); });

            Card ct = new Card(); ct.Bounds = R(p, 28, 100, 348, 380); p.Controls.Add(ct);
            tgtHeader = L(ct, "", 16, 12, 316, 22, 13, FontStyle.Bold, 1, Al.End);
            tgtList = new ThemedList(); tgtList.Bounds = R(ct, 8, 40, 284, 196); ct.Controls.Add(tgtList);
            B(ct, "▲", 298, 40, 42, 38, BtnKind.Secondary, delegate { MoveTarget(-1); });
            B(ct, "▼", 298, 84, 42, 38, BtnKind.Secondary, delegate { MoveTarget(1); });
            tgtInput = new InputBox(); tgtInput.Bounds = R(ct, 108, 248, 228, 38); ct.Controls.Add(tgtInput);
            tgtInput.Box.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddTarget(); } };
            B(ct, Loc.T("btn.add"), 12, 248, 88, 38, BtnKind.Primary, delegate { AddTarget(); });
            errLbl = L(ct, "", 16, 292, 316, 20, 12, FontStyle.Regular, 3, Al.End);
            B(ct, Loc.T("ext.tgt.remove"), 226, 328, 110, 38, BtnKind.Danger, delegate { RemoveTarget(); });
            B(ct, Loc.T("ext.reset"), 12, 328, 190, 38, BtnKind.Ghost, delegate { ResetDefaults(); });

            L(p, "⚠ " + Loc.T("ext.warn"), 28, 494, 594, 22, 13, FontStyle.Regular, 4, Al.End);
            L(p, Loc.T("ext.note"), 28, 518, 594, 22, 12.5f, FontStyle.Regular, 1, Al.End);
        }

        string CurKey()
        {
            ListItem it = srcList.SelectedItem as ListItem;
            return it != null ? it.Key : null;
        }

        void RefreshSources(string select)
        {
            loading = true;
            srcList.BeginUpdate();
            srcList.Items.Clear();
            Dictionary<string, List<string>> prof = Store.Config.Profiles;
            List<string> keys = prof.Keys.OrderBy(k => k == Ext.None ? "~" : k, StringComparer.OrdinalIgnoreCase).ToList();
            int sel = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                ListItem it = new ListItem();
                it.Key = keys[i]; it.Text = Ext.Label(keys[i]); it.Sub = prof[keys[i]].Count.ToString();
                srcList.Items.Add(it);
                if (keys[i] == select) sel = i;
            }
            srcList.EndUpdate();
            loading = false;
            if (srcList.Items.Count > 0) srcList.SelectedIndex = sel;
            RefreshTargets(null);
        }

        void RefreshTargets(string selectKey)
        {
            tgtList.Items.Clear();
            errLbl.Text = "";
            string k = CurKey();
            if (k == null) { tgtHeader.Text = Loc.T("ext.pick"); return; }
            tgtHeader.Text = Loc.F("ext.tgt.header", Ui.Ltr(Ext.Label(k)));
            int sel = -1;
            foreach (string t in Store.Config.Profiles[k])
            {
                ListItem it = new ListItem(); it.Key = t; it.Text = t;
                int u = Store.Config.UsageOf(k, t);
                it.Sub = u > 0 ? "×" + u : "";
                if (t == selectKey) sel = tgtList.Items.Count;
                tgtList.Items.Add(it);
            }
            if (sel >= 0) tgtList.SelectedIndex = sel;
        }

        void AddSource()
        {
            bool chk;
            string v = Dialogs.Prompt(Loc.T("src.new.title"), Loc.T("src.new.msg"), "", null,
                delegate(string x) { string k; return Ext.TryNormSource(x, out k) ? null : Loc.T("err.badext"); }, out chk);
            if (v == null) return;
            string key;
            if (!Ext.TryNormSource(v, out key)) return;
            if (!Store.Config.Profiles.ContainsKey(key)) Store.Config.Profiles[key] = new List<string>();
            Changed();
            RefreshSources(key);
        }

        void DelSource()
        {
            string k = CurKey();
            if (k == null) return;
            if (!Dialogs.Confirm(Loc.T("src.del.title"), Loc.F("src.del.msg", Ui.Ltr(Ext.Label(k))), Loc.T("btn.delete"), Loc.T("btn.cancel"), true)) return;
            Store.Config.Profiles.Remove(k);
            Changed();
            RefreshSources(null);
        }

        void AddTarget()
        {
            errLbl.Text = "";
            string k = CurKey();
            if (k == null) { errLbl.Text = Loc.T("err.pickfirst"); return; }
            string t;
            if (!Ext.TryNormTarget(tgtInput.Box.Text, out t)) { errLbl.Text = Loc.T("err.badext"); return; }
            if (t == k) { errLbl.Text = Loc.T("err.same"); return; }
            List<string> list = Store.Config.Profiles[k];
            if (list.Contains(t)) { errLbl.Text = Loc.T("err.dup"); return; }
            list.Add(t);
            tgtInput.Box.Clear();
            Changed();
            RefreshSources(k);
            RefreshTargets(t);
        }

        void RemoveTarget()
        {
            string k = CurKey();
            ListItem it = tgtList.SelectedItem as ListItem;
            if (k == null || it == null) return;
            Store.Config.Profiles[k].Remove(it.Key);
            Changed();
            RefreshSources(k);
        }

        void MoveTarget(int delta)
        {
            string k = CurKey();
            ListItem it = tgtList.SelectedItem as ListItem;
            if (k == null || it == null) return;
            List<string> list = Store.Config.Profiles[k];
            int i = list.IndexOf(it.Key), j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list.RemoveAt(i);
            list.Insert(j, it.Key);
            Changed();
            RefreshTargets(it.Key);
        }

        void ResetDefaults()
        {
            if (!Dialogs.Confirm(Loc.T("ext.reset.title"), Loc.T("ext.reset.msg"), Loc.T("ext.reset.yes"), Loc.T("btn.cancel"), true)) return;
            Store.Config.Profiles = Defaults.Profiles();
            Store.Config.ProfilesVersion = Defaults.Version;
            Changed();
            RefreshSources(null);
        }

        // ---- page: general
        Toggle ToggleRow(Control card, int y, string titleKey, string subKey, bool value, Action<bool> set)
        {
            L(card, Loc.T(titleKey), 158, y + 6, 420, 22, 14.5f, FontStyle.Bold, 0, Al.End);
            L(card, Loc.T(subKey), 58, y + 28, 520, 18, 12, FontStyle.Regular, 1, Al.End);
            Toggle t = new Toggle(); t.Bounds = R(card, 16, y + 12, 46, 26); t.Checked = value;
            t.Changed += delegate { set(t.Checked); Changed(); };
            card.Controls.Add(t);
            return t;
        }

        void BuildGeneralPage(Panel p)
        {
            AppConfig cfg = Store.Config;
            L(p, Loc.T("general.title"), 28, 18, 594, 36, 26, FontStyle.Bold, 0, Al.End);
            Card c = new Card(); c.Bounds = R(p, 28, 64, 594, 392); p.Controls.Add(c);
            ToggleRow(c, 6 + 0 * 52, "g.startup", "g.startup.sub", cfg.AutoStart, delegate(bool v) { cfg.AutoStart = v; Startup.Set(v); });
            ToggleRow(c, 6 + 1 * 52, "g.smart", "g.smart.sub", cfg.Smart, delegate(bool v) { cfg.Smart = v; });
            ToggleRow(c, 6 + 2 * 52, "g.usage", "g.usage.sub", cfg.UsageOrder, delegate(bool v) { cfg.UsageOrder = v; });
            ToggleRow(c, 6 + 3 * 52, "g.danger", "g.danger.sub", cfg.ConfirmDanger, delegate(bool v) { cfg.ConfirmDanger = v; });
            ToggleRow(c, 6 + 4 * 52, "g.mismatch", "g.mismatch.sub", cfg.ConfirmMismatch, delegate(bool v) { cfg.ConfirmMismatch = v; });
            ToggleRow(c, 6 + 5 * 52, "g.toasts", "g.toasts.sub", cfg.Toasts, delegate(bool v) { cfg.Toasts = v; });
            ToggleRow(c, 6 + 6 * 52, "g.anim", "g.anim.sub", cfg.Animations, delegate(bool v) { cfg.Animations = v; });
        }

        // ---- page: controls
        void BuildControlsPage(Panel p)
        {
            AppConfig cfg = Store.Config;
            L(p, Loc.T("controls.title"), 28, 18, 594, 36, 26, FontStyle.Bold, 0, Al.End);
            Card c = new Card(); c.Bounds = R(p, 28, 64, 594, 480); p.Controls.Add(c);

            // trigger button
            L(c, Loc.T("c.trigger"), 16, 12, 562, 22, 14.5f, FontStyle.Bold, 0, Al.End);
            L(c, Loc.T("c.trigger.sub"), 16, 34, 562, 18, 12, FontStyle.Regular, 1, Al.End);
            Seg seg = new Seg(); seg.Bounds = R(c, 16, 60, 562, 40);
            seg.Items = new string[] { Loc.T("trig.mid"), Loc.T("trig.x1"), Loc.T("trig.x2") };
            seg.Index = cfg.TriggerButton;
            seg.Changed += delegate { cfg.TriggerButton = seg.Index; Changed(); };
            c.Controls.Add(seg);

            // hold time
            L(c, Loc.T("c.hold"), 328, 118, 250, 22, 14.5f, FontStyle.Bold, 0, Al.End);
            L(c, Loc.T("c.hold.sub"), 328, 140, 250, 18, 12, FontStyle.Regular, 1, Al.End);
            Slider sl = new Slider(); sl.Bounds = R(c, 16, 122, 230, 30); sl.Value = cfg.HoldMs; c.Controls.Add(sl);
            holdValue = L(c, cfg.HoldMs + " ms", 250, 128, 62, 22, 12, FontStyle.Regular, 1, Al.Start);
            sl.Changed += delegate { cfg.HoldMs = sl.Value; holdValue.Text = sl.Value + " ms"; Changed(); };

            // automatic selection
            ToggleRow(c, 172, "c.autosel", "c.autosel.sub", cfg.AutoSelect, delegate(bool v) { cfg.AutoSelect = v; });

            // shortcuts
            chipOpen = BuildHotkeyRow(c, 238, "hk.open.title", "hk.open.sub", true);
            chipUndo = BuildHotkeyRow(c, 298, "hk.undo.title", "hk.undo.sub", false);
            hkErr = L(c, "", 16, 358, 562, 20, 12, FontStyle.Regular, 3, Al.End);
            L(c, Loc.T("c.note"), 16, 390, 562, 60, 12, FontStyle.Regular, 1, Al.End).Wrap = true;
        }

        UiButton BuildHotkeyRow(Card c, int y, string titleKey, string subKey, bool isOpen)
        {
            AppConfig cfg = Store.Config;
            L(c, Loc.T(titleKey), 328, y, 250, 22, 14.5f, FontStyle.Bold, 0, Al.End);
            L(c, Loc.T(subKey), 328, y + 22, 250, 18, 12, FontStyle.Regular, 1, Al.End);
            UiButton chip = B(c, Hotkeys.Format(isOpen ? cfg.HkOpenMods : cfg.HkUndoMods, isOpen ? cfg.HkOpenVk : cfg.HkUndoVk), 16, y + 2, 132, 38, BtnKind.Secondary, null);
            chip.Cursor = Cursors.Default;
            B(c, Loc.T("btn.change"), 156, y + 2, 84, 38, BtnKind.Primary, delegate { EditHotkey(isOpen, chip); });
            B(c, Loc.T("btn.default"), 248, y + 2, 66, 38, BtnKind.Ghost, delegate { ApplyHotkey(isOpen, chip, 3u, isOpen ? 0x45u : 0x5Au); });
            return chip;
        }

        void EditHotkey(bool isOpen, UiButton chip)
        {
            AppConfig cfg = Store.Config;
            using (ShortcutDialog d = new ShortcutDialog(Loc.T(isOpen ? "hk.open.title" : "hk.undo.title"),
                isOpen ? cfg.HkOpenMods : cfg.HkUndoMods, isOpen ? cfg.HkOpenVk : cfg.HkUndoVk))
            {
                d.ShowDialog();
                if (!d.Accepted) return;
                ApplyHotkey(isOpen, chip, d.Mods, d.Vk);
            }
        }

        void ApplyHotkey(bool isOpen, UiButton chip, uint mods, uint vk)
        {
            string error;
            if (app.TrySetHotkey(isOpen, mods, vk, out error))
            {
                chip.Text = Hotkeys.Format(mods, vk);
                hkErr.Text = "";
                Changed();
            }
            else hkErr.Text = error;
        }

        // ---- page: appearance & language
        void BuildLookPage(Panel p)
        {
            AppConfig cfg = Store.Config;
            L(p, Loc.T("look.title"), 28, 18, 594, 36, 26, FontStyle.Bold, 0, Al.End);
            Card c = new Card(); c.Bounds = R(p, 28, 64, 594, 270); p.Controls.Add(c);

            L(c, Loc.T("look.lang"), 16, 14, 562, 22, 14.5f, FontStyle.Bold, 0, Al.End);
            L(c, Loc.T("look.lang.sub"), 16, 36, 562, 18, 12, FontStyle.Regular, 1, Al.End);
            Seg lang = new Seg(); lang.Bounds = R(c, 16, 64, 320, 40);
            lang.Items = new string[] { "العربية", "English" };
            lang.Index = cfg.Lang == "en" ? 1 : 0;
            lang.Changed += delegate
            {
                cfg.Lang = lang.Index == 1 ? "en" : "ar";
                Loc.Lang = cfg.Lang;
                Store.Save();
                app.ReopenSettings(PgLook);
            };
            c.Controls.Add(lang);

            L(c, Loc.T("look.accent"), 16, 130, 562, 22, 14.5f, FontStyle.Bold, 0, Al.End);
            L(c, Loc.T("look.accent.sub"), 16, 152, 562, 18, 12, FontStyle.Regular, 1, Al.End);
            Swatches sw = new Swatches(); sw.Bounds = R(c, 16, 178, 300, 34); sw.Value = cfg.Accent; c.Controls.Add(sw);
            sw.Changed += delegate
            {
                cfg.Accent = sw.Value;
                Ui.Refresh();
                Changed();
                app.OnConfigChanged();
                InvalidateAll(this);
            };
            L(c, Loc.T("look.theme"), 16, 232, 562, 22, 12.5f, FontStyle.Regular, 1, Al.End);
        }

        static void InvalidateAll(Control c)
        {
            c.Invalidate();
            foreach (Control ch in c.Controls) InvalidateAll(ch);
        }

        // ---- page: history
        void BuildHistoryPage(Panel p)
        {
            L(p, Loc.T("hist.title"), 28, 22, 594, 36, 26, FontStyle.Bold, 0, Al.End);
            L(p, Loc.T("hist.desc"), 28, 60, 594, 22, 13.5f, FontStyle.Regular, 1, Al.End);
            Card c = new Card(); c.Bounds = R(p, 28, 100, 594, 372); p.Controls.Add(c);
            histList = new ThemedList(); histList.Bounds = R(c, 8, 8, 578, 356); c.Controls.Add(histList);
            histEmpty = L(c, Loc.T("hist.empty"), 8, 170, 578, 28, 15, FontStyle.Regular, 1, Al.Center);
            B(p, Loc.T("hist.undo"), 422, 488, 200, 40, BtnKind.Primary, delegate { UndoSelected(); });
            B(p, Loc.T("hist.clear"), 28, 488, 140, 40, BtnKind.Danger, delegate { ClearHistory(); });
        }

        void RefreshHistory()
        {
            histList.Items.Clear();
            for (int i = Store.History.Count - 1; i >= 0; i--)
            {
                HistoryEntry e = Store.History[i];
                if (e.Items.Count == 0) continue;
                string a = Path.GetFileName(e.Items[0][0]), b = Path.GetFileName(e.Items[0][1]);
                ListItem it = new ListItem();
                it.Key = e.Id;
                it.Text = e.Time.ToString("MM-dd HH:mm") + "   " + a + " → " + b + (e.Items.Count > 1 ? "   (+" + (e.Items.Count - 1) + ")" : "");
                it.Sub = e.Undone ? Loc.T("hist.undone") : "";
                histList.Items.Add(it);
            }
            histEmpty.Visible = histList.Items.Count == 0;
        }

        void UndoSelected()
        {
            ListItem it = histList.SelectedItem as ListItem;
            if (it == null) return;
            HistoryEntry e = Store.History.FirstOrDefault(x => x.Id == it.Key);
            if (e == null || e.Undone) return;
            app.UndoEntry(e);
            RefreshHistory();
        }

        void ClearHistory()
        {
            if (Store.History.Count == 0) return;
            if (!Dialogs.Confirm(Loc.T("hist.clear.title"), Loc.T("hist.clear.msg"), Loc.T("hist.clear.yes"), Loc.T("btn.cancel"), true)) return;
            Store.History.Clear();
            Store.SaveHistory();
            RefreshHistory();
        }

        // ---- page: about
        void BuildAboutPage(Panel p)
        {
            L(p, Loc.T("about.title"), 28, 18, 594, 36, 26, FontStyle.Bold, 0, Al.End);
            Card c = new Card(); c.Bounds = R(p, 28, 64, 594, 500); p.Controls.Add(c);
            c.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int lx = Loc.Rtl ? 594 - 16 - 56 : 16;
                Ui.DrawLogo(e.Graphics, new RectangleF(Ui.S(lx), Ui.S(16), Ui.S(56), Ui.S(56)), Ui.Accent);
            };
            L(c, AppInfo.Name + "   v" + AppInfo.Version, 16 + 56 + 14, 16, 594 - 32 - 70, 34, 24, FontStyle.Bold, 0, Al.End);
            L(c, Loc.T("about.tagline"), 16 + 56 + 14, 50, 594 - 32 - 70, 22, 13, FontStyle.Regular, 1, Al.End);

            L(c, Loc.T("about.author"), 16, 90, 140, 22, 12.5f, FontStyle.Regular, 1, Al.End);
            L(c, AppInfo.Author, 160, 90, 418, 22, 14, FontStyle.Bold, 2, Al.End);
            B(c, "GitHub", 16, 124, 178, 40, BtnKind.Secondary, delegate { Ui.OpenUrl(AppInfo.GitHubProfile); });
            B(c, "Telegram", 208, 124, 178, 40, BtnKind.Secondary, delegate { Ui.OpenUrl(AppInfo.TelegramUrl); });
            B(c, Loc.T("about.report"), 400, 124, 178, 40, BtnKind.Primary, delegate { Ui.OpenUrl(AppInfo.TelegramUrl); });

            L(c, Loc.T("about.shortcuts"), 16, 186, 562, 24, 14.5f, FontStyle.Bold, 2, Al.End);
            for (int i = 0; i < aboutLines.Length; i++)
                aboutLines[i] = L(c, "", 16, 216 + i * 30, 562, 24, 13.5f, FontStyle.Regular, 0, Al.End);
            L(c, Loc.T("about.warn"), 16, 346, 562, 22, 13, FontStyle.Regular, 4, Al.End);

            B(c, Loc.T("about.folder"), 16, 392, 190, 40, BtnKind.Secondary, delegate { try { Process.Start("explorer.exe", Store.Dir); } catch { } });
            B(c, Loc.T("about.quit"), 214, 392, 150, 40, BtnKind.Danger, delegate { app.Quit(); });
            L(c, Loc.T("about.copy"), 16, 452, 562, 22, 12, FontStyle.Regular, 1, Al.End);
        }

        void RefreshAbout()
        {
            AppConfig cfg = Store.Config;
            string[] trig = { Loc.T("trig.mid"), Loc.T("trig.x1"), Loc.T("trig.x2") };
            aboutLines[0].Text = Loc.F("about.l1", trig[Math.Max(0, Math.Min(2, cfg.TriggerButton))]);
            aboutLines[1].Text = Loc.F("about.l2", Ui.Ltr(Hotkeys.Format(cfg.HkOpenMods, cfg.HkOpenVk)));
            aboutLines[2].Text = Loc.F("about.l3", Ui.Ltr(Hotkeys.Format(cfg.HkUndoMods, cfg.HkUndoVk)));
            aboutLines[3].Text = Loc.T("about.l4");
        }
    }
}
