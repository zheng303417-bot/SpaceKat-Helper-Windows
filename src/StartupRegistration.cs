using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SpaceKatMonitor
{
    internal sealed class StartupRegistration
    {
        private readonly string startupDirectory, launcher, shortcut;

        internal StartupRegistration(string startupDirectory, string appDirectory)
        {
            this.startupDirectory = startupDirectory;
            launcher = Path.Combine(appDirectory, "启动上位机.cmd");
            shortcut = Path.Combine(startupDirectory, "SpaceKat 小助手.lnk");
        }

        internal string RegisteredTarget()
        {
            if (String.IsNullOrEmpty(startupDirectory) || !File.Exists(shortcut)) return null;
            return (string)WithShortcut(shortcut, link => Invoke(link, "TargetPath", BindingFlags.GetProperty));
        }

        internal bool Matches(string target)
        {
            return String.Equals(target, launcher, StringComparison.OrdinalIgnoreCase);
        }

        internal void SetEnabled(bool enabled)
        {
            if (String.IsNullOrEmpty(startupDirectory)) throw new IOException("Windows 未提供当前用户的启动文件夹。");
            if (!enabled)
            {
                if (Matches(RegisteredTarget())) File.Delete(shortcut);
                return;
            }
            if (!File.Exists(launcher)) throw new FileNotFoundException("请保留同目录的「启动上位机.cmd」。", launcher);
            Directory.CreateDirectory(startupDirectory);
            // Write the shortcut outside the Startup folder, then replace the known entry.
            string staged = Path.Combine(Path.GetDirectoryName(startupDirectory), "SpaceKat-" + Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                WithShortcut(staged, link =>
                {
                    Invoke(link, "TargetPath", BindingFlags.SetProperty, launcher);
                    Invoke(link, "WorkingDirectory", BindingFlags.SetProperty, Path.GetDirectoryName(launcher));
                    Invoke(link, "Description", BindingFlags.SetProperty, "登录 Windows 时打开 SpaceKat 小助手");
                    Invoke(link, "WindowStyle", BindingFlags.SetProperty, 7);
                    return Invoke(link, "Save", BindingFlags.InvokeMethod);
                });
                if (File.Exists(shortcut)) File.Replace(staged, shortcut, null);
                else File.Move(staged, shortcut);
            }
            finally
            {
                if (File.Exists(staged)) File.Delete(staged);
            }
        }

        private static object WithShortcut(string path, Func<object, object> use)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(type);
            object link = null;
            try
            {
                link = Invoke(shell, "CreateShortcut", BindingFlags.InvokeMethod, path);
                return use(link);
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static object Invoke(object target, string member, BindingFlags flags, params object[] args)
        {
            return target.GetType().InvokeMember(member, flags, null, target, args);
        }
    }
}
