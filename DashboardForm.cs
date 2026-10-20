using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using ATK_UT70DLL;

namespace UT70Dashboard
{
    internal sealed partial class DashboardForm : Form
    {
        private readonly ATKUT70 _device;
        private readonly object _apiLock = new object();
        private readonly System.Windows.Forms.Timer _clockTimer;
        private readonly ToolTip _tooltips = new ToolTip();
        private readonly NotifyIcon _taskbarNotifyIcon;
        private readonly TaskbarReadoutForm _taskbarReadout;
        private TelemetryBuffer _buffer = new TelemetryBuffer();
        private TelemetrySnapshot _snapshot;
        private volatile bool _acceptData;
        private bool _connected, _connectionBusy, _commandBusy, _exitReady;
        private volatile bool _closing;
        private bool _loadingFrequency, _fullScreen;
        private int _confirmedFrequencyIndex = -1;
        private long _windowTicks = 30L * TimeSpan.TicksPerSecond;
        private long _renderedVersion = -1, _clockSecond, _ratePreviousTotal, _rateStarted;
        private long _readoutVersion = -1, _nextReadoutAt;
        private long _nextCounterReadoutAt;
        private double _receiveRate;
        private Rectangle _savedBounds;
        private FormWindowState _savedWindowState;
        private System.Windows.Forms.Timer _shutdownTimer;

        private Label _clockLabel, _headerStateLabel, _chartStateLabel, _chartSummary, _sessionLabel;
        private Label _whValueLabel, _ahValueLabel, _resistanceValueLabel, _dpValueLabel, _dmValueLabel;
        private Label _whCaptionLabel, _ahCaptionLabel, _resistanceCaptionLabel, _dpCaptionLabel, _dmCaptionLabel;
        private Label _hardwareLabel, _serialLabel, _deviceModeLabel, _pinLabel;
        private Label _protocolHeadline, _protocolValues, _protocolDescription;
        private StatusDot _headerDot, _chartDot;
        private ActionButton _connectButton, _fullscreenButton, _monitorButton, _resetButton, _protocolScanButton;
        private ActionButton _refreshInfoButton;
        private ActionButton _voltageSelector, _currentSelector, _powerSelector;
        private MetricTile _voltageTile, _currentTile, _powerTile;
        private TelemetryChart _chart;
        private DarkSelector _frequencyCombo, _windowCombo;
        private FlowLayoutPanel _logList;
        private readonly Dictionary<string, ProtocolChip> _protocolChips = new Dictionary<string, ProtocolChip>();

