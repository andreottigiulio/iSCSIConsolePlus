iSCSIConsolePlus (unofficial fork)
==================================

This is a personal, unofficial fork of
[Tal Aloni's iSCSI Console](https://github.com/TalAloni/iSCSIConsole), built on
his iSCSI library. It is not affiliated with, endorsed by, or supported by the
original author.

It adds support for one specific loopback scenario: a VHD stored on an SMB share
of a NAS without iSCSI support, exported on 127.0.0.1 and consumed by Windows'
own initiator on the same PC. The additions are:

* Persistent settings (portable `ISCSIConsole.settings.ini`, fallback to
  `%APPDATA%\iSCSIConsole`) and a command line (`/vhd`, `/iqn`, `/listen`,
  `/portal`, `/port`, `/setup`, `/save`, `/headless ...`).
* Two tabs, Target (Server) and Windows Initiator (Client), with two independent
  auto-starts: a Startup shortcut for the target and a highest-privilege logon
  task for the initiator.
* A short-lived elevated helper for MSiSCSI and disk operations, while the
  process serving the VHD stays non-elevated (an elevated token cannot see the
  user's SMB session).
* Connect, mount, disconnect/unmount with size-matched disk selection, and a
  button that opens `iscsicpl.exe`.
* A notification-area icon with an optional "minimize to tray on close" mode
  (persisted as `MinimizeToTray`); the tray menu can reopen or close the app.
* A "Dynamically expanding" checkbox in Create Virtual Disk.

Building from source
--------------------
No Visual Studio and no system-wide installs are required. All you need is
Windows 10/11 x64 and an internet connection on the first run.

1. Run `build.cmd` (or `powershell -NoProfile -File build.ps1`).
2. The script downloads the .NET SDK and ILMerge into `.tools\`, restores the
   NuGet packages into `.tools\nuget-packages`, builds the target framework and
   merges the assemblies into `dist\ISCSIConsole.exe`.
3. Unless `-NoPackage` is passed, it also writes
   `dist\ISCSIConsole_<version>_<tfm>.zip` with a blank settings file and prints
   the SHA256 of the archive.

Options: `-TargetFramework net20|net40|net472` (default `net472`, the only one
tested), `-Clean`, `-SdkVersion`, `-IlMergeVersion`, `-NoPackage`. Nothing is
installed outside the repository and PATH is not modified; deleting `.tools\`
removes the toolchain completely.

Requirements: Windows, .NET Framework 4.7.2 and PowerShell 5.1 with the iSCSI
and Storage modules. Only the net472 target is built and tested here. The
implementation was made with AI assistance; design and testing were done by the
repository owner.

License: LGPL v3, same as the original project (see `License.txt`). Original
copyright notices are kept. If you redistribute binaries, provide the
corresponding source as the license requires.

The original project remains the reference for the target-only demo; this fork
exists because these additions were out of scope there.

About iSCSI Console:
====================
iSCSI Console is a Free, Open Source, User-Mode iSCSI Target Server written in C#.  
iSCSI Console is cross-platform, portable and requires no installation.  
iSCSI Console can serve physical and virtual disks to multiple clients.  

About the iSCSI library:
========================
The iSCSI library utilized by iSCSI Console was designed to give developers an easy way to serve block storage via iSCSI.  
Any storage object you wish to share needs to implement the abstract Disk class, and the library will take care of the rest.  
The library was written with extensibility in mind, and was designed to fit multitude of projects.  
iSCSI Console is merely a demo project that exposes some of the capabilities of this library.  

A NuGet package of the library [is available](https://www.nuget.org/packages/ISCSI/).

Notes:
------
In addition to a full fledged iSCSI Target server implementation, the iSCSI library also contain a very basic iSCSI initiator implementation.

What this program can do:
===================================
1. Serve virtual disks (VHD / VMDK / IMG).
2. Serve physical disks.
3. Serve basic volumes as disks.
4. Serve dynamic volumes as disks.
5. Create VHDs.
6. Can run under Windows PE using Mono.
7. Can run under Linux / OSX using Mono (use the release targeting .NET Framework 4.7.2)

![iSCSI Console UI](ISCSIConsole_UI.png)

Contact:
========
If you have any question, feel free to contact me.  
Tal Aloni <tal.aloni.il@gmail.com>
