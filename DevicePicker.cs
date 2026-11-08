using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AirPodsQuickConnect
{
    internal sealed class DevicePicker : Form
    {
        internal readonly ListView Devices;
        internal readonly Button SaveButton;
        internal TargetDevice SelectedTarget;
        internal bool Loaded { get; private set; }
        private readonly Label message;
        private readonly Button refresh;
        private readonly Func<List<DeviceChoice>> discover;
        private string selectedKey;
        private bool loading;

        internal DevicePicker(TargetDevice current, Func<List<DeviceChoice>> provider)
        {
            discover = provider;
            selectedKey = current == null ? null : current.Key;
            Text = "选择目标耳机 · AirPods Tray";
            float scale;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = screen.DpiX / 96f;
            Func<int, int> px = value => (int)Math.Ceiling(value * scale);
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(px(680), px(420));
            MinimumSize = new Size(px(650), px(420));
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            using (Icon artwork = Artwork.Make(Color.FromArgb(43, 133, 190))) Icon = (Icon)artwork.Clone();

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(px(18)), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(48)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(56)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(42)));
            Label intro = new Label { Text = "选择后，左键点击托盘图标即可连接这副耳机。", Dock = DockStyle.Fill, AutoSize = false };
            Devices = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, ShowItemToolTips = true };
            Devices.Columns.Add("蓝牙音频设备", px(330));
            Devices.Columns.Add("状态", px(125));
            Devices.Columns.Add("设备标识", px(135));
            Devices.SelectedIndexChanged += delegate { SaveButton.Enabled = !loading && Devices.SelectedItems.Count == 1; };
            Devices.DoubleClick += delegate { SaveSelection(); };
            message = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, px(8), 0, 0), ForeColor = Color.DimGray, Text = "正在读取已配对的音频设备…" };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            SaveButton = new Button { Text = "设为目标耳机", AutoSize = true, Enabled = false };
            SaveButton.Click += delegate { SaveSelection(); };
            refresh = new Button { Text = "刷新列表", AutoSize = true };
            refresh.Click += delegate { Reload(); };
            Button bluetooth = new Button { Text = "蓝牙设置", AutoSize = true };
            bluetooth.Click += delegate { Program.Open("ms-settings:bluetooth"); };
            buttons.Controls.AddRange(new Control[] { cancel, SaveButton, refresh, bluetooth });
            foreach (Control button in buttons.Controls) button.Margin = new Padding(px(3));
            layout.Controls.Add(intro, 0, 0); layout.Controls.Add(Devices, 0, 1);
            layout.Controls.Add(message, 0, 2); layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            AcceptButton = SaveButton; CancelButton = cancel;
            Shown += delegate { Reload(); };
        }
        private async void Reload()
        {
            if (loading) return;
            if (Devices.SelectedItems.Count == 1) selectedKey = ((DeviceChoice)Devices.SelectedItems[0].Tag).Target.Key;
            loading = true; refresh.Enabled = false; SaveButton.Enabled = false;
            message.Text = "正在读取已配对的音频设备…";
            try
            {
                List<DeviceChoice> choices = await Task.Run(discover);
                if (!IsDisposed) Populate(choices);
            }
            catch (Exception ex)
            {
                Program.Log(ex.ToString());
                if (!IsDisposed) { Devices.Items.Clear(); message.Text = "读取失败。请检查蓝牙是否开启，再刷新列表。"; }
            }
            finally
            {
                loading = false;
                Loaded = true;
                if (!IsDisposed) { refresh.Enabled = true; SaveButton.Enabled = Devices.SelectedItems.Count == 1; }
            }
        }
        internal void Populate(List<DeviceChoice> choices)
        {
            Devices.BeginUpdate();
            try
            {
                Devices.Items.Clear();
                foreach (DeviceChoice choice in choices)
                {
                    ListViewItem item = new ListViewItem(new[] { choice.Target.Name, choice.Status, choice.ShortId })
                        { Tag = choice, ToolTipText = choice.Target.Name + "\n" + choice.Target.Key };
                    Devices.Items.Add(item);
                    if (choice.Target.Key == selectedKey) item.Selected = true;
                }
                // Only preselect on first use. A missing saved device never falls back to another one.
                if (selectedKey == null)
                {
                    List<DeviceChoice> preferred = choices.Where(c => c.Target.Name.Equals("AirPods 4", StringComparison.OrdinalIgnoreCase)).ToList();
                    DeviceChoice initial = preferred.Count == 1 ? preferred[0] : choices.Count == 1 ? choices[0] : null;
                    if (initial != null) foreach (ListViewItem item in Devices.Items) if (ReferenceEquals(item.Tag, initial)) item.Selected = true;
                }
                message.Text = choices.Count == 0 ? "未找到可用设备。请打开蓝牙并先完成配对，再点“刷新列表”。"
                    : "同名设备可通过最后一列区分。保存选择不会立即发起连接。";
            }
            finally { Devices.EndUpdate(); }
            SaveButton.Enabled = !loading && Devices.SelectedItems.Count == 1;
        }
        internal void SaveSelection()
        {
            if (loading || Devices.SelectedItems.Count != 1) return;
            SelectedTarget = ((DeviceChoice)Devices.SelectedItems[0].Tag).Target;
            DialogResult = DialogResult.OK;
            Close();
        }
        protected override void Dispose(bool disposing)
        {
            Icon ownedIcon = disposing ? Icon : null;
            base.Dispose(disposing);
            if (ownedIcon != null) ownedIcon.Dispose();
        }
    }
}