        public DashboardForm()
        {
            Text = "UT70 Power Lab | 实时监控";
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1440, 900);
            MinimumSize = new Size(1120, 720);
            BackColor = Theme.Back; ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            KeyPreview = true;
            _device = new ATKUT70();
            _taskbarReadout = new TaskbarReadoutForm();
            _taskbarNotifyIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "UT70 Power Lab" };
            _taskbarNotifyIcon.MouseDoubleClick += delegate
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show(); Activate();
                ShowTaskbarReadout();
            };
            SubscribeDeviceEvents();
            BuildDashboard();
            _clockTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _clockTimer.Tick += OnRefreshTick;
            _clockTimer.Start();
            _rateStarted = Stopwatch.GetTimestamp();
            FormClosing += OnFormClosing;
            Shown += delegate
            {
                _taskbarNotifyIcon.Visible = true;
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                ShowTaskbarReadout();
                AppendLog("正在自动连接 UT70…", Theme.Amber);
                OnConnectClick(this, EventArgs.Empty);
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F11 || (e.KeyCode == Keys.Escape && _fullScreen))
                {
                    ToggleFullscreen(); e.Handled = true; e.SuppressKeyPress = true;
                }
            };
            UpdateConnectionUi(false, "设备未连接");
            AppendLog("面板已就绪，连接 UT70 开始测量", Theme.Muted);
        }

        private void SubscribeDeviceEvents()
        {
            _device.ReceCVDataEvent += OnReceiveData;
            _device.DevSNEvent += OnDeviceSerial;
            _device.DevUIChanageEvent += OnDeviceUiChanged;
            _device.FrequencyEvent += OnFrequencyChanged;
            _device.PinEvent += OnPinChanged;
            _device.FastChargeEvent += OnFastCharge;
            _device.FastChargeDescEvent += OnFastChargeDescription;
        }

        private void UnsubscribeDeviceEvents()
        {
            _device.ReceCVDataEvent -= OnReceiveData;
            _device.DevSNEvent -= OnDeviceSerial;
            _device.DevUIChanageEvent -= OnDeviceUiChanged;
            _device.FrequencyEvent -= OnFrequencyChanged;
            _device.PinEvent -= OnPinChanged;
            _device.FastChargeEvent -= OnFastCharge;
            _device.FastChargeDescEvent -= OnFastChargeDescription;
        }

        private void OnRefreshTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            long second = now.Ticks / TimeSpan.TicksPerSecond;
            bool updateStatus = _clockSecond != second;
            if (updateStatus)
            {
                _clockSecond = second;
                _clockLabel.Text = now.ToLocalTime().ToString("MM.dd  HH:mm:ss");
                double elapsed = (Stopwatch.GetTimestamp() - _rateStarted) / (double)Stopwatch.Frequency;
                long total = _buffer.Total;
                _receiveRate = elapsed > 0 ? Math.Max(0, total - _ratePreviousTotal) / elapsed : 0;
                _ratePreviousTotal = total; _rateStarted = Stopwatch.GetTimestamp();
                if (_connected && !_connectionBusy && !_commandBusy && !_device.ConnState)
                {
                    _acceptData = false;
                    UpdateConnectionUi(false, "连接已断开");
                    AppendLog("设备连接已断开，请检查 USB", Theme.Amber);
                }
            }
            if (_renderedVersion != _buffer.Version)
            {
                // 接收线程只入缓冲，曲线最多每秒刷新 20 次。
                _snapshot = _buffer.Capture(now, _windowTicks, _chart.Metric, Math.Max(200, _chart.Width * 2));
                _renderedVersion = _snapshot.Version;
                _chart.SetData(_snapshot);
                ChartSample latest = _snapshot.Latest;
                if (latest != null)
                {
                    _voltageTile.AddSpark(latest.Voltage);
                    _currentTile.AddSpark(latest.Current);
                    _powerTile.AddSpark(latest.Power);
                }
            }
            long timestamp = Stopwatch.GetTimestamp();
            if (_snapshot != null && _snapshot.Latest != null)
            {
                ChartSample latest = _snapshot.Latest;
                UpdatePrimaryMeasurements(_snapshot);
                UpdateMeasurementCounters(latest, timestamp);
                if (_readoutVersion != _snapshot.Version && timestamp >= _nextReadoutAt)
                {
                    // 电阻与 D+/D- 等辅助读数每秒更新；原始样本和峰值仍完整统计。
                    UpdateReadout(_resistanceValueLabel, _resistanceCaptionLabel, "阻值", latest.Resistance, "Ω");
                    UpdateReadout(_dpValueLabel, _dpCaptionLabel, "D+", latest.Dp, "V");
                    UpdateReadout(_dmValueLabel, _dmCaptionLabel, "D−", latest.Dm, "V");
                    _readoutVersion = _snapshot.Version;
                    _nextReadoutAt = timestamp + Stopwatch.Frequency;
                }
                if (updateStatus)
                {
                    _tooltips.SetToolTip(_sessionLabel, "本次接收 " + _snapshot.Total.ToString("N0") + " 点" +
                        (_snapshot.Rejected > 0 ? " · 无效 " + _snapshot.Rejected : ""));
                }
            }
            string chartSummary = FormatWindowDuration(_windowTicks) + "窗口  ·  时间基准：接收时间";
            if (_chartSummary.Text != chartSummary) _chartSummary.Text = chartSummary;
            UpdateFreshness(now);
        }

        private void UpdatePrimaryMeasurements(TelemetrySnapshot snapshot)
        {
            ChartSample latest = snapshot.Latest;
            if (latest == null) return;

            // 主卡片直接显示最新有效样本，峰值按缓冲区中的原始样本统计。
            _voltageTile.SetValue(latest.Voltage, snapshot.PeakV);
            _currentTile.SetValue(latest.Current, snapshot.PeakI);
            _powerTile.SetValue(latest.Power, snapshot.PeakP);
        }

        private void UpdateMeasurementCounters(ChartSample latest, long timestamp)
        {
            if (timestamp < _nextCounterReadoutAt) return;
            _nextCounterReadoutAt = timestamp + Stopwatch.Frequency / 2;
            // 只节流可见标签；原始样本仍完整写入缓冲并参与峰值统计。
            UpdateReadout(_whValueLabel, _whCaptionLabel, "能量", latest.Wh, "Wh");
            UpdateReadout(_ahValueLabel, _ahCaptionLabel, "电量", latest.Ah, "Ah");
            UpdateTaskbarMeasurementSummary(latest);
        }

        private void UpdateTaskbarMeasurementSummary(ChartSample latest)
        {
            MeasurementText current = DisplayUnits.Format(latest.Current, "A");
            MeasurementText charge = DisplayUnits.Format(latest.Ah, "Ah");
            string title = "UT70 | I:" + current.Text + " | Ah:" + charge.Text;
            if (Text != title) Text = title;
            _taskbarReadout.SetReadings(current.Text, charge.Text);
            string tooltip = "电流 " + current.Text + " | 电量 " + charge.Text;
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            if (_taskbarNotifyIcon.Text != tooltip) _taskbarNotifyIcon.Text = tooltip;
        }

        private void ShowTaskbarReadout()
        {
            _taskbarReadout.ShowAboveTaskbar(Screen.FromControl(this).WorkingArea);
        }

        private void OnChartWindowScroll(int direction)
        {
            // 只在 DateTime 可表示范围与 100ns 分辨率处饱和，不限制为固定秒数区间。
            double scaledTicks = _windowTicks * Math.Pow(1.25, direction);
            long nextTicks = scaledTicks >= DateTime.MaxValue.Ticks ? DateTime.MaxValue.Ticks :
                scaledTicks <= 1 ? 1 : (long)Math.Round(scaledTicks);
            if (nextTicks == _windowTicks) return;
            _windowTicks = nextTicks;
            _renderedVersion = -1;

            int presetIndex = nextTicks == 10L * TimeSpan.TicksPerSecond ? 0 :
                nextTicks == 30L * TimeSpan.TicksPerSecond ? 1 :
                nextTicks == 60L * TimeSpan.TicksPerSecond ? 2 : -1;
            if (presetIndex >= 0) _windowCombo.SelectedIndex = presetIndex;
            else _windowCombo.SetCustomText(FormatWindowDuration(nextTicks));
        }

        private void OnChartRangeSelected(DateTime startUtc, DateTime endUtc, ChartMetric metric)
        {
            _chart.SetRangeStatistics(_buffer.CalculateRangeStatistics(startUtc, endUtc, metric));
        }

        private static string FormatWindowDuration(long ticks)
        {
            double seconds = ticks / (double)TimeSpan.TicksPerSecond;
            if (seconds < 0.001) return (seconds * 1000000).ToString("0.#", CultureInfo.InvariantCulture) + " μs ";
            if (seconds < 1) return (seconds * 1000).ToString("0.#", CultureInfo.InvariantCulture) + " ms ";
            if (seconds < 60) return seconds.ToString("0.##", CultureInfo.InvariantCulture) + " 秒 ";
            if (seconds < 3600) return (seconds / 60).ToString("0.##", CultureInfo.InvariantCulture) + " 分 ";
            if (seconds < 86400) return (seconds / 3600).ToString("0.##", CultureInfo.InvariantCulture) + " 小时 ";
            if (seconds < 31536000) return (seconds / 86400).ToString("0.##", CultureInfo.InvariantCulture) + " 天 ";
            return (seconds / 31536000).ToString("0.##", CultureInfo.InvariantCulture) + " 年 ";
        }

        private void UpdateFreshness(DateTime now)
        {
            string state;
            Color color;
            if (_connectionBusy) { state = "正在连接或断开…"; color = Theme.Amber; }
            else if (!_connected) { state = "离线 · 保留最后读数"; color = Theme.Muted; }
            else if (_snapshot == null || _snapshot.Latest == null) { state = "已连接 · 等待数据"; color = Theme.Amber; }
            else
            {
                double age = Math.Max(0, (now - _snapshot.Latest.TimeUtc).TotalSeconds);
                state = age > 2 ? "数据超时 · " + age.ToString("0") + " 秒" : "实时 · " + _receiveRate.ToString("0") + " SPS";
                color = age > 2 ? Theme.Amber : Theme.Cyan;
            }
            _chartStateLabel.Text = state;
            _chartStateLabel.ForeColor = color;
            _chartDot.DotColor = color;
            string activity = !_connected ? "最近活动  /  等待连接" :
                _snapshot == null || _snapshot.Latest == null ? "最近活动  /  等待测量数据" :
                (_snapshot.Latest.TimeUtc.AddSeconds(2) < now ? "最近活动  /  数据超时" : "最近活动  /  正在接收");
            if (_sessionLabel.Text != activity) _sessionLabel.Text = activity;
        }

        private void OnReceiveData(List<CVDataModel> models)
        {
            if (!_acceptData || models == null || models.Count == 0) return;
            TelemetryBuffer destination = _buffer;
            DateTime receivedUtc = DateTime.UtcNow;
            ChartSample[] copy = new ChartSample[models.Count];
            for (int i = 0; i < models.Count; i++)
            {
                CVDataModel item = models[i];
                if (item != null) copy[i] = new ChartSample(receivedUtc, item.V, item.I, item.P, item.Wh, item.AH, item.R, item.Dp, item.Dm);
            }
            destination.Append(copy);
        }

        private void OnConnectClick(object sender, EventArgs e)
        {
            if (_connectionBusy || _commandBusy || _closing) return;
            bool disconnect = _connected;
            if (!disconnect) ClearTelemetry();
            _acceptData = !disconnect;
            _connectionBusy = true;
            UpdateConnectionUi(_connected, disconnect ? "正在断开…" : "正在连接…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception failure = null;
                bool connected = false;
                try
                {
                    lock (_apiLock)
                    {
                        if (disconnect) _device.Disconnect();
                        else _device.ConnDev();
                        connected = _device.ConnState;
                    }
                }
                catch (Exception ex) { failure = ex; connected = _device.ConnState; }
                bool result = connected;
                Exception error = failure;
                PostToUi(delegate
                {
                    _connectionBusy = false;
                    _acceptData = result;
                    UpdateConnectionUi(result, result ? "设备已连接" : "设备未连接");
                    if (error != null) AppendLog("连接操作异常：" + error.Message, Theme.Red);
                    else if (disconnect) AppendLog(result ? "设备仍处于连接状态" : "已断开 UT70 连接", result ? Theme.Amber : Theme.Muted);
                    else AppendLog(result ? "UT70 已连接，等待测量数据" : "未检测到 UT70，请检查 USB", result ? Theme.Cyan : Theme.Amber);
                    if (result && !disconnect) RefreshDeviceInformation();
                    if (!result)
                    {
                        _chart.SetEmptyHint(disconnect ? "设备已断开" : "未检测到设备，请检查 USB 连接");
                        _voltageTile.SetSubtitle("已断开 · 保留最后读数");
                        _currentTile.SetSubtitle("已断开 · 保留最后读数");
                        _powerTile.SetSubtitle("已断开 · 保留最后读数");
                    }
                });
            });
        }

        private void UpdateConnectionUi(bool connected, string text)
        {
            _connected = connected;
            _headerStateLabel.Text = text;
            _headerStateLabel.ForeColor = connected ? Theme.Cyan : Theme.Muted;
            _headerDot.DotColor = connected ? Theme.Cyan : Theme.Faint;
            _connectButton.Text = _connectionBusy ? "请稍候…" : connected ? "断开设备" : "连接设备";
            _connectButton.Accent = !connected;
            SetControlAvailability();
        }

        private void SetControlAvailability()
        {
            bool idle = !_connectionBusy && !_commandBusy && !_closing;
            _connectButton.Enabled = idle;
            _monitorButton.Enabled = _resetButton.Enabled = _protocolScanButton.Enabled = _frequencyCombo.Enabled = _refreshInfoButton.Enabled = _connected && idle;
        }

        private void OnDeviceSerial(byte version, string serial)
        {
            ConfirmDeviceInfoReply(DeviceInfoKind.Serial);
            PostToUi(delegate
            {
                _serialLabel.Text = "SN  " + (String.IsNullOrWhiteSpace(serial) ? "未提供" : serial);
                _tooltips.SetToolTip(_serialLabel, _serialLabel.Text);
                _hardwareLabel.Text = "硬件  V" + (version / 10F).ToString("0.0", CultureInfo.InvariantCulture);
                AppendLog("设备序列号：" + serial, Theme.Cyan);
            });
        }

        private void OnDeviceUiChanged(DevUI mode)
        {
            ConfirmDeviceInfoReply(DeviceInfoKind.Screen);
            PostToUi(delegate { _deviceModeLabel.Text = "界面  " + UiName(mode); AppendLog("设备界面：" + UiName(mode), Theme.Muted); });
        }

        private void OnFrequencyChanged(Frequency frequency)
        {
            if ((int)frequency >= 0 && (int)frequency < 6) ConfirmDeviceInfoReply(DeviceInfoKind.Frequency);
            PostToUi(delegate
            {
                int index = (int)frequency;
                _confirmedFrequencyIndex = index >= 0 && index < 6 ? index : -1;
                RestoreFrequencySelection();
                AppendLog("设备采样率：" + FrequencyText(frequency), Theme.Muted);
            });
        }

        private void RestoreFrequencySelection()
        {
            _loadingFrequency = true;
            _frequencyCombo.SelectedIndex = _confirmedFrequencyIndex;
            _loadingFrequency = false;
        }

        private void OnPinChanged(bool ok)
        {
            ConfirmDeviceInfoReply(DeviceInfoKind.Pin);
            PostToUi(delegate
            {
                _pinLabel.Text = ok ? "通讯  正常" : "通讯  异常";
                _pinLabel.ForeColor = ok ? Theme.Cyan : Theme.Amber;
                AppendLog(ok ? "设备通讯握手正常" : "设备通讯握手未通过", ok ? Theme.Cyan : Theme.Amber);
            });
        }

        private void OnFastCharge(FastChargeType type, int mask)
        {
            string name = ProtocolName(type);
            int[] voltages = { 5, 6, 7, 8, 9, 10, 11, 12, 15, 16, 18, 20, 25, 28, 30, 36, 48 };
            List<string> supported = new List<string>();
            // 电压能力位按 DLL 文档从 0x80 连续排列。
            for (int i = 0; i < voltages.Length; i++) if ((mask & (0x80 << i)) != 0) supported.Add(voltages[i] + "V");
            string text = supported.Count == 0 ? "未返回固定电压点" : String.Join("  ·  ", supported.ToArray());
            PostToUi(delegate
            {
                _protocolHeadline.Text = "最近识别  /  " + name;
                _protocolValues.Text = "电压能力  " + text;
                _tooltips.SetToolTip(_protocolValues, _protocolValues.Text);
                MarkProtocol(name, true);
                AppendLog("识别到 " + name + "：" + text, Theme.Cyan);
            });
        }

        private void OnFastChargeDescription(FastChargeType type, bool success, string description)
        {
            PostToUi(delegate
            {
                _protocolDescription.Text = String.IsNullOrEmpty(description) ? "设备未返回协议说明。" : description;
                _tooltips.SetToolTip(_protocolDescription, _protocolDescription.Text);
                _protocolDescription.ForeColor = success ? Theme.Muted : Theme.Amber;
                MarkProtocol(ProtocolName(type), success);
                if (!success) _protocolHeadline.Text = ProtocolName(type) + "  /  检测未通过";
                AppendLog(ProtocolName(type) + (success ? " 检测通过" : " 检测未通过"), success ? Theme.Cyan : Theme.Amber);
            });
        }

        private void MarkProtocol(string name, bool success)
        {
            ProtocolChip chip;
            if (_protocolChips.TryGetValue(name, out chip)) { chip.Active = success; chip.Failed = !success; }
        }

        private void OnFrequencySelection(object sender, EventArgs e)
        {
            if (_loadingFrequency || _frequencyCombo.SelectedIndex < 0) return;
            if (!_connected || _commandBusy || _connectionBusy) { RestoreFrequencySelection(); return; }
            Frequency selected = (Frequency)_frequencyCombo.SelectedIndex;
            RunDeviceAction("设置采样率", delegate { _device.SetFrequency(selected); _device.GetFrequency(); },
                delegate { AppendLog("已发送采样率设置：" + FrequencyText(selected), Theme.Muted); },
                delegate(Exception ex) { RestoreFrequencySelection(); });
        }

        private void OnResetCounters(object sender, EventArgs e)
        {
            RunDeviceAction("重置累计", delegate { _device.ResteAH(); }, delegate
            {
                _buffer.ResetPeaks();
                AppendLog("已发送电量与能量计数重置命令", Theme.Amber);
            }, null);
        }

        private void OnProtocolScan(object sender, EventArgs e)
        {
            if (!_connected || _commandBusy || _connectionBusy) return;
            foreach (ProtocolChip chip in _protocolChips.Values) { chip.Active = false; chip.Failed = false; }
            _protocolHeadline.Text = "正在检测，等待设备返回结果…";
            _protocolValues.Text = "电压能力  —";
            _protocolDescription.Text = "检测结果由设备返回，灰色协议表示尚未收到结果。";
            RunDeviceAction("自动检测协议", delegate
            {
                if (!_device.AutoDetectionProtocol()) throw new InvalidOperationException("设备未接受检测请求");
            }, delegate { AppendLog("已发送快充协议检测请求", Theme.Cyan); },
                delegate(Exception ex) { _protocolHeadline.Text = "协议检测请求失败"; _protocolDescription.Text = ex.Message; });
        }

        private void SetDeviceUi(DevUI mode)
        {
            RunDeviceAction("切换设备界面", delegate
            {
                if (!_device.SetDevUI(mode)) throw new InvalidOperationException("设备未接受界面切换请求");
            }, delegate { AppendLog("已请求切换到 " + UiName(mode), Theme.Muted); }, null);
        }

        private void RunDeviceAction(string name, Action command, Action completed, Action<Exception> failed)
        {
            if (!_connected || _connectionBusy || _commandBusy || _closing) return;
            _commandBusy = true;
            SetControlAvailability();
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception failure = null;
                try
                {
                    lock (_apiLock)
                    {
                        if (!_device.ConnState) throw new InvalidOperationException("设备已断开连接");
                        command();
                    }
                }
                catch (Exception ex) { failure = ex; }
                Exception error = failure;
                PostToUi(delegate
                {
                    _commandBusy = false;
                    SetControlAvailability();
                    if (error == null) completed();
                    else
                    {
                        AppendLog(name + "失败：" + error.Message, Theme.Red);
                        if (failed != null) failed(error);
                    }
                });
            });
        }

        private void ClearTelemetry()
        {
            lock (_infoReplyLock) Array.Clear(_infoReplyVersions, 0, _infoReplyVersions.Length);
            _buffer = new TelemetryBuffer();
            _snapshot = null; _renderedVersion = -1;
            _readoutVersion = -1; _nextReadoutAt = 0;
            _nextCounterReadoutAt = 0;
            _receiveRate = 0; _ratePreviousTotal = 0; _rateStarted = Stopwatch.GetTimestamp();
            _chart.Clear(); _chart.SetEmptyHint("已连接后自动显示测量曲线");
            _voltageTile.Clear(); _currentTile.Clear(); _powerTile.Clear();
            _whValueLabel.Text = _ahValueLabel.Text = _resistanceValueLabel.Text = _dpValueLabel.Text = _dmValueLabel.Text = "--.---";
            _whCaptionLabel.Text = "能量 / Wh"; _ahCaptionLabel.Text = "电量 / Ah";
            _resistanceCaptionLabel.Text = "阻值 / Ω";
            _dpCaptionLabel.Text = "D+ / V"; _dmCaptionLabel.Text = "D− / V";
            _serialLabel.Text = "SN  —"; _hardwareLabel.Text = "硬件  —";
            _deviceModeLabel.Text = "设备界面  —"; _pinLabel.Text = "通讯  —";
            _pinLabel.ForeColor = Theme.Muted;
            _tooltips.SetToolTip(_serialLabel, "");
            _tooltips.SetToolTip(_protocolValues, "");
            _tooltips.SetToolTip(_protocolDescription, "");
            _chartSummary.Text = "时间基准：电脑接收时间";
            _sessionLabel.Text = "最近活动  /  等待测量数据";
            _tooltips.SetToolTip(_sessionLabel, "本次接收 0 点");
            _confirmedFrequencyIndex = -1; RestoreFrequencySelection();
            _protocolHeadline.Text = "等待协议检测"; _protocolValues.Text = "电压能力  —";
            _protocolDescription.Text = "点击自动检测，查看设备返回的协议能力。";
            Text = "UT70 Power Lab | 实时监控";
            _taskbarNotifyIcon.Text = "UT70 Power Lab";
            _taskbarReadout.SetReadings("-- A", "-- Ah");
            foreach (ProtocolChip chip in _protocolChips.Values) { chip.Active = false; chip.Failed = false; }
        }

        private void ToggleFullscreen()
        {
            if (!_fullScreen)
            {
                _savedBounds = Bounds; _savedWindowState = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
                _fullScreen = true; _fullscreenButton.Text = "退出全屏";
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = _savedBounds; WindowState = _savedWindowState;
                _fullScreen = false; _fullscreenButton.Text = "全屏 F11";
            }
        }

        private void PostToUi(MethodInvoker action)
        {
            if (_closing || IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!_closing && !IsDisposed) action(); }); }
            catch (InvalidOperationException) { }
        }

        private void AppendLog(string message, Color color)
        {
            if (_logList == null || _logList.IsDisposed) return;
            Panel row = new Panel { Height = 23, BackColor = Theme.Surface, Margin = Padding.Empty };
            Label time = Ui.Label(DateTime.Now.ToString("HH:mm:ss"), 7.5F, Theme.Faint, FontStyle.Regular);
            time.Location = new Point(0, 3);
            Label text = Ui.Label(message.Replace("\r", " ").Replace("\n", " "), 8F, color, FontStyle.Regular);
            text.AutoSize = false; text.AutoEllipsis = true;
            text.Bounds = new Rectangle(58, 3, 150, 20);
            text.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _tooltips.SetToolTip(text, message);
            row.Controls.Add(time); row.Controls.Add(text);
            _logList.Controls.Add(row); _logList.Controls.SetChildIndex(row, 0);
            while (_logList.Controls.Count > 40)
            {
                Control last = _logList.Controls[_logList.Controls.Count - 1];
                _logList.Controls.Remove(last); last.Dispose();
            }
            ResizeLogRows();
        }

        private void ResizeLogRows()
        {
            if (_logList == null) return;
            foreach (Control row in _logList.Controls)
            {
                row.Width = Math.Max(120, _logList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
                if (row.Controls.Count > 1) row.Controls[1].Width = Math.Max(50, row.Width - 61);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exitReady) return;
            _acceptData = false;
            if (!_closing)
            {
                _closing = true; WakeDeviceInfoWaiters(); _clockTimer.Stop(); UnsubscribeDeviceEvents();
                _taskbarNotifyIcon.Visible = false;
                _taskbarReadout.Hide();
                if (!_connected && !_connectionBusy && !_commandBusy) return;
                SetControlAvailability();
                // 断开操作移到后台，关闭窗口最多等待 1.5 秒。
                _shutdownTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                _shutdownTimer.Tick += delegate { _shutdownTimer.Stop(); _exitReady = true; Close(); };
                _shutdownTimer.Start();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { lock (_apiLock) _device.Disconnect(); } catch { }
                    try { BeginInvoke((MethodInvoker)delegate { _exitReady = true; Close(); }); } catch (InvalidOperationException) { }
                });
            }
            e.Cancel = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true; WakeDeviceInfoWaiters();
                _acceptData = false;
                if (_clockTimer != null) _clockTimer.Dispose();
                if (_shutdownTimer != null) _shutdownTimer.Dispose();
                if (_taskbarNotifyIcon != null) _taskbarNotifyIcon.Dispose();
                if (_taskbarReadout != null) _taskbarReadout.Dispose();
                _tooltips.Dispose();
                if (_device != null) UnsubscribeDeviceEvents();
            }
            base.Dispose(disposing);
        }

        private static void UpdateReadout(Label valueLabel, Label captionLabel, string title, double value, string baseUnit)
        {
            MeasurementText display = DisplayUnits.Format(value, baseUnit);
            valueLabel.Text = display.Number;
            captionLabel.Text = title + " / " + display.Unit;
        }

        private static string ProtocolName(FastChargeType type)
        {
            switch (type)
            {
                case FastChargeType.BC1_2: return "BC1.2";
                case FastChargeType.QC2: return "QC2";
                case FastChargeType.QC3: return "QC3";
                case FastChargeType.FCP: return "FCP";
                case FastChargeType.SCP: return "SCP";
                case FastChargeType.AFC: return "AFC";
                case FastChargeType.PE1: return "PE1";
                case FastChargeType.PE2: return "PE2";
                case FastChargeType.VOOC: return "VOOC";
                case FastChargeType.PD: return "PD";
                case FastChargeType.DASH: return "DASH";
                case FastChargeType.EMarker: return "E-Marker";
                case FastChargeType.VFCP: return "VFCP";
                case FastChargeType.APPLE2_4: return "Apple 2.4A";
                default: return "未知协议";
            }
        }

        private static string UiName(DevUI mode)
        {
            switch (mode)
            {
                case DevUI.NORMAL: return "基本测量";
                case DevUI.WAVEFORM: return "波形显示";
                case DevUI.DATALOG: return "数据记录";
                case DevUI.FUNUSE: return "功能应用";
                case DevUI.MANUAL: return "手动触发";
                case DevUI.AUTO: return "自动检测";
                case DevUI.MONITOR: return "监控模式";
                case DevUI.LINER: return "线阻模式";
                case DevUI.DASHCHECK: return "DASH 检测";
                case DevUI.EMARKERCHECK: return "E-Marker 检测";
                case DevUI.SETFACE: return "设置";
                default: return "其他";
            }
        }

        private static string FrequencyText(Frequency frequency)
        {
            switch (frequency)
            {
                case Frequency._4SPS: return "4 SPS";
                case Frequency._10SPS: return "10 SPS";
                case Frequency._50SPS: return "50 SPS";
                case Frequency._200SPS: return "200 SPS";
                case Frequency._500SPS: return "500 SPS";
                case Frequency._1000SPS: return "1000 SPS";
                default: return "未知";
            }
        }
    }
}
