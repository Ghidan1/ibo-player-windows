using System;
using System.Collections.Generic;

namespace IBOPlayer
{
    public enum PlaylistKind { M3uUrl, M3uFile, Xtream, Mac, Demo }

    public class LoginProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public PlaylistKind Kind { get; set; } = PlaylistKind.Demo;
        public string M3uUrl { get; set; } = "";
        public string M3uPath { get; set; } = "";
        public string Server { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Mac { get; set; } = "";
    }

    public class Category
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public class Channel
    {
        public string Id { get; set; } = "";          // stream_id (Xtream) or tvg-id / name (M3U)
        public string Name { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string Logo { get; set; } = "";
        public string Url { get; set; } = "";
        public string EpgId { get; set; } = "";
        public string StreamId { get; set; } = "";    // Xtream numeric stream id (for EPG/catch-up)
        public bool IsDemo { get; set; }
    }

    public class VodItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string Cover { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Year { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Extension { get; set; } = "mp4";
        public string Url { get; set; } = "";
    }

    public class EpisodeItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int EpisodeNum { get; set; }
        public string Season { get; set; } = "1";
        public string Extension { get; set; } = "mp4";
        public string Url { get; set; } = "";
        public string Info { get; set; } = "";
    }

    public class SerieItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string Cover { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Year { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Cast { get; set; } = "";
        public Dictionary<string, List<EpisodeItem>> Episodes { get; set; } = new Dictionary<string, List<EpisodeItem>>();
    }

    public class EpgProgram
    {
        public string ChannelId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
    }

    public class PlaylistData
    {
        public LoginProfile Login { get; set; } = new LoginProfile();
        public List<Category> LiveCategories { get; set; } = new List<Category>();
        public List<Channel> Channels { get; set; } = new List<Channel>();
        public List<Category> VodCategories { get; set; } = new List<Category>();
        public List<VodItem> Movies { get; set; } = new List<VodItem>();
        public List<Category> SeriesCategories { get; set; } = new List<Category>();
        public List<SerieItem> Series { get; set; } = new List<SerieItem>();
        // EPG cache: key = channel EPG id (or stream id), value = programmes
        public Dictionary<string, List<EpgProgram>> EpgByChannel { get; set; } = new Dictionary<string, List<EpgProgram>>();
        public bool IsXtream => Login.Kind == PlaylistKind.Xtream;
    }

    public class RecentItem
    {
        public string Kind { get; set; } = "";   // live | vod | series
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime When { get; set; } = DateTime.Now;
    }
}
