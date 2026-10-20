using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UT70Dashboard
{
    internal enum ChartMetric { Voltage, Current, Power }

    internal sealed class TelemetryChart : Control
    {
        private TelemetrySnapshot _data;
        private ChartMetric _metric;
        private MeasurementUnit _displayUnit = DisplayUnits.Select(0, "V");
        private int _hoverX = -1;
        private int _selectionStartX = -1, _selectionEndX = -1;
        private bool _selecting;
        private RangeStatistics _rangeStatistics;
        private string _emptyHint = "连接 UT70，开始实时测量";

        public TelemetryChart()
        {
            BackColor = Theme.Surface;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public ChartMetric Metric
        {
            get { return _metric; }
            set { _metric = value; Invalidate(); }
        }

        public void SetEmptyHint(string value)
        {
            if (_emptyHint == value) return;
            _emptyHint = value; Invalidate();
        }

        public void SetData(TelemetrySnapshot data) { _data = data; Invalidate(); }
        public void Clear() { _data = null; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _data != null && _data.Points != null && _data.Points.Length > 0)
            {
                Rectangle plot = PlotBounds;
                if (plot.Contains(e.Location))
                {
                    Focus();
                    Capture = true;
                    _selecting = true;
                    _selectionStartX = _selectionEndX = Clamp(e.X, plot.Left, plot.Right);
                    _rangeStatistics = null;
                    Invalidate();
                }
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            _hoverX = e.X;
            if (_selecting)
            {
                Rectangle plot = PlotBounds;
                _selectionEndX = Clamp(e.X, plot.Left, plot.Right);
            }
            Invalidate();
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_selecting && e.Button == MouseButtons.Left)
            {
                _selectionEndX = Clamp(e.X, PlotBounds.Left, PlotBounds.Right);
                _selecting = false;
                Capture = false;
                DateTime startUtc, endUtc;
                GetSelectionTimes(out startUtc, out endUtc);
                if (endUtc > startUtc && RangeSelected != null)
                    RangeSelected(startUtc, endUtc, _metric);
            }
            base.OnMouseUp(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta != 0 && WindowScrollRequested != null)
                WindowScrollRequested(e.Delta / 120 == 0 ? (e.Delta > 0 ? 1 : -1) : e.Delta / 120);
        }
        protected override void OnMouseLeave(EventArgs e) { _hoverX = -1; Invalidate(); base.OnMouseLeave(e); }

        public event Action<int> WindowScrollRequested;
        public event Action<DateTime, DateTime, ChartMetric> RangeSelected;

        public RangeStatistics SelectedStatistics { get { return _rangeStatistics; } }

        public void SetRangeStatistics(RangeStatistics statistics)
        {
            _rangeStatistics = statistics;
            Invalidate();
        }

        private Rectangle PlotBounds
        {
            get { return new Rectangle(64, 22, Math.Max(1, Width - 91), Math.Max(1, Height - 57)); }
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void GetSelectionTimes(out DateTime startUtc, out DateTime endUtc)
        {
            Rectangle plot = PlotBounds;
            double ticks = (_data.EndUtc - _data.StartUtc).Ticks;
            double startOffset = Math.Max(0, Math.Min(1, (_selectionStartX - plot.Left) / (double)plot.Width)) * ticks;
            double endOffset = Math.Max(0, Math.Min(1, (_selectionEndX - plot.Left) / (double)plot.Width)) * ticks;
            DateTime first = _data.StartUtc.AddTicks((long)Math.Min(startOffset, endOffset));
            DateTime last = _data.StartUtc.AddTicks((long)Math.Max(startOffset, endOffset));
            startUtc = first;
            endUtc = last;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 130 || Height < 100) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle plot = PlotBounds;
            Color trace = _metric == ChartMetric.Current ? Theme.Lime : _metric == ChartMetric.Power ? Theme.Amber : Theme.Cyan;
            string baseUnit = _metric == ChartMetric.Current ? "A" : _metric == ChartMetric.Power ? "W" : "V";
            ChartSample[] samples = _data == null ? new ChartSample[0] : _data.Points;
            double min = Double.MaxValue, max = Double.MinValue;
            foreach (ChartSample sample in samples)
            {
                double value = sample.Value(_metric);
                min = Math.Min(min, value); max = Math.Max(max, value);
            }
            double lower = 0, upper = 1;
            if (samples.Length > 0 && (min != 0 || max != 0))
            {
                double span = max - min;
                // 按实际量级扩展恒定读数，避免固定跨度淹没 μA、mV 级信号。
                if (span == 0) span = Math.Abs(max) * 0.06;
                lower = min - span * 0.2; upper = max + span * 0.2;
                if (min >= 0) lower = Math.Max(0, lower);
            }
            // 同一张曲线按整个可见范围选择单位，坐标和悬停值共用此倍率。
            _displayUnit = DisplayUnits.Select(samples.Length == 0 ? 0 : Math.Max(Math.Abs(lower), Math.Abs(upper)), baseUnit);
            using (Pen grid = new Pen(Color.FromArgb(29, 45, 52)))
            using (SolidBrush text = new SolidBrush(Theme.Muted))
            using (Font font = new Font("Segoe UI", 8F))
            {
                g.DrawString(_displayUnit.Symbol, font, text, plot.Left - 20, 0);
                for (int row = 0; row <= 4; row++)
                {
                    float y = plot.Top + plot.Height * row / 4F;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string value = samples.Length == 0 ? "—" : _displayUnit.FormatNumber(upper - (upper - lower) * row / 4, true);
                    SizeF size = g.MeasureString(value, font);
                    g.DrawString(value, font, text, plot.Left - size.Width - 10, y - size.Height / 2);
                }
                for (int col = 0; col <= 6; col++)
                {
                    float x = plot.Left + plot.Width * col / 6F;
                    g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                    string time = _data == null ? "--:--:--" : _data.StartUtc.AddTicks(
                        (_data.EndUtc - _data.StartUtc).Ticks * col / 6).ToLocalTime().ToString("HH:mm:ss");
                    SizeF size = g.MeasureString(time, font);
                    g.DrawString(time, font, text, x - size.Width / 2, plot.Bottom + 10);
                }
            }
            if (samples.Length == 0) { DrawEmpty(g, plot, trace); return; }
            PointF[] points = new PointF[samples.Length];
            double ticks = Math.Max(1, (_data.EndUtc - _data.StartUtc).Ticks);
            for (int i = 0; i < samples.Length; i++)
            {
                points[i] = new PointF(
                    plot.Left + (float)((samples[i].TimeUtc - _data.StartUtc).Ticks / ticks * plot.Width),
                    plot.Bottom - (float)((samples[i].Value(_metric) - lower) / (upper - lower) * plot.Height));
            }
            GraphicsState state = g.Save();
            g.SetClip(Rectangle.Inflate(plot, 5, 5));
            using (SolidBrush dot = new SolidBrush(trace))
            {
                if (points.Length > 1)
                {
                    using (GraphicsPath area = new GraphicsPath())
                    using (LinearGradientBrush fill = new LinearGradientBrush(plot,
                        Color.FromArgb(52, trace), Color.FromArgb(0, trace), LinearGradientMode.Vertical))
                    using (Pen line = new Pen(trace, 1.8F))
                    using (Pen glow = new Pen(Color.FromArgb(24, trace), 6F))
                    {
                        area.AddLines(points);
                        area.AddLine(points[points.Length - 1], new PointF(points[points.Length - 1].X, plot.Bottom));
                        area.AddLine(new PointF(points[0].X, plot.Bottom), points[0]);
                        area.CloseFigure();
                        g.FillPath(fill, area);
                        line.LineJoin = LineJoin.Round;
                        g.DrawLines(glow, points); g.DrawLines(line, points);
                    }
                }
                PointF last = points[points.Length - 1];
                g.FillEllipse(dot, last.X - 3, last.Y - 3, 6, 6);
            }
            g.Restore(state);
            if (_selectionStartX >= 0 && _selectionEndX >= 0)
                DrawSelection(g, plot, trace);
            if (_hoverX >= plot.Left && _hoverX <= plot.Right) DrawCursor(g, points, samples, plot, trace);
        }

        private void DrawSelection(Graphics graphics, Rectangle plot, Color color)
        {
            int left = Clamp(Math.Min(_selectionStartX, _selectionEndX), plot.Left, plot.Right);
            int right = Clamp(Math.Max(_selectionStartX, _selectionEndX), plot.Left, plot.Right);
            if (_selecting || _rangeStatistics != null)
            {
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(32, color)))
                using (Pen edge = new Pen(Color.FromArgb(170, color)) { DashStyle = DashStyle.Dash })
                {
                    graphics.FillRectangle(shade, left, plot.Top, Math.Max(1, right - left), plot.Height);
                    graphics.DrawLine(edge, left, plot.Top, left, plot.Bottom);
                    graphics.DrawLine(edge, right, plot.Top, right, plot.Bottom);
                }
            }

            if (_rangeStatistics == null || _rangeStatistics.Count == 0) return;
            string baseUnit = _metric == ChartMetric.Current ? "A" : _metric == ChartMetric.Power ? "W" : "V";
            string label = "选区 " + _rangeStatistics.Count + " 点  均 " +
                DisplayUnits.Format(_rangeStatistics.Average, baseUnit).Text +
                "  最大 " + DisplayUnits.Format(_rangeStatistics.Maximum, baseUnit).Text +
                "  最小 " + DisplayUnits.Format(_rangeStatistics.Minimum, baseUnit).Text;
            using (Font font = new Font("Segoe UI", 8F, FontStyle.Bold))
            using (SolidBrush text = new SolidBrush(Theme.Text))
            using (SolidBrush background = new SolidBrush(Theme.SurfaceRaised))
            {
                SizeF size = graphics.MeasureString(label, font);
                RectangleF badge = new RectangleF(plot.Left + 7, plot.Top + 7,
                    Math.Min(plot.Width - 14, size.Width + 14), size.Height + 10);
                using (GraphicsPath path = Ui.RoundRect(Rectangle.Round(badge), 4))
                    graphics.FillPath(background, path);
                graphics.DrawString(label, font, text, badge.X + 7, badge.Y + 5);
            }
        }

        private void DrawEmpty(Graphics g, Rectangle plot, Color color)
        {
            int x = plot.Left + plot.Width / 2, y = plot.Top + plot.Height / 2 - 15;
            using (Pen pen = new Pen(Color.FromArgb(65, color), 1))
            using (SolidBrush dot = new SolidBrush(color))
            using (SolidBrush brush = new SolidBrush(Theme.Muted))
            using (Font font = new Font("Segoe UI", 10F))
            {
                g.DrawEllipse(pen, x - 21, y - 21, 42, 42);
                g.DrawEllipse(pen, x - 12, y - 12, 24, 24);
                g.FillEllipse(dot, x - 3, y - 3, 6, 6);
                SizeF size = g.MeasureString(_emptyHint, font);
                g.DrawString(_emptyHint, font, brush, x - size.Width / 2, y + 32);
            }
        }

        private void DrawCursor(Graphics g, PointF[] points, ChartSample[] samples, Rectangle plot, Color color)
        {
            int nearest = 0;
            for (int i = 1; i < points.Length; i++)
                if (Math.Abs(points[i].X - _hoverX) < Math.Abs(points[nearest].X - _hoverX)) nearest = i;
            PointF point = points[nearest];
            using (Pen cross = new Pen(Color.FromArgb(100, color)) { DashStyle = DashStyle.Dash })
            using (SolidBrush dot = new SolidBrush(color))
            using (SolidBrush text = new SolidBrush(Theme.Text))
            using (Font font = new Font("Segoe UI", 8.5F))
            {
                g.DrawLine(cross, point.X, plot.Top, point.X, plot.Bottom);
                g.FillEllipse(dot, point.X - 4, point.Y - 4, 8, 8);
                string label = samples[nearest].TimeUtc.ToLocalTime().ToString("HH:mm:ss.fff") +
                    "   " + _displayUnit.FormatNumber(samples[nearest].Value(_metric), false) + " " + _displayUnit.Symbol;
                SizeF size = g.MeasureString(label, font);
                int x = Math.Max(plot.Left, Math.Min(plot.Right - (int)size.Width - 18, (int)point.X - 70));
                Rectangle bubble = new Rectangle(x, plot.Top + 7, (int)size.Width + 18, (int)size.Height + 13);
                Ui.DrawRoundRect(g, bubble, 5, Theme.SurfaceRaised);
                g.DrawString(label, font, text, bubble.X + 9, bubble.Y + 6);
            }
        }

    }
}
