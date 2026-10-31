/* Copyright (C) 2016 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace ISCSIConsole
{
    /// <summary>
    /// Actions that must run elevated (Windows iSCSI initiator and disk management)
    /// but that must NOT run inside the elevated instance of the GUI, because an
    /// elevated process cannot access the user's SMB sessions (NAS shares).
    /// The instance serving the VHD stays non-elevated, these helpers are short lived.
    /// </summary>
    public static class AutoConnectHelper
    {
        public const string TaskName = "iSCSIConsole AutoConnect";

        public static bool RunHeadless(string action, out string report)
        {
            report = String.Empty;
            if (SecurityHelper.IsAdministrator())
            {
                Reporter reporter = new Reporter(null);
                int exitCode = HeadlessActions.Run(action, AppSettings.Load(), reporter);
                report = reporter.Text;
                return exitCode == 0;
            }

            string reportPath = Path.Combine(Path.GetTempPath(), "iscsiconsole-" + Guid.NewGuid().ToString("N") + ".log");
            string executablePath = AutoStartHelper.GetExecutablePath();
            ProcessStartInfo startInfo = new ProcessStartInfo(executablePath, "/headless " + action + " /report \"" + reportPath + "\"");
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath);
            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    report = ReadReport(reportPath);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                report = "Windows did not grant administrator rights: " + ex.Message;
                return false;
            }
            finally
            {
                TryDelete(reportPath);
            }
        }

        private static string ReadReport(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch (IOException)
            {
            }
            return String.Empty;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public static class HeadlessActions
    {
        public static int Run(string action, AppSettings settings, Reporter reporter)
        {
            if (action == "connect")
            {
                return ConnectAndMount(settings, reporter);
            }
            else if (action == "install-initiator")
            {
                bool installed = AutoStartHelper.InstallScheduledTask(reporter);
                reporter.Write(installed ? "RESULT: OK" : "RESULT: FAILED");
                return installed ? 0 : 1;
            }
            else if (action == "uninstall-initiator")
            {
                bool removed = AutoStartHelper.RemoveScheduledTask(reporter);
                reporter.Write(removed ? "RESULT: OK" : "RESULT: FAILED");
                return removed ? 0 : 1;
            }
            else if (action == "disconnect")
            {
                int exitCode = DisconnectAndUnmount(settings, reporter);
                reporter.Write(exitCode == 0 ? "RESULT: OK" : "RESULT: FAILED");
                return exitCode;
            }
            else if (action == "setup")
            {
                bool taskInstalled = AutoStartHelper.InstallScheduledTask(reporter);
                reporter.Write(taskInstalled ? "TASK: INSTALLED" : "TASK: FAILED");
                if (!taskInstalled)
                {
                    reporter.Write("RESULT: FAILED");
                    return 1;
                }
                return ConnectAndMount(settings, reporter);
            }
            else if (action == "uninstall")
            {
                AutoStartHelper.RemoveScheduledTask(reporter);
                AutoStartHelper.DeleteStartupShortcut(reporter);
                reporter.Write("RESULT: OK");
                return 0;
            }
            reporter.Write("Unknown headless action: " + action);
            reporter.Write("RESULT: FAILED");
            return 1;
        }

        private static int ConnectAndMount(AppSettings settings, Reporter reporter)
        {
            if (String.IsNullOrEmpty(settings.DiskImagePath))
            {
                reporter.Write("No disk image is configured, nothing to connect.");
                reporter.Write("RESULT: FAILED");
                return 1;
            }

            int exitCode = PowerShellRunner.Run(BuildConnectScript(settings), reporter);
            if (exitCode != 0)
            {
                reporter.Write("RESULT: FAILED");
                return 1;
            }

            int mountExitCode = PowerShellRunner.Run(BuildMountScript(settings), reporter);
            if (mountExitCode != 0)
            {
                reporter.Write("RESULT: FAILED");
                return 1;
            }

            reporter.Write("RESULT: OK");
            return 0;
        }

        private static int DisconnectAndUnmount(AppSettings settings, Reporter reporter)
        {
            if (String.IsNullOrEmpty(settings.TargetIQN))
            {
                reporter.Write("No target is configured, nothing to disconnect.");
                return 1;
            }
            return PowerShellRunner.Run(BuildDisconnectScript(settings), reporter);
        }

        public static string BuildConnectScript(AppSettings settings)
        {
            string portalAddress = !String.IsNullOrEmpty(settings.PortalAddress) ? settings.PortalAddress : (settings.ListenAddress == "Any" ? "127.0.0.1" : settings.ListenAddress);
            string portal = PowerShellRunner.Quote(portalAddress);
            string iqn = PowerShellRunner.Quote(settings.TargetIQN);
            int port = settings.Port > 0 ? settings.Port : 3260;

            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            script.AppendLine("Import-Module iSCSI -ErrorAction Stop");
            script.AppendLine("$service = Get-Service -Name MSiSCSI -ErrorAction SilentlyContinue");
            script.AppendLine("if ($service -and $service.Status -ne 'Running') {");
            script.AppendLine("    try { Start-Service -Name MSiSCSI -ErrorAction Stop; Start-Sleep -Seconds 2; Write-Output 'Started the Microsoft iSCSI service.' } catch { Write-Output ('Could not start the Microsoft iSCSI service: ' + $_.Exception.Message) }");
            script.AppendLine("}");
            script.AppendLine("$portal = " + portal);
            script.AppendLine("$port = " + port);
            script.AppendLine("$iqn = " + iqn);
            script.AppendLine("$attempts = 6");
            script.AppendLine("$connected = $false");
            script.AppendLine("for ($attempt = 1; $attempt -le $attempts; $attempt++) {");
            script.AppendLine("    $sessions = @(Get-IscsiSession -ErrorAction SilentlyContinue | Where-Object { $_.TargetNodeAddress -eq $iqn })");
            script.AppendLine("    if ($sessions.Count -gt 0) {");
            script.AppendLine("        $session = $sessions[0]");
            script.AppendLine("        if (-not $session.IsPersistent) {");
            script.AppendLine("            try { Register-IscsiSession -SessionIdentifier $session.SessionIdentifier -ErrorAction Stop | Out-Null } catch { }");
            script.AppendLine("        }");
            script.AppendLine("        Write-Output ('Already connected to ' + $iqn)");
            script.AppendLine("        $connected = $true");
            script.AppendLine("        break");
            script.AppendLine("    }");
            script.AppendLine("    $portals = @(Get-IscsiTargetPortal -ErrorAction SilentlyContinue | Where-Object { $_.TargetPortalAddress -eq $portal -and $_.TargetPortalPortNumber -eq $port })");
            script.AppendLine("    if ($portals.Count -eq 0) {");
            script.AppendLine("        try { New-IscsiTargetPortal -TargetPortalAddress $portal -TargetPortalPortNumber $port -ErrorAction Stop | Out-Null } catch { }");
            script.AppendLine("    }");
            script.AppendLine("    $targets = @(Get-IscsiTarget -ErrorAction SilentlyContinue | Where-Object { $_.NodeAddress -eq $iqn })");
            script.AppendLine("    if ($targets.Count -gt 0) {");
            script.AppendLine("        try {");
            script.AppendLine("            Connect-IscsiTarget -NodeAddress $iqn -IsPersistent $true -IsMultipathEnabled $false -ErrorAction Stop | Out-Null");
            script.AppendLine("            Write-Output ('Connected to ' + $iqn + ' through ' + $portal + ':' + $port + ' (persistent).')");
            script.AppendLine("            $connected = $true");
            script.AppendLine("            break");
            script.AppendLine("        } catch {");
            script.AppendLine("            Write-Output ('Attempt ' + $attempt + ' failed: ' + $_.Exception.Message)");
            script.AppendLine("        }");
            script.AppendLine("    } else {");
            script.AppendLine("        Write-Output ('Target not found, attempt ' + $attempt + ' of ' + $attempts + '. Is the iSCSI Console server running?')");
            script.AppendLine("    }");
            script.AppendLine("    if ($attempt -lt $attempts) { Start-Sleep -Seconds 5 }");
            script.AppendLine("}");
            script.AppendLine("if (-not $connected) { Write-Output 'ERROR: unable to connect to the iSCSI target.'; exit 1 }");
            script.AppendLine("exit 0");
            return script.ToString();
        }

        public static string BuildDisconnectScript(AppSettings settings)
        {
            string iqn = PowerShellRunner.Quote(settings.TargetIQN);

            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Continue'");
            script.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            script.AppendLine("Import-Module iSCSI -ErrorAction Stop");
            script.AppendLine("Import-Module Storage -ErrorAction SilentlyContinue");
            script.AppendLine("$iqn = " + iqn);
            script.AppendLine("$expectedSize = " + (settings.DiskSize > 0 ? settings.DiskSize.ToString() : "0"));
            script.AppendLine("$sessions = @(Get-IscsiSession -ErrorAction SilentlyContinue | Where-Object { $_.TargetNodeAddress -eq $iqn })");
            script.AppendLine("if ($sessions.Count -eq 0) { Write-Output ('No session to ' + $iqn + ' is connected.'); exit 0 }");
            script.AppendLine("try { Update-HostStorageCache -ErrorAction SilentlyContinue } catch { }");
            script.AppendLine("$iscsiDisks = @(Get-Disk -ErrorAction SilentlyContinue | Where-Object { $_.BusType -eq 'iSCSI' })");
            script.AppendLine("$targetDisks = @()");
            script.AppendLine("if ($expectedSize -gt 0) {");
            script.AppendLine("    $targetDisks = @($iscsiDisks | Where-Object { [UInt64]$_.Size -eq [UInt64]$expectedSize })");
            script.AppendLine("} elseif ($iscsiDisks.Count -eq 1) {");
            script.AppendLine("    $targetDisks = @($iscsiDisks[0])");
            script.AppendLine("}");
            script.AppendLine("if ($expectedSize -gt 0 -and $targetDisks.Count -eq 0) { Write-Output ('The configured disk (size ' + $expectedSize + ') was not found, only the iSCSI session will be disconnected.') }");
            script.AppendLine("$busyDisks = @()");
            script.AppendLine("foreach ($disk in $targetDisks) {");
            script.AppendLine("    if ($disk.IsOffline) { Write-Output ('Disk ' + $disk.Number + ' is already offline.'); continue }");
            script.AppendLine("    try {");
            script.AppendLine("        Set-Disk -Number $disk.Number -IsOffline $true -ErrorAction Stop");
            script.AppendLine("        Write-Output ('Disk ' + $disk.Number + ' is now offline.')");
            script.AppendLine("    } catch {");
            script.AppendLine("        $busyDisks += ('disk ' + $disk.Number + ': ' + $_.Exception.Message)");
            script.AppendLine("        Write-Output ('Could not take disk ' + $disk.Number + ' offline: ' + $_.Exception.Message)");
            script.AppendLine("    }");
            script.AppendLine("}");
            script.AppendLine("$persistentSessions = @($sessions | Where-Object { $_.IsPersistent })");
            script.AppendLine("foreach ($session in $persistentSessions) {");
            script.AppendLine("    try { Unregister-IscsiSession -SessionIdentifier $session.SessionIdentifier -ErrorAction Stop; Write-Output 'Removed the persistent iSCSI login.' } catch { Write-Output ('Could not remove the persistent login: ' + $_.Exception.Message) }");
            script.AppendLine("}");
            script.AppendLine("$lastError = ''");
            script.AppendLine("for ($attempt = 1; $attempt -le 6; $attempt++) {");
            script.AppendLine("    try { Disconnect-IscsiTarget -NodeAddress $iqn -Confirm:$false -ErrorAction Stop | Out-Null; $lastError = '' } catch { $lastError = $_.Exception.Message }");
            script.AppendLine("    Start-Sleep -Seconds 2");
            script.AppendLine("    $remaining = @(Get-IscsiSession -ErrorAction SilentlyContinue | Where-Object { $_.TargetNodeAddress -eq $iqn })");
            script.AppendLine("    if ($remaining.Count -eq 0) { break }");
            script.AppendLine("    if ($lastError) { Write-Output ('Attempt ' + $attempt + ' failed: ' + $lastError) }");
            script.AppendLine("}");
            script.AppendLine("$remaining = @(Get-IscsiSession -ErrorAction SilentlyContinue | Where-Object { $_.TargetNodeAddress -eq $iqn })");
            script.AppendLine("if ($remaining.Count -eq 0) { Write-Output ('Disconnected from ' + $iqn + '.'); exit 0 }");
            script.AppendLine("foreach ($session in $persistentSessions) {");
            script.AppendLine("    $stillThere = @(Get-IscsiSession -ErrorAction SilentlyContinue | Where-Object { $_.SessionIdentifier -eq $session.SessionIdentifier })");
            script.AppendLine("    if ($stillThere.Count -gt 0) {");
            script.AppendLine("        try { Register-IscsiSession -SessionIdentifier $session.SessionIdentifier -ErrorAction Stop | Out-Null; Write-Output 'Restored the persistent iSCSI login.' } catch { }");
            script.AppendLine("    }");
            script.AppendLine("}");
            script.AppendLine("Write-Output 'ERROR: Windows still considers a device in the session in use.'");
            script.AppendLine("foreach ($busyDisk in $busyDisks) { Write-Output ('The volume is still mounted on ' + $busyDisk) }");
            script.AppendLine("$diskManagement = @(Get-Process mmc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'Gestione disco|Disk Management' })");
            script.AppendLine("if ($diskManagement.Count -gt 0) { Write-Output 'Disk Management is open and keeps every disk open. Close it and retry.' }");
            script.AppendLine("Write-Output 'Close programs using files on the disk (Explorer windows, backups, antivirus scans) and retry.'");
            script.AppendLine("exit 1");
            return script.ToString();
        }

        public static string BuildMountScript(AppSettings settings)
        {
            string fileSystem = PowerShellRunner.Quote(String.IsNullOrEmpty(settings.FileSystem) ? "NTFS" : settings.FileSystem);
            string preferredLetter = PowerShellRunner.Quote(settings.DriveLetter == null ? String.Empty : settings.DriveLetter.Trim().ToUpperInvariant());

            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Continue'");
            script.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            script.AppendLine("Import-Module Storage -ErrorAction Stop");
            script.AppendLine("$expectedSize = " + (settings.DiskSize > 0 ? settings.DiskSize.ToString() : "0"));
            script.AppendLine("$initialize = $" + (settings.InitializeIfRaw ? "true" : "false"));
            script.AppendLine("$fileSystem = " + fileSystem);
            script.AppendLine("$preferredLetter = " + preferredLetter);
            script.AppendLine("try { Update-HostStorageCache -ErrorAction SilentlyContinue } catch { }");
            script.AppendLine("$disks = @()");
            script.AppendLine("for ($attempt = 1; $attempt -le 10; $attempt++) {");
            script.AppendLine("    $disks = @(Get-Disk -ErrorAction SilentlyContinue | Where-Object { $_.BusType -eq 'iSCSI' })");
            script.AppendLine("    if ($disks.Count -gt 0) { break }");
            script.AppendLine("    Start-Sleep -Seconds 2");
            script.AppendLine("}");
            script.AppendLine("if ($disks.Count -eq 0) { Write-Output 'ERROR: no iSCSI disk detected.'; exit 1 }");
            script.AppendLine("$targetDisks = @($disks)");
            script.AppendLine("if ($expectedSize -gt 0) {");
            script.AppendLine("    $matching = @($disks | Where-Object { [UInt64]$_.Size -eq [UInt64]$expectedSize })");
            script.AppendLine("    if ($matching.Count -eq 0) { Write-Output ('ERROR: the configured iSCSI disk (size ' + $expectedSize + ') was not found.'); exit 1 }");
            script.AppendLine("    $targetDisks = $matching");
            script.AppendLine("}");
            script.AppendLine("foreach ($disk in $targetDisks) {");
            script.AppendLine("    if ($disk.IsOffline) {");
            script.AppendLine("        try { Set-Disk -Number $disk.Number -IsOffline $false -ErrorAction Stop; Write-Output ('Disk ' + $disk.Number + ' is now online.') } catch { Write-Output ('Could not bring disk ' + $disk.Number + ' online: ' + $_.Exception.Message) }");
            script.AppendLine("    }");
            script.AppendLine("    if ($disk.IsReadOnly) {");
            script.AppendLine("        try { Set-Disk -Number $disk.Number -IsReadOnly $false -ErrorAction Stop } catch { Write-Output ('Could not clear read-only on disk ' + $disk.Number + ': ' + $_.Exception.Message) }");
            script.AppendLine("    }");
            script.AppendLine("}");
            script.AppendLine("if ($initialize) {");
            script.AppendLine("    $rawDisks = @(Get-Disk | Where-Object { $_.BusType -eq 'iSCSI' -and $_.PartitionStyle -eq 'RAW' })");
            script.AppendLine("    foreach ($disk in $rawDisks) {");
            script.AppendLine("        if ($expectedSize -gt 0 -and [UInt64]$disk.Size -ne [UInt64]$expectedSize) {");
            script.AppendLine("            Write-Output ('Skipping disk ' + $disk.Number + ' (size mismatch).')");
            script.AppendLine("            continue");
            script.AppendLine("        }");
            script.AppendLine("        try {");
            script.AppendLine("            Initialize-Disk -Number $disk.Number -PartitionStyle MBR -ErrorAction Stop | Out-Null");
            script.AppendLine("            $partition = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter -ErrorAction Stop");
            script.AppendLine("            Format-Volume -DriveLetter $partition.DriveLetter -FileSystem $fileSystem -NewFileSystemLabel 'iSCSI Disk' -Confirm:$false -Force -ErrorAction Stop | Out-Null");
            script.AppendLine("            Write-Output ('Initialized and formatted disk ' + $disk.Number + ' as ' + $fileSystem + '.')");
            script.AppendLine("        } catch {");
            script.AppendLine("            Write-Output ('Could not initialize disk ' + $disk.Number + ': ' + $_.Exception.Message)");
            script.AppendLine("        }");
            script.AppendLine("    }");
            script.AppendLine("}");
            script.AppendLine("$usedLetters = @(Get-Partition | Where-Object { $_.DriveLetter } | ForEach-Object { [string]$_.DriveLetter })");
            script.AppendLine("foreach ($disk in $targetDisks) {");
            script.AppendLine("    foreach ($partition in @(Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue)) {");
            script.AppendLine("        if (-not $partition.DriveLetter -and $partition.Type -eq 'Basic') {");
            script.AppendLine("            $candidate = $preferredLetter");
            script.AppendLine("            if (-not $candidate -or ($usedLetters -contains $candidate)) {");
            script.AppendLine("                $candidate = @('D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z') | Where-Object { $usedLetters -notcontains $_ } | Select-Object -First 1");
            script.AppendLine("            }");
            script.AppendLine("            if ($candidate) {");
            script.AppendLine("                try {");
            script.AppendLine("                    Set-Partition -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -NewDriveLetter $candidate -ErrorAction Stop");
            script.AppendLine("                    $usedLetters += $candidate");
            script.AppendLine("                    Write-Output ('Assigned drive ' + $candidate + ' to disk ' + $disk.Number + '.')");
            script.AppendLine("                } catch {");
            script.AppendLine("                    Write-Output ('Could not assign a drive letter to disk ' + $disk.Number + ': ' + $_.Exception.Message)");
            script.AppendLine("                }");
            script.AppendLine("            }");
            script.AppendLine("        }");
            script.AppendLine("    }");
            script.AppendLine("}");
            script.AppendLine("exit 0");
            return script.ToString();
        }
    }

    public class Reporter
    {
        private StringBuilder m_builder = new StringBuilder();
        private string m_path;

        public Reporter(string path)
        {
            m_path = path;
        }

        public void Write(string line)
        {
            if (String.IsNullOrEmpty(line))
            {
                return;
            }
            string entry = DateTime.Now.ToString("HH:mm:ss") + " " + line;
            m_builder.AppendLine(entry);
            if (m_path != null)
            {
                try
                {
                    File.AppendAllText(m_path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                }
            }
        }

        public string Text
        {
            get
            {
                return m_builder.ToString();
            }
        }
    }

    public static class PowerShellRunner
    {
        public static int Run(string script, Reporter reporter)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), "iscsiconsole-" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.WriteAllText(scriptPath, script, Encoding.UTF8);
                string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
                ProcessStartInfo startInfo = new ProcessStartInfo("powershell.exe", arguments);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    WriteOutput(reporter, output);
                    WriteOutput(reporter, error);
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                reporter.Write("PowerShell failed: " + ex.Message);
                return 1;
            }
            finally
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch (IOException)
                {
                }
            }
        }

        private static void WriteOutput(Reporter reporter, string output)
        {
            if (String.IsNullOrEmpty(output))
            {
                return;
            }
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    reporter.Write(trimmed);
                }
            }
        }

        public static string Quote(string value)
        {
            if (value == null)
            {
                value = String.Empty;
            }
            return "'" + value.Replace("'", "''") + "'";
        }
    }

    public static class AutoStartHelper
    {
        private const string ShortcutFileName = "iSCSIConsole.lnk";

        public static string GetExecutablePath()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly != null && !String.IsNullOrEmpty(assembly.Location))
            {
                return assembly.Location;
            }
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        public static bool InstallScheduledTask(Reporter reporter)
        {
            string executablePath = GetExecutablePath();
            string script = String.Format(
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$action = New-ScheduledTaskAction -Execute {0} -Argument '/headless connect'\r\n" +
                "$trigger = New-ScheduledTaskTrigger -AtLogOn\r\n" +
                "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 10)\r\n" +
                "Register-ScheduledTask -TaskName {1} -Action $action -Trigger $trigger -Settings $settings -RunLevel Highest -Force | Out-Null\r\n" +
                "$task = Get-ScheduledTask -TaskName {1} -ErrorAction SilentlyContinue\r\n" +
                "if (-not $task) {{ throw 'Task registration failed.' }}\r\n" +
                "Write-Output 'Installed the logon task {2}.'\r\n" +
                "exit 0\r\n",
                PowerShellRunner.Quote(executablePath),
                PowerShellRunner.Quote(AutoConnectHelper.TaskName),
                AutoConnectHelper.TaskName);
            int exitCode = PowerShellRunner.Run(script, reporter);
            return exitCode == 0;
        }

        public static bool RemoveScheduledTask(Reporter reporter)
        {
            string script = String.Format(
                "$taskName = {0}\r\n" +
                "Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue\r\n" +
                "if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {{ Write-Output 'Could not remove the logon task.'; exit 1 }}\r\n" +
                "Write-Output 'Removed the logon task {1}.'\r\n" +
                "exit 0\r\n",
                PowerShellRunner.Quote(AutoConnectHelper.TaskName),
                AutoConnectHelper.TaskName);
            int exitCode = PowerShellRunner.Run(script, reporter);
            return exitCode == 0;
        }

        public static bool CreateStartupShortcut(out string error)
        {
            error = null;
            try
            {
                string startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (String.IsNullOrEmpty(startupDirectory))
                {
                    error = "The Startup folder could not be located.";
                    return false;
                }
                string linkPath = Path.Combine(startupDirectory, ShortcutFileName);
                string executablePath = GetExecutablePath();
                string script = String.Format(
                    "$ErrorActionPreference = 'Stop'\r\n" +
                    "$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut({0})\r\n" +
                    "$shortcut.TargetPath = {1}\r\n" +
                    "$shortcut.WorkingDirectory = {2}\r\n" +
                    "$shortcut.Arguments = ''\r\n" +
                    "$shortcut.Description = 'iSCSI Console - restore the NAS target and mount the disk'\r\n" +
                    "$shortcut.Save()\r\n" +
                    "Write-Output 'Created the startup shortcut.'\r\n" +
                    "exit 0\r\n",
                    PowerShellRunner.Quote(linkPath),
                    PowerShellRunner.Quote(executablePath),
                    PowerShellRunner.Quote(Path.GetDirectoryName(executablePath)));
                Reporter reporter = new Reporter(null);
                int exitCode = PowerShellRunner.Run(script, reporter);
                if (exitCode == 0 && File.Exists(linkPath))
                {
                    return true;
                }
                error = reporter.Text.Trim();
                if (error.Length == 0)
                {
                    error = "Could not create " + linkPath;
                }
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool StartupShortcutExists()
        {
            string startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (String.IsNullOrEmpty(startupDirectory))
            {
                return false;
            }
            return File.Exists(Path.Combine(startupDirectory, ShortcutFileName));
        }

        public static void DeleteStartupShortcut(Reporter reporter)
        {
            try
            {
                string startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (String.IsNullOrEmpty(startupDirectory))
                {
                    reporter.Write("The Startup folder could not be located.");
                    return;
                }
                string linkPath = Path.Combine(startupDirectory, ShortcutFileName);
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                    reporter.Write("Removed the startup shortcut.");
                }
            }
            catch (IOException ex)
            {
                reporter.Write("Could not remove the startup shortcut: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                reporter.Write("Could not remove the startup shortcut: " + ex.Message);
            }
            catch (ArgumentException ex)
            {
                reporter.Write("Could not remove the startup shortcut: " + ex.Message);
            }
        }
    }
}
