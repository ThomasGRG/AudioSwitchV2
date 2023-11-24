using CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Windows.Forms;

namespace AudioSwitchV2
{
    public partial class Form1 : Form
    {
        public bool firstShow = true;
        public bool onBattery = false;
        public bool dragResize = false;
        public bool notify = false;
        public bool formHidden = false;

        public FormWindowState restoreWindowState = FormWindowState.Normal;

        public string offlineAudioDevice = "";
        public string restoreAudioDevice = "";

        private List<AudioDevice> audioDevices = new List<AudioDevice>();

        public ManagementEventWatcher managementEventWatcher;
        public readonly Dictionary<string, string> powerValues = new Dictionary<string, string>
                         {
                             {"4", "Entering Suspend"},
                             {"7", "Resume from Suspend"},
                             {"10", "Power Status Change"},
                             {"11", "OEM Event"},
                             {"18", "Resume Automatic"}
                         };

        public Form1()
        {
            InitializeComponent();
            Init();
        }

        private void Init()
        {
            CheckCurrentPowerStatus();
            LoadSettings();
            InitPowerEvents();
            GetAllAudioDevices();
            if(GetAudioDevice(offlineAudioDevice) == null)
            {
                firstShow = false;
            }
        }

        #region Buttons

        private void PauseButton_Click(object sender, System.EventArgs e)
        {
            if (PauseButton.Text == "Pause")
            {
                PauseButton.Text = "Resume";
                managementEventWatcher.Stop();
            }
            else
            {
                PauseButton.Text = "Pause";
                managementEventWatcher.Start();
            }
        }

        private void NotificationToggle_Click(object sender, System.EventArgs e)
        {
            notify = !notify;
        }

        private void StartAtBootToggle_Click(object sender, System.EventArgs e)
        {
            Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (rk.GetValue("AudioSwitchV2") == null)
            {
                rk.SetValue("AudioSwitchV2", Application.ExecutablePath);
                StartAtBootToggle.Text = "Disable Launch at Boot";
            }
            else
            {
                rk.DeleteValue("AudioSwitchV2", false);
                StartAtBootToggle.Text = "Enable Launch at Boot";
            }
        }

        private void RefreshButton_Click(object sender, System.EventArgs e)
        {
            GetAllAudioDevices();
        }

        #endregion

        #region Form

        private void Form1_Shown(object sender, System.EventArgs e)
        {
            if (firstShow)
            {
                WindowState = FormWindowState.Minimized;
                Hide();
                formHidden = true;
                notifyIcon1.ShowBalloonTip(1000);
            }
        }

        private void Form1_Resize(object sender, System.EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                formHidden = true;
            }
            else
            {
                restoreWindowState = WindowState;
            }
        }

