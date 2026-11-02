/* Copyright (C) 2016 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ISCSIConsole
{
    /// <summary>
    /// Persisted configuration: the VHD to serve, the target name, and whether to
    /// restore / connect / mount it automatically at startup.
    /// </summary>
    public class AppSettings
    {
        public const string SettingsFileName = "ISCSIConsole.settings.ini";

        public string DiskImagePath = String.Empty;
        public string TargetIQN = String.Empty;
        public string ListenAddress = "Any";
        public string PortalAddress = "127.0.0.1";
        public int Port = 3260;
        public bool ReadOnly = false;
        public long DiskSize = 0;
        public bool AutoStart = false;
        public bool AutoConnect = false;
        public bool InitializeIfRaw = false;
        public string FileSystem = "NTFS";
        public string DriveLetter = String.Empty;
        public bool ScheduledTaskInstalled = false;
        public bool MinimizeToTray = false;
        public bool ClosePromptAnswered = false;

        public static string PortableSettingsPath
        {
            get
            {
                string directory = GetExecutableDirectory();
                return Path.Combine(directory, SettingsFileName);
            }
        }

        public static string RoamingSettingsPath
        {
            get
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iSCSIConsole");
                return Path.Combine(directory, SettingsFileName);
            }
        }

        public static string GetExecutableDirectory()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly == null || String.IsNullOrEmpty(assembly.Location))
            {
                return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            }
            return Path.GetDirectoryName(assembly.Location);
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            string path = null;
            if (File.Exists(PortableSettingsPath))
            {
                path = PortableSettingsPath;
            }
            else if (File.Exists(RoamingSettingsPath))
            {
                path = RoamingSettingsPath;
            }

            if (path == null)
            {
                return settings;
            }

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    settings.ApplyLine(line);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return settings;
        }

        public bool Save()
        {
            try
            {
                WriteTo(PortableSettingsPath);
                return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            try
            {
                string directory = Path.GetDirectoryName(RoamingSettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                WriteTo(RoamingSettingsPath);
                return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return false;
        }

        private void WriteTo(string path)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("DiskImagePath=" + DiskImagePath);
            builder.AppendLine("TargetIQN=" + TargetIQN);
            builder.AppendLine("ListenAddress=" + ListenAddress);
            builder.AppendLine("PortalAddress=" + PortalAddress);
            builder.AppendLine("Port=" + Port.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("ReadOnly=" + BoolToString(ReadOnly));
            builder.AppendLine("DiskSize=" + DiskSize.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("AutoStart=" + BoolToString(AutoStart));
            builder.AppendLine("AutoConnect=" + BoolToString(AutoConnect));
            builder.AppendLine("InitializeIfRaw=" + BoolToString(InitializeIfRaw));
            builder.AppendLine("FileSystem=" + FileSystem);
            builder.AppendLine("DriveLetter=" + DriveLetter);
            builder.AppendLine("ScheduledTaskInstalled=" + BoolToString(ScheduledTaskInstalled));
            builder.AppendLine("MinimizeToTray=" + BoolToString(MinimizeToTray));
            builder.AppendLine("ClosePromptAnswered=" + BoolToString(ClosePromptAnswered));
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }

        private void ApplyLine(string line)
        {
            if (line == null)
            {
                return;
            }
            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return;
            }
            string key = line.Substring(0, separator).Trim();
            string value = line.Substring(separator + 1).Trim();
            switch (key.ToLowerInvariant())
            {
                case "diskimagepath":
                    DiskImagePath = value;
                    break;
                case "targetiqn":
                    TargetIQN = value;
                    break;
                case "listenaddress":
                    ListenAddress = value;
                    break;
                case "portaladdress":
                    PortalAddress = value;
                    break;
                case "port":
                    int port;
                    if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                    {
                        Port = port;
                    }
                    break;
                case "readonly":
                    ReadOnly = StringToBool(value);
                    break;
                case "disksize":
                    long size;
                    if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                    {
                        DiskSize = size;
                    }
                    break;
                case "autostart":
                    AutoStart = StringToBool(value);
                    break;
                case "autoconnect":
                    AutoConnect = StringToBool(value);
                    break;
                case "initializeifraw":
                    InitializeIfRaw = StringToBool(value);
                    break;
                case "filesystem":
                    FileSystem = value;
                    break;
                case "driveletter":
                    DriveLetter = value;
                    break;
                case "scheduledtaskinstalled":
                    ScheduledTaskInstalled = StringToBool(value);
                    break;
                case "minimizetotray":
                    MinimizeToTray = StringToBool(value);
                    break;
                case "closepromptanswered":
                    ClosePromptAnswered = StringToBool(value);
                    break;
            }
        }

        public static string BoolToString(bool value)
        {
            return value ? "1" : "0";
        }

        public static bool StringToBool(string value)
        {
            if (value == null)
            {
                return false;
            }
            value = value.Trim();
            return (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
    }
}
