using System;
using System.IO;
using System.Reflection;

namespace AirPodsQuickConnect
{
    internal sealed class StartupShortcut
    {
        private readonly string path, executable, directory;
        internal StartupShortcut(string shortcutPath, string applicationPath, string workingDirectory)
        {
            path = shortcutPath; executable = applicationPath; directory = workingDirectory;
        }
        internal bool IsEnabled()
        {
            if (!File.Exists(path)) return false;
            try { return string.Equals(Path.GetFullPath(ReadTarget()), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        internal string ReadTarget()
        {
            return WithShortcut(delegate(object shortcut)
            {
                return (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            });
        }
        internal void SetEnabled(bool enabled)
        {
            if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WithShortcut(delegate(object shortcut)
            {
                Type type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { executable });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { directory });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Bluetooth audio quick connect" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                return "";
            });
        }
        private string WithShortcut(Func<object, string> action)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type), shortcut = null;
            try
            {
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return action(shortcut);
            }
            finally { Native.Release(shortcut); Native.Release(shell); }
        }
    }
}
