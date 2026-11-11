using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PCCheck
{
    public sealed class CheckRow : Panel
    {
        private CheckResult _result;
        private bool _hover;
        private ToolTip _tip;

        public CheckDefinition Definition { get; private set; }

        public event EventHandler DetailsRequested;

        public CheckStatus CurrentStatus
        {
            get { return _result == null ? CheckStatus.Unknown : _result.Status; }
        }

        public bool HasResult
        {
            get { return _result != null; }
        }

        public CheckResult Result
        {
            get { return _result; }
        }

        public CheckRow(CheckDefinition definition)
        {
            Definition = definition;
            Height = Theme.S(64);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = definition.Name;
            AccessibleDescription = "单击查看完整检查详情";
        }

        public void Reset()
        {
            _result = null;
            Invalidate();
        }

        public void SetResult(CheckResult result)
        {
            _result = result;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            if (_tip == null) _tip = new ToolTip();
            string tip = Definition.Name + "：" + (_result == null ? "正在检测…" : _result.Value);
            if (_result != null && !string.IsNullOrEmpty(_result.Detail)) tip += "\n" + _result.Detail;
            _tip.SetToolTip(this, TextUtil.Limit(tip, 1600));
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_result != null && DetailsRequested != null) DetailsRequested(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && _result != null)
            {
                if (DetailsRequested != null) DetailsRequested(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_hover ? Theme.Hover : Theme.Card);
            Theme.SetHighQuality(g);
            int width = Width;
            int nameWidth = width < Theme.S(880) ? Theme.S(145) : Theme.S(190);
            int valueX = Theme.S(32) + nameWidth + Theme.S(18);
            int statusWidth = Theme.S(50);
            int statusX = width - statusWidth - Theme.S(14);
            int valueWidth = Math.Max(Theme.S(40), statusX - valueX - Theme.S(12));

            using (Pen line = new Pen(Theme.BorderSoft))
            {
                g.DrawLine(line, Theme.S(20), Height - 1, width - Theme.S(20), Height - 1);
            }

            CheckStatus status = CurrentStatus;
            Color statusColor = _result == null ? Theme.Blue : Theme.StatusColor(status);
            Color statusSoft = _result == null ? Theme.BlueSoft : Theme.StatusSoft(status);

            using (SolidBrush ring = new SolidBrush(statusSoft))
            {
                g.FillEllipse(ring, Theme.S(15), Height / 2 - Theme.S(7), Theme.S(14), Theme.S(14));
            }
            using (SolidBrush dot = new SolidBrush(statusColor))
            {
                g.FillEllipse(dot, Theme.S(18), Height / 2 - Theme.S(4), Theme.S(8), Theme.S(8));
            }

            TextRenderer.DrawText(g, Definition.Name, Theme.FontBodyBold,
                new Rectangle(Theme.S(36), Theme.S(7), nameWidth, Theme.S(24)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, StatusText.SourceLabel(Definition.Source) + " · 只读", Theme.FontTiny,
                new Rectangle(Theme.S(36), Theme.S(32), nameWidth, Theme.S(19)), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            string value = _result == null ? "正在检测…" : _result.Value;
            Color valueColor = _result == null ? Theme.Faint : Theme.Text;
            TextRenderer.DrawText(g, value, Theme.FontBodyBold,
                new Rectangle(valueX, Theme.S(7), valueWidth, Theme.S(24)), valueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string detail = _result == null ? "等待后台读取" : _result.Detail;
            if (string.IsNullOrEmpty(detail)) detail = " ";
            TextRenderer.DrawText(g, detail, Theme.FontSmall,
                new Rectangle(valueX, Theme.S(32), valueWidth, Theme.S(19)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Rectangle pill = new Rectangle(statusX, Height / 2 - Theme.S(10), statusWidth, Theme.S(20));
            using (SolidBrush soft = new SolidBrush(statusSoft))
            {
                Theme.FillRoundRect(g, soft, pill, 8);
            }
            TextRenderer.DrawText(g, _result == null ? "检测中" : StatusText.Of(status), Theme.FontTiny,
                pill, statusColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (Focused)
            {
                using (Pen pen = new Pen(Color.FromArgb(110, Theme.Blue)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawRectangle(pen, Theme.S(3), Theme.S(3), width - Theme.S(7), Height - Theme.S(7));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _tip != null)
            {
                _tip.Dispose();
                _tip = null;
            }
            base.Dispose(disposing);
        }
    }

    public sealed class SectionCard : Panel
    {
        private readonly Panel _header;
        private readonly Label _arrow;
        private readonly Label _title;
        private readonly Label _count;
        private readonly Panel _body;
        private readonly BetterRowList _rows;
        private StackPanel _stack;
        private bool _collapsed;
        private bool _inLayout;

        public SectionCard(string title)
        {
            BackColor = Theme.Bg;
            Height = Theme.S(50);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _header = new Panel();
            _header.BackColor = Color.FromArgb(250, 252, 254);
            _header.Cursor = Cursors.Hand;
            _header.Click += delegate { Toggle(); };
            _header.MouseEnter += delegate { _header.BackColor = Color.FromArgb(246, 250, 254); };
            _header.MouseLeave += delegate { _header.BackColor = Color.FromArgb(250, 252, 254); };
            Controls.Add(_header);

            _arrow = new Label();
            _arrow.Text = "▾";
            _arrow.Font = Theme.FontBodyBold;
            _arrow.ForeColor = Theme.Muted;
            _arrow.BackColor = Color.Transparent;
            _arrow.TextAlign = ContentAlignment.MiddleCenter;
            _arrow.Cursor = Cursors.Hand;
            _arrow.Click += delegate { Toggle(); };
            _header.Controls.Add(_arrow);

            _title = new Label();
            _title.Text = title;
            _title.Font = Theme.FontSection;
            _title.ForeColor = Theme.Text;
            _title.BackColor = Color.Transparent;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Cursor = Cursors.Hand;
            _title.Click += delegate { Toggle(); };
            _header.Controls.Add(_title);

            _count = new Label();
            _count.Font = Theme.FontSmall;
            _count.ForeColor = Theme.Muted;
            _count.BackColor = Color.Transparent;
            _count.TextAlign = ContentAlignment.MiddleRight;
            _count.Text = "0 项";
            _count.Cursor = Cursors.Hand;
            _count.Click += delegate { Toggle(); };
            _header.Controls.Add(_count);

            _body = new Panel();
            _body.BackColor = Theme.Card;
            Controls.Add(_body);

            _rows = new BetterRowList();
            _rows.BackColor = Theme.Card;
            _body.Controls.Add(_rows);
        }

        public bool IsCollapsed
        {
            get { return _collapsed; }
        }

        public int RowCount
        {
            get { return _rows.Rows.Count; }
        }

        public int PreferredHeight
        {
            get { return Theme.S(50) + (_collapsed ? 0 : _rows.Rows.Count * Theme.S(64)) + 1; }
        }

        public void SetParentStack(StackPanel stack)
        {
            _stack = stack;
        }

        public void AddRow(CheckRow row)
        {
            _rows.AddRow(row);
            RelayoutRows();
            RefreshCounts();
        }

        public void Toggle()
        {
            SetCollapsed(!_collapsed);
        }

        public void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            _body.Visible = !collapsed;
            _arrow.Text = collapsed ? "▸" : "▾";
            Height = PreferredHeight;
            if (_stack != null) _stack.Relayout();
            Invalidate();
        }

        public bool ContainsProblem()
        {
            foreach (CheckRow row in _rows.Rows)
            {
                if (row.CurrentStatus == CheckStatus.Warning || row.CurrentStatus == CheckStatus.Critical) return true;
            }
            return false;
        }

        public void RefreshCounts()
        {
            int warnings = 0;
            int critical = 0;
            foreach (CheckRow row in _rows.Rows)
            {
                if (row.CurrentStatus == CheckStatus.Warning) warnings++;
                else if (row.CurrentStatus == CheckStatus.Critical) critical++;
            }
            string text = _rows.Rows.Count + " 项";
            if (critical > 0) text += " · 严重 " + critical;
            if (warnings > 0) text += " · 注意 " + warnings;
            _count.Text = text;
            _count.ForeColor = critical > 0 ? Theme.Red : (warnings > 0 ? Theme.Amber : Theme.Muted);
        }

        private void RelayoutRows()
        {
            if (_inLayout || _rows == null || _body == null) return;
            _inLayout = true;
            try
            {
                int rowHeight = Theme.S(64);
                int y = 0;
                foreach (CheckRow row in _rows.Rows)
                {
                    row.SetBounds(0, y, Math.Max(100, _rows.Width), rowHeight);
                    y += rowHeight;
                }
                _rows.Height = Math.Max(0, y);
                int target = PreferredHeight;
                if (Height != target) Height = target;
                int innerWidth = Math.Max(100, Width - 2);
                _body.SetBounds(1, Theme.S(50), innerWidth, Math.Max(0, target - Theme.S(51)));
                _rows.SetBounds(0, 0, innerWidth, Math.Max(0, target - Theme.S(51)));
            }
            finally
            {
                _inLayout = false;
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (_inLayout || _header == null || _body == null || _rows == null ||
                _arrow == null || _title == null || _count == null) return;
            _inLayout = true;
            try
            {
                Theme.ApplyRoundedRegion(this, 10);
                int innerWidth = Math.Max(100, Width - 2);
                _header.SetBounds(1, 1, innerWidth, Theme.S(49));
                _arrow.SetBounds(Theme.S(14), Theme.S(8), Theme.S(20), Theme.S(32));
                _count.SetBounds(Math.Max(Theme.S(80), innerWidth - Theme.S(184)), Theme.S(8),
                    Theme.S(168), Theme.S(32));
                _title.SetBounds(Theme.S(40), Theme.S(8), Math.Max(Theme.S(80), innerWidth - Theme.S(230)),
                    Theme.S(32));
                _body.SetBounds(1, Theme.S(50), innerWidth, Math.Max(0, Height - Theme.S(51)));
                _rows.SetBounds(0, 0, innerWidth, Math.Max(0, Height - Theme.S(51)));
                int rowHeight = Theme.S(64);
                int y = 0;
                foreach (CheckRow row in _rows.Rows)
                {
                    row.SetBounds(0, y, Math.Max(100, innerWidth), rowHeight);
                    y += rowHeight;
                }
                _rows.Height = Math.Max(0, y);
            }
            finally
            {
                _inLayout = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Theme.SetHighQuality(g);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush white = new SolidBrush(Theme.Card))
            {
                Theme.FillRoundRect(g, white, rect, 10);
            }
            using (Pen pen = new Pen(Theme.Border))
            {
                Theme.DrawRoundRect(g, pen, rect, 10);
            }
            using (Pen headerLine = new Pen(Theme.Border))
            {
                g.DrawLine(headerLine, 1, Theme.S(50), Width - 2, Theme.S(50));
            }
        }

        private sealed class BetterRowList : Panel
        {
            public readonly System.Collections.Generic.List<CheckRow> Rows =
                new System.Collections.Generic.List<CheckRow>();

            public BetterRowList()
            {
                BackColor = Theme.Card;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            public void AddRow(CheckRow row)
            {
                Rows.Add(row);
                Controls.Add(row);
                row.DetailsRequested += delegate(object sender, EventArgs e)
                {
                    Form form = FindForm();
                    MainForm main = form as MainForm;
                    if (main != null) main.ShowItemDetails((CheckRow)sender);
                };
            }

            protected override void OnResize(EventArgs eventargs)
            {
                base.OnResize(eventargs);
                int rowHeight = Theme.S(64);
                int y = 0;
                foreach (CheckRow row in Rows)
                {
                    row.SetBounds(0, y, Math.Max(100, Width), rowHeight);
                    y += rowHeight;
                }
                Height = y;
            }
        }
    }

    public sealed class StackPanel : Panel
    {
        private readonly System.Collections.Generic.List<SectionCard> _cards =
            new System.Collections.Generic.List<SectionCard>();
        private bool _layingOut;

        public StackPanel()
        {
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = Theme.S(new Padding(24, 8, 6, 26));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public System.Collections.Generic.IList<SectionCard> Cards
        {
            get { return _cards; }
        }

        public void AddCard(SectionCard card)
        {
            card.SetParentStack(this);
            _cards.Add(card);
            Controls.Add(card);
            Relayout();
        }

        public bool AllCollapsed()
        {
            foreach (SectionCard card in _cards)
                if (!card.IsCollapsed) return false;
            return true;
        }

        public void SetAllCollapsed(bool collapsed)
        {
            foreach (SectionCard card in _cards) card.SetCollapsed(collapsed);
            Relayout();
        }

        public void Relayout()
        {
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_layingOut) return;
            _layingOut = true;
            try
            {
                int width = ClientSize.Width - Padding.Left - Padding.Right -
                    SystemInformation.VerticalScrollBarWidth - Theme.S(2);
                if (width < Theme.S(320)) width = Theme.S(320);
                int y = Padding.Top;
                foreach (SectionCard card in _cards)
                {
                    card.SetBounds(Padding.Left, y, width, card.PreferredHeight);
                    y += card.PreferredHeight + Theme.S(12);
                }
                AutoScrollMinSize = new Size(0, y + Padding.Bottom);
            }
            finally
            {
                _layingOut = false;
            }
        }
    }

    public sealed class ItemDetailForm : Form
    {
        private readonly CheckDefinition _definition;
        private readonly CheckResult _result;
        private readonly Label _title;
        private readonly Label _status;
        private readonly Label _valueCaption;
        private readonly Label _detailCaption;
        private readonly Label _hint;
        private readonly Label _source;
        private readonly TextBox _value;
        private readonly TextBox _detail;
        private readonly RoundedButton _copy;
        private readonly RoundedButton _close;
        private readonly Timer _copyTimer;

        public ItemDetailForm(CheckDefinition definition, CheckResult result)
        {
            _definition = definition;
            _result = result;
            Text = "检查详情 - " + definition.Name;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Theme.S(new Size(720, 560));
            MinimumSize = Theme.S(new Size(600, 480));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Theme.Bg;
            Font = Theme.FontBody;
            Padding = new Padding(0);

            Panel header = new Panel();
            header.BackColor = Theme.Navy;
            header.SetBounds(0, 0, ClientSize.Width, Theme.S(76));
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(header);

            _title = new Label();
            _title.Text = definition.Name;
            _title.Font = Theme.FontHeadline;
            _title.ForeColor = Color.White;
            _title.BackColor = Color.Transparent;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.SetBounds(Theme.S(22), Theme.S(10), Theme.S(470), Theme.S(30));
            _title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(_title);

            _status = new Label();
            _status.Text = StatusText.Of(result.Status);
            _status.Font = Theme.FontSmall;
            _status.ForeColor = Color.White;
            _status.BackColor = Theme.StatusColor(result.Status);
            _status.TextAlign = ContentAlignment.MiddleCenter;
            _status.SetBounds(Theme.S(22), Theme.S(45), Theme.S(58), Theme.S(22));
            header.Controls.Add(_status);

            Label section = new Label();
            section.Text = StatusText.GroupTitle(definition.Group) + " / " + definition.Section;
            section.Font = Theme.FontSmall;
            section.ForeColor = Color.FromArgb(180, 203, 219);
            section.BackColor = Color.Transparent;
            section.TextAlign = ContentAlignment.MiddleLeft;
            section.SetBounds(Theme.S(92), Theme.S(45), Theme.S(460), Theme.S(22));
            section.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(section);

            _valueCaption = Caption("当前值", 22, 94);
            _valueCaption.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(_valueCaption);
            _value = new TextBox();
            _value.ReadOnly = true;
            _value.TabStop = false;
            _value.Multiline = true;
            _value.ScrollBars = ScrollBars.Vertical;
            _value.BorderStyle = BorderStyle.FixedSingle;
            _value.BackColor = Theme.Card;
            _value.ForeColor = Theme.Text;
            _value.Font = new Font(Theme.FontBodyBold.FontFamily, 10.5f, FontStyle.Bold);
            _value.Text = result.Value;
            _value.SetBounds(Theme.S(22), Theme.S(118), ClientSize.Width - Theme.S(44), Theme.S(64));
            _value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_value);

            _detailCaption = Caption("详细信息", 22, 194);
            _detailCaption.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(_detailCaption);
            _detail = new TextBox();
            _detail.ReadOnly = true;
            _detail.TabStop = false;
            _detail.Multiline = true;
            _detail.ScrollBars = ScrollBars.Vertical;
            _detail.BorderStyle = BorderStyle.FixedSingle;
            _detail.BackColor = Theme.Card;
            _detail.ForeColor = Theme.TextSecondary;
            _detail.Font = Theme.FontBody;
            _detail.Text = string.IsNullOrEmpty(result.Detail) ? "无更多详细信息。" : result.Detail;
            _detail.SetBounds(Theme.S(22), Theme.S(218), ClientSize.Width - Theme.S(44), Theme.S(190));
            _detail.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_detail);

            _hint = new Label();
            _hint.Font = Theme.FontSmall;
            _hint.ForeColor = result.Status == CheckStatus.Ok ? Theme.Muted : Theme.Amber;
            _hint.BackColor = Color.Transparent;
            _hint.TextAlign = ContentAlignment.TopLeft;
            _hint.Text = string.IsNullOrEmpty(result.Hint) ? "无额外提示。" : "提示：" + result.Hint;
            _hint.SetBounds(Theme.S(22), Theme.S(416), ClientSize.Width - Theme.S(44), Theme.S(38));
            _hint.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_hint);

            _source = new Label();
            _source.Font = Theme.FontTiny;
            _source.ForeColor = Theme.Faint;
            _source.BackColor = Color.Transparent;
            _source.TextAlign = ContentAlignment.MiddleLeft;
            _source.Text = "数据来源：" + StatusText.SourceLabel(definition.Source) + " · 只读";
            _source.SetBounds(Theme.S(22), Theme.S(460), ClientSize.Width - Theme.S(44), Theme.S(20));
            _source.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_source);

            Panel footer = new Panel();
            footer.BackColor = Theme.Card;
            footer.SetBounds(0, ClientSize.Height - Theme.S(64), ClientSize.Width, Theme.S(64));
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(footer);
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            _copy = new RoundedButton();
            _copy.Text = "复制本项";
            _copy.SetBounds(ClientSize.Width - Theme.S(244), Theme.S(13), Theme.S(108), Theme.S(38));
            _copy.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _copy.Click += delegate { CopyItem(); };
            footer.Controls.Add(_copy);

            _close = new RoundedButton();
            _close.Text = "关闭";
            _close.Primary = true;
            _close.SetBounds(ClientSize.Width - Theme.S(126), Theme.S(13), Theme.S(104), Theme.S(38));
            _close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _close.Click += delegate { Close(); };
            footer.Controls.Add(_close);
            CancelButton = _close;

            _copyTimer = new Timer();
            _copyTimer.Interval = 1600;
            _copyTimer.Tick += delegate
            {
                _copyTimer.Stop();
                _copy.Text = "复制本项";
            };
            Shown += delegate
            {
                _value.SelectionStart = 0;
                _value.SelectionLength = 0;
                _detail.SelectionStart = 0;
                _detail.SelectionLength = 0;
                _close.Focus();
            };
            FormClosed += delegate { _copyTimer.Dispose(); };
        }

        private Label Caption(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.FontBodyBold;
            label.ForeColor = Theme.TextSecondary;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.SetBounds(Theme.S(x), Theme.S(y), Theme.S(200), Theme.S(20));
            return label;
        }

        private void CopyItem()
        {
            try
            {
                string text = _definition.Name + "：" + _result.Value + "\r\n";
                if (!string.IsNullOrEmpty(_result.Detail)) text += _result.Detail + "\r\n";
                if (!string.IsNullOrEmpty(_result.Hint)) text += "提示：" + _result.Hint + "\r\n";
                text += "来源：" + StatusText.SourceLabel(_definition.Source);
                Clipboard.SetText(text);
                _copy.Text = "已复制";
                _copyTimer.Start();
            }
            catch
            {
                MessageBox.Show(this, "未能复制到剪贴板，请稍后重试。", "复制失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
