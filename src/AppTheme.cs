using System;

namespace SpaceKatMonitor
{
    internal sealed class AppTheme
    {
        internal static readonly AppTheme[] Choices =
        {
            new AppTheme("teal", "青绿", "#008F87", "#17383F", "#E8F6F2", "#64B6A8"),
            new AppTheme("blue", "海蓝", "#236BB7", "#193654", "#EAF2FD", "#83ADDF"),
            new AppTheme("violet", "紫罗兰", "#7951B4", "#362850", "#F2ECFA", "#B49AD8"),
            new AppTheme("amber", "琥珀", "#9B591B", "#452E1B", "#FBF1E4", "#C69B6F")
        };

        internal readonly string Id, Name, Accent, Sidebar, Tint, Edge;

        private AppTheme(string id, string name, string accent, string sidebar, string tint, string edge)
        {
            Id = id; Name = name; Accent = accent; Sidebar = sidebar; Tint = tint; Edge = edge;
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
