using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IBOPlayer
{
    public class Settings
    {
        public string ThemeName { get; set; } = "Purple";
        public int Volume { get; set; } = 80;
        public int NetworkCachingMs { get; set; } = 1000;
        public bool HardwareAccel { get; set; } = true;
        public string EpgUrl { get; set; } = "";
        public string Pin { get; set; } = "0000";
        public string DeviceMac { get; set; } = "";
        public string LastLoginId { get; set; } = "";
        public bool DualScreen { get; set; } = false;
    }

    public class UserData
    {
        public List<LoginProfile> Logins { get; set; } = new List<LoginProfile>();
        public List<string> Favorites { get; set; } = new List<string>();   // keys: "live:<id>" "vod:<id>" "series:<id>"
        public List<RecentItem> Recents { get; set; } = new List<RecentItem>();
        public Dictionary<string, float> Resume { get; set; } = new Dictionary<string, float>(); // key -> position 0..1
        public List<string> LockedCategories { get; set; } = new List<string>(); // category names
        public Dictionary<string, long> BootHistory { get; set; } = new Dictionary<string, long>(); // date -> boot ms
    }

    public static class Store
    {
        public static readonly string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IBOPlayer");

        private static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");
        private static readonly string DataPath = Path.Combine(AppDir, "data.json");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static Settings Settings { get; private set; } = new Settings();
        public static UserData Data { get; private set; } = new UserData();

        static Store()
        {
            try { Directory.CreateDirectory(AppDir); } catch { }
            Load();
            if (string.IsNullOrEmpty(Settings.DeviceMac))
            {
                var rnd = new Random();
                Settings.DeviceMac = string.Format("00:1A:79:{0:X2}:{1:X2}:{2:X2}",
                    rnd.Next(0, 256), rnd.Next(0, 256), rnd.Next(0, 256));
                SaveSettings();
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), JsonOpts) ?? new Settings();
            }
            catch { Settings = new Settings(); }
            try
            {
                if (File.Exists(DataPath))
                    Data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(DataPath), JsonOpts) ?? new UserData();
            }
            catch { Data = new UserData(); }
            if (Settings == null) Settings = new Settings();
            if (Data == null) Data = new UserData();
        }

        public static void SaveSettings()
        {
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, JsonOpts)); } catch { }
        }

        public static void SaveData()
        {
            try { File.WriteAllText(DataPath, JsonSerializer.Serialize(Data, JsonOpts)); } catch { }
        }

        public static bool IsFavorite(string key) => Data.Favorites.Contains(key);

        public static void ToggleFavorite(string key)
        {
            if (Data.Favorites.Contains(key)) Data.Favorites.Remove(key);
            else Data.Favorites.Add(key);
            SaveData();
        }

        public static void AddRecent(string kind, string id, string name)
        {
            Data.Recents.RemoveAll(r => r.Kind == kind && r.Id == id);
            Data.Recents.Insert(0, new RecentItem { Kind = kind, Id = id, Name = name, When = DateTime.Now });
            if (Data.Recents.Count > 30) Data.Recents.RemoveRange(30, Data.Recents.Count - 30);
            SaveData();
        }

        public static bool IsCategoryLocked(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return false;
            return Data.LockedCategories.Contains(categoryName);
        }

        public static string FavKey(string kind, string id) => kind + ":" + id;
    }
}
