using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ZaghlolPlayer
{
    /// <summary>Holds the currently loaded playlist for the session.</summary>
    public static class Session
    {
        public static PlaylistData? Data;
        public static PlaylistService.XtreamClient? Xtream;
        public static LoginProfile? Login;
        public static Dictionary<string, List<EpgProgram>> XmlTv = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
        public static bool XmlTvLoaded;
    }

    /// <summary>
    /// Host object exposed to the WebView2 UI as window.chrome.webview.hostObjects.zaghlol.
    /// Every method returns a JSON string: { ok: true, data: ... } or { ok: false, error: "..." }.
    /// Async methods become Promises in JavaScript.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class Bridge
    {
        private readonly MainForm _form;

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        public Bridge(MainForm form) { _form = form; }

        private static string J(object o) => JsonSerializer.Serialize(o, Opts);
        private static string Ok(object? data = null) => J(new { ok = true, data });
        private static string Err(string msg) => J(new { ok = false, error = msg });

        // ---------------- state ----------------

        public Task<string> GetState()
        {
            return Task.FromResult(J(new
            {
                version = "2.0.2",
                settings = Store.Settings,
                logins = Store.Data.Logins,
                favorites = Store.Data.Favorites,
                recents = Store.Data.Recents,
                lockedCategories = Store.Data.LockedCategories,
                themes = new[] { "Purple", "Blue", "Green", "Red", "Orange", "Teal" }
            }));
        }

        public Task<string> SetSetting(string key, string value)
        {
            var s = Store.Settings;
            switch (key)
            {
                case "theme": s.ThemeName = value; break;
                case "volume": if (int.TryParse(value, out var v)) s.Volume = Math.Clamp(v, 0, 100); break;
                case "networkCachingMs": if (int.TryParse(value, out var n)) s.NetworkCachingMs = Math.Clamp(n, 200, 10000); break;
                case "hardwareAccel": s.HardwareAccel = value == "true" || value == "1"; break;
                case "epgUrl": s.EpgUrl = value ?? ""; Session.XmlTvLoaded = false; break;
                case "dualScreen": s.DualScreen = value == "true" || value == "1"; break;
                default: return Task.FromResult(Err("unknown setting: " + key));
            }
            Store.SaveSettings();
            return Task.FromResult(Ok());
        }

        public Task<string> SetFullscreen(bool on)
        {
            try { _form.SetFullscreen(on); return Task.FromResult(Ok()); }
            catch (Exception ex) { return Task.FromResult(Err(ex.Message)); }
        }

        public Task<string> ChangePin(string oldPin, string newPin)
        {
            if (Store.Settings.Pin != oldPin) return Task.FromResult(Err("كلمة المرور الحالية غير صحيحة"));
            if (string.IsNullOrWhiteSpace(newPin) || newPin.Length < 4) return Task.FromResult(Err("الـ PIN الجديد لازم يكون 4 أرقام على الأقل"));
            Store.Settings.Pin = newPin.Trim();
            Store.SaveSettings();
            return Task.FromResult(Ok());
        }

        public Task<string> VerifyPin(string pin)
        {
            return Task.FromResult(Store.Settings.Pin == pin ? Ok() : Err("PIN غير صحيح"));
        }

        public Task<string> LockCategory(string name)
        {
            if (!Store.Data.LockedCategories.Contains(name)) Store.Data.LockedCategories.Add(name);
            Store.SaveData();
            return Task.FromResult(Ok());
        }

        public Task<string> UnlockCategory(string name)
        {
            Store.Data.LockedCategories.Remove(name);
            Store.SaveData();
            return Task.FromResult(Ok());
        }

        // ---------------- login ----------------

        private void RememberLogin(LoginProfile p)
        {
            var existing = Store.Data.Logins.FirstOrDefault(l =>
                l.Kind == p.Kind &&
                string.Equals(l.M3uUrl, p.M3uUrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Server, p.Server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Username, p.Username, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = p.Name;
                existing.Password = p.Password;
                existing.M3uPath = p.M3uPath;
                existing.Mac = p.Mac;
                p.Id = existing.Id;
            }
            else
            {
                Store.Data.Logins.Insert(0, p);
            }
            Store.Settings.LastLoginId = p.Id;
            Store.SaveData();
            Store.SaveSettings();
        }

        public async Task<string> AutoLogin()
        {
            try
            {
                var id = Store.Settings.LastLoginId;
                if (string.IsNullOrEmpty(id)) return Err("no saved login");
                var p = Store.Data.Logins.FirstOrDefault(l => l.Id == id);
                if (p == null) return Err("saved login not found");
                switch (p.Kind)
                {
                    case PlaylistKind.M3uUrl: return await LoginM3u(p.M3uUrl, p.Name);
                    case PlaylistKind.M3uFile:
                        if (string.IsNullOrEmpty(p.M3uPath) || !File.Exists(p.M3uPath)) return Err("ملف M3U غير موجود");
                        return await LoadM3uFile(p.M3uPath);
                    case PlaylistKind.Xtream: return await LoginXtream(p.Server, p.Username, p.Password, p.Name);
                    case PlaylistKind.Demo: return await LoginDemo();
                    case PlaylistKind.Mac: return await LoginMac();
                    default: return Err("unknown login kind");
                }
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        public async Task<string> LoginM3u(string url, string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return Err("من فضلك أدخل رابط M3U");
                url = url.Trim();
                // Panels generate big playlists on the fly — give it up to 60s.
                var r = await PlaylistService.FetchTextAsync(url, 60);
                if (!r.Ok) return Err(r.ErrorAr);
                var text = r.Text ?? "";
                if (!text.Contains("#EXTM3U") && !text.Contains("#EXTINF"))
                    return Err("السيرفر ردّ برد ليس قائمة M3U — تأكد من صحة الرابط وبيانات الاشتراك");
                var login = new LoginProfile { Name = string.IsNullOrWhiteSpace(name) ? "M3U Playlist" : name, Kind = PlaylistKind.M3uUrl, M3uUrl = url };
                var data = PlaylistService.ParseM3u(text, login);
                if (data.Channels.Count == 0) return Err("القائمة فارغة — لا توجد قنوات في هذا الرابط");
                Session.Data = data; Session.Login = login; Session.Xtream = null;
                Session.XmlTvLoaded = false; Session.XmlTv.Clear();
                RememberLogin(login);
                return Ok(new { channels = data.Channels.Count });
            }
            catch (Exception ex) { return Err(PlaylistService.CleanError(ex.Message)); }
        }

        public async Task<string> LoadM3uFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Err("الملف غير موجود");
                var text = await File.ReadAllTextAsync(path);
                var login = new LoginProfile { Name = "M3U File: " + Path.GetFileName(path), Kind = PlaylistKind.M3uFile, M3uPath = path };
                var data = PlaylistService.ParseM3u(text, login);
                if (data.Channels.Count == 0) return Err("الملف لا يحتوي على قنوات");
                Session.Data = data; Session.Login = login; Session.Xtream = null;
                Session.XmlTvLoaded = false; Session.XmlTv.Clear();
                RememberLogin(login);
                return Ok(new { channels = data.Channels.Count });
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        public Task<string> PickM3uFile()
        {
            var path = _form.ShowOpenFileDialog("M3U playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*", "اختر ملف قائمة M3U");
            if (string.IsNullOrEmpty(path)) return Task.FromResult(Err("cancelled"));
            return LoadM3uFile(path);
        }

        public async Task<string> LoginXtream(string server, string user, string pass, string name)
        {
            // Hard global timeout: the whole login (auth + all lists) must finish in 25s.
            // The old code made 7 sequential requests, each up to 30s — a slow panel
            // could leave the button stuck on "loading" for minutes.
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(25));
            var ct = cts.Token;
            try
            {
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(user))
                    return Err("أدخل السيرفر واسم المستخدم وكلمة المرور");
                server = server.Trim().TrimEnd('/');
                if (!server.StartsWith("http", StringComparison.OrdinalIgnoreCase)) server = "http://" + server;
                var client = new PlaylistService.XtreamClient(server, user.Trim(), pass ?? "");
                var (authed, authErr) = await client.AuthenticateDetailedAsync(ct);
                if (!authed) return Err(authErr);
                var data = new PlaylistData
                {
                    Login = new LoginProfile { Name = string.IsNullOrWhiteSpace(name) ? "Xtream" : name, Kind = PlaylistKind.Xtream, Server = server, Username = user.Trim(), Password = pass ?? "" }
                };
                // The 6 catalogue calls are independent — run them in parallel instead of
                // one after another, so a slow panel costs the slowest call, not the sum.
                var tLiveCats = client.GetLiveCategoriesAsync(ct);
                var tLive = client.GetLiveStreamsAsync(ct);
                var tVodCats = client.GetVodCategoriesAsync(ct);
                var tVod = client.GetVodStreamsAsync(ct);
                var tSerCats = client.GetSeriesCategoriesAsync(ct);
                var tSer = client.GetSeriesAsync(ct);
                await Task.WhenAll(tLiveCats, tLive, tVodCats, tVod, tSerCats, tSer);
                if (ct.IsCancellationRequested)
                    return Err("انتهت المهلة (25 ثانية) — السيرفر بطيء أو لا يرد. جرّب «اختبار الاتصال» أولاً.");
                data.LiveCategories = await tLiveCats;
                data.Channels = await tLive;
                data.VodCategories = await tVodCats;
                data.Movies = await tVod;
                data.SeriesCategories = await tSerCats;
                data.Series = await tSer;
                var catNames = data.LiveCategories.ToDictionary(c => c.Id, c => c.Name);
                foreach (var ch in data.Channels)
                    if (catNames.TryGetValue(ch.CategoryId, out var cn)) ch.CategoryName = cn;
                var vodNames = data.VodCategories.ToDictionary(c => c.Id, c => c.Name);
                foreach (var m in data.Movies)
                    if (vodNames.TryGetValue(m.CategoryId, out var vn)) m.CategoryName = vn;
                var serNames = data.SeriesCategories.ToDictionary(c => c.Id, c => c.Name);
                foreach (var s in data.Series)
                    if (serNames.TryGetValue(s.CategoryId, out var sn)) s.CategoryName = sn;
                if (data.Channels.Count == 0 && data.Movies.Count == 0 && data.Series.Count == 0)
                    return Err("تم تسجيل الدخول لكن الحساب لا يحتوي على قنوات أو أفلام أو مسلسلات");
                Session.Data = data; Session.Login = data.Login; Session.Xtream = client;
                Session.XmlTvLoaded = false; Session.XmlTv.Clear();
                RememberLogin(data.Login);
                return Ok(new { channels = data.Channels.Count, movies = data.Movies.Count, series = data.Series.Count });
            }
            catch (OperationCanceledException)
            {
                return Err("انتهت المهلة (25 ثانية) — السيرفر بطيء أو لا يرد. جرّب «اختبار الاتصال» أولاً.");
            }
            catch (Exception ex) { return Err(PlaylistService.CleanError(ex.Message)); }
        }

        /// <summary>
        /// "اختبار الاتصال" from the login screen: reports plainly what the server
        /// answered (status / timing / auth result) so the next failure is diagnosable.
        /// Never includes the password or the full URL (query string) in the report.
        /// </summary>
        public async Task<string> TestConnection(string kind, string a, string b, string c)
        {
            try
            {
                var lines = new List<string>();
                if (kind == "m3u")
                {
                    var url = (a ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(url)) return Err("أدخل رابط M3U أولاً");
                    var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
                    lines.Add("الخادم: " + host);
                    var r = await PlaylistService.FetchStartAsync(url, 15, 2048);
                    if (!r.Ok)
                    {
                        lines.Add("النتيجة: فشل — " + r.ErrorAr);
                        return Ok(new { ok = false, report = string.Join("\n", lines) });
                    }
                    lines.Add($"الرد: HTTP {r.StatusCode} خلال {r.ElapsedMs} م.ث" + (r.ContentLength.HasValue ? $" — الحجم {r.ContentLength.Value:N0} بايت" : ""));
                    var head = (r.Text ?? "").TrimStart();
                    var looksPlaylist = head.Contains("#EXTM3U") || head.Contains("#EXTINF");
                    lines.Add(looksPlaylist
                        ? "النتيجة: الرابط يرجّع قائمة M3U صالحة ✔ — اضغط «تحميل القائمة»"
                        : "النتيجة: الرد ليس قائمة M3U — تأكد من بيانات الاشتراك والرابط");
                    return Ok(new { ok = looksPlaylist, report = string.Join("\n", lines) });
                }
                if (kind == "xtream")
                {
                    var server = (a ?? "").Trim().TrimEnd('/');
                    if (string.IsNullOrWhiteSpace(server)) return Err("أدخل رابط السيرفر أولاً");
                    if (!server.StartsWith("http", StringComparison.OrdinalIgnoreCase)) server = "http://" + server;
                    if (string.IsNullOrWhiteSpace(b)) return Err("أدخل اسم المستخدم وكلمة المرور أولاً");
                    lines.Add("الخادم: " + server);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var client = new PlaylistService.XtreamClient(server, b.Trim(), c ?? "");
                    var (authed, authErr) = await client.AuthenticateDetailedAsync(System.Threading.CancellationToken.None);
                    lines.Add($"زمن الرد: {sw.ElapsedMilliseconds} م.ث");
                    lines.Add(authed
                        ? "النتيجة: تم تسجيل الدخول بنجاح ✔ — اضغط «تسجيل الدخول»"
                        : "النتيجة: فشل — " + authErr);
                    return Ok(new { ok = authed, report = string.Join("\n", lines) });
                }
                return Err("نوع اختبار غير معروف");
            }
            catch (Exception ex) { return Err(PlaylistService.CleanError(ex.Message)); }
        }

        public Task<string> LoginDemo()
        {
            var data = PlaylistService.DemoPlaylist();
            Session.Data = data; Session.Login = data.Login; Session.Xtream = null;
            Session.XmlTvLoaded = false; Session.XmlTv.Clear();
            RememberLogin(data.Login);
            return Task.FromResult(Ok(new { channels = data.Channels.Count }));
        }

        public Task<string> LoginMac()
        {
            var data = PlaylistService.DemoPlaylist();
            data.Login = new LoginProfile { Name = "MAC Portal", Kind = PlaylistKind.Mac, Mac = Store.Settings.DeviceMac };
            Session.Data = data; Session.Login = data.Login; Session.Xtream = null;
            Session.XmlTvLoaded = false; Session.XmlTv.Clear();
            RememberLogin(data.Login);
            return Task.FromResult(Ok(new { mac = Store.Settings.DeviceMac, channels = data.Channels.Count }));
        }

        public Task<string> DeleteLogin(string id)
        {
            Store.Data.Logins.RemoveAll(l => l.Id == id);
            if (Store.Settings.LastLoginId == id) Store.Settings.LastLoginId = "";
            Store.SaveData(); Store.SaveSettings();
            return Task.FromResult(Ok());
        }

        public Task<string> Logout()
        {
            Session.Data = null; Session.Xtream = null; Session.Login = null;
            Store.Settings.LastLoginId = "";
            Store.SaveSettings();
            return Task.FromResult(Ok());
        }

        // ---------------- data ----------------

        private static List<object> WithCounts(List<Category> cats, IEnumerable<string> itemCatIds)
        {
            var counts = itemCatIds.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            return cats.Select(c => (object)new
            {
                id = c.Id,
                name = c.Name,
                count = counts.TryGetValue(c.Id, out var n) ? n : 0
            }).ToList();
        }

        public Task<string> GetData()
        {
            var d = Session.Data;
            if (d == null) return Task.FromResult(Err("no playlist loaded"));
            var payload = new
            {
                login = new { kind = d.Login.Kind.ToString(), name = d.Login.Name, mac = Store.Settings.DeviceMac },
                liveCategories = WithCounts(d.LiveCategories, d.Channels.Select(c => c.CategoryId)),
                channels = d.Channels,
                vodCategories = WithCounts(d.VodCategories, d.Movies.Select(m => m.CategoryId)),
                movies = d.Movies,
                seriesCategories = WithCounts(d.SeriesCategories, d.Series.Select(s => s.CategoryId)),
                series = d.Series.Select(s => new
                {
                    id = s.Id, name = s.Name, categoryId = s.CategoryId, categoryName = s.CategoryName,
                    cover = s.Cover, rating = s.Rating, year = s.Year, plot = s.Plot, cast = s.Cast
                }),
                favorites = Store.Data.Favorites,
                recents = Store.Data.Recents,
                resume = Store.Data.Resume,
                lockedCategories = Store.Data.LockedCategories
            };
            return Task.FromResult(Ok(payload));
        }

        public Task<string> ToggleFavorite(string key)
        {
            Store.ToggleFavorite(key);
            return Task.FromResult(Ok(new { favorite = Store.IsFavorite(key) }));
        }

        public Task<string> AddRecent(string kind, string id, string name)
        {
            Store.AddRecent(kind, id, name);
            return Task.FromResult(Ok());
        }

        public Task<string> SaveResume(string key, double pos)
        {
            if (pos <= 0.01 || pos >= 0.99)
                Store.Data.Resume.Remove(key);
            else
                Store.Data.Resume[key] = (float)pos;
            Store.SaveData();
            return Task.FromResult(Ok());
        }

        // ---------------- EPG ----------------

        public async Task<string> ReloadEpg()
        {
            try
            {
                var url = Store.Settings.EpgUrl;
                if (string.IsNullOrWhiteSpace(url)) return Err("لم يتم إدخال رابط EPG/XMLTV في الإعدادات");
                Session.XmlTv = await PlaylistService.FetchXmlTvAsync(url.Trim());
                Session.XmlTvLoaded = true;
                return Ok(new { channels = Session.XmlTv.Count });
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        private async Task EnsureXmlTvAsync()
        {
            if (Session.XmlTvLoaded) return;
            Session.XmlTvLoaded = true;
            var url = Store.Settings.EpgUrl;
            if (!string.IsNullOrWhiteSpace(url))
                Session.XmlTv = await PlaylistService.FetchXmlTvAsync(url.Trim());
        }

        private static List<EpgProgram> FilterDay(List<EpgProgram> list, DateTimeOffset day)
        {
            var from = day.Date;
            var to = from.AddDays(1);
            return list.Where(p => p.Start >= from && p.Start < to)
                       .OrderBy(p => p.Start).ToList();
        }

        public async Task<string> GetShortEpg(string streamId, string epgId)
        {
            try
            {
                var d = Session.Data;
                if (d == null) return Err("no playlist loaded");
                if (d.EpgByChannel.TryGetValue(streamId, out var cached) && cached.Count > 0)
                    return Ok(cached.OrderBy(p => p.Start).Take(10).ToList());
                if (Session.Xtream != null)
                {
                    var list = await Session.Xtream.GetShortEpgAsync(streamId, 10);
                    if (list.Count > 0) d.EpgByChannel[streamId] = list;
                    return Ok(list);
                }
                await EnsureXmlTvAsync();
                var key = !string.IsNullOrEmpty(epgId) ? epgId : streamId;
                if (Session.XmlTv.TryGetValue(key, out var xml))
                {
                    var now = DateTimeOffset.Now;
                    return Ok(xml.Where(p => p.End > now.AddHours(-2)).OrderBy(p => p.Start).Take(10).ToList());
                }
                return Ok(new List<EpgProgram>());
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        public async Task<string> GetFullEpg(string streamId, string epgId, int dayOffset)
        {
            try
            {
                var d = Session.Data;
                if (d == null) return Err("no playlist loaded");
                var day = DateTimeOffset.Now.Date.AddDays(dayOffset);
                if (Session.Xtream != null)
                {
                    var cacheKey = streamId + "|full";
                    if (!d.EpgByChannel.TryGetValue(cacheKey, out var full) || full.Count == 0)
                    {
                        full = await Session.Xtream.GetFullEpgAsync(streamId);
                        d.EpgByChannel[cacheKey] = full;
                    }
                    return Ok(FilterDay(full, day));
                }
                await EnsureXmlTvAsync();
                var key = !string.IsNullOrEmpty(epgId) ? epgId : streamId;
                if (Session.XmlTv.TryGetValue(key, out var xml)) return Ok(FilterDay(xml, day));
                return Ok(new List<EpgProgram>());
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        public async Task<string> GetSeriesInfo(string seriesId)
        {
            try
            {
                var d = Session.Data;
                if (d == null) return Err("no playlist loaded");
                var serie = d.Series.FirstOrDefault(s => s.Id == seriesId);
                if (serie == null) return Err("series not found");
                if (Session.Xtream != null && serie.Episodes.Count == 0)
                    await Session.Xtream.GetSeriesInfoAsync(serie);
                return Ok(serie);
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        public Task<string> GetTimeshiftUrl(string streamId, string startIso, int durationMinutes)
        {
            if (Session.Xtream == null) return Task.FromResult(Err("timeshift متاح فقط مع اشتراك Xtream"));
            if (!DateTimeOffset.TryParse(startIso, out var start)) return Task.FromResult(Err("bad start time"));
            var url = Session.Xtream.TimeshiftUrl(streamId, start, Math.Max(1, durationMinutes));
            return Task.FromResult(Ok(new { url }));
        }
    }
}
