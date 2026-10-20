using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ATK_UT70DLL;

namespace UT70Dashboard
{
    internal sealed partial class DashboardForm
    {
        private void BuildDashboard()
        {
            SuspendLayout();
            TableLayoutPanel page = Grid(1, 4);
            page.Padding = new Padding(24, 16, 24, 20);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            page.Controls.Add(BuildHeader(), 0, 0);

            TableLayoutPanel metrics = Grid(5, 1);
            metrics.Margin = new Padding(0, 0, 0, 14);
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            _voltageTile = new MetricTile("BUS VOLTAGE", "电压", "V", Theme.Cyan);
            _currentTile = new MetricTile("OUTPUT CURRENT", "电流", "A", Theme.Lime);
            _powerTile = new MetricTile("ACTIVE POWER", "功率", "W", Theme.Amber);
            metrics.Controls.Add(_voltageTile, 0, 0);
            metrics.Controls.Add(_currentTile, 2, 0);
            metrics.Controls.Add(_powerTile, 4, 0);
            page.Controls.Add(metrics, 0, 1);

            TableLayoutPanel main = Grid(2, 1);
            main.Margin = new Padding(0, 0, 0, 14);
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            main.Controls.Add(BuildChartCard(), 0, 0);
            main.Controls.Add(BuildEnergyCard(), 1, 0);
            page.Controls.Add(main, 0, 2);

            TableLayoutPanel bottom = Grid(2, 1);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            bottom.Controls.Add(BuildProtocolCard(), 0, 0);
            bottom.Controls.Add(BuildDeviceLogCard(), 1, 0);
            page.Controls.Add(bottom, 0, 3);
            Controls.Add(page);
            ResumeLayout(true);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = Grid(2, 1);
            header.Margin = new Padding(0, 0, 0, 14);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 345));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Panel identity = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label logo = FillLabel("70", 18F, Theme.Back, FontStyle.Bold);
            logo.BackColor = Theme.Cyan;
            logo.Dock = DockStyle.None;
            logo.Bounds = new Rectangle(0, 7, 46, 46);
            logo.TextAlign = ContentAlignment.MiddleCenter;
            Label brand = Ui.Label("UT70  POWER LAB", 18F, Theme.Text, FontStyle.Bold);
            brand.Location = new Point(60, 7);
            Label subtitle = Ui.Label("USB POWER ANALYZER  /  实时监控", 8F, Theme.Muted, FontStyle.Regular);
            subtitle.Location = new Point(62, 39);
            identity.Controls.Add(logo); identity.Controls.Add(brand); identity.Controls.Add(subtitle);
            header.Controls.Add(identity, 0, 0);

