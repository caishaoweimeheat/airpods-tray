using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AirPodsQuickConnect
{
    internal sealed class Endpoint
    {
        internal string Id, Name, Description, Container;
        internal int Flow;
        internal uint State;
        internal bool SupportsReconnect;
    }
    internal sealed class DeviceChoice
    {
        internal TargetDevice Target;
        internal List<Endpoint> Endpoints;
        internal string Status
        {
            get
            {
                if (Endpoints.Any(e => e.Flow == 0 && e.State == 1)) return "已连接";
                if (Endpoints.Any(e => e.Flow == 0 && e.State == 8)) return "未连接";
                return "已禁用 / 不可用";
            }
        }
        internal string ShortId
        {
            get
            {
                string key = Target.Key;
                return key.Length > 8 ? key.Substring(key.Length - 8) : key;
            }
        }
    }
    internal static class DeviceCatalog
    {
        internal static string Identity(string container, string endpoint)
        {
            Guid id;
            if (Guid.TryParse(container, out id) && id != Guid.Empty) return "container:" + id.ToString("D");
            return string.IsNullOrWhiteSpace(endpoint) ? "" : "endpoint:" + endpoint;
        }
        internal static List<DeviceChoice> Group(IEnumerable<Endpoint> endpoints)
        {
            List<DeviceChoice> choices = new List<DeviceChoice>();
            foreach (var group in endpoints.Where(e => e.State != 4 && Identity(e.Container, e.Id).Length > 0)
                .GroupBy(e => Identity(e.Container, e.Id), StringComparer.Ordinal))
            {
                List<Endpoint> members = group.ToList();
                // Playback devices only; the BtAudio support query excludes speakers/HDMI/USB audio.
                Endpoint playback = members.Where(e => e.Flow == 0 && e.SupportsReconnect)
                    .OrderBy(e => e.State == 1 ? 0 : 1).FirstOrDefault();
                if (playback == null) continue;
                choices.Add(new DeviceChoice
                {
                    Target = new TargetDevice { Name = Label(playback), ContainerId = playback.Container, EndpointId = playback.Id },
                    Endpoints = members
                });
            }
            return choices.OrderBy(e => e.Target.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Target.Key).ToList();
        }
        internal static string Label(Endpoint endpoint)
        {
            return !string.IsNullOrWhiteSpace(endpoint.Description) ? endpoint.Description : endpoint.Name ?? "蓝牙音频设备";
        }
        internal static Snapshot Match(IEnumerable<Endpoint> endpoints, TargetDevice target)
        {
            return new Snapshot { Endpoints = target == null ? new List<Endpoint>() : endpoints.Where(target.Matches).ToList() };
        }
    }
    internal sealed class Snapshot
    {
        internal List<Endpoint> Endpoints = new List<Endpoint>();
        internal bool PlaybackActive { get { return Endpoints.Any(e => e.Flow == 0 && e.State == 1); } }
        internal string Name
        {
            get { Endpoint endpoint = Endpoints.FirstOrDefault(e => e.Flow == 0); return endpoint == null ? null : DeviceCatalog.Label(endpoint); }
        }
    }
    internal sealed class Outcome
    {
        internal bool Success;
        internal string Message;
        internal Outcome(bool success, string message) { Success = success; Message = message; Program.Log(message); }
    }
    internal static class Audio
    {
        private static readonly Guid BluetoothProperties = new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
        private static readonly PropertyKey FriendlyName = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
        private static readonly PropertyKey InterfaceName = new PropertyKey("026e516e-b814-414b-83cd-856d6fef4822", 2);
        private static readonly PropertyKey ContainerId = new PropertyKey("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);

        internal static List<DeviceChoice> Discover()
        {
            List<Endpoint> endpoints = Enumerate();
            foreach (Endpoint endpoint in endpoints.Where(e => e.State != 4))
                endpoint.SupportsReconnect = HasReconnect(endpoint.Id);
            return DeviceCatalog.Group(endpoints);
        }
        internal static Snapshot Read(TargetDevice target) { return DeviceCatalog.Match(Enumerate(), target); }

        private static List<Endpoint> Enumerate()
        {
            List<Endpoint> endpoints = new List<Endpoint>();
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                for (int flow = 0; flow < 2; flow++)
                {
                    IMMDeviceCollection collection = null;
                    try
                    {
                        Native.Check(enumerator.EnumAudioEndpoints(flow, 15, out collection));
                        uint count; Native.Check(collection.GetCount(out count));
                        for (uint index = 0; index < count; index++)
                        {
                            IMMDevice device = null; IPropertyStore store = null;
                            try
                            {
                                Native.Check(collection.Item(index, out device));
                                Native.Check(device.OpenPropertyStore(0, out store));
                                string id; uint state;
                                Native.Check(device.GetId(out id)); Native.Check(device.GetState(out state));
                                endpoints.Add(new Endpoint { Id = id, Name = Property(store, FriendlyName), Description = Property(store, InterfaceName),
                                    Container = Property(store, ContainerId), Flow = flow, State = state });
                            }
                            catch (COMException) { /* A device may disappear during enumeration. */ }
                            finally { Native.Release(store); Native.Release(device); }
                        }
                    }
                    finally { Native.Release(collection); }
                }
            }
            finally { Native.Release(enumerator); }
            return endpoints;
        }
        private static string Property(IPropertyStore store, PropertyKey key)
        {
            PropVariant value;
            int hr = store.GetValue(ref key, out value);
            try
            {
                if (hr < 0) return "";
                if (value.Type == 31 && value.Pointer != IntPtr.Zero) return Marshal.PtrToStringUni(value.Pointer) ?? "";
                if (value.Type == 72 && value.Pointer != IntPtr.Zero) return ((Guid)Marshal.PtrToStructure(value.Pointer, typeof(Guid))).ToString();
                return "";
            }
            finally { Native.PropVariantClear(ref value); }
        }
        private static bool HasReconnect(string endpointId)
        {
            IntPtr buffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buffer, 0);
                string adapter;
                int hr = Send(endpointId, 0x200, buffer, 4, null, out adapter);
                return hr >= 0 && (Marshal.ReadInt32(buffer) & 1) != 0;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        internal static Outcome Connect(TargetDevice target)
        {
            if (target == null || !target.IsValid) return new Outcome(false, "请先右键选择目标耳机。");
            Snapshot before = Read(target);
            if (before.Endpoints.Count == 0)
                return new Outcome(false, "未找到所选耳机。请检查蓝牙；重新配对后，请再次选择目标设备。");
            if (before.PlaybackActive) return new Outcome(true, (before.Name ?? target.Name) + " 已连接，无需重复连接。");
            List<Endpoint> candidates = before.Endpoints.Where(e => e.State == 8).OrderBy(e => e.Flow).ToList();
            if (candidates.Count == 0)
                return new Outcome(false, "所选音频设备不可用。请检查蓝牙是否开启，并在声音设置中启用该设备。");
            int accepted = 0;
            HashSet<string> sentAdapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Endpoint endpoint in candidates)
            {
                try
                {
                    string adapter;
                    int hr = Send(endpoint.Id, 1, IntPtr.Zero, 0, sentAdapters, out adapter);
                    Program.Log("Reconnect request: " + endpoint.Name + " HRESULT=0x" + hr.ToString("X8"));
                    if (hr >= 0) accepted++;
                }
                catch (Exception ex) { Program.Log("Reconnect request failed: " + ex.Message); }
            }
            if (accepted == 0) return new Outcome(false, "Windows 未接受连接请求。可右键打开蓝牙设置，或查看日志。");
            Stopwatch deadline = Stopwatch.StartNew();
            while (deadline.Elapsed.TotalSeconds < 25)
            {
                Thread.Sleep(700);
                Snapshot current = Read(target);
                if (current.PlaybackActive) return new Outcome(true, (current.Name ?? target.Name) + " 已连接，可以播放音频。");
            }
            return new Outcome(false, "25 秒内未确认连接。请取出耳机并靠近电脑；若正在连接手机，请先从手机断开后重试。");
        }

        private static int Send(string endpointId, uint flags, IntPtr data, uint dataLength, HashSet<string> visited, out string adapterId)
        {
            adapterId = "";
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice endpoint = null, adapter = null;
            IConnector connector = null;
            object topologyObject = null, ksObject = null;
            try
            {
                Native.Check(enumerator.GetDevice(endpointId, out endpoint));
                Guid iid = typeof(IDeviceTopology).GUID;
                Native.Check(endpoint.Activate(ref iid, 23, IntPtr.Zero, out topologyObject));
                IDeviceTopology topology = (IDeviceTopology)topologyObject;
                Native.Check(topology.GetConnector(0, out connector));
                Native.Check(connector.GetDeviceIdConnectedTo(out adapterId));
                if (visited != null && visited.Contains(adapterId)) return 1;
                Native.Check(enumerator.GetDevice(adapterId, out adapter));
                iid = typeof(IKsControl).GUID;
                Native.Check(adapter.Activate(ref iid, 23, IntPtr.Zero, out ksObject));
                KsProperty property = new KsProperty { Set = BluetoothProperties, Id = 0, Flags = flags };
                uint returned;
                int hr = ((IKsControl)ksObject).KsProperty(ref property, (uint)Marshal.SizeOf(typeof(KsProperty)), data, dataLength, out returned);
                if (hr >= 0 && visited != null) visited.Add(adapterId);
                return hr;
            }
            finally
            {
                Native.Release(ksObject); Native.Release(adapter); Native.Release(connector);
                Native.Release(topologyObject); Native.Release(endpoint); Native.Release(enumerator);
            }
        }
        internal static string Diagnose(TargetDevice target)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("AirPods Tray " + Program.Version);
            text.AppendLine("OS=" + Environment.OSVersion + "; Process64=" + Environment.Is64BitProcess);
            text.AppendLine("Selected=" + (target == null ? "(none)" : target.Name));
            if (target != null) text.AppendLine("PlaybackConnected=" + Read(target).PlaybackActive);
            List<DeviceChoice> choices = Discover();
            text.AppendLine("SelectableDevices=" + choices.Count);
            foreach (DeviceChoice device in choices)
                text.AppendLine("Name=" + device.Target.Name + "; Status=" + device.Status + "; Endpoints=" + device.Endpoints.Count + "; IdSuffix=" + device.ShortId);
            return text.ToString();
        }
    }
}
