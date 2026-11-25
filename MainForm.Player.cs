using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LibVLCSharp.Shared;

namespace IBOPlayer
{
    public partial class MainForm
    {
        private readonly string[] _aspects = { "16:9", "4:3", "1:1", "21:9", "Default" };
        private int _aspectIndex = 0;
        private float _pendingResume = -1;

        // ================= PLAYER EVENTS =================

        private void WirePlayerEvents(MediaPlayer mp)
        {
            mp.EndReached += (_, _) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (_nowKind != "live" && _resumeKey.Length > 0)
                    {
                        Store.Data.Resume.Remove(_resumeKey);
                        Store.SaveData();
                    }
                    SetStatus("Playback finished: " + _nowName);
                }));
            };
            mp.EncounteredError += (_, _) =>
            {
                BeginInvoke(new Action(() => SetStatus("Stream error — the channel may be offline. Try another one.")));
            };
            mp.Playing += (_, _) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (_pendingResume > 0 && ActivePlayer != null)
                    {
                        try { ActivePlayer.Position = _pendingResume; } catch { }
                        _pendingResume = -1;
                    }
                    _btnPlayPause.Text = "⏸";
                }));
            };
            mp.Paused += (_, _) => BeginInvoke(new Action(() => { _btnPlayPause.Text = "▶"; }));
            mp.Stopped += (_, _) => BeginInvoke(new Action(() => { _btnPlayPause.Text = "▶"; }));
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            var mp = ActivePlayer;
            if (mp == null) return;
            try
            {
                if (mp.Length > 0 && !_positionDragging)
                {
                    _tbPosition.Value = Math.Max(0, Math.Min(1000, (int)(mp.Position * 1000)));
                    _lblTime.Text = FormatTime(mp.Time) + " / " + FormatTime(mp.Length);
                }
                else if (_isLive)
                {
                    _lblTime.Text = "● LIVE";
                }
                _btnPlayPause.Text = mp.IsPlaying ? "⏸" : "▶";
                if (!_isLive && mp.IsPlaying && (DateTime.Now - _lastResumeSave).TotalSeconds > 5)
                {
                    _lastResumeSave = DateTime.Now;
                    SaveResumePosition();
                }
            }
            catch { }
        }

        private static string FormatTime(long ms)
        {
            if (ms < 0) ms = 0;
            var t = TimeSpan.FromMilliseconds(ms);
            return t.Hours > 0 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        // ================= PLAYBACK =================

        private Media? CreateMedia(string url)
        {
            if (_libVLC == null) return null;
            try
            {
                var media = new Media(_libVLC, url, FromType.FromLocation);
                media.AddOption(":network-caching=" + Store.Settings.NetworkCachingMs);
                return media;
            }
            catch { return null; }
        }

        private MediaPlayer? PickPlayer(bool secondScreen)
        {
            if (_playerA == null) return null;
            if (secondScreen)
            {
                if (!_dual) ToggleDual();
                _focusB = true;
                return _playerB;
            }
            if (_dual && _playerA.IsPlaying && !_focusB)
            {
                _focusB = true;
                return _playerB;
            }
            _focusB = false;
            return _playerA;
        }

        private void PlayChannel(Channel ch, bool secondScreen = false)
        {
            if (!Program.VlcAvailable || _libVLC == null)
            {
                MessageBox.Show(this, "Video engine (VLC) failed to start on this PC.", "Playback unavailable",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var mp = PickPlayer(secondScreen);
            if (mp == null) return;
            _isLive = true;
            _resumeKey = "";
            _nowChannel = ch;
            Store.AddRecent("live", ch.Id, ch.Name);
            PlayOn(mp, ch.Url, true, "live", ch.Id, ch.Name);
        }

        private void PlayVod(VodItem m)
        {
            if (!Program.VlcAvailable || _libVLC == null)
            {
                MessageBox.Show(this, "Video engine (VLC) failed to start on this PC.", "Playback unavailable",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var mp = PickPlayer(false);
            if (mp == null) return;
            var key = "vod:" + m.Id;
            if (TryResume(key, m.Name)) return;
            _isLive = false;
            _resumeKey = key;
            Store.AddRecent("vod", m.Id, m.Name);
            PlayOn(mp, m.Url, false, "vod", m.Id, m.Name);
        }

        private void PlayEpisode(SerieItem s, EpisodeItem ep)
        {
            if (!Program.VlcAvailable || _libVLC == null)
            {
                MessageBox.Show(this, "Video engine (VLC) failed to start on this PC.", "Playback unavailable",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var mp = PickPlayer(false);
            if (mp == null) return;
            var key = "series:" + ep.Id;
            if (TryResume(key, s.Name + " E" + ep.EpisodeNum)) return;
            _isLive = false;
            _resumeKey = key;
            Store.AddRecent("series", s.Id, s.Name);
            PlayOn(mp, ep.Url, false, "series", ep.Id, s.Name + " — Episode " + ep.EpisodeNum);
        }

        private bool TryResume(string key, string displayName)
        {
            if (!Store.Data.Resume.TryGetValue(key, out var pos)) return false;
            if (pos < 0.05f || pos > 0.95f)
            {
                Store.Data.Resume.Remove(key);
                Store.SaveData();
                return false;
            }
            var pct = (int)(pos * 100);
            var r = MessageBox.Show(this,
                $"Resume \"{displayName}\" from {pct}%?",
                "Resume playback", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
            {
                _pendingResume = pos;
                return false;
            }
            Store.Data.Resume.Remove(key);
            Store.SaveData();
            return false;
        }

        private void PlayStreamUrl(string url, bool isLive, string kind, string key, string name, Channel? channel)
        {
            if (!Program.VlcAvailable || _libVLC == null) return;
            var mp = PickPlayer(false);
            if (mp == null) return;
            _isLive = isLive;
            _resumeKey = "";
            _nowChannel = channel;
            PlayOn(mp, url, isLive, kind, key, name);
        }

        private void PlayOn(MediaPlayer mp, string url, bool isLive, string kind, string key, string name)
        {
            var media = CreateMedia(url);
            if (media == null)
            {
                SetStatus("Could not open stream.");
                return;
            }
            try { mp.Stop(); } catch { }
            _videoPanel.Visible = true;
            _nowKey = key;
            _nowName = name;
            _nowKind = kind;
            _isLive = isLive;
            _lblNowPlaying.Text = name;
            _lblLiveBadge.Visible = isLive;
            _tbPosition.Value = 0;
            _lblTime.Text = isLive ? "● LIVE" : "00:00 / 00:00";
            SetStatus("Playing: " + name);
            try { mp.Play(media); } catch (Exception ex) { SetStatus("Playback error: " + ex.Message); }
        }

        private void SaveResumePosition()
        {
            if (_isLive || _resumeKey.Length == 0) return;
            var mp = ActivePlayer;
            if (mp == null || mp.Length <= 0) return;
            try
            {
                float pos = mp.Position;
                if (pos > 0.02f && pos < 0.97f)
                {
                    Store.Data.Resume[_resumeKey] = pos;
                    Store.SaveData();
                }
                else if (pos >= 0.97f)
                {
                    Store.Data.Resume.Remove(_resumeKey);
                    Store.SaveData();
                }
            }
            catch { }
        }

        // ================= CONTROLS =================

        private void TogglePlayPause()
        {
            var mp = ActivePlayer;
            if (mp == null || _nowKey.Length == 0) return;
            try
            {
                if (mp.IsPlaying) mp.Pause();
                else mp.Play();
            }
            catch { }
        }

        private void StopPlayback()
        {
            SaveResumePosition();
            try { _playerA?.Stop(); } catch { }
            try { _playerB?.Stop(); } catch { }
            _nowKey = "";
            _nowName = "";
            _lblNowPlaying.Text = "Nothing playing";
            _lblLiveBadge.Visible = false;
            _lblTime.Text = "00:00 / 00:00";
            _tbPosition.Value = 0;
            _btnPlayPause.Text = "▶";
            SetStatus("Stopped");
        }

        private void SeekToPosition()
        {
            var mp = ActivePlayer;
            if (mp == null || mp.Length <= 0 || _isLive) return;
            try { mp.Position = _tbPosition.Value / 1000f; } catch { }
        }

        private void ToggleFullscreen()
        {
            if (_libVLC == null) return;
            _fullscreen = !_fullscreen;
            if (_fullscreen)
            {
                _topBar.Visible = false;
                _sidebar.Visible = false;
                _playerBar.Visible = false;
                _statusBar.Visible = false;
                _listPanel.Visible = false;
                _videoPanel.Visible = true;
                _videoPanel.Dock = DockStyle.Fill;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                _videoPanel.Dock = DockStyle.Top;
                _videoPanel.Height = 360;
                _topBar.Visible = true;
                _sidebar.Visible = true;
                _playerBar.Visible = true;
                _statusBar.Visible = true;
                _listPanel.Visible = true;
            }
        }

        private void CycleAspect()
        {
            var mp = ActivePlayer;
            if (mp == null) return;
            _aspectIndex = (_aspectIndex + 1) % _aspects.Length;
            var a = _aspects[_aspectIndex];
            try { mp.AspectRatio = a == "Default" ? null : a; } catch { }
            _btnAspect.Text = a == "Default" ? "Auto" : a;
            SetStatus("Aspect ratio: " + a);
        }

        private void ShowAudioMenu()
        {
            var mp = ActivePlayer;
            if (mp == null) return;
            var menu = new ContextMenuStrip();
            try
            {
                var tracks = mp.AudioTrackDescription;
                if (tracks == null || tracks.Length == 0)
                {
                    menu.Items.Add(new ToolStripMenuItem("(no audio tracks)") { Enabled = false });
                }
                else
                {
                    foreach (var t in tracks)
                    {
                        var item = new ToolStripMenuItem(t.Name ?? ("Track " + t.Id));
                        int id = t.Id;
                        item.Click += (_, _) => { try { mp.AudioTrack = id; } catch { } };
                        menu.Items.Add(item);
                    }
                }
            }
            catch { }
            menu.Show(_btnAudio, new Point(0, -menu.Height - 4));
        }

        private void ShowSubsMenu()
        {
            var mp = ActivePlayer;
            if (mp == null) return;
            var menu = new ContextMenuStrip();
            var none = new ToolStripMenuItem("No subtitles");
            none.Click += (_, _) => { try { mp.Spu = -1; } catch { } };
            menu.Items.Add(none);
            try
            {
                var tracks = mp.SpuDescription;
                if (tracks != null)
                {
                    foreach (var t in tracks)
                    {
                        var item = new ToolStripMenuItem(t.Name ?? ("Subtitle " + t.Id));
                        int id = t.Id;
                        item.Click += (_, _) => { try { mp.Spu = id; } catch { } };
                        menu.Items.Add(item);
                    }
                }
            }
            catch { }
            menu.Show(_btnSubs, new Point(0, -menu.Height - 4));
        }

        private void ToggleDual()
        {
            _dual = !_dual;
            Store.Settings.DualScreen = _dual;
            Store.SaveSettings();
            _dualTable.ColumnStyles[0].Width = _dual ? 50 : 100;
            _dualTable.ColumnStyles[1].Width = _dual ? 50 : 0;
            if (_viewB != null) _viewB.Visible = _dual;
            _btnDual.Text = _dual ? "▦ Dual ●" : "▦ Dual";
            SetStatus(_dual
                ? "Dual screen on — use 'Play in second screen' from a channel's right-click menu."
                : "Dual screen off.");
        }

        private void UpdateFocusHighlight()
        {
            SetStatus(_focusB ? "Focused: second screen" : "Focused: main screen");
        }

        // ================= SETTINGS PANEL =================

        private bool _settingsBuilt = false;

        private void BuildSettingsPanel()
        {
            _settingsPanel.Controls.Clear();
            _settingsPanel.Dock = DockStyle.Fill;
            _settingsPanel.BackColor = Theme.Current.Bg;
            _settingsPanel.Visible = false;
            if (!_listPanel.Controls.Contains(_settingsPanel))
                _listPanel.Controls.Add(_settingsPanel);

            int y = 12;
            var title = new Label
            {
                Text = "Settings",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Theme.Current.Text,
                AutoSize = true,
                Location = new Point(12, y)
            };
            _settingsPanel.Controls.Add(title);
            y += 44;

            // Themes
            var lblTheme = new Label { Text = "Theme", AutoSize = true, Location = new Point(12, y), ForeColor = Theme.Current.Text, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            _settingsPanel.Controls.Add(lblTheme);
            y += 26;
            int tx = 12;
            foreach (var th in Theme.All)
            {
                var b = new Button
                {
                    Text = th.Name,
                    Size = new Size(92, 34),
                    Location = new Point(tx, y),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = th.Name == Theme.Current.Name ? th.Accent : Theme.Current.CardBg,
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                string name = th.Name;
                b.Click += (_, _) =>
                {
                    Theme.Set(name);
                    Store.Settings.ThemeName = name;
                    Store.SaveSettings();
                    ApplyTheme();
                    BuildSettingsPanel();
                    LoadCategories();
                    RefreshCurrentList();
                    SetStatus("Theme: " + name);
                };
                _settingsPanel.Controls.Add(b);
                tx += 100;
            }
            y += 52;

            // EPG URL
            var lblEpg = new Label { Text = "EPG / XMLTV URL (for M3U playlists)", AutoSize = true, Location = new Point(12, y), ForeColor = Theme.Current.Text, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            _settingsPanel.Controls.Add(lblEpg);
            y += 26;
            var txtEpg = new TextBox
            {
                Text = Store.Settings.EpgUrl,
                Location = new Point(12, y),
                Width = 520,
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _settingsPanel.Controls.Add(txtEpg);
            var btnEpg = new Button
            {
                Text = "Save & Reload EPG",
                Location = new Point(544, y - 2),
                Size = new Size(160, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Current.Accent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnEpg.FlatAppearance.BorderSize = 0;
            btnEpg.Click += async (_, _) =>
            {
                Store.Settings.EpgUrl = txtEpg.Text.Trim();
                Store.SaveSettings();
                SetStatus("Loading EPG…");
                if (_data != null)
                {
                    _data.EpgByChannel = await PlaylistService.FetchXmlTvAsync(Store.Settings.EpgUrl);
                    RefreshCurrentList();
                    SetStatus("EPG loaded: " + _data.EpgByChannel.Count + " channels with guide data.");
                }
            };
            _settingsPanel.Controls.Add(btnEpg);
            y += 48;

            // Buffer
            var lblBuf = new Label { Text = "Buffer (network caching, ms)", AutoSize = true, Location = new Point(12, y), ForeColor = Theme.Current.Text, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            _settingsPanel.Controls.Add(lblBuf);
            y += 26;
            var numBuf = new NumericUpDown
            {
                Minimum = 200,
                Maximum = 10000,
                Increment = 100,
                Value = Store.Settings.NetworkCachingMs,
                Location = new Point(12, y),
                Width = 140,
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text
            };
            numBuf.ValueChanged += (_, _) =>
            {
                Store.Settings.NetworkCachingMs = (int)numBuf.Value;
                Store.SaveSettings();
            };
            _settingsPanel.Controls.Add(numBuf);
            var lblBufHint = new Label { Text = "Higher = fewer stalls on slow networks. Applies to the next stream.", AutoSize = true, Location = new Point(164, y + 4), ForeColor = Theme.Current.Muted };
            _settingsPanel.Controls.Add(lblBufHint);
            y += 44;

            // Hardware accel
            var chkHw = new CheckBox
            {
                Text = "Hardware acceleration (applies after restart)",
                Checked = Store.Settings.HardwareAccel,
                AutoSize = true,
                Location = new Point(12, y),
                ForeColor = Theme.Current.Text
            };
            chkHw.CheckedChanged += (_, _) =>
            {
                Store.Settings.HardwareAccel = chkHw.Checked;
                Store.SaveSettings();
            };
            _settingsPanel.Controls.Add(chkHw);
            y += 40;

            // PIN
            var lblPin = new Label { Text = "Parental PIN", AutoSize = true, Location = new Point(12, y), ForeColor = Theme.Current.Text, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            _settingsPanel.Controls.Add(lblPin);
            y += 26;
            var txtOldPin = new TextBox { Location = new Point(12, y), Width = 120, PasswordChar = '•', BackColor = Theme.Current.CardBg, ForeColor = Theme.Current.Text, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Current PIN" };
            var txtNewPin = new TextBox { Location = new Point(144, y), Width = 120, PasswordChar = '•', BackColor = Theme.Current.CardBg, ForeColor = Theme.Current.Text, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "New PIN" };
            _settingsPanel.Controls.Add(txtOldPin);
            _settingsPanel.Controls.Add(txtNewPin);
            var btnPin = new Button
            {
                Text = "Change PIN",
                Location = new Point(276, y - 2),
                Size = new Size(110, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                Cursor = Cursors.Hand
            };
            btnPin.FlatAppearance.BorderSize = 0;
            btnPin.Click += (_, _) =>
            {
                if (txtOldPin.Text != Store.Settings.Pin)
                {
                    MessageBox.Show(this, "Current PIN is wrong.", "PIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (txtNewPin.Text.Length < 4)
                {
                    MessageBox.Show(this, "New PIN must be at least 4 digits.", "PIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Store.Settings.Pin = txtNewPin.Text;
                Store.SaveSettings();
                MessageBox.Show(this, "PIN changed.", "PIN", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _settingsPanel.Controls.Add(btnPin);
            y += 48;

            // Logout
            var btnLogout = new Button
            {
                Text = "Logout / Switch playlist",
                Location = new Point(12, y),
                Size = new Size(220, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Current.Accent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.Click += (_, _) =>
            {
                StopPlayback();
                _data = null;
                _xtream = null;
                _sessionUnlocked = false;
                _lvChannels.Items.Clear();
                _posterFlow.Controls.Clear();
                RefreshSavedList();
                ShowLogin();
                SetStatus("Logged out.");
            };
            _settingsPanel.Controls.Add(btnLogout);
            y += 56;

            var lblInfo = new Label
            {
                Text = "IBO Player v1.0  •  Device ID (MAC): " + Store.Settings.DeviceMac + "\nData folder: " + Store.AppDir,
                AutoSize = true,
                Location = new Point(12, y),
                ForeColor = Theme.Current.Muted
            };
            _settingsPanel.Controls.Add(lblInfo);
            _settingsBuilt = true;
        }
    }
}
