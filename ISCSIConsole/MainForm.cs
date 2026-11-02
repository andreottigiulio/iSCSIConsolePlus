/* Copyright (C) 2016 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ISCSI.Server;
using DiskAccessLibrary;
using Utilities;

namespace ISCSIConsole
{
    public partial class MainForm : Form
    {
        private ISCSIServer m_server = new ISCSIServer();
        private List<ISCSITarget> m_targets = new List<ISCSITarget>();
        private UsageCounter m_usageCounter = new UsageCounter();
        private bool m_started = false;

        private CommandLineOptions m_options;
        private AppSettings m_settings;
        private bool m_loading = false;
        private bool m_connecting = false;

        private TabControl tabMain;
        private TabPage tabTarget;
        private TabPage tabInitiator;
        private GroupBox grpTargetAutoStart;
        private CheckBox chkRestoreConfiguration;
        private Button btnEnableTargetAutoStart;
        private Button btnRemoveTargetAutoStart;
        private Label lblTargetAutoStartState;
        private GroupBox grpInitiatorSummary;
        private Label lblInitiatorDisk;
        private Label lblInitiatorTarget;
        private Label lblInitiatorPortal;
        private GroupBox grpInitiatorAutoStart;
        private CheckBox chkAutoConnect;
        private CheckBox chkInitializeDisk;
        private Label lblInitiatorAutoStartState;
        private Label lblInitiatorHint;
        private Button btnEnableInitiatorAutoStart;
        private Button btnConnectNow;
        private Button btnDisconnect;
        private Button btnOpenIscsiPanel;
        private Button btnRemoveInitiatorAutoStart;
        private CheckBox chkMinimizeToTray;
        private NotifyIcon m_trayIcon;
        private ContextMenuStrip m_trayMenu;
        private bool m_exitRequested = false;
        private bool m_trayBalloonShown = false;
        private bool m_installingInitiatorTask = false;

        public MainForm() : this(new CommandLineOptions())
        {
        }

        public MainForm(CommandLineOptions options)
        {
            InitializeComponent();
            m_options = options == null ? new CommandLineOptions() : options;
            m_settings = AppSettings.Load();
            ApplyCommandLineOverrides(m_options, m_settings);
            if (m_options.Save)
            {
                m_settings.Save();
            }
            CreateTabbedLayout();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.Text += " v" + version.ToString(3);
            m_server.OnLogEntry += Program.OnLogEntry;

            List<IPAddress> localIPs = GetHostIPAddresses();
            KeyValuePairList<string, IPAddress> list = new KeyValuePairList<string, IPAddress>();
            list.Add("Any", IPAddress.Any);
            foreach (IPAddress address in localIPs)
            {
                list.Add(address.ToString(), address);
            }
            comboIPAddress.DataSource = list;
            comboIPAddress.DisplayMember = "Key";
            comboIPAddress.ValueMember = "Value";
            lblStatus.Text = "Author: Tal Aloni (tal.aloni.il@gmail.com)";

            if (RuntimeHelper.IsWin32 && !SecurityHelper.IsAdministrator())
            {
                lblStatus.Text = "Some features require administrator privileges and have been disabled";
            }

            ApplySettingsToUI();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            if (m_settings.AutoStart && !String.IsNullOrEmpty(m_settings.DiskImagePath))
            {
                RestoreConfiguration();
            }

            UpdateInitiatorSummary();
            UpdateAutoStartStateLabels();

            if (m_options.Setup)
            {
                SetupAllAutoStart();
            }
            else if (m_options.AutoConnect && m_targets.Count > 0)
            {
                ConnectWindowsAsync(false);
            }
        }

        private void CreateTabbedLayout()
        {
            this.SuspendLayout();

            this.ClientSize = new Size(506, 386);
            this.MinimumSize = new Size(512, 411);
            this.MaximumSize = new Size(512, 411);
            this.AcceptButton = null;
            this.statusStrip1.Location = new Point(0, 364);
            this.statusStrip1.Size = new Size(506, 22);

            tabMain = new TabControl();
            tabMain.Location = new Point(8, 8);
            tabMain.Size = new Size(490, 356);
            tabMain.TabIndex = 0;

            tabTarget = new TabPage("Target (Server)");
            tabTarget.UseVisualStyleBackColor = true;
            tabInitiator = new TabPage("Windows Initiator (Client)");
            tabInitiator.UseVisualStyleBackColor = true;
            tabMain.TabPages.Add(tabTarget);
            tabMain.TabPages.Add(tabInitiator);
            this.Controls.Add(tabMain);

            MoveToTab(btnStart, tabTarget);
            MoveToTab(lblPort, tabTarget);
            MoveToTab(txtPort, tabTarget);
            MoveToTab(lblIPAddress, tabTarget);
            MoveToTab(comboIPAddress, tabTarget);
            MoveToTab(btnAddTarget, tabTarget);
            MoveToTab(lblTargets, tabTarget);
            MoveToTab(btnRemoveTarget, tabTarget);
            MoveToTab(listTargets, tabTarget);

            grpTargetAutoStart = new GroupBox();
            grpTargetAutoStart.Text = "Target auto-start";
            grpTargetAutoStart.Location = new Point(8, 164);
            grpTargetAutoStart.Size = new Size(470, 124);

            chkRestoreConfiguration = new CheckBox();
            chkRestoreConfiguration.Text = "Restore this target and start the server when iSCSI Console starts";
            chkRestoreConfiguration.Location = new Point(14, 22);
            chkRestoreConfiguration.Size = new Size(442, 17);
            chkRestoreConfiguration.CheckedChanged += new EventHandler(OnTargetOptionsChanged);

            btnEnableTargetAutoStart = new Button();
            btnEnableTargetAutoStart.Text = "Enable target auto-start";
            btnEnableTargetAutoStart.Location = new Point(14, 50);
            btnEnableTargetAutoStart.Size = new Size(150, 23);
            btnEnableTargetAutoStart.Click += new EventHandler(btnEnableTargetAutoStart_Click);

            btnRemoveTargetAutoStart = new Button();
            btnRemoveTargetAutoStart.Text = "Remove target auto-start";
            btnRemoveTargetAutoStart.Location = new Point(172, 50);
            btnRemoveTargetAutoStart.Size = new Size(150, 23);
            btnRemoveTargetAutoStart.Click += new EventHandler(btnRemoveTargetAutoStart_Click);

            lblTargetAutoStartState = new Label();
            lblTargetAutoStartState.Location = new Point(14, 80);
            lblTargetAutoStartState.Size = new Size(442, 16);
            lblTargetAutoStartState.AutoEllipsis = true;
            lblTargetAutoStartState.Text = "Windows logon: disabled.";

            Label targetHint = new Label();
            targetHint.Location = new Point(14, 98);
            targetHint.Size = new Size(442, 16);
            targetHint.ForeColor = SystemColors.GrayText;
            targetHint.Text = "The server must stay non-elevated to access NAS shares.";

            grpTargetAutoStart.Controls.Add(chkRestoreConfiguration);
            grpTargetAutoStart.Controls.Add(btnEnableTargetAutoStart);
            grpTargetAutoStart.Controls.Add(btnRemoveTargetAutoStart);
            grpTargetAutoStart.Controls.Add(lblTargetAutoStartState);
            grpTargetAutoStart.Controls.Add(targetHint);
            tabTarget.Controls.Add(grpTargetAutoStart);

            grpInitiatorSummary = new GroupBox();
            grpInitiatorSummary.Text = "Configured disk";
            grpInitiatorSummary.Location = new Point(8, 8);
            grpInitiatorSummary.Size = new Size(470, 92);

            lblInitiatorDisk = new Label();
            lblInitiatorDisk.Location = new Point(14, 20);
            lblInitiatorDisk.Size = new Size(442, 16);
            lblInitiatorDisk.AutoEllipsis = true;
            lblInitiatorDisk.Text = "Virtual disk: (not configured)";

            lblInitiatorTarget = new Label();
            lblInitiatorTarget.Location = new Point(14, 42);
            lblInitiatorTarget.Size = new Size(442, 16);
            lblInitiatorTarget.AutoEllipsis = true;
            lblInitiatorTarget.Text = "Target IQN: (not configured)";

            lblInitiatorPortal = new Label();
            lblInitiatorPortal.Location = new Point(14, 64);
            lblInitiatorPortal.Size = new Size(442, 16);
            lblInitiatorPortal.Text = "Portal: 127.0.0.1:3260";

            grpInitiatorSummary.Controls.Add(lblInitiatorDisk);
            grpInitiatorSummary.Controls.Add(lblInitiatorTarget);
            grpInitiatorSummary.Controls.Add(lblInitiatorPortal);
            tabInitiator.Controls.Add(grpInitiatorSummary);

            grpInitiatorAutoStart = new GroupBox();
            grpInitiatorAutoStart.Text = "Connection and automatic startup";
            grpInitiatorAutoStart.Location = new Point(8, 104);
            grpInitiatorAutoStart.Size = new Size(470, 180);

            chkAutoConnect = new CheckBox();
            chkAutoConnect.Text = "Connect Windows automatically at logon (persistent) and mount the disk";
            chkAutoConnect.Location = new Point(14, 20);
            chkAutoConnect.Size = new Size(442, 17);
            chkAutoConnect.CheckedChanged += new EventHandler(OnInitiatorOptionsChanged);

            chkInitializeDisk = new CheckBox();
            chkInitializeDisk.Text = "Initialize / format the iSCSI disk if it is unformatted";
            chkInitializeDisk.Location = new Point(14, 42);
            chkInitializeDisk.Size = new Size(442, 17);
            chkInitializeDisk.CheckedChanged += new EventHandler(OnInitiatorOptionsChanged);

            lblInitiatorAutoStartState = new Label();
            lblInitiatorAutoStartState.Location = new Point(14, 66);
            lblInitiatorAutoStartState.Size = new Size(442, 16);
            lblInitiatorAutoStartState.AutoEllipsis = true;
            lblInitiatorAutoStartState.Text = "Windows logon task: not installed.";

            btnConnectNow = new Button();
            btnConnectNow.Text = "Connect now";
            btnConnectNow.Location = new Point(14, 88);
            btnConnectNow.Size = new Size(110, 23);
            btnConnectNow.Click += new EventHandler(btnConnectNow_Click);

            btnDisconnect = new Button();
            btnDisconnect.Text = "Disconnect and unmount";
            btnDisconnect.Location = new Point(132, 88);
            btnDisconnect.Size = new Size(160, 23);
            btnDisconnect.Click += new EventHandler(btnDisconnect_Click);

            btnOpenIscsiPanel = new Button();
            btnOpenIscsiPanel.Text = "Open iSCSI panel";
            btnOpenIscsiPanel.Location = new Point(300, 88);
            btnOpenIscsiPanel.Size = new Size(166, 23);
            btnOpenIscsiPanel.Click += new EventHandler(btnOpenIscsiPanel_Click);

            btnEnableInitiatorAutoStart = new Button();
            btnEnableInitiatorAutoStart.Text = "Enable initiator auto-start";
            btnEnableInitiatorAutoStart.Location = new Point(14, 116);
            btnEnableInitiatorAutoStart.Size = new Size(160, 23);
            btnEnableInitiatorAutoStart.Click += new EventHandler(btnEnableInitiatorAutoStart_Click);

            btnRemoveInitiatorAutoStart = new Button();
            btnRemoveInitiatorAutoStart.Text = "Remove initiator auto-start";
            btnRemoveInitiatorAutoStart.Location = new Point(182, 116);
            btnRemoveInitiatorAutoStart.Size = new Size(170, 23);
            btnRemoveInitiatorAutoStart.Click += new EventHandler(btnRemoveInitiatorAutoStart_Click);

            lblInitiatorHint = new Label();
            lblInitiatorHint.Location = new Point(14, 144);
            lblInitiatorHint.Size = new Size(442, 30);
            lblInitiatorHint.ForeColor = SystemColors.GrayText;
            lblInitiatorHint.Text = "Disconnect removes the persistent login and unmounts the disk. Close Disk Management if Windows reports that the device is in use.";

            grpInitiatorAutoStart.Controls.Add(chkAutoConnect);
            grpInitiatorAutoStart.Controls.Add(chkInitializeDisk);
            grpInitiatorAutoStart.Controls.Add(lblInitiatorAutoStartState);
            grpInitiatorAutoStart.Controls.Add(btnConnectNow);
            grpInitiatorAutoStart.Controls.Add(btnDisconnect);
            grpInitiatorAutoStart.Controls.Add(btnOpenIscsiPanel);
            grpInitiatorAutoStart.Controls.Add(btnEnableInitiatorAutoStart);
            grpInitiatorAutoStart.Controls.Add(btnRemoveInitiatorAutoStart);
            grpInitiatorAutoStart.Controls.Add(lblInitiatorHint);
            tabInitiator.Controls.Add(grpInitiatorAutoStart);

            chkMinimizeToTray = new CheckBox();
            chkMinimizeToTray.Text = "Minimize to the notification area when I close the window";
            chkMinimizeToTray.Location = new Point(14, 296);
            chkMinimizeToTray.Size = new Size(470, 17);
            chkMinimizeToTray.CheckedChanged += new EventHandler(OnTargetOptionsChanged);
            tabTarget.Controls.Add(chkMinimizeToTray);

            CreateTrayIcon();

            this.Shown += new EventHandler(MainForm_Shown);
            this.FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
            this.FormClosed += new FormClosedEventHandler(MainForm_FormClosed);

            this.ResumeLayout();
        }

        private void MoveToTab(Control control, TabPage page)
        {
            this.Controls.Remove(control);
            page.Controls.Add(control);
        }

        private void ApplySettingsToUI()
        {
            m_loading = true;
            try
            {
                if (m_settings.Port > 0)
                {
                    txtPort.Text = m_settings.Port.ToString();
                }

                int selectedIndex = 0;
                for (int index = 0; index < comboIPAddress.Items.Count; index++)
                {
                    KeyValuePair<string, IPAddress> item = (KeyValuePair<string, IPAddress>)comboIPAddress.Items[index];
                    if (String.Equals(item.Key, m_settings.ListenAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = index;
                        break;
                    }
                }
                comboIPAddress.SelectedIndex = selectedIndex;

                chkRestoreConfiguration.Checked = m_settings.AutoStart;
                chkAutoConnect.Checked = m_settings.AutoConnect;
                chkInitializeDisk.Checked = m_settings.InitializeIfRaw;
                chkMinimizeToTray.Checked = m_settings.MinimizeToTray;
            }
            finally
            {
                m_loading = false;
            }

            UpdateInitiatorSummary();
            UpdateAutoStartStateLabels();
        }

        private static void ApplyCommandLineOverrides(CommandLineOptions options, AppSettings settings)
        {
            if (options.HasDiskImagePath)
            {
                settings.DiskImagePath = options.DiskImagePath;
                settings.AutoStart = true;
            }
            if (options.HasTargetIQN)
            {
                settings.TargetIQN = options.TargetIQN;
            }
            if (options.HasListenAddress)
            {
                settings.ListenAddress = options.ListenAddress;
            }
            if (options.HasPortalAddress)
            {
                settings.PortalAddress = options.PortalAddress;
            }
            if (options.HasPort)
            {
                settings.Port = options.Port;
            }
            if (options.ReadOnly)
            {
                settings.ReadOnly = true;
            }
            if (options.AutoStart)
            {
                settings.AutoStart = true;
            }
            if (options.Setup)
            {
                settings.AutoStart = true;
                settings.AutoConnect = true;
            }
            if (options.AutoConnect)
            {
                settings.AutoConnect = true;
            }
            if (options.InitializeIfRaw)
            {
                settings.InitializeIfRaw = true;
            }
            if (options.HasFileSystem)
            {
                settings.FileSystem = options.FileSystem;
            }
            if (options.HasDriveLetter)
            {
                settings.DriveLetter = options.DriveLetter;
            }

            if (!String.IsNullOrEmpty(settings.DiskImagePath) && String.IsNullOrEmpty(settings.TargetIQN))
            {
                settings.TargetIQN = AddTargetForm.DefaultTargetIQN + ":target1";
            }
            if (String.IsNullOrEmpty(settings.PortalAddress))
            {
                settings.PortalAddress = settings.ListenAddress == "Any" ? "127.0.0.1" : settings.ListenAddress;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (!m_started)
            {
                if (!TryStartServer(true))
                {
                    return;
                }
                UpdateUI();
                SaveConfiguration();
                UpdateInitiatorSummary();
            }
            else
            {
                m_server.Stop();
                lblStatus.Text = String.Empty;
                m_started = false;
                btnStart.Text = "Start";
                txtPort.Enabled = true;
                comboIPAddress.Enabled = true;
            }
        }

        private bool TryStartServer(bool showError)
        {
            IPAddress serverAddress = IPAddress.Any;
            if (comboIPAddress.SelectedItem is KeyValuePair<string, IPAddress>)
            {
                serverAddress = ((KeyValuePair<string, IPAddress>)comboIPAddress.SelectedItem).Value;
            }

            int port = Conversion.ToInt32(txtPort.Text, 0);
            if (port <= 0 || port > UInt16.MaxValue)
            {
                if (showError)
                {
                    MessageBox.Show("Invalid TCP port", "Error");
                }
                return false;
            }

            IPEndPoint endpoint = new IPEndPoint(serverAddress, port);
            try
            {
                m_server.Start(endpoint);
            }
            catch (SocketException ex)
            {
                if (showError)
                {
                    MessageBox.Show("Cannot start server, " + ex.Message, "Error");
                }
                return false;
            }

            btnStart.Text = "Stop";
            txtPort.Enabled = false;
            comboIPAddress.Enabled = false;
            m_started = true;
            return true;
        }

        private void RestoreConfiguration()
        {
            if (!m_started && !TryStartServer(false))
            {
                lblStatus.Text = "Could not start the iSCSI server, the saved target was not restored.";
                return;
            }

            try
            {
                DiskImage diskImage = DiskImage.GetDiskImage(m_settings.DiskImagePath, m_settings.ReadOnly);
                if (!diskImage.ExclusiveLock())
                {
                    throw new IOException("Cannot lock the disk image for exclusive access.");
                }

                ISCSITarget target = new ISCSITarget(m_settings.TargetIQN, new List<Disk>(new Disk[] { diskImage }));
                if (!AddTargetToServer(target, false))
                {
                    LockUtils.ReleaseDisk(diskImage);
                    return;
                }

                UpdateUI();
                lblStatus.Text = "Restored target " + target.TargetName;
                UpdateInitiatorSummary();
                UpdateAutoStartStateLabels();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Could not restore the saved target: " + ex.Message;
                UpdateAutoStartStateLabels();
            }
        }

        private void btnAddTarget_Click(object sender, EventArgs e)
        {
            AddTargetForm addTarget = new AddTargetForm();
            DialogResult addTargetResult = addTarget.ShowDialog();
            if (addTargetResult == DialogResult.OK)
            {
                ISCSITarget target = addTarget.Target;
                if (!AddTargetToServer(target, true))
                {
                    return;
                }

                SyncSettingsFromTarget(target);
                m_loading = true;
                chkRestoreConfiguration.Checked = true;
                m_loading = false;
                SaveConfiguration();
                UpdateUI();
                UpdateInitiatorSummary();
                UpdateAutoStartStateLabels();
            }
        }

        private bool AddTargetToServer(ISCSITarget target, bool showError)
        {
            ((SCSI.VirtualSCSITarget)target.SCSITarget).OnLogEntry += Program.OnLogEntry;
            target.OnAuthorizationRequest += new EventHandler<AuthorizationRequestArgs>(ISCSITarget_OnAuthorizationRequest);
            target.OnSessionTermination += new EventHandler<SessionTerminationArgs>(ISCSITarget_OnSessionTermination);

            try
            {
                m_server.AddTarget(target);
            }
            catch (ArgumentException ex)
            {
                if (showError)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
                return false;
            }

            m_targets.Add(target);
            listTargets.Items.Add(target.TargetName);
            return true;
        }

        private void btnRemoveTarget_Click(object sender, EventArgs e)
        {
            if (listTargets.SelectedIndices.Count > 0)
            {
                int targetIndex = listTargets.SelectedIndices[0];
                ISCSITarget target = m_targets[targetIndex];
                bool isTargetRemoved = m_server.RemoveTarget(target.TargetName);
                if (!isTargetRemoved)
                {
                    MessageBox.Show("Could not remove iSCSI target", "Error");
                    return;
                }
                List<Disk> disks = ((SCSI.VirtualSCSITarget)target.SCSITarget).Disks;
                LockUtils.ReleaseDisks(disks);
                m_targets.RemoveAt(targetIndex);
                listTargets.Items.RemoveAt(targetIndex);

                if (target.TargetName == m_settings.TargetIQN)
                {
                    m_settings.DiskImagePath = String.Empty;
                    m_settings.TargetIQN = String.Empty;
                    m_settings.DiskSize = 0;
                    m_settings.AutoStart = false;
                    m_loading = true;
                    chkRestoreConfiguration.Checked = false;
                    m_loading = false;
                    AutoStartHelper.DeleteStartupShortcut(new Reporter(null));
                }
                SaveConfiguration();
                UpdateInitiatorSummary();
                UpdateAutoStartStateLabels();
            }
        }

        private void SetupAllAutoStart()
        {
            if (!EnsureTargetConfigured(true))
            {
                return;
            }

            if (m_targets.Count == 0 && !String.IsNullOrEmpty(m_settings.DiskImagePath))
            {
                RestoreConfiguration();
            }
            if (m_targets.Count == 0)
            {
                MessageBox.Show("Start the server and add the target first.", "iSCSI Console");
                return;
            }
            if (!m_started && !TryStartServer(true))
            {
                return;
            }

            SetTargetAutoStart(true);
            string shortcutError;
            if (!AutoStartHelper.CreateStartupShortcut(out shortcutError))
            {
                MessageBox.Show("Could not create the startup shortcut: " + shortcutError, "Warning");
            }

            SetInitiatorAutoStart(true);
            RunInitiatorTaskAction("setup", true, true);
        }

        private void btnEnableTargetAutoStart_Click(object sender, EventArgs e)
        {
            if (!EnsureTargetConfigured(true))
            {
                return;
            }

            SetTargetAutoStart(true);
            string error;
            if (!AutoStartHelper.CreateStartupShortcut(out error))
            {
                SetTargetAutoStart(false);
                MessageBox.Show("Could not create the startup shortcut: " + error, "Warning");
                return;
            }

            UpdateAutoStartStateLabels();
            lblStatus.Text = "Target auto-start enabled. The target will be restored at Windows logon.";
        }

        private void btnRemoveTargetAutoStart_Click(object sender, EventArgs e)
        {
            SetTargetAutoStart(false);
            Reporter reporter = new Reporter(null);
            AutoStartHelper.DeleteStartupShortcut(reporter);
            UpdateAutoStartStateLabels();
            if (AutoStartHelper.StartupShortcutExists())
            {
                MessageBox.Show("Could not remove the startup shortcut.\r\n" + reporter.Text, "Warning");
            }
            else
            {
                lblStatus.Text = "Target auto-start removed.";
            }
        }

        private void SetTargetAutoStart(bool enabled)
        {
            m_loading = true;
            chkRestoreConfiguration.Checked = enabled;
            m_loading = false;
            m_settings.AutoStart = enabled;
            SaveConfiguration();
            UpdateAutoStartStateLabels();
        }

        private void btnEnableInitiatorAutoStart_Click(object sender, EventArgs e)
        {
            EnableInitiatorAutoStart(false);
        }

        private void EnableInitiatorAutoStart(bool connectNow)
        {
            if (m_installingInitiatorTask || m_connecting)
            {
                return;
            }
            if (!EnsureTargetConfigured(true))
            {
                return;
            }

            SetInitiatorAutoStart(true);
            RunInitiatorTaskAction(connectNow ? "setup" : "install-initiator", true, connectNow);
        }

        private void btnRemoveInitiatorAutoStart_Click(object sender, EventArgs e)
        {
            if (m_installingInitiatorTask || m_connecting)
            {
                return;
            }
            RunInitiatorTaskAction("uninstall-initiator", false, false);
        }

        private void SetInitiatorAutoStart(bool enabled)
        {
            m_loading = true;
            chkAutoConnect.Checked = enabled;
            m_loading = false;
            m_settings.AutoConnect = enabled;
            SaveConfiguration();
            UpdateAutoStartStateLabels();
        }

        private void RunInitiatorTaskAction(string action, bool enabling, bool showReport)
        {
            m_installingInitiatorTask = true;
            SetInitiatorButtonsEnabled(false);
            lblStatus.Text = enabling
                ? "Configuring the Windows logon task (administrator consent may be required)..."
                : "Removing the Windows logon task (administrator consent may be required)...";
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string report;
                bool success = AutoConnectHelper.RunHeadless(action, out report);
                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            OnInitiatorTaskActionCompleted(action, enabling, success, report, showReport);
                        });
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnInitiatorTaskActionCompleted(string action, bool enabling, bool success, string report, bool showReport)
        {
            m_installingInitiatorTask = false;
            SetInitiatorButtonsEnabled(true);

            if (enabling)
            {
                bool taskInstalled = success || (action == "setup" && report.IndexOf("TASK: INSTALLED", StringComparison.OrdinalIgnoreCase) >= 0);
                m_settings.ScheduledTaskInstalled = taskInstalled;
                if (!taskInstalled)
                {
                    m_settings.AutoConnect = false;
                    m_loading = true;
                    chkAutoConnect.Checked = false;
                    m_loading = false;
                }
                SaveConfiguration();
                UpdateInitiatorSummary();
                UpdateAutoStartStateLabels();
                lblStatus.Text = success
                    ? (action == "setup" ? "Automatic startup configured and Windows initiator connected." : "Initiator auto-start enabled.")
                    : "Could not enable the initiator auto-start.";
                if (!success || showReport)
                {
                    MessageBox.Show(report, success ? "iSCSI Console" : "Automatic startup error");
                }
            }
            else
            {
                if (success)
                {
                    m_settings.ScheduledTaskInstalled = false;
                    m_settings.AutoConnect = false;
                    m_loading = true;
                    chkAutoConnect.Checked = false;
                    m_loading = false;
                }
                SaveConfiguration();
                UpdateInitiatorSummary();
                UpdateAutoStartStateLabels();
                lblStatus.Text = success ? "Initiator auto-start removed." : "Could not remove the initiator auto-start.";
                if (!success)
                {
                    MessageBox.Show(report, "Automatic startup error");
                }
            }
        }

        private bool EnsureTargetConfigured(bool showMessage)
        {
            if (m_targets.Count > 0)
            {
                SyncSettingsFromTarget(m_targets[0]);
            }
            if (String.IsNullOrEmpty(m_settings.DiskImagePath) || String.IsNullOrEmpty(m_settings.TargetIQN))
            {
                if (showMessage)
                {
                    MessageBox.Show("Add the NAS virtual disk to a target first.", "iSCSI Console");
                }
                return false;
            }
            return true;
        }

        private void SetInitiatorButtonsEnabled(bool enabled)
        {
            chkAutoConnect.Enabled = enabled;
            chkInitializeDisk.Enabled = enabled;
            btnEnableInitiatorAutoStart.Enabled = enabled;
            btnConnectNow.Enabled = enabled;
            btnDisconnect.Enabled = enabled;
            btnRemoveInitiatorAutoStart.Enabled = enabled;
        }

        private void btnConnectNow_Click(object sender, EventArgs e)
        {
            if (m_targets.Count == 0)
            {
                MessageBox.Show("Start the server and add the target first.", "iSCSI Console");
                return;
            }
            SyncSettingsFromTarget(m_targets[0]);
            SaveConfiguration();
            ConnectWindowsAsync(true);
        }

        private void ConnectWindowsAsync(bool showReport)
        {
            if (m_connecting || m_installingInitiatorTask)
            {
                return;
            }
            m_connecting = true;
            SetInitiatorButtonsEnabled(false);
            lblStatus.Text = "Connecting the Windows iSCSI initiator (administrator consent may be required)...";
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string report;
                bool success = AutoConnectHelper.RunHeadless("connect", out report);
                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            OnConnectCompleted(success, report, showReport);
                        });
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnConnectCompleted(bool success, string report, bool showReport)
        {
            m_connecting = false;
            SetInitiatorButtonsEnabled(true);
            UpdateInitiatorSummary();
            if (success)
            {
                lblStatus.Text = "Windows initiator connected and disk mounted.";
            }
            else
            {
                lblStatus.Text = "Automatic Windows connection failed.";
            }
            if (showReport)
            {
                MessageBox.Show(report, success ? "iSCSI Console" : "Connection error");
            }
        }

        private void btnOpenIscsiPanel_Click(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("iscsicpl.exe");
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
                Process.Start(startInfo);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223)
                {
                    MessageBox.Show("Could not open the Windows iSCSI panel: " + ex.Message, "iSCSI Console");
                }
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            if (m_connecting || m_installingInitiatorTask)
            {
                return;
            }
            if (!EnsureTargetConfigured(true))
            {
                return;
            }
            SaveConfiguration();
            DisconnectWindowsAsync();
        }

        private void DisconnectWindowsAsync()
        {
            if (m_connecting || m_installingInitiatorTask)
            {
                return;
            }
            m_connecting = true;
            SetInitiatorButtonsEnabled(false);
            lblStatus.Text = "Disconnecting the Windows iSCSI initiator (administrator consent may be required)...";
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string report;
                bool success = AutoConnectHelper.RunHeadless("disconnect", out report);
                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            OnDisconnectCompleted(success, report);
                        });
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnDisconnectCompleted(bool success, string report)
        {
            m_connecting = false;
            SetInitiatorButtonsEnabled(true);
            UpdateInitiatorSummary();
            if (success)
            {
                lblStatus.Text = m_settings.AutoConnect
                    ? "Windows initiator disconnected. It will reconnect at the next Windows logon."
                    : "Windows initiator disconnected and disk unmounted.";
            }
            else
            {
                lblStatus.Text = "Could not disconnect the Windows initiator.";
            }
            MessageBox.Show(report, success ? "iSCSI Console" : "Disconnect error");
        }

        private void OnTargetOptionsChanged(object sender, EventArgs e)
        {
            if (m_loading)
            {
                return;
            }
            m_settings.AutoStart = chkRestoreConfiguration.Checked;
            if (m_settings.MinimizeToTray != chkMinimizeToTray.Checked)
            {
                // Changing the option by hand counts as an answer to the close prompt.
                m_settings.MinimizeToTray = chkMinimizeToTray.Checked;
                m_settings.ClosePromptAnswered = true;
            }
            SaveConfiguration();
            UpdateAutoStartStateLabels();
        }

        private void OnInitiatorOptionsChanged(object sender, EventArgs e)
        {
            if (m_loading)
            {
                return;
            }
            m_settings.AutoConnect = chkAutoConnect.Checked;
            m_settings.InitializeIfRaw = chkInitializeDisk.Checked;
            SaveConfiguration();
            UpdateAutoStartStateLabels();
        }

        private void UpdateInitiatorSummary()
        {
            if (m_settings == null || lblInitiatorDisk == null)
            {
                return;
            }
            string disk = String.IsNullOrEmpty(m_settings.DiskImagePath) ? "(not configured)" : m_settings.DiskImagePath;
            string iqn = String.IsNullOrEmpty(m_settings.TargetIQN) ? "(not configured)" : m_settings.TargetIQN;
            string portalAddress = !String.IsNullOrEmpty(m_settings.PortalAddress)
                ? m_settings.PortalAddress
                : (m_settings.ListenAddress == "Any" ? "127.0.0.1" : m_settings.ListenAddress);
            int port = m_settings.Port > 0 ? m_settings.Port : 3260;
            lblInitiatorDisk.Text = "Virtual disk: " + disk;
            lblInitiatorTarget.Text = "Target IQN: " + iqn;
            lblInitiatorPortal.Text = String.Format("Portal: {0}:{1}", portalAddress, port);
        }

        private void UpdateAutoStartStateLabels()
        {
            if (m_settings == null || lblTargetAutoStartState == null)
            {
                return;
            }
            bool shortcutInstalled = AutoStartHelper.StartupShortcutExists();
            if (m_settings.AutoStart && shortcutInstalled)
            {
                lblTargetAutoStartState.Text = "Windows logon: enabled (startup shortcut installed).";
            }
            else if (m_settings.AutoStart)
            {
                lblTargetAutoStartState.Text = "Windows logon: restore enabled, but the startup shortcut is missing.";
            }
            else if (shortcutInstalled)
            {
                lblTargetAutoStartState.Text = "Windows logon: startup shortcut present, but restore is disabled.";
            }
            else
            {
                lblTargetAutoStartState.Text = "Windows logon: disabled.";
            }

            if (m_settings.AutoConnect && m_settings.ScheduledTaskInstalled)
            {
                lblInitiatorAutoStartState.Text = "Windows logon task: installed (persistent login and mount).";
            }
            else if (m_settings.AutoConnect)
            {
                lblInitiatorAutoStartState.Text = "Windows logon task: not installed. Click 'Enable initiator auto-start'.";
            }
            else if (m_settings.ScheduledTaskInstalled)
            {
                lblInitiatorAutoStartState.Text = "Windows logon task: installed, but automatic connection is disabled.";
            }
            else
            {
                lblInitiatorAutoStartState.Text = "Windows logon task: not installed.";
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfiguration();
            if (m_exitRequested || e.CloseReason != CloseReason.UserClosing)
            {
                return;
            }

            if (!m_settings.ClosePromptAnswered)
            {
                CloseActionDialog dialog = new CloseActionDialog();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }

                if (dialog.RememberChoice)
                {
                    m_settings.MinimizeToTray = dialog.MinimizeToTray;
                    m_settings.ClosePromptAnswered = true;
                    m_loading = true;
                    chkMinimizeToTray.Checked = m_settings.MinimizeToTray;
                    m_loading = false;
                    SaveConfiguration();
                }

                if (dialog.MinimizeToTray)
                {
                    e.Cancel = true;
                    HideToTray();
                }
                return;
            }

            if (m_settings.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (m_trayIcon != null)
            {
                m_trayIcon.Visible = false;
                m_trayIcon.Dispose();
                m_trayIcon = null;
            }
            if (m_trayMenu != null)
            {
                m_trayMenu.Dispose();
                m_trayMenu = null;
            }
        }

        private void CreateTrayIcon()
        {
            ToolStripMenuItem openItem = new ToolStripMenuItem("Open iSCSI Console");
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            openItem.Click += delegate(object sender, EventArgs e)
            {
                ShowFromTray();
            };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Close iSCSI Console");
            exitItem.Click += delegate(object sender, EventArgs e)
            {
                ExitApplication();
            };

            m_trayMenu = new ContextMenuStrip();
            m_trayMenu.Items.Add(openItem);
            m_trayMenu.Items.Add(new ToolStripSeparator());
            m_trayMenu.Items.Add(exitItem);

            m_trayIcon = new NotifyIcon();
            m_trayIcon.Text = "iSCSI Console";
            m_trayIcon.Icon = this.Icon != null ? this.Icon : SystemIcons.Application;
            m_trayIcon.ContextMenuStrip = m_trayMenu;
            m_trayIcon.DoubleClick += delegate(object sender, EventArgs e)
            {
                ShowFromTray();
            };
            m_trayIcon.Visible = true;
        }

        private void ShowFromTray()
        {
            Show();
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            Activate();
        }

        private void HideToTray()
        {
            Hide();
            if (!m_trayBalloonShown && m_trayIcon != null)
            {
                m_trayBalloonShown = true;
                m_trayIcon.ShowBalloonTip(3000, "iSCSI Console", "The server is still running. Right-click the tray icon to close it.", ToolTipIcon.Info);
            }
        }

        private void ExitApplication()
        {
            m_exitRequested = true;
            Close();
        }

        private void SaveConfiguration()
        {
            if (m_loading)
            {
                return;
            }
            UpdateSettingsFromUI();
            if (m_targets.Count > 0)
            {
                SyncSettingsFromTarget(m_targets[0]);
            }
            m_settings.Save();
        }

        private void UpdateSettingsFromUI()
        {
            int port;
            if (Int32.TryParse(txtPort.Text.Trim(), out port) && port > 0 && port <= UInt16.MaxValue)
            {
                m_settings.Port = port;
            }

            if (comboIPAddress.SelectedItem is KeyValuePair<string, IPAddress>)
            {
                KeyValuePair<string, IPAddress> selected = (KeyValuePair<string, IPAddress>)comboIPAddress.SelectedItem;
                m_settings.ListenAddress = selected.Key;
                if (!m_options.HasPortalAddress)
                {
                    m_settings.PortalAddress = selected.Key == "Any" ? "127.0.0.1" : selected.Key;
                }
            }

            m_settings.AutoStart = chkRestoreConfiguration.Checked;
            m_settings.AutoConnect = chkAutoConnect.Checked;
            m_settings.InitializeIfRaw = chkInitializeDisk.Checked;
            m_settings.MinimizeToTray = chkMinimizeToTray.Checked;
        }

        private void SyncSettingsFromTarget(ISCSITarget target)
        {
            m_settings.TargetIQN = target.TargetName;
            List<Disk> disks = ((SCSI.VirtualSCSITarget)target.SCSITarget).Disks;
            foreach (Disk disk in disks)
            {
                DiskImage diskImage = disk as DiskImage;
                if (diskImage != null)
                {
                    m_settings.DiskImagePath = diskImage.Path;
                    m_settings.ReadOnly = diskImage.IsReadOnly;
                    m_settings.DiskSize = diskImage.Size;
                    break;
                }
            }
        }

        private void ISCSITarget_OnAuthorizationRequest(object sender, AuthorizationRequestArgs e)
        {
            string targetName = ((ISCSITarget)sender).TargetName;
            m_usageCounter.NotifySessionStart(targetName);
            UpdateUI();
        }

        private void ISCSITarget_OnSessionTermination(object sender, SessionTerminationArgs e)
        {
            string targetName = ((ISCSITarget)sender).TargetName;
            m_usageCounter.NotifySessionTermination(targetName);
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)UpdateUI);
            }
            else
            {
                if (m_started)
                {
                    lblStatus.Text = String.Format("{0} Active Sessions", m_usageCounter.SessionCount);
                }

                if (listTargets.SelectedIndices.Count > 0)
                {
                    int targetIndex = listTargets.SelectedIndices[0];
                    ISCSITarget target = m_targets[targetIndex];
                    bool isInUse = m_usageCounter.IsTargetInUse(target.TargetName);
                    btnRemoveTarget.Enabled = !isInUse;
                }
                else
                {
                    btnRemoveTarget.Enabled = false;
                }
            }
        }

        private static List<IPAddress> GetHostIPAddresses()
        {
            List<IPAddress> result = new List<IPAddress>();
            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties ipProperties = netInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation addressInfo in ipProperties.UnicastAddresses)
                {
                    if (addressInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        result.Add(addressInfo.Address);
                    }
                }
            }
            return result;
        }

        private void listTargets_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUI();
        }
    }
}
