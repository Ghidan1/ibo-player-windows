using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace IBOPlayer
{
    public partial class MainForm : Form
    {
        // ---------- VLC ----------
        private LibVLC? _libVLC;
        private MediaPlayer? _playerA;
        private MediaPlayer? _playerB;
        private VideoView? _viewA;
        private VideoView? _viewB;
        private bool _focusB = false;
        private bool _dual = false;
        private bool _fullscreen = false;
        private bool _isLive = false;
        private bool _positionDragging = false;
        private string _nowKey = "";
        private string _nowName = "";
        private string _nowKind = ""; // live | vod | series
        private Channel? _nowChannel;
        private string _resumeKey = "";
        private DateTime _lastResumeSave = DateTime.MinValue;

        private MediaPlayer? ActivePlayer => _focusB && _playerB != null ? _playerB : _playerA;

        // ---------- Data ----------
        private PlaylistData? _data;
        private PlaylistService.XtreamClient? _xtream;
        private string _section = "Live"; // Live | Movies | Series | Guide | Favorites | Settings
        private string _categoryId = "";
        private string _search = "";
        private bool _sessionUnlocked = false;
        private readonly HashSet<string> _logoTried = new HashSet<string>();
        private readonly HashSet<string> _epgTried = new HashSet<string>();
        private readonly HashSet<string> _posterTried = new HashSet<string>();

        // ---------- UI ----------
        private Panel _topBar = new Panel();
        private Panel _sidebar = new Panel();
        private Panel _content = new Panel();
        private Panel _playerBar = new Panel();
        private Panel _statusBar = new Panel();
        private Panel _loginPanel = new Panel();
        private Panel _videoPanel = new Panel();
        private TableLayoutPanel _dualTable = new TableLayoutPanel();
        private Panel _listPanel = new Panel();

        private FlowLayoutPanel _sideCats = new FlowLayoutPanel();
        private ListView _lvChannels = new ListView();
        private ImageList _logoImages = new ImageList();
        private FlowLayoutPanel _posterFlow = new FlowLayoutPanel();
        private Panel _detailsPanel = new Panel();
        private Panel _guidePanel = new Panel();
        private Panel _settingsPanel = new Panel();

        private Label _lblStatus = new Label();
        private Label _lblClock = new Label();
        private Label _lblNowPlaying = new Label();
        private Label _lblTime = new Label();
        private Label _lblLiveBadge = new Label();
        private TrackBar _tbPosition = new TrackBar();
        private TrackBar _tbVolume = new TrackBar();
        private Button _btnPlayPause = new Button();
        private Button _btnStop = new Button();
        private Button _btnFullscreen = new Button();
        private Button _btnAspect = new Button();
        private Button _btnAudio = new Button();
        private Button _btnSubs = new Button();
        private Button _btnDual = new Button();
        private TextBox _txtSearch = new TextBox();
        private Button _btnSearchClear = new Button();
        private System.Windows.Forms.Timer _uiTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer _clockTimer = new System.Windows.Forms.Timer();

        // Login controls
        private Panel _loginBody = new Panel();
        private TextBox _txtM3uUrl = new TextBox();
        private TextBox _txtServer = new TextBox();
        private TextBox _txtUser = new TextBox();
        private TextBox _txtPass = new TextBox();
        private Label _lblLoginStatus = new Label();
        private Label _lblMac = new Label();
        private ListBox _lbSaved = new ListBox();
        private string _loginMethod = "Xtream";

        private static readonly HttpClient ImgHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public MainForm()
        {
            Text = "IBO Player  v1.0";
            Size = new Size(1280, 800);
            MinimumSize = new Size(1024, 640);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Theme.Set(Store.Settings.ThemeName);
            BackColor = Theme.Current.Bg;
            ForeColor = Theme.Current.Text;
            Font = new Font("Segoe UI", 10);

            if (Program.VlcAvailable)
            {
                try
                {
                    var opts = new List<string> { "--no-video-title-show" };
                    opts.Add(Store.Settings.HardwareAccel ? "--avcodec-hw=any" : "--avcodec-hw=none");
                    _libVLC = new LibVLC(opts.ToArray());
                    _playerA = new MediaPlayer(_libVLC);
                    _playerB = new MediaPlayer(_libVLC);
                    _playerA.Volume = Store.Settings.Volume;
                    _playerB.Volume = Store.Settings.Volume;
                    WirePlayerEvents(_playerA);
                    WirePlayerEvents(_playerB);
                }
                catch { _libVLC = null; _playerA = null; _playerB = null; }
            }

            // DOCK ORDER MATTERS (WinForms docks in reverse add order):
            // Fill first, then edges — otherwise the Fill panel covers everything.
            BuildContent();
            BuildTopBar();
            BuildSidebar();
            BuildPlayerBar();
            BuildStatusBar();
            BuildLoginPanel();

            _uiTimer.Interval = 500;
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
            _clockTimer.Interval = 1000;
            _clockTimer.Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("dddd, d MMMM yyyy  •  HH:mm:ss");
            _clockTimer.Start();
            _lblClock.Text = DateTime.Now.ToString("dddd, d MMMM yyyy  •  HH:mm:ss");

            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && _fullscreen) ToggleFullscreen(); };
            FormClosing += (_, _) =>
            {
                SaveResumePosition();
                try { _playerA?.Stop(); _playerB?.Stop(); } catch { }
                try { _playerA?.Dispose(); _playerB?.Dispose(); _libVLC?.Dispose(); } catch { }
            };

            ApplyTheme();
            ShowLogin();
        }

        // ================= LAYOUT =================

        private void BuildContent()
        {
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Current.Bg;
            Controls.Add(_content);

            // List area first (Fill), video panel second (Top) — see dock rule above.
            _listPanel.Dock = DockStyle.Fill;
            _listPanel.BackColor = Theme.Current.Bg;
            _content.Controls.Add(_listPanel);

            _videoPanel.Dock = DockStyle.Top;
            _videoPanel.Height = 360;
            _videoPanel.BackColor = Color.Black;
            _videoPanel.Visible = false;
            _content.Controls.Add(_videoPanel);

            _dualTable.Dock = DockStyle.Fill;
            _dualTable.ColumnCount = 2;
            _dualTable.RowCount = 1;
            _dualTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _dualTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 0));
            _dualTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _videoPanel.Controls.Add(_dualTable);

            if (_playerA != null)
            {
                _viewA = new VideoView { Dock = DockStyle.Fill, MediaPlayer = _playerA, BackColor = Color.Black };
                _viewA.DoubleClick += (_, _) => ToggleFullscreen();
                _viewA.Click += (_, _) => { _focusB = false; UpdateFocusHighlight(); };
                _dualTable.Controls.Add(_viewA, 0, 0);
            }
            if (_playerB != null)
            {
                _viewB = new VideoView { Dock = DockStyle.Fill, MediaPlayer = _playerB, BackColor = Color.Black, Visible = false };
                _viewB.DoubleClick += (_, _) => ToggleFullscreen();
                _viewB.Click += (_, _) => { _focusB = true; UpdateFocusHighlight(); };
                _dualTable.Controls.Add(_viewB, 1, 0);
            }

            BuildChannelList();
            BuildPosterFlow();
            BuildGuidePanel();
            BuildSettingsPanel();
            _detailsPanel.Dock = DockStyle.Fill;
            _detailsPanel.Visible = false;
            _listPanel.Controls.Add(_detailsPanel);
        }

        private void BuildTopBar()
        {
            _topBar.Dock = DockStyle.Top;
            _topBar.Height = 56;
            _topBar.BackColor = Theme.Current.PanelBg;
            Controls.Add(_topBar);

            var logo = new Label
            {
                Text = "IBO Player",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Theme.Current.Accent,
                AutoSize = true,
                Location = new Point(14, 10)
            };
            _topBar.Controls.Add(logo);

            var sections = new[] { "Live TV", "Movies", "Series", "Guide", "Favorites", "Settings" };
            int x = 190;
            foreach (var s in sections)
            {
                var b = new Button
                {
                    Text = s,
                    Tag = s,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Theme.Current.Text,
                    BackColor = Color.Transparent,
                    Size = new Size(92, 36),
                    Location = new Point(x, 10),
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (_, _) => SetSection(SectionName((string)b.Tag));
                _topBar.Controls.Add(b);
                x += 98;
            }

            _txtSearch = new TextBox
            {
                PlaceholderText = "Search…",
                Size = new Size(200, 30),
                Location = new Point(x + 8, 13),
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.TextChanged += (_, _) => { _search = _txtSearch.Text.Trim(); RefreshCurrentList(); };
            _topBar.Controls.Add(_txtSearch);

            _btnSearchClear = new Button
            {
                Text = "✕",
                Size = new Size(30, 30),
                Location = new Point(x + 214, 13),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Current.Muted,
                Cursor = Cursors.Hand
            };
            _btnSearchClear.FlatAppearance.BorderSize = 0;
            _btnSearchClear.Click += (_, _) => { _txtSearch.Text = ""; };
            _topBar.Controls.Add(_btnSearchClear);

            _lblClock = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Current.Muted,
                Location = new Point(0, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _topBar.Controls.Add(_lblClock);
            _topBar.Resize += (_, _) => { _lblClock.Location = new Point(_topBar.Width - _lblClock.Width - 14, 20); };
        }

        private static string SectionName(string label) => label switch
        {
            "Live TV" => "Live",
            "Movies" => "Movies",
            "Series" => "Series",
            "Guide" => "Guide",
            "Favorites" => "Favorites",
            _ => "Settings"
        };

        private void BuildSidebar()
        {
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = 220;
            _sidebar.BackColor = Theme.Current.PanelBg;
            Controls.Add(_sidebar);

            var title = new Label
            {
                Text = "Categories",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Theme.Current.Text,
                AutoSize = true,
                Location = new Point(14, 12)
            };

            _sideCats.Dock = DockStyle.Fill;
            _sideCats.FlowDirection = FlowDirection.TopDown;
            _sideCats.WrapContents = false;
            _sideCats.AutoScroll = true;
            _sideCats.BackColor = Theme.Current.PanelBg;
            _sideCats.Padding = new Padding(8, 44, 8, 8);
            _sidebar.Controls.Add(_sideCats);

            var hint = new Label
            {
                Text = "Right-click a category to lock it with a PIN.",
                ForeColor = Theme.Current.Muted,
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(12, 6, 12, 6)
            };
            _sidebar.Controls.Add(hint);

            // Title added last so it stays on top of the Fill-docked category list.
            _sidebar.Controls.Add(title);
        }

        private void BuildPlayerBar()
        {
            _playerBar.Dock = DockStyle.Bottom;
            _playerBar.Height = 88;
            _playerBar.BackColor = Theme.Current.PanelBg;
            Controls.Add(_playerBar);

            _btnPlayPause = MakeBarButton("⏸", 10, (_, _) => TogglePlayPause());
            _btnStop = MakeBarButton("⏹", 58, (_, _) => StopPlayback());
            _lblNowPlaying = new Label
            {
                Text = "Nothing playing",
                AutoSize = false,
                Size = new Size(260, 24),
                Location = new Point(106, 8),
                ForeColor = Theme.Current.Text,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            _playerBar.Controls.Add(_lblNowPlaying);

            _lblLiveBadge = new Label
            {
                Text = "● LIVE",
                AutoSize = true,
                Location = new Point(106, 34),
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Visible = false
            };
            _playerBar.Controls.Add(_lblLiveBadge);

            _tbPosition = new TrackBar
            {
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                Size = new Size(420, 30),
                Location = new Point(380, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _tbPosition.MouseDown += (_, _) => _positionDragging = true;
            _tbPosition.MouseUp += (_, _) => { _positionDragging = false; SeekToPosition(); };
            _playerBar.Controls.Add(_tbPosition);

            _lblTime = new Label
            {
                Text = "00:00 / 00:00",
                AutoSize = true,
                Location = new Point(380, 48),
                ForeColor = Theme.Current.Muted
            };
            _playerBar.Controls.Add(_lblTime);

            var volLabel = new Label { Text = "🔊", AutoSize = true, Location = new Point(0, 20), ForeColor = Theme.Current.Text };
            _playerBar.Controls.Add(volLabel);
            _tbVolume = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Store.Settings.Volume,
                TickStyle = TickStyle.None,
                Size = new Size(110, 30),
                Location = new Point(0, 14)
            };
            _tbVolume.ValueChanged += (_, _) =>
            {
                Store.Settings.Volume = _tbVolume.Value;
                Store.SaveSettings();
                if (_playerA != null) _playerA.Volume = _tbVolume.Value;
                if (_playerB != null) _playerB.Volume = _tbVolume.Value;
            };
            _playerBar.Controls.Add(_tbVolume);
            _playerBar.Resize += (_, _) =>
            {
                volLabel.Location = new Point(_playerBar.Width - 330, 20);
                _tbVolume.Location = new Point(_playerBar.Width - 305, 14);
            };

            _btnFullscreen = MakeBarButton("⛶", 0, (_, _) => ToggleFullscreen());
            _btnAspect = MakeBarButton("16:9", 0, (_, _) => CycleAspect());
            _btnAudio = MakeBarButton("🎵", 0, (_, _) => ShowAudioMenu());
            _btnSubs = MakeBarButton("CC", 0, (_, _) => ShowSubsMenu());
            _btnDual = MakeBarButton("▦ Dual", 0, (_, _) => ToggleDual());
            _btnDual.Size = new Size(70, 36);
            _playerBar.Controls.Add(_btnFullscreen);
            _playerBar.Controls.Add(_btnAspect);
            _playerBar.Controls.Add(_btnAudio);
            _playerBar.Controls.Add(_btnSubs);
            _playerBar.Controls.Add(_btnDual);
            _playerBar.Resize += (_, _) =>
            {
                int x = _playerBar.Width - 250;
                foreach (var b in new[] { _btnFullscreen, _btnAspect, _btnAudio, _btnSubs })
                {
                    b.Location = new Point(x, 46);
                    x += 46;
                }
                _btnDual.Location = new Point(_playerBar.Width - 330, 52);
            };
        }

        private Button MakeBarButton(string text, int x, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(40, 36),
                Location = new Point(x, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            _playerBar.Controls.Add(b);
            return b;
        }

        private void BuildStatusBar()
        {
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 24;
            _statusBar.BackColor = Theme.Current.PanelBg;
            Controls.Add(_statusBar);

            _lblStatus = new Label
            {
                Text = "Ready",
                AutoSize = true,
                ForeColor = Theme.Current.Muted,
                Location = new Point(10, 4)
            };
            _statusBar.Controls.Add(_lblStatus);
        }

        // ================= THEME =================

        private void ApplyTheme()
        {
            var t = Theme.Current;
            BackColor = t.Bg;
            ForeColor = t.Text;
            _topBar.BackColor = t.PanelBg;
            _sidebar.BackColor = t.PanelBg;
            _content.BackColor = t.Bg;
            _listPanel.BackColor = t.Bg;
            _playerBar.BackColor = t.PanelBg;
            _statusBar.BackColor = t.PanelBg;
            _sideCats.BackColor = t.PanelBg;
            _posterFlow.BackColor = t.Bg;
            _lvChannels.BackColor = t.CardBg;
            _lvChannels.ForeColor = t.Text;
            _lblStatus.ForeColor = t.Muted;
            _lblClock.ForeColor = t.Muted;
            _lblTime.ForeColor = t.Muted;
            _lblNowPlaying.ForeColor = t.Text;
            foreach (Control c in _topBar.Controls)
            {
                if (c is Label l && l.Text == "IBO Player") l.ForeColor = t.Accent;
                if (c is Button b && b.BackColor == Color.Transparent) b.ForeColor = t.Text;
            }
            foreach (Control c in _playerBar.Controls)
            {
                if (c is Button b) { b.BackColor = t.CardBg; b.ForeColor = t.Text; }
            }
            Invalidate(true);
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => _lblStatus.Text = text)); return; }
            _lblStatus.Text = text;
        }

        // ================= SECTIONS =================

        private void SetSection(string section)
        {
            _section = section;
            _categoryId = "";
            _detailsPanel.Visible = false;
            _lvChannels.Visible = section == "Live" || section == "Favorites";
            _posterFlow.Visible = section == "Movies" || section == "Series";
            _guidePanel.Visible = section == "Guide";
            _settingsPanel.Visible = section == "Settings";
            if (section == "Settings") BuildSettingsPanel();
            _settingsPanel.Visible = section == "Settings";
            LoadCategories();
            RefreshCurrentList();
            foreach (Control c in _topBar.Controls)
            {
                if (c is Button b && b.Tag is string tag)
                {
                    bool active = SectionName(tag) == section;
                    b.BackColor = active ? Theme.Current.Accent : Color.Transparent;
                    b.ForeColor = active ? Color.White : Theme.Current.Text;
                }
            }
        }

        private void LoadCategories()
        {
            _sideCats.Controls.Clear();
            if (_data == null) return;
            List<Category> cats = _section switch
            {
                "Live" => _data.LiveCategories,
                "Movies" => _data.VodCategories,
                "Series" => _data.SeriesCategories,
                _ => new List<Category>()
            };
            if (_section == "Live" || _section == "Movies" || _section == "Series")
            {
                AddCategoryButton("★ Favorites", "__fav");
                AddCategoryButton("🕐 Recent", "__recent");
                AddCategoryButton("All", "");
            }
            foreach (var c in cats)
            {
                int count = _section == "Live"
                    ? _data.Channels.Count(ch => ch.CategoryId == c.Id || ch.CategoryName == c.Name)
                    : _section == "Movies"
                        ? _data.Movies.Count(m => m.CategoryId == c.Id || m.CategoryName == c.Name)
                        : _data.Series.Count(s => s.CategoryId == c.Id || s.CategoryName == c.Name);
                AddCategoryButton(c.Name + $"  ({count})", c.Id, c.Name);
            }
            if (_section == "Guide")
            {
                AddCategoryButton("All channels", "");
                foreach (var c in _data.LiveCategories) AddCategoryButton(c.Name, c.Id, c.Name);
            }
        }

        private void AddCategoryButton(string text, string id, string? lockName = null)
        {
            bool locked = lockName != null && Store.IsCategoryLocked(lockName) && !_sessionUnlocked;
            var b = new Button
            {
                Text = (locked ? "🔒 " : "") + text,
                Tag = id,
                Size = new Size(196, 34),
                Margin = new Padding(2),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = _categoryId == id ? Theme.Current.Accent : Color.Transparent,
                ForeColor = _categoryId == id ? Color.White : Theme.Current.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) =>
            {
                if (locked)
                {
                    if (AskPin("This category is locked. Enter PIN:"))
                    {
                        _sessionUnlocked = true;
                        LoadCategories();
                    }
                    else return;
                }
                _categoryId = id;
                LoadCategories();
                RefreshCurrentList();
            };
            if (lockName != null)
            {
                var menu = new ContextMenuStrip();
                bool isLocked = Store.IsCategoryLocked(lockName);
                var item = new ToolStripMenuItem(isLocked ? "Unlock category" : "Lock category (PIN)");
                item.Click += (_, _) =>
                {
                    if (isLocked)
                    {
                        if (AskPin("Enter PIN to unlock:"))
                        {
                            Store.Data.LockedCategories.Remove(lockName);
                            Store.SaveData();
                            LoadCategories();
                        }
                    }
                    else
                    {
                        if (AskPin("Enter PIN to lock this category:"))
                        {
                            Store.Data.LockedCategories.Add(lockName);
                            Store.SaveData();
                            LoadCategories();
                        }
                    }
                };
                menu.Items.Add(item);
                b.ContextMenuStrip = menu;
            }
            _sideCats.Controls.Add(b);
        }

        private bool AskPin(string prompt)
        {
            using var dlg = new Form
            {
                Text = "Parental PIN",
                Size = new Size(320, 180),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Theme.Current.Bg,
                ForeColor = Theme.Current.Text
            };
            var lbl = new Label { Text = prompt, AutoSize = true, Location = new Point(16, 16), ForeColor = Theme.Current.Text };
            var txt = new TextBox { Location = new Point(16, 48), Width = 260, PasswordChar = '•', BackColor = Theme.Current.CardBg, ForeColor = Theme.Current.Text };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(110, 92), Size = new Size(90, 32), BackColor = Theme.Current.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            dlg.Controls.Add(lbl);
            dlg.Controls.Add(txt);
            dlg.Controls.Add(ok);
            dlg.AcceptButton = ok;
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            return txt.Text == Store.Settings.Pin;
        }

        private void RefreshCurrentList()
        {
            if (_data == null) return;
            switch (_section)
            {
                case "Live": RefreshLiveList(); break;
                case "Movies": RefreshVodList(false); break;
                case "Series": RefreshVodList(true); break;
                case "Guide": RefreshGuideChannels(); break;
                case "Favorites": RefreshFavoritesList(); break;
            }
        }

        private bool MatchesSearch(string name)
            => string.IsNullOrEmpty(_search) || name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

        private bool CategoryVisible(string categoryId, string categoryName)
        {
            if (Store.IsCategoryLocked(categoryName) && !_sessionUnlocked) return false;
            if (_categoryId == "__fav" || _categoryId == "__recent") return true;
            if (string.IsNullOrEmpty(_categoryId)) return true;
            return categoryId == _categoryId || categoryName == _categoryId;
        }
    }
}
