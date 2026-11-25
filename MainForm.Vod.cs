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
        private void BuildPosterFlow()
        {
            _posterFlow.Dock = DockStyle.Fill;
            _posterFlow.AutoScroll = true;
            _posterFlow.BackColor = Theme.Current.Bg;
            _posterFlow.Visible = false;
            _posterFlow.Padding = new Padding(10);
            _listPanel.Controls.Add(_posterFlow);
        }

        private List<VodItem> CurrentMovies()
        {
            var all = _data?.Movies ?? new List<VodItem>();
            var list = new List<VodItem>();
            foreach (var m in all)
            {
                if (!MatchesSearch(m.Name)) continue;
                if (_categoryId == "__fav")
                {
                    if (!Store.IsFavorite(Store.FavKey("vod", m.Id))) continue;
                }
                else if (_categoryId == "__recent")
                {
                    if (!Store.Data.Recents.Any(r => r.Kind == "vod" && r.Id == m.Id)) continue;
                }
                else if (!CategoryVisible(m.CategoryId, m.CategoryName)) continue;
                list.Add(m);
            }
            return list;
        }

        private List<SerieItem> CurrentSeries()
        {
            var all = _data?.Series ?? new List<SerieItem>();
            var list = new List<SerieItem>();
            foreach (var s in all)
            {
                if (!MatchesSearch(s.Name)) continue;
                if (_categoryId == "__fav")
                {
                    if (!Store.IsFavorite(Store.FavKey("series", s.Id))) continue;
                }
                else if (_categoryId == "__recent")
                {
                    if (!Store.Data.Recents.Any(r => r.Kind == "series" && r.Id == s.Id)) continue;
                }
                else if (!CategoryVisible(s.CategoryId, s.CategoryName)) continue;
                list.Add(s);
            }
            return list;
        }

        private void RefreshVodList(bool series)
        {
            _posterFlow.Controls.Clear();
            if (_data == null) return;
            if (!series && _data.Movies.Count == 0)
            {
                _posterFlow.Controls.Add(new Label
                {
                    Text = _data.IsXtream ? "No movies in this playlist." : "Movies are available with Xtream Codes playlists.",
                    AutoSize = true,
                    ForeColor = Theme.Current.Muted,
                    Padding = new Padding(10)
                });
                return;
            }
            if (series && _data.Series.Count == 0)
            {
                _posterFlow.Controls.Add(new Label
                {
                    Text = _data.IsXtream ? "No series in this playlist." : "Series are available with Xtream Codes playlists.",
                    AutoSize = true,
                    ForeColor = Theme.Current.Muted,
                    Padding = new Padding(10)
                });
                return;
            }
            if (!series)
            {
                var movies = CurrentMovies();
                int shown = 0;
                foreach (var m in movies)
                {
                    if (shown >= 200) break;
                    _posterFlow.Controls.Add(MakePosterCard(m.Name, m.Year, m.Rating, m.Cover,
                        () => ShowMovieDetails(m)));
                    shown++;
                    _ = LoadPosterAsync(m.Cover, m.Id);
                }
                SetStatus(movies.Count + " movies" + (movies.Count > 200 ? " (showing 200)" : ""));
            }
            else
            {
                var list = CurrentSeries();
                int shown = 0;
                foreach (var s in list)
                {
                    if (shown >= 200) break;
                    _posterFlow.Controls.Add(MakePosterCard(s.Name, s.Year, s.Rating, s.Cover,
                        () => ShowSerieDetails(s)));
                    shown++;
                    _ = LoadPosterAsync(s.Cover, s.Id);
                }
                SetStatus(list.Count + " series" + (list.Count > 200 ? " (showing 200)" : ""));
            }
        }

        private Panel MakePosterCard(string name, string year, string rating, string cover, Action onClick)
        {
            var card = new Panel
            {
                Size = new Size(160, 250),
                Margin = new Padding(6),
                BackColor = Theme.Current.CardBg,
                Cursor = Cursors.Hand
            };
            var pic = new PictureBox
            {
                Dock = DockStyle.Top,
                Height = 190,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.Current.PanelBg,
                Tag = cover
            };
            pic.Paint += (_, e) =>
            {
                if (pic.Image == null)
                {
                    using var brush = new SolidBrush(Theme.Current.AccentDark);
                    e.Graphics.FillRectangle(brush, pic.ClientRectangle);
                    var initials = "";
                    foreach (var w in name.Split(' '))
                        if (w.Length > 0) initials += char.ToUpper(w[0]);
                    if (initials.Length > 3) initials = initials.Substring(0, 3);
                    using var f = new Font("Segoe UI", 28, FontStyle.Bold);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString(initials, f, Brushes.White, pic.ClientRectangle, sf);
                }
            };
            card.Controls.Add(pic);
            var lblName = new Label
            {
                Text = name,
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Theme.Current.Text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 0)
            };
            card.Controls.Add(lblName);
            var lblMeta = new Label
            {
                Text = (year ?? "") + (string.IsNullOrEmpty(rating) ? "" : "   ★ " + rating),
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.Current.Muted,
                Padding = new Padding(6, 0, 6, 0)
            };
            card.Controls.Add(lblMeta);
            card.Click += (_, _) => onClick();
            pic.Click += (_, _) => onClick();
            lblName.Click += (_, _) => onClick();
            lblMeta.Click += (_, _) => onClick();
            return card;
        }

        private async Task LoadPosterAsync(string coverUrl, string id)
        {
            if (string.IsNullOrEmpty(coverUrl) || _posterTried.Contains(id)) return;
            _posterTried.Add(id);
            try
            {
                var bytes = await ImgHttp.GetByteArrayAsync(coverUrl);
                using var ms = new MemoryStream(bytes);
                using var img = Image.FromStream(ms);
                var bmp = new Bitmap(img);
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        foreach (Control card in _posterFlow.Controls)
                        {
                            foreach (Control c in card.Controls)
                            {
                                if (c is PictureBox pb && (string?)pb.Tag == coverUrl)
                                {
                                    pb.Image = bmp;
                                    pb.Invalidate();
                                }
                            }
                        }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void ShowDetailsBase(string title, string cover, string meta, string plot, Action onPlay, Action onFavorite, bool isFav, Control? extra)
        {
            _detailsPanel.Controls.Clear();
            _detailsPanel.BackColor = Theme.Current.Bg;
            var back = new Button
            {
                Text = "← Back",
                Location = new Point(12, 12),
                Size = new Size(90, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                Cursor = Cursors.Hand
            };
            back.FlatAppearance.BorderSize = 0;
            back.Click += (_, _) => { _detailsPanel.Visible = false; };
            _detailsPanel.Controls.Add(back);

            var pic = new PictureBox
            {
                Location = new Point(12, 58),
                Size = new Size(180, 240),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.Current.PanelBg
            };
            _detailsPanel.Controls.Add(pic);
            _ = LoadDetailsPosterAsync(cover, pic);

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Theme.Current.Text,
                AutoSize = true,
                Location = new Point(210, 58)
            };
            _detailsPanel.Controls.Add(lblTitle);
            var lblMeta = new Label
            {
                Text = meta,
                AutoSize = true,
                ForeColor = Theme.Current.Muted,
                Location = new Point(212, 100)
            };
            _detailsPanel.Controls.Add(lblMeta);
            var lblPlot = new Label
            {
                Text = string.IsNullOrEmpty(plot) ? "No description available." : plot,
                AutoSize = false,
                Size = new Size(620, 120),
                Location = new Point(212, 130),
                ForeColor = Theme.Current.Text,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _detailsPanel.Controls.Add(lblPlot);

            var btnPlay = new Button
            {
                Text = "▶  Play",
                Location = new Point(212, 260),
                Size = new Size(140, 44),
                BackColor = Theme.Current.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPlay.FlatAppearance.BorderSize = 0;
            btnPlay.Click += (_, _) => onPlay();
            _detailsPanel.Controls.Add(btnPlay);

            var btnFav = new Button
            {
                Text = isFav ? "★ Favorited" : "☆ Favorite",
                Location = new Point(364, 260),
                Size = new Size(140, 44),
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnFav.FlatAppearance.BorderSize = 0;
            btnFav.Click += (_, _) => { onFavorite(); btnFav.Text = "★ Favorited"; };
            _detailsPanel.Controls.Add(btnFav);

            if (extra != null)
            {
                extra.Location = new Point(12, 320);
                extra.Size = new Size(760, 220);
                extra.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                _detailsPanel.Controls.Add(extra);
            }
            _detailsPanel.Visible = true;
            _detailsPanel.BringToFront();
        }

        private async Task LoadDetailsPosterAsync(string coverUrl, PictureBox pic)
        {
            if (string.IsNullOrEmpty(coverUrl)) return;
            try
            {
                var bytes = await ImgHttp.GetByteArrayAsync(coverUrl);
                using var ms = new MemoryStream(bytes);
                using var img = Image.FromStream(ms);
                var bmp = new Bitmap(img);
                if (IsDisposed) return;
                BeginInvoke(new Action(() => { try { pic.Image = bmp; } catch { } }));
            }
            catch { }
        }

        private void ShowMovieDetails(VodItem m)
        {
            ShowDetailsBase(m.Name, m.Cover,
                (m.Year ?? "") + (string.IsNullOrEmpty(m.Rating) ? "" : "   ★ " + m.Rating) + "   " + (m.CategoryName ?? ""),
                m.Plot ?? "",
                () => PlayVod(m),
                () => Store.ToggleFavorite(Store.FavKey("vod", m.Id)),
                Store.IsFavorite(Store.FavKey("vod", m.Id)),
                null);
        }

        private void ShowSerieDetails(SerieItem s)
        {
            var panel = new Panel();
            var lblSeasons = new Label { Text = "Season:", AutoSize = true, Location = new Point(0, 6), ForeColor = Theme.Current.Text };
            panel.Controls.Add(lblSeasons);
            var cbSeasons = new ComboBox
            {
                Location = new Point(70, 2),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text
            };
            panel.Controls.Add(cbSeasons);
            var lbEps = new ListBox
            {
                Location = new Point(0, 40),
                Size = new Size(740, 170),
                BackColor = Theme.Current.CardBg,
                ForeColor = Theme.Current.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(lbEps);

            void LoadSeason(string season)
            {
                lbEps.Items.Clear();
                if (!s.Episodes.TryGetValue(season, out var eps)) return;
                foreach (var ep in eps)
                    lbEps.Items.Add($"Episode {ep.EpisodeNum}:  {ep.Title}");
            }

            cbSeasons.SelectedIndexChanged += (_, _) =>
            {
                if (cbSeasons.SelectedItem != null) LoadSeason(cbSeasons.SelectedItem.ToString() ?? "1");
            };
            lbEps.DoubleClick += (_, _) =>
            {
                if (lbEps.SelectedIndex < 0 || cbSeasons.SelectedItem == null) return;
                var season = cbSeasons.SelectedItem.ToString() ?? "1";
                if (!s.Episodes.TryGetValue(season, out var eps)) return;
                if (lbEps.SelectedIndex < eps.Count) PlayEpisode(s, eps[lbEps.SelectedIndex]);
            };

            ShowDetailsBase(s.Name, s.Cover,
                (s.Year ?? "") + (string.IsNullOrEmpty(s.Rating) ? "" : "   ★ " + s.Rating) + "   " + (s.CategoryName ?? "") +
                (string.IsNullOrEmpty(s.Cast) ? "" : "\nCast: " + s.Cast),
                s.Plot ?? "",
                () =>
                {
                    if (cbSeasons.SelectedItem != null && s.Episodes.TryGetValue(cbSeasons.SelectedItem.ToString() ?? "1", out var eps) && eps.Count > 0)
                        PlayEpisode(s, eps[0]);
                },
                () => Store.ToggleFavorite(Store.FavKey("series", s.Id)),
                Store.IsFavorite(Store.FavKey("series", s.Id)),
                panel);

            _ = LoadSerieEpisodesAsync(s, cbSeasons, LoadSeason);
        }

        private async Task LoadSerieEpisodesAsync(SerieItem s, ComboBox cbSeasons, Action<string> loadSeason)
        {
            if (s.Episodes.Count == 0 && _xtream != null)
            {
                SetStatus("Loading episodes for " + s.Name + "…");
                await _xtream.GetSeriesInfoAsync(s);
            }
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                try
                {
                    cbSeasons.Items.Clear();
                    foreach (var season in s.Episodes.Keys)
                        cbSeasons.Items.Add(season);
                    if (cbSeasons.Items.Count > 0)
                    {
                        cbSeasons.SelectedIndex = 0;
                        loadSeason(cbSeasons.Items[0].ToString() ?? "1");
                    }
                    else
                    {
                        cbSeasons.Items.Add("No episodes found");
                        cbSeasons.SelectedIndex = 0;
                    }
                }
                catch { }
            }));
        }
    }
}
