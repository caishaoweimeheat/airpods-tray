using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AirPodsQuickConnect.Tests
{
    internal static class TestProgram
    {
        private static int passed;
        private const string A = "11111111-1111-1111-1111-111111111111";
        private const string B = "22222222-2222-2222-2222-222222222222";
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static Endpoint E(string id, string container, string name, int flow, uint state, bool supported)
        {
            return new Endpoint { Id = id, Container = container, Name = name, Description = name, Flow = flow, State = state, SupportsReconnect = supported };
        }
        private static void PumpUntil(Func<bool> condition)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("UI test timeout");
                Application.DoEvents(); Thread.Sleep(10);
            }
            Application.DoEvents();
        }
        [STAThread]
        private static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            Program.LogPath = Path.Combine(output, "tests.log");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                var endpoints = new List<Endpoint>
                {
                    E("a-render", A, "同名耳机", 0, 8, true), E("a-mic", A, "同名耳机", 1, 8, true),
                    E("b-render", B, "同名耳机", 0, 1, true), E("speaker", "", "扬声器", 0, 1, false),
                    E("old", "33333333-3333-3333-3333-333333333333", "旧配对", 0, 4, true)
                };
                List<DeviceChoice> grouped = DeviceCatalog.Group(endpoints);
                Check(grouped.Count == 2, "same names stay separate; non-Bluetooth and stale entries excluded");
                Check(grouped.Single(d => d.Target.ContainerId == A).Endpoints.Count == 2, "playback and microphone grouped by physical container");
                var first = grouped.Single(d => d.Target.ContainerId == A).Target;
                var second = grouped.Single(d => d.Target.ContainerId == B).Target;
                Check(!DeviceCatalog.Match(endpoints, first).PlaybackActive, "another device being connected does not mark the target connected");
                Check(DeviceCatalog.Match(endpoints, second).PlaybackActive, "selected active playback is connected");
                Check(first.Matches(E("a-new-render", A.ToUpperInvariant(), "耳机已改名", 0, 1, true)), "rename and endpoint recreation preserve container targeting");
                Check(!first.Matches(E("a-render", B, "同名耳机", 0, 1, true)), "container mismatch never falls back to endpoint or name");
                Check(DeviceCatalog.Match(new[] { endpoints[2] }, first).Endpoints.Count == 0, "missing target never falls back to same-name device");
                Check(DeviceCatalog.Group(new[] { E("x", Guid.Empty.ToString(), "耳机", 0, 8, true), E("y", "", "耳机", 0, 8, true) }).Count == 2,
                    "missing container IDs remain separate by endpoint");
                TargetDevice fallback = new TargetDevice { Name = "Fallback", EndpointId = "CaseSensitive-ID" };
                Check(fallback.Matches(E("CaseSensitive-ID", "", "renamed", 0, 8, true)) && !fallback.Matches(E("casesensitive-id", "", "renamed", 0, 8, true)), "opaque endpoint fallback preserves exact ID");

                var store = new SettingsStore(Path.Combine(output, "settings.xml"));
                first.Name = "蔡的耳机 & <AirPods> 🎧";
                store.Save(first);
                string warning;
                TargetDevice loaded = new SettingsStore(store.PathName).Load(out warning);
                Check(warning == null && loaded.Key == first.Key && loaded.Name == first.Name, "selection persists Unicode/XML characters across store instances");
                store.Save(second);
                Check(store.Load(out warning).Key == second.Key, "atomic replacement persists changed selection");
                bool rejected = false;
                try { store.Save(new TargetDevice { Name = "Missing identity" }); } catch (ArgumentException) { rejected = true; }
                Check(rejected && store.Load(out warning).Key == second.Key, "invalid selection cannot overwrite saved target");
                File.WriteAllText(store.PathName, "<broken>");
                Check(store.Load(out warning) == null && warning != null && File.ReadAllText(store.PathName) == "<broken>", "corrupt configuration requests reselection without silent fallback");
                string longName = new string('a', 61) + "🎧" + new string('b', 50);
                string clipped = Program.ShortText(longName, 63);
                Check(clipped.Length <= 63 && !char.IsHighSurrogate(clipped[clipped.Length - 2]), "long Unicode device name fits tray tooltip");

                string shortcutPath = Path.Combine(output, "test-startup.lnk");
                string executable = Application.ExecutablePath;
                var startup = new StartupShortcut(shortcutPath, executable, output);
                startup.SetEnabled(true);
                Check(startup.IsEnabled(), "startup shortcut can be created in isolated test directory");
                var moved = new StartupShortcut(shortcutPath, Path.Combine(output, "new-location.exe"), output);
                Check(!moved.IsEnabled(), "old startup target is detected after moving executable");
                moved.SetEnabled(true);
                Check(moved.IsEnabled() && !startup.IsEnabled(), "enabling startup updates an old shortcut target");
                moved.SetEnabled(false);
                Check(!File.Exists(shortcutPath), "startup shortcut removal affects only owned test shortcut");

                using (DevicePicker picker = new DevicePicker(second, () => grouped))
                {
                    picker.Show(); PumpUntil(() => picker.Loaded);
                    Check(picker.Devices.SelectedItems.Count == 1 && ((DeviceChoice)picker.Devices.SelectedItems[0].Tag).Target.Key == second.Key, "picker restores exact selection with duplicate names");
                    picker.SaveSelection();
                    Check(picker.SelectedTarget.Key == second.Key && picker.DialogResult == DialogResult.OK, "picker confirms selected physical device");
                }
                using (DevicePicker picker = new DevicePicker(new TargetDevice { Name = "Gone", ContainerId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }, () => grouped))
                {
                    picker.Show(); PumpUntil(() => picker.Loaded);
                    Check(picker.Devices.SelectedItems.Count == 0 && !picker.SaveButton.Enabled, "picker never silently substitutes a missing saved device"); picker.Close();
                }
                using (DevicePicker picker = new DevicePicker(null, () => new List<DeviceChoice>()))
                {
                    picker.Show(); PumpUntil(() => picker.Loaded);
                    Check(!picker.SaveButton.Enabled && picker.SelectedTarget == null, "empty device list cannot be saved"); picker.Close();
                }

                List<DeviceChoice> live = Audio.Discover();
                File.WriteAllText(Path.Combine(output, "live-diagnostic.txt"), Audio.Diagnose(null));
                Check(true, "live Core Audio enumeration completed: " + live.Count + " supported device(s)");
                using (DevicePicker picker = new DevicePicker(null, () => live))
                {
                    picker.Show(); PumpUntil(() => picker.Loaded);
                    using (Bitmap bitmap = new Bitmap(picker.Width, picker.Height))
                    {
                        picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, "device-picker.png"));
                    }
                    if (picker.Devices.SelectedItems.Count == 1)
                    {
                        picker.SaveSelection(); store.Save(picker.SelectedTarget);
                        TargetDevice saved = new SettingsStore(store.PathName).Load(out warning);
                        Check(saved.Key == picker.SelectedTarget.Key, "live device picker selection persists");
                        if (Audio.Read(saved).PlaybackActive)
                            Check(Audio.Connect(saved).Success, "already-connected device returns success without a reconnect request");
                    }
                    else picker.Close();
                }
                Console.WriteLine("All " + passed + " checks passed. No device was deliberately disconnected.");
                File.WriteAllText(Path.Combine(output, "summary.txt"), "PASS: " + passed + " checks. No deliberate device disconnect.\n");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); return 1;
            }
        }
    }
}