            TableLayoutPanel actions = Grid(4, 1);
            actions.Padding = new Padding(4, 10, 0, 10);
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            _clockLabel = FillLabel(DateTime.Now.ToString("MM.dd  HH:mm:ss"), 9F, Theme.Muted, FontStyle.Regular);
            _clockLabel.TextAlign = ContentAlignment.MiddleRight;
            _clockLabel.Padding = new Padding(0, 0, 18, 0);
            actions.Controls.Add(_clockLabel, 0, 0);
            FlowLayoutPanel status = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty,
                Padding = new Padding(8, 12, 0, 0), BackColor = Theme.Back
            };
            _headerDot = new StatusDot { Size = new Size(14, 14), Margin = new Padding(0, 0, 6, 0) };
            _headerStateLabel = Ui.Label("设备未连接", 9F, Theme.Muted, FontStyle.Regular);
            _headerStateLabel.Margin = new Padding(0, 0, 0, 0);
            status.Controls.Add(_headerDot); status.Controls.Add(_headerStateLabel);
            actions.Controls.Add(status, 1, 0);
            _fullscreenButton = new ActionButton { Text = "全屏 F11", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            _fullscreenButton.Click += delegate { ToggleFullscreen(); };
            _connectButton = new ActionButton { Text = "连接设备", Accent = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            _connectButton.Click += OnConnectClick;
            actions.Controls.Add(_fullscreenButton, 2, 0);
            actions.Controls.Add(_connectButton, 3, 0);
            header.Controls.Add(actions, 1, 0);
            return header;
        }

        private Control BuildChartCard()
        {
            DashboardCard card = Card(true);
            TableLayoutPanel content = Grid(1, 3);
            content.BackColor = Theme.Surface;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            TableLayoutPanel heading = Grid(2, 1);
            heading.BackColor = Theme.Surface;
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 247));
            heading.Controls.Add(Heading("SIGNAL TRACE", "实时曲线"), 0, 0);
            FlowLayoutPanel selectors = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false,
                BackColor = Theme.Surface, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty
            };
            _voltageSelector = SmallButton("V", true);
            _currentSelector = SmallButton("I", false);
            _powerSelector = SmallButton("P", false);
            _voltageSelector.Click += delegate { SelectChart(ChartMetric.Voltage); };
            _currentSelector.Click += delegate { SelectChart(ChartMetric.Current); };
            _powerSelector.Click += delegate { SelectChart(ChartMetric.Power); };
            selectors.Controls.Add(_voltageSelector); selectors.Controls.Add(_currentSelector); selectors.Controls.Add(_powerSelector);
            _windowCombo = DarkCombo();
            _windowCombo.Width = 76;
            _windowCombo.Margin = new Padding(7, 2, 0, 0);
            _windowCombo.Items.AddRange(new object[] { "10 秒", "30 秒", "60 秒" });
            _windowCombo.SelectedIndex = 1;
            _windowCombo.SelectedIndexChanged += delegate
            {
                if (_windowCombo.SelectedIndex < 0) return;
                _windowTicks = new long[] { 10, 30, 60 }[_windowCombo.SelectedIndex] * TimeSpan.TicksPerSecond;
                _renderedVersion = -1;
            };
            selectors.Controls.Add(_windowCombo);
            heading.Controls.Add(selectors, 1, 0);
            _chart = new TelemetryChart { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _chart.WindowScrollRequested += OnChartWindowScroll;
            _chart.RangeSelected += OnChartRangeSelected;
            _chart.Resize += delegate { _renderedVersion = -1; };

            TableLayoutPanel footer = Grid(2, 1);
            footer.BackColor = Theme.Surface;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            FlowLayoutPanel live = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface, Margin = Padding.Empty, Padding = new Padding(2, 4, 0, 0) };
            _chartDot = new StatusDot { Size = new Size(12, 12), DotColor = Theme.Faint, Margin = new Padding(0, 0, 6, 0) };
            _chartStateLabel = Ui.Label("离线 · 等待连接", 8F, Theme.Muted, FontStyle.Regular);
            live.Controls.Add(_chartDot); live.Controls.Add(_chartStateLabel);
            _chartSummary = FillLabel("时间基准：电脑接收时间", 8F, Theme.Muted, FontStyle.Regular);
            _chartSummary.TextAlign = ContentAlignment.MiddleRight;
            footer.Controls.Add(live, 0, 0); footer.Controls.Add(_chartSummary, 1, 0);
            content.Controls.Add(heading, 0, 0); content.Controls.Add(_chart, 0, 1); content.Controls.Add(footer, 0, 2);
            card.Controls.Add(content);
            return card;
        }

        private Control BuildEnergyCard()
        {
            DashboardCard card = Card(false);
            TableLayoutPanel content = Grid(1, 5);
            content.BackColor = Theme.Surface;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            content.Controls.Add(Heading("ENERGY & CHARGE", "测量累计"), 0, 0);
            TableLayoutPanel counters = Grid(2, 1);
            counters.BackColor = Theme.Surface;
            counters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            counters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            counters.Controls.Add(CounterReadout("累计能量", "能量 / Wh", Theme.Cyan,
                out _whValueLabel, out _whCaptionLabel), 0, 0);
            counters.Controls.Add(CounterReadout("累计电量", "电量 / Ah", Theme.Lime,
                out _ahValueLabel, out _ahCaptionLabel), 1, 0);
            content.Controls.Add(counters, 0, 1);

            TableLayoutPanel measurements = Grid(3, 1);
            measurements.BackColor = Theme.Surface;
            for (int i = 0; i < 3; i++) measurements.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            measurements.Controls.Add(Readout("阻值 / Ω", Theme.Muted, 11, out _resistanceValueLabel, out _resistanceCaptionLabel), 0, 0);
            measurements.Controls.Add(Readout("D+ / V", Theme.Muted, 11, out _dpValueLabel, out _dpCaptionLabel), 1, 0);
            measurements.Controls.Add(Readout("D− / V", Theme.Muted, 11, out _dmValueLabel, out _dmCaptionLabel), 2, 0);
            content.Controls.Add(measurements, 0, 2);

            TableLayoutPanel frequency = Grid(2, 1);
            frequency.BackColor = Theme.Surface;
            frequency.Padding = new Padding(0, 5, 0, 5);
            frequency.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            frequency.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            frequency.Controls.Add(FillLabel("设备采样率", 8.5F, Theme.Muted, FontStyle.Regular), 0, 0);
            _frequencyCombo = DarkCombo();
            _frequencyCombo.Dock = DockStyle.Fill;
            _frequencyCombo.Items.AddRange(new object[] { "4 SPS", "10 SPS", "50 SPS", "200 SPS", "500 SPS", "1000 SPS" });
            _frequencyCombo.SelectedIndexChanged += OnFrequencySelection;
            frequency.Controls.Add(_frequencyCombo, 1, 0);
            content.Controls.Add(frequency, 0, 3);

            TableLayoutPanel actions = Grid(2, 1);
            actions.BackColor = Theme.Surface;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _monitorButton = new ActionButton { Text = "设备监控页", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 7, 0) };
            _monitorButton.Click += delegate { SetDeviceUi(DevUI.MONITOR); };
            _resetButton = new ActionButton { Text = "重置累计", Dock = DockStyle.Fill, Margin = Padding.Empty };
            _resetButton.Click += OnResetCounters;
            actions.Controls.Add(_monitorButton, 0, 0); actions.Controls.Add(_resetButton, 1, 0);
            content.Controls.Add(actions, 0, 4);
            card.Controls.Add(content);
            return card;
        }

        private Control BuildProtocolCard()
        {
            DashboardCard card = Card(true);
            TableLayoutPanel content = Grid(1, 5);
            content.BackColor = Theme.Surface;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel heading = Grid(2, 1);
            heading.BackColor = Theme.Surface;
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            heading.Controls.Add(Heading("FAST CHARGE ANALYSIS", "快充协议检测"), 0, 0);
            _protocolScanButton = new ActionButton { Text = "自动检测", Accent = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 12) };
            _protocolScanButton.Click += OnProtocolScan;
            heading.Controls.Add(_protocolScanButton, 1, 0);
            content.Controls.Add(heading, 0, 0);
            FlowLayoutPanel chips = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Surface, Margin = Padding.Empty };
            string[] names = { "BC1.2", "QC2", "QC3", "FCP", "SCP", "AFC", "PE1", "PE2", "VOOC", "PD", "DASH", "E-Marker", "VFCP", "Apple 2.4A" };
            foreach (string name in names)
            {
                ProtocolChip chip = new ProtocolChip(name) { Margin = new Padding(0, 0, 6, 6) };
                _protocolChips.Add(name, chip); chips.Controls.Add(chip);
            }
            content.Controls.Add(chips, 0, 1);
            _protocolHeadline = FillLabel("等待协议检测", 9F, Theme.Text, FontStyle.Bold);
            _protocolValues = FillLabel("电压能力  —", 8.5F, Theme.Cyan, FontStyle.Regular);
            _protocolDescription = FillLabel("点击自动检测，查看设备返回的协议能力。", 8F, Theme.Muted, FontStyle.Regular);
            _protocolDescription.TextAlign = ContentAlignment.TopLeft;
            content.Controls.Add(_protocolHeadline, 0, 2);
            content.Controls.Add(_protocolValues, 0, 3);
            content.Controls.Add(_protocolDescription, 0, 4);
            card.Controls.Add(content);
            return card;
        }

        private Control BuildDeviceLogCard()
        {
            DashboardCard card = Card(false);
            TableLayoutPanel content = Grid(1, 4);
            content.BackColor = Theme.Surface;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel heading = Grid(2, 1);
            heading.BackColor = Theme.Surface;
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            heading.Controls.Add(Heading("DEVICE SESSION", "设备与活动"), 0, 0);
            _refreshInfoButton = new ActionButton { Text = "刷新信息", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 12) };
            _refreshInfoButton.Click += delegate { RefreshDeviceInformation(); };
            heading.Controls.Add(_refreshInfoButton, 1, 0);
            content.Controls.Add(heading, 0, 0);
            TableLayoutPanel details = Grid(2, 2);
            details.BackColor = Theme.Surface;
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _serialLabel = FillLabel("SN  —", 8F, Theme.Muted, FontStyle.Regular);
            _hardwareLabel = FillLabel("硬件  —", 8F, Theme.Muted, FontStyle.Regular);
            _deviceModeLabel = FillLabel("设备界面  —", 8F, Theme.Muted, FontStyle.Regular);
            _pinLabel = FillLabel("通讯  —", 8F, Theme.Muted, FontStyle.Regular);
            details.Controls.Add(_serialLabel, 0, 0); details.Controls.Add(_hardwareLabel, 1, 0);
            details.Controls.Add(_deviceModeLabel, 0, 1); details.Controls.Add(_pinLabel, 1, 1);
            content.Controls.Add(details, 0, 1);
            _sessionLabel = FillLabel("最近活动", 8F, Theme.Muted, FontStyle.Regular);
            content.Controls.Add(_sessionLabel, 0, 2);
            _logList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty
            };
            _logList.SizeChanged += delegate { ResizeLogRows(); };
            content.Controls.Add(_logList, 0, 3);
            card.Controls.Add(content);
            return card;
        }

        private static TableLayoutPanel Grid(int columns, int rows)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, BackColor = Theme.Back, Margin = Padding.Empty, Padding = Padding.Empty };
            if (rows == 1) panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            if (columns == 1) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }

        private static DashboardCard Card(bool gutter)
        {
            return new DashboardCard { Dock = DockStyle.Fill, Margin = gutter ? new Padding(0, 0, 14, 0) : Padding.Empty, Padding = new Padding(17, 14, 17, 13) };
        }

        private static Control Heading(string caption, string title)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty };
            Label cap = Ui.Label(caption, 7.5F, Theme.Muted, FontStyle.Bold);
            cap.Location = new Point(0, 0);
            Label label = Ui.Label(title, 13F, Theme.Text, FontStyle.Bold);
            label.Location = new Point(0, 17);
            panel.Controls.Add(cap); panel.Controls.Add(label);
            return panel;
        }

        private static Control Readout(string caption, Color color, float size, out Label value, out Label captionLabel)
        {
            TableLayoutPanel panel = Grid(1, 2);
            panel.BackColor = Theme.Surface;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            captionLabel = FillLabel(caption, 8F, color, FontStyle.Regular);
            captionLabel.BackColor = Theme.Surface;
            panel.Controls.Add(captionLabel, 0, 0);
            value = new ReadoutValueLabel { Text = "--.---", ForeColor = Theme.Text };
            value.Font = new Font("Consolas", size, FontStyle.Regular);
            panel.Controls.Add(value, 0, 1);
            return panel;
        }

        private static Control CounterReadout(string title, string unitCaption, Color accent,
            out Label value, out Label captionLabel)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty };
            TableLayoutPanel content = Grid(1, 2);
            content.BackColor = Theme.Surface;
            content.Padding = new Padding(12, 4, 8, 2);
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            captionLabel = FillLabel(title + "  /  " + unitCaption, 8F, accent, FontStyle.Bold);
            captionLabel.BackColor = Theme.Surface;
            content.Controls.Add(captionLabel, 0, 0);

            value = new ReadoutValueLabel
            {
                Text = "--.---",
                ForeColor = Theme.Text,
                Font = new Font("Consolas", 27F, FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 2)
            };
            content.Controls.Add(value, 0, 1);
            panel.Controls.Add(content);

            Panel accentMark = new Panel
            {
                BackColor = accent,
                Bounds = new Rectangle(0, 15, 3, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            panel.Controls.Add(accentMark);
            return panel;
        }

        private static Label FillLabel(string text, float size, Color color, FontStyle style)
        {
            Label label = Ui.Label(text, size, color, style);
            label.AutoSize = false; label.AutoEllipsis = true;
            label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static DarkSelector DarkCombo()
        {
            return new DarkSelector { Font = new Font("Segoe UI", 8.5F), Margin = Padding.Empty };
        }

        private static ActionButton SmallButton(string title, bool active)
        {
            return new ActionButton { Text = title, Accent = active, Size = new Size(45, 29), Margin = new Padding(0, 0, 5, 0) };
        }

        private void SelectChart(ChartMetric metric)
        {
            _chart.Metric = metric;
            _voltageSelector.Accent = metric == ChartMetric.Voltage;
            _currentSelector.Accent = metric == ChartMetric.Current;
            _powerSelector.Accent = metric == ChartMetric.Power;
            _renderedVersion = -1;
        }
    }

    internal sealed class MetricTile : DashboardCard
    {
        private string _value = "--.---", _subtitle = "等待设备数据";
        private long _nextPeakTimestamp;
        private bool _hasPeak;
        private readonly string _caption, _title, _baseUnit;
        private string _unit;
        private readonly Color _accent;
        private readonly Sparkline _spark;

        public MetricTile(string caption, string title, string unit, Color accent)
        {
            _caption = caption; _title = title; _baseUnit = _unit = unit; _accent = accent;
            Dock = DockStyle.Fill;
            _spark = new Sparkline { LineColor = accent, Size = new Size(92, 30) };
            Controls.Add(_spark);
            Resize += delegate
            {
                _spark.Visible = Width >= 355;
                _spark.Location = new Point(Width - 110, Height - 50);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            float scale = g.DpiX / 96F;
            using (SolidBrush accent = new SolidBrush(_accent))
            using (SolidBrush text = new SolidBrush(Theme.Text))
            using (SolidBrush muted = new SolidBrush(Theme.Muted))
            using (Font caption = new Font("Segoe UI", 7.5F, FontStyle.Bold))
            using (Font local = new Font("Segoe UI", 9F))
            using (Font value = new Font("Consolas", 29F))
            using (Font unit = new Font("Segoe UI", 11F, FontStyle.Bold))
            {
                g.FillRectangle(accent, 0, 23 * scale, 3 * scale, Height - 46 * scale);
                g.DrawString(_caption, caption, muted, 18 * scale, 12 * scale);
                SizeF titleSize = g.MeasureString(_title, local);
                g.DrawString(_title, local, muted, Width - titleSize.Width - 18 * scale, 12 * scale);
                SizeF number = g.MeasureString(_value, value);
                g.DrawString(_value, value, text, 14 * scale, 34 * scale);
                g.DrawString(_unit, unit, accent, 14 * scale + number.Width, 53 * scale);
                g.DrawString(_subtitle, caption, muted, 18 * scale, Height - 26 * scale);
            }
        }

        public void SetValue(double value, double peak)
        {
            long timestamp = Stopwatch.GetTimestamp();
            MeasurementText display = DisplayUnits.Format(value, _baseUnit);
            string subtitle = _subtitle;
            if (!_hasPeak || timestamp >= _nextPeakTimestamp)
            {
                subtitle = "峰值 " + DisplayUnits.Format(peak, _baseUnit).Text;
                _hasPeak = true;
                _nextPeakTimestamp = timestamp + Stopwatch.Frequency;
            }
            if (_value == display.Number && _unit == display.Unit && _subtitle == subtitle) return;
            _value = display.Number; _unit = display.Unit;
            _subtitle = subtitle;
            Invalidate();
        }
        public void AddSpark(float value) { _spark.Add(value); }
        public void SetSubtitle(string subtitle) { if (_subtitle == subtitle) return; _subtitle = subtitle; Invalidate(); }
        public void Clear()
        {
            _value = "--.---"; _unit = _baseUnit; _subtitle = "等待设备数据";
            _nextPeakTimestamp = 0;
            _hasPeak = false;
            _spark.Clear(); Invalidate();
        }
    }

    internal sealed class ProtocolChip : Control
    {
        private bool _active, _failed;
        public bool Active { get { return _active; } set { _active = value; Invalidate(); } }
        public bool Failed { get { return _failed; } set { _failed = value; Invalidate(); } }

        public ProtocolChip(string text)
        {
            Text = text; AccessibleName = text;
            Size = new Size(text.Length > 8 ? 87 : 61, 25);
            BackColor = Theme.Surface;
            Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = Failed ? Theme.Amber : Active ? Theme.Cyan : Theme.Muted;
            using (GraphicsPath path = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 6))
            using (SolidBrush brush = new SolidBrush(Active || Failed ? Color.FromArgb(24, 42, 45) : Theme.SurfaceRaised))
            using (Pen pen = new Pen(Active || Failed ? color : Theme.Border))
            {
                e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
