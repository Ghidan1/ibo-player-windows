using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IBOPlayer
{
    public partial class MainForm
    {
        private void BuildChannelList()
        {
            _lvChannels.Dock = DockStyle.Fill;
            _lvChannels.View = View.Details;
            _lvChannels.FullRowSelect = true;
            _lvChannels.HideSelection = false;
            _lvChannels.MultiSelect = false;
            _lvChannels.BackColor = Theme.Current.CardBg;
            _lvChannels.ForeColor = Theme.Current.Text;
            _lvChannels.Columns.Add("Channel", 300);
            _lvChannels.Columns.Add("Now", 320);
            _lvChannels.Columns.Add("Next", 320);
            _logoImages.ImageSize = new Size(48, 27);
            _logoImages.ColorDepth = ColorDepth.Depth32Bit;
            _lvChannels.SmallImageList = _logoImages;
            _lvChannels.DoubleClick += (_, _) => PlaySelectedChannel();
            _lvChannels.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) PlaySelectedChannel();
            };
            var ctx = new ContextMenuStrip();
            var miPlay = new ToolStripMenuItem("Play");
            miPlay.Click += (_, _) => PlaySelectedChannel();
            var miSecond = new ToolStripMenuItem("Play in second screen");
            miSecond.Click += (_, _) => PlaySelectedChannel(secondScreen: true);
            var miFav = new ToolStripMenuItem("Toggle favorite ★");
            miFav.Click += (_, _) => ToggleSelectedFavorite();
            var miInfo = new ToolStripMenuItem("Programme info (EPG)");
            miInfo.Click += (_, _) => ShowSelectedEpg();
            ctx.Items.Add(miPlay);
            ctx.Items.Add(miSecond);
            ctx.Items.Add(miFav);
            ctx.Items.Add(miInfo);
            _lvChannels.ContextMenuStrip = ctx;
            _listPanel.Controls.Add(_lvChannels);
        }

        private List<Channel> CurrentChannels()
        {
            var all = _data?.Channels ?? new List<Channel>();
            var list = new List<Channel>();
            foreach (var ch in all)
            {
                if (!MatchesSearch(ch.Name)) continue;
                if (_categoryId == "__fav")
                {
                    if (!Store.IsFavorite(Store.FavKey("live", ch.Id))) continue;
                }
                else if (_categoryId == "__recent")
                {
                    if (!Store.Data.Recents.Any(r => r.Kind == "live" && r.Id == ch.Id)) continue;
                }
                else if (!CategoryVisible(ch.CategoryId, ch.CategoryName)) continue;
                list.Add(ch);
            }
            return list;
        }

        private void RefreshLiveList()
        {
            if (_data == null) return;
            // Favorites view rewrites the columns — restore the Live columns.
            if (_lvChannels.Columns.Count != 3 || _lvChannels.Columns[0].Text != "Channel")
            {
                _lvChannels.Columns.Clear();
                _lvChannels.Columns.Add("Channel", 300);
                _lvChannels.Columns.Add("Now", 320);
                _lvChannels.Columns.Add("Next", 320);
            }
            var channels = CurrentChannels();
            _lvChannels.BeginUpdate();
            _lvChannels.Items.Clear();
            foreach (var ch in channels)
            {
                var item = new ListViewItem(ch.Name) { Tag = ch };
                var nowNext = GetNowNext(ch);
                item.SubItems.Add(nowNext.Item1);
                item.SubItems.Add(nowNext.Item2);
                if (Store.IsFavorite(Store.FavKey("live", ch.Id)))
                    item.Text = "★ " + ch.Name;
                if (_logoImages.Images.ContainsKey(ch.Id))
                    item.ImageKey = ch.Id;
                _lvChannels.Items.Add(item);
            }
            _lvChannels.EndUpdate();
            SetStatus(channels.Count + " channels");
            _ = LoadLogosAsync(channels);
            _ = LoadEpgForCategoryAsync(channels);
        }

        private Tuple<string, string> GetNowNext(Channel ch)
        {
            var now = DateTimeOffset.Now;
            if (_data != null && _data.EpgByChannel.TryGetValue(ch.EpgId, out var progs))
            {
                string cur = "", nxt = "";
                foreach (var p in progs)
                {
                    if (p.Start <= now && p.End > now) cur = p.Title;
                    else if (p.Start > now && nxt.Length == 0) nxt = p.Start.ToString("HH:mm") + "  " + p.Title;
                }
                return Tuple.Create(cur, nxt);
            }
            if (_data != null && _data.EpgByChannel.TryGetValue(ch.StreamId, out var progs2))
            {
                string cur = "", nxt = "";
                foreach (var p in progs2)
                {
                    if (p.Start <= now && p.End > now) cur = p.Title;
                    else if (p.Start > now && nxt.Length == 0) nxt = p.Start.ToString("HH:mm") + "  " + p.Title;
                }
                return Tuple.Create(cur, nxt);
            }
            return Tuple.Create("", "");
        }

        private async Task LoadLogosAsync(List<Channel> channels)
        {
            int done = 0;
            foreach (var ch in channels)
            {
                if (done >= 120) break;
                if (string.IsNullOrEmpty(ch.Logo) || _logoTried.Contains(ch.Id)) continue;
                _logoTried.Add(ch.Id);
                done++;
                try
                {
                    var bytes = await ImgHttp.GetByteArrayAsync(ch.Logo);
                    using var ms = new MemoryStream(bytes);
                    using var img = Image.FromStream(ms);
                    var bmp = new Bitmap(img, new Size(48, 27));
                    if (IsDisposed) return;
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!_logoImages.Images.ContainsKey(ch.Id))
                                _logoImages.Images.Add(ch.Id, bmp);
                            foreach (ListViewItem it in _lvChannels.Items)
                            {
                                if (it.Tag is Channel c && c.Id == ch.Id)
                                {
                                    it.ImageKey = ch.Id;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }));
                }
                catch { }
                await Task.Delay(30);
            }
        }

        private async Task LoadEpgForCategoryAsync(List<Channel> channels)
        {
            if (_data == null) return;
            if (_data.IsXtream && _xtream != null)
            {
                int done = 0;
                foreach (var ch in channels)
                {
                    if (done >= 60) break;
                    if (string.IsNullOrEmpty(ch.StreamId) || _epgTried.Contains(ch.StreamId)) continue;
                    _epgTried.Add(ch.StreamId);
                    done++;
                    try
                    {
                        var progs = await _xtream.GetShortEpgAsync(ch.StreamId, 4);
                        if (progs.Count > 0)
                        {
                            _data.EpgByChannel[ch.StreamId] = progs;
                            if (!string.IsNullOrEmpty(ch.EpgId) && ch.EpgId != ch.StreamId)
                                _data.EpgByChannel[ch.EpgId] = progs;
                            if (IsDisposed) return;
                            BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    foreach (ListViewItem it in _lvChannels.Items)
                                    {
                                        if (it.Tag is Channel c && c.Id == ch.Id)
                                        {
                                            var nn = GetNowNext(c);
                                            it.SubItems[1].Text = nn.Item1;
                                            it.SubItems[2].Text = nn.Item2;
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }));
                        }
                    }
                    catch { }
                    await Task.Delay(120);
                }
            }
        }

        private Channel? SelectedChannel()
        {
            if (_lvChannels.SelectedItems.Count == 0) return null;
            return _lvChannels.SelectedItems[0].Tag as Channel;
        }

        private void PlaySelectedChannel(bool secondScreen = false)
        {
            if (_lvChannels.SelectedItems.Count == 0) return;
            var tag = _lvChannels.SelectedItems[0].Tag;
            if (tag is Channel ch) PlayChannel(ch, secondScreen);
            else if (tag is VodItem m) PlayVod(m);
            else if (tag is SerieItem s) ShowSerieDetails(s);
        }

        private void ToggleSelectedFavorite()
        {
            if (_lvChannels.SelectedItems.Count == 0) return;
            var tag = _lvChannels.SelectedItems[0].Tag;
            if (tag is Channel ch) Store.ToggleFavorite(Store.FavKey("live", ch.Id));
            else if (tag is VodItem m) Store.ToggleFavorite(Store.FavKey("vod", m.Id));
            else if (tag is SerieItem s) Store.ToggleFavorite(Store.FavKey("series", s.Id));
            else return;
            RefreshCurrentList();
        }

        private void ShowSelectedEpg()
        {
            var ch = SelectedChannel();
            if (ch == null) return;
            var progs = GetChannelPrograms(ch);
            string text = "No programme information for " + ch.Name + ".";
            if (progs.Count > 0)
            {
                var now = DateTimeOffset.Now;
                var lines = new List<string>();
                foreach (var p in progs)
                {
                    if (p.End < now.AddHours(-2)) continue;
                    if (p.Start > now.AddHours(6)) break;
                    lines.Add(p.Start.ToString("HH:mm") + " - " + p.End.ToString("HH:mm") + "   " + p.Title);
                    if (!string.IsNullOrEmpty(p.Description)) lines.Add("      " + p.Description);
                    if (lines.Count > 14) break;
                }
                text = ch.Name + "\n\n" + string.Join("\n", lines);
            }
            MessageBox.Show(this, text, "EPG — " + ch.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<EpgProgram> GetChannelPrograms(Channel ch)
        {
            if (_data == null) return new List<EpgProgram>();
            if (_data.EpgByChannel.TryGetValue(ch.EpgId, out var p)) return p;
            if (_data.EpgByChannel.TryGetValue(ch.StreamId, out var p2)) return p2;
            return new List<EpgProgram>();
        }

        // ================= GUIDE (7-day EPG) =================

        private ListView _lvGuideChannels = new ListView();
        private ListView _lvGuidePrograms = new ListView();
        private FlowLayoutPanel _guideDays = new FlowLayoutPanel();
        private int _guideDayOffset = 0;
        private Channel? _guideChannel;

        private void BuildGuidePanel()
        {
            _guidePanel.Dock = DockStyle.Fill;
            _guidePanel.BackColor = Theme.Current.Bg;
            _guidePanel.Visible = false;
            _listPanel.Controls.Add(_guidePanel);

            var lbl = new Label
            {
                Text = "Programme Guide — pick a channel, then a day",
                AutoSize = true,
                Location = new Point(8, 8),
                ForeColor = Theme.Current.Text,
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            _guidePanel.Controls.Add(lbl);

            _guideDays = new FlowLayoutPanel
            {
                Location = new Point(8, 40),
                Size = new Size(700, 34),
                FlowDirection = FlowDirection.LeftToRight
            };
            _guidePanel.Controls.Add(_guideDays);
            for (int d = 0; d < 7; d++)
            {
                var day = DateTime.Today.AddDays(d);
                var b = new Button
                {
                    Text = d == 0 ? "Today" : day.ToString("ddd dd MMM"),
                    Tag = d,
                    Size = new Size(96, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = d == 0 ? Theme.Current.Accent : Theme.Current.CardBg,
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (_, _) =>
                {
                    _guideDayOffset = (int)b.Tag;
                    foreach (Control c in _guideDays.Controls)
                        if (c is Button bb) bb.BackColor = (int)bb.Tag == _guideDayOffset ? Theme.Current.Accent : Theme.Current.CardBg;
                    RefreshGuidePrograms();
                };
                _guideDays.Controls.Add(b);
            }

            _lvGuideChannels = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Location = new Point(8, 84),
                Size = new Size(300, 460),
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            _lvGuideChannels.Columns.Add("Channel", 290);
            _lvGuideChannels.SelectedIndexChanged += (_, _) => RefreshGuidePrograms();
            _guidePanel.Controls.Add(_lvGuideChannels);

            _lvGuidePrograms = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Location = new Point(320, 84),
                Size = new Size(620, 460),
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _lvGuidePrograms.Columns.Add("Time", 130);
            _lvGuidePrograms.Columns.Add("Programme", 480);
            _lvGuidePrograms.DoubleClick += (_, _) => PlayGuideSelection();
            _guidePanel.Controls.Add(_lvGuidePrograms);
        }

        private void RefreshGuideChannels()
        {
            if (_data == null) return;
            _lvGuideChannels.BeginUpdate();
            _lvGuideChannels.Items.Clear();
            foreach (var ch in _data.Channels)
            {
                if (!MatchesSearch(ch.Name)) continue;
                if (!CategoryVisible(ch.CategoryId, ch.CategoryName)) continue;
                _lvGuideChannels.Items.Add(new ListViewItem(ch.Name) { Tag = ch });
            }
            _lvGuideChannels.EndUpdate();
            if (_lvGuideChannels.Items.Count > 0 && _guideChannel == null)
                _lvGuideChannels.Items[0].Selected = true;
        }

        private async void RefreshGuidePrograms()
        {
            _lvGuidePrograms.Items.Clear();
            if (_lvGuideChannels.SelectedItems.Count == 0) return;
            _guideChannel = _lvGuideChannels.SelectedItems[0].Tag as Channel;
            if (_guideChannel == null || _data == null) return;
            var day = DateTime.Today.AddDays(_guideDayOffset);
            var progs = GetChannelPrograms(_guideChannel);
            if (progs.Count == 0 && _data.IsXtream && _xtream != null && !string.IsNullOrEmpty(_guideChannel.StreamId))
            {
                SetStatus("Loading guide for " + _guideChannel.Name + "…");
                progs = await _xtream.GetFullEpgAsync(_guideChannel.StreamId);
                if (progs.Count > 0) _data.EpgByChannel[_guideChannel.StreamId] = progs;
            }
            _lvGuidePrograms.BeginUpdate();
            foreach (var p in progs)
            {
                if (p.Start.Date != day) continue;
                var item = new ListViewItem(p.Start.ToString("HH:mm") + " - " + p.End.ToString("HH:mm")) { Tag = p };
                item.SubItems.Add(p.Title);
                _lvGuidePrograms.Items.Add(item);
            }
            _lvGuidePrograms.EndUpdate();
            if (_lvGuidePrograms.Items.Count == 0)
                SetStatus("No programmes for " + _guideChannel.Name + " on " + day.ToString("dd MMM") + ".");
        }

        private void PlayGuideSelection()
        {
            if (_lvGuidePrograms.SelectedItems.Count == 0 || _guideChannel == null) return;
            var p = _lvGuidePrograms.SelectedItems[0].Tag as EpgProgram;
            if (p == null) return;
            var now = DateTimeOffset.Now;
            if (_data != null && _data.IsXtream && _xtream != null && p.Start <= now && p.End > now.AddMinutes(-1))
            {
                // Catch-up: play from the programme start
                int duration = (int)Math.Max(1, (p.End - p.Start).TotalMinutes);
                var url = _xtream.TimeshiftUrl(_guideChannel.StreamId, p.Start, duration);
                PlayStreamUrl(url, false, "live", _guideChannel.Id, _guideChannel.Name + " — " + p.Title, _guideChannel);
                return;
            }
            if (p.Start <= now && p.End > now)
            {
                PlayChannel(_guideChannel);
                return;
            }
            MessageBox.Show(this, p.Title + "\n" + p.Start.ToString("dd MMM HH:mm") + " - " + p.End.ToString("HH:mm") +
                (string.IsNullOrEmpty(p.Description) ? "" : "\n\n" + p.Description),
                "Programme", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================= FAVORITES =================

        private void RefreshFavoritesList()
        {
            if (_data == null) return;
            _lvChannels.BeginUpdate();
            _lvChannels.Items.Clear();
            _lvChannels.Columns.Clear();
            _lvChannels.Columns.Add("Type", 90);
            _lvChannels.Columns.Add("Name", 300);
            _lvChannels.Columns.Add("Info", 320);
            foreach (var ch in _data.Channels)
            {
                if (!Store.IsFavorite(Store.FavKey("live", ch.Id))) continue;
                if (!MatchesSearch(ch.Name)) continue;
                var item = new ListViewItem("📺 Live") { Tag = ch };
                item.SubItems.Add(ch.Name);
                var nn = GetNowNext(ch);
                item.SubItems.Add(nn.Item1);
                _lvChannels.Items.Add(item);
            }
            foreach (var m in _data.Movies)
            {
                if (!Store.IsFavorite(Store.FavKey("vod", m.Id))) continue;
                if (!MatchesSearch(m.Name)) continue;
                var item = new ListViewItem("🎬 Movie") { Tag = m };
                item.SubItems.Add(m.Name);
                item.SubItems.Add((m.Year ?? "") + "  ★ " + (m.Rating ?? ""));
                _lvChannels.Items.Add(item);
            }
            foreach (var s in _data.Series)
            {
                if (!Store.IsFavorite(Store.FavKey("series", s.Id))) continue;
                if (!MatchesSearch(s.Name)) continue;
                var item = new ListViewItem("📺 Series") { Tag = s };
                item.SubItems.Add(s.Name);
                item.SubItems.Add((s.Year ?? "") + "  ★ " + (s.Rating ?? ""));
                _lvChannels.Items.Add(item);
            }
            _lvChannels.EndUpdate();
            SetStatus(_lvChannels.Items.Count + " favorites");
        }
    }
}
