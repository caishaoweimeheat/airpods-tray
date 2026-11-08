using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("AirPods Tray")]
[assembly: AssemblyDescription("Choose a paired Bluetooth audio device and reconnect from the Windows tray.")]
[assembly: AssemblyVersion("0.2.2.0")]
[assembly: AssemblyFileVersion("0.2.2.0")]

namespace AirPodsQuickConnect
{
    internal static class Program
    {
        internal const string Version = "0.2.2-beta";
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPodsTray");
        internal static string LogPath = Path.Combine(DataDirectory, "AirPodsTray.log");
        private static readonly object LogLock = new object();
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string result; int code = 0;
                try
                {
                    string warning;
                    TargetDevice selected = SettingsStore.Default().Load(out warning);
                    if (args[0] == "--diagnose") result = Audio.Diagnose(selected);
                    else if (args[0] == "--connect")
                    {
                        Outcome outcome = Audio.Connect(selected);
                        result = outcome.Message + Environment.NewLine + Audio.Diagnose(selected);
                        code = outcome.Success ? 0 : 2;
                    }
                    else return 64;
                }
                catch (Exception ex) { result = ex.ToString(); code = 1; }
                if (args.Length > 1) File.WriteAllText(args[1], result, new UTF8Encoding(true));
                else Log(result);
                return code;
            }
            bool created;
            using (Mutex singleton = new Mutex(true, "Local\\AirPodsTray_SingleInstance_v2", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); };
                try { Application.Run(new Tray(SettingsStore.Default())); }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    MessageBox.Show("程序未能启动：\n" + ex.Message, "AirPods Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 1;
                }
            }
            return 0;
        }
        internal static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024) File.WriteAllText(LogPath, "", Encoding.UTF8);
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
        internal static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log(ex.ToString()); }
        }
        internal static string ShortText(string text, int max)
        {
            text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (text.Length <= max) return text;
            int end = max - 1;
            if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
            return text.Substring(0, end) + "…";
        }
    }
    internal sealed class Tray : ApplicationContext
    {
        private readonly NotifyIcon icon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem stateItem, connectItem, startupItem, chooseItem;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Icon idleIcon, connectedIcon, busyIcon;
        private readonly SettingsStore settings;
        private readonly StartupShortcut startup;
        private TargetDevice target;
        private int selectionRevision;
        private bool busy, refreshing, choosing, closing;
        private DevicePicker picker;
        private const string StartupFile = "AirPods4QuickConnect.lnk";
        internal Tray(SettingsStore store)
        {
            settings = store;
            startup = new StartupShortcut(StartupPath, Application.ExecutablePath, Program.Root);
            string warning; target = settings.Load(out warning);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            idleIcon = Artwork.Make(Color.FromArgb(43, 133, 190));
            connectedIcon = Artwork.Make(Color.FromArgb(31, 160, 103));
            busyIcon = Artwork.Make(Color.FromArgb(224, 159, 38));
            menu = new ContextMenuStrip();
            stateItem = new ToolStripMenuItem("正在读取状态…") { Enabled = false };
            connectItem = new ToolStripMenuItem("连接目标耳机", null, delegate { Connect(); });
            chooseItem = new ToolStripMenuItem("选择目标耳机…", null, delegate { ChooseDevice(); });
            startupItem = new ToolStripMenuItem("开机启动") { Checked = startup.IsEnabled() };
            startupItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(stateItem); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(connectItem); menu.Items.Add(chooseItem);
            menu.Items.Add("刷新状态", null, delegate { RefreshState(); });
            menu.Items.Add("蓝牙设置", null, delegate { Program.Open("ms-settings:bluetooth"); });
            menu.Items.Add("声音设置", null, delegate { Program.Open("ms-settings:sound"); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(startupItem);
            menu.Items.Add("查看日志", null, delegate { Program.Log("Log opened."); Program.Open(Program.LogPath); });
            menu.Items.Add("关于", null, delegate { MessageBox.Show("AirPods Tray " + Program.Version + "\n\n左键连接目标耳机；右键更换目标。\n蓝色：未连接；黄色：连接中；绿色：已连接。", "AirPods Tray", MessageBoxButtons.OK, MessageBoxIcon.Information); });
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            icon = new NotifyIcon { Icon = idleIcon, Text = "AirPods Tray · 选择目标耳机", ContextMenuStrip = menu, Visible = true };
            icon.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Connect(); };
            timer = new System.Windows.Forms.Timer { Interval = 8000 };
            timer.Tick += delegate { RefreshState(); }; timer.Start();
            Program.Log("Tray started. Version=" + Program.Version);
            RefreshState();
            if (warning != null) Notify("请重新选择耳机", warning, true);
            if (target == null) SynchronizationContext.Current.Post(delegate { if (!closing) ChooseDevice(); }, null);
        }
        private string TargetName { get { return target == null ? "尚未选择耳机" : target.Name; } }
        private void ChooseDevice()
        {
            if (closing || busy) return;
            if (choosing) { if (picker != null) picker.Activate(); return; }
            choosing = true;
            try
            {
                using (picker = new DevicePicker(target, Audio.Discover))
                {
                    if (picker.ShowDialog() == DialogResult.OK && !closing)
                    {
                        settings.Save(picker.SelectedTarget);
                        target = picker.SelectedTarget; selectionRevision++;
                        SetStatus("正在检查…", idleIcon);
                        Notify("已保存目标耳机", target.Name + "。之后左键点击图标即可连接。", false);
                    }
                }
            }
            catch (Exception ex) { Program.Log(ex.ToString()); if (!closing) Notify("选择未保存", ex.Message, true); }
            finally { picker = null; choosing = false; if (!closing) RefreshState(); }
        }
        private async void Connect()
        {
            if (busy || closing || choosing) return;
            if (target == null) { ChooseDevice(); return; }
            TargetDevice operationTarget = target;
            busy = true; connectItem.Enabled = false; chooseItem.Enabled = false;
            SetStatus("正在连接…", busyIcon);
            try
            {
                Outcome outcome = await Task.Run(() => Audio.Connect(operationTarget));
                if (!closing) Notify(outcome.Success ? "耳机连接" : "暂未连接", outcome.Message, !outcome.Success);
            }
            catch (Exception ex)
            {
                Program.Log(ex.ToString());
                if (!closing) Notify("连接失败", "请确认蓝牙已开启、耳机已取出。可右键打开蓝牙设置或查看日志。", true);
            }
            finally
            {
                busy = false;
                if (!closing) { connectItem.Enabled = true; chooseItem.Enabled = true; RefreshState(); }
            }
        }
        private async void RefreshState()
        {
            if (busy || refreshing || closing) return;
            if (target == null) { SetStatus("请右键选择目标", idleIcon); return; }
            TargetDevice checkedTarget = target;
            int revision = selectionRevision;
            refreshing = true;
            try
            {
                Snapshot snapshot = await Task.Run(() => Audio.Read(checkedTarget));
                if (!closing && !busy && revision == selectionRevision)
                {
                    if (!string.IsNullOrWhiteSpace(snapshot.Name)) checkedTarget.Name = snapshot.Name;
                    SetStatus(snapshot.PlaybackActive ? "已连接" : snapshot.Endpoints.Count == 0 ? "未找到设备" : "未连接", snapshot.PlaybackActive ? connectedIcon : idleIcon);
                }
            }
            catch (Exception ex)
            {
                if (!closing && !busy && revision == selectionRevision) SetStatus("无法读取状态", idleIcon);
                Program.Log("Read status failed: " + ex.Message);
            }
            finally
            {
                refreshing = false;
                if (!closing && revision != selectionRevision) RefreshState();
            }
        }
        private void SetStatus(string status, Icon image)
        {
            stateItem.Text = Program.ShortText(TargetName, 40) + " · " + status;
            connectItem.Text = target == null ? "选择目标耳机…" : "连接 " + Program.ShortText(TargetName, 30);
            icon.Text = Program.ShortText(TargetName + " · " + status + " · 左键连接", 63);
            icon.Icon = image;
        }
        private void Notify(string title, string body, bool warning)
        {
            icon.ShowBalloonTip(4500, Program.ShortText(title, 63), Program.ShortText(body, 255), warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        private static string StartupPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupFile); } }
        private void ToggleStartup()
        {
            try
            {
                startup.SetEnabled(!startupItem.Checked);
                startupItem.Checked = startup.IsEnabled();
                Notify("开机启动", startupItem.Checked ? "已开启。请保留此程序所在文件夹。" : "已关闭。", false);
            }
            catch (Exception ex) { Program.Log(ex.ToString()); Notify("设置未完成", ex.Message, true); }
        }
        protected override void ExitThreadCore()
        {
            closing = true;
            if (picker != null && !picker.IsDisposed) picker.Close();
            timer.Stop(); timer.Dispose(); icon.Visible = false; icon.Dispose(); menu.Dispose();
            idleIcon.Dispose(); connectedIcon.Dispose(); busyIcon.Dispose();
            Program.Log("Tray exited.");
            base.ExitThreadCore();
        }
    }
}
