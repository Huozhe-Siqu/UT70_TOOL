using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace UT70Dashboard
{
    internal static class DashboardChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static int _assertions;

        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CheckPaintIsolation();
                CheckSparklinePaintIsolation();
                CheckSurfaceEdges();
                CheckStableReadout();
                CheckUnits();
                CheckTaskbarReadoutSettings();
                CheckBuffer();
                CheckChartRangeSelection();
                CheckConcurrentAccess();
                CheckRendering();
                CheckContinuousRefresh();
                Console.WriteLine("PASS: " + _assertions + " assertions; no USB commands were sent.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static ChartSample Sample(DateTime time, float voltage)
        {
            return new ChartSample(time, voltage, 2.5F, voltage * 2.5F, 1.25F, 0.5F, 8F, 2.7F, 2.6F);
        }

        private static void CheckPaintIsolation()
        {
            using (Panel parent = new Panel { BackColor = Theme.Surface, Size = new Size(300, 100) })
            using (Label label = Ui.Label("26.550", 25F, Theme.Text, FontStyle.Regular))
            using (Bitmap bitmap = new Bitmap(220, 55))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                label.AutoSize = false;
                label.Size = bitmap.Size;
                parent.Controls.Add(label);
                int parentPaints = 0;
                parent.Paint += delegate { parentPaints++; };
                graphics.Clear(Color.Magenta);
                // 直接走标签局部重绘的背景路径，不强制重绘整张静态截图。
                typeof(Control).GetMethod("OnPaintBackground", PrivateInstance).Invoke(label,
                    new object[] { new PaintEventArgs(graphics, label.ClientRectangle) });
                typeof(Control).GetMethod("OnPaint", PrivateInstance).Invoke(label,
                    new object[] { new PaintEventArgs(graphics, label.ClientRectangle) });
                Console.WriteLine("Live label background repaint replayed parent " + parentPaints + " time(s).");
                Console.WriteLine("Label background: " + label.BackColor + "; painted corner: " + bitmap.GetPixel(0, 0));
                Assert(parentPaints == 0, "a live readout repaint must not replay the parent container");
                Assert(bitmap.GetPixel(0, 0).ToArgb() == Theme.Surface.ToArgb(), "live label clears its own background");
            }
        }

        private static void CheckSparklinePaintIsolation()
        {
            using (MetricTile tile = new MetricTile("CURRENT", "电流", "A", Theme.Lime))
            {
                tile.Size = new Size(420, 120);
                Sparkline spark = Get<Sparkline>(tile, "_spark");
                spark.Add(0.4F); spark.Add(0.41F);
                int tilePaints = 0;
                tile.Paint += delegate { tilePaints++; };
                using (Bitmap bitmap = new Bitmap(spark.Width, spark.Height))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Magenta);
                    PaintControl(spark, graphics);
                    Console.WriteLine("Sparkline repaint replayed KPI " + tilePaints + " time(s).");
                    Assert(tilePaints == 0, "sparkline repaint must not erase the KPI readout");
                    Assert(bitmap.GetPixel(0, 0).ToArgb() == Theme.Surface.ToArgb(), "sparkline background painted");
                }
            }
        }

        private static void CheckSurfaceEdges()
        {
            using (Panel parent = new Panel { BackColor = Theme.Back, Size = new Size(300, 130) })
            using (DashboardCard card = new DashboardCard { Size = new Size(280, 110) })
            using (Bitmap bitmap = new Bitmap(card.Width, card.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                parent.Controls.Add(card);
                graphics.Clear(Color.Magenta);
                PaintControl(card, graphics);
                Assert(bitmap.GetPixel(0, 0).ToArgb() == Theme.Back.ToArgb(), "rounded card clears corner pixels");
                using (ActionButton button = new ActionButton { Size = new Size(100, 35) })
                using (Bitmap buttonBitmap = new Bitmap(button.Width, button.Height))
                using (Graphics buttonGraphics = Graphics.FromImage(buttonBitmap))
                {
                    card.Controls.Add(button);
                    buttonGraphics.Clear(Color.Magenta);
                    PaintControl(button, buttonGraphics);
                    Assert(buttonBitmap.GetPixel(0, 0).ToArgb() == Theme.Surface.ToArgb(), "button corners use parent background");
                }
            }
        }

        private static void PaintControl(Control control, Graphics graphics)
        {
            PaintEventArgs args = new PaintEventArgs(graphics, control.ClientRectangle);
            typeof(Control).GetMethod("OnPaintBackground", PrivateInstance).Invoke(control, new object[] { args });
            typeof(Control).GetMethod("OnPaint", PrivateInstance).Invoke(control, new object[] { args });
        }

        private static void CheckStableReadout()
        {
            using (MetricTile tile = new MetricTile("VOLTAGE", "电压", "V", Theme.Cyan))
            {
                tile.Size = new Size(420, 120);
                tile.SetValue(4.985, 4.999);
                IntPtr handle = tile.Handle;
                int invalidations = 0;
                tile.Invalidated += delegate { invalidations++; };
                for (int i = 0; i < 20; i++) tile.SetValue(4.985, 4.999);
                Console.WriteLine("Unchanged KPI requested " + invalidations + " repaint(s).");
                Assert(invalidations == 0, "unchanged displayed readings must not request repaints");
                tile.SetValue(4.986, 4.999);
                Assert(invalidations == 1, "changed reading requests repaint");
            }
        }

        private static void CheckUnits()
        {
            AssertDisplay(0, "A", "0.000", "A");
            AssertDisplay(-0.0, "V", "0.000", "V");
            AssertDisplay(0.0000123, "A", "12.300", "μA");
            AssertDisplay(0.0123, "A", "12.300", "mA");
            AssertDisplay(1.234, "A", "1.234", "A");
            AssertDisplay(1234, "W", "1.234", "kW");
            AssertDisplay(0.5, "V", "500.000", "mV");
            AssertDisplay(0.0000005, "V", "0.500", "μV");
            AssertDisplay(0.01234, "Wh", "12.340", "mWh");
            AssertDisplay(0.0025, "Ah", "2.500", "mAh");
            AssertDisplay(1234, "Wh", "1.234", "kWh");
            AssertDisplay(4700, "Ω", "4.700", "kΩ");
            AssertDisplay(4700000, "Ω", "4.700", "MΩ");
            AssertDisplay(0.02, "Ω", "20.000", "mΩ");
            AssertDisplay(-0.002, "A", "-2.000", "mA");
            AssertDisplay(0.0009999996, "A", "1.000", "mA");
            AssertDisplay(0.9999996, "A", "1.000", "A");
            AssertDisplay(999.9996, "A", "1.000", "kA");
            AssertDisplay(0.9994, "A", "999.400", "mA");
            AssertDisplay((double)0.001F, "A", "1.000", "mA");
            AssertDisplay((double)0.000001F, "A", "1.000", "μA");
            AssertDisplay(4700000000, "Ω", "4.700", "GΩ");
            AssertDisplay(Double.NaN, "A", "—", "A");
            AssertDisplay(Double.PositiveInfinity, "Ω", "∞", "Ω");
            AssertDisplay(Double.NegativeInfinity, "Ω", "−∞", "Ω");
            Assert(DisplayUnits.Format(1e-12, "A").Number != "0.000", "nonzero tiny readings not rounded to zero");
            MeasurementUnit axis = DisplayUnits.Select(0.0019, "A");
            Assert(axis.Symbol == "mA" && axis.FormatNumber(0.0012, false) == "1.200", "axis scaling matches hover scaling");
            Console.WriteLine("Units: micro/milli/base/kilo/mega, rounding boundaries, negatives and special values passed.");
        }

        private static void AssertDisplay(double value, string baseUnit, string number, string unit)
        {
            MeasurementText result = DisplayUnits.Format(value, baseUnit);
            Assert(result.Number == number && result.Unit == unit, "automatic unit: " + value + " " + baseUnit + " => " + result.Text);
        }

        private static void CheckBuffer()
        {
            DateTime epoch = DateTime.UtcNow.AddSeconds(-100);
            TelemetryBuffer buffer = new TelemetryBuffer();
            Assert(buffer.Capture(epoch, 30L * TimeSpan.TicksPerSecond, ChartMetric.Voltage, 300).Latest == null, "empty state");
            ChartSample[] raw = new ChartSample[100000];
            for (int i = 0; i < raw.Length; i++) raw[i] = Sample(epoch.AddMilliseconds(i), i == 500 ? 99 : i == 90000 ? 75 : i == 90500 ? -7 : 20);
            Stopwatch watch = Stopwatch.StartNew();
            buffer.Append(raw);
            TelemetrySnapshot view = buffer.Capture(raw[raw.Length - 1].TimeUtc, 60L * TimeSpan.TicksPerSecond, ChartMetric.Voltage, 300);
            watch.Stop();
            Assert(view.Total == raw.Length, "all valid samples counted");
            Assert(view.Points.Length <= 300, "drawing budget bounded");
            Assert(view.Points.Any(p => p.Voltage == 75) && view.Points.Any(p => p.Voltage == -7), "positive and negative spikes preserved");
            Assert(view.PeakV == 99, "session peak uses raw data outside visible window");
            Assert(view.Points.All(p => p.TimeUtc >= view.StartUtc), "time window enforced");
            Assert(Object.ReferenceEquals(view.Latest, raw[raw.Length - 1]), "latest sample retained");
            for (int i = 1; i < view.Points.Length; i++) Assert(view.Points[i - 1].TimeUtc <= view.Points[i].TimeUtc, "time order");
            buffer.ResetPeaks();
            Assert(buffer.Capture(view.EndUtc, 10L * TimeSpan.TicksPerSecond, ChartMetric.Voltage, 200).PeakV == 20, "peak reset");
            buffer.Append(new ChartSample[] { Sample(view.EndUtc, Single.NaN), Sample(view.EndUtc, Single.PositiveInfinity) });
            TelemetrySnapshot invalid = buffer.Capture(view.EndUtc, 10L * TimeSpan.TicksPerSecond, ChartMetric.Voltage, 200);
            Assert(invalid.Total == 100000 && invalid.Rejected == 2, "invalid input excluded from drawing");
            TelemetryBuffer saturated = new TelemetryBuffer();
            ChartSample same = Sample(epoch, 20);
            saturated.Append(Enumerable.Repeat(same, 70000).ToArray());
            TelemetrySnapshot capacity = saturated.Capture(epoch, 60L * TimeSpan.TicksPerSecond, ChartMetric.Voltage, 100000);
            Assert(capacity.Points.Length == TelemetryBuffer.Capacity && capacity.Total == 70000, "capacity remains bounded");

            TelemetryBuffer rangeBuffer = new TelemetryBuffer();
            ChartSample[] rangeSamples = new ChartSample[]
            {
                Sample(epoch, 1), Sample(epoch.AddSeconds(1), 5), Sample(epoch.AddSeconds(2), -2),
                Sample(epoch.AddSeconds(3), 4), Sample(epoch.AddSeconds(4), 100)
            };
            rangeBuffer.Append(rangeSamples);
            RangeStatistics range = rangeBuffer.CalculateRangeStatistics(epoch.AddSeconds(1), epoch.AddSeconds(3), ChartMetric.Voltage);
            Assert(range.Count == 3 && range.Average == 7.0 / 3.0 && range.Minimum == -2 && range.Maximum == 5,
                "selected range statistics use all raw samples and include both endpoints");
            Console.WriteLine("Buffer: 100,000 samples + capture in " + watch.ElapsedMilliseconds + " ms; " + view.Points.Length + " drawing points.");
        }

        private static void CheckChartRangeSelection()
        {
            using (TelemetryChart chart = new TelemetryChart { Size = new Size(500, 240) })
            {
                DateTime start = DateTime.UtcNow.AddSeconds(-10);
                ChartSample[] points = new ChartSample[]
                {
                    Sample(start, 1), Sample(start.AddSeconds(5), 3), Sample(start.AddSeconds(10), 2)
                };
                chart.SetData(new TelemetrySnapshot
                {
                    StartUtc = start,
                    EndUtc = start.AddSeconds(10),
                    Points = points
                });
                DateTime selectedStart = DateTime.MinValue, selectedEnd = DateTime.MinValue;
                ChartMetric selectedMetric = ChartMetric.Power;
                chart.RangeSelected += delegate(DateTime from, DateTime to, ChartMetric metric)
                {
                    selectedStart = from;
                    selectedEnd = to;
                    selectedMetric = metric;
                };
                Invoke(chart, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
                Invoke(chart, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 400, 100, 0));
                Invoke(chart, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 400, 100, 0));
                Assert(selectedStart > start && selectedEnd < start.AddSeconds(10) && selectedEnd > selectedStart,
                    "left drag maps chart selection to ordered time range");
                Assert(selectedMetric == ChartMetric.Voltage, "range selection reports active chart metric");
                Assert(chart.SelectedStatistics == null, "chart awaits exact raw-buffer range statistics");
            }
        }

        private static void CheckTaskbarReadoutSettings()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ut70-readout-check-" + Guid.NewGuid().ToString("N"));
            string settingsPath = Path.Combine(directory, "settings.xml");
            try
            {
                TaskbarReadoutForm first = new TaskbarReadoutForm(settingsPath);
                Assert(!first.IsVertical && first.Size == new Size(286, 68) && first.OpacityPercent == 100,
                    "taskbar readout defaults to horizontal and fully opaque");
                first.SetVertical(true);
                Assert(first.Size == new Size(124, 228), "vertical taskbar readout uses vertical layout size");
                typeof(Form).GetProperty("Opacity").SetValue(first, 0.7, null);
                Assert(first.OpacityPercent == 70, "taskbar readout opacity can be adjusted");
                first.Location = new Point(123, 234);
                first.Dispose();

                TaskbarReadoutForm restored = new TaskbarReadoutForm(settingsPath);
                Assert(restored.IsVertical && restored.OpacityPercent == 70 && restored.Location == new Point(123, 234),
                    "taskbar readout orientation, opacity and location persist across instances");
                restored.ShowAboveTaskbar(Screen.PrimaryScreen.WorkingArea);
                Assert(restored.Location == new Point(123, 234),
                    "showing restored readout preserves its saved on-screen location");
                restored.Hide();
                restored.Dispose();
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void CheckConcurrentAccess()
        {
            TelemetryBuffer buffer = new TelemetryBuffer();
            DateTime now = DateTime.UtcNow;
            Thread producer = new Thread(delegate()
            {
                for (int batch = 0; batch < 100; batch++)
                {
                    ChartSample[] samples = new ChartSample[100];
                    for (int i = 0; i < samples.Length; i++) samples[i] = Sample(now.AddTicks(batch * 100 + i), 20);
                    buffer.Append(samples);
                }
            });
            producer.Start();
            long previous = 0;
            while (producer.IsAlive)
            {
                TelemetrySnapshot snapshot = buffer.Capture(now.AddSeconds(1), 30L * TimeSpan.TicksPerSecond, ChartMetric.Current, 100);
                Assert(snapshot.Total >= previous, "concurrent count never regresses");
                Assert(snapshot.Points.Length <= 100, "concurrent rendering budget");
                previous = snapshot.Total;
            }
            producer.Join();
            Assert(buffer.Total == 10000, "concurrent ingestion complete");
        }

        private static void CheckRendering()
        {
            using (DashboardForm form = new DashboardForm())
            {
                Get<System.Windows.Forms.Timer>(form, "_clockTimer").Stop();
                Assert(form.Text.Contains("实时监控"), "Chinese source encoding preserved");
                IntPtr handle = form.Handle;
                Render(form, 1440, 900, "dashboard-offline.png");
                Assert(!Get<ActionButton>(form, "_resetButton").Enabled, "device commands disabled offline");
                Assert(Get<ActionButton>(form, "_connectButton").Enabled, "connect action available offline");

                // 合成数据仅用于检查，不进入产品启动流程。
                TelemetryBuffer buffer = new TelemetryBuffer();
                DateTime now = DateTime.UtcNow;
                ChartSample[] samples = new ChartSample[30000];
                for (int i = 0; i < samples.Length; i++) samples[i] = Sample(now.AddMilliseconds(i - samples.Length + 1), 20F + (float)Math.Sin(i / 900.0) * 0.12F + (i == 16001 ? 0.6F : 0));
                buffer.Append(samples);
                Set(form, "_buffer", buffer);
                Set(form, "_clockSecond", DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
                Invoke(form, "OnRefreshTick", null, EventArgs.Empty);
                Get<Label>(form, "_headerStateLabel").Text = "界面检查 · 合成数据";
                Render(form, 1440, 900, "dashboard-data.png");
                Invoke(form, "SelectChart", ChartMetric.Power);
                Invoke(form, "OnRefreshTick", null, EventArgs.Empty);
                Assert(Get<TelemetryChart>(form, "_chart").Metric == ChartMetric.Power, "metric switch");
                Get<DarkSelector>(form, "_windowCombo").SelectedIndex = 0;
                Invoke(form, "OnRefreshTick", null, EventArgs.Empty);
                Assert(Get<TelemetrySnapshot>(form, "_snapshot").EndUtc - Get<TelemetrySnapshot>(form, "_snapshot").StartUtc == TimeSpan.FromSeconds(10), "time window switch");
                Render(form, 1120, 720, "dashboard-compact.png");

                Rectangle original = form.Bounds;
                Invoke(form, "ToggleFullscreen");
                Assert(form.FormBorderStyle == FormBorderStyle.None, "fullscreen enabled");
                Invoke(form, "ToggleFullscreen");
                Assert(form.FormBorderStyle == FormBorderStyle.Sizable && form.Bounds == original, "window restored after fullscreen");

                Set(form, "_connected", true);
                Invoke(form, "UpdateFreshness", DateTime.UtcNow.AddSeconds(4));
                Assert(Get<Label>(form, "_chartStateLabel").Text.Contains("数据超时"), "stale readings marked");
                Set(form, "_connected", false);
                CheckUnitRendering(form);
                Console.WriteLine("Rendered offline, data and compact layouts without displaying a window.");
            }
        }

        private static void CheckUnitRendering(DashboardForm form)
        {
            TelemetryBuffer buffer = new TelemetryBuffer();
            ChartSample[] samples = new ChartSample[60];
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = new ChartSample(now.AddMilliseconds((i - 59) * 50),
                    0.0123F, 0.0000456F, 0.00056F, 0.000006F, 0.0125F, 4700F, 0.007F, 0.000004F);
            }
            buffer.Append(samples);
            Set(form, "_buffer", buffer);
            Set(form, "_renderedVersion", -1L);
            Set(form, "_readoutVersion", -1L);
            Set(form, "_nextReadoutAt", 0L);
            Set(form, "_nextCounterReadoutAt", 0L);
            Get<DarkSelector>(form, "_windowCombo").SelectedIndex = 1;
            Invoke(form, "SelectChart", ChartMetric.Current);
            Invoke(form, "OnRefreshTick", null, EventArgs.Empty);
            MetricTile current = Get<MetricTile>(form, "_currentTile");
            Assert(Get<string>(current, "_value") == "45.600" && Get<string>(current, "_unit") == "μA", "current card unit and number agree");
            Assert(Get<Label>(form, "_whCaptionLabel").Text == "能量 / μWh" && Get<Label>(form, "_whValueLabel").Text == "6.000", "energy readout auto unit");
            Assert(Get<Label>(form, "_ahCaptionLabel").Text == "电量 / mAh", "charge readout auto unit");
            Assert(form.ShowInTaskbar && form.Text.Contains("I:45.600 μA") && form.Text.Contains("Ah:12.500 mAh"),
                "taskbar title directly shows current and charge with auto units");
            Assert(Get<NotifyIcon>(form, "_taskbarNotifyIcon").Text.Contains("电流 45.600 μA") &&
                Get<NotifyIcon>(form, "_taskbarNotifyIcon").Text.Contains("电量 12.500 mAh"),
                "Windows taskbar notification icon shows current and charge on hover");
            TaskbarReadoutForm taskbarReadout = Get<TaskbarReadoutForm>(form, "_taskbarReadout");
            Assert(taskbarReadout.CurrentText == "45.600 μA" && taskbarReadout.ChargeText == "12.500 mAh" &&
                !taskbarReadout.ShowInTaskbar && taskbarReadout.TopMost,
                "always-on-top taskbar-adjacent readout receives current and charge values");
            Assert(Get<Label>(form, "_resistanceCaptionLabel").Text == "阻值 / kΩ", "resistance readout auto unit");
            Assert(Get<Label>(form, "_dpCaptionLabel").Text == "D+ / mV" && Get<Label>(form, "_dmCaptionLabel").Text == "D− / μV", "data-line voltage units");
            Assert(Get<TelemetrySnapshot>(form, "_snapshot").Latest.Current == samples[59].Current, "display conversion leaves raw data intact");
            Set(Get<TelemetryChart>(form, "_chart"), "_hoverX", 600);
            Render(form, 1120, 720, "dashboard-auto-units.png");
            Assert(Get<Label>(form, "_whValueLabel").Height > 30 &&
                Get<Label>(form, "_ahValueLabel").Height > 30,
                "cumulative values have enough layout height to render without clipping");
            Assert(Get<MeasurementUnit>(Get<TelemetryChart>(form, "_chart"), "_displayUnit").Symbol == "μA", "chart axis auto unit");
            Set(current, "_nextPeakTimestamp", 0L);
            current.SetValue(0.002, 2.0);
            Assert(Get<string>(current, "_unit") == "mA" && Get<string>(current, "_subtitle") == "峰值 2.000 A", "peak uses its own appropriate unit");
            current.Clear();
            Assert(Get<string>(current, "_unit") == "A", "empty card restores base unit");
            TelemetryChart chart = Get<TelemetryChart>(form, "_chart");
            Invoke(chart, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
            Assert(Get<long>(form, "_windowTicks") > 30L * TimeSpan.TicksPerSecond &&
                Get<long>(form, "_windowTicks") < 60L * TimeSpan.TicksPerSecond &&
                Get<DarkSelector>(form, "_windowCombo").SelectedIndex == -1,
                "scrolling up selects a custom window beyond preset choices");
            Invoke(chart, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
            Invoke(chart, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
            Invoke(chart, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
            Assert(Get<long>(form, "_windowTicks") > 60L * TimeSpan.TicksPerSecond,
                "scrolling expands beyond the former60-second limit");
            Invoke(chart, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120));
            Assert(Get<long>(form, "_windowTicks") > 30L * TimeSpan.TicksPerSecond &&
                Get<long>(form, "_windowTicks") < 60L * TimeSpan.TicksPerSecond &&
                Get<DarkSelector>(form, "_windowCombo").SelectedIndex == -1,
                "scrolling down adjusts a custom window without snapping to presets");
            for (int i = 0; i < 60; i++) Invoke(chart, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120));
            Assert(Get<long>(form, "_windowTicks") < TimeSpan.TicksPerSecond,
                "scrolling can reduce the window below one second");
            Assert(Get<long>(form, "_windowTicks") > 0, "window remains positive at its minimum resolution");
            chart.SetData(new TelemetrySnapshot
            {
                StartUtc = now.AddSeconds(-10), EndUtc = now,
                Points = new ChartSample[] { new ChartSample(now, 0, 0, 0, 0, 0, 0, 0, 0) }
            });
            using (Bitmap bitmap = new Bitmap(chart.Width, chart.Height))
                chart.DrawToBitmap(bitmap, chart.ClientRectangle);
            Assert(Get<MeasurementUnit>(chart, "_displayUnit").Symbol == "A", "all-zero chart retains base unit");
        }

        private static void CheckContinuousRefresh()
        {
            using (DashboardForm form = new DashboardForm())
            {
                Get<System.Windows.Forms.Timer>(form, "_clockTimer").Stop();
                IntPtr handle = form.Handle;
                form.ClientSize = new Size(1920, 1080);
                Control page = form.Controls[0];
                form.Controls.Remove(page); page.Dock = DockStyle.None;
                page.Bounds = new Rectangle(0, 0, 1920, 1080);
                Layout(page);
                TelemetryBuffer buffer = new TelemetryBuffer();
                Set(form, "_buffer", buffer);
                Label[] liveLabels = {
                    Get<Label>(form, "_whValueLabel"), Get<Label>(form, "_ahValueLabel"),
                    Get<Label>(form, "_sessionLabel"), Get<Label>(form, "_resistanceValueLabel")
                };
                int parentPaints = 0;
                foreach (Label label in liveLabels) label.Parent.Paint += delegate { parentPaints++; };
                DateTime epoch = DateTime.UtcNow.AddSeconds(-10);
                Stopwatch watch = Stopwatch.StartNew();
                for (int frame = 0; frame < 200; frame++)
                {
                    ChartSample[] batch = new ChartSample[50];
                    for (int i = 0; i < batch.Length; i++)
                    {
                        float voltage = 4.985F + (float)Math.Sin((frame * 50 + i) * 0.17) * 0.01F;
                        batch[i] = new ChartSample(epoch.AddMilliseconds(frame * 50 + i), voltage,
                            0.4F, voltage * 0.4F, 0.02655F + frame * 0.00001F,
                            0.005327F + frame * 0.000002F, 12.453F, 0.0016F, 0.0886F);
                    }
                    buffer.Append(batch);
                    Invoke(form, "OnRefreshTick", null, EventArgs.Empty);
                    foreach (Label label in liveLabels)
                    {
                        using (Bitmap bitmap = new Bitmap(label.Width, label.Height))
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.Magenta);
                            PaintControl(label, graphics);
                            Assert(bitmap.GetPixel(0, 0).ToArgb() == label.BackColor.ToArgb(), "streaming label leaves no stale background");
                        }
                    }
                    using (Bitmap bitmap = new Bitmap(Get<TelemetryChart>(form, "_chart").Width,
                        Get<TelemetryChart>(form, "_chart").Height))
                        Get<TelemetryChart>(form, "_chart").DrawToBitmap(bitmap, Get<TelemetryChart>(form, "_chart").ClientRectangle);
                }
                watch.Stop();
                Assert(parentPaints == 0, "continuous readout painting must not replay surrounding panels");
                Assert(buffer.Total == 10000, "continuous refresh preserves ingestion count");
                Console.WriteLine("Continuous paint: 200 refreshes / 10,000 samples; parent replay count=" + parentPaints +
                    "; elapsed=" + watch.ElapsedMilliseconds + " ms.");
                page.Dock = DockStyle.Fill; form.Controls.Add(page);
                Get<Label>(form, "_headerStateLabel").Text = "绘制检查 · 合成数据";
                Render(form, 1920, 1080, "dashboard-streaming.png");
                Render(form, 3756, 2119, "dashboard-streaming-4k.png");
            }
        }

        private static void Render(DashboardForm form, int width, int height, string name)
        {
            form.ClientSize = new Size(width, height);
            Control page = form.Controls[0];
            form.Controls.Remove(page);
            page.Dock = DockStyle.None;
            page.Bounds = new Rectangle(0, 0, width, height);
            Layout(page);
            Application.DoEvents();
            TelemetryChart chart = Get<TelemetryChart>(form, "_chart");
            Assert(chart.Width > 300 && chart.Height >= 100, "chart has usable space");
            using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), ImageFormat.Png);
            }
            foreach (Control child in Descendants(page))
            {
                if (!(child is ActionButton) && !(child is ComboBox) && !(child is ProtocolChip)) continue;
                Assert(child.Parent.ClientRectangle.Contains(child.Bounds), "control contained: " + child.Text +
                    " bounds=" + child.Bounds + " parent=" + child.Parent.ClientRectangle);
            }
            page.Dock = DockStyle.Fill;
            form.Controls.Add(page);
        }

        private static void Layout(Control control)
        {
            control.CreateControl(); control.PerformLayout();
            foreach (Control child in control.Controls) Layout(child);
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                yield return control;
                foreach (Control child in Descendants(control)) yield return child;
            }
        }

        private static T Get<T>(object target, string name) { return (T)target.GetType().GetField(name, PrivateInstance).GetValue(target); }
        private static void Set(object target, string name, object value) { target.GetType().GetField(name, PrivateInstance).SetValue(target, value); }
        private static void Invoke(object target, string name, params object[] args) { target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args); }
        private static void Assert(bool value, string message) { _assertions++; if (!value) throw new InvalidOperationException("Check failed: " + message); }
    }
}
