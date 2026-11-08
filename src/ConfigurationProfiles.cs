using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SpaceKatMonitor
{
    internal static class ConfigurationProfiles
    {
        internal static readonly string[] Names =
        {
            "stick-settings.txt", "wheel-settings.txt", "mouse-settings.txt", "button-mapping.txt",
            "button-actions.txt", "gamepad-button-actions.txt", "theme-settings.txt"
        };
        private const int MaxTextLength = 8192;

        internal static bool TryParseMouseMotion(string text, out double speed, out int deadZone, out bool invertX, out bool invertY)
        {
            speed = 0; deadZone = 0; invertX = invertY = false;
            string[] fields = text.Trim().Split(',');
            return fields.Length == 4
                && Double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out speed)
                && speed >= 100 && speed <= 2400
                && Int32.TryParse(fields[1], out deadZone) && deadZone >= 0 && deadZone <= 100
                && Boolean.TryParse(fields[2], out invertX) && Boolean.TryParse(fields[3], out invertY);
        }

        internal static bool TryParseLogicalKeys(string text, out int[] keys)
        {
            string[] fields = text.Trim().Split(',');
            keys = new int[4];
            if (fields.Length != keys.Length) return false;
            for (int i = 0; i < keys.Length; i++)
                if (!Int32.TryParse(fields[i], out keys[i]) || keys[i] < 1 || keys[i] > 32) return false;
            return true;
        }

        internal static bool TryParseMouseActions(string text, out MouseButtonAction[] actions)
        {
            string[] fields = text.Trim().Split(',');
            actions = new MouseButtonAction[4];
            if (fields.Length != actions.Length) return false;
            for (int i = 0; i < actions.Length; i++)
                if (!Enum.TryParse(fields[i], out actions[i]) || !Enum.IsDefined(typeof(MouseButtonAction), actions[i])) return false;
            return true;
        }

        internal static bool IsValid(string name, string text)
        {
            switch (name)
            {
                case "stick-settings.txt":
                    JoystickSettings joystick;
                    return JoystickSettings.TryParse(text, out joystick);
                case "wheel-settings.txt":
                    MouseWheelSettings wheel;
                    return MouseWheelSettings.TryParse(text, out wheel);
                case "mouse-settings.txt":
                    double speed;
                    int deadZone;
                    bool x, y;
                    return TryParseMouseMotion(text, out speed, out deadZone, out x, out y);
                case "button-mapping.txt":
                    int[] keys;
                    return TryParseLogicalKeys(text, out keys);
                case "button-actions.txt":
                    MouseButtonAction[] mouse;
                    return TryParseMouseActions(text, out mouse);
                case "gamepad-button-actions.txt":
                    GamepadButtons[] gamepad;
                    return GamepadButtonModel.TryParse(text, out gamepad);
                case "theme-settings.txt":
                    return AppTheme.Find(text) != null;
                default:
                    return false;
            }
        }

        internal static Dictionary<string, string> ReadSelection(string[] paths)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (FileStream file = File.OpenRead(path))
                    using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
                    {
                        if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("配置 ZIP 太大。");
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string name = KnownName(Path.GetFileName(entry.FullName));
                            if (name == null) continue;
                            using (Stream stream = entry.Open()) ReadEntry(result, name, stream);
                        }
                    }
                }
                else
                {
                    string name = KnownName(Path.GetFileName(path));
                    if (name == null) throw new InvalidDataException("无法识别配置文件 " + Path.GetFileName(path));
                    using (FileStream stream = File.OpenRead(path)) ReadEntry(result, name, stream);
                }
            }
            if (result.Count == 0) throw new InvalidDataException("所选文件中没有 SpaceKat 配置。");
            return result;
        }

        private static string KnownName(string fileName)
        {
            return Array.Find(Names, name => String.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static void ReadEntry(Dictionary<string, string> result, string name, Stream stream)
        {
            if (result.ContainsKey(name)) throw new InvalidDataException("存在多个 " + name + "，请选择一份。");
            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
            {
                char[] buffer = new char[MaxTextLength + 1];
                int length = reader.ReadBlock(buffer, 0, buffer.Length);
                string text = new string(buffer, 0, length);
                if (length > MaxTextLength || !IsValid(name, text))
                    throw new InvalidDataException(name + " 的内容不符合 SpaceKat 配置格式。");
                result.Add(name, text);
            }
        }

        internal static void WriteArchive(string path, IDictionary<string, string> settings)
        {
            WriteAtomically(path, stream =>
            {
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    foreach (string name in Names)
                    {
                        string text;
                        if (!settings.TryGetValue(name, out text)) continue;
                        using (StreamWriter writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)))
                            writer.Write(text);
                    }
            });
        }

        internal static void Replace(string directory, IDictionary<string, string> settings)
        {
            Dictionary<string, byte[]> original = new Dictionary<string, byte[]>();
            List<string> changed = new List<string>();
            try
            {
                foreach (string name in Names)
                {
                    string text;
                    if (!settings.TryGetValue(name, out text)) continue;
                    string path = Path.Combine(directory, name);
                    original.Add(path, File.Exists(path) ? File.ReadAllBytes(path) : null);
                    WriteText(path, text);
                    changed.Add(path);
                }
            }
            catch (Exception error)
            {
                bool restored = true;
                for (int i = changed.Count - 1; i >= 0; i--)
                {
                    string path = changed[i];
                    byte[] contents = original[path];
                    try
                    {
                        if (contents == null) File.Delete(path);
                        else WriteAtomically(path, stream => stream.Write(contents, 0, contents.Length));
                    }
                    catch (IOException) { restored = false; }
                    catch (UnauthorizedAccessException) { restored = false; }
                }
                throw new IOException(restored ? "替换失败，原设置已恢复。" : "替换失败，部分文件未能恢复，请使用导入前的备份。", error);
            }
        }

        internal static void WriteText(string path, string text)
        {
            byte[] data = new UTF8Encoding(false).GetBytes(text);
            WriteAtomically(path, stream => stream.Write(data, 0, data.Length));
        }

        private static void WriteAtomically(string path, Action<Stream> write)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".spacekat-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    write(stream);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
