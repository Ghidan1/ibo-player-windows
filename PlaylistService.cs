using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace ZaghlolPlayer
{
    public static class PlaylistService
    {
        private static readonly HttpClient Http;

        static PlaylistService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            // Browser-like User-Agent: some IPTV panels / WAFs drop requests from unknown clients.
            Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        }

        /// <summary>Removes any credentials that might leak into an exception message (never show passwords).</summary>
        public static string CleanError(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return "خطأ غير معروف";
            var s = Regex.Replace(msg, @"(username|password)=[^&\s""']*", "$1=***", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"://[^\s""']*?@(?!$)", "://***@", RegexOptions.IgnoreCase);
            return s;
        }

        public sealed class FetchResult
        {
            public bool Ok;
            public string? Text;
            public string ErrorAr = "";
            public int? StatusCode;
            public long ElapsedMs;
            public long? ContentLength;
        }

        /// <summary>
        /// GET with an explicit hard timeout. Never returns null: on failure it returns a
        /// detailed Arabic error (HTTP status / timeout / empty reply) so the UI can show
        /// the REAL reason instead of a generic message.
        /// </summary>
        public static async Task<FetchResult> FetchTextAsync(string url, int timeoutSeconds, CancellationToken externalCt = default)
        {
            var sw = Stopwatch.StartNew();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return new FetchResult { ErrorAr = "الرابط غير صحيح — لازم يبدأ بـ http:// أو https://" };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
            try
            {
                using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    return new FetchResult
                    {
                        StatusCode = (int)resp.StatusCode, ElapsedMs = sw.ElapsedMilliseconds,
                        ErrorAr = $"السيرفر رد بالخطأ {(int)resp.StatusCode} — تأكد من صحة الرابط وبيانات الاشتراك"
                    };
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                return new FetchResult { Ok = true, Text = text, StatusCode = (int)resp.StatusCode, ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (OperationCanceledException)
            {
                return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = $"انتهت المهلة ({timeoutSeconds} ثانية) — السيرفر بطيء أو لا يرد" };
            }
            catch (HttpRequestException ex)
            {
                var msg = ex.Message ?? "";
                if (msg.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                    ex.InnerException is IOException)
                    return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = "السيرفر أغلق الاتصال بدون رد — تأكد من صحة الرابط وبيانات الاشتراك" };
                return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = "تعذر الاتصال بالسيرفر: " + CleanError(msg) };
            }
            catch (Exception ex)
            {
                return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = "خطأ أثناء التحميل: " + CleanError(ex.Message) };
            }
        }

        /// <summary>GET that reads only the first bytes — used by the connection test (no full download).</summary>
        public static async Task<FetchResult> FetchStartAsync(string url, int timeoutSeconds, int maxBytes)
        {
            var sw = Stopwatch.StartNew();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return new FetchResult { ErrorAr = "الرابط غير صحيح — لازم يبدأ بـ http:// أو https://" };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
            try
            {
                using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    return new FetchResult
                    {
                        StatusCode = (int)resp.StatusCode, ElapsedMs = sw.ElapsedMilliseconds,
                        ErrorAr = $"السيرفر رد بالخطأ {(int)resp.StatusCode} — تأكد من صحة الرابط وبيانات الاشتراك"
                    };
                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                var buf = new byte[maxBytes];
                int read = 0, n;
                while (read < maxBytes && (n = await stream.ReadAsync(buf, read, maxBytes - read, cts.Token)) > 0) read += n;
                var text = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                return new FetchResult
                {
                    Ok = true, Text = text, StatusCode = (int)resp.StatusCode,
                    ElapsedMs = sw.ElapsedMilliseconds, ContentLength = resp.Content.Headers.ContentLength
                };
            }
            catch (OperationCanceledException)
            {
                return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = $"انتهت المهلة ({timeoutSeconds} ثانية) — السيرفر بطيء أو لا يرد" };
            }
            catch (HttpRequestException ex)
            {
                var msg = ex.Message ?? "";
                if (msg.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                    ex.InnerException is IOException)
                    return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = "السيرفر أغلق الاتصال بدون رد — تأكد من صحة الرابط وبيانات الاشتراك" };
                return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = "تعذر الاتصال بالسيرفر: " + CleanError(msg) };
            }
            catch (Exception ex)
            {
                return new FetchResult { ElapsedMs = sw.ElapsedMilliseconds, ErrorAr = "خطأ أثناء التحميل: " + CleanError(ex.Message) };
            }
        }

        // ---------------- M3U ----------------

        public static PlaylistData ParseM3u(string text, LoginProfile login)
        {
            var data = new PlaylistData { Login = login };
            // Canonical group key: trimmed + case-insensitive. The FIRST-seen spelling
            // is the canonical category id, and every channel is resolved through this
            // map — so a channel's CategoryId can never disagree with its category's Id
            // (different casing / stray spaces in group-title used to hide channels).
            var liveCatMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var vodCatMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var serCatMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seriesMap = new Dictionary<string, SerieItem>(StringComparer.OrdinalIgnoreCase);
            string CanonicalGroup(Dictionary<string, string> map, string g)
            {
                g = (g ?? "").Trim();
                if (g.Length == 0) g = "All Channels";
                if (!map.TryGetValue(g, out var canon)) { canon = g; map[g] = canon; }
                return canon;
            }
            string pendingAttrs = "";
            string pendingName = "";
            string pendingExtGrp = "";
            bool pending = false;

            // Split on \r\n, \n AND lone \r (some panels emit old-Mac line endings).
            foreach (var rawLine in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = line.LastIndexOf(',');
                    pendingAttrs = comma > 0 ? line.Substring(0, comma) : line;
                    pendingName = comma > 0 && comma + 1 < line.Length ? line.Substring(comma + 1).Trim() : "";
                    pendingExtGrp = "";
                    pending = true;
                    continue;
                }
                // #EXTGRP:Category — some panels put the group here instead of group-title.
                if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
                {
                    pendingExtGrp = line.Substring("#EXTGRP:".Length).Trim();
                    continue;
                }
                if (line.StartsWith("#")) continue; // #EXTM3U, #EXTVLCOPT, etc.
                if (pending && (line.StartsWith("http", StringComparison.OrdinalIgnoreCase) || line.Contains("://")))
                {
                    var attrs = ParseAttrs(pendingAttrs);
                    string name = pendingName;
                    if (name.Length == 0 && attrs.TryGetValue("tvg-name", out var tn)) name = tn;
                    if (name.Length == 0) name = line;
                    string rawGroup = attrs.TryGetValue("group-title", out var g) && g.Length > 0 ? g : pendingExtGrp;
                    string logo = attrs.TryGetValue("tvg-logo", out var l) ? l : "";
                    string tvgId = attrs.TryGetValue("tvg-id", out var tid) ? tid : name;
                    // ---- classify the entry: live channel | VOD movie | series episode ----
                    // m3u_plus playlists from panels mix all three in one file, so the
                    // URL extension + name markers decide — otherwise Movies/Series
                    // tabs stay empty for M3U logins.
                    string urlPath = line.Split('?')[0];
                    string ext = "";
                    var dot = urlPath.LastIndexOf('.');
                    if (dot >= 0 && dot > urlPath.LastIndexOf('/')) ext = urlPath.Substring(dot + 1).ToLowerInvariant();
                    bool isVodExt = ext is "mp4" or "mkv" or "avi" or "mov" or "m4v" or "webm" or "flv";
                    string year = "";
                    var ym = Regex.Match(name, @"\((19|20)\d\d\)");
                    if (ym.Success) { year = ym.Value.Trim('(', ')'); name = name.Remove(ym.Index, ym.Length).Trim(); }
                    // Series markers: S01E02 / Season 1 Episode 2 / الموسم 1 الحلقة 2
                    int season = 0, epNum = 0;
                    string seriesName = "";
                    var se = Regex.Match(name, @"[Ss](\d{1,2})\s*[Ee](\d{1,3})");
                    if (se.Success)
                    {
                        season = int.Parse(se.Groups[1].Value); epNum = int.Parse(se.Groups[2].Value);
                        seriesName = name.Remove(se.Index, se.Length).Trim().TrimEnd('-', ':', '|').Trim();
                    }
                    else
                    {
                        var sm = Regex.Match(name, @"[Ss]eason\s*(\d+)", RegexOptions.IgnoreCase);
                        var em = Regex.Match(name, @"[Ee]pisode\s*(\d+)", RegexOptions.IgnoreCase);
                        var am = Regex.Match(name, @"الموسم\s*(\d+)");
                        var ae = Regex.Match(name, @"الحلقة\s*(\d+)");
                        if (sm.Success) season = int.Parse(sm.Groups[1].Value);
                        if (am.Success) season = int.Parse(am.Groups[1].Value);
                        if (em.Success) epNum = int.Parse(em.Groups[1].Value);
                        if (ae.Success) epNum = int.Parse(ae.Groups[1].Value);
                        if (season > 0 || epNum > 0)
                        {
                            seriesName = name;
                            foreach (var rx in new[] { @"[Ss]eason\s*\d+", @"[Ee]pisode\s*\d+", @"الموسم\s*\d+", @"الحلقة\s*\d+" })
                                seriesName = Regex.Replace(seriesName, rx, "", RegexOptions.IgnoreCase).Trim();
                            seriesName = seriesName.TrimEnd('-', ':', '|').Trim();
                        }
                    }
                    bool groupSaysSeries = (rawGroup ?? "").IndexOf("مسلسل", StringComparison.OrdinalIgnoreCase) >= 0
                        || (rawGroup ?? "").IndexOf("series", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (seriesName.Length == 0 && groupSaysSeries && isVodExt)
                    {
                        // VOD entry inside a series group but with no episode marker:
                        // keep it as a series with a single season.
                        seriesName = name; season = 1; epNum = 0;
                    }
                    if (seriesName.Length > 0)
                    {
                        string group = CanonicalGroup(serCatMap, rawGroup);
                        string key = seriesName.ToLowerInvariant();
                        if (!seriesMap.TryGetValue(key, out var serie))
                        {
                            serie = new SerieItem { Id = seriesName, Name = seriesName, CategoryId = group, CategoryName = group, Cover = logo, Year = year };
                            seriesMap[key] = serie;
                            data.Series.Add(serie);
                        }
                        if (logo.Length > 0 && string.IsNullOrEmpty(serie.Cover)) serie.Cover = logo;
                        string sn = season > 0 ? season.ToString() : "1";
                        if (!serie.Episodes.TryGetValue(sn, out var eps)) { eps = new List<EpisodeItem>(); serie.Episodes[sn] = eps; }
                        eps.Add(new EpisodeItem
                        {
                            Id = tvgId, Title = name, EpisodeNum = epNum, Season = sn,
                            Extension = ext.Length > 0 ? ext : "mp4", Url = line, Info = ""
                        });
                    }
                    else if (isVodExt)
                    {
                        string group = CanonicalGroup(vodCatMap, rawGroup);
                        data.Movies.Add(new VodItem
                        {
                            Id = tvgId, Name = name, CategoryId = group, CategoryName = group,
                            Cover = logo, Year = year, Extension = ext, Url = line
                        });
                    }
                    else
                    {
                        string group = CanonicalGroup(liveCatMap, rawGroup);
                        data.Channels.Add(new Channel
                        {
                            Id = tvgId,
                            Name = name,
                            CategoryId = group,
                            CategoryName = group,
                            Logo = logo,
                            Url = line,
                            EpgId = tvgId,
                            StreamId = tvgId
                        });
                    }
                    pending = false;
                }
            }

            // Categories in the playlist's OWN order (first appearance), NOT alphabetical.
            // "الكل" (All) is prepended by the UI; this list must keep a stable order
            // every time the playlist loads.
            foreach (var kv in liveCatMap)
                data.LiveCategories.Add(new Category { Id = kv.Value, Name = kv.Value });
            foreach (var kv in vodCatMap)
                data.VodCategories.Add(new Category { Id = kv.Value, Name = kv.Value });
            foreach (var kv in serCatMap)
                data.SeriesCategories.Add(new Category { Id = kv.Value, Name = kv.Value });
            // Episodes arrive in playlist order — sort each season by episode number.
            foreach (var s in data.Series)
                foreach (var eps in s.Episodes.Values)
                    eps.Sort((a, b) => a.EpisodeNum.CompareTo(b.EpisodeNum));
            return data;
        }

        private static Dictionary<string, string> ParseAttrs(string extinf)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(extinf, "([A-Za-z0-9_-]+)=\"([^\"]*)\""))
                d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }

        // ---------------- Demo playlist (free public streams, verified working) ----------------

        public static PlaylistData DemoPlaylist()
        {
            var login = new LoginProfile { Name = "Demo Channels", Kind = PlaylistKind.Demo };
            var data = new PlaylistData { Login = login };
            data.LiveCategories.Add(new Category { Id = "Demo", Name = "Demo Channels" });
            var demo = new[]
            {
                new { Name = "Al Arabiya", Url = "https://live.alarabiya.net/alarabiapublish/alarabiya.smil/playlist.m3u8" },
                new { Name = "Al Jazeera", Url = "https://live-hls-web-aja.getaj.net/AJA/index.m3u8" },
                new { Name = "Red Bull TV", Url = "https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master.m3u8" },
                new { Name = "DW (Deutsche Welle)", Url = "https://dwamdstream102.akamaized.net/hls/live/2015525/dwstream102/index.m3u8" },
                new { Name = "Test Stream (Mux)", Url = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8" },
            };
            foreach (var d in demo)
            {
                data.Channels.Add(new Channel
                {
                    Id = d.Name, Name = d.Name, CategoryId = "Demo", CategoryName = "Demo Channels",
                    Url = d.Url, EpgId = d.Name, StreamId = d.Name, IsDemo = true
                });
            }
            return data;
        }

        // ---------------- Xtream Codes ----------------

        public class XtreamClient
        {
            private readonly string _server;
            private readonly string _user;
            private readonly string _pass;

            public XtreamClient(string server, string user, string pass)
            {
                _server = server.TrimEnd('/');
                _user = user;
                _pass = pass;
            }

            public string LiveUrl(string streamId, bool m3u8 = true)
                => $"{_server}/live/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{streamId}.{(m3u8 ? "m3u8" : "ts")}";

            public string MovieUrl(string streamId, string ext)
                => $"{_server}/movie/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{streamId}.{ext}";

            public string SeriesUrl(string episodeId, string ext)
                => $"{_server}/series/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{episodeId}.{ext}";

            public string TimeshiftUrl(string streamId, DateTimeOffset start, int durationMinutes)
                => $"{_server}/streaming/timeshift.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}&stream={streamId}&start={start:yyyy-MM-dd:HH-mm-ss}&duration={durationMinutes}";

            private string ApiUrl(string action, string extra = "")
            {
                var url = $"{_server}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}";
                if (!string.IsNullOrEmpty(action)) url += "&action=" + action;
                if (!string.IsNullOrEmpty(extra)) url += "&" + extra;
                return url;
            }

            private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
            {
                try
                {
                    var r = await FetchTextAsync(url, 15, ct);
                    if (!r.Ok || string.IsNullOrWhiteSpace(r.Text)) return null;
                    return JsonDocument.Parse(r.Text);
                }
                catch { return null; }
            }

            public async Task<bool> AuthenticateAsync(CancellationToken ct = default)
            {
                var (ok, _) = await AuthenticateDetailedAsync(ct);
                return ok;
            }

            /// <summary>Auth with a detailed Arabic reason on failure (wrong creds / timeout / HTTP error).</summary>
            public async Task<(bool Ok, string ErrorAr)> AuthenticateDetailedAsync(CancellationToken ct = default)
            {
                var r = await FetchTextAsync(ApiUrl(""), 15, ct);
                if (!r.Ok) return (false, r.ErrorAr);
                try
                {
                    using var doc = JsonDocument.Parse(r.Text ?? "");
                    var root = doc.RootElement;
                    if (root.TryGetProperty("user_info", out var ui) && ui.ValueKind == JsonValueKind.Object)
                    {
                        if (ui.TryGetProperty("auth", out var auth) && auth.ValueKind == JsonValueKind.Number && auth.GetInt32() == 0)
                            return (false, "بيانات الدخول غير صحيحة أو الاشتراك منتهي");
                        if (ui.TryGetProperty("status", out var st))
                            return st.GetString() == "Active"
                                ? (true, "")
                                : (false, "الحساب غير نشط (الحالة: " + (st.GetString() ?? "?") + ") — راجع مزوّد الخدمة");
                        return (true, "");
                    }
                    return (false, "السيرفر ردّ برد غير متوقع — تأكد من رابط السيرفر");
                }
                catch
                {
                    return (false, "السيرفر ردّ برد غير صالح (ليس JSON) — تأكد من رابط السيرفر");
                }
            }

            private static string Str(JsonElement el, string name)
            {
                if (el.ValueKind != JsonValueKind.Object) return "";
                if (!el.TryGetProperty(name, out var v)) return "";
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
                return "";
            }

            private async Task<List<Category>> GetCategoriesAsync(string action, CancellationToken ct)
            {
                var list = new List<Category>();
                using var doc = await GetJsonAsync(ApiUrl(action), ct);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    list.Add(new Category
                    {
                        Id = Str(el, "category_id"),
                        Name = Str(el, "category_name")
                    });
                }
                return list;
            }

            public Task<List<Category>> GetLiveCategoriesAsync(CancellationToken ct = default) => GetCategoriesAsync("get_live_categories", ct);
            public Task<List<Category>> GetVodCategoriesAsync(CancellationToken ct = default) => GetCategoriesAsync("get_vod_categories", ct);
            public Task<List<Category>> GetSeriesCategoriesAsync(CancellationToken ct = default) => GetCategoriesAsync("get_series_categories", ct);

            public async Task<List<Channel>> GetLiveStreamsAsync(CancellationToken ct = default)
            {
                var list = new List<Channel>();
                using var doc = await GetJsonAsync(ApiUrl("get_live_streams"), ct);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var id = Str(el, "stream_id");
                    list.Add(new Channel
                    {
                        Id = id,
                        StreamId = id,
                        Name = Str(el, "name"),
                        CategoryId = Str(el, "category_id"),
                        Logo = Str(el, "stream_icon"),
                        EpgId = Str(el, "epg_channel_id"),
                        Url = LiveUrl(id)
                    });
                }
                return list;
            }

            public async Task<List<VodItem>> GetVodStreamsAsync(CancellationToken ct = default)
            {
                var list = new List<VodItem>();
                using var doc = await GetJsonAsync(ApiUrl("get_vod_streams"), ct);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var id = Str(el, "stream_id");
                    var ext = Str(el, "container_extension");
                    if (ext.Length == 0) ext = "mp4";
                    var rating = Str(el, "rating");
                    if (rating.Length == 0 && el.TryGetProperty("rating_5based", out var r5))
                        rating = r5.ValueKind == JsonValueKind.Number ? r5.GetRawText() : "";
                    list.Add(new VodItem
                    {
                        Id = id,
                        Name = Str(el, "name"),
                        CategoryId = Str(el, "category_id"),
                        Cover = Str(el, "cover"),
                        Rating = rating,
                        Year = Str(el, "year"),
                        Plot = Str(el, "plot"),
                        Extension = ext,
                        Url = MovieUrl(id, ext)
                    });
                }
                return list;
            }

            public async Task<List<SerieItem>> GetSeriesAsync(CancellationToken ct = default)
            {
                var list = new List<SerieItem>();
                using var doc = await GetJsonAsync(ApiUrl("get_series"), ct);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    list.Add(new SerieItem
                    {
                        Id = Str(el, "series_id"),
                        Name = Str(el, "name"),
                        CategoryId = Str(el, "category_id"),
                        Cover = Str(el, "cover"),
                        Rating = Str(el, "rating"),
                        Year = Str(el, "year"),
                        Plot = Str(el, "plot"),
                        Cast = Str(el, "cast")
                    });
                }
                return list;
            }

            public async Task GetSeriesInfoAsync(SerieItem serie, CancellationToken ct = default)
            {
                using var doc = await GetJsonAsync(ApiUrl("get_series_info", "series_id=" + serie.Id), ct);
                if (doc == null) return;
                var root = doc.RootElement;
                if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
                {
                    var plot = Str(info, "plot");
                    if (plot.Length > 0) serie.Plot = plot;
                    var cast = Str(info, "cast");
                    if (cast.Length > 0) serie.Cast = cast;
                    var rating = Str(info, "rating");
                    if (rating.Length > 0) serie.Rating = rating;
                    var year = Str(info, "year");
                    if (year.Length > 0) serie.Year = year;
                    var cover = Str(info, "cover");
                    if (cover.Length > 0) serie.Cover = cover;
                }
                if (!root.TryGetProperty("episodes", out var eps) || eps.ValueKind != JsonValueKind.Object) return;
                foreach (var season in eps.EnumerateObject())
                {
                    var list = new List<EpisodeItem>();
                    if (season.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var ep in season.Value.EnumerateArray())
                    {
                        var epId = Str(ep, "id");
                        var ext = Str(ep, "container_extension");
                        if (ext.Length == 0) ext = "mp4";
                        int epNum = 0;
                        int.TryParse(Str(ep, "episode_num"), out epNum);
                        string infoText = "";
                        if (ep.TryGetProperty("info", out var epi) && epi.ValueKind == JsonValueKind.Object)
                            infoText = Str(epi, "plot");
                        list.Add(new EpisodeItem
                        {
                            Id = epId,
                            Title = Str(ep, "title"),
                            EpisodeNum = epNum,
                            Season = season.Name,
                            Extension = ext,
                            Url = SeriesUrl(epId, ext),
                            Info = infoText
                        });
                    }
                    list.Sort((a, b) => a.EpisodeNum.CompareTo(b.EpisodeNum));
                    serie.Episodes[season.Name] = list;
                }
            }

            // Short EPG (now/next) for one stream
            public async Task<List<EpgProgram>> GetShortEpgAsync(string streamId, int limit = 10, CancellationToken ct = default)
            {
                var list = new List<EpgProgram>();
                using var doc = await GetJsonAsync(ApiUrl("get_short_epg", "stream_id=" + streamId + "&limit=" + limit), ct);
                if (doc == null) return list;
                var root = doc.RootElement;
                if (!root.TryGetProperty("epg_listings", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in arr.EnumerateArray())
                    list.Add(ParseEpgListing(el, streamId));
                return list;
            }

            // Full EPG table for one stream (used by the 7-day guide)
            public async Task<List<EpgProgram>> GetFullEpgAsync(string streamId, CancellationToken ct = default)
            {
                var list = new List<EpgProgram>();
                using var doc = await GetJsonAsync(ApiUrl("get_simple_data_table", "stream_id=" + streamId), ct);
                if (doc == null) return list;
                var root = doc.RootElement;
                if (!root.TryGetProperty("epg_listings", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in arr.EnumerateArray())
                    list.Add(ParseEpgListing(el, streamId));
                return list;
            }

            private static EpgProgram ParseEpgListing(JsonElement el, string channelId)
            {
                var p = new EpgProgram { ChannelId = channelId };
                p.Title = Str(el, "title");
                if (p.Title.Length == 0) p.Title = Str(el, "name");
                p.Description = Str(el, "description");
                long startTs = 0, endTs = 0;
                long.TryParse(Str(el, "start_timestamp"), out startTs);
                long.TryParse(Str(el, "stop_timestamp"), out endTs);
                if (startTs > 0) p.Start = DateTimeOffset.FromUnixTimeSeconds(startTs);
                if (endTs > 0) p.End = DateTimeOffset.FromUnixTimeSeconds(endTs);
                return p;
            }
        }

        // ---------------- XMLTV (for M3U playlists) ----------------

        public static async Task<Dictionary<string, List<EpgProgram>>> FetchXmlTvAsync(string url)
        {
            var result = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
            var r = await FetchTextAsync(url, 20);
            if (r.Ok && !string.IsNullOrEmpty(r.Text))
                ParseXmlTv(r.Text, result);
            return result;
        }

        public static void ParseXmlTv(string xml, Dictionary<string, List<EpgProgram>> result)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var nodes = doc.SelectNodes("//programme");
                if (nodes == null) return;
                foreach (XmlNode n in nodes)
                {
                    var ch = n.Attributes?["channel"]?.Value ?? "";
                    var start = ParseXmlTvTime(n.Attributes?["start"]?.Value);
                    var stop = ParseXmlTvTime(n.Attributes?["stop"]?.Value);
                    if (ch.Length == 0 || start == null) continue;
                    var title = n.SelectSingleNode("title")?.InnerText ?? "";
                    var desc = n.SelectSingleNode("desc")?.InnerText ?? "";
                    if (!result.TryGetValue(ch, out var list))
                    {
                        list = new List<EpgProgram>();
                        result[ch] = list;
                    }
                    list.Add(new EpgProgram
                    {
                        ChannelId = ch,
                        Title = title,
                        Description = desc,
                        Start = start.Value,
                        End = stop ?? start.Value.AddHours(1)
                    });
                }
            }
            catch { }
        }

        private static DateTimeOffset? ParseXmlTvTime(string? s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            // "20261125200000 +0300"
            var parts = s.Trim().Split(' ');
            if (!DateTime.TryParseExact(parts[0], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt)) return null;
            var offset = TimeSpan.Zero;
            if (parts.Length > 1)
            {
                var o = parts[1];
                if (o.Length == 5 && (o[0] == '+' || o[0] == '-'))
                {
                    int sign = o[0] == '-' ? -1 : 1;
                    if (int.TryParse(o.Substring(1, 2), out var hh) && int.TryParse(o.Substring(3, 2), out var mm))
                        offset = new TimeSpan(sign * hh, sign * mm, 0);
                }
            }
            return new DateTimeOffset(dt, offset);
        }

        public static async Task<string?> DownloadTextAsync(string url)
        {
            var r = await FetchTextAsync(url, 30);
            return r.Ok ? r.Text : null;
        }
    }
}
