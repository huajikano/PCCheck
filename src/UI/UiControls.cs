using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PCCheck
{
    public sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _pressed;
        private bool _primary;

        public bool Primary
        {
            get { return _primary; }
            set { _primary = value; Invalidate(); }
        }

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Card;
            Font = Theme.FontButton;
            Height = Theme.S(38);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            Theme.SetHighQuality(g);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill;
            Color border;
            Color text;
            if (!Enabled)
            {
                fill = Theme.GraySoft;
                border = Theme.Border;
                text = Theme.Faint;
            }
            else if (_primary)
            {
                fill = _pressed ? Color.FromArgb(30, 64, 175) : (_hover ? Theme.BlueDark : Theme.Blue);
                border = fill;
                text = Color.White;
            }
            else
            {
                fill = _pressed ? Color.FromArgb(241, 245, 249) : (_hover ? Theme.Hover : Theme.Card);
                border = _pressed ? Theme.Blue : Theme.Border;
                text = Theme.TextSecondary;
            }
            using (SolidBrush brush = new SolidBrush(fill))
            {
                Theme.FillRoundRect(g, brush, rect, 8);
            }
            using (Pen pen = new Pen(border, 1f))
            {
                Theme.DrawRoundRect(g, pen, rect, 8);
            }
            if (Focused && Enabled)
            {
                int inset = Theme.S(3);
                Rectangle focus = new Rectangle(rect.X + inset, rect.Y + inset,
                    Math.Max(1, rect.Width - inset * 2), Math.Max(1, rect.Height - inset * 2));
                using (Pen pen = new Pen(Color.FromArgb(120, Theme.Blue), 1f))
                {
                    pen.DashStyle = DashStyle.Dot;
                    Theme.DrawRoundRect(g, pen, focus, 6);
                }
            }
            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public sealed class HeaderBanner : Panel
    {
        private string _machine = "本机";
        private string _os = "正在读取系统信息";
        private string _privilege = "只读检测";

        public HeaderBanner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = Theme.S(88);
        }

        public void SetInfo(string machine, string os, string privilege)
        {
            if (!string.IsNullOrEmpty(machine)) _machine = machine;
            if (!string.IsNullOrEmpty(os)) _os = os;
            if (!string.IsNullOrEmpty(privilege)) _privilege = privilege;
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Theme.ApplyRoundedRegion(this, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            Theme.SetHighQuality(g);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundRect(rect, 14))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, Theme.Navy, Theme.NavyLight, 0f))
            {
                g.FillPath(brush, path);
            }
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(24, 255, 255, 255)))
            {
                g.FillEllipse(soft, Width - Theme.S(250), -Theme.S(80), Theme.S(320), Theme.S(220));
                g.FillEllipse(soft, Width - Theme.S(120), Theme.S(30), Theme.S(160), Theme.S(160));
            }
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(42, 111, 190)))
            {
                Theme.FillRoundRect(g, accent, new Rectangle(Theme.S(22), Theme.S(20), Theme.S(46), Theme.S(46)), 12);
            }
            TextRenderer.DrawText(g, "PC", Theme.FontHeadline,
                new Rectangle(Theme.S(22), Theme.S(20), Theme.S(46), Theme.S(46)), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "PCCheck", Theme.FontTitle,
                new Rectangle(Theme.S(82), Theme.S(12), Theme.S(300), Theme.S(44)), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "电脑配置与环境检查 · 单文件绿色版", Theme.FontSmall,
                new Rectangle(Theme.S(84), Theme.S(51), Theme.S(430), Theme.S(24)), Color.FromArgb(183, 203, 219),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            Rectangle right = new Rectangle(Width - Theme.S(390), Theme.S(17), Theme.S(360), Theme.S(26));
            TextRenderer.DrawText(g, _machine, Theme.FontBodyBold, right, Color.White,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _os, Theme.FontSmall,
                new Rectangle(Width - Theme.S(500), Theme.S(43), Theme.S(470), Theme.S(22)), Color.FromArgb(183, 203, 219),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int badgeWidth = TextRenderer.MeasureText(_privilege, Theme.FontTiny).Width + Theme.S(20);
            Rectangle badge = new Rectangle(Width - badgeWidth - Theme.S(30), Theme.S(67), badgeWidth, Theme.S(19));
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(35, 255, 255, 255)))
            {
                Theme.FillRoundRect(g, brush, badge, 8);
            }
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(74, 222, 128)))
            {
                g.FillEllipse(dot, badge.X + Theme.S(7), badge.Y + Theme.S(6), Theme.S(6), Theme.S(6));
            }
            TextRenderer.DrawText(g, _privilege, Theme.FontTiny,
                new Rectangle(badge.X + Theme.S(17), badge.Y, badge.Width - Theme.S(20), badge.Height),
                Color.FromArgb(220, 240, 250),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using (Pen pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
            {
                g.DrawLine(pen, Theme.S(22), Height - 1, Width - Theme.S(22), Height - 1);
            }
        }
    }

    public sealed class ConclusionCard : Panel
    {
        private string _headline = "正在检测本机配置…";
        private string _subline = "正在读取硬件、系统与网络信息，请稍候。";
        private CheckStatus _tone = CheckStatus.Unknown;
        private bool _scanning = true;
        private double _progress;

        public ConclusionCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = Theme.S(102);
        }

        public void SetScanning(int received, int total, double seconds)
        {
            _scanning = true;
            _tone = CheckStatus.Unknown;
            _headline = "正在检测本机配置…";
            _subline = string.Format("已读取 {0} / {1} 项 · 已用时 {2:0.0} 秒 · 检测期间可以随时关闭窗口", received, total, seconds);
            _progress = total > 0 ? Math.Min(1.0, (double)received / total) : 0;
            Invalidate();
        }

        public void SetSummary(SummaryView summary)
        {
            _scanning = false;
            _tone = summary.Tone;
            _headline = summary.Headline;
            _subline = summary.Subline;
            _progress = 1;
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Theme.ApplyRoundedRegion(this, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            Theme.SetHighQuality(g);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush white = new SolidBrush(Theme.Card))
            {
                Theme.FillRoundRect(g, white, rect, 12);
            }
            using (Pen border = new Pen(Theme.Border))
            {
                Theme.DrawRoundRect(g, border, rect, 12);
            }
            Color tone = _scanning ? Theme.Blue : Theme.StatusColor(_tone);
            using (SolidBrush bar = new SolidBrush(tone))
            {
                Theme.FillRoundRect(g, bar, new Rectangle(0, Theme.S(18), Theme.S(5), Height - Theme.S(36)), 2);
            }
            Color iconSoft = _scanning ? Theme.BlueSoft : Theme.StatusSoft(_tone);
            Rectangle iconRect = new Rectangle(Theme.S(22), (Height - Theme.S(40)) / 2, Theme.S(40), Theme.S(40));
            using (SolidBrush soft = new SolidBrush(iconSoft))
            {
                g.FillEllipse(soft, iconRect);
            }
            string glyph;
            if (_scanning) glyph = "…";
            else if (_tone == CheckStatus.Ok) glyph = "✓";
            else if (_tone == CheckStatus.Warning) glyph = "!";
            else if (_tone == CheckStatus.Critical) glyph = "!";
            else glyph = "?";
            TextRenderer.DrawText(g, glyph, Theme.FontHeadline,
                new Rectangle(iconRect.X, iconRect.Y - Theme.S(1), iconRect.Width, iconRect.Height),
                tone, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, _headline, Theme.FontHeadline,
                new Rectangle(Theme.S(76), Theme.S(14), Width - Theme.S(100), Theme.S(34)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _subline, Theme.FontSmall,
                new Rectangle(Theme.S(76), Theme.S(49), Width - Theme.S(100), Theme.S(24)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (!_scanning) return;
            int trackX = Theme.S(76);
            int trackY = Height - Theme.S(13);
            int trackW = Math.Max(Theme.S(20), Width - trackX - Theme.S(26));
            using (SolidBrush track = new SolidBrush(Theme.BorderSoft))
            {
                Theme.FillRoundRect(g, track, new Rectangle(trackX, trackY, trackW, Theme.S(4)), 2);
            }
            int fillW = (int)Math.Round(trackW * Math.Max(0, Math.Min(1, _progress)));
            if (fillW > 0)
            {
                using (SolidBrush fill = new SolidBrush(tone))
                {
                    Theme.FillRoundRect(g, fill, new Rectangle(trackX, trackY, fillW, Theme.S(4)), 2);
                }
            }
        }
    }

    public sealed class TabButton : Control
    {
        private bool _selected;
        private bool _hover;
        private int _issueCount;
        private bool _critical;

        public int Index { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public TabButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.FontTab;
            Height = Theme.S(42);
            Cursor = Cursors.Hand;
            BackColor = Theme.Bg;
            TabStop = true;
        }

        public void SetIssueCount(int count, bool critical)
        {
            _issueCount = count;
            _critical = critical;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            Theme.SetHighQuality(g);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_selected)
            {
                using (SolidBrush white = new SolidBrush(Theme.Card))
                {
                    Theme.FillRoundRect(g, white, rect, 10);
                }
                using (Pen border = new Pen(Theme.Border))
                {
                    Theme.DrawRoundRect(g, border, rect, 10);
                }
                using (SolidBrush accent = new SolidBrush(Theme.Blue))
                {
                    Theme.FillRoundRect(g, accent, new Rectangle(Theme.S(14), Height - Theme.S(4),
                        Width - Theme.S(28), Theme.S(3)), 1);
                }
            }
            else if (_hover)
            {
                using (SolidBrush hover = new SolidBrush(Color.FromArgb(232, 238, 247)))
                {
                    Theme.FillRoundRect(g, hover, rect, 10);
                }
            }
            Color textColor = _selected ? Theme.Blue : Theme.TextSecondary;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(16), 0, Width - Theme.S(46), Height),
                textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_issueCount > 0)
            {
                Color badge = _critical ? Theme.Red : Theme.Amber;
                Color badgeSoft = _critical ? Theme.RedSoft : Theme.AmberSoft;
                Rectangle b = new Rectangle(Width - Theme.S(31), (Height - Theme.S(19)) / 2,
                    Theme.S(20), Theme.S(19));
                using (SolidBrush soft = new SolidBrush(badgeSoft))
                {
                    Theme.FillRoundRect(g, soft, b, 8);
                }
                TextRenderer.DrawText(g, _issueCount > 99 ? "99+" : _issueCount.ToString(), Theme.FontTiny,
                    b, badge, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            if (Focused)
            {
                using (Pen pen = new Pen(Color.FromArgb(130, Theme.Blue)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    Theme.DrawRoundRect(g, pen, new Rectangle(Theme.S(3), Theme.S(3),
                        Width - Theme.S(7), Height - Theme.S(7)), 8);
                }
            }
        }
    }

    public sealed class TabBar : Panel
    {
        private readonly TabButton[] _buttons;
        private readonly LinkLabel _toggle;
        private int _selected;

        public event EventHandler SelectedIndexChanged;
        public event EventHandler ToggleAllClicked;

        public int SelectedIndex
        {
            get { return _selected; }
        }

        public TabBar()
        {
            BackColor = Theme.Bg;
            Height = Theme.S(50);
            _buttons = new TabButton[5];
            for (int i = 0; i < 5; i++)
            {
                TabButton b = new TabButton();
                b.Index = i;
                b.Text = StatusText.GroupTitle((CheckGroup)i);
                b.AccessibleName = b.Text + "页签";
                b.Click += delegate(object sender, EventArgs e)
                {
                    TabButton tb = (TabButton)sender;
                    Select(tb.Index);
                };
                _buttons[i] = b;
                Controls.Add(b);
            }
            _toggle = new LinkLabel();
            _toggle.Text = "全部折叠";
            _toggle.AutoSize = true;
            _toggle.Font = Theme.FontSmall;
            _toggle.LinkColor = Theme.Blue;
            _toggle.ActiveLinkColor = Theme.BlueDark;
            _toggle.VisitedLinkColor = Theme.Blue;
            _toggle.LinkBehavior = LinkBehavior.HoverUnderline;
            _toggle.BackColor = Theme.Bg;
            _toggle.Cursor = Cursors.Hand;
            _toggle.Click += delegate
            {
                if (ToggleAllClicked != null) ToggleAllClicked(this, EventArgs.Empty);
            };
            Controls.Add(_toggle);
            Select(0);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _buttons.Length) return;
            if (_selected == index && _buttons[index].Selected) return;
            _selected = index;
            for (int i = 0; i < _buttons.Length; i++) _buttons[i].Selected = (i == index);
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        public void SetTabIssues(int index, int count, bool critical)
        {
            if (index >= 0 && index < _buttons.Length) _buttons[index].SetIssueCount(count, critical);
        }

        public void SetToggleText(string text)
        {
            _toggle.Text = text;
            PerformLayout();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_buttons == null) return;
            int x = 0;
            int width = Theme.S(150);
            int gap = Theme.S(8);
            foreach (TabButton b in _buttons)
            {
                if (b == null) continue;
                b.SetBounds(x, Theme.S(4), width, Theme.S(42));
                x += width + gap;
            }
            if (_toggle != null)
            {
                _toggle.Location = new Point(Math.Max(0, Width - _toggle.Width - Theme.S(4)), Theme.S(16));
            }
        }
    }

    public sealed class FooterPanel : Panel
    {
        private readonly Label _main;
        private readonly Label _sub;
        public readonly RoundedButton RecheckButton;
        public readonly RoundedButton CopyButton;
        public readonly RoundedButton ExportButton;

        public FooterPanel()
        {
            BackColor = Theme.Card;
            Height = Theme.S(66);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _main = new Label();
            _main.AutoSize = false;
            _main.Font = Theme.FontBodyBold;
            _main.ForeColor = Theme.Text;
            _main.BackColor = Color.Transparent;
            _main.Text = "准备检测";
            Controls.Add(_main);

            _sub = new Label();
            _sub.AutoSize = false;
            _sub.Font = Theme.FontSmall;
            _sub.ForeColor = Theme.Muted;
            _sub.BackColor = Color.Transparent;
            _sub.Text = "只读检测，不会修改系统设置";
            Controls.Add(_sub);

            RecheckButton = new RoundedButton();
            RecheckButton.Text = "重新检测";
            RecheckButton.Primary = true;
            RecheckButton.AccessibleName = "重新检测";
            Controls.Add(RecheckButton);

            CopyButton = new RoundedButton();
            CopyButton.Text = "一键复制全文";
            CopyButton.AccessibleName = "一键复制全文";
            Controls.Add(CopyButton);

            ExportButton = new RoundedButton();
            ExportButton.Text = "导出报告";
            ExportButton.AccessibleName = "导出报告";
            Controls.Add(ExportButton);
        }

        public void SetStatus(string main, string sub)
        {
            _main.Text = main;
            if (sub != null) _sub.Text = sub;
            Invalidate();
        }

        public void SetButtonsEnabled(bool recheck, bool report)
        {
            RecheckButton.Enabled = recheck;
            CopyButton.Enabled = report;
            ExportButton.Enabled = report;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (_main == null || _sub == null || RecheckButton == null ||
                CopyButton == null || ExportButton == null) return;
            int right = Width - Theme.S(22);
            int y = Theme.S(14);
            int h = Theme.S(38);
            ExportButton.SetBounds(right - Theme.S(104), y, Theme.S(104), h);
            right = ExportButton.Left - Theme.S(10);
            CopyButton.SetBounds(right - Theme.S(128), y, Theme.S(128), h);
            right = CopyButton.Left - Theme.S(10);
            RecheckButton.SetBounds(right - Theme.S(108), y, Theme.S(108), h);
            _main.SetBounds(Theme.S(24), Theme.S(9), Math.Max(Theme.S(120), RecheckButton.Left - Theme.S(48)), Theme.S(23));
            _sub.SetBounds(Theme.S(24), Theme.S(33), Math.Max(Theme.S(120), RecheckButton.Left - Theme.S(48)), Theme.S(20));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }
        }
    }

    public sealed class SlimProgressBar : Control
    {
        private double _value;

        public double Value
        {
            get { return _value; }
            set { _value = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        public SlimProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Card : Parent.BackColor);
            using (SolidBrush track = new SolidBrush(Theme.BorderSoft))
            {
                e.Graphics.FillRectangle(track, 0, 0, Width, Height);
            }
            using (SolidBrush fill = new SolidBrush(Theme.Blue))
            {
                e.Graphics.FillRectangle(fill, 0, 0, (int)(Width * _value), Height);
            }
        }
    }
}
