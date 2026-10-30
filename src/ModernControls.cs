using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LgSwitch
{
    internal static class UiTheme
    {
        internal static readonly Color Background = Color.FromArgb(16, 23, 37);
        internal static readonly Color Surface = Color.FromArgb(23, 34, 53);
        internal static readonly Color Text = Color.FromArgb(240, 245, 252);
        internal static readonly Color Muted = Color.FromArgb(146, 163, 186);
        internal static readonly Color Accent = Color.FromArgb(107, 156, 255);
        internal static readonly Color Border = Color.FromArgb(49, 66, 91);
        internal static Font Font(float size, bool bold = false)
        { return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
        internal static GraphicsPath Round(RectangleF bounds, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void DrawText(Graphics g, string text, Font font, RectangleF bounds, Color color, bool centered = false)
        {
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            using (var brush = new SolidBrush(color))
            {
                format.Alignment = centered ? StringAlignment.Center : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, font, brush, bounds, format);
            }
        }
        internal static void Focus(Graphics g, Rectangle bounds, Color color, float scale)
        {
            bounds.Inflate(-(int)(7 * scale), -(int)(7 * scale));
            using (var pen = new Pen(color, scale) { DashStyle = DashStyle.Dot })
                g.DrawRectangle(pen, bounds);
        }
    }

    internal sealed class SourceButton : Button
    {
        string inputKey = "", heading = "", caption = "";
        bool selected, observed, hovered;
        public string InputKey { get { return inputKey; } set { inputKey = value ?? ""; Invalidate(); } }
        public string Heading { get { return heading; } set { heading = value ?? ""; Text = heading; AccessibleName = heading; Invalidate(); } }
        public string Caption { get { return caption; } set { caption = value ?? ""; AccessibleDescription = caption; Invalidate(); } }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public bool Observed { get { return observed; } set { observed = value; Invalidate(); } }
        public SourceButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemInformation.HighContrast ? SystemColors.Control : UiTheme.Surface;
            ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : UiTheme.Text;
            Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PushButton;
            Size = new Size(200, 168); TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics; var saved = g.Save();
            try
            {
                g.SetClip(ClientRectangle, CombineMode.Intersect);
                // Paint the complete buffer; GDI background/text can reuse parent pixels during partial redraws.
                using (var background = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Control : Parent == null ? UiTheme.Background : Parent.BackColor))
                    g.FillRectangle(background, ClientRectangle);
                if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
                float s = g.DpiX / 96f;
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Color ink = Enabled ? UiTheme.Text : UiTheme.Muted;
                var box = new RectangleF(s, s, Width - 2 * s - 1, Height - 2 * s - 1);
                using (var shape = UiTheme.Round(box, 14 * s))
                using (var fill = new SolidBrush(selected ? Color.FromArgb(30, 48, 79) : hovered && Enabled ? Color.FromArgb(30, 44, 66) : UiTheme.Surface))
                using (var border = new Pen(selected ? UiTheme.Accent : hovered && Enabled ? UiTheme.Muted : UiTheme.Border, (selected ? 2 : 1) * s))
                { g.FillPath(fill, shape); g.DrawPath(border, shape); }
                float x = 20 * s, y = 20 * s;
                using (var icon = new Pen(Enabled ? UiTheme.Accent : UiTheme.Muted, 1.6f * s))
                {
                    g.DrawRectangle(icon, x, y, 30 * s, 20 * s);
                    g.DrawLine(icon, x + 15 * s, y + 20 * s, x + 15 * s, y + 26 * s);
                    g.DrawLine(icon, x + 8 * s, y + 26 * s, x + 22 * s, y + 26 * s);
                    g.DrawLine(icon, x + 6 * s, y + 15 * s, x + 24 * s, y + 15 * s);
                }
                using (var title = UiTheme.Font(16, true))
                using (var small = UiTheme.Font(9))
                {
                    UiTheme.DrawText(g, heading, title, new RectangleF(x, 58 * s, Math.Max(0, Width - 2 * x), 29 * s), ink);
                    UiTheme.DrawText(g, caption, small, new RectangleF(x, 91 * s, Math.Max(0, Width - 2 * x), 22 * s), UiTheme.Muted);
                    string badge = observed ? "Đang dùng" : selected ? "Đã gửi lệnh" : String.Equals(inputKey, "DP", StringComparison.OrdinalIgnoreCase) ? "NUC" : "Thiết bị ngoài";
                    float top = Height - 36 * s;
                    Color badgeColor = observed || selected ? UiTheme.Accent : UiTheme.Muted;
                    if (observed) using (var dot = new SolidBrush(badgeColor)) g.FillEllipse(dot, x, top + 6 * s, 6 * s, 6 * s);
                    float offset = observed ? 13 * s : 0;
                    UiTheme.DrawText(g, badge, small, new RectangleF(x + offset, top, Math.Max(0, Width - 2 * x - offset), 22 * s), badgeColor);
                }
                if (Focused && ShowFocusCues) UiTheme.Focus(g, ClientRectangle, UiTheme.Text, s);
            }
            finally { g.Restore(saved); }
        }
    }

    internal sealed class QuietButton : Button
    {
        bool hovered;
        public QuietButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : UiTheme.Muted;
            BackColor = SystemInformation.HighContrast ? SystemColors.Control : UiTheme.Background;
            Cursor = Cursors.Hand; TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics; var saved = g.Save();
            try
            {
                g.SetClip(ClientRectangle, CombineMode.Intersect);
                using (var background = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Control : Parent == null ? UiTheme.Background : Parent.BackColor))
                    g.FillRectangle(background, ClientRectangle);
                if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
                float s = g.DpiX / 96f;
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (hovered && Enabled)
                    using (var shape = UiTheme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 8 * s))
                    using (var fill = new SolidBrush(UiTheme.Surface)) g.FillPath(fill, shape);
                UiTheme.DrawText(g, Text, Font, ClientRectangle, Enabled && hovered ? UiTheme.Text : UiTheme.Muted, true);
                if (Focused && ShowFocusCues) UiTheme.Focus(g, ClientRectangle, UiTheme.Text, s);
            }
            finally { g.Restore(saved); }
        }
    }
}
