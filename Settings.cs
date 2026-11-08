using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace AirPodsQuickConnect
{
    [XmlRoot("SelectedBluetoothDevice")]
    public sealed class TargetDevice
    {
        public string Name { get; set; }
        public string ContainerId { get; set; }
        public string EndpointId { get; set; }

        internal string Key
        {
            get { return DeviceCatalog.Identity(ContainerId, EndpointId); }
        }
        internal bool IsValid
        {
            get { return !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrEmpty(Key); }
        }
        internal bool Matches(Endpoint endpoint)
        {
            return IsValid && string.Equals(Key, DeviceCatalog.Identity(endpoint.Container, endpoint.Id), StringComparison.Ordinal);
        }
    }

    internal sealed class SettingsStore
    {
        internal readonly string PathName;
        internal SettingsStore(string path) { PathName = path; }
        internal static SettingsStore Default()
        {
            return new SettingsStore(Path.Combine(Program.DataDirectory, "settings.xml"));
        }
        internal TargetDevice Load(out string warning)
        {
            warning = null;
            if (!File.Exists(PathName)) return null;
            try
            {
                if (new FileInfo(PathName).Length > 64 * 1024) throw new InvalidDataException("Settings file too large.");
                XmlReaderSettings options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(PathName, options))
                {
                    TargetDevice device = (TargetDevice)new XmlSerializer(typeof(TargetDevice)).Deserialize(reader);
                    if (device == null || !device.IsValid) throw new InvalidDataException("Invalid selection.");
                    return device;
                }
            }
            catch (Exception ex)
            {
                Program.Log("Settings could not be read: " + ex.Message);
                warning = "上次保存的选择无法读取，请重新选择目标耳机。";
                return null;
            }
        }
        internal void Save(TargetDevice device)
        {
            if (device == null || !device.IsValid) throw new ArgumentException("请选择有效的蓝牙音频设备。");
            string directory = Path.GetDirectoryName(Path.GetFullPath(PathName));
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (XmlWriter writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true }))
                    new XmlSerializer(typeof(TargetDevice)).Serialize(writer, device);
                if (File.Exists(PathName)) File.Replace(temporary, PathName, null);
                else File.Move(temporary, PathName);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
