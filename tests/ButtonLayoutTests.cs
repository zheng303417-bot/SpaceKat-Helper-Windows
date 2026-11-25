using System;
using System.Collections.Generic;
using System.IO;

namespace SpaceKatMonitor
{
    internal static class ButtonLayoutTests
    {
        public static string Run()
        {
            int count = 0;
            ButtonLayout layout;
            Check(ButtonLayout.TryParse(ButtonLayout.Default.Serialize(), out layout), ref count);
            for (int i = 0; i < ButtonLayout.Count; i++)
                Check(layout.SlotAt(i) == i && layout.Name(i) == "按键 " + (i + 1), ref count);
            int[] order = { 2, 0, 3, 1 };
            string[] names = { " 中文,名称 ", "", "🚀", "  " };
            Check(ButtonLayout.TryCreate(order, names, out layout), ref count);
            order[0] = 0;
            names[0] = "changed";
            Check(layout.SlotAt(0) == 2 && layout.Name(0) == "中文,名称" && layout.Name(1) == "按键 2", ref count);
            ButtonLayout parsed;
            Check(ButtonLayout.TryParse(layout.Serialize(), out parsed) && parsed.SlotAt(0) == 2
                && parsed.Name(0) == "中文,名称" && parsed.Name(2) == "🚀", ref count);
            Check(!ButtonLayout.TryCreate(new[] { 0, 1, 1, 3 }, new string[4], out parsed)
                && !ButtonLayout.TryCreate(new[] { -1, 1, 2, 3 }, new string[4], out parsed)
                && !ButtonLayout.TryCreate(new[] { 0, 1, 2, 4 }, new string[4], out parsed), ref count);
            Check(!ButtonLayout.TryParse("1\ta\n1\tb\n3\tc\n4\td\n", out parsed)
                && !ButtonLayout.TryParse("1\ta\n2\tb\n3\tc\n", out parsed)
                && !ButtonLayout.TryParse("1\ta\n2\tb\n3\tc\n4\td\n\n", out parsed), ref count);
            Check(!ButtonLayout.TryCreate(new[] { 0, 1, 2, 3 }, new[] { new string('x', 33), "", "", "" }, out parsed)
                && !ButtonLayout.TryCreate(new[] { 0, 1, 2, 3 }, new[] { "\ud800", "", "", "" }, out parsed)
                && !ButtonLayout.TryCreate(new[] { 0, 1, 2, 3 }, new[] { "a\tb", "", "", "" }, out parsed), ref count);
            Check(ConfigurationProfiles.IsValid(ButtonLayout.FileName, layout.Serialize())
                && !ConfigurationProfiles.IsValid(ButtonLayout.FileName, "bad"), ref count);

            string archive = Path.Combine(Path.GetTempPath(), "spacekat-layout-test-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();
                settings.Add(ButtonLayout.FileName, layout.Serialize());
                ConfigurationProfiles.WriteArchive(archive, settings);
                Dictionary<string, string> imported = ConfigurationProfiles.ReadSelection(new[] { archive });
                Check(imported[ButtonLayout.FileName] == layout.Serialize(), ref count);
                settings.Clear();
                settings.Add("button-mapping.txt", "1,2,3,4");
                ConfigurationProfiles.WriteArchive(archive, settings);
                imported = ConfigurationProfiles.ReadSelection(new[] { archive });
                Check(imported.ContainsKey("button-mapping.txt") && !imported.ContainsKey(ButtonLayout.FileName), ref count);
            }
            finally { if (File.Exists(archive)) File.Delete(archive); }
            return "PASS: " + count + " button layout checks\r\n";
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Button layout assertion " + count + " failed.");
        }
    }
}
