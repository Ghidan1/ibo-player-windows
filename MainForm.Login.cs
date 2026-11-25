using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IBOPlayer
{
    public partial class MainForm
    {
        private void BuildLoginPanel()
        {
            _loginPanel.Dock = DockStyle.Fill;
            _loginPanel.BackColor = Theme.Current.Bg;
            _loginPanel.Visible = false;
            Controls.Add(_loginPanel);

            var title = new Label
            {
                Text = "IBO Player",
                Font = new Font("Segoe UI", 32, FontStyle.Bold),
                ForeColor = Theme.Current.Accent,
                AutoSize = true,
                Location = new Point(40, 30)
            };
            _loginPanel.Controls.Add(title);
            var sub = new Label
            {
                Text = "Your IPTV player — load your own playlist to start watching.",
                AutoSize = true,
                ForeColor = Theme.Current.Muted,
                Location = new Point(42, 84)
            };
            _loginPanel.Controls.Add(sub);

            var methods = new[] { "Xtream Codes", "M3U Link", "M3U File", "MAC Portal", "Demo" };
            int x = 40;
            foreach (var m in methods)
            {
                var b = new Button
                {
                    Text = m,
                    Tag = m,
                    Size = new Size(120, 38),
                    Location = new Point(x, 120),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.Current.CardBg,
                    ForeColor = Theme.Current.Text,
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (_, _) => { _loginMethod = (string)b.Tag; ShowLoginMethod(); HighlightLoginButtons(); };
                _loginPanel.Controls.Add(b);
                x += 130;
            }

            _loginBody = new Panel
            {
                Location = new Point(40, 180),
                Size = new Size(620, 300),
                BackColor = Theme.Current.CardBg
            };
            _loginPanel.Controls.Add(_loginBody);

            _lblLoginStatus = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Current.Muted,
                Location = new Point(40, 500)
            };
            _loginPanel.Controls.Add(_lblLoginStatus);

            _lblMac = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Current.Muted,
                Location = new Point(40, 530)
            };
            _loginPanel.Controls.Add(_lblMac);
            _lblMac.Text = "Device ID (MAC): " + Store.Settings.DeviceMac + "   •   No activation needed — free for personal use.";

            var savedLabel = new Label
            {
                Text = "Saved logins",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Theme.Current.Text,
                AutoSize = true,
                Location = new Point(700, 180)
            };
            _loginPanel.Controls.Add(savedLabel);
            _lbSaved = new ListBox
            {
                Location = new Point(700, 210),
                Size = new Size(360, 240),
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lbSaved.DoubleClick += async (_, _) => await ConnectSavedAsync();
            _loginPanel.Controls.Add(_lbSaved);

            RefreshSavedList();
            _loginMethod = "Xtream";
            ShowLoginMethod();
            HighlightLoginButtons();
        }

        private void HighlightLoginButtons()
        {
            foreach (Control c in _loginPanel.Controls)
            {
                if (c is Button b && b.Tag is string tag)
                {
                    bool active = tag == _loginMethod;
                    b.BackColor = active ? Theme.Current.Accent : Theme.Current.CardBg;
                    b.ForeColor = active ? Color.White : Theme.Current.Text;
                }
            }
        }

        private void RefreshSavedList()
        {
            _lbSaved.Items.Clear();
            foreach (var l in Store.Data.Logins)
                _lbSaved.Items.Add(l.Name + "  (" + l.Kind + ")");
        }

        private void ShowLogin()
        {
            _loginPanel.Visible = true;
            _loginPanel.BringToFront();
        }

        private void HideLogin()
        {
            _loginPanel.Visible = false;
        }

        private Label LoginLabel(string text, int y)
            => new Label { Text = text, AutoSize = true, Location = new Point(20, y), ForeColor = Theme.Current.Text };

        private TextBox LoginTextBox(int y, int width = 420, bool password = false)
        {
            var t = new TextBox
            {
                Location = new Point(20, y),
                Width = width,
                BackColor = Theme.Current.Bg,
                ForeColor = Theme.Current.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            if (password) t.PasswordChar = '•';
            return t;
        }

        private Button LoginButton(string text, int y, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(20, y),
                Size = new Size(200, 40),
                BackColor = Theme.Current.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            return b;
        }

        private void ShowLoginMethod()
        {
            _loginBody.Controls.Clear();
            _lblLoginStatus.Text = "";
            switch (_loginMethod)
            {
                case "Xtream":
                    _loginBody.Controls.Add(LoginLabel("Server URL (e.g. http://example.com:8080)", 20));
                    _txtServer = LoginTextBox(46);
                    _loginBody.Controls.Add(_txtServer);
                    _loginBody.Controls.Add(LoginLabel("Username", 84));
                    _txtUser = LoginTextBox(110);
                    _loginBody.Controls.Add(_txtUser);
                    _loginBody.Controls.Add(LoginLabel("Password", 148));
                    _txtPass = LoginTextBox(174, 420, true);
                    _loginBody.Controls.Add(_txtPass);
                    _loginBody.Controls.Add(LoginButton("Connect", 220, async (_, _) => await ConnectXtreamAsync()));
                    break;
                case "M3U Link":
                    _loginBody.Controls.Add(LoginLabel("M3U playlist URL", 20));
                    _txtM3uUrl = LoginTextBox(46, 560);
                    _loginBody.Controls.Add(_txtM3uUrl);
                    _loginBody.Controls.Add(LoginLabel("Optional: paste an XMLTV EPG URL in Settings after login.", 84));
                    _loginBody.Controls.Add(LoginButton("Load Playlist", 120, async (_, _) => await ConnectM3uUrlAsync()));
                    break;
                case "M3U File":
                    _loginBody.Controls.Add(LoginLabel("Choose a .m3u / .m3u8 playlist file from your PC.", 20));
                    _loginBody.Controls.Add(LoginButton("Browse & Load", 60, async (_, _) => await ConnectM3uFileAsync()));
                    break;
                case "MAC Portal":
                    _loginBody.Controls.Add(LoginLabel("Your device ID (MAC) — give this to your provider to activate your portal:", 20));
                    var mac = new Label
                    {
                        Text = Store.Settings.DeviceMac,
                        Font = new Font("Consolas", 18, FontStyle.Bold),
                        ForeColor = Theme.Current.Accent,
                        AutoSize = true,
                        Location = new Point(20, 56)
                    };
                    _loginBody.Controls.Add(mac);
                    _loginBody.Controls.Add(LoginLabel("No paid activation is required in this player.", 100));
                    _loginBody.Controls.Add(LoginButton("Continue with Demo Channels", 140, (_, _) => { FinishLogin(PlaylistService.DemoPlaylist()); }));
                    break;
                default: // Demo
                    _loginBody.Controls.Add(LoginLabel("Try the player instantly with 5 free public channels.", 20));
                    _loginBody.Controls.Add(LoginLabel("Al Arabiya • Al Jazeera • Red Bull TV • DW • Mux Test", 48));
                    _loginBody.Controls.Add(LoginButton("Watch Demo Channels", 90, (_, _) => { FinishLogin(PlaylistService.DemoPlaylist()); }));
                    break;
            }
        }

        private async Task ConnectSavedAsync()
        {
            int i = _lbSaved.SelectedIndex;
            if (i < 0 || i >= Store.Data.Logins.Count) return;
            var login = Store.Data.Logins[i];
            _lblLoginStatus.Text = "Connecting to " + login.Name + "…";
            if (login.Kind == PlaylistKind.Xtream)
                await ConnectXtreamAsync(login);
            else if (login.Kind == PlaylistKind.M3uUrl)
            {
                _txtM3uUrl.Text = login.M3uUrl;
                await ConnectM3uUrlAsync(login);
            }
            else if (login.Kind == PlaylistKind.Demo)
                FinishLogin(PlaylistService.DemoPlaylist());
        }

        private async Task ConnectXtreamAsync(LoginProfile? saved = null)
        {
            var server = saved?.Server ?? _txtServer.Text.Trim();
            var user = saved?.Username ?? _txtUser.Text.Trim();
            var pass = saved?.Password ?? _txtPass.Text;
            if (server.Length == 0 || user.Length == 0)
            {
                _lblLoginStatus.Text = "Please enter server URL and username.";
                return;
            }
            _lblLoginStatus.Text = "Checking account…";
            var client = new PlaylistService.XtreamClient(server, user, pass);
            bool ok = await client.AuthenticateAsync();
            if (!ok)
            {
                _lblLoginStatus.Text = "Login failed — check server, username and password.";
                return;
            }
            _lblLoginStatus.Text = "Loading channels…";
            var data = new PlaylistData
            {
                Login = saved ?? new LoginProfile
                {
                    Name = user + " @ " + server,
                    Kind = PlaylistKind.Xtream,
                    Server = server, Username = user, Password = pass
                }
            };
            _xtream = client;
            try
            {
                data.LiveCategories = await client.GetLiveCategoriesAsync();
                data.Channels = await client.GetLiveStreamsAsync();
                foreach (var ch in data.Channels)
                {
                    var cat = data.LiveCategories.Find(c => c.Id == ch.CategoryId);
                    if (cat != null) ch.CategoryName = cat.Name;
                }
                _lblLoginStatus.Text = "Loading movies…";
                data.VodCategories = await client.GetVodCategoriesAsync();
                data.Movies = await client.GetVodStreamsAsync();
                foreach (var m in data.Movies)
                {
                    var cat = data.VodCategories.Find(c => c.Id == m.CategoryId);
                    if (cat != null) m.CategoryName = cat.Name;
                }
                _lblLoginStatus.Text = "Loading series…";
                data.SeriesCategories = await client.GetSeriesCategoriesAsync();
                data.Series = await client.GetSeriesAsync();
                foreach (var s in data.Series)
                {
                    var cat = data.SeriesCategories.Find(c => c.Id == s.CategoryId);
                    if (cat != null) s.CategoryName = cat.Name;
                }
            }
            catch (Exception ex)
            {
                _lblLoginStatus.Text = "Error loading data: " + ex.Message;
                return;
            }
            SaveLoginProfile(data.Login);
            FinishLogin(data);
        }

        private async Task ConnectM3uUrlAsync(LoginProfile? saved = null)
        {
            var url = saved?.M3uUrl ?? _txtM3uUrl.Text.Trim();
            if (url.Length == 0)
            {
                _lblLoginStatus.Text = "Please paste an M3U URL.";
                return;
            }
            _lblLoginStatus.Text = "Downloading playlist…";
            var text = await PlaylistService.DownloadTextAsync(url);
            if (text == null || text.Length < 10)
            {
                _lblLoginStatus.Text = "Could not download the playlist. Check the URL.";
                return;
            }
            var login = saved ?? new LoginProfile { Name = url, Kind = PlaylistKind.M3uUrl, M3uUrl = url };
            var data = PlaylistService.ParseM3u(text, login);
            if (data.Channels.Count == 0)
            {
                _lblLoginStatus.Text = "No channels found in this playlist.";
                return;
            }
            SaveLoginProfile(login);
            FinishLogin(data);
            _ = LoadXmlTvInBackground();
        }

        private async Task ConnectM3uFileAsync()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "M3U playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*",
                Title = "Choose an M3U playlist"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _lblLoginStatus.Text = "Reading file…";
            string text;
            try { text = File.ReadAllText(dlg.FileName); }
            catch (Exception ex) { _lblLoginStatus.Text = "Could not read file: " + ex.Message; return; }
            var login = new LoginProfile { Name = Path.GetFileName(dlg.FileName), Kind = PlaylistKind.M3uFile, M3uPath = dlg.FileName };
            var data = PlaylistService.ParseM3u(text, login);
            if (data.Channels.Count == 0)
            {
                _lblLoginStatus.Text = "No channels found in this file.";
                return;
            }
            SaveLoginProfile(login);
            FinishLogin(data);
            _ = LoadXmlTvInBackground();
        }

        private async Task LoadXmlTvInBackground()
        {
            var url = Store.Settings.EpgUrl;
            if (string.IsNullOrEmpty(url) || _data == null) return;
            var epg = await PlaylistService.FetchXmlTvAsync(url);
            if (_data != null)
            {
                _data.EpgByChannel = epg;
                if (_section == "Live") RefreshLiveList();
            }
        }

        private void SaveLoginProfile(LoginProfile login)
        {
            var existing = Store.Data.Logins.Find(l =>
                l.Kind == login.Kind &&
                ((login.Kind == PlaylistKind.Xtream && l.Server == login.Server && l.Username == login.Username) ||
                 (login.Kind == PlaylistKind.M3uUrl && l.M3uUrl == login.M3uUrl) ||
                 (login.Kind == PlaylistKind.M3uFile && l.M3uPath == login.M3uPath)));
            if (existing == null)
            {
                Store.Data.Logins.Add(login);
                Store.SaveData();
                RefreshSavedList();
            }
            Store.Settings.LastLoginId = login.Id;
            Store.SaveSettings();
        }

        private void FinishLogin(PlaylistData data)
        {
            _data = data;
            _xtream = data.IsXtream
                ? new PlaylistService.XtreamClient(data.Login.Server, data.Login.Username, data.Login.Password)
                : null;
            HideLogin();
            SetStatus("Logged in: " + data.Login.Name + " — " + data.Channels.Count + " channels");
            SetSection("Live");
        }
    }
}
