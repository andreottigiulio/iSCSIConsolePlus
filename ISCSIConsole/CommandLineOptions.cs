/* Copyright (C) 2016 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Globalization;
using System.Text;

namespace ISCSIConsole
{
    public class CommandLineOptions
    {
        public bool ShowHelp = false;
        public bool LogToFile = false;
        public bool Save = false;
        public bool Setup = false;
        public string HeadlessAction = null;
        public string ReportPath = null;

        public bool HasDiskImagePath = false;
        public string DiskImagePath = null;
        public bool HasTargetIQN = false;
        public string TargetIQN = null;
        public bool HasListenAddress = false;
        public string ListenAddress = null;
        public bool HasPort = false;
        public int Port = 0;
        public bool HasPortalAddress = false;
        public string PortalAddress = null;
        public bool ReadOnly = false;
        public bool AutoStart = false;
        public bool AutoConnect = false;
        public bool InitializeIfRaw = false;
        public bool HasFileSystem = false;
        public string FileSystem = null;
        public bool HasDriveLetter = false;
        public string DriveLetter = null;

        public string Error = null;

        public static CommandLineOptions Parse(string[] args)
        {
            CommandLineOptions options = new CommandLineOptions();
            if (args == null)
            {
                return options;
            }

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument == null)
                {
                    continue;
                }

                switch (argument.ToLowerInvariant())
                {
                    case "/help":
                    case "-h":
                    case "-?":
                    case "--help":
                        options.ShowHelp = true;
                        break;
                    case "/log":
                        options.LogToFile = true;
                        break;
                    case "/save":
                        options.Save = true;
                        break;
                    case "/setup":
                        options.Setup = true;
                        break;
                    case "/autostart":
                        options.AutoStart = true;
                        break;
                    case "/connect":
                        options.AutoConnect = true;
                        break;
                    case "/init":
                        options.InitializeIfRaw = true;
                        break;
                    case "/readonly":
                        options.ReadOnly = true;
                        break;
                    case "/vhd":
                        options.DiskImagePath = ReadValue(args, ref index, options);
                        options.HasDiskImagePath = true;
                        break;
                    case "/iqn":
                        options.TargetIQN = ReadValue(args, ref index, options);
                        options.HasTargetIQN = true;
                        break;
                    case "/listen":
                        options.ListenAddress = ReadValue(args, ref index, options);
                        options.HasListenAddress = true;
                        break;
                    case "/portal":
                        options.PortalAddress = ReadValue(args, ref index, options);
                        options.HasPortalAddress = true;
                        break;
                    case "/port":
                        string portValue = ReadValue(args, ref index, options);
                        int port;
                        if (Int32.TryParse(portValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port <= UInt16.MaxValue)
                        {
                            options.Port = port;
                            options.HasPort = true;
                        }
                        else if (portValue != null && portValue.Length > 0)
                        {
                            AppendError(options, "Invalid TCP port: " + portValue);
                        }
                        break;
                    case "/fs":
                        options.FileSystem = ReadValue(args, ref index, options);
                        options.HasFileSystem = true;
                        break;
                    case "/drive":
                        options.DriveLetter = ReadValue(args, ref index, options);
                        options.HasDriveLetter = true;
                        break;
                    case "/headless":
                        options.HeadlessAction = ReadValue(args, ref index, options);
                        if (options.HeadlessAction != null)
                        {
                            options.HeadlessAction = options.HeadlessAction.ToLowerInvariant();
                        }
                        break;
                    case "/report":
                        options.ReportPath = ReadValue(args, ref index, options);
                        break;
                    default:
                        AppendError(options, "Unknown command line argument: " + argument);
                        break;
                }
            }
            return options;
        }

        private static string ReadValue(string[] args, ref int index, CommandLineOptions options)
        {
            if (index + 1 >= args.Length)
            {
                AppendError(options, "Missing value after " + args[index]);
                return null;
            }
            index++;
            return args[index];
        }

        private static void AppendError(CommandLineOptions options, string message)
        {
            if (options.Error == null)
            {
                options.Error = message;
            }
            else
            {
                options.Error += Environment.NewLine + message;
            }
        }

        public static string GetUsage()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Usage: ISCSIConsole.exe [options]");
            builder.AppendLine();
            builder.AppendLine("Options:");
            builder.AppendLine("/vhd <path>       Virtual disk image to serve (local path or \\\\server\\share\\disk.vhd)");
            builder.AppendLine("/iqn <iqn>        Target IQN (default: iqn.1991-05.com.microsoft:target1)");
            builder.AppendLine("/listen <address> Server listen address (\"Any\" or a local IP address)");
            builder.AppendLine("/port <number>    TCP port (default: 3260)");
            builder.AppendLine("/portal <address> Portal address used by the local Windows initiator");
            builder.AppendLine("/readonly         Serve the disk image read-only");
            builder.AppendLine("/autostart        Target auto-start: start the server and recreate the target at launch");
            builder.AppendLine("/connect          Initiator auto-start: connect the Windows initiator and mount the disk at launch");
            builder.AppendLine("/init             Initialize and format the disk if it is unformatted");
            builder.AppendLine("/fs <filesystem>  Filesystem used by /init (default: NTFS)");
            builder.AppendLine("/drive <letter>   Preferred drive letter for the mounted disk");
            builder.AppendLine("/save             Save these options to " + AppSettings.SettingsFileName);
            builder.AppendLine("/setup            Save, enable both auto-starts and connect now");
            builder.AppendLine("/log              Write a log file to the executable directory");
            builder.AppendLine("/help             Show this help");
            builder.AppendLine();
            builder.AppendLine("Headless (used by the automatic startup task):");
            builder.AppendLine("/headless connect|disconnect|setup|uninstall [/report <path>]");
            return builder.ToString();
        }
    }
}
