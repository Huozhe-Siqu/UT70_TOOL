using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace UT70Dashboard
{
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(9, 14, 18);
        public static readonly Color Surface = Color.FromArgb(15, 23, 28);
        public static readonly Color SurfaceRaised = Color.FromArgb(20, 30, 36);
        public static readonly Color Border = Color.FromArgb(35, 49, 56);
        public static readonly Color Text = Color.FromArgb(232, 241, 240);
        public static readonly Color Muted = Color.FromArgb(124, 144, 149);
        public static readonly Color Faint = Color.FromArgb(76, 94, 100);
        public static readonly Color Cyan = Color.FromArgb(72, 226, 210);
        public static readonly Color Lime = Color.FromArgb(194, 239, 105);
        public static readonly Color Blue = Color.FromArgb(108, 167, 255);
        public static readonly Color Amber = Color.FromArgb(247, 177, 83);
        public static readonly Color Red = Color.FromArgb(255, 111, 111);
    }

    internal static class Ui
    {
        public static Label Label(string text, float size, Color color, FontStyle style)
        {
            return new BufferedLabel
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Empty,
                Font = new Font("Segoe UI", size, style, GraphicsUnit.Point),
                AutoSize = true,
                UseCompatibleTextRendering = false,
                Margin = Padding.Empty
            };
        }

        public static void DrawRoundRect(Graphics graphics, Rectangle rect, int radius, Color color)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
                graphics.FillPath(brush, path);
        }

        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            radius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BufferedLabel : Label
    {
        private string _displayText = String.Empty;

        public BufferedLabel()
        {
            // 继承父容器的不透明底色，在同一缓冲中完成擦除与文字绘制。
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public override string Text
        {
            get { return _displayText ?? String.Empty; }
            set
            {
                string next = value ?? String.Empty;
                if (Text == next) return;
                // 只更新绘制缓存，避免原生标签在 WM_SETTEXT 时先擦除正在显示的文字。
                _displayText = next;
                OnTextChanged(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            base.OnPaint(e);
        }
    }

    internal sealed class ReadoutValueLabel : Label
    {
        private string _displayText = String.Empty;

        public ReadoutValueLabel()
        {
            BackColor = Theme.Surface;
            AutoSize = false;
            AutoEllipsis = true;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            TextAlign = ContentAlignment.MiddleLeft;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }

        public override string Text
        {
            get { return _displayText; }
            set
            {
                string next = value ?? String.Empty;
                if (_displayText == next) return;
                _displayText = next;
                OnTextChanged(EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);
            if (_displayText.Length == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            RectangleF bounds = new RectangleF(Padding.Left, Padding.Top,
                Math.Max(0, ClientSize.Width - Padding.Horizontal),
                Math.Max(0, ClientSize.Height - Padding.Vertical));
            using (SolidBrush text = new SolidBrush(ForeColor))
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = AutoEllipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                format.FormatFlags = StringFormatFlags.NoWrap;
                e.Graphics.DrawString(_displayText, Font, text, bounds, format);
            }
        }
    }

    internal sealed class TaskbarReadoutForm : Form
    {
        private string _currentText = "-- A";
        private string _chargeText = "-- Ah";
        private Point _dragStart;
        private Point _formStart;
        private bool _dragging;
        private bool _vertical;
        private bool _loadingSettings;
        private readonly ContextMenuStrip _opacityMenu;
        private readonly string _settingsPath;
        private bool _hasSavedLocation;

        public string CurrentText { get { return _currentText; } }
        public string ChargeText { get { return _chargeText; } }
        public bool IsVertical { get { return _vertical; } }
        public int OpacityPercent { get { return (int)Math.Round(Opacity * 100); } }

        public TaskbarReadoutForm() : this(GetDefaultSettingsPath()) { }

        internal TaskbarReadoutForm(string settingsPath)
        {
            _settingsPath = settingsPath;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Back;
            Size = new Size(286, 68);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _opacityMenu = new ContextMenuStrip
            {
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Text,
                ShowImageMargin = false
            };
            foreach (int percent in new int[] { 100, 85, 70, 55 })
            {
                int selectedPercent = percent;
                ToolStripMenuItem item = new ToolStripMenuItem(percent + "%")
                {
                    Checked = percent == 100,
                    CheckOnClick = false
                };
                item.Click += delegate
                {
                    Opacity = selectedPercent / 100.0;
                    foreach (ToolStripItem menuItem in _opacityMenu.Items)
                    {
                        ToolStripMenuItem option = menuItem as ToolStripMenuItem;
                        if (option != null) option.Checked = option == item;
                    }
                    SaveSettings();
                };
                _opacityMenu.Items.Add(item);
            }

            ActionButton orientationButton = new ActionButton
            {
                Text = "↕", Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                Size = new Size(22, 20), Location = new Point(235, 5),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = Padding.Empty
            };
            orientationButton.Click += delegate { SetVertical(!_vertical); };
            Controls.Add(orientationButton);

            ActionButton closeButton = new ActionButton
            {
                Text = "×", Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Size = new Size(22, 20), Location = new Point(259, 5),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = Padding.Empty
            };
            closeButton.Click += delegate { Hide(); };
            Controls.Add(closeButton);

            LoadSettings();
        }

        public void SetVertical(bool vertical)
        {
            if (_vertical == vertical) return;
            _vertical = vertical;
            Size = vertical ? new Size(124, 228) : new Size(286, 68);
            Control orientation = Controls[0];
            Control close = Controls[1];
            orientation.Location = new Point(Width - 48, 5);
            close.Location = new Point(Width - 24, 5);
            if (!_loadingSettings) SaveSettings();
            Invalidate();
        }

        public void SetReadings(string current, string charge)
        {
            current = current ?? String.Empty;
            charge = charge ?? String.Empty;
            if (_currentText == current && _chargeText == charge) return;
            _currentText = current;
            _chargeText = charge;
            Invalidate();
        }

        public void ShowAboveTaskbar(Rectangle workingArea)
        {
            if (!_hasSavedLocation)
                Location = new Point(workingArea.Right - Width - 16, workingArea.Bottom - Height - 12);
            else
                RestoreVisibleLocation();
            if (!Visible) Show();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _formStart = Location;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point cursor = Cursor.Position;
                Location = new Point(_formStart.X + cursor.X - _dragStart.X,
                    _formStart.Y + cursor.Y - _dragStart.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                SaveSettings();
            }
            else if (e.Button == MouseButtons.Right) _opacityMenu.Show(this, e.Location);
            base.OnMouseUp(e);
        }

        private static string GetDefaultSettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UT70Dashboard", "taskbar-readout.xml");
        }

        private void LoadSettings()
        {
            if (String.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath)) return;
            _loadingSettings = true;
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(_settingsPath);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "taskbarReadout") return;

                bool vertical;
                if (Boolean.TryParse(root.GetAttribute("vertical"), out vertical)) SetVertical(vertical);

                int opacityPercent;
                if (Int32.TryParse(root.GetAttribute("opacity"), out opacityPercent))
                {
                    opacityPercent = Math.Max(30, Math.Min(100, opacityPercent));
                    Opacity = opacityPercent / 100.0;
                    foreach (ToolStripItem menuItem in _opacityMenu.Items)
                    {
                        ToolStripMenuItem option = menuItem as ToolStripMenuItem;
                        if (option != null) option.Checked = option.Text == opacityPercent + "%";
                    }
                }

                int x, y;
                if (Int32.TryParse(root.GetAttribute("x"), out x) && Int32.TryParse(root.GetAttribute("y"), out y))
                {
                    Location = new Point(x, y);
                    _hasSavedLocation = true;
                }
            }
            catch (Exception) { }
            finally { _loadingSettings = false; }
        }

        private void RestoreVisibleLocation()
        {
            Rectangle savedBounds = new Rectangle(Location, Size);
            Rectangle workingArea = Screen.FromRectangle(savedBounds).WorkingArea;
            int maxX = Math.Max(workingArea.Left, workingArea.Right - Width);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - Height);
            Location = new Point(Math.Max(workingArea.Left, Math.Min(maxX, Location.X)),
                Math.Max(workingArea.Top, Math.Min(maxY, Location.Y)));
        }

        private void SaveSettings()
        {
            if (_loadingSettings || String.IsNullOrEmpty(_settingsPath) || IsDisposed) return;
            try
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement("taskbarReadout");
                root.SetAttribute("vertical", _vertical.ToString());
                root.SetAttribute("opacity", OpacityPercent.ToString());
                root.SetAttribute("x", Location.X.ToString());
                root.SetAttribute("y", Location.Y.ToString());
                document.AppendChild(root);
                document.Save(_settingsPath);
            }
            catch (Exception) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.RoundRect(bounds, 10))
            using (SolidBrush background = new SolidBrush(Theme.Surface))
            using (Pen border = new Pen(Theme.Border))
            using (SolidBrush muted = new SolidBrush(Theme.Muted))
            using (SolidBrush currentBrush = new SolidBrush(Theme.Lime))
            using (SolidBrush chargeBrush = new SolidBrush(Theme.Cyan))
            using (Font heading = new Font("Segoe UI", 7.5F, FontStyle.Bold))
            using (Font caption = new Font("Segoe UI", 7F, FontStyle.Regular))
            using (Font value = new Font("Consolas", 12F, FontStyle.Bold))
            {
                graphics.FillPath(background, path);
                graphics.DrawPath(border, path);
                if (_vertical)
                {
                    graphics.DrawString("I", heading, muted, 14, 7);
                    graphics.DrawString("Ah", heading, muted, 67, 7);
                    DrawVerticalText(graphics, "电流", caption, muted, 12, 36, 14);
                    DrawVerticalText(graphics, _currentText, value, currentBrush, 34, 36, 15);
                    DrawVerticalText(graphics, "电量", caption, muted, 65, 36, 14);
                    DrawVerticalText(graphics, _chargeText, value, chargeBrush, 87, 36, 15);
                }
                else
                {
                    graphics.DrawString("UT70", heading, muted, 10, 7);
                    graphics.DrawString("电流", caption, muted, 10, 37);
                    graphics.DrawString(_currentText, value, currentBrush, 44, 33);
                    graphics.DrawString("电量", caption, muted, 151, 37);
                    graphics.DrawString(_chargeText, value, chargeBrush, 185, 33);
                }
            }
            base.OnPaint(e);
        }

        private static void DrawVerticalText(Graphics graphics, string text, Font font, Brush brush,
            float x, float y, float lineHeight)
        {
            if (text == null) return;
            for (int i = 0; i < text.Length; i++)
            {
                if (Char.IsWhiteSpace(text[i])) continue;
                graphics.DrawString(text[i].ToString(), font, brush, x, y);
                y += lineHeight;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SaveSettings();
                if (_opacityMenu != null) _opacityMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class DashboardCard : Panel
    {
        public DashboardCard()
        {
            BackColor = Theme.Surface;
            Padding = new Padding(18);
            Margin = Padding.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width < 3 || Height < 3) return;
            using (SolidBrush background = new SolidBrush(Parent == null ? Theme.Back : Parent.BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.RoundRect(bounds, 12))
            using (SolidBrush brush = new SolidBrush(Theme.Surface))
            using (Pen pen = new Pen(Theme.Border, 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal class ActionButton : Button
    {
        private bool _accent;
        public bool Accent { get { return _accent; } set { if (_accent != value) { _accent = value; Invalidate(); } } }
        private bool _hot;

        public ActionButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.SurfaceRaised;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Parent == null ? Theme.Surface : Parent.BackColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Accent ? Theme.Cyan : Theme.SurfaceRaised;
            Color text = Accent ? Theme.Back : Theme.Text;
            if (_hot)
            {
                fill = Accent ? Color.FromArgb(112, 244, 228) : Color.FromArgb(29, 43, 49);
                if (!Accent) text = Color.White;
            }
            if (!Enabled)
            {
                fill = Color.FromArgb(24, 33, 38);
                text = Theme.Faint;
            }
            Ui.DrawRoundRect(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 8, fill);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), text, fill);
        }
    }

    internal class StatusDot : Control
    {
        private Color _dotColor;
        public Color DotColor { get { return _dotColor; } set { if (_dotColor != value) { _dotColor = value; Invalidate(); } } }

        public StatusDot()
        {
            DotColor = Theme.Faint;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(14, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(35, DotColor)))
            using (SolidBrush dot = new SolidBrush(DotColor))
            {
                e.Graphics.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
                e.Graphics.FillEllipse(dot, 4, 4, Math.Max(2, Width - 9), Math.Max(2, Height - 9));
            }
        }
    }

    internal sealed class DarkSelector : ActionButton
    {
        private readonly ContextMenuStrip _menu;
        private int _selectedIndex = -1;
        public readonly List<object> Items = new List<object>();
        public event EventHandler SelectedIndexChanged;

        public DarkSelector()
        {
            Text = "选择 ▾";
            _menu = new ContextMenuStrip
            {
                BackColor = Theme.SurfaceRaised, ForeColor = Theme.Text,
                Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
                ShowImageMargin = false
            };
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("value");
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                Text = (value < 0 ? "选择" : Items[value].ToString()) + " ▾";
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public void SetCustomText(string text)
        {
            if (_selectedIndex != -1) SelectedIndex = -1;
            Text = text + " ▾";
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _menu.Items.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(Items[i].ToString())
                {
                    Checked = i == _selectedIndex, ForeColor = Theme.Text,
                    BackColor = Theme.SurfaceRaised
                };
                item.Click += delegate { SelectedIndex = index; };
                _menu.Items.Add(item);
            }
            if (_menu.Items.Count > 0) _menu.Show(this, new Point(0, Height));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Items.Count > 0 && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, _selectedIndex + (e.KeyCode == Keys.Down ? 1 : -1)));
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _menu != null) _menu.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.SurfaceRaised; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Cyan; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Border; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Border; } }
    }

    internal class Sparkline : Control
    {
        private readonly System.Collections.Generic.List<float> _values = new System.Collections.Generic.List<float>();
        public Color LineColor { get; set; }

        public Sparkline()
        {
            LineColor = Theme.Cyan;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            BackColor = Theme.Surface;
        }

        public void Add(float value)
        {
            _values.Add(value);
            if (_values.Count > 42) _values.RemoveAt(0);
            Invalidate();
        }

        public void Clear()
        {
            _values.Clear();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            base.OnPaint(e);
            if (_values.Count < 2 || Width < 4 || Height < 4) return;
            double min = _values[0], max = _values[0];
            foreach (float value in _values) { min = Math.Min(min, value); max = Math.Max(max, value); }
            if (Math.Abs(max - min) < 0.00001f) { min -= 0.5f; max += 0.5f; }
            using (Pen pen = new Pen(LineColor, 1.8f))
            {
                pen.LineJoin = LineJoin.Round;
                PointF[] points = new PointF[_values.Count];
                for (int i = 0; i < _values.Count; i++)
                {
                    float x = 2 + (Width - 4) * i / (float)(_values.Count - 1);
                    float y = (float)(Height - 3 - (_values[i] - min) / (max - min) * (Height - 7));
                    points[i] = new PointF(x, y);
                }
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, points);
            }
        }
    }
}