        private void Form1_Load(object sender, System.EventArgs e)
        {
            Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (rk.GetValue("AudioSwitchV2") != null)
            {
                StartAtBootToggle.Text = "Disable Launch at Boot";
            }
            if (Properties.Settings.Default.notify)
            {
                NotificationToggle.Text = "Disable Notifications";
            }
            if (Properties.Settings.Default.wState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            } else
            {
                Size = Properties.Settings.Default.wSize;
                Location = Properties.Settings.Default.wLocation;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            managementEventWatcher.Stop();
            SaveSettings();
        }

        #endregion

        #region Settings

        private void LoadSettings()
        {
            offlineAudioDevice = Properties.Settings.Default.offlineDevice;
            notify = Properties.Settings.Default.notify;
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.offlineDevice = offlineAudioDevice;
            if (WindowState == FormWindowState.Minimized)
            {
                Properties.Settings.Default.wSize = RestoreBounds.Size;
                Properties.Settings.Default.wLocation = RestoreBounds.Location;
            }
            else
            {
                Properties.Settings.Default.wSize = Size;
                Properties.Settings.Default.wLocation = Location;
            }
            Properties.Settings.Default.wState = WindowState;
            Properties.Settings.Default.notify = notify;
            Properties.Settings.Default.Save();
        }

        #endregion

        #region Power

        private void CheckCurrentPowerStatus()
        {
            // Check current power status
            PowerLineStatus status = SystemInformation.PowerStatus.PowerLineStatus;
            if (status == PowerLineStatus.Offline)
            {
                Text = "AudioSwitchV2 - On Battery";
                onBattery = true;
            }
            else
            {
                Text = "AudioSwitchV2 - Plugged In";
                onBattery = false;
            }
        }

        private void InitPowerEvents()
        {
            var q = new WqlEventQuery();
            var scope = new ManagementScope("root\\CIMV2");

            q.EventClassName = "Win32_PowerManagementEvent";
            managementEventWatcher = new ManagementEventWatcher(scope, q);
            managementEventWatcher.EventArrived += PowerEventArrive;
            managementEventWatcher.Start();
        }

        private void PowerEventArrive(object sender, EventArrivedEventArgs e)
        {
            foreach (PropertyData pd in e.NewEvent.Properties)
            {
                if (pd == null || pd.Value == null) continue;
                var name = powerValues.ContainsKey(pd.Value.ToString())
                               ? powerValues[pd.Value.ToString()]
                               : pd.Value.ToString();
                if (name == powerValues["10"])
                {
                    getPowerInfo();
                }
            }
        }

        public void getPowerInfo()
        {
            PowerLineStatus status = SystemInformation.PowerStatus.PowerLineStatus;
            if (status == PowerLineStatus.Offline && !onBattery)
            {
                Invoke((MethodInvoker)delegate
                {
                    Text = "AudioSwitchV2 - On Battery";
                });
                onBattery = true;
                GetAllAudioDevices();
                restoreAudioDevice = GetDefaultAudioDevice().ID;
                if (restoreAudioDevice == offlineAudioDevice)
                {
                    return;
                }
                AudioDevice device = GetAudioDevice(offlineAudioDevice);
                if (device != null)
                {
                    SetAudioDevice(device.ID);
                    Notify("On Battery", $"Audio Device Change -> {device.Name}");
                }
            }
            else if (status == PowerLineStatus.Online && onBattery)
            {
                Invoke((MethodInvoker)delegate
                {
                    Text = "AudioSwitchV2 - Plugged In";
                });
                onBattery = false;
                GetAllAudioDevices();
                AudioDevice device = GetAudioDevice(restoreAudioDevice);
                if (device != null)
                {
                    SetAudioDevice(device.ID);
                    restoreAudioDevice = "";
                    Notify("Plugged In", $"Audio Device Change -> {device.Name}");
                }
            }
        }

        #endregion

        #region AudioDevice

        private AudioDevice GetDefaultAudioDevice()
        {
            for (int i = 0; i < audioDevices.Count; i++)
            {
                if (audioDevices[i].Default == true)
                {
                    return audioDevices[i];
                }
            }
            return null;
        }

        private AudioDevice GetAudioDevice(string deviceID)
        {
            for (int i = 0; i < audioDevices.Count; i++)
            {
                if (audioDevices[i].ID == deviceID)
                {
                    return audioDevices[i];
                }
            }
            return null;
        }

        private void GetAllAudioDevices()
        {
            audioDevices.Clear();
            listView1.Items.Clear();
            // Create a new MMDeviceEnumerator
            MMDeviceEnumerator DevEnum = new MMDeviceEnumerator();
            // Create a MMDeviceCollection of every devices that are enabled
            MMDeviceCollection DeviceCollection = DevEnum.EnumerateAudioEndPoints(EDataFlow.eRender, EDeviceState.DEVICE_STATE_ACTIVE);

            string defaultDevice = DevEnum.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia).ID;

            // For every MMDevice in DeviceCollection
            for (int i = 0; i < DeviceCollection.Count; i++)
            {
                ListViewItem listViewItem = new ListViewItem(DeviceCollection[i].FriendlyName);
                listViewItem.Tag = DeviceCollection[i].ID;
                listViewItem.ToolTipText = "Device ID : " + DeviceCollection[i].ID;
                if (DeviceCollection[i].ID == defaultDevice)
                {
                    listViewItem.Font = new Font(listViewItem.Font.FontFamily, 14f, FontStyle.Bold);
                    listViewItem.ToolTipText += " (Default)";
                } else if (DeviceCollection[i].ID == offlineAudioDevice)
                {
                    listViewItem.Font = new Font(listViewItem.Font.FontFamily, 14f, FontStyle.Underline);
                    listViewItem.ForeColor = Color.FromArgb(0, 255, 0);
                }
                audioDevices.Add(new AudioDevice(DeviceCollection[i].ID, DeviceCollection[i].FriendlyName, DeviceCollection[i].ID == defaultDevice));
                listView1.Items.Add(listViewItem);
            }
            if (DeviceCollection.Count <= 1)
            {
                PauseButton.Text = "Resume";
                managementEventWatcher.Stop();
            }
            else
            {
                if (PauseButton.Text == "Resume")
                {
                    PauseButton.Text = "Pause";
                    managementEventWatcher.Start();
                }
            }
        }

        private void SetAudioDevice(string deviceID)
        {
            // Create a new MMDeviceEnumerator
            MMDeviceEnumerator DevEnum = new MMDeviceEnumerator();
            // Create a MMDeviceCollection of every devices that are enabled
            MMDeviceCollection DeviceCollection = DevEnum.EnumerateAudioEndPoints(EDataFlow.eRender, EDeviceState.DEVICE_STATE_ACTIVE);

            for (int i = 0; i < DeviceCollection.Count; i++)
            {
                // If this MMDevice's ID is the same as the string received by the ID parameter
                if (string.Compare(DeviceCollection[i].ID, deviceID, System.StringComparison.CurrentCultureIgnoreCase) == 0)
                {
                    // Create a new audio PolicyConfigClient
                    PolicyConfigClient client = new PolicyConfigClient();
                    // Using PolicyConfigClient, set the given device as the default communication device (for its type)
                    client.SetDefaultEndpoint(DeviceCollection[i].ID, ERole.eCommunications);
                    // Using PolicyConfigClient, set the given device as the default device (for its type)
                    client.SetDefaultEndpoint(DeviceCollection[i].ID, ERole.eMultimedia);
                }
            }
        }

        #endregion

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            offlineAudioDevice = listView1.SelectedItems[0].Tag as string;
            GetAllAudioDevices();
        }

        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && formHidden)
            {
                Show();
                formHidden = false;
                WindowState = restoreWindowState;
                return;
            }
            if (!formHidden)
            {
                restoreWindowState = WindowState;
                WindowState = FormWindowState.Minimized;
                Hide();
                formHidden = true;
                return;
            }
        }

        private void Notify(string Title, string Text)
        {
            if (notify)
            {
                notifyIcon1.ShowBalloonTip(1000, Title, Text, ToolTipIcon.Info);
            }
        }

        private class AudioDevice
        {
            public string ID;
            public string Name;
            public bool Default;

            public AudioDevice(string ID, string Name, bool @default)
            {
                this.ID = ID;
                this.Name = Name;
                Default = @default;
            }
        }
    }
}
