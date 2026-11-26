using System;

namespace SpaceKatMonitor
{
    internal sealed class AppTheme
    {
        // Accent families tuned for the warm ceramic surfaces. Text is the darker
        // step that keeps small accent text readable; OnAccent is the label colour
        // that keeps at least 4.5:1 contrast on the accent fill.
        internal static readonly AppTheme[] Choices =
        {
            new AppTheme("orange", "陶土橙", "#F2661B", "#DC5410", "#FDEDE3", "#F6B08A", "#B4480C", "#FF7A33", "#1F1E1C"),
            new AppTheme("teal", "青绿", "#0F8574", "#0A6E60", "#E1F1ED", "#86C9BB", "#0C6E60", "#1E9C88", "#FFFFFF"),
            new AppTheme("blue", "海蓝", "#2F6BD8", "#2156BD", "#E8EEFB", "#9CB8EC", "#2456B8", "#4683EA", "#FFFFFF"),
            new AppTheme("violet", "紫罗兰", "#7A55C9", "#6542B2", "#F0EAFA", "#BFA9E8", "#6343AF", "#8F6BDC", "#FFFFFF"),
            new AppTheme("amber", "琥珀", "#C98714", "#AE710B", "#FAF1DC", "#E4C27F", "#8A5A08", "#DD9B25", "#1F1E1C")
        };

        internal readonly string Id, Name, Accent, Deep, Tint, Edge, Text, Light, OnAccent;

        private AppTheme(string id, string name, string accent, string deep, string tint, string edge, string text, string light, string onAccent)
        {
            Id = id; Name = name; Accent = accent; Deep = deep; Tint = tint; Edge = edge; Text = text; Light = light; OnAccent = onAccent;
        }

        internal static AppTheme Find(string text)
        {
            if (text == null) return null;
            string id = text.Trim();
            foreach (AppTheme theme in Choices)
                if (String.Equals(id, theme.Id, StringComparison.OrdinalIgnoreCase)) return theme;
            return null;
        }
    }
}
