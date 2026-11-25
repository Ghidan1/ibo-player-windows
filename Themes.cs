using System.Collections.Generic;
using System.Drawing;

namespace IBOPlayer
{
    public class Theme
    {
        public string Name { get; set; } = "";
        public Color Bg { get; set; }
        public Color PanelBg { get; set; }
        public Color CardBg { get; set; }
        public Color Text { get; set; }
        public Color Muted { get; set; }
        public Color Accent { get; set; }
        public Color AccentDark { get; set; }

        public static readonly List<Theme> All = new List<Theme>
        {
            new Theme { Name = "Purple", Bg = Color.FromArgb(0x12, 0x12, 0x2B), PanelBg = Color.FromArgb(0x1D, 0x1D, 0x38),
                CardBg = Color.FromArgb(0x23, 0x23, 0x3F), Text = Color.FromArgb(0xE8, 0xE8, 0xF0), Muted = Color.FromArgb(0x9A, 0x9A, 0xB0),
                Accent = Color.FromArgb(0x7B, 0x61, 0xFF), AccentDark = Color.FromArgb(0x5A, 0x3F, 0xD9) },
            new Theme { Name = "Blue", Bg = Color.FromArgb(0x0E, 0x1A, 0x2B), PanelBg = Color.FromArgb(0x16, 0x2A, 0x44),
                CardBg = Color.FromArgb(0x1B, 0x33, 0x52), Text = Color.FromArgb(0xE8, 0xEE, 0xF5), Muted = Color.FromArgb(0x8F, 0xA6, 0xBD),
                Accent = Color.FromArgb(0x21, 0x96, 0xF3), AccentDark = Color.FromArgb(0x15, 0x6F, 0xC0) },
            new Theme { Name = "Green", Bg = Color.FromArgb(0x0E, 0x1F, 0x1A), PanelBg = Color.FromArgb(0x15, 0x30, 0x28),
                CardBg = Color.FromArgb(0x1A, 0x3A, 0x31), Text = Color.FromArgb(0xE8, 0xF5, 0xEE), Muted = Color.FromArgb(0x8F, 0xB3, 0xA4),
                Accent = Color.FromArgb(0x00, 0xC8, 0x53), AccentDark = Color.FromArgb(0x00, 0x9A, 0x40) },
            new Theme { Name = "Red", Bg = Color.FromArgb(0x22, 0x10, 0x14), PanelBg = Color.FromArgb(0x33, 0x18, 0x1E),
                CardBg = Color.FromArgb(0x3D, 0x1E, 0x25), Text = Color.FromArgb(0xF5, 0xE8, 0xEA), Muted = Color.FromArgb(0xBD, 0x95, 0x9B),
                Accent = Color.FromArgb(0xFF, 0x3B, 0x30), AccentDark = Color.FromArgb(0xD6, 0x25, 0x1E) },
            new Theme { Name = "Orange", Bg = Color.FromArgb(0x22, 0x17, 0x0E), PanelBg = Color.FromArgb(0x33, 0x24, 0x16),
                CardBg = Color.FromArgb(0x3D, 0x2C, 0x1B), Text = Color.FromArgb(0xF5, 0xEE, 0xE6), Muted = Color.FromArgb(0xBD, 0xA3, 0x8F),
                Accent = Color.FromArgb(0xFF, 0x95, 0x00), AccentDark = Color.FromArgb(0xD6, 0x76, 0x00) },
            new Theme { Name = "Teal", Bg = Color.FromArgb(0x0E, 0x1E, 0x22), PanelBg = Color.FromArgb(0x15, 0x2F, 0x35),
                CardBg = Color.FromArgb(0x1A, 0x39, 0x3F), Text = Color.FromArgb(0xE6, 0xF4, 0xF5), Muted = Color.FromArgb(0x8F, 0xB3, 0xB8),
                Accent = Color.FromArgb(0x00, 0xBC, 0xD4), AccentDark = Color.FromArgb(0x00, 0x8F, 0xA3) },
        };

        public static Theme Current { get; private set; } = All[0];

        public static void Set(string name)
        {
            foreach (var t in All)
            {
                if (t.Name == name) { Current = t; return; }
            }
            Current = All[0];
        }
    }
}
