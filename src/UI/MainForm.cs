using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PCCheck
{
    public sealed class MainForm : Form
    {
        public const string WindowTitle = "PCCheck - 电脑配置与环境检查";

        private readonly Dictionary<string, CheckRow> _rows =
            new Dictionary<string, CheckRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SectionCard> _cards = new List<SectionCard>();
        private readonly StackPanel[] _pages = new StackPanel[5];
        private readonly Timer _uiTimer;
        private readonly Timer _startTimer;
        private readonly DateTime _startedAt = DateTime.Now;

        private HeaderBanner _header;
        private ConclusionCard _conclusion;
        private TabBar _tabBar;
        private Panel _contentHost;
        private FooterPanel _footer;
        private Panel _root;
        private ProbeRunner _runner;
        private ScanSnapshot _snapshot;
        private SummaryView _summary;
        private Stopwatch _scanWatch;
        private int _received;
        private int _generation;
        private bool _running;
        private bool _closed;

        public MainForm()
        {
            Text = WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Theme.S(new Size(1120, 780));
            MinimumSize = Theme.S(new Size(920, 660));
            ConstrainToScreen();
            BackColor = Theme.Bg;
            Font = Theme.FontBody;
            DoubleBuffered = true;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildUi();
            LayoutRoot();

            _uiTimer = new Timer();
            _uiTimer.Interval = 200;
            _uiTimer.Tick += delegate { OnUiTick(); };

            _startTimer = new Timer();
            _startTimer.Interval = 120;
            _startTimer.Tick += delegate
            {
                _startTimer.Stop();
                StartScan();
            };
            Shown += delegate { _startTimer.Start(); };
            FormClosing += delegate { Shutdown(); };
            Resize += delegate { LayoutRoot(); };
        }

        private int Total
        {
            get { return CheckCatalog.Count; }
        }

        private void BuildUi()
        {
            _root = new Panel();
            _root.Dock = DockStyle.Fill;
            _root.BackColor = Theme.Bg;
            Controls.Add(_root);

            _header = new HeaderBanner();
            _header.SetInfo(Environment.MachineName, "正在读取系统信息", "只读检测");
            _root.Controls.Add(_header);

            _conclusion = new ConclusionCard();
            _root.Controls.Add(_conclusion);

            _tabBar = new TabBar();
            _tabBar.SelectedIndexChanged += delegate { SwitchPage(_tabBar.SelectedIndex); };
            _tabBar.ToggleAllClicked += delegate { ToggleAllSections(); };
            _root.Controls.Add(_tabBar);

            _contentHost = new Panel();
            _contentHost.BackColor = Theme.Bg;
            _root.Controls.Add(_contentHost);

            for (int i = 0; i < 5; i++)
            {
                StackPanel page = new StackPanel();
                page.Dock = DockStyle.Fill;
                page.Visible = i == 0;
                _pages[i] = page;
                _contentHost.Controls.Add(page);
            }

            CheckGroup? currentGroup = null;
            SectionCard currentCard = null;
            string currentSection = null;
            foreach (CheckDefinition def in CheckCatalog.All)
            {
                if (!currentGroup.HasValue || currentGroup.Value != def.Group)
                {
                    currentGroup = def.Group;
                    currentCard = null;
                    currentSection = null;
                }
                if (currentCard == null || !string.Equals(currentSection, def.Section, StringComparison.Ordinal))
                {
                    currentSection = def.Section;
                    currentCard = new SectionCard(def.Section);
                    _pages[(int)def.Group].AddCard(currentCard);
                    _cards.Add(currentCard);
                }
                CheckRow row = new CheckRow(def);
                row.Tag = currentCard;
                currentCard.AddRow(row);
                _rows[def.Id] = row;
            }

            _footer = new FooterPanel();
            _footer.SetStatus("准备检测", "只读检测，不会修改系统设置");
            _footer.SetButtonsEnabled(false, false);
            _footer.RecheckButton.Click += delegate { StartScan(); };
            _footer.CopyButton.Click += delegate { CopyAll(); };
            _footer.ExportButton.Click += delegate { ExportReport(); };
            _root.Controls.Add(_footer);

            _tabBar.Select(0);
        }

        private void ConstrainToScreen()
        {
            try
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                int maxW = Math.Max(600, work.Width - Theme.S(20));
                int maxH = Math.Max(500, work.Height - Theme.S(50));
                if (ClientSize.Width > maxW || ClientSize.Height > maxH)
                    ClientSize = new Size(Math.Min(ClientSize.Width, maxW), Math.Min(ClientSize.Height, maxH));
                MinimumSize = new Size(
                    Math.Min(Theme.S(920), maxW),
                    Math.Min(Theme.S(660), maxH));
            }
            catch
            {
            }
        }

        private void LayoutRoot()
        {
            if (_root == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int margin = Theme.S(22);
            int cardWidth = Math.Max(Theme.S(400), w - margin * 2);
            _header.SetBounds(margin, Theme.S(16), cardWidth, Theme.S(88));
            _conclusion.SetBounds(margin, Theme.S(114), cardWidth, Theme.S(102));
            _tabBar.SetBounds(margin, Theme.S(226), cardWidth, Theme.S(50));
            int footerHeight = Theme.S(66);
            _footer.SetBounds(0, h - footerHeight, w, footerHeight);
            int contentTop = Theme.S(280);
            _contentHost.SetBounds(0, contentTop, w, Math.Max(Theme.S(120), h - footerHeight - contentTop));
        }

        private void SwitchPage(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == index;
            _tabBar.SetToggleText(_pages[index].AllCollapsed() ? "全部展开" : "全部折叠");
            _pages[index].Focus();
        }

        private void ToggleAllSections()
        {
            StackPanel page = _pages[_tabBar.SelectedIndex];
            bool collapse = !page.AllCollapsed();
            page.SetAllCollapsed(collapse);
            _tabBar.SetToggleText(collapse ? "全部展开" : "全部折叠");
        }

        private void StartScan()
        {
            if (_closed || _running) return;
            _running = true;
            _snapshot = null;
            _summary = null;
            _received = 0;
            _scanWatch = Stopwatch.StartNew();
            int generation = ++_generation;

            if (_runner != null)
            {
                _runner.Close();
                _runner = null;
            }

            foreach (CheckRow row in _rows.Values) row.Reset();
            foreach (SectionCard card in _cards) card.RefreshCounts();
            for (int i = 0; i < 5; i++) _tabBar.SetTabIssues(i, 0, false);
            _conclusion.SetScanning(0, Total, 0);
            _footer.SetButtonsEnabled(false, false);
            _footer.SetStatus("正在检测… 已读取 0 / " + Total + " 项", "只读检测，不会修改系统设置");
            _header.SetInfo(Environment.MachineName, "正在读取系统信息", "只读检测");

            ProbeRunner runner = new ProbeRunner();
            _runner = runner;
            runner.ItemReady += delegate(CheckResult result)
            {
                Post(generation, delegate { ApplyResult(result); });
            };
            runner.Completed += delegate(ScanSnapshot snapshot)
            {
                Post(generation, delegate { FinishScan(snapshot); });
            };
            _uiTimer.Start();
            runner.Start();
        }

        private void Post(int generation, Action action)
        {
            if (_closed || generation != _generation) return;
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (_closed || generation != _generation) return;
                    action();
                }));
            }
            catch
            {
            }
        }

        private void ApplyResult(CheckResult result)
        {
            CheckRow row;
            if (!_rows.TryGetValue(result.Id, out row)) return;
            bool wasPending = !row.HasResult;
            row.SetResult(result);
            SectionCard card = row.Tag as SectionCard;
            if (card != null) card.RefreshCounts();
            if (wasPending) _received++;
            if (_running)
            {
                _conclusion.SetScanning(_received, Total, _scanWatch.Elapsed.TotalSeconds);
                _footer.SetStatus(string.Format("正在检测… 已读取 {0} / {1} 项", _received, Total),
                    "只读检测，不会修改系统设置 · 已用时 " + _scanWatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
            }
        }

        private void OnUiTick()
        {
            if (!_running)
            {
                _uiTimer.Stop();
                return;
            }
            _conclusion.SetScanning(_received, Total, _scanWatch.Elapsed.TotalSeconds);
            _footer.SetStatus(string.Format("正在检测… 已读取 {0} / {1} 项", _received, Total),
                "只读检测，不会修改系统设置 · 已用时 " + _scanWatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
        }

        private void FinishScan(ScanSnapshot snapshot)
        {
            _running = false;
            _uiTimer.Stop();
            _scanWatch.Stop();
            double totalSeconds = (DateTime.Now - _startedAt).TotalSeconds;
            ScanSnapshot displaySnapshot = new ScanSnapshot(snapshot.Results, snapshot.Metrics,
                TimeSpan.FromSeconds(totalSeconds), snapshot.FinishedAt);
            _snapshot = displaySnapshot;
            _summary = SummaryBuilder.Build(displaySnapshot);

            foreach (CheckResult result in displaySnapshot.Results)
            {
                CheckRow row;
                if (!_rows.TryGetValue(result.Id, out row)) continue;
                row.SetResult(result);
                SectionCard card = row.Tag as SectionCard;
                if (card != null) card.RefreshCounts();
            }
            _received = displaySnapshot.Results.Count;

            _conclusion.SetSummary(_summary);
            _footer.SetButtonsEnabled(true, true);
            _footer.SetStatus(
                string.Format("检测完成 · 总用时 {0:0.0} 秒", totalSeconds),
                "只读检测，不会修改系统设置 · 最后检测 " +
                snapshot.FinishedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture));

            string os = FindValue("os.name");
            string privilege = FindValue("sec.priv");
            if (privilege.Length == 0) privilege = "只读检测";
            _header.SetInfo(Environment.MachineName,
                (os.Length > 0 ? os : "系统信息已读取") + " · " + privilege,
                privilege + " · 只读");
            UpdateTabIssues();
            if (_tabBar.SelectedIndex >= 0 && _tabBar.SelectedIndex < 5)
                _tabBar.SetToggleText(_pages[_tabBar.SelectedIndex].AllCollapsed() ? "全部展开" : "全部折叠");
        }

        private void UpdateTabIssues()
        {
            if (_snapshot == null) return;
            for (int g = 0; g < 5; g++)
            {
                int issues = 0;
                bool critical = false;
                foreach (CheckResult r in _snapshot.Results)
                {
                    CheckDefinition def = CheckCatalog.Get(r.Id);
                    if (def == null || (int)def.Group != g) continue;
                    if (r.Status == CheckStatus.Warning) issues++;
                    else if (r.Status == CheckStatus.Critical) { issues++; critical = true; }
                }
                _tabBar.SetTabIssues(g, issues, critical);
            }
        }

        private string FindValue(string id)
        {
            if (_snapshot == null) return string.Empty;
            foreach (CheckResult r in _snapshot.Results)
                if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) return r.Value;
            return string.Empty;
        }

        public void ShowItemDetails(CheckRow row)
        {
            if (row == null || row.Result == null) return;
            using (ItemDetailForm form = new ItemDetailForm(row.Definition, row.Result))
            {
                form.ShowDialog(this);
            }
        }

        private void CopyAll()
        {
            if (_snapshot == null || _summary == null) return;
            try
            {
                string text = ReportWriter.BuildText(_snapshot, _summary);
                Clipboard.SetText(text);
                _footer.SetStatus("已复制全文", "可直接粘贴到记事本、聊天窗口或文档中 · 内容不上传");
            }
            catch
            {
                MessageBox.Show(this, "未能复制到剪贴板，请稍后重试。", "复制失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExportReport()
        {
            if (_snapshot == null || _summary == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出 PCCheck 报告";
                dialog.Filter = "文本报告 (*.txt)|*.txt|网页报告 (*.html)|*.html";
                dialog.FilterIndex = 1;
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = ReportWriter.DefaultFileName(".txt");
                try { dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
                catch { }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    bool html = ext == ".html" || ext == ".htm";
                    string content = html
                        ? ReportWriter.BuildHtml(_snapshot, _summary)
                        : ReportWriter.BuildText(_snapshot, _summary);
                    File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
                    _footer.SetStatus("报告已导出", "文件仅保存在您选择的位置 · 未上传");
                    MessageBox.Show(this,
                        "报告已导出到：\r\n" + dialog.FileName + "\r\n\r\n报告只保存在本机，未上传任何数据。",
                        "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch
                {
                    MessageBox.Show(this, "未能写入所选位置，请换一个可写目录后重试。", "导出失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void Shutdown()
        {
            if (_closed) return;
            _closed = true;
            _generation++;
            try { _uiTimer.Stop(); } catch { }
            try { _startTimer.Stop(); } catch { }
            if (_runner != null)
            {
                _runner.Close();
                _runner = null;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5 && !_running && !_closed)
            {
                StartScan();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
