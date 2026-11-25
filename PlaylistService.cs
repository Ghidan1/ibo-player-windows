using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace IBOPlayer
{
    public static class PlaylistService
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static PlaylistService()
        {
            Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "IBOPlayer/1.0");
        }

        // ---------------- M3U ----------------

        public static PlaylistData ParseM3u(string text, LoginProfile login)
        {
            var data = new PlaylistData { Login = login };
            var catMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string pendingAttrs = "";
            string pendingName = "";
            bool pending = false;

            foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = line.LastIndexOf(',');
                    pendingAttrs = comma > 0 ? line.Substring(0, comma) : line;
                    pendingName = comma > 0 && comma + 1 < line.Length ? line.Substring(comma + 1).Trim() : "";
                    pending = true;
                    continue;
                }
                if (line.StartsWith("#")) continue; // #EXTM3U, #EXTGRP, etc.
                if (pending && (line.StartsWith("http", StringComparison.OrdinalIgnoreCase) || line.Contains("://")))
                {
                    var attrs = ParseAttrs(pendingAttrs);
                    string name = pendingName;
                    if (name.Length == 0 && attrs.TryGetValue("tvg-name", out var tn)) name = tn;
                    if (name.Length == 0) name = line;
                    string group = attrs.TryGetValue("group-title", out var g) && g.Length > 0 ? g : "All Channels";
                    string logo = attrs.TryGetValue("tvg-logo", out var l) ? l : "";
                    string tvgId = attrs.TryGetValue("tvg-id", out var tid) ? tid : name;
                    if (!catMap.ContainsKey(group)) catMap[group] = group;
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
                    pending = false;
                }
            }

            foreach (var kv in catMap)
                data.LiveCategories.Add(new Category { Id = kv.Key, Name = kv.Key });
            data.LiveCategories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
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

            private async Task<JsonDocument?> GetJsonAsync(string url)
            {
                try
                {
                    var json = await Http.GetStringAsync(url);
                    if (string.IsNullOrWhiteSpace(json)) return null;
                    return JsonDocument.Parse(json);
                }
                catch { return null; }
            }

            public async Task<bool> AuthenticateAsync()
            {
                using var doc = await GetJsonAsync(ApiUrl(""));
                if (doc == null) return false;
                var root = doc.RootElement;
                if (root.TryGetProperty("user_info", out var ui) && ui.ValueKind == JsonValueKind.Object)
                {
                    if (ui.TryGetProperty("status", out var st))
                        return st.GetString() == "Active";
                    return true;
                }
                return false;
            }

            private static string Str(JsonElement el, string name)
            {
                if (el.ValueKind != JsonValueKind.Object) return "";
                if (!el.TryGetProperty(name, out var v)) return "";
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
                return "";
            }

            private async Task<List<Category>> GetCategoriesAsync(string action)
            {
                var list = new List<Category>();
                using var doc = await GetJsonAsync(ApiUrl(action));
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

            public Task<List<Category>> GetLiveCategoriesAsync() => GetCategoriesAsync("get_live_categories");
            public Task<List<Category>> GetVodCategoriesAsync() => GetCategoriesAsync("get_vod_categories");
            public Task<List<Category>> GetSeriesCategoriesAsync() => GetCategoriesAsync("get_series_categories");

            public async Task<List<Channel>> GetLiveStreamsAsync()
            {
                var list = new List<Channel>();
                using var doc = await GetJsonAsync(ApiUrl("get_live_streams"));
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

            public async Task<List<VodItem>> GetVodStreamsAsync()
            {
                var list = new List<VodItem>();
                using var doc = await GetJsonAsync(ApiUrl("get_vod_streams"));
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

            public async Task<List<SerieItem>> GetSeriesAsync()
            {
                var list = new List<SerieItem>();
                using var doc = await GetJsonAsync(ApiUrl("get_series"));
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

            public async Task GetSeriesInfoAsync(SerieItem serie)
            {
                using var doc = await GetJsonAsync(ApiUrl("get_series_info", "series_id=" + serie.Id));
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
            public async Task<List<EpgProgram>> GetShortEpgAsync(string streamId, int limit = 10)
            {
                var list = new List<EpgProgram>();
                using var doc = await GetJsonAsync(ApiUrl("get_short_epg", "stream_id=" + streamId + "&limit=" + limit));
                if (doc == null) return list;
                var root = doc.RootElement;
                if (!root.TryGetProperty("epg_listings", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in arr.EnumerateArray())
                    list.Add(ParseEpgListing(el, streamId));
                return list;
            }

            // Full EPG table for one stream (used by the 7-day guide)
            public async Task<List<EpgProgram>> GetFullEpgAsync(string streamId)
            {
                var list = new List<EpgProgram>();
                using var doc = await GetJsonAsync(ApiUrl("get_simple_data_table", "stream_id=" + streamId));
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
            try
            {
                var xml = await Http.GetStringAsync(url);
                ParseXmlTv(xml, result);
            }
            catch { }
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
            try { return await Http.GetStringAsync(url); }
            catch { return null; }
        }
    }
}
