using System;
using System.Text;

namespace SpaceKatMonitor
{
    internal sealed class ButtonLayout
    {
        internal const string FileName = "button-layout.txt";
        internal const int Count = 4;
        internal const int MaxNameLength = 32;
        internal static readonly ButtonLayout Default = CreateDefault();
        private readonly int[] order;
        private readonly string[] names;

        private ButtonLayout(int[] order, string[] names)
        {
            this.order = order;
            this.names = names;
        }

        private static ButtonLayout CreateDefault()
        {
            ButtonLayout layout;
            TryCreate(new[] { 0, 1, 2, 3 }, new string[Count], out layout);
            return layout;
        }

        internal string Name(int slot) { return names[slot]; }
        internal int SlotAt(int position) { return order[position]; }

        internal static bool TryCreate(int[] order, string[] names, out ButtonLayout layout)
        {
            layout = null;
            if (order == null || names == null || order.Length != Count || names.Length != Count) return false;
            int[] copiedOrder = new int[Count];
            string[] copiedNames = new string[Count];
            bool[] seen = new bool[Count];
            for (int i = 0; i < Count; i++)
            {
                int slot = order[i];
                if (slot < 0 || slot >= Count || seen[slot]) return false;
                seen[slot] = true;
                copiedOrder[i] = slot;
                string name = names[i] ?? String.Empty;
                for (int j = 0; j < name.Length; j++)
                {
                    char c = name[j];
                    if (Char.IsControl(c) || c == '\u2028' || c == '\u2029') return false;
                    if (Char.IsHighSurrogate(c))
                    {
                        if (j + 1 >= name.Length || !Char.IsLowSurrogate(name[j + 1])) return false;
                        j++;
                    }
                    else if (Char.IsLowSurrogate(c)) return false;
                }
                name = name.Trim();
                if (name.Length > MaxNameLength) return false;
                copiedNames[i] = name.Length == 0 ? "按键 " + (i + 1) : name;
            }
            layout = new ButtonLayout(copiedOrder, copiedNames);
            return true;
        }

        internal static bool TryParse(string text, out ButtonLayout layout)
        {
            layout = null;
            if (text == null) return false;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int length = lines.Length;
            if (length == Count + 1 && lines[Count].Length == 0) length--;
            if (length != Count) return false;
            int[] order = new int[Count];
            string[] names = new string[Count];
            for (int i = 0; i < Count; i++)
            {
                string line = lines[i];
                if (line.Length < 2 || line[0] < '1' || line[0] > '4' || line[1] != '\t') return false;
                int slot = line[0] - '1';
                order[i] = slot;
                names[slot] = line.Substring(2);
            }
            return TryCreate(order, names, out layout);
        }

        internal string Serialize()
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < Count; i++)
                result.Append(order[i] + 1).Append('\t').Append(names[order[i]]).Append('\n');
            return result.ToString();
        }
    }
}
