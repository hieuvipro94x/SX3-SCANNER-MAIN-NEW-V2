using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SX3VersionMonitor
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Windows cũ vẫn có thể chạy bình thường nếu DPI API không khả dụng.
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MonitorForm());
        }
    }

    internal sealed class MonitorForm : Form
    {
        private static readonly Color PageBackground = Color.FromArgb(244, 247, 251);
        private static readonly Color HeaderStart = Color.FromArgb(15, 23, 42);
        private static readonly Color HeaderEnd = Color.FromArgb(30, 64, 175);
        private static readonly Color PrimaryBlue = Color.FromArgb(37, 99, 235);
        private static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
        private static readonly Color TextMain = Color.FromArgb(15, 23, 42);
        private static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
        private static readonly Color BorderColor = Color.FromArgb(218, 226, 237);
        private static readonly Color OkColor = Color.FromArgb(22, 163, 74);
        private static readonly Color WarningColor = Color.FromArgb(234, 88, 12);
        private static readonly Color DangerColor = Color.FromArgb(220, 38, 38);
        private static readonly TimeSpan OfflineAfter = TimeSpan.FromHours(24);
        private const string DefaultStatusFolder = @"\\192.168.10.150\public\DB\SX3VersionStatus";

        private ModernTextBox _folderTextBox;
        private ModernTextBox _latestVersionTextBox;
        private ModernTextBox _searchTextBox;
        private Label _statusLabel;
        private DataGridView _grid;
        private Timer _refreshTimer;
        private Timer _entranceTimer;
        private readonly string _settingsPath;
        private StatCard _totalCard;
        private StatCard _okCard;
        private StatCard _outdatedCard;
        private StatCard _offlineCard;
        private Label _lastRefreshLabel;
        private Label _autoRefreshStateLabel;
        private CheckBox _autoRefreshCheckBox;
        private ModernButton _refreshButton;
        private AnimatedLoadingBar _loadingBar;
        private Label _emptyLabel;
        private readonly ToolTip _toolTip;
        private readonly Font _statusBadgeFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        private List<ClientReport> _reports = new List<ClientReport>();
        private bool _isLoading;
        private string _baseStatusText = "Sẵn sàng.";
        private StatusKind _baseStatusKind = StatusKind.Neutral;

        internal MonitorForm()
        {
            Text = "SX3 Version Monitor";
            Width = 1320;
            Height = 820;
            MinimumSize = new Size(1080, 660);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = PageBackground;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            DoubleBuffered = true;
            Opacity = 0D;

            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SX3VersionMonitor",
                "settings.txt");

            _toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 350,
                ReshowDelay = 100,
                ShowAlways = true
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1,
                Padding = new Padding(20, 18, 20, 18),
                BackColor = PageBackground,
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 146F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildSettingsPanel(), 0, 1);
            root.Controls.Add(BuildCards(), 0, 2);
            root.Controls.Add(BuildToolbar(), 0, 3);

            _loadingBar = new AnimatedLoadingBar
            {
                Dock = DockStyle.Fill,
                AccentColor = PrimaryBlue,
                Margin = new Padding(0)
            };
            root.Controls.Add(_loadingBar, 0, 4);

            root.Controls.Add(BuildGrid(), 0, 5);

            _refreshTimer = new Timer { Interval = 30000 };
            _refreshTimer.Tick += delegate
            {
                if (_autoRefreshCheckBox.Checked)
                    LoadReports();
            };
            _refreshTimer.Start();

            _entranceTimer = new Timer { Interval = 16 };
            _entranceTimer.Tick += delegate
            {
                Opacity = Math.Min(1D, Opacity + 0.09D);
                if (Opacity >= 1D)
                    _entranceTimer.Stop();
            };

            Shown += delegate
            {
                _entranceTimer.Start();
                _totalCard.StartEntrance(0);
                _okCard.StartEntrance(60);
                _outdatedCard.StartEntrance(120);
                _offlineCard.StartEntrance(180);
            };

            FormClosed += delegate
            {
                if (_refreshTimer != null) _refreshTimer.Stop();
                if (_entranceTimer != null) _entranceTimer.Stop();
                if (_statusBadgeFont != null) _statusBadgeFont.Dispose();
            };

            KeyDown += MonitorFormKeyDown;

            LoadSettings();
            LoadReports();
        }

        private Control BuildHeader()
        {
            var panel = new GradientHeaderPanel
            {
                Dock = DockStyle.Fill,
                StartColor = HeaderStart,
                EndColor = HeaderEnd,
                CornerRadius = 18,
                Margin = new Padding(0, 0, 0, 12)
            };

            var eyebrow = new Label
            {
                Text = "●  LIVE MONITOR",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(147, 197, 253),
                BackColor = Color.Transparent,
                Location = new Point(26, 18)
            };
            panel.Controls.Add(eyebrow);

            var title = new Label
            {
                Text = "SX3 Version Monitor",
                AutoSize = true,
                Font = new Font("Segoe UI", 23F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(23, 36)
            };
            panel.Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Theo dõi trạng thái và phiên bản ứng dụng SCAN trên toàn bộ máy client",
                AutoSize = true,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(203, 213, 225),
                BackColor = Color.Transparent,
                Location = new Point(27, 78)
            };
            panel.Controls.Add(subtitle);

            _lastRefreshLabel = new Label
            {
                Text = "Chưa làm mới",
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            panel.Controls.Add(_lastRefreshLabel);

            _autoRefreshStateLabel = new Label
            {
                Text = "Tự động làm mới mỗi 30 giây",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(191, 219, 254),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            panel.Controls.Add(_autoRefreshStateLabel);

            panel.Resize += delegate
            {
                PositionHeaderRightInfo(panel);
            };
            panel.HandleCreated += delegate
            {
                PositionHeaderRightInfo(panel);
            };

            return panel;
        }

        private void PositionHeaderRightInfo(Control panel)
        {
            if (_lastRefreshLabel == null || _autoRefreshStateLabel == null)
                return;

            _lastRefreshLabel.Location = new Point(
                Math.Max(500, panel.Width - _lastRefreshLabel.Width - 28),
                36);
            _autoRefreshStateLabel.Location = new Point(
                Math.Max(500, panel.Width - _autoRefreshStateLabel.Width - 28),
                62);
        }

        private Control BuildSettingsPanel()
        {
            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderColor = BorderColor,
                CornerRadius = 14,
                Padding = new Padding(18, 14, 18, 12),
                Margin = new Padding(0, 0, 0, 12)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 2,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.Controls.Add(panel);

            panel.Controls.Add(CreateFieldLabel("THƯ MỤC STATUS"), 0, 0);
            _folderTextBox = CreateInputTextBox("Đường dẫn thư mục chứa file JSON status");
            panel.Controls.Add(_folderTextBox, 1, 0);

            var browseButton = CreateButton("Chọn...", Color.White, TextMain, BorderColor, Color.FromArgb(248, 250, 252));
            browseButton.Margin = new Padding(4, 1, 0, 1);
            browseButton.Click += delegate { BrowseFolder(); };
            _toolTip.SetToolTip(browseButton, "Chọn thư mục status");
            panel.Controls.Add(browseButton, 2, 0);

            panel.Controls.Add(CreateFieldLabel("VERSION MỚI NHẤT"), 4, 0);
            _latestVersionTextBox = CreateInputTextBox("Ví dụ: 1.2.3.0");
            panel.Controls.Add(_latestVersionTextBox, 5, 0);

            var saveButton = CreateButton("Lưu", PrimaryBlue, Color.White, PrimaryBlue, PrimaryHover);
            saveButton.Margin = new Padding(4, 1, 0, 1);
            saveButton.Click += delegate
            {
                if (SaveSettings())
                    LoadReports();
            };
            _toolTip.SetToolTip(saveButton, "Lưu cấu hình và làm mới dữ liệu");
            panel.Controls.Add(saveButton, 6, 0);

            _statusLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 4, 0, 0),
                Text = "●  Sẵn sàng."
            };
            panel.SetColumnSpan(_statusLabel, 7);
            panel.Controls.Add(_statusLabel, 0, 1);

            return card;
        }

        private Control BuildCards()
        {
            var cards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(0),
                BackColor = PageBackground
            };
            for (int i = 0; i < 4; i++)
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            _totalCard = new StatCard("TỔNG CLIENT", "Máy đã gửi trạng thái", PrimaryBlue, "●")
            {
                Margin = new Padding(0, 0, 10, 0)
            };
            _okCard = new StatCard("ĐÃ MỚI NHẤT", "Đúng phiên bản hiện tại", OkColor, "✓")
            {
                Margin = new Padding(4, 0, 6, 0)
            };
            _outdatedCard = new StatCard("CẦN UPDATE", "Phiên bản thấp hơn latest", WarningColor, "!")
            {
                Margin = new Padding(6, 0, 4, 0)
            };
            _offlineCard = new StatCard("MẤT LIÊN LẠC", "Không báo quá 24 giờ", DangerColor, "×")
            {
                Margin = new Padding(10, 0, 0, 0)
            };

            cards.Controls.Add(_totalCard, 0, 0);
            cards.Controls.Add(_okCard, 1, 0);
            cards.Controls.Add(_outdatedCard, 2, 0);
            cards.Controls.Add(_offlineCard, 3, 0);
            return cards;
        }

        private Control BuildToolbar()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                BackColor = PageBackground,
                Margin = new Padding(0),
                Padding = new Padding(0, 5, 0, 5)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));

            panel.Controls.Add(CreateFieldLabel("TÌM MÁY"), 0, 0);
            _searchTextBox = CreateInputTextBox("Tên máy, user, version...");
            _searchTextBox.Margin = new Padding(8, 0, 12, 0);
            _searchTextBox.TextChanged += delegate { RenderReports(); };
            panel.Controls.Add(_searchTextBox, 1, 0);

            _autoRefreshCheckBox = new CheckBox
            {
                Text = "Tự làm mới 30s",
                Checked = true,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextMuted,
                Margin = new Padding(0, 0, 14, 0),
                Cursor = Cursors.Hand
            };
            _autoRefreshCheckBox.CheckedChanged += delegate
            {
                _autoRefreshStateLabel.Text = _autoRefreshCheckBox.Checked
                    ? "Tự động làm mới mỗi 30 giây"
                    : "Đang tạm dừng tự động làm mới";
                PositionHeaderRightInfo(_autoRefreshStateLabel.Parent);
            };
            panel.Controls.Add(_autoRefreshCheckBox, 3, 0);

            var openButton = CreateButton("Mở thư mục", Color.White, TextMain, BorderColor, Color.FromArgb(248, 250, 252));
            openButton.Click += delegate { OpenStatusFolder(); };
            _toolTip.SetToolTip(openButton, "Mở thư mục status bằng File Explorer");
            panel.Controls.Add(openButton, 4, 0);

            var copyButton = CreateButton("Copy path", Color.White, TextMain, BorderColor, Color.FromArgb(248, 250, 252));
            copyButton.Click += delegate { CopyStatusPath(); };
            _toolTip.SetToolTip(copyButton, "Sao chép đường dẫn thư mục");
            panel.Controls.Add(copyButton, 5, 0);

            var clearButton = CreateButton("Xóa lọc", Color.White, TextMain, BorderColor, Color.FromArgb(248, 250, 252));
            clearButton.Click += delegate
            {
                _searchTextBox.Text = string.Empty;
                _searchTextBox.FocusInput();
            };
            _toolTip.SetToolTip(clearButton, "Xóa nội dung tìm kiếm");
            panel.Controls.Add(clearButton, 6, 0);

            _refreshButton = CreateButton("Làm mới", PrimaryBlue, Color.White, PrimaryBlue, PrimaryHover);
            _refreshButton.Click += delegate { LoadReports(); };
            _toolTip.SetToolTip(_refreshButton, "Làm mới ngay (F5)");
            panel.Controls.Add(_refreshButton, 7, 0);

            return panel;
        }

        private Control BuildGrid()
        {
            var gridShell = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderColor = BorderColor,
                CornerRadius = 14,
                Padding = new Padding(1),
                Margin = new Padding(0, 8, 0, 0)
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(235, 240, 246),
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                RowTemplate = { Height = 44 },
                ScrollBars = ScrollBars.Both
            };
            ConfigureGrid();
            gridShell.Controls.Add(_grid);

            _emptyLabel = new Label
            {
                Text = "Không có dữ liệu để hiển thị",
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.White,
                Visible = false
            };
            gridShell.Controls.Add(_emptyLabel);
            _emptyLabel.BringToFront();

            gridShell.Resize += delegate
            {
                PositionEmptyLabel(gridShell);
            };

            return gridShell;
        }

        private void ConfigureGrid()
        {
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMain;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 8, 0);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.ColumnHeadersHeight = 44;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.2F);
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.BackColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 254);
            _grid.DefaultCellStyle.SelectionForeColor = TextMain;
            _grid.DefaultCellStyle.Padding = new Padding(10, 0, 8, 0);
            _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 254);

            _grid.Columns.Add("MachineName", "MÁY");
            _grid.Columns.Add("AppVersion", "VERSION APP");
            _grid.Columns.Add("LatestKnownVersion", "LATEST");
            _grid.Columns.Add("Status", "TRẠNG THÁI");
            _grid.Columns.Add("LastSeen", "LẦN BÁO CUỐI");
            _grid.Columns.Add("WindowsUser", "USER WINDOWS");
            _grid.Columns.Add("UpdateStatus", "GHI CHÚ UPDATE");
            _grid.Columns[0].FillWeight = 92;
            _grid.Columns[1].FillWeight = 78;
            _grid.Columns[2].FillWeight = 72;
            _grid.Columns[3].FillWeight = 94;
            _grid.Columns[4].FillWeight = 108;
            _grid.Columns[5].FillWeight = 98;
            _grid.Columns[6].FillWeight = 220;
            _grid.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", 9.2F, FontStyle.Bold);
            _grid.Columns[1].DefaultCellStyle.Font = new Font("Consolas", 9F, FontStyle.Bold);
            _grid.Columns[2].DefaultCellStyle.Font = new Font("Consolas", 9F);

            _grid.CellPainting += GridCellPainting;
            _grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0)
                {
                    string machine = Convert.ToString(_grid.Rows[e.RowIndex].Cells[0].Value);
                    if (!string.IsNullOrWhiteSpace(machine) && machine != "-")
                    {
                        Clipboard.SetText(machine);
                        SetStatus("Đã sao chép tên máy: " + machine, StatusKind.Success);
                    }
                }
            };
        }

        private void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3)
                return;

            e.PaintBackground(e.CellBounds, true);

            string text = Convert.ToString(e.FormattedValue) ?? string.Empty;
            Color fill;
            Color foreground;
            if (text == "OK")
            {
                fill = Color.FromArgb(220, 252, 231);
                foreground = Color.FromArgb(21, 128, 61);
            }
            else if (text == "MẤT LIÊN LẠC")
            {
                fill = Color.FromArgb(255, 237, 213);
                foreground = Color.FromArgb(194, 65, 12);
            }
            else
            {
                fill = Color.FromArgb(254, 226, 226);
                foreground = Color.FromArgb(185, 28, 28);
            }

            Size textSize = TextRenderer.MeasureText(text, _statusBadgeFont);
            int width = Math.Min(e.CellBounds.Width - 20, textSize.Width + 24);
            int height = 26;
            var badgeBounds = new Rectangle(
                e.CellBounds.X + 10,
                e.CellBounds.Y + (e.CellBounds.Height - height) / 2,
                Math.Max(54, width),
                height);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiShape.CreateRoundedRectangle(badgeBounds, 13))
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillPath(brush, path);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                _statusBadgeFont,
                badgeBounds,
                foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            e.Handled = true;
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Chọn thư mục chứa file JSON status từ các máy SX3 client";
                dialog.ShowNewFolderButton = true;
                dialog.SelectedPath = Directory.Exists(_folderTextBox.Text) ? _folderTextBox.Text : string.Empty;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _folderTextBox.Text = dialog.SelectedPath;
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string[] lines = File.ReadAllLines(_settingsPath);
                    if (lines.Length > 0) _folderTextBox.Text = NormalizeSavedFolder(lines[0]);
                    if (lines.Length > 1) _latestVersionTextBox.Text = lines[1];
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Không đọc được cấu hình cũ: " + ex.Message, StatusKind.Warning);
            }

            _folderTextBox.Text = DefaultStatusFolder;
            _latestVersionTextBox.Text = string.Empty;
        }

        private static string NormalizeSavedFolder(string folder)
        {
            string value = (folder ?? string.Empty).Trim();
            if (string.Equals(value, @"C:\SX3VersionStatus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, @"\\ADMIN-PC\SX3VersionStatus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, @"\\192.168.10.150\SX3VersionStatus", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultStatusFolder;
            }

            return string.IsNullOrWhiteSpace(value) ? DefaultStatusFolder : value;
        }

        private bool SaveSettings()
        {
            try
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(_settingsPath, new[]
                {
                    _folderTextBox.Text.Trim(),
                    _latestVersionTextBox.Text.Trim()
                });
                SetStatus("Đã lưu cấu hình.", StatusKind.Success);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Không lưu được cấu hình: " + ex.Message, StatusKind.Error);
                MessageBox.Show(
                    this,
                    "Không lưu được cấu hình.\r\n\r\n" + ex.Message,
                    "SX3 Version Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        private void OpenStatusFolder()
        {
            string folder = _folderTextBox.Text.Trim();
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(
                    this,
                    "Không mở được thư mục status. Vui lòng kiểm tra lại đường dẫn hoặc kết nối mạng.",
                    "SX3 Version Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Không mở được thư mục.\r\n\r\n" + ex.Message,
                    "SX3 Version Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void CopyStatusPath()
        {
            string folder = _folderTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                SetStatus("Chưa có đường dẫn để sao chép.", StatusKind.Warning);
                return;
            }

            try
            {
                Clipboard.SetText(folder);
                SetStatus("Đã sao chép đường dẫn thư mục status.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatus("Không sao chép được đường dẫn: " + ex.Message, StatusKind.Error);
            }
        }

        private async void LoadReports()
        {
            if (_isLoading)
                return;

            string folder = _folderTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                _reports = new List<ClientReport>();
                _baseStatusText = "Chưa cấu hình thư mục status.";
                _baseStatusKind = StatusKind.Error;
                RenderReports();
                return;
            }

            SetLoading(true);
            try
            {
                LoadResult result = await Task.Run(delegate { return ReadReports(folder); });
                if (IsDisposed || Disposing)
                    return;

                _reports = result.Reports;
                _lastRefreshLabel.Text = "Cập nhật lúc " + DateTime.Now.ToString("HH:mm:ss  dd/MM/yyyy");
                PositionHeaderRightInfo(_lastRefreshLabel.Parent);

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    _baseStatusText = result.ErrorMessage;
                    _baseStatusKind = StatusKind.Error;
                }
                else
                {
                    string details = "Đã đọc " + _reports.Count + " máy từ thư mục status";
                    if (result.InvalidFiles > 0)
                        details += " • bỏ qua " + result.InvalidFiles + " file lỗi";
                    if (result.DuplicateFiles > 0)
                        details += " • gộp " + result.DuplicateFiles + " bản ghi trùng";

                    _baseStatusText = details + ".";
                    _baseStatusKind = result.InvalidFiles > 0 ? StatusKind.Warning : StatusKind.Success;
                }

                RenderReports();
            }
            catch (Exception ex)
            {
                _reports = new List<ClientReport>();
                _baseStatusText = "Không thể đọc dữ liệu: " + ex.Message;
                _baseStatusKind = StatusKind.Error;
                RenderReports();
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                    SetLoading(false);
            }
        }

        private static LoadResult ReadReports(string folder)
        {
            var result = new LoadResult();
            var serializer = new JavaScriptSerializer();
            var reports = new List<ClientReport>();

            try
            {
                if (!Directory.Exists(folder))
                {
                    result.ErrorMessage = "Thư mục status không tồn tại hoặc không thể truy cập: " + folder;
                    return result;
                }

                string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
                foreach (string file in files)
                {
                    try
                    {
                        string json;
                        using (var stream = new FileStream(
                            file,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete))
                        using (var reader = new StreamReader(stream))
                            json = reader.ReadToEnd();

                        ClientReport report = serializer.Deserialize<ClientReport>(json);
                        if (report != null && !string.IsNullOrWhiteSpace(report.MachineName))
                            reports.Add(report);
                        else
                            result.InvalidFiles++;
                    }
                    catch
                    {
                        result.InvalidFiles++;
                    }
                }

                List<ClientReport> deduplicated = reports
                    .GroupBy(
                        x => x.MachineName.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(x => x
                        .OrderByDescending(y => ParseLastSeenOrMin(y.LastSeen))
                        .First())
                    .ToList();

                result.DuplicateFiles = reports.Count - deduplicated.Count;
                result.Reports = deduplicated;
            }
            catch (Exception ex)
            {
                result.Reports = new List<ClientReport>();
                result.ErrorMessage = "Không thể truy cập thư mục status: " + ex.Message;
            }

            return result;
        }

        private void RenderReports()
        {
            if (_grid == null)
                return;

            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                string latestVersion = ResolveLatestVersion(_reports);
                string search = _searchTextBox == null ? string.Empty : _searchTextBox.Text.Trim();
                IEnumerable<ClientReport> visibleReports = _reports;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    visibleReports = visibleReports.Where(x =>
                        Contains(x.MachineName, search) ||
                        Contains(x.WindowsUser, search) ||
                        Contains(x.AppVersion, search) ||
                        Contains(x.UpdateStatus, search));
                }

                List<ClientReport> visible = visibleReports
                    .OrderByDescending(x => GetStatusRank(x, latestVersion))
                    .ThenBy(x => x.MachineName)
                    .ToList();

                int ok = 0;
                int outdated = 0;
                int offline = 0;

                foreach (ClientReport report in visible)
                {
                    ClientState state = GetClientState(report, latestVersion);
                    if (state == ClientState.Ok) ok++;
                    else if (state == ClientState.Offline) offline++;
                    else outdated++;

                    int rowIndex = _grid.Rows.Add(
                        Safe(report.MachineName),
                        Safe(report.AppVersion),
                        Safe(latestVersion),
                        GetStateText(state),
                        FormatLastSeen(report.LastSeen),
                        Safe(report.WindowsUser),
                        Safe(report.UpdateStatus));

                    StyleRow(_grid.Rows[rowIndex], state);
                }

                _totalCard.SetValue(visible.Count);
                _okCard.SetValue(ok);
                _outdatedCard.SetValue(outdated);
                _offlineCard.SetValue(offline);

                _emptyLabel.Visible = visible.Count == 0;
                PositionEmptyLabel(_emptyLabel.Parent);

                if (!string.IsNullOrWhiteSpace(search) && visible.Count == 0)
                {
                    SetStatus("Không có máy nào khớp từ khóa “" + search + "”.", StatusKind.Warning);
                }
                else if (string.IsNullOrWhiteSpace(latestVersion) && visible.Count > 0)
                {
                    SetStatus(_baseStatusText + " Chưa xác định được latest version.", StatusKind.Warning);
                }
                else
                {
                    SetStatus(_baseStatusText, _baseStatusKind);
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }
        }

        private void StyleRow(DataGridViewRow row, ClientState state)
        {
            row.Tag = state;
            if (state == ClientState.Offline)
                row.DefaultCellStyle.ForeColor = Color.FromArgb(120, 53, 15);
            else if (state == ClientState.Outdated)
                row.DefaultCellStyle.ForeColor = Color.FromArgb(127, 29, 29);
            else
                row.DefaultCellStyle.ForeColor = TextMain;

        }

        private string ResolveLatestVersion(IEnumerable<ClientReport> reports)
        {
            string configured = _latestVersionTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            return reports
                .Select(x => string.IsNullOrWhiteSpace(x.LatestKnownVersion) ? x.AppVersion : x.LatestKnownVersion)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderByDescending(ParseVersionOrZero)
                .FirstOrDefault() ?? string.Empty;
        }

        private static ClientState GetClientState(ClientReport report, string latestVersion)
        {
            if (IsOffline(report.LastSeen))
                return ClientState.Offline;

            return IsLatest(report.AppVersion, latestVersion)
                ? ClientState.Ok
                : ClientState.Outdated;
        }

        private static int GetStatusRank(ClientReport report, string latestVersion)
        {
            ClientState state = GetClientState(report, latestVersion);
            if (state == ClientState.Outdated) return 3;
            if (state == ClientState.Offline) return 2;
            return 1;
        }

        private static bool IsOffline(string lastSeen)
        {
            DateTime value = ParseLastSeenOrMin(lastSeen);
            if (value == DateTime.MinValue)
                return true;
            return DateTime.Now - value > OfflineAfter;
        }

        private static DateTime ParseLastSeenOrMin(string lastSeen)
        {
            DateTime value;
            if (DateTime.TryParse(lastSeen, out value))
                return value;
            return DateTime.MinValue;
        }

        private static string FormatLastSeen(string lastSeen)
        {
            DateTime value = ParseLastSeenOrMin(lastSeen);
            if (value == DateTime.MinValue)
                return Safe(lastSeen);
            return value.ToString("HH:mm:ss  dd/MM/yyyy");
        }

        private static bool IsLatest(string appVersion, string latestVersion)
        {
            if (string.IsNullOrWhiteSpace(latestVersion))
                return false;

            return ParseVersionOrZero(appVersion).CompareTo(ParseVersionOrZero(latestVersion)) >= 0;
        }

        private static Version ParseVersionOrZero(string value)
        {
            Version version;
            string normalized = (value ?? string.Empty).Trim().TrimStart('v', 'V');
            if (Version.TryParse(normalized, out version))
                return version;
            return new Version(0, 0, 0, 0);
        }

        private static string GetStateText(ClientState state)
        {
            if (state == ClientState.Ok) return "OK";
            if (state == ClientState.Offline) return "MẤT LIÊN LẠC";
            return "CẦN UPDATE";
        }

        private static bool Contains(string value, string keyword)
        {
            return (value ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                Margin = new Padding(0, 0, 10, 0)
            };
        }

        private static ModernTextBox CreateInputTextBox(string placeholder)
        {
            return new ModernTextBox
            {
                Dock = DockStyle.Fill,
                Height = 40,
                Font = new Font("Segoe UI", 9.5F),
                BorderColor = BorderColor,
                FocusBorderColor = PrimaryBlue,
                PlaceholderText = placeholder,
                Margin = new Padding(0, 1, 8, 1)
            };
        }

        private static ModernButton CreateButton(
            string text,
            Color background,
            Color foreground,
            Color border,
            Color hover)
        {
            return new ModernButton
            {
                Text = text,
                Dock = DockStyle.Fill,
                Height = 40,
                NormalColor = background,
                HoverColor = hover,
                TextColor = foreground,
                BorderColor = border,
                CornerRadius = 9,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Margin = new Padding(4, 0, 0, 0),
                Cursor = Cursors.Hand
            };
        }

        private void SetLoading(bool loading)
        {
            _isLoading = loading;
            if (loading)
            {
                _loadingBar.Start();
                _refreshButton.SetBusy(true);
                SetStatus("Đang đọc dữ liệu từ thư mục status...", StatusKind.Loading);
            }
            else
            {
                _loadingBar.Stop();
                _refreshButton.SetBusy(false);
            }
        }

        private void SetStatus(string text, StatusKind kind)
        {
            if (_statusLabel == null)
                return;

            string prefix = "●  ";
            _statusLabel.Text = prefix + (text ?? string.Empty);
            if (kind == StatusKind.Success)
                _statusLabel.ForeColor = OkColor;
            else if (kind == StatusKind.Warning)
                _statusLabel.ForeColor = WarningColor;
            else if (kind == StatusKind.Error)
                _statusLabel.ForeColor = DangerColor;
            else if (kind == StatusKind.Loading)
                _statusLabel.ForeColor = PrimaryBlue;
            else
                _statusLabel.ForeColor = TextMuted;
        }

        private void PositionEmptyLabel(Control parent)
        {
            if (_emptyLabel == null || parent == null)
                return;

            _emptyLabel.Location = new Point(
                Math.Max(10, (parent.ClientSize.Width - _emptyLabel.Width) / 2),
                Math.Max(60, (parent.ClientSize.Height - _emptyLabel.Height) / 2));
        }

        private void MonitorFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                LoadReports();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                _searchTextBox.FocusInput();
                _searchTextBox.SelectAll();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
    }

    internal sealed class LoadResult
    {
        internal List<ClientReport> Reports { get; set; }
        internal int InvalidFiles { get; set; }
        internal int DuplicateFiles { get; set; }
        internal string ErrorMessage { get; set; }

        internal LoadResult()
        {
            Reports = new List<ClientReport>();
            ErrorMessage = string.Empty;
        }
    }

    internal sealed class ModernTextBox : UserControl
    {
        private const int EmSetCueBanner = 0x1501;
        private readonly TextBox _innerTextBox;
        private Color _borderColor = Color.FromArgb(218, 226, 237);
        private Color _focusBorderColor = Color.FromArgb(37, 99, 235);
        private string _placeholderText = string.Empty;
        private bool _focused;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        internal ModernTextBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            BackColor = Color.White;
            Padding = new Padding(12, 9, 12, 7);
            MinimumSize = new Size(80, 38);
            Height = 40;

            _innerTextBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = Font
            };
            Controls.Add(_innerTextBox);

            _innerTextBox.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
            _innerTextBox.Enter += delegate
            {
                _focused = true;
                Invalidate();
            };
            _innerTextBox.Leave += delegate
            {
                _focused = false;
                Invalidate();
            };
            _innerTextBox.HandleCreated += delegate { ApplyPlaceholder(); };

            Click += delegate { _innerTextBox.Focus(); };
        }

        public override string Text
        {
            get { return _innerTextBox == null ? base.Text : _innerTextBox.Text; }
            set
            {
                if (_innerTextBox == null)
                    base.Text = value;
                else
                    _innerTextBox.Text = value ?? string.Empty;
            }
        }

        internal string PlaceholderText
        {
            get { return _placeholderText; }
            set
            {
                _placeholderText = value ?? string.Empty;
                ApplyPlaceholder();
            }
        }

        internal Color BorderColor
        {
            get { return _borderColor; }
            set
            {
                _borderColor = value;
                Invalidate();
            }
        }

        internal Color FocusBorderColor
        {
            get { return _focusBorderColor; }
            set
            {
                _focusBorderColor = value;
                Invalidate();
            }
        }

        internal bool ReadOnly
        {
            get { return _innerTextBox.ReadOnly; }
            set { _innerTextBox.ReadOnly = value; }
        }

        internal void FocusInput()
        {
            _innerTextBox.Focus();
        }

        internal void SelectAll()
        {
            _innerTextBox.SelectAll();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_innerTextBox != null)
                _innerTextBox.Font = Font;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath path = UiShape.CreateRoundedRectangle(ClientRectangle, 9))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null) oldRegion.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiShape.CreateRoundedRectangle(bounds, 9))
            using (var fill = new SolidBrush(Color.White))
            using (var pen = new Pen(_focused ? _focusBorderColor : _borderColor, _focused ? 1.6F : 1F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void ApplyPlaceholder()
        {
            if (_innerTextBox == null || !_innerTextBox.IsHandleCreated)
                return;

            try
            {
                SendMessage(_innerTextBox.Handle, EmSetCueBanner, new IntPtr(1), _placeholderText);
            }
            catch
            {
                // Placeholder chỉ là phần trang trí, không ảnh hưởng chức năng nhập liệu.
            }
        }
    }

    internal sealed class ModernButton : Button
    {
        private readonly Timer _hoverTimer;
        private float _hoverAmount;
        private bool _mouseInside;
        private bool _pressed;
        private bool _isBusy;
        private string _normalText = string.Empty;

        internal ModernButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            TabStop = true;

            NormalColor = Color.White;
            HoverColor = Color.FromArgb(248, 250, 252);
            TextColor = Color.FromArgb(15, 23, 42);
            BorderColor = Color.FromArgb(218, 226, 237);
            CornerRadius = 9;

            _hoverTimer = new Timer { Interval = 16 };
            _hoverTimer.Tick += delegate
            {
                float target = _mouseInside ? 1F : 0F;
                _hoverAmount += (target - _hoverAmount) * 0.24F;
                if (Math.Abs(target - _hoverAmount) < 0.02F)
                {
                    _hoverAmount = target;
                    _hoverTimer.Stop();
                }
                Invalidate();
            };
        }

        internal Color NormalColor { get; set; }
        internal Color HoverColor { get; set; }
        internal Color TextColor { get; set; }
        internal Color BorderColor { get; set; }
        internal int CornerRadius { get; set; }

        internal void SetBusy(bool busy)
        {
            if (busy && !_isBusy)
                _normalText = Text;

            _isBusy = busy;
            Text = busy ? "Đang tải..." : _normalText;
            Enabled = !busy;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _mouseInside = true;
            _hoverTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseInside = false;
            _pressed = false;
            _hoverTimer.Start();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, _pressed ? 1 : 0, Width - 1, Height - 2);
            Color background = Enabled
                ? UiShape.Blend(NormalColor, HoverColor, _hoverAmount)
                : Color.FromArgb(241, 245, 249);
            Color foreground = Enabled ? TextColor : Color.FromArgb(148, 163, 184);
            Color border = Enabled ? BorderColor : Color.FromArgb(226, 232, 240);

            using (GraphicsPath path = UiShape.CreateRoundedRectangle(bounds, CornerRadius))
            using (var brush = new SolidBrush(background))
            using (var pen = new Pen(border, 1F))
            {
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                pevent.Graphics,
                Text,
                Font,
                bounds,
                foreground,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = Rectangle.Inflate(bounds, -4, -4);
                ControlPaint.DrawFocusRectangle(pevent.Graphics, focusBounds, foreground, background);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _hoverTimer != null)
                _hoverTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal class RoundedPanel : Panel
    {
        internal RoundedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            CornerRadius = 14;
            BorderColor = Color.FromArgb(218, 226, 237);
        }

        internal int CornerRadius { get; set; }
        internal Color BorderColor { get; set; }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width <= 0 || Height <= 0)
                return;

            using (GraphicsPath path = UiShape.CreateRoundedRectangle(ClientRectangle, CornerRadius))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null) oldRegion.Dispose();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiShape.CreateRoundedRectangle(bounds, CornerRadius))
            using (var pen = new Pen(BorderColor, 1F))
                e.Graphics.DrawPath(pen, path);
        }
    }

    internal sealed class GradientHeaderPanel : Panel
    {
        private readonly Timer _animationTimer;
        private float _phase;

        internal GradientHeaderPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);

            StartColor = Color.FromArgb(15, 23, 42);
            EndColor = Color.FromArgb(30, 64, 175);
            CornerRadius = 18;

            _animationTimer = new Timer { Interval = 40 };
            _animationTimer.Tick += delegate
            {
                _phase += 0.018F;
                if (_phase > 1F) _phase = 0F;
                Invalidate();
            };
            _animationTimer.Start();
        }

        internal Color StartColor { get; set; }
        internal Color EndColor { get; set; }
        internal int CornerRadius { get; set; }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width <= 0 || Height <= 0)
                return;

            using (GraphicsPath path = UiShape.CreateRoundedRectangle(ClientRectangle, CornerRadius))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null) oldRegion.Dispose();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            using (var brush = new LinearGradientBrush(bounds, StartColor, EndColor, 12F))
                e.Graphics.FillRectangle(brush, bounds);

            int glowX = (int)(Width * (0.55F + _phase * 0.35F));
            using (var glow = new SolidBrush(Color.FromArgb(22, 147, 197, 253)))
                e.Graphics.FillEllipse(glow, glowX, -80, 260, 220);

            using (var glow2 = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                e.Graphics.FillEllipse(glow2, Width - 190, 28, 170, 150);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _animationTimer != null)
                _animationTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class AnimatedLoadingBar : Control
    {
        private readonly Timer _timer;
        private int _offset;
        private bool _running;

        internal AnimatedLoadingBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            AccentColor = Color.FromArgb(37, 99, 235);
            BackColor = Color.Transparent;
            Visible = false;

            _timer = new Timer { Interval = 22 };
            _timer.Tick += delegate
            {
                _offset += 18;
                if (_offset > Width + 220)
                    _offset = -220;
                Invalidate();
            };
        }

        internal Color AccentColor { get; set; }

        internal void Start()
        {
            _running = true;
            _offset = -220;
            Visible = true;
            _timer.Start();
            Invalidate();
        }

        internal void Stop()
        {
            _running = false;
            _timer.Stop();
            Visible = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_running || Width <= 0 || Height <= 0)
                return;

            using (var baseBrush = new SolidBrush(Color.FromArgb(219, 234, 254)))
                e.Graphics.FillRectangle(baseBrush, ClientRectangle);

            Rectangle moving = new Rectangle(_offset, 0, 220, Height);
            using (var brush = new LinearGradientBrush(
                moving,
                Color.FromArgb(30, AccentColor),
                AccentColor,
                LinearGradientMode.Horizontal))
            {
                var blend = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(0, AccentColor),
                        AccentColor,
                        Color.FromArgb(0, AccentColor)
                    },
                    Positions = new[] { 0F, 0.5F, 1F }
                };
                brush.InterpolationColors = blend;
                e.Graphics.FillRectangle(brush, moving);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class StatCard : Panel
    {
        private readonly Label _valueLabel;
        private readonly Color _accent;
        private readonly Timer _valueTimer;
        private readonly Timer _entranceTimer;
        private int _displayedValue;
        private int _startValue;
        private int _targetValue;
        private float _valueProgress;
        private float _entranceProgress;
        private int _entranceDelay;
        private int _delayElapsed;

        internal StatCard(string title, string subtitle, Color accent, string icon)
        {
            _accent = accent;
            Dock = DockStyle.Fill;
            MinimumSize = new Size(0, 122);
            BackColor = Color.White;
            Padding = new Padding(18, 17, 18, 15);
            DoubleBuffered = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(layout);

            var iconBadge = new IconBadge
            {
                Text = icon,
                AccentColor = accent,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 10)
            };
            layout.SetRowSpan(iconBadge, 3);
            layout.Controls.Add(iconBadge, 0, 0);

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.BottomLeft,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.Controls.Add(titleLabel, 2, 0);

            _valueLabel = new Label
            {
                Text = "0",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 25F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.Controls.Add(_valueLabel, 2, 1);

            var subtitleLabel = new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.7F),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.TopLeft,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 0)
            };
            layout.Controls.Add(subtitleLabel, 2, 2);

            _valueTimer = new Timer { Interval = 16 };
            _valueTimer.Tick += delegate
            {
                _valueProgress += 0.09F;
                float progress = Math.Min(1F, _valueProgress);
                float eased = 1F - (float)Math.Pow(1F - progress, 3D);
                _displayedValue = _startValue + (int)Math.Round((_targetValue - _startValue) * eased);
                _valueLabel.Text = _displayedValue.ToString("0");
                if (progress >= 1F)
                    _valueTimer.Stop();
            };

            _entranceTimer = new Timer { Interval = 16 };
            _entranceTimer.Tick += delegate
            {
                if (_delayElapsed < _entranceDelay)
                {
                    _delayElapsed += _entranceTimer.Interval;
                    return;
                }

                _entranceProgress += 0.08F;
                if (_entranceProgress >= 1F)
                {
                    _entranceProgress = 1F;
                    _entranceTimer.Stop();
                }
                Invalidate(true);
            };
        }

        internal void SetValue(int value)
        {
            _startValue = _displayedValue;
            _targetValue = Math.Max(0, value);
            _valueProgress = 0F;
            _valueTimer.Start();
        }

        internal void StartEntrance(int delayMilliseconds)
        {
            _entranceDelay = Math.Max(0, delayMilliseconds);
            _delayElapsed = 0;
            _entranceProgress = 0F;
            _entranceTimer.Start();
            Invalidate(true);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width <= 0 || Height <= 0)
                return;

            using (GraphicsPath path = UiShape.CreateRoundedRectangle(ClientRectangle, 14))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null) oldRegion.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fillColor = UiShape.Blend(Color.White, _accent, 0.025F);

            using (GraphicsPath path = UiShape.CreateRoundedRectangle(bounds, 14))
            using (var fill = new SolidBrush(fillColor))
            using (var border = new Pen(Color.FromArgb(218, 226, 237), 1F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            using (var accentBrush = new SolidBrush(_accent))
                e.Graphics.FillRectangle(accentBrush, 0, 0, Width, 4);

            if (_entranceProgress < 1F)
            {
                int alpha = (int)(255F * (1F - _entranceProgress));
                using (var overlay = new SolidBrush(Color.FromArgb(alpha, 244, 247, 251)))
                    e.Graphics.FillRectangle(overlay, ClientRectangle);
            }

            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_valueTimer != null) _valueTimer.Dispose();
                if (_entranceTimer != null) _entranceTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class IconBadge : Control
    {
        internal IconBadge()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            AccentColor = Color.FromArgb(37, 99, 235);
            Font = new Font("Segoe UI", 17F, FontStyle.Bold);
            ForeColor = Color.White;
        }

        internal Color AccentColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int diameter = Math.Min(Width, Height) - 2;
            Rectangle circle = new Rectangle(
                (Width - diameter) / 2,
                (Height - diameter) / 2,
                diameter,
                diameter);

            using (var brush = new SolidBrush(AccentColor))
                e.Graphics.FillEllipse(brush, circle);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                circle,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }
    }

    internal static class UiShape
    {
        internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return path;

            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            int a = from.A + (int)((to.A - from.A) * amount);
            int r = from.R + (int)((to.R - from.R) * amount);
            int g = from.G + (int)((to.G - from.G) * amount);
            int b = from.B + (int)((to.B - from.B) * amount);
            return Color.FromArgb(a, r, g, b);
        }
    }

    internal enum ClientState
    {
        Ok,
        Outdated,
        Offline
    }

    internal enum StatusKind
    {
        Neutral,
        Success,
        Warning,
        Error,
        Loading
    }

    internal sealed class ClientReport
    {
        public string MachineName { get; set; }
        public string WindowsUser { get; set; }
        public string AppVersion { get; set; }
        public string LatestKnownVersion { get; set; }
        public bool IsLatestKnownVersion { get; set; }
        public bool UpdateCheckSucceeded { get; set; }
        public string UpdateStatus { get; set; }
        public string LastSeen { get; set; }
        public string AppPath { get; set; }
        public string DatabasePath { get; set; }
    }
}
